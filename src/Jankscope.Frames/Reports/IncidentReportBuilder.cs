using Jankscope.Core;
using Jankscope.Core.Reports;
using Jankscope.Frames.Detection;

namespace Jankscope.Frames.Reports;

public static class IncidentReportBuilder
{
    public static IncidentReport Build(
        IncidentTrigger trigger,
        string? targetProcess,
        ReadOnlySpan<FrameSample> samples,
        StutterResult stutterResult,
        CapabilityMatrix capabilityMatrix,
        string? reportId = null,
        DateTimeOffset? timestampUtc = null,
        bool redact = true,
        IEnumerable<string>? canariesToAssert = null)
    {
        var id = reportId ?? Guid.NewGuid().ToString("D");
        var time = timestampUtc ?? DateTimeOffset.UtcNow;
        var version = capabilityMatrix.ToolVersion;

        var validTimes = new List<double>(samples.Length);
        double totalMs = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            var t = samples[i].FrameTimeMs;
            if (double.IsFinite(t) && t > 0)
            {
                validTimes.Add(t);
                totalMs += t;
            }
        }

        var frameTimes = validTimes.ToArray();
        var durationSec = totalMs / 1000.0;
        var avgFps = FrameStats.AverageFps(frameTimes);
        var p50 = FrameStats.PercentileFrameTimeMs(frameTimes, 50.0);
        var p99 = FrameStats.PercentileFrameTimeMs(frameTimes, 99.0);
        var p999 = FrameStats.PercentileFrameTimeMs(frameTimes, 99.9);

        var metrics = new MetricSummary(
            TotalSamples: samples.Length,
            DurationSeconds: Math.Round(durationSec, 3),
            AverageFps: double.IsFinite(avgFps) ? Math.Round(avgFps, 2) : null,
            MedianFrameTimeMs: double.IsFinite(p50) ? Math.Round(p50, 2) : null,
            P99FrameTimeMs: double.IsFinite(p99) ? Math.Round(p99, 2) : null,
            P999FrameTimeMs: double.IsFinite(p999) ? Math.Round(p999, 2) : null,
            StutterEventsCount: stutterResult.Events.Count,
            StutterClustersCount: stutterResult.Clusters.Count,
            InsufficientHistoryFrames: stutterResult.InsufficientHistoryFrames,
            ClockDiscontinuities: stutterResult.ClockDiscontinuities,
            RejectedFrames: stutterResult.RejectedFrames);

        var hypotheses = new List<Hypothesis>();
        if (stutterResult.Events.Count > 0)
        {
            var maxExcess = stutterResult.Events.Max(e => e.ExcessMs);
            var maxRatio = stutterResult.Events.Max(e => e.Ratio);
            hypotheses.Add(new Hypothesis(
                Name: "Frame pacing hitch",
                Grade: "B",
                Summary: $"Detected {stutterResult.Events.Count} presentation delay event(s) exceeding normal interval by > {stutterResult.Options.MinExcessMs:F1} ms.",
                SupportingEvidence: $"Maximum excess was {maxExcess:F1} ms (ratio {maxRatio:F1}x baseline).",
                WhatWouldFalsify: "GPU render completion logs showing frame ready before target present interval."));
        }

        var acRow = capabilityMatrix.Entries.FirstOrDefault(e => e.Id == "anticheat.resident");
        if (acRow is { Availability: Availability.Available } && acRow.Value is not null)
        {
            hypotheses.Add(new Hypothesis(
                Name: "Resident kernel anti-cheat presence",
                Grade: "C",
                Summary: "A resident kernel-level anti-cheat driver is running on this system.",
                SupportingEvidence: redact ? RedactionEngine.RedactText(acRow.Value) : acRow.Value,
                WhatWouldFalsify: "DPC/ISR latency capture proving zero interrupted present calls during hitches."));
        }

        var eventsSummary = stutterResult.Events.Select(e => new StutterEventSummary(
            Index: e.FrameIndex,
            TimestampMs: Math.Round(e.TimestampMs, 2),
            FrameTimeMs: Math.Round(e.FrameTimeMs, 2),
            BaselineMedianMs: Math.Round(e.MedianMs, 2),
            ExcessMs: Math.Round(e.ExcessMs, 2),
            Ratio: Math.Round(e.Ratio, 2),
            Severity: e.Severity.ToString())).ToList();

        var matrix = redact ? capabilityMatrix.Redacted() : capabilityMatrix;
        var envEntries = new List<CapabilityEntry>(matrix.Entries.Count);
        foreach (var entry in matrix.Entries)
        {
            if (!redact)
            {
                envEntries.Add(entry);
                continue;
            }

            var val = entry.Value is not null ? RedactionEngine.RedactText(entry.Value) : null;
            var reason = entry.Reason is not null ? RedactionEngine.RedactText(entry.Reason) : null;
            var source = RedactionEngine.RedactText(entry.Source);

            envEntries.Add(entry.Availability == Availability.Available
                ? CapabilityEntry.Available(entry.Id, entry.Title, entry.Tier, source, val ?? "", entry.Provenance, reason, entry.IsSensitive)
                : CapabilityEntry.Unavailable(entry.Id, entry.Title, entry.Tier, entry.Availability, source, reason ?? ""));
        }

        var targetProc = redact && targetProcess is not null ? RedactionEngine.RedactText(targetProcess) : targetProcess;

        var metadata = new IncidentMetadata(
            ReportId: id,
            TimestampUtc: time,
            ToolVersion: version,
            Trigger: trigger,
            TargetProcess: targetProc,
            Provenance: stutterResult.Provenance);

        var report = new IncidentReport(metadata, metrics, hypotheses, envEntries, eventsSummary);

        if (canariesToAssert is not null)
        {
            RedactionEngine.AssertCanariesRedacted(report.ToJson(), canariesToAssert);
        }

        return report;
    }
}
