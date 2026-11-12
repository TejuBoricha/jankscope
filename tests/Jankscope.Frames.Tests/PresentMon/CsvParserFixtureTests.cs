using System.Globalization;
using Jankscope.Core;
using Jankscope.Frames.PresentMon;

namespace Jankscope.Frames.Tests.PresentMon;

/// <summary>The real PresentMon 2.6.0 capture (DataProvenance.Fixture) and the synthetic files built from the documented header shapes.</summary>
public class CsvParserFixtureTests
{
    private const string RealFixture = "v2.6.0-real-sanitized.csv";

    private static IReadOnlyList<FrameSample> ReadFixture(string name, out LogHeaderResult header, out IReadOnlyList<string> problems)
    {
        using var reader = new StreamReader(CsvTestData.FixturePath(name));
        return PresentMonCsv.ReadAll(reader, out header, out problems, DataProvenance.Fixture); // every file here is a fixture (tests/fixtures/presentmon/README.md)
    }

    // ---- the real capture -------------------------------------------------------------------------------------------------

    [Fact]
    public void Real_fixture_parses_all_857_rows_without_a_single_problem()
    {
        var samples = ReadFixture(RealFixture, out var header, out var problems);

        Assert.True(header.Supported, header.UnsupportedReason);
        Assert.Equal(FrameLogFormat.PresentMon2, header.Format);
        Assert.Empty(problems);
        Assert.Equal(857, samples.Count);
    }

    [Fact]
    public void Real_fixture_first_and_last_rows_match_the_csv_text()
    {
        var raw = CsvTestData.ReadRawFixture(RealFixture);
        var samples = ReadFixture(RealFixture, out _, out _);

        AssertRowMatches(raw, raw.Rows[0], samples[0], 0);
        AssertRowMatches(raw, raw.Rows[^1], samples[^1], 856);

        // Spot values read by eye from the first and last data lines of the file, as a guard against the helper above being wrong too.
        Assert.Equal(15.3549, samples[0].TimestampMs);
        Assert.Equal(6.4395, samples[0].FrameTimeMs, 12);
        Assert.Equal(6.951, samples[0].DisplayedFrameTimeMs!.Value, 12);
        Assert.Equal(5963.7721, samples[^1].TimestampMs);
        Assert.Equal(6.8876, samples[^1].FrameTimeMs, 12);
    }

    [Fact]
    public void Every_real_row_matches_an_independent_reading_of_the_csv_text()
    {
        var raw = CsvTestData.ReadRawFixture(RealFixture);
        var samples = ReadFixture(RealFixture, out _, out _);

        Assert.Equal(raw.Rows.Count, samples.Count);
        for (var i = 0; i < raw.Rows.Count; i++)
        {
            AssertRowMatches(raw, raw.Rows[i], samples[i], i);
        }
    }

    [Fact]
    public void Real_fixture_frame_time_is_the_between_presents_column_and_never_the_display_column()
    {
        var raw = CsvTestData.ReadRawFixture(RealFixture);
        var samples = ReadFixture(RealFixture, out _, out _);
        var differing = 0;

        for (var i = 0; i < samples.Count; i++)
        {
            Assert.Equal(raw.Number(raw.Rows[i], "MsBetweenPresents"), samples[i].FrameTimeMs);
            Assert.Equal(raw.Number(raw.Rows[i], "MsBetweenDisplayChange"), samples[i].DisplayedFrameTimeMs);
            if (samples[i].FrameTimeMs != samples[i].DisplayedFrameTimeMs)
            {
                differing++;
            }
        }

        // If the two columns were identical everywhere this test could not tell them apart.
        Assert.True(differing > 800, $"only {differing} of {samples.Count} rows have different present and display times");
    }

