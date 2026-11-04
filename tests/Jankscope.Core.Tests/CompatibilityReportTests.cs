using Jankscope.Core.Reports;

namespace Jankscope.Core.Tests;

public class CompatibilityReportTests
{
    [Fact]
    public void Compatibility_report_builds_and_redacts_cleanly_with_zero_personal_data()
    {
        var entries = new List<CapabilityEntry>
        {
            CapabilityEntry.Available("os.version", "Windows Version", CapabilityTier.A, "Registry", "Windows 11 Build 26100"),
            CapabilityEntry.Available("hardware.cpu.model", "CPU Model", CapabilityTier.A, "WMI", "AMD Ryzen 7 7800X3D"),
            CapabilityEntry.Available("os.user_profile", "User Path", CapabilityTier.A, "Environment", @"C:\Users\AliceSmith\AppData", isSensitive: true),
            CapabilityEntry.Unavailable("network.probes", "Network Probes", CapabilityTier.A, Availability.NotImplemented, "Planned", "Stage 2"),
        };

        var health = new List<ProbeHealth>
        {
            new("os", true, 1.2, null),
            new("hardware", true, 2.5, null),
        };

        var matrix = new CapabilityMatrix(DateTimeOffset.UtcNow, "1.0.0", entries, health);
        var problems = new[] { @"Could not read C:\Users\AliceSmith\config.json" };

        var report = CompatibilityReportBuilder.Build(matrix, problems, redact: true);

        Assert.Equal("1.0.0", report.ToolVersion);
        Assert.Equal(4, report.Capabilities.Count);
        Assert.Equal(2, report.ProbeHealth.Count);
        Assert.Single(report.CollectorProblems);

        // Verify that sensitive entry was redacted
        var sensitive = report.Capabilities.First(c => c.Id == "os.user_profile");
        Assert.Equal("[REDACTED: sensitive]", sensitive.Value);

        // Verify that problems list was redacted
        Assert.DoesNotContain("AliceSmith", report.CollectorProblems[0]);

        // Run full ExportPipeline with canaries
        var context = RedactionContext.Empty.With(RedactionCategory.UserName, "AliceSmith");
        var preview = ExportPipeline.Prepare(report, context, redact: true);

        Assert.True(preview.RedactionApplied);
        Assert.DoesNotContain("AliceSmith", preview.Text);
        Assert.NotEmpty(preview.Sha256);

        // Verify guard passes
        ExportGuard.Verify(preview.Text, context);
    }

    [Fact]
    public void Compatibility_report_preserves_unredacted_state_when_explicitly_requested()
    {
        var entries = new List<CapabilityEntry>
        {
            CapabilityEntry.Available("hardware.cpu", "CPU", CapabilityTier.A, "WMI", "Intel Core i9-14900K"),
        };
        var matrix = new CapabilityMatrix(DateTimeOffset.UtcNow, "1.0.0", entries, []);
        var report = CompatibilityReportBuilder.Build(matrix, redact: false);

        var preview = ExportPipeline.Prepare(report, RedactionContext.ForThisMachine(), redact: false);
        Assert.False(preview.RedactionApplied);
        Assert.Contains("This export was made with redaction switched off", preview.Text);
    }
}
