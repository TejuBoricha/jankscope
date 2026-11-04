using Jankscope.Frames.PresentMon;

namespace Jankscope.Frames.Tests.PresentMon;

/// <summary>Header handling: columns by name (case-insensitive, any order), format detection, required vs optional columns, honest reasons.</summary>
public class CsvParserHeaderTests
{
    private static readonly string[] MappedNames =
        ["Application", "ProcessID", "PresentMode", "TimeInMs", "MsBetweenPresents", "MsBetweenDisplayChange", "MsCPUBusy", "MsGPUBusy"];

    private static LogHeaderResult Read(string header) => new PresentMonCsvParser().ReadHeader(header);

    [Fact]
    public void Real_pm2_header_is_supported_and_every_unmapped_column_is_reported_as_ignored()
    {
        var result = Read(CsvTestData.RealHeader);

        Assert.True(result.Supported);
        Assert.Equal(FrameLogFormat.PresentMon2, result.Format);
        Assert.Null(result.UnsupportedReason);
        Assert.Empty(result.MissingOptionalColumns);

        var expectedIgnored = CsvTestData.RealHeader.Split(',').Where(n => !MappedNames.Contains(n)).ToArray();
        Assert.Equal(20, expectedIgnored.Length);
        Assert.Equal(expectedIgnored, result.IgnoredColumns);
        Assert.Contains("SwapChainAddress", result.IgnoredColumns);
        Assert.DoesNotContain("MsBetweenPresents", result.IgnoredColumns);
    }

    [Fact]
    public void Column_order_does_not_matter()
    {
        var reversed = string.Join(",", CsvTestData.RealHeader.Split(',').Reverse());
        var result = Read(reversed);

        Assert.True(result.Supported);
        Assert.Equal(FrameLogFormat.PresentMon2, result.Format);
        Assert.Empty(result.MissingOptionalColumns);
        Assert.Equal(20, result.IgnoredColumns.Count);
    }

    [Theory]
    [InlineData("timeinms,msbetweenpresents")]
    [InlineData("TIMEINMS,MSBETWEENPRESENTS")]
    [InlineData("tImEiNmS,mSbEtWeEnPrEsEnTs")]
    public void Column_names_are_case_insensitive(string header)
    {
        var result = Read(header);

        Assert.True(result.Supported);
        Assert.Equal(FrameLogFormat.PresentMon2, result.Format);
    }

    [Fact]
    public void Names_are_trimmed_and_may_be_quoted()
    {
        var result = Read(" TimeInMs ,\" MsBetweenPresents \",Extra");

        Assert.True(result.Supported);
        Assert.Equal(["Extra"], result.IgnoredColumns);
    }

    [Fact]
    public void Uppercase_i_matches_under_the_turkish_culture()
    {
        // Culture-sensitive comparison under tr-TR maps "I" to a dotless i, so "TIMEINMS" would stop matching "TimeInMs".
        CsvTestData.WithCulture("tr-TR", () =>
        {
            var result = Read("TIMEINMS,MSBETWEENPRESENTS,MSBETWEENDISPLAYCHANGE,MSCPUBUSY,MSGPUBUSY,PRESENTMODE,APPLICATION,PROCESSID");
            Assert.True(result.Supported, result.UnsupportedReason);
            Assert.Empty(result.MissingOptionalColumns);
            Assert.Empty(result.IgnoredColumns);
        });
    }

    [Fact]
    public void Unknown_columns_are_listed_as_ignored_in_header_order()
    {
        var result = Read("Zeta,TimeInMs,MsBetweenPresents,Alpha,SomethingNew");

        Assert.True(result.Supported);
        Assert.Equal(["Zeta", "Alpha", "SomethingNew"], result.IgnoredColumns);
    }

    [Fact]
    public void Missing_optional_columns_are_listed_and_do_not_make_the_log_unsupported()
    {
        var result = Read("TimeInMs,MsBetweenPresents");

        Assert.True(result.Supported);
        Assert.Null(result.UnsupportedReason);
        Assert.Equal(["MsBetweenDisplayChange", "MsCPUBusy", "MsGPUBusy", "PresentMode", "Application", "ProcessID"], result.MissingOptionalColumns);
    }

    [Fact]
    public void Present_optional_columns_are_not_reported_missing()
    {
        var result = Read("TimeInMs,MsBetweenPresents,MsGPUBusy,Application");

        Assert.Equal(["MsBetweenDisplayChange", "MsCPUBusy", "PresentMode", "ProcessID"], result.MissingOptionalColumns);
    }

