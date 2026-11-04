using Jankscope.Core;
using Jankscope.Frames.Detection;
using Jankscope.Frames.Ring;
using Jankscope.Frames.Tests.Detection;

namespace Jankscope.Frames.Tests.EndToEnd;

/// <summary>
/// The Stage 1a exit criterion on the pure path (no Direct3D): synthetic workload with injected hitches -> frame samples ->
/// ring buffer -> detector -> ground-truth matcher. Data is <see cref="DataProvenance.Simulated"/>.
/// </summary>
public class SyntheticEndToEndTests
{
    private const int RunFrames = 1_500;
    private const int HitchesPerRun = 8;
    private const int CleanRunFrames = 3_000;

    private static readonly int[] Seeds = Enumerable.Range(1, 12).ToArray();
    private static readonly double[] Rates = [60.0, 144.0, 240.0];
    private static readonly double[] Extras = [15.0, 25.0, 50.0];
    private static readonly double[] Jitters = [0.0, 0.3];

    public static TheoryData<int, double, double, double> HitchGrid
    {
        get
        {
            var data = new TheoryData<int, double, double, double>();
            foreach (var seed in Seeds)
            {
                foreach (var fps in Rates)
                {
                    foreach (var extra in Extras)
                    {
                        foreach (var jitter in Jitters)
                        {
                            data.Add(seed, fps, extra, jitter);
                        }
                    }
                }
            }

            return data;
        }
    }

    public static TheoryData<int, double, double> CleanGrid
    {
        get
        {
            var data = new TheoryData<int, double, double>();
            foreach (var seed in Seeds)
            {
                foreach (var fps in Rates)
                {
                    foreach (var jitter in Jitters)
                    {
                        data.Add(seed, fps, jitter);
                    }
                }
            }

            return data;
        }
    }

    /// <summary>Frame samples as a capture would deliver them: cumulative timestamps, the frame time of each frame, 0-based index.</summary>
    internal static FrameSample[] ToSamples(SyntheticRun run) => Series.FromFrameTimes(run.FrameTimesMs);

    /// <summary>Through the ring buffer, the way the engine will hold frames before an incident is cut.</summary>
    private static FrameSample[] ThroughRing(FrameSample[] samples)
    {
        var ring = new RingBuffer<FrameSample>(samples.Length);
        foreach (var s in samples)
        {
            ring.Write(s);
        }

        return ring.Snapshot();
    }

    /// <summary>
    /// By hand: a hitch of E ms on a frame of B ms is detectable iff (B + E) &gt;= 2 B and E &gt;= 4 ms. Of the grid only
    /// 60 fps with +15 ms fails (16.67 + 15 = 31.67 ms = 1.9x); 144 fps needs +6.94 ms and 240 fps +4.17 ms, so every other
    /// combination is detectable by definition. Written out here, not computed by the matcher, so the two stay independent.
    /// </summary>
    private static bool ExpectedDetectable(double fps, double extraMs) => !(fps == 60.0 && extraMs == 15.0);

    [Theory]
    [MemberData(nameof(HitchGrid))]
    public void Injected_hitches_are_found_within_one_frame_with_no_false_positives(int seed, double fps, double extraMs, double jitterMs)
    {
        var run = SyntheticWorkload.Generate(seed, fps, RunFrames, HitchesPerRun, extraMs, jitterMs);
        Assert.Equal(DataProvenance.Simulated, run.Provenance);
        Assert.Equal(HitchesPerRun, run.Hitches.Count);

        var result = StutterDetector.Analyze(ThroughRing(ToSamples(run)));
        var report = GroundTruthMatcher.Match(run.Hitches, result.Events, toleranceFrames: 1, StutterOptions.Default, 1000.0 / fps);

        var context = $"seed={seed} fps={fps} extra={extraMs} jitter={jitterMs}";
        Assert.True(report.FalsePositiveCount == 0, $"{context}: false positives at frames {string.Join(",", report.FalsePositives.Select(e => e.FrameIndex))}");

        foreach (var m in report.Hitches)
        {
            Assert.Equal(ExpectedDetectable(fps, extraMs), m.DetectableByDefinition);
            if (ExpectedDetectable(fps, extraMs))
            {
                Assert.True(m.Outcome == HitchOutcome.Detected, $"{context}: hitch at frame {m.Hitch.FrameIndex} was {m.Outcome}");
                Assert.True(Math.Abs(m.FrameOffset!.Value) <= 1, $"{context}: offset {m.FrameOffset} at frame {m.Hitch.FrameIndex} exceeds +/-1 frame");
                Assert.Equal(0, m.FrameOffset); // the pure path has no timing noise: the event sits exactly on the injected frame
            }
            else
            {
                Assert.True(m.Outcome == HitchOutcome.NotDetectableByDefinition, $"{context}: sub-threshold hitch at frame {m.Hitch.FrameIndex} was {m.Outcome}");
            }
        }

        Assert.Equal(0, report.MissedCount);
        if (ExpectedDetectable(fps, extraMs))
        {
            Assert.Equal(HitchesPerRun, report.DetectedCount);
            Assert.Equal(1.0, report.Precision);
            Assert.Equal(1.0, report.Recall);
            Assert.Equal(HitchesPerRun, result.Events.Count);
        }
        else
        {
            Assert.Equal(HitchesPerRun, report.NotDetectableCount);
            Assert.Empty(result.Events); // sub-threshold: nothing at all is flagged
            Assert.Null(report.Recall);
        }

        // Honest labelling: this run used the default, uncalibrated constants.
        Assert.False(result.Calibrated);
        Assert.Equal(0L, result.RejectedFrames);
        Assert.Equal(10L, result.InsufficientHistoryFrames);
    }

