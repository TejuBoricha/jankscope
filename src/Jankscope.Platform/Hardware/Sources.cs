using Jankscope.Core.Hardware;

namespace Jankscope.Platform.Hardware;

// The seams between the probes (what to say) and Windows (what is read). Every failure path can be injected in tests.

/// <param name="Buffer">The raw records of GetLogicalProcessorInformationEx, or null when they could not be read.</param>
/// <param name="OsLogicalProcessorCount">GetActiveProcessorCount for all groups, or null when unavailable.</param>
/// <param name="Failure">Why <paramref name="Buffer"/> is null.</param>
internal sealed record ProcessorInfoReading(byte[]? Buffer, int? OsLogicalProcessorCount, string? Failure);

internal interface IProcessorInfoSource
{
    ProcessorInfoReading Read();
}

/// <param name="Failure">Non-null when enumeration failed before any adapter could be listed; <paramref name="Adapters"/> is then empty.</param>
internal sealed record AdapterEnumeration(IReadOnlyList<GpuAdapterInfo> Adapters, string? Failure);

internal interface IGpuAdapterSource
{
    AdapterEnumeration Enumerate();
}

/// <param name="Paths">All paths the OS reported as active.</param>
/// <param name="AdvancedColor">The advanced color state per (target adapter, target id); a missing entry means it was not obtained.</param>
/// <param name="Failure">Non-null when the configuration could not be read at all.</param>
internal sealed record DisplayConfigReading(
    IReadOnlyList<DisplayPath> Paths,
    IReadOnlyDictionary<(Luid Adapter, uint Target), AdvancedColorState> AdvancedColor,
    IReadOnlyList<string> Problems,
    string? Failure);

internal interface IDisplayConfigSource
{
    DisplayConfigReading Read();
}

/// <param name="Failure">Non-null when no WMI class could be queried at all; individual unreadable strings are null inside <paramref name="Strings"/>.</param>
internal sealed record FirmwareReading(FirmwareStrings Strings, string? Failure);

internal interface IFirmwareStringSource
{
    FirmwareReading Read();
}