    [Fact]
    public void Missing_frame_time_column_is_unsupported_and_named()
    {
        var result = Read("TimeInMs,Application");

        Assert.False(result.Supported);
        Assert.StartsWith("unsupported log:", result.UnsupportedReason);
        Assert.Contains("MsBetweenPresents", result.UnsupportedReason);
        Assert.DoesNotContain("TimeInMs", result.UnsupportedReason);
    }

    [Fact]
    public void Missing_timestamp_column_is_unsupported_and_names_both_accepted_spellings()
    {
        var result = Read("MsBetweenPresents,Application");

        Assert.False(result.Supported);
        Assert.StartsWith("unsupported log:", result.UnsupportedReason);
        Assert.Contains("TimeInMs", result.UnsupportedReason);
        Assert.Contains("TimeInSeconds", result.UnsupportedReason);
        Assert.DoesNotContain("MsBetweenPresents", result.UnsupportedReason);
    }

    [Fact]
    public void Missing_both_required_columns_names_both()
    {
        var result = Read("Application,ProcessID");

        Assert.False(result.Supported);
        Assert.Contains("MsBetweenPresents", result.UnsupportedReason);
        Assert.Contains("TimeInMs", result.UnsupportedReason);
    }

    [Fact]
    public void The_display_column_is_never_a_substitute_for_the_frame_time_column()
    {
        // MsBetweenDisplayChange counts displayed frames (frame generation included); using it as frame time would be a silent lie.
        var result = Read("TimeInMs,MsBetweenDisplayChange,MsCPUBusy");

        Assert.False(result.Supported);
        Assert.Contains("MsBetweenPresents", result.UnsupportedReason);
    }

    [Fact]
    public void V1_header_is_detected_as_presentmon1_and_supported()
    {
        var result = Read(CsvTestData.V1Header);

        Assert.True(result.Supported, result.UnsupportedReason);
        Assert.Equal(FrameLogFormat.PresentMon1, result.Format);
        Assert.Equal(["MsCPUBusy", "MsGPUBusy"], result.MissingOptionalColumns);
        Assert.Contains("Dropped", result.IgnoredColumns);
        Assert.Contains("msUntilRenderComplete", result.IgnoredColumns);
        Assert.DoesNotContain("TimeInSeconds", result.IgnoredColumns);
    }

    [Fact]
    public void Timestamp_unit_decides_the_format_when_the_header_is_minimal()
    {
        Assert.Equal(FrameLogFormat.PresentMon2, Read("TimeInMs,MsBetweenPresents").Format);
        Assert.Equal(FrameLogFormat.PresentMon1, Read("TimeInSeconds,MsBetweenPresents").Format);
    }

    [Fact]
    public void An_unsupported_header_still_reports_the_generation_it_recognises()
    {
        Assert.Equal(FrameLogFormat.PresentMon1, Read("Application,Runtime,Dropped,msUntilRenderComplete").Format);
        Assert.Equal(FrameLogFormat.PresentMon2, Read("Application,PresentRuntime,MsGPUTime").Format);
        Assert.Equal(FrameLogFormat.Unknown, Read("Alpha,Beta").Format);
    }

    [Fact]
    public void Both_timestamp_units_in_one_header_is_ambiguous_and_unsupported()
    {
        var result = Read("TimeInMs,TimeInSeconds,MsBetweenPresents");

        Assert.False(result.Supported);
        Assert.Contains("TimeInMs", result.UnsupportedReason);
        Assert.Contains("TimeInSeconds", result.UnsupportedReason);
        Assert.Contains("both", result.UnsupportedReason);
    }

