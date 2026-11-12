using Jankscope.Core;

namespace Jankscope.Frames;

/// <summary>
/// One presented application frame from any source (a PresentMon log, the harness's own present log, a fixture).
/// Optional metrics are <c>null</c> when the source said "NA" or did not provide the column. They are never zero:
/// a missing value must not look like a measurement (spec §6.4).
/// </summary>
/// <param name="Index">0-based position of the frame in the capture.</param>
/// <param name="TimestampMs">Time since the capture started, in milliseconds (PresentMon <c>TimeInMs</c>).</param>
/// <param name="FrameTimeMs">Application frame time: PresentMon <c>MsBetweenPresents</c>. This is the series stutter detection and lows use.</param>
/// <param name="DisplayedFrameTimeMs">Time between displayed frames (<c>MsBetweenDisplayChange</c>). Includes generated frames; never mixed with <paramref name="FrameTimeMs"/>.</param>
/// <param name="Provenance">
/// Where the frame came from (HC-9). The parser sets <see cref="DataProvenance.Measured"/> for a log the pinned tool wrote, and
/// <see cref="DataProvenance.Simulated"/> for a row of the synthetic harness's own log, which says so in its <c>JankscopeProvenance</c> column.
/// A sample built by hand defaults to <see cref="DataProvenance.Assumed"/>, so it can never be exported as a measurement by accident.
/// </param>
/// <param name="SwapChain">
/// The swap chain the frame was presented on (PresentMon <c>SwapChainAddress</c>, for example <c>0x103E8</c>), or <c>null</c> when the log does not say.
/// PresentMon measures <paramref name="FrameTimeMs"/> per swap chain, so one program that presents on two interleaves two series (<c>LIMITATIONS</c> L26).
/// It is only used to notice that; it is never exported (it is a memory address of the program's own).
/// </param>
public readonly record struct FrameSample(
    long Index,
    double TimestampMs,
    double FrameTimeMs,
    double? DisplayedFrameTimeMs = null,
    double? CpuBusyMs = null,
    double? GpuBusyMs = null,
    string? PresentMode = null,
    string? Application = null,
    int? ProcessId = null,
    DataProvenance Provenance = DataProvenance.Assumed,
    string? SwapChain = null);
