using System.Buffers.Binary;
using System.Globalization;

namespace Jankscope.Core.Hardware;

/// <summary>A vertical refresh rate as the display configuration API gives it: a rational number. A zero denominator is "unknown".</summary>
public readonly record struct RationalHz(uint Numerator, uint Denominator)
{
    public double? Hz => Denominator == 0 ? null : (double)Numerator / Denominator;
}

/// <summary>One DISPLAYCONFIG_PATH_INFO, reduced to the facts this code reads.</summary>
public sealed record DisplayPath(
    Luid SourceAdapter,
    uint SourceId,
    Luid TargetAdapter,
    uint TargetId,
    uint OutputTechnology,
    RationalHz Refresh,
    bool TargetAvailable,
    bool IsActive);

public sealed record DisplayPathParseResult(IReadOnlyList<DisplayPath> Paths, IReadOnlyList<string> Problems);

/// <summary>
/// State of "advanced color", which Windows reports for HDR and wide color gamut together; the documented query used here cannot
/// tell the two apart, so this is never presented as "HDR".
/// </summary>
public enum AdvancedColorState
{
    Unknown,
    NotSupported,
    SupportedNotEnabled,
    Enabled,
}

/// <summary>
/// Pure parsers for the documented display configuration structures (wingdi.h, 64-bit layout). A path is 72 bytes:
/// source info (20: adapter LUID, id, mode index, status), target info (48: adapter LUID, id, mode index, output technology,
/// rotation, scaling, refresh rate as numerator and denominator, scan-line ordering, availability, status) and the path flags.
/// </summary>
public static class DisplayConfigParser
{
    public const int PathInfoSize = 72;
    public const int AdvancedColorInfoSize = 32;

    private const int SourceAdapterOffset = 0;
    private const int SourceIdOffset = 8;
    private const int TargetOffset = 20;
    private const int TargetAdapterOffset = TargetOffset;
    private const int TargetIdOffset = TargetOffset + 8;
    private const int TargetTechnologyOffset = TargetOffset + 16;
    private const int TargetRefreshOffset = TargetOffset + 28;
    private const int TargetAvailableOffset = TargetOffset + 40;
    private const int PathFlagsOffset = 68;
    private const uint PathActive = 0x1;
    private const int AdvancedColorInfoType = 9; // DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO

    public static DisplayPathParseResult ParsePaths(ReadOnlySpan<byte> buffer, int pathCount)
    {
        var paths = new List<DisplayPath>();
        var problems = new List<string>();
        var available = buffer.Length / PathInfoSize;
        var count = Math.Min(Math.Max(pathCount, 0), available);
        if (pathCount > available)
        {
            problems.Add($"the buffer holds {available} complete display path(s) but {pathCount} were reported");
        }

        for (var i = 0; i < count; i++)
        {
            var path = buffer.Slice(i * PathInfoSize, PathInfoSize);
            paths.Add(new DisplayPath(
                ReadLuid(path[SourceAdapterOffset..]),
                BinaryPrimitives.ReadUInt32LittleEndian(path[SourceIdOffset..]),
                ReadLuid(path[TargetAdapterOffset..]),
                BinaryPrimitives.ReadUInt32LittleEndian(path[TargetIdOffset..]),
                BinaryPrimitives.ReadUInt32LittleEndian(path[TargetTechnologyOffset..]),
                new RationalHz(
                    BinaryPrimitives.ReadUInt32LittleEndian(path[TargetRefreshOffset..]),
                    BinaryPrimitives.ReadUInt32LittleEndian(path[(TargetRefreshOffset + 4)..])),
                BinaryPrimitives.ReadInt32LittleEndian(path[TargetAvailableOffset..]) != 0,
                (BinaryPrimitives.ReadUInt32LittleEndian(path[PathFlagsOffset..]) & PathActive) != 0));
        }

        return new DisplayPathParseResult(paths, problems);
    }

    private static Luid ReadLuid(ReadOnlySpan<byte> bytes) =>
        new(BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]), BinaryPrimitives.ReadUInt32LittleEndian(bytes));

    /// <summary>Fills the 32-byte request of DisplayConfigGetDeviceInfo for the advanced color state of one target.</summary>
    public static void WriteAdvancedColorRequest(Span<byte> request, Luid targetAdapter, uint targetId)
    {
        if (request.Length < AdvancedColorInfoSize)
        {
            throw new ArgumentException($"the request buffer must hold {AdvancedColorInfoSize} bytes", nameof(request));
        }

        request[..AdvancedColorInfoSize].Clear();
        BinaryPrimitives.WriteInt32LittleEndian(request, AdvancedColorInfoType);
        BinaryPrimitives.WriteInt32LittleEndian(request[4..], AdvancedColorInfoSize);
        BinaryPrimitives.WriteUInt32LittleEndian(request[8..], targetAdapter.Low);
        BinaryPrimitives.WriteInt32LittleEndian(request[12..], targetAdapter.High);
        BinaryPrimitives.WriteUInt32LittleEndian(request[16..], targetId);
    }

    /// <summary>Reads the reply to <see cref="WriteAdvancedColorRequest"/>: bit 0 = supported, bit 1 = enabled. Anything unexpected is Unknown.</summary>
    public static AdvancedColorState ParseAdvancedColor(ReadOnlySpan<byte> reply)
    {
        if (reply.Length < AdvancedColorInfoSize || BinaryPrimitives.ReadInt32LittleEndian(reply) != AdvancedColorInfoType)
        {
            return AdvancedColorState.Unknown;
        }

        var flags = BinaryPrimitives.ReadUInt32LittleEndian(reply[20..]);
        var supported = (flags & 0x1) != 0;
        var enabled = (flags & 0x2) != 0;
        return supported
            ? enabled ? AdvancedColorState.Enabled : AdvancedColorState.SupportedNotEnabled
            : enabled ? AdvancedColorState.Unknown : AdvancedColorState.NotSupported;
    }
}

