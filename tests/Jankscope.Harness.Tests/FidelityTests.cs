using Jankscope.Core;

namespace Jankscope.Harness.Tests;

public class FidelityTests
{
    private const long Qpc = 10_000_000;

    /// <summary>100 fps (10 ms frames), 120 frames, one 20 ms hitch; every frame presented e_i = (i mod 10) x 0.1 ms after its target.</summary>
    private static (FrameSchedule Schedule, List<FrameRecord> Records) Sawtooth()
    {
        var schedule = FrameSchedule.Create(SyntheticWorkload.Generate(1, 100, 120, 1, 20, 0), Qpc);
        var records = Enumerable.Range(0, schedule.Count)
            .Select(i => new FrameRecord(i, schedule.TargetTicks[i], schedule.TargetTicks[i] + (i % 10) * 1_000L, 300, schedule.InjectedExtraMs[i]))
            .ToList();
        return (schedule, records);
    }

    [Fact]
    public void Present_time_error_is_max_median_and_p99_of_the_absolute_difference_to_the_target()
    {
        var (schedule, records) = Sawtooth();

        var report = Fidelity.Compute(schedule, records);

        // |e| takes each value 0.0 ... 0.9 ms twelve times. Nearest rank: median = rank 60 = 0.4 ms, p99 = rank 119 = 0.9 ms.
        Assert.Equal(0.9, report.PresentTimeError.MaxMs, 9);
        Assert.Equal(0.4, report.PresentTimeError.MedianMs, 9);
        Assert.Equal(0.9, report.PresentTimeError.P99Ms, 9);
        Assert.Equal(0.45, report.MeanSignedPresentErrorMs, 9);
        Assert.Equal(120, report.Frames);
    }

    [Fact]
    public void Frame_time_error_compares_each_achieved_interval_with_the_ground_truth_interval()
    {
        var (schedule, records) = Sawtooth();

        var report = Fidelity.Compute(schedule, records);

        // a_i - f_i = e_i - e_(i-1): +0.1 ms on 108 frames, -0.9 ms on the 11 frames where the sawtooth wraps, 0 on frame 0.
        Assert.Equal(0.9, report.FrameTimeError.MaxMs, 9);
        Assert.Equal(0.1, report.FrameTimeError.MedianMs, 9);
        Assert.Equal(0.9, report.FrameTimeError.P99Ms, 9);
    }

    [Fact]
    public void One_outlier_moves_the_max_but_not_the_median_or_the_p99()
    {
        // 300 frames, every one 0.1 ms late except frame 150, which is 5 ms late. Nearest-rank p99 of 300 values is rank 297, so it ignores
        // the single worst frame; the max reports it. A statistic mix-up (p99 computed as max, or the other way round) cannot hide here.
        var schedule = FrameSchedule.Create(SyntheticWorkload.Generate(1, 100, 300, 0, 0, 0), Qpc);
        var records = Enumerable.Range(0, schedule.Count)
            .Select(i => new FrameRecord(i, schedule.TargetTicks[i], schedule.TargetTicks[i] + (i == 150 ? 50_000L : 1_000L), 300, 0))
            .ToList();

        var report = Fidelity.Compute(schedule, records);

        Assert.Equal(5.0, report.PresentTimeError.MaxMs, 9);
        Assert.Equal(0.1, report.PresentTimeError.MedianMs, 9);
        Assert.Equal(0.1, report.PresentTimeError.P99Ms, 9);
        // The same outlier seen as an interval error: +4.9 ms entering frame 150, -4.9 ms leaving it (two frames, still under 1 % of 300).
        Assert.Equal(4.9, report.FrameTimeError.MaxMs, 9);
        Assert.Equal(0.0, report.FrameTimeError.MedianMs, 9);
        Assert.Equal(0.0, report.FrameTimeError.P99Ms, 9);
    }