    [Theory]
    [MemberData(nameof(CleanGrid))]
    public void Clean_runs_have_zero_false_positives(int seed, double fps, double jitterMs)
    {
        var run = SyntheticWorkload.Generate(seed, fps, CleanRunFrames, hitchCount: 0, hitchExtraMs: 0.0, jitterMs);
        Assert.Empty(run.Hitches);

        var result = StutterDetector.Analyze(ToSamples(run));
        var report = GroundTruthMatcher.Match(run.Hitches, result.Events, 1, StutterOptions.Default, 1000.0 / fps);

        Assert.Empty(result.Events);
        Assert.Equal(0, report.FalsePositiveCount);
        Assert.Empty(result.Clusters);
        Assert.Equal(CleanRunFrames - 10L, result.FramesEvaluated);
    }

    [Fact]
    public void Streaming_one_frame_at_a_time_gives_the_same_events_as_the_batch_call()
    {
        var run = SyntheticWorkload.Generate(5, 144.0, RunFrames, HitchesPerRun, 25.0, 0.3);
        var samples = ToSamples(run);

        var detector = new StutterDetector();
        foreach (var s in samples)
        {
            detector.Push(s);
        }

        Assert.Equal(StutterDetector.Analyze(samples).Events, detector.GetResult().Events);
        Assert.Equal(HitchesPerRun, detector.GetResult().Events.Count);
    }

    [Fact]
    public void A_pre_roll_cut_from_the_ring_still_finds_the_hitches_inside_it()
    {
        // The ring only keeps the last 600 frames; the detector runs on that cut. Hitches inside the cut (after its own
        // 10-frame warm-up) are found at their original frame indices; the others were never in the data.
        var run = SyntheticWorkload.Generate(9, 240.0, RunFrames, HitchesPerRun, 25.0, 0.3);
        var ring = new RingBuffer<FrameSample>(600);
        foreach (var s in ToSamples(run))
        {
            ring.Write(s);
        }

        var cut = ring.Snapshot();
        Assert.Equal(600, cut.Length);
        Assert.Equal(RunFrames - 600L, cut[0].Index);

        var result = StutterDetector.Analyze(cut);
        var inside = run.Hitches.Where(h => h.FrameIndex >= cut[0].Index + 10).ToArray();
        Assert.NotEmpty(inside);

        var report = GroundTruthMatcher.Match(inside, result.Events, 1, StutterOptions.Default, 1000.0 / 240.0);

        Assert.All(report.Hitches, m => Assert.Equal(HitchOutcome.Detected, m.Outcome));
        Assert.Equal(0, report.FalsePositiveCount);
    }

    [Fact]
    public void A_time_window_cut_from_the_ring_keeps_the_frames_around_a_mark()
    {
        var run = SyntheticWorkload.Generate(2, 144.0, RunFrames, HitchesPerRun, 50.0, 0.0);
        var samples = ToSamples(run);
        var ring = new RingBuffer<FrameSample>(RunFrames);
        foreach (var s in samples)
        {
            ring.Write(s);
        }

        var hitch = run.Hitches[^1];
        var markMs = samples[hitch.FrameIndex].TimestampMs + 100.0; // the user presses the key ~100 ms after the hitch
        var window = ring.SnapshotWindow(markMs, windowMs: 2_000.0);

        Assert.True(window[^1].TimestampMs <= markMs);
        Assert.True(window[0].TimestampMs >= markMs - 2_000.0);
        Assert.Contains(window, s => s.Index == hitch.FrameIndex);

        var result = StutterDetector.Analyze(window);
        Assert.Contains(result.Events, e => e.FrameIndex == hitch.FrameIndex);
    }
}
