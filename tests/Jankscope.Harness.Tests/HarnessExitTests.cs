using System.Globalization;
using Jankscope.Core;
using Jankscope.Frames;
using Jankscope.Frames.Detection;
using Jankscope.Frames.PresentMon;
using Xunit.Abstractions;

namespace Jankscope.Harness.Tests;

/// <summary>
/// Exit-test variant (a) from docs/test-plans/stage-1a.md row 8:
/// Harness present log -> PresentMonCsv.ReadAll -> StutterDetector -> GroundTruthMatcher.
/// Verifies that:
/// 1. The CSV parser tolerates the harness's 5 trailing Jankscope* columns.
/// 2. HC-9 provenance is preserved: the JankscopeProvenance column marks frames DataProvenance.Simulated.
/// 3. Injected hitches are detected within +-1 frame.
/// 4. Sub-threshold hitches (e.g. 60 fps with +15 ms) are reported as NotDetectableByDefinition, not misses.
/// 5. On live D3D runs, any unmatched event corresponds to a known disturbed frame (OS deschedule).
/// </summary>
[Trait("requires", "d3d")]
[Collection("d3d")]
public class HarnessExitTests(D3dRunFixture fixture, ITestOutputHelper log) : IClassFixture<D3dRunFixture>
{
    private static readonly int[] Seeds = [1, 2, 3, 5, 7, 11];
    private static readonly double[] Rates = [60.0, 144.0, 240.0];
    private static readonly double[] Extras = [15.0, 25.0, 50.0];
    private static readonly double[] Jitters = [0.0, 0.3];

    public static TheoryData<int, double, double, double> HitchGrid
    {
        get
        {
            var data = new TheoryData<int, double, double, double>();
            foreach (var seed in Seeds)
            {
                foreach (var fps in Rates)
                {
                    foreach (var extra in Extras)
                    {
                        foreach (var jitter in Jitters)
                        {
                            data.Add(seed, fps, extra, jitter);
                        }
                    }
                }
            }

            return data;
        }
    }

    public static TheoryData<int, double, double> CleanGrid
    {
        get
        {
            var data = new TheoryData<int, double, double>();
            foreach (var seed in Seeds)
            {
                foreach (var fps in Rates)
                {
                    foreach (var jitter in Jitters)
                    {
                        data.Add(seed, fps, jitter);
                    }
                }
            }

            return data;
        }
    }

    private static (FrameSample[] Samples, LogHeaderResult Header, IReadOnlyList<string> Problems) ParseHarnessLog(
        HarnessRunResult result,
        string application = "jankscope-harness.exe",
        int processId = 4242)
    {
        var context = PresentLogContext.For(result, application, processId);
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        PresentLogCsv.Write(writer, context, result.Records);

        using var reader = new StringReader(writer.ToString());
        var samples = PresentMonCsv.ReadAll(reader, out var header, out var problems, DataProvenance.Measured);
        return (samples.ToArray(), header, problems);
    }

    private static (FrameSample[] Samples, LogHeaderResult Header, IReadOnlyList<string> Problems) WriteAndParseSyntheticLog(
        SyntheticRun run,
        long frequency = 10_000_000L)
    {
        var schedule = FrameSchedule.Create(run, frequency);
        var records = new FrameRecord[schedule.Count];
        for (var i = 0; i < schedule.Count; i++)
        {
            // Paced at exact schedule times
            records[i] = new FrameRecord(i, schedule.TargetTicks[i], schedule.TargetTicks[i], 100L, schedule.InjectedExtraMs[i]);
        }

        var context = new PresentLogContext("jankscope-harness.exe", 1000, "0x12345678", 0, false, 0, frequency);
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        PresentLogCsv.Write(writer, context, records);

        using var reader = new StringReader(writer.ToString());
        var samples = PresentMonCsv.ReadAll(reader, out var header, out var problems, DataProvenance.Measured);
        return (samples.ToArray(), header, problems);
    }

    [D3dFact]
    public void Hardware_harness_present_log_through_parser_and_detector_detects_all_hitches()
    {
        var runResult = fixture.Hardware;
        var (samples, header, problems) = ParseHarnessLog(runResult);

        Assert.True(header.Supported, header.UnsupportedReason);
        Assert.Equal(FrameLogFormat.PresentMon2, header.Format);
        Assert.Empty(problems);
        Assert.NotEmpty(samples);

        // HC-9: The parser sees JankscopeProvenance=Simulated in trailing columns and tags each sample
        Assert.All(samples, s => Assert.Equal(DataProvenance.Simulated, s.Provenance));

        var stutterResult = StutterDetector.Analyze(samples);
        Assert.Equal(DataProvenance.Simulated, stutterResult.Provenance);

        var matchReport = GroundTruthMatcher.Match(
            runResult.Run.Hitches,
            stutterResult.Events,
            toleranceFrames: 1,
            StutterOptions.Default,
            1000.0 / runResult.Workload.Fps);

        log.WriteLine($"Hardware run: {runResult.Records.Count} frames, {runResult.Run.Hitches.Count} hitches, {stutterResult.Events.Count} detected events");
        log.WriteLine($"Detected: {matchReport.DetectedCount}, Missed: {matchReport.MissedCount}, False positives: {matchReport.FalsePositiveCount}");

        // Injected hitches are detected within +-1 frame
        Assert.Equal(runResult.Run.Hitches.Count, matchReport.DetectedCount);
        Assert.Equal(0, matchReport.MissedCount);

        // Any false positive on real rendering must correspond to a frame the harness identified as disturbed by the OS
        var disturbedIndices = new HashSet<long>(runResult.Fidelity.DisturbedFrames.Select(d => (long)d.FrameIndex));
        foreach (var fp in matchReport.FalsePositives)
        {
            Assert.Contains(fp.FrameIndex, (IReadOnlySet<long>)disturbedIndices);
        }
    }

