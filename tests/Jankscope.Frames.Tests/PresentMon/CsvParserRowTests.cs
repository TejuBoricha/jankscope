using Jankscope.Frames.PresentMon;

namespace Jankscope.Frames.Tests.PresentMon;

/// <summary>Row parsing: column mapping, "NA" and empty values, quoting, culture, and every way a line can be malformed.</summary>
public class CsvParserRowTests
{
    private const string TwoColumns = "TimeInMs,MsBetweenPresents";

    [Fact]
    public void Every_column_lands_in_its_own_field()
    {
        var parser = CsvTestData.ParserFor(CsvTestData.FullHeader);

        var sample = CsvTestData.Ok(parser, CsvTestData.FullRow);

        Assert.Equal(CsvTestData.FullRowSample, sample);
    }

    [Fact]
    public void Reordering_the_columns_gives_the_same_sample()
    {
        var parser = CsvTestData.ParserFor("ProcessID,Application,PresentMode,MsGPUBusy,MsCPUBusy,MsBetweenDisplayChange,MsBetweenPresents,TimeInMs");

        var sample = CsvTestData.Ok(parser, "4242,Game.exe,Hardware Composed: Independent Flip,3.5,1.25,9.5,6.25,100.5");

        Assert.Equal(CsvTestData.FullRowSample, sample);
    }

    [Fact]
    public void Frame_time_and_displayed_frame_time_are_never_merged()
    {
        var parser = CsvTestData.ParserFor("TimeInMs,MsBetweenPresents,MsBetweenDisplayChange");

        var sample = CsvTestData.Ok(parser, "10.0,4.0,8.0");

        Assert.Equal(4.0, sample.FrameTimeMs);
        Assert.Equal(8.0, sample.DisplayedFrameTimeMs);
    }

    [Fact]
    public void A_missing_display_column_leaves_the_displayed_frame_time_null_instead_of_copying_the_frame_time()
    {
        var parser = CsvTestData.ParserFor(TwoColumns);

        var sample = CsvTestData.Ok(parser, "10.0,4.0");

        Assert.Equal(4.0, sample.FrameTimeMs);
        Assert.Null(sample.DisplayedFrameTimeMs);
    }

    [Fact]
    public void An_unavailable_display_value_does_not_fall_back_to_the_frame_time()
    {
        var parser = CsvTestData.ParserFor("TimeInMs,MsBetweenPresents,MsBetweenDisplayChange");

        var sample = CsvTestData.Ok(parser, "10.0,4.0,NA");

        Assert.Equal(4.0, sample.FrameTimeMs);
        Assert.Null(sample.DisplayedFrameTimeMs);
    }

    [Fact]
    public void An_unavailable_frame_time_rejects_the_row_instead_of_falling_back_to_the_display_time()
    {
        var parser = CsvTestData.ParserFor("TimeInMs,MsBetweenPresents,MsBetweenDisplayChange");

        var problem = CsvTestData.Bad(parser, "10.0,NA,8.0");

        Assert.Contains("MsBetweenPresents", problem);
    }

    [Fact]
    public void Na_becomes_null_for_every_optional_value_never_zero()
    {
        var parser = CsvTestData.ParserFor(CsvTestData.FullHeader);

        var sample = CsvTestData.Ok(parser, "100.5,6.25,NA,NA,NA,NA,NA,NA");

        Assert.Equal(new FrameSample(0, 100.5, 6.25, Provenance: Jankscope.Core.DataProvenance.Measured), sample);
        Assert.Null(sample.DisplayedFrameTimeMs);
        Assert.Null(sample.CpuBusyMs);
        Assert.Null(sample.GpuBusyMs);
        Assert.Null(sample.PresentMode);
        Assert.Null(sample.Application);
        Assert.Null(sample.ProcessId);
    }

    [Fact]
    public void Empty_values_are_not_available_either()
    {
        var parser = CsvTestData.ParserFor(CsvTestData.FullHeader);

        var sample = CsvTestData.Ok(parser, "100.5,6.25,,,,,,");

        Assert.Equal(new FrameSample(0, 100.5, 6.25, Provenance: Jankscope.Core.DataProvenance.Measured), sample);
    }

    [Fact]
    public void A_real_zero_is_a_measurement_and_is_kept()
    {
        var parser = CsvTestData.ParserFor(CsvTestData.FullHeader);

        var sample = CsvTestData.Ok(parser, "100.5,6.25,0.0000,0.0000,0.0000,x,y,0");

        Assert.Equal(0.0, sample.DisplayedFrameTimeMs);
        Assert.Equal(0.0, sample.CpuBusyMs);
        Assert.Equal(0.0, sample.GpuBusyMs);
        Assert.Equal(0, sample.ProcessId);
    }

