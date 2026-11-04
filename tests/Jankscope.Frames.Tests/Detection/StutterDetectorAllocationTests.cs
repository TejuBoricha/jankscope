using Jankscope.Frames.Detection;
using Jankscope.Frames.Tests.Ring;
using Xunit.Abstractions;

namespace Jankscope.Frames.Tests.Detection;

public class StutterDetectorAllocationTests(ITestOutputHelper output)
{
    [Fact]
    public void The_streaming_path_allocates_nothing_per_frame_after_warm_up()
    {
        // 500 fps: the 1 s window holds ~500 frames, the largest the structure has to grow to; warm-up reaches it.
        var detector = new StutterDetector();
        var random = new Random(11);
        var timestamp = 0.0;

        FrameSample Next()
        {
            var frameTime = 1.8 + (random.NextDouble() * 0.4);
            timestamp += frameTime;
            return new FrameSample(0, timestamp, frameTime);
        }

        for (var i = 0; i < 6_000; i++)
        {
            detector.Push(Next());
        }

        // Pre-generate so the measured loop contains only detector work. The attempts continue the same series, so each is a
        // fresh stretch of frames through the same warmed-up detector.
        const int attempts = 5;
        var batches = new FrameSample[attempts][];
        for (var a = 0; a < attempts; a++)
        {
            batches[a] = new FrameSample[200_000];
            for (var i = 0; i < batches[a].Length; i++)
            {
                batches[a][i] = Next();
            }
        }

        var attempt = 0;
        var statusSum = 0L;
        var allocated = AllocationProbe.MinimumBytes(() =>
        {
            var batch = batches[attempt++];
            for (var i = 0; i < batch.Length; i++)
            {
                statusSum += (int)detector.Push(batch[i]).Status;
            }
        }, out var deltas, attempts);

        output.WriteLine("allocated per attempt: " + string.Join(", ", deltas));
        Assert.Equal(0L, allocated);
        Assert.Equal(0L, statusSum); // all NoStutter (0): nothing was flagged, so no event was stored either
    }

    [Fact]
    public void Rejected_and_insufficient_history_frames_also_allocate_nothing()
    {
        var detector = new StutterDetector();
        for (var i = 0; i < 2_000; i++)
        {
            detector.Push(new FrameSample(i, i * 4.0, 4.0));
        }

        var allocated = AllocationProbe.MinimumBytes(() =>
        {
            for (var i = 0; i < 100_000; i++)
            {
                detector.Push(new FrameSample(i, 8_000.0 + (i * 4.0), double.NaN));
                detector.Push(new FrameSample(i, 8_000.0 + (i * 4.0), -1.0));
            }
        }, out var deltas);

        output.WriteLine("allocated per attempt: " + string.Join(", ", deltas));
        Assert.Equal(0L, allocated);
    }

    [Fact]
    public void A_long_clean_run_is_not_quadratic_in_the_window_size()
    {
        // 1 kHz capture: ~1000 frames in the window. A per-frame sort would be ~1000 log 1000 per frame (10^7 operations per
        // 1000 frames); a heap-based median is ~log 1000. This is a loose guard against accidental O(w) per frame work, not a benchmark.
        var detector = new StutterDetector();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 1_000_000; i++)
        {
            var frameTime = 1.0 + ((i * 2654435761L % 1000) / 10_000.0);
            detector.Push(new FrameSample(i, i * 1.05, frameTime));
        }

        watch.Stop();
        output.WriteLine($"1 M frames through a ~1000-frame window: {watch.Elapsed.TotalMilliseconds:F0} ms");
        Assert.Equal(1_000_000L, detector.GetResult().FramesSeen);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"1 M frames took {watch.Elapsed}");
    }
}