    [Fact]
    public void Frames_with_a_large_interval_error_are_listed_as_disturbed_with_their_sign()
    {
        // 100 fps, 120 frames; frame 40 is 6 ms late (a stall) and frame 80 is 3 ms early. Intervals: +6 ms into 40, -6 ms out of it at 41;
        // -3 ms into 80, +3 ms out of it at 81. A 1.9 ms wobble elsewhere stays below the 2 ms threshold and is not listed.
        var schedule = FrameSchedule.Create(SyntheticWorkload.Generate(1, 100, 120, 0, 0, 0), Qpc);
        long Offset(int i) => i == 40 ? 60_000 : i == 80 ? -30_000 : i == 100 ? 19_000 : 0;
        var records = Enumerable.Range(0, schedule.Count)
            .Select(i => new FrameRecord(i, schedule.TargetTicks[i], schedule.TargetTicks[i] + Offset(i), 100, 0))
            .ToList();

        var report = Fidelity.Compute(schedule, records);

        Assert.Equal([40, 41, 80, 81], report.DisturbedFrames.Select(d => d.FrameIndex));
        Assert.Equal([6.0, -6.0, -3.0, 3.0], report.DisturbedFrames.Select(d => Math.Round(d.IntervalErrorMs, 6)));
        Assert.Equal(2.0, Fidelity.DisturbedThresholdMs);
    }

    [Fact]
    public void A_perfect_run_has_no_disturbed_frames()
    {
        var schedule = FrameSchedule.Create(SyntheticWorkload.Generate(2, 60, 150, 2, 30, 0.4), Qpc);
        var records = Enumerable.Range(0, schedule.Count).Select(i => new FrameRecord(i, schedule.TargetTicks[i], schedule.TargetTicks[i], 100, schedule.InjectedExtraMs[i])).ToList();

        Assert.Empty(Fidelity.Compute(schedule, records).DisturbedFrames);
    }

    [Fact]
    public void A_perfect_run_has_zero_error_everywhere()
    {
        var schedule = FrameSchedule.Create(SyntheticWorkload.Generate(2, 60, 150, 2, 30, 0.4), Qpc);
        var records = Enumerable.Range(0, schedule.Count).Select(i => new FrameRecord(i, schedule.TargetTicks[i], schedule.TargetTicks[i], 100, schedule.InjectedExtraMs[i])).ToList();

        var report = Fidelity.Compute(schedule, records);

        Assert.Equal(0, report.PresentTimeError.MaxMs);
        Assert.Equal(0, report.FrameTimeError.P99Ms);
        Assert.Equal(0, report.MeanSignedPresentErrorMs);
        Assert.All(report.Hitches, h => Assert.Equal(h.InjectedExtraMs, h.CarriedExtraMs, 3)); // targets are rounded to whole ticks (0.0001 ms)
    }

    [Fact]
    public void Each_injected_hitch_reports_the_extra_that_the_achieved_interval_actually_carried()
    {
        var (schedule, records) = Sawtooth();
        var h = schedule.Run.Hitches.Single().FrameIndex;

        var report = Fidelity.Compute(schedule, records);

        var observation = Assert.Single(report.Hitches);
        Assert.Equal(h, observation.FrameIndex);
        Assert.Equal(20.0, observation.InjectedExtraMs);
        var eHere = (h % 10) * 0.1;
        var ePrev = ((h - 1) % 10) * 0.1;
        Assert.Equal(10.0 + 20.0 + (eHere - ePrev), observation.AchievedFrameTimeMs, 6);
        Assert.Equal(20.0 + (eHere - ePrev), observation.CarriedExtraMs, 6);
    }

    [Fact]
    public void A_missing_hitch_would_show_up_as_a_carried_extra_of_zero()
    {
        // The frame was scheduled with 20 ms extra but the loop presented it on time: carried extra must be about -20, not 20.
        var schedule = FrameSchedule.Create(SyntheticWorkload.Generate(1, 100, 120, 1, 20, 0), Qpc);
        var h = schedule.Run.Hitches.Single().FrameIndex;
        var records = new List<FrameRecord>();
        for (var i = 0; i < schedule.Count; i++)
        {
            var presentAt = i < h ? schedule.TargetTicks[i] : schedule.TargetTicks[i] - 200_000; // 20 ms early from the hitch on
            records.Add(new FrameRecord(i, schedule.TargetTicks[i], presentAt, 100, schedule.InjectedExtraMs[i]));
        }

        var observation = Assert.Single(Fidelity.Compute(schedule, records).Hitches);

        Assert.Equal(0.0, observation.CarriedExtraMs, 6);
    }

    [Fact]
    public void Mismatched_input_is_rejected()
    {
        var (schedule, records) = Sawtooth();
        Assert.Throws<ArgumentException>(() => Fidelity.Compute(schedule, records.Take(10).ToList()));
        Assert.Throws<ArgumentException>(() => Fidelity.Compute(schedule, []));
    }
}