    [Fact]
    public void Negative_zero_in_an_optional_metric_is_stored_as_plain_zero()
    {
        // A value like -0.0000 comes from rounding a tiny negative difference; it is zero, and must not carry a sign into later arithmetic.
        var parser = CsvTestData.ParserFor("TimeInMs,MsBetweenPresents,MsGPUBusy");

        var sample = CsvTestData.Ok(parser, "10.0,5.0,-0.0000");

        Assert.Equal(0.0, sample.GpuBusyMs);
        Assert.False(double.IsNegative(sample.GpuBusyMs!.Value));
    }

    [Theory]
    [InlineData("NA", "TimeInMs")]
    [InlineData("", "TimeInMs")]
    [InlineData("   ", "TimeInMs")]
    public void An_unavailable_timestamp_rejects_the_row(string value, string column)
    {
        var parser = CsvTestData.ParserFor(TwoColumns);

        var problem = CsvTestData.Bad(parser, value + ",5.0");

        Assert.Contains(column, problem);
    }

    [Theory]
    [InlineData("NA")]
    [InlineData("")]
    public void An_unavailable_frame_time_rejects_the_row(string value)
    {
        var parser = CsvTestData.ParserFor(TwoColumns);

        var problem = CsvTestData.Bad(parser, "10.0," + value);

        Assert.Contains("MsBetweenPresents", problem);
    }

    [Fact]
    public void Unknown_application_text_is_kept_verbatim()
    {
        var parser = CsvTestData.ParserFor("TimeInMs,MsBetweenPresents,Application");

        var sample = CsvTestData.Ok(parser, "10.0,5.0,<unknown>");

        Assert.Equal("<unknown>", sample.Application);
    }

    [Fact]
    public void Quoted_fields_may_contain_commas_and_doubled_quotes()
    {
        var parser = CsvTestData.ParserFor("Application,TimeInMs,MsBetweenPresents,PresentMode");

        var sample = CsvTestData.Ok(parser, "\"My Game, Deluxe \"\"Edition\"\".exe\",10.0,5.0,\"Hardware, Composed: \"\"Flip\"\"\"");

        Assert.Equal("My Game, Deluxe \"Edition\".exe", sample.Application);
        Assert.Equal("Hardware, Composed: \"Flip\"", sample.PresentMode);
        Assert.Equal(10.0, sample.TimestampMs);
    }

    [Fact]
    public void Quoted_text_keeps_its_surrounding_spaces_but_unquoted_numbers_may_have_them()
    {
        var parser = CsvTestData.ParserFor("Application,TimeInMs,MsBetweenPresents");

        var sample = CsvTestData.Ok(parser, "\"  spaced.exe  \", 10.0 ,\"5.0\"");

        Assert.Equal("  spaced.exe  ", sample.Application);
        Assert.Equal(10.0, sample.TimestampMs);
        Assert.Equal(5.0, sample.FrameTimeMs);
    }

    [Fact]
    public void An_empty_quoted_field_is_not_available()
    {
        var parser = CsvTestData.ParserFor("Application,TimeInMs,MsBetweenPresents");

        var sample = CsvTestData.Ok(parser, "\"\",10.0,5.0");

        Assert.Null(sample.Application);
    }

    [Theory]
    [InlineData("\"unterminated,10.0,5.0", "quote")]
    [InlineData("\"abc\"x,10.0,5.0", "quote")]
    [InlineData("ab\"c,10.0,5.0", "quote")]
    public void Malformed_quoting_rejects_the_row(string line, string expectedWord)
    {
        var parser = CsvTestData.ParserFor("Application,TimeInMs,MsBetweenPresents");

        var problem = CsvTestData.Bad(parser, line);

        Assert.Contains(expectedWord, problem);
    }

    [Theory]
    [InlineData("10.0", 1)]
    [InlineData("10.0,5.0,7.0", 3)]
    [InlineData(",,,", 4)]
    public void A_wrong_field_count_rejects_the_row_and_says_how_many_were_found(string line, int found)
    {
        var parser = CsvTestData.ParserFor(TwoColumns);

        var problem = CsvTestData.Bad(parser, line);

        Assert.Contains("expected 2 fields", problem);
        Assert.Contains($"found {found}", problem);
    }

