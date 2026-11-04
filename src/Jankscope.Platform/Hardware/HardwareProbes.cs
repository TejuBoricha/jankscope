using Jankscope.Core;
using Jankscope.Core.Hardware;

namespace Jankscope.Platform.Hardware;

/// <summary>
/// Registers the four hardware-discovery probes. They replace the NotImplemented rows <c>cpu.topology</c>, <c>gpu.adapters</c>,
/// <c>display.topology</c> and <c>environment.virtual_machine</c> of <see cref="PlannedProbe"/>, whose entries must be removed when
/// these are added to <see cref="DefaultProbes"/>. Every probe is read-only and unelevated.
/// </summary>
public static class HardwareDiscoveryProbes
{
    public static IReadOnlyList<IProbe> Create(CatalogSet catalogs) =>
    [
        new CpuTopologyProbe(),
        new GpuAdaptersProbe(),
        new DisplayTopologyProbe(),
        new VirtualMachineProbe(catalogs),
    ];
}

/// <summary>CPU topology from GetLogicalProcessorInformationEx: hybrid performance/efficiency cores by efficiency class, SMT, cache domains.</summary>
public sealed class CpuTopologyProbe : IProbe
{
    internal const string RowId = "cpu.topology";
    private const string Title = "CPU topology (hybrid cores, CCDs, SMT)";
    private const string Source = "GetLogicalProcessorInformationEx + GetActiveProcessorCount (documented)";

    private readonly IProcessorInfoSource _source;

    public CpuTopologyProbe()
        : this(new WindowsProcessorInfoSource())
    {
    }

    internal CpuTopologyProbe(IProcessorInfoSource source) => _source = source;

    public string Id => "cpu";

    public IReadOnlyList<CapabilityEntry> Run() => [Row(_source.Read())];

    internal static CapabilityEntry Row(ProcessorInfoReading reading)
    {
        if (reading.Buffer is null)
        {
            return CapabilityEntry.Unavailable(RowId, Title, CapabilityTier.A, Availability.Unknown, Source,
                reading.Failure ?? "the processor records could not be read");
        }

        var raw = ProcessorInfoParser.Parse(reading.Buffer);
        var topology = CpuTopologyMapper.Map(raw, reading.OsLogicalProcessorCount);
        if (topology is null)
        {
            return CapabilityEntry.Unavailable(RowId, Title, CapabilityTier.A, Availability.Unknown, Source,
                $"the processor records contain no core ({string.Join("; ", raw.Problems)})");
        }

        return CapabilityEntry.Available(RowId, Title, CapabilityTier.A, Source, topology.Describe(), DataProvenance.Measured,
            topology.Problems.Count == 0 ? null : "notes: " + string.Join("; ", topology.Problems));
    }

    /// <summary>The topology itself, for callers that need numbers instead of text; null when it cannot be read.</summary>
    public static CpuTopology? ReadTopology()
    {
        var reading = new WindowsProcessorInfoSource().Read();
        return reading.Buffer is null ? null : CpuTopologyMapper.Map(ProcessorInfoParser.Parse(reading.Buffer), reading.OsLogicalProcessorCount);
    }
}

/// <summary>
/// Graphics adapters keyed by LUID (DXGI through Vortice.DXGI) with the number of active displays each one drives (display configuration
/// API). Facts only: no statement about which adapter a game uses or should use. No vendor API and no device creation, so a sleeping GPU stays asleep.
/// </summary>
public sealed class GpuAdaptersProbe : IProbe
{
    internal const string RowId = "gpu.adapters";
    private const string Title = "GPU adapters keyed by LUID";
    private const string Source = "DXGI EnumAdapters1 (documented, via Vortice.DXGI) + QueryDisplayConfig for display ownership";

    private readonly IGpuAdapterSource _adapters;
    private readonly IDisplayConfigSource _displays;

    public GpuAdaptersProbe()
        : this(new DxgiAdapterSource(), new DisplayConfigSource())
    {
    }

    internal GpuAdaptersProbe(IGpuAdapterSource adapters, IDisplayConfigSource displays)
    {
        _adapters = adapters;
        _displays = displays;
    }

    public string Id => "gpu";

    public IReadOnlyList<CapabilityEntry> Run() => [Row(_adapters.Enumerate(), _displays.Read())];

    internal static CapabilityEntry Row(AdapterEnumeration adapters, DisplayConfigReading displays)
    {
        if (adapters.Adapters.Count == 0)
        {
            return adapters.Failure is { } why
                ? CapabilityEntry.Unavailable(RowId, Title, CapabilityTier.A, Availability.Unknown, Source, why)
                : CapabilityEntry.Unavailable(RowId, Title, CapabilityTier.A, Availability.NotAvailable, Source, "DXGI reports no graphics adapter");
        }

        IReadOnlyDictionary<Luid, int>? driven = null;
        if (displays.Failure is null)
        {
            driven = DisplayTopologyAnalyzer.Analyze(displays.Paths).Outputs
                .GroupBy(o => o.DrivingAdapter)
                .ToDictionary(g => g.Key, g => g.Count());
        }

        var notes = new List<string>();
        if (adapters.Failure is { } partial)
        {
            notes.Add($"the list may be incomplete: {partial}");
        }

        if (displays.Failure is { } displayWhy)
        {
            notes.Add($"display ownership not stated: {displayWhy}");
        }

        return CapabilityEntry.Available(RowId, Title, CapabilityTier.A, Source, GpuAdapterReport.Describe(adapters.Adapters, driven),
            DataProvenance.Measured, notes.Count == 0 ? null : string.Join("; ", notes));
    }
}

