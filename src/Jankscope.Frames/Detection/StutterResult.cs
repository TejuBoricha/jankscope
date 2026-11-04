using Jankscope.Core;

namespace Jankscope.Frames.Detection;

/// <summary>What the detector concluded about one pushed frame.</summary>
public enum FrameStatus
{
    /// <summary>The frame was judged against enough history and is not a stutter. (Value 0 so a default verdict is never mistaken for a stutter.)</summary>
    NoStutter = 0,

    /// <summary>The frame met the definition.</summary>
    Stutter = 1,

    /// <summary>Fewer than <see cref="StutterOptions.MinHistoryFrames"/> frames precede it in the window: it could not be judged. This is a status of its own and is counted; it is never "no stutter".</summary>
    InsufficientHistory = 2,

    /// <summary>The sample was unusable (see <see cref="FrameRejectionReason"/>); it did not enter the window.</summary>
    Rejected = 3,
}

/// <summary>Why a sample was rejected.</summary>
public enum FrameRejectionReason
{
    None = 0,

    /// <summary>Frame time is NaN or infinite.</summary>
    FrameTimeNotFinite = 1,

    /// <summary>Frame time is below zero.</summary>
    FrameTimeNegative = 2,

    /// <summary>Frame time is exactly zero: no frame takes no time, so this is a missing measurement, not a fast frame.</summary>
    FrameTimeZero = 3,

    /// <summary>Timestamp is NaN or infinite.</summary>
    TimestampNotFinite = 4,
}

/// <summary>
/// The verdict for one frame. <see cref="MedianMs"/>, <see cref="ExcessMs"/> and <see cref="Ratio"/> are NaN when no median was
/// computed (insufficient history or rejected): an absent number, never a zero.
/// </summary>
public readonly record struct FrameVerdict(
    FrameStatus Status,
    FrameRejectionReason Rejection,
    double MedianMs,
    double ExcessMs,
    double Ratio,
    StutterEvent? Event);

/// <summary>
/// The outcome of a detection run. Counts add up: <c>FramesSeen = FramesAccepted + RejectedFrames</c> and
/// <c>FramesAccepted = FramesEvaluated + InsufficientHistoryFrames</c>. <paramref name="Provenance"/> is the weakest provenance of any frame seen
/// (<see cref="ProvenanceRules.Combine"/>), rejected frames included; it is <see cref="DataProvenance.None"/> when no frame was seen.
/// </summary>
public sealed record StutterResult(
    StutterOptions Options,
    IReadOnlyList<StutterEvent> Events,
    IReadOnlyList<StutterCluster> Clusters,
    long FramesSeen,
    long FramesEvaluated,
    long InsufficientHistoryFrames,
    long RejectedFrames,
    IReadOnlyDictionary<FrameRejectionReason, long> RejectedByReason,
    long ClockDiscontinuities,
    DataProvenance Provenance)
{
    /// <summary>
    /// False when any frame that went in was simulated, a fixture or assumed (HC-9): the result then cannot be shown or exported as a measurement.
    /// Call <see cref="ProvenanceRules.RequireExportableAsMeasurement"/> on <see cref="Provenance"/> at the export boundary.
    /// </summary>
    public bool MayBeExportedAsMeasurement => Provenance.MayBeExportedAsMeasurement();

    /// <summary>Copy of <see cref="StutterOptions.Calibrated"/>: false means the constants are the uncalibrated defaults and downstream grades must be capped.</summary>
    public bool Calibrated => Options.Calibrated;

    /// <summary>"calibrated" or "uncalibrated", for display next to any figure derived from this result.</summary>
    public string CalibrationLabel => Calibrated ? "calibrated" : "uncalibrated";

    /// <summary>Samples that entered the window (judged or in warm-up).</summary>
    public long FramesAccepted => FramesSeen - RejectedFrames;

    /// <summary>Clusters of exactly one event: spikes on their own rather than bursts.</summary>
    public int IsolatedSpikeCount => Clusters.Count(c => c.IsIsolated);

    /// <summary>Sum of the excess of all events in milliseconds.</summary>
    public double TotalExcessMs => Events.Sum(e => e.ExcessMs);
}
