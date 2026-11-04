using Jankscope.Core;

namespace Jankscope.Harness;

internal enum AdapterRequest
{
    Auto,
    Hardware,
    Warp,
}

/// <summary>The kind of adapter a run actually used. WARP is the Windows software rasterizer.</summary>
internal enum AdapterKind
{
    Hardware,
    Warp,
}

/// <summary>The environment cannot run the harness (no usable Direct3D 11 adapter, no window). Not a bug; the CLI exits with code 3.</summary>
/// <param name="message">What went wrong (never a path).</param>
/// <param name="deviceUnavailable">
/// True only when this machine cannot give the harness a device at all (no adapter, the adapter refuses a device, no WARP, no window): a test may
/// skip itself for that. False for a failure after a device exists (swap chain, present): that is a defect in the harness and must fail a test,
/// not skip it (review 1a, finding 7).
/// </param>
internal sealed class HarnessEnvironmentException(string message, bool deviceUnavailable = false) : Exception(message)
{
    public bool DeviceUnavailable { get; } = deviceUnavailable;
}

internal sealed record WorkloadOptions(int Seed, double Fps, int Frames, int Hitches, double ExtraMs, double JitterMs)
{
    public SyntheticRun Generate() => SyntheticWorkload.Generate(Seed, Fps, Frames, Hitches, ExtraMs, JitterMs);
}

internal sealed record PlanOptions(WorkloadOptions Workload, string? OutPath);

/// <param name="SimulateNoHardware">Test hook, not reachable from the command line: behave as if no hardware adapter could be created.</param>
internal sealed record RunOptions(WorkloadOptions Workload, string OutPath, AdapterRequest Adapter, bool WindowVisible)
{
    public bool SimulateNoHardware { get; init; }
}

internal sealed record AdapterInfo(AdapterKind Kind, string Description, string FeatureLevel, AdapterRequest Requested, string? FallbackReason);

internal sealed record SwapChainInfo(string SwapEffect, int BufferCount, int Width, int Height, int SyncInterval, int PresentFlags, bool AllowsTearing, string? Address);

internal sealed record PacerReport(double MarginMs, int Sleeps, long SpinIterations, int LateArrivals, int WarmUpFrames);

/// <summary>The harness's own CPU time (all of its threads) over the paced loop, against wall-clock time.</summary>
internal sealed record CpuCost(double CpuMs, double WallMs, double PercentOfOneCore);

/// <summary>
/// Everything one run produced. It is Simulated by construction: the workload is synthetic, so no value here is a measurement of a real game
/// (HC-9). <see cref="Provenance"/> is a property, not a field a caller could set.
/// </summary>
internal sealed record HarnessRunResult(
    WorkloadOptions Workload,
    SyntheticRun Run,
    FrameSchedule Schedule,
    IReadOnlyList<FrameRecord> Records,
    long Frequency,
    AdapterInfo Adapter,
    SwapChainInfo SwapChain,
    FidelityReport Fidelity,
    PacerReport Pacer,
    CpuCost Cpu,
    int OccludedPresents,
    bool WindowVisible,
    bool Elevated)
{
    public DataProvenance Provenance => DataProvenance.Simulated;
}
