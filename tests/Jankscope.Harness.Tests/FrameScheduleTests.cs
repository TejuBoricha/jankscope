using Jankscope.Core;

namespace Jankscope.Harness.Tests;

public class FrameScheduleTests
{
    private const long Qpc = 10_000_000;

    private static FrameSchedule Make(int seed = 7, double fps = 144, int frames = 300, int hitches = 3, double extra = 25, double jitter = 0.3) =>
        FrameSchedule.Create(SyntheticWorkload.Generate(seed, fps, frames, hitches, extra, jitter), Qpc);

    [Fact]
    public void Targets_are_the_cumulative_sum_of_the_ground_truth_frame_times()
    {
        var schedule = Make();
        var running = 0.0;
        for (var i = 0; i < schedule.Count; i++)
        {
            running += schedule.Run.FrameTimesMs[i];
            Assert.Equal(running, schedule.TargetMs[i], 9);
            Assert.Equal((long)Math.Round(running * Qpc / 1000.0, MidpointRounding.AwayFromZero), schedule.TargetTicks[i]);
        }
    }

    [Fact]
    public void Target_differences_reproduce_each_ground_truth_frame_time_within_one_tick()
    {
        var schedule = Make();
        var previous = 0L;
        for (var i = 0; i < schedule.Count; i++)
        {
            var intervalMs = QpcTime.ToMs(schedule.TargetTicks[i] - previous, Qpc);
            Assert.InRange(Math.Abs(intervalMs - schedule.Run.FrameTimesMs[i]), 0, 2.0 * 1000 / Qpc);
            previous = schedule.TargetTicks[i];
        }
    }

    [Fact]
    public void Targets_strictly_increase()
    {
        var schedule = Make(jitter: 0.5, fps: 240);
        for (var i = 1; i < schedule.Count; i++)
        {
            Assert.True(schedule.TargetTicks[i] > schedule.TargetTicks[i - 1], $"frame {i}");
        }
    }

    [Fact]
    public void The_same_inputs_give_the_same_schedule_and_another_seed_gives_another()
    {
        var a = Make(seed: 11);
        var b = Make(seed: 11);
        var c = Make(seed: 12);
        Assert.Equal(a.TargetTicks, b.TargetTicks);
        Assert.Equal(a.Run.Hitches, b.Run.Hitches);
        Assert.NotEqual(a.TargetTicks, c.TargetTicks);
    }

    [Fact]
    public void Hitch_frames_are_flagged_and_carry_their_injected_extra_in_the_interval()
    {
        var schedule = Make(extra: 40, jitter: 0);
        Assert.Equal(3, schedule.Run.Hitches.Count);
        var flagged = Enumerable.Range(0, schedule.Count).Where(schedule.IsHitch).ToList();
        Assert.Equal(schedule.Run.Hitches.Select(h => h.FrameIndex).Order(), flagged);

        foreach (var hitch in schedule.Run.Hitches)
        {
            Assert.Equal(hitch.ExtraMs, schedule.InjectedExtraMs[hitch.FrameIndex]);
            var interval = QpcTime.ToMs(schedule.TargetTicks[hitch.FrameIndex] - schedule.TargetTicks[hitch.FrameIndex - 1], Qpc);
            Assert.InRange(interval, 1000.0 / 144 + 40 - 0.001, 1000.0 / 144 + 40 + 0.001);
        }

        // Every other frame carries no extra.
        Assert.Equal(3, Enumerable.Range(0, schedule.Count).Count(i => schedule.InjectedExtraMs[i] > 0));
    }

    [Fact]
    public void A_clean_run_has_no_hitch_flags()
    {
        var schedule = FrameSchedule.Create(SyntheticWorkload.Generate(1, 60, 120, 0, 0, 0), Qpc);
        Assert.DoesNotContain(true, Enumerable.Range(0, schedule.Count).Select(schedule.IsHitch));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-5L)]
    public void A_clock_without_a_positive_frequency_is_rejected(long frequency) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => FrameSchedule.Create(SyntheticWorkload.Generate(1, 60, 120, 0, 0, 0), frequency));

    [Fact]
    public void Time_conversion_round_trips_at_the_clock_resolution()
    {
        Assert.Equal(1000.0, QpcTime.ToMs(Qpc, Qpc));
        Assert.Equal(0.0001, QpcTime.ToMs(1, Qpc), 12);
        Assert.Equal(Qpc / 1000 * 25, QpcTime.FromMs(25, Qpc));
        Assert.Equal(1L, QpcTime.FromMs(0.0001, Qpc));
    }
}