/// <summary>DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY values (wingdi.h) as words.</summary>
public static class DisplayOutputTechnology
{
    public static string Describe(uint code) => code switch
    {
        0x80000000 => "internal",
        0xFFFFFFFF => "other",
        0 => "VGA (HD15)",
        1 => "S-Video",
        2 => "composite video",
        3 => "component video",
        4 => "DVI",
        5 => "HDMI",
        6 => "LVDS",
        8 => "D-Jpn",
        9 => "SDI",
        10 => "DisplayPort (external)",
        11 => "DisplayPort (embedded)",
        12 => "UDI (external)",
        13 => "UDI (embedded)",
        14 => "SDTV dongle",
        15 => "Miracast",
        16 => "indirect wired",
        17 => "indirect virtual",
        18 => "DisplayPort over USB tunnel",
        _ => string.Create(CultureInfo.InvariantCulture, $"unrecognized ({code})"),
    };

    /// <summary>True for outputs that are a built-in panel's own connection (internal, LVDS, embedded DisplayPort or UDI).</summary>
    public static bool IsInternal(uint code) => code is 0x80000000 or 6 or 11 or 13;
}

public sealed record DisplayOutput(
    Luid DrivingAdapter,
    Luid SourceAdapter,
    uint TargetId,
    RationalHz Refresh,
    double? RefreshHz,
    string Technology,
    bool IsInternal,
    AdvancedColorState AdvancedColor);

/// <param name="MixedRefresh">True when active displays run at different nominal rates, false when they agree or there is one display, null when it cannot be told (no display, or a rate is unknown and the known ones agree).</param>
public sealed record DisplayTopology(
    IReadOnlyList<DisplayOutput> Outputs,
    bool? MixedRefresh,
    double? MinRefreshHz,
    double? MaxRefreshHz,
    IReadOnlyList<Luid> DrivingAdapters)
{
    public string Describe()
    {
        var inv = CultureInfo.InvariantCulture;
        var displays = Outputs.Select(o =>
        {
            var rate = o.RefreshHz is { } hz ? string.Create(inv, $"{hz:0.##} Hz") : "refresh rate unknown";
            return string.Create(inv, $"[adapter {o.DrivingAdapter}: {rate}, {o.Technology}, {AdvancedColorText(o.AdvancedColor)}]");
        });
        var mixed = MixedRefresh switch { true => "yes", false => "no", null => "unknown" };
        return string.Create(inv, $"{Outputs.Count} active display(s): {string.Join("; ", displays)}; mixed refresh rates: {mixed}");
    }

    private static string AdvancedColorText(AdvancedColorState state) => state switch
    {
        AdvancedColorState.Enabled => "advanced color (HDR or wide color gamut) on",
        AdvancedColorState.SupportedNotEnabled => "advanced color (HDR or wide color gamut) supported, off",
        AdvancedColorState.NotSupported => "advanced color (HDR or wide color gamut) not supported",
        _ => "advanced color state unknown",
    };
}

public static class DisplayTopologyAnalyzer
{
    /// <summary>Two refresh rates closer than this (inclusive) are the same nominal rate: 60 and 59.94, 144 and 143.98.</summary>
    public const double MixedRefreshToleranceHz = 1.0;

    public static DisplayTopology Analyze(IEnumerable<DisplayPath> paths, Func<DisplayPath, AdvancedColorState>? advancedColor = null)
    {
        var outputs = new List<DisplayOutput>();
        var seen = new HashSet<(Luid, uint)>();
        foreach (var path in paths.Where(p => p.IsActive))
        {
            if (!seen.Add((path.TargetAdapter, path.TargetId)))
            {
                continue;
            }

            var hz = path.Refresh.Hz is { } v && v > 0 ? v : (double?)null;
            outputs.Add(new DisplayOutput(
                path.TargetAdapter,
                path.SourceAdapter,
                path.TargetId,
                path.Refresh,
                hz,
                DisplayOutputTechnology.Describe(path.OutputTechnology),
                DisplayOutputTechnology.IsInternal(path.OutputTechnology),
                advancedColor?.Invoke(path) ?? AdvancedColorState.Unknown));
        }

        outputs = outputs.OrderBy(o => o.DrivingAdapter).ThenBy(o => o.TargetId).ToList();
        var known = outputs.Where(o => o.RefreshHz is not null).Select(o => o.RefreshHz!.Value).ToList();
        double? min = known.Count == 0 ? null : known.Min();
        double? max = known.Count == 0 ? null : known.Max();

        bool? mixed;
        if (outputs.Count == 0)
        {
            mixed = null;
        }
        else if (outputs.Count == 1)
        {
            mixed = false;
        }
        else if (known.Count >= 2 && max - min > MixedRefreshToleranceHz)
        {
            mixed = true;
        }
        else
        {
            mixed = known.Count == outputs.Count ? false : null;
        }

        return new DisplayTopology(outputs, mixed, min, max, outputs.Select(o => o.DrivingAdapter).Distinct().Order().ToList());
    }
}
