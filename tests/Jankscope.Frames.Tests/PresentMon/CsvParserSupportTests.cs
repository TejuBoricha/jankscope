using System.Globalization;
using System.Text;
using Jankscope.Frames.PresentMon;

namespace Jankscope.Frames.Tests.PresentMon;

/// <summary>
/// Shared helpers for the PresentMon CSV parser tests (test plan row 3). The file name ends in "Tests.cs" only because the
/// package scope allows nothing else under this folder; the class below contains no tests of its own except the sanity checks
/// that keep the helpers honest.
/// </summary>
internal static class CsvTestData
{
    /// <summary>The 28-column header of PresentMon 2.6.0 (tests/fixtures/presentmon/README.md).</summary>
    public const string RealHeader =
        "Application,ProcessID,SwapChainAddress,PresentRuntime,SyncInterval,PresentFlags,AllowsTearing,PresentMode,TimeInMs,MsBetweenSimulationStart," +
        "MsBetweenPresents,MsBetweenDisplayChange,MsInPresentAPI,MsRenderPresentLatency,MsUntilDisplayed,CPUStartTimeInMs,MsBetweenAppStart,MsCPUBusy," +
        "MsCPUWait,MsGPULatency,MsGPUTime,MsGPUBusy,MsGPUWait,MsAnimationError,AnimationTime,MsFlipDelay,MsAllInputToPhotonLatency,MsClickToPhotonLatency";

    /// <summary>The PresentMon 1.x style header (<c>--v1_metrics</c>) named in the package prompt.</summary>
    public const string V1Header =
        "Application,ProcessID,SwapChainAddress,Runtime,SyncInterval,PresentFlags,AllowsTearing,PresentMode,Dropped,TimeInSeconds,msInPresentAPI," +
        "msBetweenPresents,msBetweenDisplayChange,msUntilRenderComplete,msUntilDisplayed";

    /// <summary>Eight columns, one per <see cref="FrameSample"/> field, with a row whose values are all different (so a mixed-up column shows).</summary>
    public const string FullHeader = "TimeInMs,MsBetweenPresents,MsBetweenDisplayChange,MsCPUBusy,MsGPUBusy,PresentMode,Application,ProcessID";

    public const string FullRow = "100.5,6.25,9.5,1.25,3.5,Hardware Composed: Independent Flip,Game.exe,4242";

    public static readonly FrameSample FullRowSample = new(
        Index: 0, TimestampMs: 100.5, FrameTimeMs: 6.25, DisplayedFrameTimeMs: 9.5, CpuBusyMs: 1.25, GpuBusyMs: 3.5,
        PresentMode: "Hardware Composed: Independent Flip", Application: "Game.exe", ProcessId: 4242, Provenance: Jankscope.Core.DataProvenance.Measured);

    public static string FixturePath(string fileName) => Path.Combine(AppContext.BaseDirectory, "fixtures", "presentmon", fileName);

    public static string ReadFixture(string fileName) => File.ReadAllText(FixturePath(fileName), new UTF8Encoding(false));

    /// <summary>A parser whose header is already read and supported.</summary>
    public static PresentMonCsvParser ParserFor(string header)
    {
        var parser = new PresentMonCsvParser();
        var result = parser.ReadHeader(header);
        Assert.True(result.Supported, result.UnsupportedReason);
        return parser;
    }

    public static FrameSample Ok(PresentMonCsvParser parser, string line)
    {
        Assert.True(parser.TryParseRow(line, out var sample, out var problem), problem);
        Assert.Null(problem);
        return sample;
    }

    /// <summary>Asserts that the row is rejected and returns the problem text.</summary>
    public static string Bad(PresentMonCsvParser parser, string line)
    {
        Assert.False(parser.TryParseRow(line, out _, out var problem), $"row was accepted: {line}");
        Assert.False(string.IsNullOrWhiteSpace(problem), "a rejected row must say why");
        return problem!;
    }

    public static IReadOnlyList<FrameSample> ReadAllText(string text, out LogHeaderResult header, out IReadOnlyList<string> problems)
    {
        using var reader = new StringReader(text);
        return PresentMonCsv.ReadAll(reader, out header, out problems);
    }

    /// <summary>Runs <paramref name="action"/> with the given culture as both current culture and UI culture, restoring both afterwards.</summary>
    public static void WithCulture(string cultureName, Action action)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        var oldCulture = CultureInfo.CurrentCulture;
        var oldUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = oldCulture;
            CultureInfo.CurrentUICulture = oldUi;
        }
    }

    /// <summary>The real fixture read the plain way: split on commas (the file has no quoted fields), column positions found by header name.</summary>
    public sealed record RawLog(string[] Header, List<string[]> Rows)
    {
        public int Col(string name) => Array.IndexOf(Header, name);

        public double Number(string[] row, string name) => double.Parse(row[Col(name)], NumberStyles.Float, CultureInfo.InvariantCulture);

        public double? NumberOrNull(string[] row, string name) =>
            row[Col(name)] == "NA" ? null : double.Parse(row[Col(name)], NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    public static RawLog ReadRawFixture(string fileName)
    {
        var lines = ReadFixture(fileName).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var header = lines[0].TrimEnd('\r').Split(',');
        var rows = lines.Skip(1).Select(l => l.TrimEnd('\r').Split(',')).ToList();
        return new RawLog(header, rows);
    }
}

public class CsvParserSupportTests
{
    [Fact]
    public void The_helper_constants_describe_the_headers_they_claim_to()
    {
        Assert.Equal(28, CsvTestData.RealHeader.Split(',').Length);
        Assert.Equal(15, CsvTestData.V1Header.Split(',').Length);
        Assert.Equal(8, CsvTestData.FullHeader.Split(',').Length);
    }

    [Fact]
    public void The_real_fixture_starts_with_the_documented_header()
    {
        var firstLine = CsvTestData.ReadFixture("v2.6.0-real-sanitized.csv").Split('\n')[0].TrimEnd('\r');
        Assert.Equal(CsvTestData.RealHeader, firstLine);
    }

    [Fact]
    public void Synthetic_fixtures_are_named_as_such()
    {
        // Provenance label (HC-9): everything this package added next to the real capture carries "synthetic" in its file name.
        var dir = Path.GetDirectoryName(CsvTestData.FixturePath("v2.6.0-real-sanitized.csv"))!;
        var synthetic = Directory.GetFiles(dir, "synthetic-*.csv");
        Assert.NotEmpty(synthetic);
        foreach (var name in new[] { "synthetic-v1-metrics.csv", "synthetic-reordered-extra-columns.csv", "synthetic-missing-optional.csv",
                     "synthetic-missing-frame-time.csv", "synthetic-missing-timestamp.csv", "synthetic-quoted-and-unknown.csv",
                     "synthetic-truncated-last-line.csv", "synthetic-frame-generation.csv" })
        {
            Assert.True(File.Exists(CsvTestData.FixturePath(name)), name);
        }
    }
}
