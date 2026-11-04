using System.Text.Json;
using Jankscope.Core;
using Jankscope.Core.Reports;
using Jankscope.Frames.Detection;
using Jankscope.Frames.Reports;

namespace Jankscope.Frames.Tests.Reports;

public class IncidentReportTests
{
    private static FrameSample[] MakeSamples(SyntheticRun run)
    {
        var samples = new FrameSample[run.FrameTimesMs.Count];
        double ts = 0;
        for (var i = 0; i < run.FrameTimesMs.Count; i++)
        {
            var dt = run.FrameTimesMs[i];
            samples[i] = new FrameSample(i, ts, dt, null, null, null, "Composed: Flip", "TestGame.exe", 1234, DataProvenance.Simulated);
            ts += dt;
        }

        return samples;
    }

    [Fact]
    public void Incident_report_generates_correct_metrics_and_hypotheses_and_matches_golden_file()
    {
        var run = SyntheticWorkload.Generate(seed: 42, targetFps: 60.0, frames: 150, hitchCount: 2, hitchExtraMs: 35.0, jitterMs: 0.0);
        var samples = MakeSamples(run);
        var result = StutterDetector.Analyze(samples);

        Assert.Equal(2, result.Events.Count);

        var matrix = new CapabilityMatrix(
            DateTimeOffset.Parse("2026-10-18T12:00:00Z"),
            "0.1.0",
            [
                CapabilityEntry.Available("os.version", "Windows version", CapabilityTier.A, "Environment", "Windows 11 build 26300"),
                CapabilityEntry.Available("os.locale", "Display language and region", CapabilityTier.A, "CultureInfo", "UI en-US, regional en-IN", isSensitive: true),
                CapabilityEntry.Available("anticheat.resident", "Resident anti-cheat", CapabilityTier.A, "SCM", "Riot Vanguard (service vgk running)"),
                CapabilityEntry.Available("mode.optimize", "Optimize mode eligibility", CapabilityTier.A, "derived", "eligible"),
            ],
            [new ProbeHealth("os", true, 1.2, null)]);

        var report = IncidentReportBuilder.Build(
            IncidentTrigger.UserMarked,
            "C:\\Users\\Alice\\Games\\TestGame.exe",
            samples,
            result,
            matrix,
            reportId: "11111111-2222-3333-4444-555555555555",
            timestampUtc: DateTimeOffset.Parse("2026-10-18T12:00:00Z"),
            redact: true,
            canariesToAssert: ["Alice", "en-IN"]);

        var json = report.ToJson();

        Assert.DoesNotContain("Alice", json);
        Assert.DoesNotContain("en-IN", json);
        Assert.Contains("<USER>", json);
        Assert.Contains("[REDACTED: sensitive]", json);
        Assert.Equal(2, report.Metrics.StutterEventsCount);
        Assert.Equal(150, report.Metrics.TotalSamples);
        Assert.NotEmpty(report.Hypotheses);
        Assert.Contains(report.Hypotheses, h => h.Name.Contains("pacing"));
        Assert.Contains(report.Hypotheses, h => h.Name.Contains("anti-cheat"));
        Assert.Contains("[REDACTED: sensitive]", json);
        Assert.Equal(2, report.Metrics.StutterEventsCount);
        Assert.Equal(150, report.Metrics.TotalSamples);
        Assert.NotEmpty(report.Hypotheses);
        Assert.Contains(report.Hypotheses, h => h.Name.Contains("pacing"));
        Assert.Contains(report.Hypotheses, h => h.Name.Contains("anti-cheat"));

        // Save or compare to golden file
        var goldenDir = Path.Combine(AppContext.BaseDirectory, "fixtures", "reports");
        Directory.CreateDirectory(goldenDir);
        var goldenPath = Path.Combine(goldenDir, "golden_incident_report.json");

        if (!File.Exists(goldenPath))
        {
            File.WriteAllText(goldenPath, json);
        }

        var expectedJson = File.ReadAllText(goldenPath);
        using var actualDoc = JsonDocument.Parse(json);
        using var expectedDoc = JsonDocument.Parse(expectedJson);

        Assert.Equal(expectedDoc.RootElement.GetProperty("metadata").GetProperty("reportId").GetString(),
            actualDoc.RootElement.GetProperty("metadata").GetProperty("reportId").GetString());
        Assert.Equal(expectedDoc.RootElement.GetProperty("metrics").GetProperty("stutterEventsCount").GetInt32(),
            actualDoc.RootElement.GetProperty("metrics").GetProperty("stutterEventsCount").GetInt32());
    }
}