    [Fact]
    public void Real_fixture_has_no_missing_values_so_nothing_is_null()
    {
        // This capture happens to contain no "NA" in any column the parser reads; a null here would mean a value was dropped.
        var samples = ReadFixture(RealFixture, out _, out _);

        Assert.All(samples, s =>
        {
            Assert.NotNull(s.DisplayedFrameTimeMs);
            Assert.NotNull(s.CpuBusyMs);
            Assert.NotNull(s.GpuBusyMs);
            Assert.Equal("Hardware Composed: Independent Flip", s.PresentMode);
            Assert.Equal("TestAppA.exe", s.Application);
            Assert.Equal(1000, s.ProcessId);
            Assert.Equal("0x103E8", s.SwapChain);
        });
    }

    [Fact]
    public void Real_fixture_timestamps_and_frame_times_describe_the_same_clock()
    {
        // TimeInMs is the capture clock and MsBetweenPresents the gap between consecutive presents, so the difference of two
        // consecutive timestamps must equal the later frame's MsBetweenPresents. TimeInMs is printed with 4 decimals, so each timestamp
        // is off by at most 0.5e-4 and a difference by at most 1e-4. A parser that mixed up columns could not satisfy this for 856 pairs.
        const double roundingBound = 1e-4 + 1e-9;
        var samples = ReadFixture(RealFixture, out _, out _);

        for (var i = 1; i < samples.Count; i++)
        {
            var delta = samples[i].TimestampMs - samples[i - 1].TimestampMs;
            Assert.True(Math.Abs(delta - samples[i].FrameTimeMs) <= roundingBound, $"row {i}: timestamp step {delta} vs frame time {samples[i].FrameTimeMs}");
        }
    }

    [Fact]
    public void Real_fixture_average_fps_agrees_with_the_logged_frame_times_and_with_the_capture_clock()
    {
        var samples = ReadFixture(RealFixture, out _, out _);
        var frameTimes = samples.Select(s => s.FrameTimeMs).ToArray();

        var fps = FrameStats.AverageFps(frameTimes);

        // (1) Straight from the definition (frames / total duration), computed here from the CSV text without the parser.
        var raw = CsvTestData.ReadRawFixture(RealFixture);
        var totalMs = raw.Rows.Sum(r => raw.Number(r, "MsBetweenPresents"));
        Assert.Equal(raw.Rows.Count / (totalMs / 1000.0), fps, 9);

        // (2) From the capture clock alone: frames 1..N-1 span (last timestamp - first timestamp).
        var elapsedMs = samples[^1].TimestampMs - samples[0].TimestampMs;
        var fpsFromClock = (samples.Count - 1) / (elapsedMs / 1000.0);
        Assert.Equal(fpsFromClock, FrameStats.AverageFps(frameTimes.AsSpan(1)), 4);

        // (3) The log shows 6.4-7.0 ms between presents for the typical frame (first rows 6.44, 6.81, 6.92; mean 6.95): that is
        //     about 143-156 fps, and anything outside would mean the column was misread (display column, busy time, seconds...).
        Assert.InRange(fps, 1000.0 / 7.0, 1000.0 / 6.4);
    }

    // ---- synthetic files ---------------------------------------------------------------------------------------------------

    [Fact]
    public void Synthetic_v1_log_is_read_as_presentmon1_with_seconds_converted_to_milliseconds()
    {
        var samples = ReadFixture("synthetic-v1-metrics.csv", out var header, out var problems);

        Assert.True(header.Supported, header.UnsupportedReason);
        Assert.Equal(FrameLogFormat.PresentMon1, header.Format);
        Assert.Equal(["MsCPUBusy", "MsGPUBusy"], header.MissingOptionalColumns);
        Assert.Empty(problems);
        Assert.Equal(6, samples.Count);

        Assert.Equal(16.7, samples[0].TimestampMs, 9);
        Assert.Equal(33.3, samples[1].TimestampMs, 9);
        Assert.Equal(100.0, samples[4].TimestampMs, 9);
        Assert.Equal([16.7, 16.6, 16.8, 16.5, 33.4, 16.7], samples.Select(s => s.FrameTimeMs));
        Assert.All(samples, s =>
        {
            Assert.Null(s.CpuBusyMs);
            Assert.Null(s.GpuBusyMs);
            Assert.Equal("SyntheticGameV1.exe", s.Application);
            Assert.Equal(2000, s.ProcessId);
            Assert.Equal("Hardware: Independent Flip", s.PresentMode);
        });
    }

