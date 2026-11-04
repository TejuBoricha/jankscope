using System.Globalization;
using Jankscope.Core;

namespace Jankscope.Frames.PresentMon;

/// <summary>
/// Turns the CSV that the pinned PresentMon console tool writes (2.x, or 1.x with <c>--v1_metrics</c>) into <see cref="FrameSample"/> values.
/// Columns are found by name (case-insensitive, any order); unknown columns are ignored and absent optional ones become <c>null</c>.
/// The literal <c>NA</c> and empty values are "not available" (<c>null</c>), never zero. Numbers are parsed with the invariant culture.
/// A malformed line is rejected with a reason and never throws. Nothing is guessed: if any column the parser reads holds something
/// that is not a valid value for it, the whole row is rejected rather than half-trusted.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="FrameSample.Index"/> is the position of the row among the data rows (blank lines excluded). A rejected row still uses
/// up its index, so a lost frame leaves a visible gap instead of silently making its neighbours look adjacent.
/// </para>
/// <para>
/// A line that was cut off by a crash is caught by the field count, a quote or a value that is not a number. The one cut that cannot
/// be told apart from a whole row is a cut inside the last column when every column the parser reads is intact; the values are
/// then exactly what the complete row would have given.
/// </para>
/// <para>Instances keep state (the header, the next index) and are not thread-safe.</para>
/// </remarks>
public sealed class PresentMonCsvParser : IFrameLogParser
{
    private const string NotAvailable = "NA";

    private readonly DataProvenance _sourceProvenance;
    private CsvColumnMap? _map;
    private CsvField[] _fields = [];
    private long _nextIndex;
    private string? _lastApplication;
    private string? _lastPresentMode;

