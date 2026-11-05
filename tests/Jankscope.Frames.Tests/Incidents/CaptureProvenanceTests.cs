using Jankscope.Core;
using Jankscope.Frames.Capture;
using Jankscope.Frames.Incidents;
using Jankscope.Frames.PresentMon;

namespace Jankscope.Frames.Tests.Incidents;

/// <summary>
/// HC-9 on the live path (review of Stage 1b, finding 6): PresentMon never prints the harness's provenance column, so a capture of the synthetic
/// harness has to be recognised by the program name or it is exported as a measurement of a real game.
/// </summary>
public class CaptureProvenanceTests
{
    private const string Header = "TimeInMs,MsBetweenPresents,MsBetweenDisplayChange,MsCPUBusy,MsGPUBusy,PresentMode,Application,ProcessID";
    private const string Row = "100.0,16.7,16.7,5.0,5.0,Hardware: Independent Flip,x.exe,1234";

    [Theory]
    [InlineData("jankscope-harness.exe")]
    [InlineData("JANKSCOPE-HARNESS.EXE")]
    [InlineData("Jankscope-Harness.exe")]
    [InlineData("jankscope-harness")]
    [InlineData("  jankscope-harness.exe  ")]
    public void The_synthetic_harness_is_recognised_by_name_in_any_spelling_a_person_types(string name)
    {
        Assert.True(CaptureProvenance.IsSyntheticHarness(name));
        Assert.Equal(DataProvenance.Simulated, CaptureProvenance.ForProgram(name));
    }

    [Theory]
    [InlineData("game.exe")]
    [InlineData("jankscope.exe")]
    [InlineData("jankscope-app.exe")]
    [InlineData("jankscope-harness2.exe")]
    [InlineData("my-jankscope-harness.exe")]
    [InlineData("jankscope-harness.exe.exe")]
    [InlineData("jankscope-harness.com")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Any_other_program_is_a_measurement(string? name)
    {
        Assert.False(CaptureProvenance.IsSyntheticHarness(name));
        Assert.Equal(DataProvenance.Measured, CaptureProvenance.ForProgram(name));
    }

    [Fact]
    public void The_harness_file_name_is_the_one_the_rule_matches()
    {
        Assert.Equal("jankscope-harness.exe", CaptureProvenance.HarnessExecutableName);
        Assert.True(CaptureProvenance.IsSyntheticHarness(CaptureProvenance.HarnessExecutableName));
    }

    [Theory]
    [InlineData("jankscope-harness.exe", DataProvenance.Simulated)]
    [InlineData("game.exe", DataProvenance.Measured)]
    public void A_parser_built_from_the_rule_labels_every_frame_it_reads(string program, DataProvenance expected)
    {
        var parser = new PresentMonCsvParser(CaptureProvenance.ForProgram(program));
        Assert.True(parser.ReadHeader(Header).Supported);

        Assert.True(parser.TryParseRow(Row, out var sample, out var problem), problem);

        Assert.Equal(expected, sample.Provenance);
    }

    [Fact]
    public void A_simulated_capture_is_exported_as_the_synthetic_harness_with_a_notice_and_never_as_a_measurement()
    {
        // The notice and the trigger are chosen from the frames' provenance; this pins that the label this rule gives is the one they react to.
        Assert.NotNull(IncidentBundleBuilder.ProvenanceNotice(CaptureProvenance.ForProgram("jankscope-harness.exe")));
        Assert.Null(IncidentBundleBuilder.ProvenanceNotice(CaptureProvenance.ForProgram("game.exe")));
    }
}
