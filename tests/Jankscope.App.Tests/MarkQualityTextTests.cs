using Jankscope.App.Views;
using Jankscope.Frames.Incidents;

namespace Jankscope.App.Tests;

/// <summary>The line under the investigator's title (independent Stage 1b review, finding 7): it tells the person when the program presented on more than one swap chain.</summary>
public class MarkQualityTextTests
{
    private static IncidentMark MarkWith(IReadOnlyList<SwapChainUse>? chains, CaptureTime? time = null) => new(
        Id: "m1",
        Kind: MarkKind.Stutter,
        PressedUtc: DateTimeOffset.Parse("2026-11-13T12:00:00Z"),
        PressedTicks: 0,
        CaptureTime: time ?? new CaptureTime(10_000.0, 2.0, 200, 0),
        Search: new AnchorSearchResult([], 0.0, 10_000.0, 100, [], chains),
        Selection: null,
        Note: null,
        Window: new MarkWindow(0.0, 10_000.0, 120.0, 10.0, 30.0),
        PreRoll: FrameSeries.Empty,
        TargetProcess: "game.exe");

    [Fact]
    public void Two_swap_chains_are_said_in_the_line_with_the_other_facts()
    {
        var text = MarkQualityText.Describe(MarkWith([new SwapChainUse("0x1", 1440), new SwapChainUse("0x2", 10)]));

        Assert.Contains("2 swap chains", text);
        Assert.Contains("1440 frame(s) on the main one and 10 on another", text);
        Assert.Contains("Key-press time", text);
        Assert.Contains("Before the mark: 10 s recorded of the 120 s asked for", text);
        Assert.DoesNotContain("0x1", text); // the address is the program's own and stays out of the screen text as well as the export
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void One_chain_or_none_known_adds_nothing(bool known)
    {
        var text = MarkQualityText.Describe(MarkWith(known ? [new SwapChainUse("0x1", 1440)] : null));

        Assert.DoesNotContain("swap chain", text);
        Assert.Contains("Key-press time", text);
    }

    [Fact]
    public void Without_a_capture_time_the_line_still_says_so_and_still_adds_the_chains()
    {
        var mark = MarkWith([new SwapChainUse("0x1", 5), new SwapChainUse("0x2", 4)]) with { CaptureTime = null };

        var text = MarkQualityText.Describe(mark);

        Assert.StartsWith("No frames had arrived when the key was pressed", text);
        Assert.Contains("2 swap chains", text);
    }
}
