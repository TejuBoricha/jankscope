using Jankscope.Frames.PresentMon;

namespace Jankscope.Frames.Tests.PresentMon;

/// <summary>The swap-chain column (independent Stage 1b review, finding 7): read when it is an address, unknown otherwise, and never a reason to reject a row or call a log incomplete.</summary>
public class CsvParserSwapChainTests
{
    private const string Header = "TimeInMs,MsBetweenPresents,SwapChainAddress";

    [Theory]
    [InlineData("0x103E8")]
    [InlineData("0x000001F3A5B6C7D8")]
    [InlineData("0xABCDEF0123456789")] // sixteen digits is the longest address
    [InlineData("0x1")]
    public void An_address_is_carried_on_the_sample(string cell)
    {
        var sample = CsvTestData.Ok(CsvTestData.ParserFor(Header), "10.0,4.0," + cell);

        Assert.Equal(cell, sample.SwapChain);
    }

    [Theory]
    [InlineData("NA")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("103E8")]                // no 0x prefix: not how the capture tool writes an address
    [InlineData("0x")]                   // no digits
    [InlineData("0xGHIJ")]               // not hex
    [InlineData("0x12345678901234567")]  // seventeen digits: cannot be an address
    [InlineData("0x12 34")]
    [InlineData("not an address at all and a long cell that would otherwise ride along on every frame of the capture")]
    public void A_cell_that_is_not_an_address_is_unknown_not_a_value_and_the_row_is_still_read(string cell)
    {
        var sample = CsvTestData.Ok(CsvTestData.ParserFor(Header), "10.0,4.0," + cell);

        Assert.Null(sample.SwapChain);
        Assert.Equal(10.0, sample.TimestampMs);
        Assert.Equal(4.0, sample.FrameTimeMs);
    }

    [Fact]
    public void A_log_without_the_column_is_complete_as_before_and_every_sample_says_unknown()
    {
        var parser = new PresentMonCsvParser();
        var header = parser.ReadHeader("TimeInMs,MsBetweenPresents,MsBetweenDisplayChange,MsCPUBusy,MsGPUBusy,PresentMode,Application,ProcessID");

        Assert.True(header.Supported);
        Assert.DoesNotContain("SwapChainAddress", header.MissingOptionalColumns); // not an optional column that is "missing": nothing was promised
        Assert.Null(CsvTestData.Ok(parser, CsvTestData.FullRow).SwapChain);
    }

    [Fact]
    public void The_column_is_found_by_name_in_any_case_and_any_position()
    {
        var sample = CsvTestData.Ok(CsvTestData.ParserFor("swapchainaddress,MsBetweenPresents,TimeInMs"), "0xBEEF,4.0,10.0");

        Assert.Equal("0xBEEF", sample.SwapChain);
        Assert.Equal(10.0, sample.TimestampMs);
    }

    [Fact]
    public void The_same_address_on_every_row_is_one_string_not_one_per_frame()
    {
        var parser = CsvTestData.ParserFor(Header);

        var first = CsvTestData.Ok(parser, "10.0,4.0,0x103E8");
        var second = CsvTestData.Ok(parser, "14.0,4.0,0x103E8");

        Assert.Same(first.SwapChain, second.SwapChain);
    }

    [Fact]
    public void A_new_capture_forgets_the_cached_address()
    {
        var parser = CsvTestData.ParserFor(Header);
        var before = CsvTestData.Ok(parser, "10.0,4.0,0x103E8");

        parser.ReadHeader(Header);
        var after = CsvTestData.Ok(parser, "10.0,4.0,0x103E8");

        Assert.Equal(before.SwapChain, after.SwapChain);
        Assert.NotSame(before.SwapChain, after.SwapChain);
    }
}