    /// <summary>
    /// <paramref name="sourceProvenance"/> is what the caller knows about where the log came from (HC-9): <see cref="DataProvenance.Measured"/>
    /// for the pinned tool's own output, <see cref="DataProvenance.Fixture"/> for a test file. A row that carries the harness's
    /// <c>JankscopeProvenance</c> column can only make the claim weaker (Simulated), never stronger.
    /// </summary>
    public PresentMonCsvParser(DataProvenance sourceProvenance = DataProvenance.Measured)
    {
        if (sourceProvenance == DataProvenance.None)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceProvenance), "A log of frames is data: its provenance cannot be None.");
        }

        _sourceProvenance = sourceProvenance;
    }

    /// <summary>
    /// Reads the header line and starts a new capture (the next row is index 0). A leading byte order mark and a trailing line break
    /// are ignored. Never throws: an unusable header comes back with <see cref="LogHeaderResult.Supported"/> false and a reason.
    /// </summary>
    public LogHeaderResult ReadHeader(string headerLine)
    {
        _nextIndex = 0;
        _lastApplication = null;
        _lastPresentMode = null;
        _map = CsvColumnMap.Analyze(headerLine);
        _fields = _map.Result.Supported ? new CsvField[_map.FieldCount] : [];
        return _map.Result;
    }

    /// <summary>
    /// Parses one data line. Returns false with a <paramref name="problem"/> for a blank line, a line from before a usable header,
    /// or a malformed row; <paramref name="sample"/> is then <c>default</c> and must not be used.
    /// </summary>
    public bool TryParseRow(string line, out FrameSample sample, out string? problem)
    {
        sample = default;

        if (line is null)
        {
            problem = "the line is missing (null)";
            return false;
        }

        var map = _map;
        if (map is null)
        {
            problem = "no header has been read yet (call ReadHeader first)";
            return false;
        }

        if (!map.Result.Supported)
        {
            problem = "the log header is not supported: " + map.Result.UnsupportedReason;
            return false;
        }

        var text = line.AsSpan().TrimEnd("\r\n");
        if (text.IsWhiteSpace())
        {
            problem = "blank line";
            return false;
        }

        // From here on the line is a data row: it uses up an index whether or not it turns out to be usable.
        var index = _nextIndex++;

        if (text.Length > CsvLineSplitter.MaxLineLength)
        {
            problem = string.Create(CultureInfo.InvariantCulture, $"line is too long ({text.Length} characters)");
            return false;
        }

        var bad = text.IndexOfAny('\0', '\r', '\n');
        if (bad >= 0)
        {
            problem = text[bad] == '\0' ? "line contains a NUL character" : "line contains an embedded line break";
            return false;
        }

        if (!CsvLineSplitter.TrySplit(text, _fields, out var count, out problem))
        {
            return false;
        }

        if (count != map.FieldCount)
        {
            problem = string.Create(CultureInfo.InvariantCulture, $"expected {map.FieldCount} fields, found {count} (truncated or corrupt line?)");
            return false;
        }

        if (!TryReadTimestamp(text, map, out var timestampMs, out problem)
            || !TryReadFrameTime(text, map, out var frameTimeMs, out problem)
            || !TryReadOptionalMetric(text, map.DisplayColumn, CsvColumnMap.DisplayTime, out var displayedMs, out problem)
            || !TryReadOptionalMetric(text, map.CpuBusyColumn, CsvColumnMap.CpuBusy, out var cpuBusyMs, out problem)
            || !TryReadOptionalMetric(text, map.GpuBusyColumn, CsvColumnMap.GpuBusy, out var gpuBusyMs, out problem)
            || !TryReadProcessId(text, map.ProcessIdColumn, out var processId, out problem)
            || !TryReadProvenance(text, map.ProvenanceColumn, out var provenance, out problem))
        {
            return false;
        }

        var presentMode = ReadText(text, map.PresentModeColumn, ref _lastPresentMode);
        var application = ReadText(text, map.ApplicationColumn, ref _lastApplication);

        sample = new FrameSample(index, timestampMs, frameTimeMs, displayedMs, cpuBusyMs, gpuBusyMs, presentMode, application, processId, provenance);
        problem = null;
        return true;
    }

    private bool TryReadTimestamp(ReadOnlySpan<char> text, CsvColumnMap map, out double timestampMs, out string? problem)
    {
        var name = map.TimestampName;
        timestampMs = 0;
        switch (ReadNumber(text, _fields[map.Timestamp], name, out var value, out problem))
        {
            case Cell.NotAvailable:
                problem = name + " is not available (NA or empty)";
                return false;
            case Cell.Bad:
                return false;
        }

        if (map.TimestampIsSeconds)
        {
            value *= 1000.0;
            if (!double.IsFinite(value))
            {
                problem = name + " is not a finite number once converted to milliseconds";
                return false;
            }
        }

        if (value < 0)
        {
            problem = name + " is negative";
            return false;
        }

        timestampMs = value == 0 ? 0.0 : value; // -0 becomes +0
        return true;
    }

    private bool TryReadFrameTime(ReadOnlySpan<char> text, CsvColumnMap map, out double frameTimeMs, out string? problem)
    {
        frameTimeMs = 0;
        switch (ReadNumber(text, _fields[map.FrameTimeColumn], CsvColumnMap.FrameTime, out var value, out problem))
        {
            case Cell.NotAvailable:
                problem = CsvColumnMap.FrameTime + " is not available (NA or empty)";
                return false;
            case Cell.Bad:
                return false;
        }

        if (value < 0)
        {
            problem = CsvColumnMap.FrameTime + " is negative";
            return false;
        }

        // Jankscope.Core.FrameStats treats a non-positive frame time as invalid input and answers NaN for the whole series,
        // so one zero must not get through and spoil every statistic that is computed from the log.
        if (value == 0)
        {
            problem = CsvColumnMap.FrameTime + " must be greater than zero";
            return false;
        }

        frameTimeMs = value;
        return true;
    }

    private bool TryReadOptionalMetric(ReadOnlySpan<char> text, int column, string name, out double? metricMs, out string? problem)
    {
        metricMs = null;
        problem = null;
        if (column < 0)
        {
            return true;
        }

        switch (ReadNumber(text, _fields[column], name, out var value, out problem))
        {
            case Cell.NotAvailable:
                return true;
            case Cell.Bad:
                return false;
        }

        if (value < 0)
        {
            problem = name + " is negative";
            return false;
        }

        metricMs = value == 0 ? 0.0 : value; // -0 (a tiny negative that rounded to zero) becomes +0
        return true;
    }

    private bool TryReadProcessId(ReadOnlySpan<char> text, int column, out int? processId, out string? problem)
    {
        processId = null;
        problem = null;
        if (column < 0)
        {
            return true;
        }

        var raw = _fields[column].Raw(text).Trim();
        if (raw.IsEmpty || raw.SequenceEqual(NotAvailable))
        {
            return true;
        }

        if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            problem = CsvColumnMap.ProcessId + " is not a valid process id: '" + Quote(raw) + "'";
            return false;
        }

        processId = id;
        return true;
    }

    /// <summary>
    /// The harness writes <c>Simulated</c> on every row of its own log. A log without the column keeps the caller's provenance; a column that holds
    /// anything else (empty, NA, an unknown word, or a claim of Measured) rejects the row: a provenance label is never guessed or upgraded.
    /// </summary>
    private bool TryReadProvenance(ReadOnlySpan<char> text, int column, out DataProvenance provenance, out string? problem)
    {
        provenance = _sourceProvenance;
        problem = null;
        if (column < 0)
        {
            return true;
        }

        var raw = _fields[column].Raw(text).Trim();
        DataProvenance claimed;
        if (raw.Equals(nameof(DataProvenance.Simulated), StringComparison.OrdinalIgnoreCase))
        {
            claimed = DataProvenance.Simulated;
        }
        else if (raw.Equals(nameof(DataProvenance.Fixture), StringComparison.OrdinalIgnoreCase))
        {
            claimed = DataProvenance.Fixture;
        }
        else
        {
            problem = CsvColumnMap.Provenance + " is not a recognized label (only Simulated or Fixture can be read from a log): '" + Quote(raw) + "'";
            return false;
        }

        provenance = ProvenanceRules.Combine(_sourceProvenance, claimed);
        return true;
    }

    private enum Cell
    {
        Value,
        NotAvailable,
        Bad,
    }

    private static Cell ReadNumber(ReadOnlySpan<char> text, CsvField field, string column, out double value, out string? problem)
    {
        value = 0;
        problem = null;
        var raw = field.Raw(text).Trim();
        if (raw.IsEmpty || raw.SequenceEqual(NotAvailable))
        {
            return Cell.NotAvailable;
        }

        // Float = optional sign, digits with a "." decimal point, optional exponent, surrounding spaces; no thousands separators.
        // Invariant culture, whatever the current culture is: PresentMon always writes ".".
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            problem = column + " is not a number: '" + Quote(raw) + "'";
            return Cell.Bad;
        }

        if (!double.IsFinite(value))
        {
            problem = column + " is not a finite number";
            return Cell.Bad;
        }

        return Cell.Value;
    }

    /// <summary>The text of a string column, or <c>null</c> when the cell is empty or <c>NA</c>. Repeated values share one instance.</summary>
    private string? ReadText(ReadOnlySpan<char> text, int column, ref string? cache)
    {
        if (column < 0)
        {
            return null;
        }

        var field = _fields[column];
        var raw = field.Raw(text);
        var trimmed = raw.Trim();
        if (trimmed.IsEmpty || trimmed.SequenceEqual(NotAvailable))
        {
            return null;
        }

        if (field.HasDoubledQuote)
        {
            return raw.ToString().Replace("\"\"", "\"", StringComparison.Ordinal);
        }

        if (cache is not null && raw.SequenceEqual(cache))
        {
            return cache;
        }

        cache = raw.ToString();
        return cache;
    }

    /// <summary>A short excerpt of cell text for a problem message (never the whole line).</summary>
    private static string Quote(ReadOnlySpan<char> raw) => raw.Length <= 24 ? raw.ToString() : raw[..24].ToString() + "...";
}

