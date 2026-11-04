using System.Globalization;
using Jankscope.Core;
using Jankscope.Core.Reports;
using Jankscope.Frames.Detection;
using Jankscope.Frames.Reports;

namespace Jankscope.Frames.Incidents;

public sealed record BundleMark(
    string Id,
    string Kind,
    string KindNote,
    DateTimeOffset PressedUtc,
    double? CaptureTimeMs,
    double? CaptureUncertaintyMs,
    string? TargetProcess,
    string? Note);

public sealed record BundleAnchors(
    string SelectionStatus,
    AnchorSelection? Selected,
    IReadOnlyList<CandidateAnchor> Candidates,
    IReadOnlyList<string> Notes);

/// <summary>Per-frame series of the incident window: capture time and frame time, both in milliseconds, three decimals.</summary>
public sealed record BundleFrames(string Unit, int Count, long? FirstIndex, double[] TimestampsMs, double[] FrameTimesMs);

public sealed record BundleDataQuality(
    bool PostRollComplete,
    double RequestedPreRollSeconds,
    double AvailablePreRollSeconds,
    double RequestedPostRollSeconds,
    double PostRollCollectedSeconds,
    long DroppedFrames,
    long RejectedFrames,
    string StutterDefinition,
    string Calibration,
    IReadOnlyList<string> Notes);

/// <summary>
/// The exported incident: the diagnosis report for the window, the mark with its candidate anchors and what the person chose, the frame series, and an
/// honest account of what is missing. Redaction and the guard happen in <see cref="ExportPipeline"/>, not here.
/// </summary>
public sealed record IncidentBundle(
    string Schema,
    IncidentReport Report,
    BundleMark Mark,
    BundleAnchors Anchors,
    BundleFrames Frames,
    BundleDataQuality DataQuality,
    string? ProvenanceNotice);

public static class IncidentBundleBuilder
{
    public const string SchemaId = "jankscope.incident/1";

