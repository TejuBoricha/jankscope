namespace Jankscope.Frames.Detection;

/// <summary>
/// The tunable constants of the stutter definition (docs/METRICS.md): a frame is a stutter if it takes at least
/// <see cref="Multiplier"/> times the median frame time of the preceding <see cref="RollingWindowMs"/> AND at least
/// <see cref="MinExcessMs"/> longer than that median.
/// </summary>
/// <param name="RollingWindowMs">Length of the history the median is taken over. The window ends where the judged frame began.</param>
/// <param name="Multiplier">Required ratio of the frame time to the median (at least 1).</param>
/// <param name="MinExcessMs">Required absolute excess over the median in milliseconds (at least 0).</param>
/// <param name="MinHistoryFrames">Frames the window must hold before a frame can be judged; fewer is reported as "insufficient history", never as "no stutter".</param>
/// <param name="ClusterGapMs">Events whose timestamps are closer than this belong to the same cluster.</param>
/// <param name="Calibrated">False until a baseline exists for this machine and game. Copied to every result so downstream grade caps apply (spec section 7.1).</param>
public sealed record StutterOptions(
    double RollingWindowMs = 1000.0,
    double Multiplier = 2.0,
    double MinExcessMs = 4.0,
    int MinHistoryFrames = 10,
    double ClusterGapMs = 500.0,
    bool Calibrated = false)
{
    /// <summary>The documented defaults, uncalibrated.</summary>
    public static StutterOptions Default { get; } = new();

    /// <summary>Throws <see cref="ArgumentOutOfRangeException"/> naming the property if any value makes the definition meaningless.</summary>
    public void Validate()
    {
        if (!double.IsFinite(RollingWindowMs) || RollingWindowMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(RollingWindowMs), RollingWindowMs, "must be finite and positive");
        }

        if (!double.IsFinite(Multiplier) || Multiplier < 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(Multiplier), Multiplier, "must be finite and at least 1 (a smaller multiplier would flag typical frames)");
        }

        if (!double.IsFinite(MinExcessMs) || MinExcessMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MinExcessMs), MinExcessMs, "must be finite and not negative");
        }

        if (MinHistoryFrames < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MinHistoryFrames), MinHistoryFrames, "must be at least 1 (a median needs a frame)");
        }

        if (!double.IsFinite(ClusterGapMs) || ClusterGapMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ClusterGapMs), ClusterGapMs, "must be finite and not negative");
        }
    }
}
