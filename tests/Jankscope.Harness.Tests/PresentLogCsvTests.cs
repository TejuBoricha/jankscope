using System.Globalization;

namespace Jankscope.Harness.Tests;

public class PresentLogCsvTests
{
    private static readonly string[] ProvidedPresentMonColumns =
    [
        "Application", "ProcessID", "SwapChainAddress", "PresentRuntime", "SyncInterval", "PresentFlags", "AllowsTearing",
        "TimeInMs", "MsBetweenPresents", "MsInPresentAPI",
    ];

    private static string[] FixtureColumns()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "presentmon", "v2.6.0-real-sanitized.csv");
        return File.ReadLines(path).First().Split(',');
    }

    private static (string[] Header, List<string[]> Rows, string Text) Render(HarnessRunResult? result = null, PresentLogContext? context = null)
    {
        result ??= TestRuns.Fake();
        context ??= PresentLogContext.For(result, "jankscope-harness.exe", 4242);
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        PresentLogCsv.Write(writer, context, result.Records);
        var text = writer.ToString();
        var lines = text.Split("\r\n", StringSplitOptions.None);
        Assert.Equal(string.Empty, lines[^1]); // the file ends with a line break
        var rows = lines.Skip(1).Where(l => l.Length > 0).Select(l => l.Split(',')).ToList();
        return (lines[0].Split(','), rows, text);
    }

    [Fact]
    public void The_first_28_columns_are_exactly_the_PresentMon_2_6_0_columns_in_order()
    {
        var (header, _, _) = Render();
        var fixture = FixtureColumns();

        Assert.Equal(28, fixture.Length);
        Assert.Equal(fixture, header.Take(28).ToArray());
        Assert.Equal(PresentLogCsv.PresentMonColumns, fixture);
    }

    [Fact]
    public void The_only_extra_columns_are_the_documented_harness_columns_at_the_end()
    {
        var (header, _, _) = Render();

        Assert.Equal(
            ["JankscopeProvenance", "JankscopeFrameIndex", "JankscopeScheduledMs", "JankscopeInjectedExtraMs", "JankscopePresentQpcTicks"],
            header.Skip(28).ToArray());
        Assert.Equal(header.Skip(28).ToArray(), PresentLogCsv.HarnessColumns);
    }

    [Fact]
    public void Every_row_has_one_value_per_column_and_there_is_one_row_per_frame()
    {
        var result = TestRuns.Fake(frames: 150, hitches: 1);
        var (header, rows, _) = Render(result);

        Assert.Equal(150, rows.Count);
        Assert.All(rows, r => Assert.Equal(header.Length, r.Length));
    }

    [Fact]
    public void Columns_the_harness_cannot_truthfully_provide_are_the_literal_NA()
    {
        var (header, rows, _) = Render();
        var index = header.Select((name, i) => (name, i)).ToDictionary(x => x.name, x => x.i);

        foreach (var name in PresentLogCsv.PresentMonColumns.Except(ProvidedPresentMonColumns).Except(["PresentMode"]))
        {
            Assert.All(rows, r => Assert.Equal("NA", r[index[name]]));
        }

        // Which composition path the OS chose is not observable from inside the app: never invented.
        Assert.All(rows, r => Assert.Equal("NA", r[index["PresentMode"]]));
    }

    [Fact]
    public void Provided_columns_carry_what_the_harness_knows()
    {
        var result = TestRuns.Fake(allowsTearing: true);
        var (header, rows, _) = Render(result);
        var index = header.Select((name, i) => (name, i)).ToDictionary(x => x.name, x => x.i);

        Assert.All(rows, r =>
        {
            Assert.Equal("jankscope-harness.exe", r[index["Application"]]);
            Assert.Equal("4242", r[index["ProcessID"]]);
            Assert.Equal("0xABC123", r[index["SwapChainAddress"]]);
            Assert.Equal("DXGI", r[index["PresentRuntime"]]);
            Assert.Equal("0", r[index["SyncInterval"]]);
            Assert.Equal("512", r[index["PresentFlags"]]); // DXGI_PRESENT_ALLOW_TEARING
            Assert.Equal("1", r[index["AllowsTearing"]]);
        });
    }

    [Fact]
    public void Without_tearing_the_flags_and_the_tearing_column_say_so()
    {
        var result = TestRuns.Fake(allowsTearing: false);
        var (header, rows, _) = Render(result);
        var index = header.Select((name, i) => (name, i)).ToDictionary(x => x.name, x => x.i);

        Assert.All(rows, r =>
        {
            Assert.Equal("0", r[index["PresentFlags"]]);
            Assert.Equal("0", r[index["AllowsTearing"]]);
        });
    }

    [Fact]
    public void An_unknown_swap_chain_address_is_NA_not_zero()
    {
        var result = TestRuns.Fake();
        var context = PresentLogContext.For(result, "jankscope-harness.exe", 1) with { SwapChainAddress = null };
        var (header, rows, _) = Render(result, context);

        Assert.All(rows, r => Assert.Equal("NA", r[Array.IndexOf(header, "SwapChainAddress")]));
    }

    [Fact]
    public void Time_columns_are_the_harness_own_QPC_in_milliseconds_with_four_decimals()
    {
        var result = TestRuns.Fake();
        var (header, rows, _) = Render(result);
        var time = Array.IndexOf(header, "TimeInMs");
        var between = Array.IndexOf(header, "MsBetweenPresents");
        var inPresent = Array.IndexOf(header, "MsInPresentAPI");
        var ticks = Array.IndexOf(header, "JankscopePresentQpcTicks");
        var inv = CultureInfo.InvariantCulture;

        for (var i = 0; i < result.Records.Count; i++)
        {
            var rec = result.Records[i];
            var previous = i == 0 ? 0 : result.Records[i - 1].PresentTicks;
            Assert.Equal(QpcTime.ToMs(rec.PresentTicks, result.Frequency).ToString("F4", inv), rows[i][time]);
            Assert.Equal(QpcTime.ToMs(rec.PresentTicks - previous, result.Frequency).ToString("F4", inv), rows[i][between]);
            Assert.Equal(QpcTime.ToMs(rec.PresentCallTicks, result.Frequency).ToString("F4", inv), rows[i][inPresent]);
            Assert.Equal(rec.PresentTicks.ToString(inv), rows[i][ticks]);
        }

        // Frame 0 has no earlier present: its interval is measured from the pacing origin, so it equals TimeInMs.
        Assert.Equal(rows[0][time], rows[0][between]);
    }

    [Fact]
    public void Every_row_is_tagged_Simulated_and_carries_the_ground_truth_columns()
    {
        var result = TestRuns.Fake(frames: 240, hitches: 2, extra: 25);
        var (header, rows, _) = Render(result);
        var inv = CultureInfo.InvariantCulture;

        Assert.All(rows, r => Assert.Equal("Simulated", r[Array.IndexOf(header, "JankscopeProvenance")]));
        for (var i = 0; i < rows.Count; i++)
        {
            Assert.Equal(i.ToString(inv), rows[i][Array.IndexOf(header, "JankscopeFrameIndex")]);
            Assert.Equal(result.Schedule.TargetMs[i].ToString("F4", inv), rows[i][Array.IndexOf(header, "JankscopeScheduledMs")]);
            Assert.Equal(result.Schedule.InjectedExtraMs[i].ToString("F4", inv), rows[i][Array.IndexOf(header, "JankscopeInjectedExtraMs")]);
        }

        Assert.Equal(2, rows.Count(r => r[Array.IndexOf(header, "JankscopeInjectedExtraMs")] != "0.0000"));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("tr-TR")]
    [InlineData("fr-FR")]
    public void Numbers_use_a_dot_whatever_the_thread_culture(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            var invariant = Render().Text;
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            var localized = Render().Text;

            Assert.Equal(invariant, localized);
            Assert.Matches(@",DXGI,0,\d+,\d,NA,\d+\.\d{4},NA,\d+\.\d{4},", localized.Split("\r\n")[1]);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Fields_that_contain_a_comma_or_quote_are_quoted_so_the_row_stays_parseable()
    {
        var result = TestRuns.Fake(frames: 70, hitches: 0);
        var context = PresentLogContext.For(result, "my,\"odd\" game.exe", 7);
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        PresentLogCsv.Write(writer, context, result.Records);

        var first = writer.ToString().Split("\r\n")[1];
        Assert.StartsWith("\"my,\"\"odd\"\" game.exe\",7,", first);
    }

    [Fact]
    public void Files_are_written_without_a_byte_order_mark_and_with_PresentMon_style_line_breaks()
    {
        var result = TestRuns.Fake(frames: 70, hitches: 0);
        var path = Path.Combine(Path.GetTempPath(), "jankscope-harness-test-" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            PresentLogCsv.WriteFile(path, PresentLogContext.For(result, "jankscope-harness.exe", 1), result.Records);
            var bytes = File.ReadAllBytes(path);

            Assert.NotEqual(0xEF, bytes[0]);
            Assert.Equal((byte)'A', bytes[0]);
            Assert.Equal((byte)'\r', bytes[^2]);
            Assert.Equal((byte)'\n', bytes[^1]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
