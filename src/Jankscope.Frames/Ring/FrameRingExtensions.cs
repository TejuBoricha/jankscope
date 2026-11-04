namespace Jankscope.Frames.Ring;

/// <summary>Pre-roll window selection for rings of <see cref="FrameSample"/> (spec section 7.3: the frames just before a mark).</summary>
public static class FrameRingExtensions
{
    /// <summary>
    /// The samples whose <see cref="FrameSample.TimestampMs"/> lies in <c>[newest - windowMs, newest]</c>, where <c>newest</c> is the
    /// timestamp of the newest sample in the ring. Empty for an empty ring. Oldest first.
    /// </summary>
    public static FrameSample[] SnapshotWindow(this RingBuffer<FrameSample> ring, double windowMs)
    {
        ArgumentNullException.ThrowIfNull(ring);
        ValidateWindow(windowMs);
        var all = ring.Snapshot();
        return all.Length == 0 ? all : SelectWindow(all, all[^1].TimestampMs, windowMs).ToArray();
    }

    /// <summary>
    /// The samples whose timestamp lies in <c>[endTimestampMs - windowMs, endTimestampMs]</c>: the pre-roll before a mark at
    /// <paramref name="endTimestampMs"/>. Both ends are inclusive. Oldest first.
    /// </summary>
    public static FrameSample[] SnapshotWindow(this RingBuffer<FrameSample> ring, double endTimestampMs, double windowMs)
    {
        ArgumentNullException.ThrowIfNull(ring);
        return SelectWindow(ring.Snapshot(), endTimestampMs, windowMs).ToArray();
    }

    /// <summary>
    /// Selects the contiguous run of <paramref name="chronological"/> whose timestamps lie in <c>[endTimestampMs - windowMs,
    /// endTimestampMs]</c>. The input must be in capture order (non-decreasing timestamps). Samples later than the end are skipped from
    /// the back; the run stops at the first sample older than the start, so a clock that jumped backwards inside the span cuts the
    /// window there instead of reaching into an unrelated epoch.
    /// </summary>
    public static ReadOnlySpan<FrameSample> SelectWindow(ReadOnlySpan<FrameSample> chronological, double endTimestampMs, double windowMs)
    {
        if (!double.IsFinite(endTimestampMs))
        {
            throw new ArgumentOutOfRangeException(nameof(endTimestampMs), endTimestampMs, "must be finite");
        }

        ValidateWindow(windowMs);

        if (chronological.IsEmpty)
        {
            return chronological;
        }

        var runStart = chronological.Length - 1;
        while (runStart > 0 && SameCaptureEpoch(in chronological[runStart - 1], in chronological[runStart]))
        {
            runStart--;
        }

        chronological = chronological[runStart..];

        var startTimestampMs = endTimestampMs - windowMs;
        var last = chronological.Length - 1;
        while (last >= 0 && chronological[last].TimestampMs > endTimestampMs)
        {
            last--;
        }

        var first = last + 1;
        while (first > 0 && chronological[first - 1].TimestampMs >= startTimestampMs)
        {
            first--;
        }

        return chronological[first..(last + 1)];
    }

    private static bool SameCaptureEpoch(in FrameSample earlier, in FrameSample later)
    {
        if (earlier.TimestampMs > later.TimestampMs)
        {
            return false;
        }

        if (earlier.Application is not null
            && later.Application is not null
            && !string.Equals(earlier.Application, later.Application, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static void ValidateWindow(double windowMs)
    {
        if (!double.IsFinite(windowMs) || windowMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(windowMs), windowMs, "must be finite and not negative");
        }
    }
}
