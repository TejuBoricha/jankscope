namespace Jankscope.Core.Reports;

/// <summary>
/// A standalone diagnostic summary of hardware, drivers, topology, capabilities, and probe health (spec §3.6).
/// Contains zero personal data, is fully redacted, and is previewable prior to export (HC-16, Acceptance #19).
/// </summary>
public sealed record CompatibilityReport(
    string ReportId,
    DateTimeOffset TimestampUtc,
    string ToolVersion,
    IReadOnlyList<CapabilityEntry> Capabilities,
    IReadOnlyList<ProbeHealth> ProbeHealth,
    IReadOnlyList<string> CollectorProblems);

public static class CompatibilityReportBuilder
{
    public static CompatibilityReport Build(
        CapabilityMatrix matrix,
        IReadOnlyList<string>? problems = null,
        string? reportId = null,
        DateTimeOffset? timestampUtc = null,
        bool redact = true)
    {
        ArgumentNullException.ThrowIfNull(matrix);

        var id = reportId ?? Guid.NewGuid().ToString("D");
        var time = timestampUtc ?? matrix.GeneratedUtc;
        var m = redact ? matrix.Redacted() : matrix;

        var entries = new List<CapabilityEntry>(m.Entries.Count);
        foreach (var entry in m.Entries)
        {
            if (!redact)
            {
                entries.Add(entry);
                continue;
            }

            var val = entry.Value is not null ? RedactionEngine.RedactText(entry.Value) : null;
            var reason = entry.Reason is not null ? RedactionEngine.RedactText(entry.Reason) : null;
            var source = RedactionEngine.RedactText(entry.Source);

            entries.Add(entry.Availability == Availability.Available
                ? CapabilityEntry.Available(entry.Id, entry.Title, entry.Tier, source, val ?? "", entry.Provenance, reason, entry.IsSensitive)
                : CapabilityEntry.Unavailable(entry.Id, entry.Title, entry.Tier, entry.Availability, source, reason ?? ""));
        }

        var cleanProblems = problems is null
            ? []
            : (redact ? problems.Select(p => RedactionEngine.RedactText(p)).ToList() : problems.ToList());

        return new CompatibilityReport(id, time, m.ToolVersion, entries, m.ProbeHealth, cleanProblems);
    }
}