/// <summary>
/// Reads a whole PresentMon CSV from a <see cref="TextReader"/>, either into a list or as a lazy sequence that never holds more
/// than the current line. Both paths use <see cref="PresentMonCsvParser"/> line by line, so they give the same samples and problems.
/// </summary>
public static class PresentMonCsv
{
    /// <summary>At most this many problems are listed by <see cref="ReadAll"/>; the rest are only counted (a garbage file must not fill memory).</summary>
    public const int MaxListedProblems = 1_000;

    /// <summary>
    /// Parses every row. <paramref name="header"/> tells whether the log is supported (and why not); <paramref name="problems"/> lists rejected
    /// rows as <c>line N: reason</c> (physical line number, 1-based, counting blank lines), capped at <see cref="MaxListedProblems"/> with a final
    /// line saying how many more there were. Blank lines are skipped silently. An unsupported header gives no samples.
    /// </summary>
    /// <param name="reader">The log.</param>
    /// <param name="header">The header verdict.</param>
    /// <param name="problems">Rejected rows.</param>
    /// <param name="sourceProvenance">What the caller knows about the file (HC-9): Measured for the pinned tool's output (the default), Fixture for a test file. A harness log marks its own rows Simulated.</param>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is null. I/O errors from the reader are not caught.</exception>
    public static IReadOnlyList<FrameSample> ReadAll(TextReader reader, out LogHeaderResult header, out IReadOnlyList<string> problems, DataProvenance sourceProvenance = DataProvenance.Measured)
    {
        ArgumentNullException.ThrowIfNull(reader);

        LogHeaderResult? seen = null;
        var listed = new List<string>();
        var total = 0;
        var samples = new List<FrameSample>();

        foreach (var sample in ReadStreaming(
                     reader,
                     h => seen = h,
                     p =>
                     {
                         total++;
                         if (listed.Count < MaxListedProblems)
                         {
                             listed.Add(p);
                         }
                     },
                     sourceProvenance))
        {
            samples.Add(sample);
        }

        if (total > MaxListedProblems)
        {
            listed.Add(string.Create(CultureInfo.InvariantCulture, $"... and {total - MaxListedProblems} more problems not listed"));
        }

        header = seen!; // the sequence always reports a header, supported or not, before it ends
        problems = listed;
        return samples;
    }

