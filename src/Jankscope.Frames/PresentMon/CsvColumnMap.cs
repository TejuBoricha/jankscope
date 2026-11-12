using System.Globalization;

namespace Jankscope.Frames.PresentMon;

/// <summary>
/// Which header column feeds which <see cref="FrameSample"/> field, found by column NAME (case-insensitive, any order), plus the
/// <see cref="LogHeaderResult"/> that describes the header to the caller. A column index of -1 means "not in this log".
/// </summary>
internal sealed class CsvColumnMap
{
    // Canonical spellings (PresentMon 2.x). PresentMon 1.x writes the frame-time columns with a lower-case "ms" prefix; matching ignores case.
    public const string TimeInMs = "TimeInMs";
    public const string TimeInSeconds = "TimeInSeconds";
    public const string FrameTime = "MsBetweenPresents";
    public const string DisplayTime = "MsBetweenDisplayChange";
    public const string CpuBusy = "MsCPUBusy";
    public const string GpuBusy = "MsGPUBusy";
    public const string PresentMode = "PresentMode";
    public const string Application = "Application";
    public const string ProcessId = "ProcessID";

    /// <summary>
    /// Which swap chain a row belongs to (an address such as <c>0x103E8</c>). Frame times are measured per swap chain, so a program that presents on two (a game and an
    /// overlay, say) writes two interleaved series. Read when present; not an "optional column" that is reported as missing, since a log without it is complete as before.
    /// </summary>
    public const string SwapChainAddress = "SwapChainAddress";

    /// <summary>Written by the synthetic harness on every row (never by PresentMon): the row is simulated. Not an optional PresentMon column, so it is never reported as missing.</summary>
    public const string Provenance = "JankscopeProvenance";

    /// <summary>More columns than this is not a PresentMon log (2.6.0 writes 28) and would only size buffers for nothing.</summary>
    public const int MaxColumns = 256;

    private const char ByteOrderMark = (char)0xFEFF;

    private enum Role
    {
        None,
        TimeMs,
        TimeSeconds,
        FrameTime,
        Display,
        CpuBusy,
        GpuBusy,
        PresentMode,
        Application,
        ProcessId,
        SwapChain,
        Provenance,
    }

    private static readonly (string Name, Role Role)[] Mapped =
    [
        (TimeInMs, Role.TimeMs),
        (TimeInSeconds, Role.TimeSeconds),
        (FrameTime, Role.FrameTime),
        (DisplayTime, Role.Display),
        (CpuBusy, Role.CpuBusy),
        (GpuBusy, Role.GpuBusy),
        (PresentMode, Role.PresentMode),
        (Application, Role.Application),
        (ProcessId, Role.ProcessId),
        (SwapChainAddress, Role.SwapChain),
        (Provenance, Role.Provenance),
    ];

    /// <summary>Optional columns, in the order they are reported when missing.</summary>
    private static readonly (string Name, Role Role)[] Optional =
    [
        (DisplayTime, Role.Display),
        (CpuBusy, Role.CpuBusy),
        (GpuBusy, Role.GpuBusy),
        (PresentMode, Role.PresentMode),
        (Application, Role.Application),
        (ProcessId, Role.ProcessId),
    ];

    private static readonly Dictionary<string, Role> RoleByName = Mapped.ToDictionary(m => m.Name, m => m.Role, StringComparer.OrdinalIgnoreCase);

    // Columns that exist in only one generation. They tell the generations apart when the timestamp column is missing.
    private static readonly HashSet<string> OnlyIn1X = new(StringComparer.OrdinalIgnoreCase)
    {
        TimeInSeconds, "Runtime", "Dropped", "MsUntilRenderComplete",
    };

    private static readonly HashSet<string> OnlyIn2X = new(StringComparer.OrdinalIgnoreCase)
    {
        TimeInMs, "PresentRuntime", "MsBetweenSimulationStart", "CPUStartTimeInMs", "MsBetweenAppStart", "MsRenderPresentLatency", CpuBusy, GpuBusy, "MsGPUTime",
    };

    private CsvColumnMap(LogHeaderResult result)
    {
        Result = result;
    }

    public LogHeaderResult Result { get; }

    public int FieldCount { get; private init; }

    public int Timestamp { get; private init; } = -1;

    /// <summary>True when the timestamp column is PresentMon 1.x <c>TimeInSeconds</c>; the value is converted to milliseconds.</summary>
    public bool TimestampIsSeconds { get; private init; }

    public string TimestampName => TimestampIsSeconds ? TimeInSeconds : TimeInMs;

    public int FrameTimeColumn { get; private init; } = -1;

    public int DisplayColumn { get; private init; } = -1;

    public int CpuBusyColumn { get; private init; } = -1;

    public int GpuBusyColumn { get; private init; } = -1;

    public int PresentModeColumn { get; private init; } = -1;

    public int ApplicationColumn { get; private init; } = -1;

    public int ProcessIdColumn { get; private init; } = -1;

    public int SwapChainColumn { get; private init; } = -1;

    public int ProvenanceColumn { get; private init; } = -1;

