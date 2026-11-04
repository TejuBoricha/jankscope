using Jankscope.Core;

namespace Jankscope.Harness;

/// <summary>Absolute error statistics in milliseconds. Percentiles use the nearest-rank definition of METRICS.md.</summary>
internal sealed record ErrorStats(double MaxMs, double MedianMs, double P99Ms);

/// <summary>What one injected hitch really looked like in the harness's own present log.</summary>
internal sealed record HitchObservation(int FrameIndex, double InjectedExtraMs, double AchievedFrameTimeMs, double CarriedExtraMs);

/// <param name="PresentTimeError">|Present-return time - scheduled time| per frame.</param>
/// <param name="FrameTimeError">|achieved interval - ground-truth interval| per frame: what a detector working on frame times is exposed to.</param>
/// <param name="MeanSignedPresentErrorMs">Positive when frames come out late on average (the Present call itself takes time).</param>
/// <param name="DisturbedFrames">Frames whose achieved interval is off by more than <see cref="Fidelity.DisturbedThresholdMs"/> (signed: positive = late). These are stalls of the harness or the OS, not ground truth; an end-to-end test should treat them as such.</param>
/// <param name="Hitches">For each injected hitch: the achieved interval and the extra it carried beyond the same frame without the injection.</param>
internal sealed record FidelityReport(
    int Frames,
    ErrorStats PresentTimeError,
    ErrorStats FrameTimeError,
    double MeanSignedPresentErrorMs,
    IReadOnlyList<HitchObservation> Hitches,
    IReadOnlyList<DisturbedFrame> DisturbedFrames);

/// <summary>A frame whose achieved interval differs from the ground truth by more than the disturbance threshold.</summary>
internal sealed record DisturbedFrame(int FrameIndex, double IntervalErrorMs);

internal static class Fidelity
{
    /// <summary>Half the 4 ms "above the rolling median" rule of the stutter definition (METRICS.md): anything beyond this could move a detector's verdict.</summary>
    public const double DisturbedThresholdMs = 2.0;

    public static FidelityReport Compute(FrameSchedule schedule, IReadOnlyList<FrameRecord> records)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(records);
        if (records.Count == 0 || records.Count != schedule.Count)
        {
            throw new ArgumentException($"Expected one record per scheduled frame ({schedule.Count}), got {records.Count}.", nameof(records));
        }

        var frequency = schedule.Frequency;
        var presentError = new double[records.Count];
        var frameError = new double[records.Count];
        var signedSum = 0.0;
        var previousPresent = 0L;
        var previousTarget = 0L;
        var hitches = new List<HitchObservation>();
        var disturbed = new List<DisturbedFrame>();

        for (var i = 0; i < records.Count; i++)
        {
            var r = records[i];
            var errorTicks = r.PresentTicks - r.ScheduledTicks;
            presentError[i] = Math.Abs(QpcTime.ToMs(errorTicks, frequency));
            signedSum += QpcTime.ToMs(errorTicks, frequency);

            var achievedTicks = r.PresentTicks - previousPresent;
            var wantedTicks = r.ScheduledTicks - previousTarget;
            var signedIntervalError = QpcTime.ToMs(achievedTicks - wantedTicks, frequency);
            frameError[i] = Math.Abs(signedIntervalError);
            if (frameError[i] > DisturbedThresholdMs)
            {
                disturbed.Add(new DisturbedFrame(i, signedIntervalError));
            }

            if (schedule.IsHitch(i))
            {
                var achievedMs = QpcTime.ToMs(achievedTicks, frequency);
                var withoutHitchMs = schedule.Run.FrameTimesMs[i] - schedule.InjectedExtraMs[i];
                hitches.Add(new HitchObservation(i, schedule.InjectedExtraMs[i], achievedMs, achievedMs - withoutHitchMs));
            }

            previousPresent = r.PresentTicks;
            previousTarget = r.ScheduledTicks;
        }

        return new FidelityReport(records.Count, Summarize(presentError), Summarize(frameError), signedSum / records.Count, hitches, disturbed);
    }

    private static ErrorStats Summarize(double[] absoluteErrorsMs)
    {
        var sorted = (double[])absoluteErrorsMs.Clone();
        Array.Sort(sorted);
        double At(double percentile) => sorted[FrameStats.NearestRank(sorted.Length, percentile) - 1];
        return new ErrorStats(sorted[^1], At(50), At(99));
    }
}