    [Fact]
    public void Synthetic_v1_dropped_present_has_no_display_time_and_the_frame_time_is_unchanged()
    {
        var samples = ReadFixture("synthetic-v1-metrics.csv", out _, out _);

        Assert.Null(samples[3].DisplayedFrameTimeMs);
        Assert.Equal(16.5, samples[3].FrameTimeMs);
        Assert.Equal([16.7, 16.65, 16.75, null, 33.4, 16.7], samples.Select(s => s.DisplayedFrameTimeMs));
    }

    [Fact]
    public void Synthetic_v1_and_v2_headers_are_distinguishable_so_downstream_can_refuse_to_compare_them()
    {
        ReadFixture("synthetic-v1-metrics.csv", out var v1, out _);
        ReadFixture(RealFixture, out var v2, out _);

        Assert.NotEqual(v1.Format, v2.Format);
    }

    [Fact]
    public void Synthetic_reordered_log_with_extra_and_oddly_cased_columns_gives_the_expected_samples()
    {
        var samples = ReadFixture("synthetic-reordered-extra-columns.csv", out var header, out var problems);

        Assert.True(header.Supported, header.UnsupportedReason);
        Assert.Equal(FrameLogFormat.PresentMon2, header.Format);
        Assert.Empty(problems);
        Assert.Equal(["Extra_Vendor_Column", "NewInSomeFutureVersion"], header.IgnoredColumns);
        Assert.Empty(header.MissingOptionalColumns);

        FrameSample[] expected =
        [
            new(0, 10.25, 6.5, 6.75, 1.5, 3.25, "Hardware: Independent Flip", "ReorderedApp.exe", 1500, DataProvenance.Fixture),
            new(1, 17.75, 7.5, null, 2.5, null, "Composed: Flip", "ReorderedApp.exe", 1500, DataProvenance.Fixture),
            new(2, 33.75, 16.0, 16.5, null, 4.5, "Composed: Flip", "ReorderedApp.exe", 1500, DataProvenance.Fixture),
        ];
        Assert.Equal(expected, samples);
    }

    [Fact]
    public void Synthetic_log_with_only_the_required_columns_degrades_every_optional_metric_to_null()
    {
        var samples = ReadFixture("synthetic-missing-optional.csv", out var header, out var problems);

        Assert.True(header.Supported);
        Assert.Equal(["MsBetweenDisplayChange", "MsCPUBusy", "MsGPUBusy", "PresentMode", "ProcessID"], header.MissingOptionalColumns);
        Assert.Empty(problems);
        Assert.Equal(3, samples.Count);
        Assert.All(samples, s =>
        {
            Assert.Null(s.DisplayedFrameTimeMs);
            Assert.Null(s.CpuBusyMs);
            Assert.Null(s.GpuBusyMs);
            Assert.Null(s.PresentMode);
            Assert.Null(s.ProcessId);
            Assert.Equal("OnlyRequiredApp.exe", s.Application);
            Assert.Equal(5.25, s.FrameTimeMs);
        });
    }

    [Fact]
    public void Synthetic_log_without_a_frame_time_column_is_unsupported_even_though_it_has_display_times()
    {
        var samples = ReadFixture("synthetic-missing-frame-time.csv", out var header, out var problems);

        Assert.False(header.Supported);
        Assert.StartsWith("unsupported log:", header.UnsupportedReason);
        Assert.Contains("MsBetweenPresents", header.UnsupportedReason);
        Assert.Empty(samples);
        Assert.Empty(problems);
    }