    private static CsvColumnMap Unsupported(string reason, FrameLogFormat format = FrameLogFormat.Unknown, IReadOnlyList<string>? missingOptional = null, IReadOnlyList<string>? ignored = null) =>
        new(new LogHeaderResult(false, format, "unsupported log: " + reason, missingOptional ?? [], ignored ?? []));

    public static CsvColumnMap Analyze(string? headerLine)
    {
        if (headerLine is null)
        {
            return Unsupported("the header line is empty (no header line was provided)");
        }

        var line = headerLine.AsSpan().TrimStart(ByteOrderMark).TrimEnd("\r\n");
        if (line.IsWhiteSpace())
        {
            return Unsupported("the header line is empty");
        }

        if (line.Length > CsvLineSplitter.MaxLineLength)
        {
            return Unsupported("the header line is too long to be a PresentMon header");
        }

        var bad = line.IndexOfAny('\0', '\r', '\n');
        if (bad >= 0)
        {
            return Unsupported(line[bad] == '\0' ? "the header line contains a NUL character" : "the header line contains an embedded line break");
        }

        var fields = new CsvField[MaxColumns];
        if (!CsvLineSplitter.TrySplit(line, fields, out var count, out var splitProblem))
        {
            return Unsupported("the header line is malformed (" + splitProblem + ")");
        }

        if (count > MaxColumns)
        {
            return Unsupported(string.Create(CultureInfo.InvariantCulture, $"the header has {count} columns; a PresentMon log has far fewer than {MaxColumns}"));
        }

        var first = new int[Enum.GetValues<Role>().Length];
        Array.Fill(first, -1);
        var duplicated = new List<string>();
        var ignored = new List<string>();
        var sawOnly1X = false;
        var sawOnly2X = false;

        for (var column = 0; column < count; column++)
        {
            var name = fields[column].Raw(line).ToString();
            if (fields[column].HasDoubledQuote)
            {
                name = name.Replace("\"\"", "\"", StringComparison.Ordinal);
            }

            name = name.Trim();

            sawOnly1X |= OnlyIn1X.Contains(name);
            sawOnly2X |= OnlyIn2X.Contains(name);

            if (!RoleByName.TryGetValue(name, out var role))
            {
                ignored.Add(name.Length == 0 ? string.Create(CultureInfo.InvariantCulture, $"(unnamed column {column + 1})") : name);
            }
            else if (first[(int)role] >= 0)
            {
                var canonical = Canonical(role);
                if (!duplicated.Contains(canonical))
                {
                    duplicated.Add(canonical);
                }
            }
            else
            {
                first[(int)role] = column;
            }
        }

        var hasMs = first[(int)Role.TimeMs] >= 0;
        var hasSeconds = first[(int)Role.TimeSeconds] >= 0;
        var format = (hasMs, hasSeconds, sawOnly1X, sawOnly2X) switch
        {
            (true, false, _, _) => FrameLogFormat.PresentMon2,
            (false, true, _, _) => FrameLogFormat.PresentMon1,
            (_, _, true, false) => FrameLogFormat.PresentMon1,
            (_, _, false, true) => FrameLogFormat.PresentMon2,
            _ => FrameLogFormat.Unknown,
        };

        var missingOptional = Optional.Where(o => first[(int)o.Role] < 0).Select(o => o.Name).ToArray();

        var problems = new List<string>();
        if (duplicated.Count > 0)
        {
            problems.Add("duplicate column(s) " + string.Join(", ", duplicated) + ", so the right one cannot be known");
        }

        if (hasMs && hasSeconds)
        {
            problems.Add($"both {TimeInMs} and {TimeInSeconds} are present, so the clock cannot be known");
        }

        var missingRequired = new List<string>();
        if (first[(int)Role.FrameTime] < 0)
        {
            missingRequired.Add(FrameTime);
        }

        if (!hasMs && !hasSeconds)
        {
            missingRequired.Add($"{TimeInMs} (PresentMon 2.x) or {TimeInSeconds} (PresentMon 1.x)");
        }

        if (missingRequired.Count > 0)
        {
            problems.Add("missing required column(s): " + string.Join(", ", missingRequired));
        }

        if (problems.Count > 0)
        {
            return Unsupported(string.Join("; ", problems), format, missingOptional, ignored);
        }

        var result = new LogHeaderResult(true, format, null, missingOptional, ignored);
        return new CsvColumnMap(result)
        {
            FieldCount = count,
            Timestamp = hasMs ? first[(int)Role.TimeMs] : first[(int)Role.TimeSeconds],
            TimestampIsSeconds = !hasMs,
            FrameTimeColumn = first[(int)Role.FrameTime],
            DisplayColumn = first[(int)Role.Display],
            CpuBusyColumn = first[(int)Role.CpuBusy],
            GpuBusyColumn = first[(int)Role.GpuBusy],
            PresentModeColumn = first[(int)Role.PresentMode],
            ApplicationColumn = first[(int)Role.Application],
            ProcessIdColumn = first[(int)Role.ProcessId],
            SwapChainColumn = first[(int)Role.SwapChain],
            ProvenanceColumn = first[(int)Role.Provenance],
        };
    }

    private static string Canonical(Role role) => Mapped.First(m => m.Role == role).Name;
}