    /// <summary>
    /// Builds the bundle for a mark. Throws <see cref="InvalidOperationException"/> when the window holds no usable frame: there is nothing to export.
    /// </summary>
    /// <param name="droppedFrames">Frames the capture pipeline dropped (queue overflow); stated in the data-quality block.</param>
    /// <param name="redactSensitiveRows">Applies the capability matrix's own sensitive-row policy (for example the locale). Free text is redacted later by the pipeline.</param>
    public static IncidentBundle Build(
        IncidentMark mark,
        CollectedWindow window,
        CapabilityMatrix matrix,
        long droppedFrames = 0,
        bool redactSensitiveRows = true,
        StutterOptions? stutterOptions = null)
    {
        ArgumentNullException.ThrowIfNull(mark);
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(matrix);

        var all = window.Frames.ToSamples();
        var usable = all.Where(s => double.IsFinite(s.FrameTimeMs) && s.FrameTimeMs > 0 && double.IsFinite(s.TimestampMs)).ToArray();
        if (usable.Length == 0)
        {
            throw new InvalidOperationException("The window holds no usable frame, so there is nothing to export.");
        }

        var options = stutterOptions ?? StutterOptions.Default;
        var stutter = StutterDetector.Analyze(usable, options);
        var provenance = window.Frames.Provenance;
        var trigger = provenance == DataProvenance.Simulated ? IncidentTrigger.SyntheticHarness : IncidentTrigger.UserMarked;
        var report = IncidentReportBuilder.Build(
            trigger,
            mark.TargetProcess,
            usable,
            stutter,
            redactSensitiveRows ? matrix.Redacted() : matrix,
            reportId: mark.Id,
            timestampUtc: mark.PressedUtc,
            redact: false);

        var rejected = all.Length - usable.Length;
        var notes = new List<string>();
        if (mark.Kind == MarkKind.InputStall)
        {
            notes.Add("This is an input-stall mark. Input timing (Raw Input) is not collected in this version, so the export holds the time of the mark and the frame data around it.");
        }

        if (mark.Window.PreRollShort)
        {
            notes.Add($"Only {Number(mark.Window.AvailablePreRollSeconds)} s of the requested {Number(mark.Window.RequestedPreRollSeconds)} s of pre-roll existed when the mark was made; the window was not padded.");
        }

        if (!window.PostRollComplete)
        {
            notes.Add($"The post-roll is incomplete: {Number(window.PostRollCollectedSeconds)} s of the requested {Number(mark.Window.PostRollSeconds)} s were collected.");
        }

        if (droppedFrames > 0)
        {
            notes.Add($"{droppedFrames.ToString(CultureInfo.InvariantCulture)} frame(s) were dropped by the capture pipeline during this session; the series may have gaps.");
        }

        if (rejected > 0)
        {
            notes.Add($"{rejected.ToString(CultureInfo.InvariantCulture)} frame(s) with an unusable frame time were left out of the statistics and the series.");
        }

        if (mark.CaptureTime is { } time && time.UncertaintyMs >= 20)
        {
            notes.Add($"The mark time on the capture clock is uncertain by about {Number(time.UncertaintyMs)} ms (frame delivery jitter); the event itself is earlier by the delivery delay and the person's reaction time.");
        }

        notes.Add("The stutter thresholds are the uncalibrated defaults; grades derived from them are capped until a baseline exists.");

        var anchors = new BundleAnchors(
            SelectionStatus(mark.Selection),
            mark.Selection,
            mark.Search.Candidates,
            mark.Search.Notes);

        var frames = new BundleFrames(
            "milliseconds",
            usable.Length,
            usable[0].Index,
            usable.Select(s => Math.Round(s.TimestampMs, 3)).ToArray(),
            usable.Select(s => Math.Round(s.FrameTimeMs, 3)).ToArray());

        var quality = new BundleDataQuality(
            window.PostRollComplete,
            mark.Window.RequestedPreRollSeconds,
            Math.Round(mark.Window.AvailablePreRollSeconds, 1),
            mark.Window.PostRollSeconds,
            Math.Round(window.PostRollCollectedSeconds, 1),
            droppedFrames,
            rejected,
            $"a frame is a stutter if it takes at least {Number(options.Multiplier)}x the median of the previous {Number(options.RollingWindowMs)} ms and at least {Number(options.MinExcessMs)} ms more",
            stutter.CalibrationLabel,
            notes);

        var bundleMark = new BundleMark(
            mark.Id,
            mark.Kind == MarkKind.Stutter ? "stutter" : "input stall",
            mark.Kind == MarkKind.Stutter ? "The person pressed the stutter key after feeling a stutter." : "The person pressed the input-stall key after input froze.",
            mark.PressedUtc,
            mark.CaptureTime is { } t ? Math.Round(t.Ms, 3) : null,
            mark.CaptureTime is { } u ? Math.Round(u.UncertaintyMs, 3) : null,
            mark.TargetProcess,
            mark.Note);

        return new IncidentBundle(SchemaId, report, bundleMark, anchors, frames, quality, ProvenanceNotice(provenance));
    }

    public static string SelectionStatus(AnchorSelection? selection) => selection?.Kind switch
    {
        null => "No event time was chosen. The moment the key was pressed was not used as the event time.",
        AnchorChoiceKind.Candidate => $"The person chose candidate {selection.CandidateRank?.ToString(CultureInfo.InvariantCulture)} as the event time.",
        AnchorChoiceKind.KeyPressTime => "The person chose the moment the key was pressed as the event time (the real event is earlier by their reaction time).",
        AnchorChoiceKind.Adjusted => "The person chose a time on the timeline as the event time.",
        _ => "The event time choice is not recognised.",
    };

    public static string? ProvenanceNotice(DataProvenance provenance) => provenance switch
    {
        DataProvenance.Measured or DataProvenance.Inferred => null,
        DataProvenance.Simulated => "SIMULATED data produced by the synthetic test harness. It is not a measurement of a real game and must not be presented as one (HC-9).",
        DataProvenance.Fixture => "FIXTURE data (a recorded or hand-written test file). It is not a measurement of this session and must not be presented as one (HC-9).",
        DataProvenance.Assumed => "ASSUMED data: the frames were built by hand without a source. It is not a measurement and must not be presented as one (HC-9).",
        _ => "The provenance of this data is not known, so it must not be presented as a measurement (HC-9).",
    };

    private static string Number(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);
}
