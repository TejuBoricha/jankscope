using System.Text.Json;

namespace Jankscope.Core.Reports;

public enum IncidentTrigger
{
    UserMarked,
    AutoFlagged,
    SyntheticHarness,
}

public sealed record IncidentMetadata(
    string ReportId,
    DateTimeOffset TimestampUtc,
    string ToolVersion,
    IncidentTrigger Trigger,
    string? TargetProcess,
    DataProvenance Provenance);

public sealed record MetricSummary(
    int TotalSamples,
    double DurationSeconds,
    double? AverageFps,
    double? MedianFrameTimeMs,
    double? P99FrameTimeMs,
    double? P999FrameTimeMs,
    int StutterEventsCount,
    int StutterClustersCount,
    long InsufficientHistoryFrames,
    long ClockDiscontinuities,
    long RejectedFrames);

public sealed record Hypothesis(
    string Name,
    string Grade,
    string Summary,
    string SupportingEvidence,
    string WhatWouldFalsify);

public sealed record StutterEventSummary(
    long Index,
    double TimestampMs,
    double FrameTimeMs,
    double BaselineMedianMs,
    double ExcessMs,
    double Ratio,
    string Severity);

public sealed record IncidentReport(
    IncidentMetadata Metadata,
    MetricSummary Metrics,
    IReadOnlyList<Hypothesis> Hypotheses,
    IReadOnlyList<CapabilityEntry> SystemEnvironment,
    IReadOnlyList<StutterEventSummary> Events)
{
    public string ToJson() => JsonSerializer.Serialize(this, JankscopeJson.Options);
}