    [Theory]
    [InlineData("TimeInMs,MsBetweenPresents,MsBetweenPresents", "MsBetweenPresents")]
    [InlineData("TimeInMs,msbetweenpresents,MsBetweenPresents", "MsBetweenPresents")]
    [InlineData("TimeInMs,MsBetweenPresents,Application,APPLICATION", "Application")]
    public void A_duplicated_mapped_column_is_unsupported_because_the_right_one_cannot_be_known(string header, string name)
    {
        var result = Read(header);

        Assert.False(result.Supported);
        Assert.Contains("duplicate", result.UnsupportedReason);
        Assert.Contains(name, result.UnsupportedReason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n")]
    public void An_empty_header_is_unsupported_with_a_reason(string header)
    {
        var result = Read(header);

        Assert.False(result.Supported);
        Assert.Equal(FrameLogFormat.Unknown, result.Format);
        Assert.Contains("empty", result.UnsupportedReason);
    }

    [Fact]
    public void A_null_header_is_unsupported_and_does_not_throw()
    {
        var result = new PresentMonCsvParser().ReadHeader(null!);

        Assert.False(result.Supported);
        Assert.False(string.IsNullOrWhiteSpace(result.UnsupportedReason));
    }

    [Fact]
    public void An_unterminated_quote_in_the_header_is_unsupported()
    {
        var result = Read("TimeInMs,\"MsBetweenPresents");

        Assert.False(result.Supported);
        Assert.Contains("quote", result.UnsupportedReason);
    }

    [Fact]
    public void A_header_with_a_nul_character_is_unsupported()
    {
        var result = Read("TimeInMs,MsBetween" + (char)0 + "Presents");

        Assert.False(result.Supported);
    }

    [Fact]
    public void A_utf8_byte_order_mark_in_front_of_the_first_column_is_ignored()
    {
        var result = Read((char)0xFEFF + CsvTestData.RealHeader);

        Assert.True(result.Supported);
        Assert.Empty(result.MissingOptionalColumns); // "Application" was recognised, not swallowed into a BOM-prefixed unknown name
        Assert.DoesNotContain(result.IgnoredColumns, n => n.Contains((char)0xFEFF));
    }

    [Fact]
    public void A_trailing_line_break_on_the_header_is_ignored()
    {
        Assert.True(Read(CsvTestData.RealHeader + "\r\n").Supported);
        Assert.True(Read(CsvTestData.RealHeader + "\n").Supported);
    }

    [Fact]
    public void An_unnamed_column_is_listed_by_position()
    {
        var result = Read("TimeInMs,MsBetweenPresents,");

        Assert.True(result.Supported);
        Assert.Equal(["(unnamed column 3)"], result.IgnoredColumns);
    }

    [Fact]
    public void An_absurdly_wide_header_is_unsupported_instead_of_allocating_without_bound()
    {
        // 5 000 one-letter columns are 10 000 characters, so the column limit is hit before the line-length limit.
        var header = "TimeInMs,MsBetweenPresents," + string.Join(",", Enumerable.Repeat("c", 5000));

        var result = Read(header);

        Assert.False(result.Supported);
        Assert.Contains("columns", result.UnsupportedReason);
    }

    [Fact]
    public void A_header_line_longer_than_any_real_header_is_unsupported()
    {
        var result = Read("TimeInMs,MsBetweenPresents," + new string('x', 20_000));

        Assert.False(result.Supported);
        Assert.Contains("too long", result.UnsupportedReason);
    }

    [Fact]
    public void A_quoted_column_name_may_contain_a_doubled_quote()
    {
        var result = Read("TimeInMs,MsBetweenPresents,\"Odd \"\"name\"\"\"");

        Assert.True(result.Supported);
        Assert.Equal(["Odd \"name\""], result.IgnoredColumns);
    }

    [Fact]
    public void Rows_before_a_header_are_rejected_with_a_reason()
    {
        var parser = new PresentMonCsvParser();

        var problem = CsvTestData.Bad(parser, "10.0,5.0");

        Assert.Contains("header", problem);
    }

    [Fact]
    public void Rows_after_an_unsupported_header_are_rejected_with_a_reason()
    {
        var parser = new PresentMonCsvParser();
        parser.ReadHeader("TimeInMs,Application");

        var problem = CsvTestData.Bad(parser, "10.0,App.exe");

        Assert.Contains("header", problem);
    }

    [Fact]
    public void Reading_a_new_header_starts_a_new_capture()
    {
        var parser = CsvTestData.ParserFor("TimeInMs,MsBetweenPresents");
        Assert.Equal(0, CsvTestData.Ok(parser, "1.0,1.0").Index);
        Assert.Equal(1, CsvTestData.Ok(parser, "2.0,1.0").Index);

        parser.ReadHeader("MsBetweenPresents,TimeInMs");

        var sample = CsvTestData.Ok(parser, "5.0,9.0");
        Assert.Equal(0, sample.Index);
        Assert.Equal(9.0, sample.TimestampMs);
        Assert.Equal(5.0, sample.FrameTimeMs);
    }
}