    [Fact]
    public void Synthetic_log_without_a_timestamp_column_is_unsupported_and_says_which_names_were_accepted()
    {
        var samples = ReadFixture("synthetic-missing-timestamp.csv", out var header, out _);

        Assert.False(header.Supported);
        Assert.Contains("TimeInMs", header.UnsupportedReason);
        Assert.Contains("TimeInSeconds", header.UnsupportedReason);
        Assert.Empty(samples);
    }

    [Fact]
    public void Synthetic_log_with_quoted_names_and_unknown_application_is_read_exactly()
    {
        var samples = ReadFixture("synthetic-quoted-and-unknown.csv", out var header, out var problems);

        Assert.True(header.Supported);
        Assert.Empty(problems);
        FrameSample[] expected =
        [
            new(0, 10.0, 5.0, 5.0, 1.0, 2.0, "Hardware, Composed: \"Flip\"", "My Game, Deluxe \"Edition\".exe", 3000, DataProvenance.Fixture),
            new(1, 15.0, 5.0, null, null, null, "Hardware Composed: Independent Flip", "<unknown>", null, DataProvenance.Fixture),
            new(2, 20.0, 5.0, 5.0, 1.5, 2.5, "Unknown", "  spaced name.exe  ", 3001, DataProvenance.Fixture),
        ];
        Assert.Equal(expected, samples);
    }

    [Fact]
    public void Synthetic_log_with_a_cut_off_last_line_keeps_the_good_rows_and_reports_the_bad_one()
    {
        var samples = ReadFixture("synthetic-truncated-last-line.csv", out var header, out var problems);

        Assert.True(header.Supported);
        Assert.Equal(3, samples.Count);
        Assert.Equal([10.0, 15.0, 20.0], samples.Select(s => s.TimestampMs));
        var problem = Assert.Single(problems);
        Assert.StartsWith("line 5:", problem);
        Assert.Contains("expected 4 fields, found 3", problem);
    }

    [Fact]
    public void Synthetic_frame_generation_log_keeps_application_and_displayed_frame_times_apart()
    {
        var samples = ReadFixture("synthetic-frame-generation.csv", out var header, out var problems);

        Assert.True(header.Supported);
        Assert.Empty(problems);
        Assert.Equal(4, samples.Count);
        Assert.All(samples, s =>
        {
            Assert.Equal(8.0, s.FrameTimeMs);
            Assert.Equal(4.0, s.DisplayedFrameTimeMs);
        });
        // Application frame rate and displayed frame rate differ by the generation factor; neither may stand in for the other.
        Assert.Equal(125.0, FrameStats.AverageFps(samples.Select(s => s.FrameTimeMs).ToArray()), 9);
        Assert.Equal(250.0, FrameStats.AverageFps(samples.Select(s => s.DisplayedFrameTimeMs!.Value).ToArray()), 9);
    }

    // ---- helpers -------------------------------------------------------------------------------------------------------------

    private static void AssertRowMatches(CsvTestData.RawLog raw, string[] row, FrameSample sample, long expectedIndex)
    {
        Assert.Equal(expectedIndex, sample.Index);
        Assert.Equal(raw.Number(row, "TimeInMs"), sample.TimestampMs);
        Assert.Equal(raw.Number(row, "MsBetweenPresents"), sample.FrameTimeMs);
        Assert.Equal(raw.NumberOrNull(row, "MsBetweenDisplayChange"), sample.DisplayedFrameTimeMs);
        Assert.Equal(raw.NumberOrNull(row, "MsCPUBusy"), sample.CpuBusyMs);
        Assert.Equal(raw.NumberOrNull(row, "MsGPUBusy"), sample.GpuBusyMs);
        Assert.Equal(row[raw.Col("PresentMode")], sample.PresentMode);
        Assert.Equal(row[raw.Col("Application")], sample.Application);
        Assert.Equal(int.Parse(row[raw.Col("ProcessID")], CultureInfo.InvariantCulture), sample.ProcessId);
        Assert.Equal(row[raw.Col("SwapChainAddress")], sample.SwapChain);
    }
}
