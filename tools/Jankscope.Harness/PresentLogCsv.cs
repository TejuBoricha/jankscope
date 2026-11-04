using System.Globalization;
using System.Text;

namespace Jankscope.Harness;

/// <summary>What the harness knows about the swap chain and process, for the constant columns of the log.</summary>
internal sealed record PresentLogContext(string Application, int ProcessId, string? SwapChainAddress, int PresentFlags, bool AllowsTearing, int SyncInterval, long Frequency)
{
    public static PresentLogContext For(HarnessRunResult result, string application, int processId) =>
        new(application, processId, result.SwapChain.Address, result.SwapChain.PresentFlags, result.SwapChain.AllowsTearing, result.SwapChain.SyncInterval, result.Frequency);
}

/// <summary>
/// Writes the harness's own present log in the shape of PresentMon 2.6.0 console output: the same 28 columns, in the same order, with the same
/// separators and the literal <c>NA</c> for everything the harness cannot truthfully provide. Five harness columns follow; a tolerant
/// PresentMon parser ignores columns it does not know.
/// <list type="bullet">
/// <item><c>TimeInMs</c> is the harness's own QPC at Present() return (PresentMon's value is taken from its ETW trace, so the two are not interchangeable).</item>
/// <item><c>MsBetweenPresents</c> is the difference of consecutive <c>TimeInMs</c> values; frame 0 has no earlier present, so its value is measured from the pacing origin and equals its <c>TimeInMs</c>.</item>
/// <item><c>MsInPresentAPI</c> is the time spent inside the Present() call, measured around it.</item>
/// <item><c>PresentMode</c> is NA: which composition path the OS chose cannot be observed from inside the application.</item>
/// </list>
/// </summary>
internal static class PresentLogCsv
{
    public const string NotAvailable = "NA";

    public static IReadOnlyList<string> PresentMonColumns { get; } =
    [
        "Application", "ProcessID", "SwapChainAddress", "PresentRuntime", "SyncInterval", "PresentFlags", "AllowsTearing", "PresentMode",
        "TimeInMs", "MsBetweenSimulationStart", "MsBetweenPresents", "MsBetweenDisplayChange", "MsInPresentAPI", "MsRenderPresentLatency",
        "MsUntilDisplayed", "CPUStartTimeInMs", "MsBetweenAppStart", "MsCPUBusy", "MsCPUWait", "MsGPULatency", "MsGPUTime", "MsGPUBusy",
        "MsGPUWait", "MsAnimationError", "AnimationTime", "MsFlipDelay", "MsAllInputToPhotonLatency", "MsClickToPhotonLatency",
    ];

    // Declared after PresentMonColumns on purpose: static initializers run in textual order.
    private static readonly int TimeInMsColumn = ColumnIndex("TimeInMs");
    private static readonly int MsBetweenPresentsColumn = ColumnIndex("MsBetweenPresents");
    private static readonly int MsInPresentApiColumn = ColumnIndex("MsInPresentAPI");

    /// <summary>Trailing columns. They tag every row Simulated and carry the ground truth and the raw ticks; a frame-log parser ignores them.</summary>
    public static IReadOnlyList<string> HarnessColumns { get; } =
        ["JankscopeProvenance", "JankscopeFrameIndex", "JankscopeScheduledMs", "JankscopeInjectedExtraMs", "JankscopePresentQpcTicks"];

    public static void Write(TextWriter writer, PresentLogContext context, IReadOnlyList<FrameRecord> records)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(records);

        var inv = CultureInfo.InvariantCulture;
        var frequency = context.Frequency;
        var fixedColumns = new Dictionary<string, string>
        {
            ["Application"] = Field(context.Application),
            ["ProcessID"] = context.ProcessId.ToString(inv),
            ["SwapChainAddress"] = context.SwapChainAddress is null ? NotAvailable : Field(context.SwapChainAddress),
            ["PresentRuntime"] = "DXGI",
            ["SyncInterval"] = context.SyncInterval.ToString(inv),
            ["PresentFlags"] = context.PresentFlags.ToString(inv),
            ["AllowsTearing"] = context.AllowsTearing ? "1" : "0",
        };

        writer.Write(string.Join(',', PresentMonColumns.Concat(HarnessColumns)));
        writer.Write("\r\n");

        var previous = 0L;
        var values = new string[PresentMonColumns.Count + HarnessColumns.Count];
        foreach (var record in records)
        {
            for (var c = 0; c < PresentMonColumns.Count; c++)
            {
                values[c] = fixedColumns.TryGetValue(PresentMonColumns[c], out var fixedValue) ? fixedValue : NotAvailable;
            }

            values[TimeInMsColumn] = Ms(record.PresentTicks, frequency);
            values[MsBetweenPresentsColumn] = Ms(record.PresentTicks - previous, frequency);
            values[MsInPresentApiColumn] = Ms(record.PresentCallTicks, frequency);

            var h = PresentMonColumns.Count;
            values[h] = "Simulated";
            values[h + 1] = record.Index.ToString(inv);
            values[h + 2] = Ms(record.ScheduledTicks, frequency);
            values[h + 3] = record.InjectedExtraMs.ToString("F4", inv);
            values[h + 4] = record.PresentTicks.ToString(inv);

            writer.Write(string.Join(',', values));
            writer.Write("\r\n");
            previous = record.PresentTicks;
        }
    }

    /// <summary>Writes the log as UTF-8 without a byte-order mark, like PresentMon.</summary>
    public static void WriteFile(string path, PresentLogContext context, IReadOnlyList<FrameRecord> records)
    {
        using var writer = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Write(writer, context, records);
    }

    private static int ColumnIndex(string column)
    {
        for (var i = 0; i < PresentMonColumns.Count; i++)
        {
            if (PresentMonColumns[i] == column)
            {
                return i;
            }
        }

        throw new ArgumentException("Unknown column " + column, nameof(column));
    }

    private static string Ms(long ticks, long frequency) => QpcTime.ToMs(ticks, frequency).ToString("F4", CultureInfo.InvariantCulture);

    /// <summary>RFC 4180 quoting: a field with a comma, quote or line break is quoted and its quotes doubled.</summary>
    private static string Field(string value) =>
        value.AsSpan().IndexOfAny(",\"\r\n") >= 0 ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
}