    [Fact]
    public void A_row_with_a_trailing_comma_has_an_extra_empty_field_and_is_rejected()
    {
        var parser = CsvTestData.ParserFor(TwoColumns);

        var problem = CsvTestData.Bad(parser, "10.0,5.0,");

        Assert.Contains("found 3", problem);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1.2.3")]
    [InlineData("0x10")]
    [InlineData("12ms")]
    [InlineData("--5")]
    [InlineData("\"6,25\"")]
    [InlineData("1 000.5")]
    public void A_non_numeric_frame_time_rejects_the_row(string text)
    {
        var parser = CsvTestData.ParserFor(TwoColumns);

        var problem = CsvTestData.Bad(parser, "10.0," + text);

        Assert.Contains("MsBetweenPresents", problem);
        Assert.Contains("not a number", problem);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("-0.0001")]
    [InlineData("-1e-9")]
    public void A_negative_frame_time_rejects_the_row(string text)
    {
        var parser = CsvTestData.ParserFor(TwoColumns);

        var problem = CsvTestData.Bad(parser, "10.0," + text);

        Assert.Contains("negative", problem);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.0000")]
    [InlineData("-0")]
    public void A_frame_time_of_zero_rejects_the_row_because_the_core_statistics_treat_it_as_invalid(string text)
    {
        var parser = CsvTestData.ParserFor(TwoColumns);

        var problem = CsvTestData.Bad(parser, "10.0," + text);

        Assert.Contains("greater than zero", problem);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("1e999")]
    [InlineData("-1e999")]
    public void A_non_finite_frame_time_rejects_the_row(string text)
    {
        var parser = CsvTestData.ParserFor(TwoColumns);

        var problem = CsvTestData.Bad(parser, "10.0," + text);

        Assert.Contains("finite", problem);
    }

    [Theory]
    [InlineData("-1", "negative")]
    [InlineData("NaN", "finite")]
    [InlineData("Infinity", "finite")]
    [InlineData("abc", "not a number")]
    public void A_bad_timestamp_rejects_the_row(string text, string expected)
    {
        var parser = CsvTestData.ParserFor(TwoColumns);

        var problem = CsvTestData.Bad(parser, text + ",5.0");

        Assert.Contains("TimeInMs", problem);
        Assert.Contains(expected, problem);
    }

    [Theory]
    [InlineData("MsBetweenDisplayChange", "-3", "negative")]
    [InlineData("MsBetweenDisplayChange", "abc", "not a number")]
    [InlineData("MsCPUBusy", "NaN", "finite")]
    [InlineData("MsGPUBusy", "-0.5", "negative")]
    [InlineData("MsGPUBusy", "Infinity", "finite")]
    public void A_corrupt_optional_value_rejects_the_whole_row_instead_of_being_guessed(string column, string value, string expected)
    {
        var parser = CsvTestData.ParserFor($"TimeInMs,MsBetweenPresents,{column}");

        var problem = CsvTestData.Bad(parser, $"10.0,5.0,{value}");

        Assert.Contains(column, problem);
        Assert.Contains(expected, problem);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("1.5")]
    [InlineData("99999999999")]
    public void A_corrupt_process_id_rejects_the_row(string value)
    {
        var parser = CsvTestData.ParserFor("TimeInMs,MsBetweenPresents,ProcessID");

        var problem = CsvTestData.Bad(parser, $"10.0,5.0,{value}");

        Assert.Contains("ProcessID", problem);
    }

    [Fact]
    public void Unread_columns_are_not_validated()
    {
        // Columns the parser does not map may hold anything (they are "ignored"); garbage there must not cost a frame.
        var parser = CsvTestData.ParserFor("TimeInMs,MsBetweenPresents,SomethingNew");

        var sample = CsvTestData.Ok(parser, "10.0,5.0,not a number at all");

        Assert.Equal(5.0, sample.FrameTimeMs);
    }

    [Fact]
    public void Exponent_notation_and_a_leading_plus_are_accepted_as_numbers()
    {
        var parser = CsvTestData.ParserFor(TwoColumns);

        var sample = CsvTestData.Ok(parser, "+1.5e1,6.25E0");

        Assert.Equal(15.0, sample.TimestampMs);
        Assert.Equal(6.25, sample.FrameTimeMs);
    }

    [Fact]
    public void Trailing_carriage_return_and_line_feed_are_ignored()
    {
        var parser = CsvTestData.ParserFor(TwoColumns);

        Assert.Equal(5.0, CsvTestData.Ok(parser, "10.0,5.0\r\n").FrameTimeMs);
        Assert.Equal(5.0, CsvTestData.Ok(parser, "10.0,5.0\n").FrameTimeMs);
        Assert.Equal(5.0, CsvTestData.Ok(parser, "10.0,5.0\r").FrameTimeMs);
    }

    [Fact]
    public void A_line_break_inside_a_line_rejects_it()
    {
        var parser = CsvTestData.ParserFor(TwoColumns);

        var problem = CsvTestData.Bad(parser, "10.0,5.0\n11.0,5.0");

        Assert.Contains("line break", problem);
    }

    [Fact]
    public void A_nul_character_rejects_the_row()
    {
        var parser = CsvTestData.ParserFor(TwoColumns);

        var problem = CsvTestData.Bad(parser, "10.0,5." + (char)0 + "0");

        Assert.Contains("NUL", problem);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n")]
    [InlineData("\t")]
    public void A_blank_line_is_reported_as_blank_and_does_not_consume_an_index(string line)
    {
        var parser = CsvTestData.ParserFor(TwoColumns);

        var problem = CsvTestData.Bad(parser, line);
        var next = CsvTestData.Ok(parser, "10.0,5.0");

        Assert.Contains("blank", problem);
        Assert.Equal(0, next.Index);
    }

    [Fact]
    public void A_null_line_is_rejected_without_throwing()
    {
        var parser = CsvTestData.ParserFor(TwoColumns);

        var accepted = parser.TryParseRow(null!, out var sample, out var problem);

        Assert.False(accepted);
        Assert.Equal(default, sample);
        Assert.False(string.IsNullOrWhiteSpace(problem));
    }

    [Fact]
    public void A_line_longer_than_any_real_row_is_rejected()
    {
        var parser = CsvTestData.ParserFor(TwoColumns);

        var problem = CsvTestData.Bad(parser, "10.0," + new string('9', 100_000));

        Assert.Contains("too long", problem);
    }

    [Fact]
    public void A_rejected_row_returns_a_default_sample()
    {
        var parser = CsvTestData.ParserFor(TwoColumns);

        Assert.False(parser.TryParseRow("10.0,-5", out var sample, out _));
        Assert.Equal(default, sample);
    }

    [Fact]
    public void Index_counts_data_rows_in_order()
    {
        var parser = CsvTestData.ParserFor(TwoColumns);

        var indexes = Enumerable.Range(0, 5).Select(i => CsvTestData.Ok(parser, $"{i + 1}.0,5.0").Index).ToArray();

        Assert.Equal(new long[] { 0, 1, 2, 3, 4 }, indexes);
    }

    [Fact]
    public void A_rejected_row_leaves_a_gap_in_the_index_so_a_lost_frame_is_visible_downstream()
    {
        var parser = CsvTestData.ParserFor(TwoColumns);

        var first = CsvTestData.Ok(parser, "10.0,5.0");
        CsvTestData.Bad(parser, "15.0,-5.0");
        var third = CsvTestData.Ok(parser, "20.0,5.0");

        Assert.Equal(0, first.Index);
        Assert.Equal(2, third.Index);
    }

    [Fact]
    public void The_same_text_always_gives_the_same_sample_on_a_fresh_parser()
    {
        var a = CsvTestData.Ok(CsvTestData.ParserFor(CsvTestData.FullHeader), CsvTestData.FullRow);
        var b = CsvTestData.Ok(CsvTestData.ParserFor(CsvTestData.FullHeader), CsvTestData.FullRow);

        Assert.Equal(a, b);
    }

    [Fact]
    public void Repeated_text_values_are_shared_instead_of_allocated_per_row()
    {
        // 1 M rows would otherwise create 2 M identical strings; the guarantee is only about sharing, never about changing the value.
        var parser = CsvTestData.ParserFor(CsvTestData.FullHeader);

        var first = CsvTestData.Ok(parser, CsvTestData.FullRow);
        var second = CsvTestData.Ok(parser, CsvTestData.FullRow);

        Assert.Same(first.Application, second.Application);
        Assert.Same(first.PresentMode, second.PresentMode);
    }

    [Fact]
    public void A_changed_application_is_not_confused_with_the_cached_one()
    {
        var parser = CsvTestData.ParserFor("TimeInMs,MsBetweenPresents,Application");

        Assert.Equal("A.exe", CsvTestData.Ok(parser, "1.0,1.0,A.exe").Application);
        Assert.Equal("B.exe", CsvTestData.Ok(parser, "2.0,1.0,B.exe").Application);
        Assert.Equal("A.exe", CsvTestData.Ok(parser, "3.0,1.0,A.exe").Application);
        Assert.Null(CsvTestData.Ok(parser, "4.0,1.0,NA").Application);
    }

    [Fact]
    public void Time_in_seconds_is_converted_to_milliseconds()
    {
        var parser = CsvTestData.ParserFor("TimeInSeconds,MsBetweenPresents");

        var sample = CsvTestData.Ok(parser, "0.0167,16.7");

        Assert.Equal(16.7, sample.TimestampMs, 9);
        Assert.Equal(16.7, sample.FrameTimeMs);
    }

    [Fact]
    public void A_huge_seconds_value_that_overflows_when_converted_is_rejected_instead_of_becoming_infinity()
    {
        var parser = CsvTestData.ParserFor("TimeInSeconds,MsBetweenPresents");

        var problem = CsvTestData.Bad(parser, "1e308,16.7");

        Assert.Contains("finite", problem);
    }
}
