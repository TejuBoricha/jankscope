using Jankscope.App.Services;
using Jankscope.Frames;
using Jankscope.Frames.Capture;
using Jankscope.Frames.Incidents;
using Jankscope.Frames.Ring;

namespace Jankscope.App.Tests;

/// <summary>
/// The built-in demo writes twenty seconds of history in an instant. The capture clock map must not read that as seconds of delivery jitter it never had
/// (the real-app run of 2026-11-12 showed "frame-delivery jitter of about 8313 ms" on the demo and an equally wide band on the timeline).
/// </summary>
public class SimulatedFrameSourceTests
{
    private static async Task<(CaptureSession Session, CaptureClockMap Clock)> StartAsync(int framesToWaitFor)
    {
        var clock = new CaptureClockMap(TimeProvider.System.TimestampFrequency);
        var session = new CaptureSession(new SimulatedFrameSource(), new RingBuffer<FrameSample>(8192), clock);
        session.Start();
        await TestData.WaitUntil(() => session.FramesReceived >= framesToWaitFor);
        return (session, clock);
    }

    [Fact]
    public async Task The_demo_history_does_not_read_as_seconds_of_delivery_jitter()
    {
        var (session, clock) = await StartAsync(framesToWaitFor: 2400);
        await using var owner = session;

        var estimate = clock.Estimate(TimeProvider.System.GetTimestamp());

        Assert.NotNull(estimate);
        Assert.True(estimate.Value.UncertaintyIsMeaningful);
        Assert.Equal(0.0, estimate.Value.UncertaintyMs, 2); // simulated delivery is steady by construction; the data itself stays labelled Simulated
    }

    [Fact]
    public async Task A_mark_made_now_lands_beside_the_newest_frame_and_the_live_part_never_runs_ahead_of_the_clock()
    {
        var (session, clock) = await StartAsync(framesToWaitFor: 2400 + 240); // two seconds of live frames after the history
        await using var owner = session;

        var estimate = clock.Estimate(TimeProvider.System.GetTimestamp());
        var newestMs = session.History.Snapshot().Max(s => s.TimestampMs);

        Assert.NotNull(estimate);
        var gap = estimate.Value.Ms - newestMs; // how far "now" is past the newest frame
        Assert.True(gap >= -1.0, $"a frame was written before its time: the mark is {gap} ms before the newest frame");
        Assert.True(gap <= 750.0, $"the live frames fell {gap} ms behind the clock");
    }
}