    [D3dFact]
    public void Warp_harness_present_log_through_parser_and_detector_detects_all_hitches()
    {
        var runResult = fixture.Warp;
        var (samples, header, problems) = ParseHarnessLog(runResult);

        Assert.True(header.Supported, header.UnsupportedReason);
        Assert.Equal(FrameLogFormat.PresentMon2, header.Format);
        Assert.Empty(problems);
        Assert.NotEmpty(samples);

        Assert.All(samples, s => Assert.Equal(DataProvenance.Simulated, s.Provenance));

        var stutterResult = StutterDetector.Analyze(samples);
        Assert.Equal(DataProvenance.Simulated, stutterResult.Provenance);

        var matchReport = GroundTruthMatcher.Match(
            runResult.Run.Hitches,
            stutterResult.Events,
            toleranceFrames: 1,
            StutterOptions.Default,
            1000.0 / runResult.Workload.Fps);

        log.WriteLine($"WARP run: {runResult.Records.Count} frames, {runResult.Run.Hitches.Count} hitches, {stutterResult.Events.Count} detected events");
        log.WriteLine($"Detected: {matchReport.DetectedCount}, Missed: {matchReport.MissedCount}, False positives: {matchReport.FalsePositiveCount}");

        Assert.Equal(runResult.Run.Hitches.Count, matchReport.DetectedCount);
        Assert.Equal(0, matchReport.MissedCount);

        var disturbedIndices = new HashSet<long>(runResult.Fidelity.DisturbedFrames.Select(d => (long)d.FrameIndex));
        foreach (var fp in matchReport.FalsePositives)
        {
            Assert.Contains(fp.FrameIndex, (IReadOnlySet<long>)disturbedIndices);
        }
    }

    [Theory]
    [MemberData(nameof(HitchGrid))]
    public void Synthetic_harness_CSV_log_through_parser_and_detector_matches_ground_truth(int seed, double fps, double extraMs, double jitterMs)
    {
        const int runFrames = 1_200;
        const int hitchesPerRun = 6;
        var run = SyntheticWorkload.Generate(seed, fps, runFrames, hitchesPerRun, extraMs, jitterMs);

        var (samples, header, problems) = WriteAndParseSyntheticLog(run);

        Assert.True(header.Supported, header.UnsupportedReason);
        Assert.Empty(problems);
        Assert.Equal(runFrames, samples.Length);
        Assert.All(samples, s => Assert.Equal(DataProvenance.Simulated, s.Provenance));

        var stutterResult = StutterDetector.Analyze(samples);
        Assert.Equal(DataProvenance.Simulated, stutterResult.Provenance);

        var report = GroundTruthMatcher.Match(run.Hitches, stutterResult.Events, toleranceFrames: 1, StutterOptions.Default, 1000.0 / fps);

        var isDetectable = !(fps == 60.0 && extraMs == 15.0);
        var context = $"seed={seed} fps={fps} extra={extraMs} jitter={jitterMs}";

        Assert.True(report.FalsePositiveCount == 0, $"{context}: unexpected false positive at frames {string.Join(",", report.FalsePositives.Select(e => e.FrameIndex))}");

        if (isDetectable)
        {
            Assert.Equal(hitchesPerRun, report.DetectedCount);
            Assert.Equal(0, report.MissedCount);
            Assert.Equal(1.0, report.Precision);
            Assert.Equal(1.0, report.Recall);
        }
        else
        {
            Assert.Equal(0, report.DetectedCount);
            Assert.Equal(hitchesPerRun, report.NotDetectableCount);
        }
    }

    [Theory]
    [MemberData(nameof(CleanGrid))]
    public void Clean_synthetic_harness_CSV_log_produces_zero_false_positives(int seed, double fps, double jitterMs)
    {
        const int runFrames = 2_000;
        var run = SyntheticWorkload.Generate(seed, fps, runFrames, 0, 0, jitterMs);

        var (samples, header, problems) = WriteAndParseSyntheticLog(run);

        Assert.True(header.Supported, header.UnsupportedReason);
        Assert.Empty(problems);
        Assert.Equal(runFrames, samples.Length);

        var stutterResult = StutterDetector.Analyze(samples);

        Assert.Empty(stutterResult.Events);
    }
}
