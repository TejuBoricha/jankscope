using Jankscope.Core;
using Jankscope.Frames.Detection;
using Jankscope.Frames.PresentMon;

namespace Jankscope.Frames.Tests.PresentMon;

/// <summary>
/// HC-9 across the pipeline (review 1a, finding 6): a simulated frame must not lose its label between the harness log, the parser and the
/// detector's result, and nothing may upgrade a weak label to "Measured".
/// </summary>
public class ProvenanceTests
{
    private const string PresentMonHeader = "Application,ProcessID,TimeInMs,MsBetweenPresents";
    private const string HarnessHeader = PresentMonHeader + ",JankscopeProvenance,JankscopeFrameIndex";

    private static string Log(string header, params string[] rows) => header + "\r\n" + string.Join("\r\n", rows) + "\r\n";

    private static IReadOnlyList<FrameSample> Parse(string text, out IReadOnlyList<string> problems, DataProvenance source = DataProvenance.Measured)
    {
        using var reader = new StringReader(text);
        return PresentMonCsv.ReadAll(reader, out _, out problems, source);
    }

    [Fact]
    public void A_log_the_pinned_tool_wrote_is_measured_by_default()
    {
        var samples = Parse(Log(PresentMonHeader, "game.exe,10,10.0,10.0", "game.exe,10,20.0,10.0"), out var problems);

        Assert.Empty(problems);
        Assert.All(samples, s => Assert.Equal(DataProvenance.Measured, s.Provenance));
    }

    [Fact]
    public void The_caller_can_state_that_a_file_is_a_fixture_and_every_sample_says_so()
    {
        var samples = Parse(Log(PresentMonHeader, "game.exe,10,10.0,10.0"), out _, DataProvenance.Fixture);

        Assert.Equal(DataProvenance.Fixture, Assert.Single(samples).Provenance);
    }

    [Fact]
    public void A_harness_row_is_simulated_even_when_the_caller_says_measured()
    {
        var samples = Parse(Log(HarnessHeader, "jankscope-harness.exe,10,10.0,10.0,Simulated,0", "jankscope-harness.exe,10,20.0,10.0,Simulated,1"), out var problems);

        Assert.Empty(problems);
        Assert.All(samples, s => Assert.Equal(DataProvenance.Simulated, s.Provenance));
    }

    [Fact]
    public void A_row_can_only_make_the_claim_weaker_never_stronger_than_the_caller()
    {
        // The caller says Simulated; a row that says Fixture cannot raise it to anything better, and the weakest claim wins.
        var samples = Parse(Log(HarnessHeader, "x.exe,1,10.0,10.0,Fixture,0"), out _, DataProvenance.Simulated);
        Assert.Equal(DataProvenance.Simulated, Assert.Single(samples).Provenance);

        var fixtureRow = Parse(Log(HarnessHeader, "x.exe,1,10.0,10.0,Fixture,0"), out _, DataProvenance.Measured);
        Assert.Equal(DataProvenance.Fixture, Assert.Single(fixtureRow).Provenance);
    }

    [Theory]
    [InlineData("Measured")]
    [InlineData("")]
    [InlineData("NA")]
    [InlineData("real")]
    public void A_provenance_cell_that_is_not_simulated_or_fixture_rejects_the_row_instead_of_being_believed(string cell)
    {
        var samples = Parse(Log(HarnessHeader, $"x.exe,1,10.0,10.0,{cell},0", "x.exe,1,20.0,10.0,Simulated,1"), out var problems);

        Assert.Equal(DataProvenance.Simulated, Assert.Single(samples).Provenance); // only the honest row got through
        Assert.Contains(problems, p => p.Contains("JankscopeProvenance", StringComparison.Ordinal));
    }