/// <summary>Active displays with refresh rate per monitor, mixed-refresh detection and the adapter that drives each one; VRR and HDR are stated as not available with the reason.</summary>
public sealed class DisplayTopologyProbe : IProbe
{
    internal const string RowId = "display.topology";
    internal const string HdrRowId = "display.hdr";
    internal const string VrrRowId = "display.vrr";
    private const string Title = "Displays, refresh rates, VRR";
    private const string Source = "QueryDisplayConfig + DisplayConfigGetDeviceInfo (documented CCD API)";

    internal const string VrrReason =
        "Windows documents no per-monitor query for the variable-refresh-rate state (the display configuration API and DXGI expose only whether tearing presents are allowed); " +
        "the user setting is informally documented and is not read here.";

    internal const string HdrReason =
        "the documented query reports advanced color, which covers HDR and wide color gamut together; the advanced color state is part of display.topology and is never called HDR.";

    private readonly IDisplayConfigSource _source;

    public DisplayTopologyProbe()
        : this(new DisplayConfigSource())
    {
    }

    internal DisplayTopologyProbe(IDisplayConfigSource source) => _source = source;

    public string Id => "display";

    public IReadOnlyList<CapabilityEntry> Run() => Rows(_source.Read());

    internal static IReadOnlyList<CapabilityEntry> Rows(DisplayConfigReading reading) =>
    [
        Topology(reading),
        CapabilityEntry.Unavailable(HdrRowId, "HDR state", CapabilityTier.A, Availability.NotAvailable, Source, HdrReason),
        CapabilityEntry.Unavailable(VrrRowId, "Variable refresh rate (VRR) state", CapabilityTier.A, Availability.NotAvailable, Source, VrrReason),
    ];

    private static CapabilityEntry Topology(DisplayConfigReading reading)
    {
        if (reading.Failure is { } why)
        {
            return CapabilityEntry.Unavailable(RowId, Title, CapabilityTier.A, Availability.Unknown, Source, why);
        }

        var topology = DisplayTopologyAnalyzer.Analyze(reading.Paths, path =>
            reading.AdvancedColor.TryGetValue((path.TargetAdapter, path.TargetId), out var state) ? state : AdvancedColorState.Unknown);
        if (topology.Outputs.Count == 0)
        {
            return CapabilityEntry.Unavailable(RowId, Title, CapabilityTier.A, Availability.NotAvailable, Source,
                "no active display path (a headless or remote session, or all displays are off)");
        }

        return CapabilityEntry.Available(RowId, Title, CapabilityTier.A, Source, topology.Describe(), DataProvenance.Measured,
            reading.Problems.Count == 0 ? null : "notes: " + string.Join("; ", reading.Problems));
    }

    /// <summary>The topology itself, for callers that need numbers; null when the configuration cannot be read.</summary>
    public static DisplayTopology? ReadTopology()
    {
        var reading = new DisplayConfigSource().Read();
        return reading.Failure is null
            ? DisplayTopologyAnalyzer.Analyze(reading.Paths, path => reading.AdvancedColor.TryGetValue((path.TargetAdapter, path.TargetId), out var state) ? state : AdvancedColorState.Unknown)
            : null;
    }
}

/// <summary>
/// Virtual machine detection from firmware strings matched against catalogs/vm_markers.json. The row value is exactly
/// <see cref="ManagedValues.VmNone"/> or <see cref="ManagedValues.VmPrefix"/> plus the catalog display name, the forms the optimize-mode gate
/// accepts (HC-11); anything unclear is an unavailable row, never "not a virtual machine".
/// </summary>
public sealed class VirtualMachineProbe : IProbe
{
    internal const string RowId = "environment.virtual_machine";
    private const string Title = "Virtual machine detection";
    private const string Source = "WMI Win32_ComputerSystem, Win32_BaseBoard and Win32_BIOS (documented) matched against catalogs/vm_markers.json";

    private readonly IReadOnlyList<VmMarkerEntry> _markers;
    private readonly IFirmwareStringSource _source;

    public VirtualMachineProbe(CatalogSet catalogs)
        : this(catalogs.VmMarkers, new WmiFirmwareStringSource())
    {
    }

    internal VirtualMachineProbe(IReadOnlyList<VmMarkerEntry> markers, IFirmwareStringSource source)
    {
        _markers = markers;
        _source = source;
    }

    public string Id => "environment";

    public IReadOnlyList<CapabilityEntry> Run() => [Row(_markers, _source.Read())];

    internal static CapabilityEntry Row(IReadOnlyList<VmMarkerEntry> markers, FirmwareReading reading)
    {
        if (reading.Failure is { } why)
        {
            return CapabilityEntry.Unavailable(RowId, Title, CapabilityTier.A, Availability.Unknown, Source, why);
        }

        var detection = VmDetector.Detect(markers, reading.Strings);
        return detection.Verdict switch
        {
            VmVerdict.Virtual => CapabilityEntry.Available(RowId, Title, CapabilityTier.A, Source,
                ManagedValues.VmPrefix + detection.Name, DataProvenance.Inferred, detection.Reason),
            VmVerdict.NotVirtual => CapabilityEntry.Available(RowId, Title, CapabilityTier.A, Source,
                ManagedValues.VmNone, DataProvenance.Inferred, detection.Reason),
            _ => CapabilityEntry.Unavailable(RowId, Title, CapabilityTier.A, Availability.Unknown, Source, detection.Reason),
        };
    }
}
