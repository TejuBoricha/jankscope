using Jankscope.Core;

namespace Jankscope.Harness.Tests;

public class FrameLoopTests
{
    private const long Ms = 10_000;

    private static (FrameSchedule Schedule, FakeClock Clock, FramePacer Pacer) Setup(int frames = 120, int hitches = 1, double extra = 25, double fps = 144, double jitter = 0)
    {
        var clock = new FakeClock { Now = 777_000 }; // an arbitrary non-zero origin proves the records are origin-relative
        var schedule = FrameSchedule.Create(SyntheticWorkload.Generate(3, fps, frames, hitches, extra, jitter), clock.Frequency);
        return (schedule, clock, new FramePacer(clock, 2 * Ms));
    }

    [Fact]
    public void Records_are_relative_to_the_origin_and_land_on_the_schedule()
    {
        var (schedule, clock, pacer) = Setup();
        var presenter = new FakePresenter(clock, presentCostTicks: 500);

        var result = FrameLoop.Run(schedule, presenter, pacer, clock);

        Assert.Equal(schedule.Count, result.Records.Count);
        Assert.Equal(777_000, result.OriginTimestamp);
        for (var i = 0; i < schedule.Count; i++)
        {
            var r = result.Records[i];
            Assert.Equal(i, r.Index);
            Assert.Equal(schedule.TargetTicks[i], r.ScheduledTicks);
            Assert.Equal(500, r.PresentCallTicks);
            // Present starts at the first spin step at or after the target and takes 500 ticks: return = target + [500, 510).
            Assert.InRange(r.PresentTicks - r.ScheduledTicks, 500, 509);
            Assert.Equal(schedule.InjectedExtraMs[i], r.InjectedExtraMs);
        }
    }

    [Fact]
    public void Each_frame_is_prepared_before_the_wait_and_presented_at_or_after_its_target()
    {
        var (schedule, clock, pacer) = Setup(frames: 90, hitches: 0);
        var presenter = new FakePresenter(clock);

        FrameLoop.Run(schedule, presenter, pacer, clock);

        for (var i = 0; i < schedule.Count; i++)
        {
            var prepare = presenter.Calls.Single(c => c.Call == "Prepare" && c.Frame == i);
            var present = presenter.Calls.Single(c => c.Call == "Present" && c.Frame == i);
            Assert.True(prepare.At <= present.At, $"frame {i}: prepared at {prepare.At}, presented at {present.At}");
            Assert.True(present.At >= 777_000 + schedule.TargetTicks[i], $"frame {i} presented before its target");
        }

        // Strict alternation Prepare(i), Present(i), Prepare(i+1) ...
        Assert.Equal(Enumerable.Range(0, schedule.Count).SelectMany(i => new[] { ("Prepare", i), ("Present", i) }).ToList(),
            presenter.Calls.Select(c => (c.Call, c.Frame)).ToList());
    }

    [Fact]
    public void The_hitch_flag_reaches_the_presenter_for_exactly_the_injected_frames()
    {
        var (schedule, clock, pacer) = Setup(frames: 240, hitches: 3);
        var presenter = new FakePresenter(clock);

        FrameLoop.Run(schedule, presenter, pacer, clock);

        Assert.Equal(schedule.Run.Hitches.Select(h => h.FrameIndex).Order(), presenter.HitchFlags.Order());
    }

    [Fact]
    public void The_after_present_callback_runs_once_per_frame_and_after_the_present()
    {
        var (schedule, clock, pacer) = Setup(frames: 80, hitches: 0);
        var presenter = new FakePresenter(clock);
        var callbacks = new List<int>();

        FrameLoop.Run(schedule, presenter, pacer, clock, afterPresent: () => callbacks.Add(presenter.Calls.Count(c => c.Call == "Present")));

        Assert.Equal(Enumerable.Range(1, schedule.Count), callbacks);
    }

    [Fact]
    public void Occluded_presents_are_counted_and_do_not_change_the_timing()
    {
        var (schedule, clock, pacer) = Setup(frames: 100, hitches: 0);
        var presenter = new FakePresenter(clock) { Outcome = i => i % 10 == 0 ? PresentOutcome.Occluded : PresentOutcome.Presented };

        var result = FrameLoop.Run(schedule, presenter, pacer, clock);

        Assert.Equal(10, result.OccludedPresents);
        Assert.All(result.Records, r => Assert.InRange(r.PresentTicks - r.ScheduledTicks, 500, 509));
    }

    [Fact]
    public void A_stall_makes_following_frames_late_but_the_absolute_schedule_is_kept()
    {
        // Frame 20's Present takes 30 ms (about 4 frames at 144 fps). The loop must not stretch or shift the schedule: the next frames
        // go out back to back until the clock catches up, and later frames are on time again. That is what a slow Present looks like.
        var (schedule, clock, pacer) = Setup(frames: 90, hitches: 0);
        var presenter = new FakePresenter(clock) { ExtraCostTicks = i => i == 20 ? 30 * Ms : 0 };

        var result = FrameLoop.Run(schedule, presenter, pacer, clock);

        Assert.True(pacer.LateArrivals >= 3, $"late arrivals: {pacer.LateArrivals}");
        for (var i = 0; i < schedule.Count; i++)
        {
            Assert.Equal(schedule.TargetTicks[i], result.Records[i].ScheduledTicks);
        }

        Assert.InRange(result.Records[60].PresentTicks - result.Records[60].ScheduledTicks, 500, 509); // recovered
        Assert.True(result.Records[22].PresentTicks - result.Records[21].PresentTicks < 5 * Ms, "caught-up frames are presented back to back");
    }

    [Fact]
    public void Cancelling_stops_the_loop_before_the_next_frame_and_throws()
    {
        var (schedule, clock, pacer) = Setup(frames: 90, hitches: 0);
        var presenter = new FakePresenter(clock);
        using var cancellation = new CancellationTokenSource();

        Assert.Throws<OperationCanceledException>(() =>
            FrameLoop.Run(schedule, presenter, pacer, clock, afterPresent: () =>
            {
                if (presenter.Calls.Count(c => c.Call == "Present") == 6)
                {
                    cancellation.Cancel();
                }
            }, cancellationToken: cancellation.Token));

        Assert.Equal(6, presenter.Calls.Count(c => c.Call == "Present")); // frames 0-5 were presented; frame 6 was never prepared
        Assert.Equal(6, presenter.Calls.Count(c => c.Call == "Prepare"));
    }

    [Fact]
    public void A_token_cancelled_before_the_start_presents_nothing()
    {
        var (schedule, clock, pacer) = Setup(frames: 90, hitches: 0);
        var presenter = new FakePresenter(clock);

        Assert.Throws<OperationCanceledException>(() => FrameLoop.Run(schedule, presenter, pacer, clock, cancellationToken: new CancellationToken(canceled: true)));

        Assert.Empty(presenter.Calls);
    }

    [Fact]
    public void End_timestamp_follows_the_last_frame()
    {
        var (schedule, clock, pacer) = Setup(frames: 70, hitches: 0);

        var result = FrameLoop.Run(schedule, new FakePresenter(clock), pacer, clock);

        Assert.True(result.EndTimestamp >= result.OriginTimestamp + result.Records[^1].PresentTicks);
    }
}
