using Jankscope.App.Services;
using Jankscope.Core;

namespace Jankscope.App.Tests;

/// <summary>
/// The production factory has to label a live capture of the synthetic harness as Simulated (HC-9, review of Stage 1b, finding 6): the owner's
/// validation run captures the harness first, and PresentMon cannot say what the program is.
/// </summary>
public class CaptureFactoryTests
{
    private const string Header = "TimeInMs,MsBetweenPresents,MsBetweenDisplayChange,MsCPUBusy,MsGPUBusy,PresentMode,Application,ProcessID";
    private const string Row = "100.0,16.7,16.7,5.0,5.0,Hardware: Independent Flip,x.exe,1234";

    private static DataProvenance ProvenanceOfAFrameFrom(string program)
    {
        var parser = PresentMonCaptureFactory.ParserFor(program)();
        Assert.True(parser.ReadHeader(Header).Supported);
        Assert.True(parser.TryParseRow(Row, out var sample, out var problem), problem);
        return sample.Provenance;
    }

    [Fact]
    public void A_capture_of_the_synthetic_harness_reads_every_frame_as_simulated()
    {
        Assert.Equal(DataProvenance.Simulated, ProvenanceOfAFrameFrom("jankscope-harness.exe"));
    }

    [Fact]
    public void A_capture_of_any_other_program_reads_every_frame_as_measured()
    {
        Assert.Equal(DataProvenance.Measured, ProvenanceOfAFrameFrom("game.exe"));
    }

    [Fact]
    public void Each_call_makes_a_new_parser_because_a_restarted_capture_must_not_share_header_state()
    {
        var make = PresentMonCaptureFactory.ParserFor("game.exe");

        Assert.NotSame(make(), make());
    }
}
