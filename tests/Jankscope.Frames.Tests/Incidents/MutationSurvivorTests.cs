using Jankscope.Core;
using Jankscope.Frames.Capture;
using Jankscope.Frames.Incidents;
using Jankscope.Frames.PresentMon;
using Jankscope.Frames.Ring;
using Jankscope.Frames.Tests.PresentMon;

namespace Jankscope.Frames.Tests.Incidents;

/// <summary>
/// Rules that the independent Stage 1b review's mutation campaign found no failing test for (finding 17): each test below turns red when its rule is
/// broken, and the commit message names the mutation that was used to prove it.
/// </summary>
public class MutationSurvivorTests
{
    private static FrameSample Sample(long index, double timestampMs, DataProvenance provenance) =>
        new(index, timestampMs, 16.667, PresentMode: "Hardware: Independent Flip", Application: "game.exe", Provenance: provenance);

    // ---- S2: the weakest provenance wins when a series is continued (HC-9) ----

    [Fact]
    public void A_measured_series_continued_with_simulated_frames_is_simulated()
    {
        var measured = FrameSeries.FromSamples([Sample(0, 10, DataProvenance.Measured), Sample(1, 20, DataProvenance.Measured)]);

        var continued = measured.ContinueWith([Sample(1, 20, DataProvenance.Measured), Sample(2, 30, DataProvenance.Simulated)], endMs: 100);

        Assert.Equal(3, continued.Count);
        Assert.Equal(DataProvenance.Simulated, continued.Provenance);
    }

    [Fact]
    public void A_simulated_series_continued_with_measured_frames_stays_simulated()
    {
        var simulated = FrameSeries.FromSamples([Sample(0, 10, DataProvenance.Simulated), Sample(1, 20, DataProvenance.Simulated)]);

        var continued = simulated.ContinueWith([Sample(1, 20, DataProvenance.Simulated), Sample(2, 30, DataProvenance.Measured)], endMs: 100);

        Assert.Equal(3, continued.Count);
        Assert.Equal(DataProvenance.Simulated, continued.Provenance);
    }

    [Fact]
    public void One_simulated_frame_among_measured_ones_makes_the_whole_series_simulated()
    {
        var series = FrameSeries.FromSamples(
            [Sample(0, 10, DataProvenance.Measured), Sample(1, 20, DataProvenance.Simulated), Sample(2, 30, DataProvenance.Measured)]);

        Assert.Equal(DataProvenance.Simulated, series.Provenance);
    }

    // ---- C2: a new capture run resets the clock map, even when its timestamps look continuous ----

    [Fact]
    public async Task A_new_capture_run_starts_a_new_clock_epoch_even_when_its_first_timestamp_is_only_slightly_earlier()
    {
        // Review of Stage 1b, E2C: a restart inside the first moments of the old clock was not detected by the timestamp rule alone (it needs a drop of more than
        // 5 ms). The run number is the second signal. Here the new run's first frame is 1 ms behind the old run's last, so only the run change can tell.
        var time = new ManualTimeProvider();
        var source = new ScriptedFrameSource();
        var clock = new CaptureClockMap(time.TimestampFrequency);
        var session = new CaptureSession(source, new RingBuffer<FrameSample>(256), clock, time);
        session.Start();
        source.Push(1, Sample(0, 100.0, DataProvenance.Measured));
        source.Push(1, Sample(1, 102.0, DataProvenance.Measured));
        source.Push(1, Sample(2, 104.0, DataProvenance.Measured));
        source.Push(2, Sample(0, 103.0, DataProvenance.Measured)); // PresentMon was started again: its clock begins anew
        await session.StopAsync();

        var estimate = clock.Estimate(time.GetTimestamp())!.Value;

        Assert.Equal(2, session.Run);
        Assert.Equal(1, estimate.Epoch);
        Assert.Equal(1, estimate.Observations);
        Assert.Equal(4, session.FramesReceived);
    }

    // ---- Q6: the pre-roll covers the clock uncertainty ----

    [Fact]
    public void The_pre_roll_of_a_mark_reaches_back_the_full_requested_time_even_when_the_clock_time_is_uncertain()
    {
        // Frames reach this program 20 ms or 220 ms after they were presented, alternating, so the mark time is uncertain by about 200 ms. The mark time
        // sits early by that much at worst, so the pre-roll window starts that much further back; otherwise the requested pre-roll is silently short.
        var time = new ManualTimeProvider();
        var ring = new RingBuffer<FrameSample>(65_536);
        var clock = new CaptureClockMap(time.TimestampFrequency);
        var recorder = new IncidentRecorder(ring, clock, time, new IncidentRecorderOptions(PreRollSeconds: 30.0, PostRollSeconds: 30.0));
        const double frame = 1000.0 / 60.0;
        var lastPresentMs = 0.0;
        for (var i = 0; i < 2400; i++)
        {
            lastPresentMs = (i + 1) * frame;
            var delay = i % 2 == 0 ? 20.0 : 220.0;
            clock.Observe((long)((lastPresentMs + delay) * time.TimestampFrequency / 1000.0), lastPresentMs);
            ring.Write(Sample(i, lastPresentMs, DataProvenance.Measured));
        }

        time.Advance(TimeSpan.FromMilliseconds(lastPresentMs + 20.0)); // "now" is when the newest frame, delivered with the smallest delay, arrived
        var mark = recorder.Mark(MarkKind.Stutter)!;
        var captureTime = mark.CaptureTime!.Value;

        Assert.InRange(captureTime.UncertaintyMs, 150.0, 250.0); // the precondition: a clearly uncertain time
        var firstPreRollFrame = mark.PreRoll.TimestampsMs[0];
        Assert.True(firstPreRollFrame <= captureTime.Ms - 30_000.0 + 2 * frame,
            $"the pre-roll starts at {firstPreRollFrame} ms but should reach back to {captureTime.Ms - 30_000.0} ms");
    }
}