    [Fact]
    public void The_provenance_column_is_not_reported_as_missing_from_a_real_log_nor_as_an_ignored_column_in_a_harness_log()
    {
        using var real = new StringReader(Log(PresentMonHeader, "x.exe,1,10.0,10.0"));
        PresentMonCsv.ReadAll(real, out var realHeader, out _);
        Assert.DoesNotContain("JankscopeProvenance", realHeader.MissingOptionalColumns);

        using var harness = new StringReader(Log(HarnessHeader, "x.exe,1,10.0,10.0,Simulated,0"));
        PresentMonCsv.ReadAll(harness, out var harnessHeader, out _);
        Assert.DoesNotContain("JankscopeProvenance", harnessHeader.IgnoredColumns);
        Assert.Contains("JankscopeFrameIndex", harnessHeader.IgnoredColumns);
    }

    [Fact]
    public void Nobody_can_build_a_parser_that_claims_there_is_no_data()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PresentMonCsvParser(DataProvenance.None));
        using var reader = new StringReader(Log(PresentMonHeader, "x.exe,1,10.0,10.0"));
        Assert.Throws<ArgumentOutOfRangeException>(() => PresentMonCsv.ReadStreaming(reader, sourceProvenance: DataProvenance.None));
    }

    // ---- the detector's result keeps the label ----

    private static StutterResult Detect(IReadOnlyList<FrameSample> samples) => StutterDetector.Analyze(samples.ToArray());

    [Fact]
    public void A_detector_result_from_a_simulated_log_cannot_be_exported_as_a_measurement()
    {
        var samples = Parse(Log(HarnessHeader, "h.exe,1,10.0,10.0,Simulated,0", "h.exe,1,20.0,10.0,Simulated,1"), out _);

        var result = Detect(samples);

        Assert.Equal(DataProvenance.Simulated, result.Provenance);
        Assert.False(result.MayBeExportedAsMeasurement);
        Assert.Throws<InvalidOperationException>(() => result.Provenance.RequireExportableAsMeasurement());
    }

    [Fact]
    public void A_detector_result_from_a_real_log_may_be_exported_as_a_measurement()
    {
        var result = Detect(Parse(Log(PresentMonHeader, "g.exe,1,10.0,10.0", "g.exe,1,20.0,10.0"), out _));

        Assert.Equal(DataProvenance.Measured, result.Provenance);
        Assert.True(result.MayBeExportedAsMeasurement);
        result.Provenance.RequireExportableAsMeasurement(); // does not throw
    }

    [Fact]
    public void One_simulated_frame_among_measured_ones_makes_the_whole_result_simulated()
    {
        var detector = new StutterDetector();
        detector.Push(new FrameSample(0, 10.0, 10.0, Provenance: DataProvenance.Measured));
        detector.Push(new FrameSample(1, 20.0, 10.0, Provenance: DataProvenance.Simulated));
        detector.Push(new FrameSample(2, 30.0, 10.0, Provenance: DataProvenance.Measured));

        Assert.Equal(DataProvenance.Simulated, detector.GetResult().Provenance);
    }

    [Fact]
    public void A_rejected_frame_still_counts_toward_the_label_and_a_hand_built_series_is_only_assumed()
    {
        var detector = new StutterDetector();
        detector.Push(new FrameSample(0, 10.0, 10.0, Provenance: DataProvenance.Measured));
        detector.Push(new FrameSample(1, 20.0, double.NaN, Provenance: DataProvenance.Simulated)); // rejected, but it was part of the series
        Assert.Equal(DataProvenance.Simulated, detector.GetResult().Provenance);

        var byHand = StutterDetector.Analyze([new FrameSample(0, 10.0, 10.0), new FrameSample(1, 20.0, 10.0)]);
        Assert.Equal(DataProvenance.Assumed, byHand.Provenance);
        Assert.False(byHand.MayBeExportedAsMeasurement);
    }

    [Fact]
    public void An_empty_series_has_no_provenance_and_reset_forgets_the_label()
    {
        var detector = new StutterDetector();
        Assert.Equal(DataProvenance.None, detector.GetResult().Provenance);

        detector.Push(new FrameSample(0, 10.0, 10.0, Provenance: DataProvenance.Simulated));
        detector.Reset();

        Assert.Equal(DataProvenance.None, detector.GetResult().Provenance);
    }
}