    /// <summary>
    /// Lazily parses the log: nothing is read until the sequence is enumerated, and each row is read only when the caller asks for the
    /// next sample, so memory use does not grow with the file. <paramref name="onHeader"/> is called once, before the first sample (also for an
    /// unsupported log, which then yields nothing); <paramref name="onProblem"/> is called with <c>line N: reason</c> for every rejected row,
    /// uncapped. Both run on the enumerating thread. The reader is not disposed; the caller owns it. Enumerate once.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is null. I/O errors from the reader surface while enumerating.</exception>
    public static IEnumerable<FrameSample> ReadStreaming(TextReader reader, Action<LogHeaderResult>? onHeader = null, Action<string>? onProblem = null, DataProvenance sourceProvenance = DataProvenance.Measured)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _ = new PresentMonCsvParser(sourceProvenance); // an unusable provenance is refused now, not at the first enumeration
        return Enumerate(reader, onHeader, onProblem, sourceProvenance);
    }

    private static IEnumerable<FrameSample> Enumerate(TextReader reader, Action<LogHeaderResult>? onHeader, Action<string>? onProblem, DataProvenance sourceProvenance)
    {
        var parser = new PresentMonCsvParser(sourceProvenance);
        long lineNumber = 0;

        LogHeaderResult? header = null;
        while (header is null && reader.ReadLine() is { } headerLine)
        {
            lineNumber++;
            if (!headerLine.AsSpan().IsWhiteSpace())
            {
                header = parser.ReadHeader(headerLine);
            }
        }

        header ??= parser.ReadHeader(string.Empty); // nothing but blank lines: reported as an empty log
        onHeader?.Invoke(header);
        if (!header.Supported)
        {
            yield break;
        }

        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            if (line.AsSpan().IsWhiteSpace())
            {
                continue;
            }

            if (parser.TryParseRow(line, out var sample, out var problem))
            {
                yield return sample;
            }
            else
            {
                onProblem?.Invoke(string.Create(CultureInfo.InvariantCulture, $"line {lineNumber}: {problem}"));
            }
        }
    }
}
