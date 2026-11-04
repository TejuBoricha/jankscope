using Jankscope.Core;

namespace Jankscope.Harness;

/// <summary>
/// The ground truth turned into absolute present times. Frame i is due at the cumulative sum of the first i + 1 ground-truth frame times, so
/// each ground-truth value is the interval between present i - 1 and present i (present -1 is the pacing origin). The targets are absolute:
/// a late frame never shifts the frames after it, which keeps the error from accumulating.
/// </summary>
internal sealed class FrameSchedule
{
    private readonly HashSet<int> _hitchFrames;

    private FrameSchedule(SyntheticRun run, long frequency, double[] targetMs, long[] targetTicks, double[] injectedExtraMs)
    {
        Run = run;
        Frequency = frequency;
        TargetMs = targetMs;
        TargetTicks = targetTicks;
        InjectedExtraMs = injectedExtraMs;
        _hitchFrames = run.Hitches.Select(h => h.FrameIndex).ToHashSet();
    }

    public SyntheticRun Run { get; }

    public long Frequency { get; }

    public int Count => TargetTicks.Count;

    /// <summary>Scheduled present time of each frame in milliseconds from the origin (exact cumulative sum).</summary>
    public IReadOnlyList<double> TargetMs { get; }

    /// <summary>The same times in clock ticks from the origin, rounded once from <see cref="TargetMs"/>.</summary>
    public IReadOnlyList<long> TargetTicks { get; }

    /// <summary>The extra time injected into each frame (0 for frames without a hitch).</summary>
    public IReadOnlyList<double> InjectedExtraMs { get; }

    public bool IsHitch(int index) => _hitchFrames.Contains(index);

    public static FrameSchedule Create(SyntheticRun run, long frequency)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(frequency, 0L);

        var count = run.FrameTimesMs.Count;
        var targetMs = new double[count];
        var targetTicks = new long[count];
        var extra = new double[count];
        var sum = 0.0;
        for (var i = 0; i < count; i++)
        {
            sum += run.FrameTimesMs[i];
            targetMs[i] = sum;
            targetTicks[i] = QpcTime.FromMs(sum, frequency);
        }

        foreach (var hitch in run.Hitches)
        {
            extra[hitch.FrameIndex] = hitch.ExtraMs;
        }

        return new FrameSchedule(run, frequency, targetMs, targetTicks, extra);
    }
}
