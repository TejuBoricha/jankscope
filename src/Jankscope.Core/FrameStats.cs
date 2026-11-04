namespace Jankscope.Core;

/// <summary>
/// Frame-time statistics with the operational definitions of spec §7.1 (documented in docs/METRICS.md).
/// All inputs are frame times in milliseconds. Invalid input (empty, non-finite or non-positive frame times) yields
/// <see cref="double.NaN"/>: an honest "cannot compute", never a plausible number.
/// </summary>
public static class FrameStats
{
    public const int MinFramesForOnePercentLow = 1_000;
    public const int MinFramesForPointOnePercentLow = 10_000;

    /// <summary>Average FPS = frames ÷ duration (not the mean of per-frame FPS).</summary>
    public static double AverageFps(ReadOnlySpan<double> frameTimesMs)
    {
        if (frameTimesMs.IsEmpty || !AllValid(frameTimesMs))
        {
            return double.NaN;
        }

        double totalMs = 0;
        foreach (var t in frameTimesMs)
        {
            totalMs += t;
        }

        return frameTimesMs.Length / (totalMs / 1000.0);
    }

    /// <summary>Nearest-rank percentile of frame time: the value at rank ceil(p/100 × N) of the ascending sort.</summary>
    public static double PercentileFrameTimeMs(ReadOnlySpan<double> frameTimesMs, double percentile)
    {
        if (frameTimesMs.IsEmpty || !double.IsFinite(percentile) || percentile <= 0 || percentile > 100 || !AllValid(frameTimesMs))
        {
            return double.NaN;
        }

        var sorted = frameTimesMs.ToArray();
        Array.Sort(sorted);
        return sorted[NearestRank(sorted.Length, percentile) - 1];
    }

    /// <summary>
    /// 1-based nearest rank. Computed in <see cref="decimal"/> because binary floating point gets it wrong:
    /// 99.9 / 100 × 10 000 evaluates to 9990.000000000002 in <see cref="double"/>, whose ceiling is rank 9991 instead of 9990.
    /// </summary>
    public static int NearestRank(int count, double percentile)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        var rank = (int)Math.Ceiling((decimal)percentile * count / 100m);
        return Math.Clamp(rank, 1, count);
    }

    /// <summary>Primary 1 % low: FPS at the 99th-percentile frame time. Null (insufficient samples) below 1 000 frames; NaN for invalid input.</summary>
    public static double? OnePercentLowFps(ReadOnlySpan<double> frameTimesMs) =>
        frameTimesMs.Length < MinFramesForOnePercentLow ? null : 1000.0 / PercentileFrameTimeMs(frameTimesMs, 99.0);

    /// <summary>Primary 0.1 % low: FPS at the 99.9th-percentile frame time. Null below 10 000 frames (session-level only); NaN for invalid input.</summary>
    public static double? PointOnePercentLowFps(ReadOnlySpan<double> frameTimesMs) =>
        frameTimesMs.Length < MinFramesForPointOnePercentLow ? null : 1000.0 / PercentileFrameTimeMs(frameTimesMs, 99.9);

    private static bool AllValid(ReadOnlySpan<double> frameTimesMs)
    {
        foreach (var t in frameTimesMs)
        {
            if (!double.IsFinite(t) || t <= 0)
            {
                return false;
            }
        }

        return true;
    }
}
