using System.Globalization;
using Jankscope.Frames.Detection;
using Jankscope.Frames.Ring;

namespace Jankscope.Frames.Incidents;

public enum AnchorKind
{
    /// <summary>A frame that meets the stutter definition (spec section 7.1).</summary>
    StutterEvent = 0,

    /// <summary>The longest frame in the window although it does not meet the definition; offered after every real candidate and labelled so.</summary>
    WeakSpike = 1,

    /// <summary>Rows missing from the capture: time passed with no frame logged. The finding is "we cannot see this stretch".</summary>
    CollectorGap = 2,
}

/// <summary>One moment the person may pick as "where it happened". Rank 1 is the most likely by severity; nothing is selected for them.</summary>
/// <param name="TimestampMs">Capture-clock time of the candidate (the frame's present time, or the start of a gap).</param>
/// <param name="StartMs">Start of the stall (the frame's start, or the start of the gap).</param>
/// <param name="EndMs">End of the stall.</param>
/// <param name="SeverityMs">How long the stall exceeded normal (excess over the median, or the length of the gap).</param>
/// <param name="SecondsBeforeMark">How long before the mark it was; negative when it falls within the clock uncertainty after it.</param>
public sealed record CandidateAnchor(
    int Rank,
    AnchorKind Kind,
    double TimestampMs,
    double StartMs,
    double EndMs,
    double SeverityMs,
    double SecondsBeforeMark,
    long? FrameIndex,
    string Description);

public sealed record AnchorSearchResult(
    IReadOnlyList<CandidateAnchor> Candidates,
    double? WindowStartMs,
    double? WindowEndMs,
    int FramesExamined,
    IReadOnlyList<string> Notes);

/// <param name="WindowMs">How far back from the mark to look (spec section 7.3: about 10 s).</param>
/// <param name="MaxCandidates">Length limit of the list offered.</param>
/// <param name="ContextMs">Extra history before the window the detector may use to judge the first frames of it.</param>
/// <param name="GapMinMs">A missing stretch shorter than this is not reported.</param>
/// <param name="GapMedianFactor">A missing stretch must also exceed this many median frame times.</param>
/// <param name="WeakRatio">A frame below the stutter definition is offered only if at least this many times the median.</param>
/// <param name="WeakMinExcessMs">...and at least this much longer than the median.</param>
public sealed record AnchorSearchOptions(
    double WindowMs = 10_000.0,
    int MaxCandidates = 5,
    double ContextMs = 2_000.0,
    double GapMinMs = 100.0,
    double GapMedianFactor = 5.0,
    double WeakRatio = 1.5,
    double WeakMinExcessMs = 3.0)
{
    public static AnchorSearchOptions Default { get; } = new();

    public void Validate()
    {
        if (!double.IsFinite(WindowMs) || WindowMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(WindowMs), WindowMs, "must be finite and positive");
        }

        if (MaxCandidates < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxCandidates), MaxCandidates, "must be at least 1");
        }

        if (!double.IsFinite(ContextMs) || ContextMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ContextMs), ContextMs, "must be finite and not negative");
        }
    }
}

public enum AnchorChoiceKind
{
    /// <summary>The person picked one of the offered candidates.</summary>
    Candidate = 0,

    /// <summary>The person chose the moment the key was pressed. It is a labelled choice, never a default.</summary>
    KeyPressTime = 1,

    /// <summary>The person placed the event at a time of their own choosing, for example by clicking the timeline.</summary>
    Adjusted = 2,
}

/// <summary>The person's decision about where the event was. A mark has none until the person makes it.</summary>
public sealed record AnchorSelection(AnchorChoiceKind Kind, double TimestampMs, int? CandidateRank, string Description)
{
    public static AnchorSelection FromCandidate(CandidateAnchor candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return new AnchorSelection(AnchorChoiceKind.Candidate, candidate.TimestampMs, candidate.Rank, candidate.Description);
    }

    public static AnchorSelection FromAdjusted(double captureMs) => new(
        AnchorChoiceKind.Adjusted,
        captureMs,
        null,
        "A time the person chose on the timeline.");

    public static AnchorSelection FromKeyPress(CaptureTime markTime) => new(
        AnchorChoiceKind.KeyPressTime,
        markTime.Ms,
        null,
        "The moment the key was pressed. This is not the event itself: a reaction takes a fraction of a second or more, so the event is earlier.");
}

/// <summary>
/// Looks in the seconds before a mark for moments that could be what the person felt (spec section 7.3: the key press is not the event time).
/// It offers candidates and never decides: with nothing notable in the window it says so instead of inventing one.
/// </summary>
public static class CandidateAnchorFinder
{
    public const string InputNote = "Input and device events are not collected in this version, so only frame data was searched.";

    /// <param name="chronological">The frames in capture order.</param>
    /// <param name="markTime">The mark on the capture clock; null when the clock is not known yet.</param>
    public static AnchorSearchResult Find(
        ReadOnlySpan<FrameSample> chronological,
        CaptureTime? markTime,
        AnchorSearchOptions? options = null,
        StutterOptions? stutterOptions = null)
    {
        options ??= AnchorSearchOptions.Default;
        options.Validate();
        stutterOptions ??= StutterOptions.Default;

        if (markTime is not { } mark || !double.IsFinite(mark.Ms) || !double.IsFinite(mark.UncertaintyMs))
        {
            return new AnchorSearchResult(
                [],
                null,
                null,
                0,
                ["The capture clock is not known yet (no frames have arrived), so no candidates can be offered.", InputNote]);
        }

        var windowEnd = mark.Ms + mark.UncertaintyMs;
        var windowStart = mark.Ms - options.WindowMs;
        var withContext = FrameRingExtensions.SelectWindow(chronological, windowEnd, options.WindowMs + options.ContextMs + mark.UncertaintyMs);
        var inWindow = FrameRingExtensions.SelectWindow(chronological, windowEnd, options.WindowMs + mark.UncertaintyMs);

        var candidates = new List<CandidateAnchor>();
        var eventFrames = new HashSet<long>();
        var detected = StutterDetector.Analyze(withContext, stutterOptions);
        foreach (var e in detected.Events)
        {
            if (e.TimestampMs < windowStart || e.TimestampMs > windowEnd)
            {
                continue;
            }

            eventFrames.Add(e.FrameIndex);
            candidates.Add(new CandidateAnchor(
                0,
                AnchorKind.StutterEvent,
                e.TimestampMs,
                e.TimestampMs - e.FrameTimeMs,
                e.TimestampMs,
                e.ExcessMs,
                (mark.Ms - e.TimestampMs) / 1000.0,
                e.FrameIndex,
                $"Frame {Number(e.FrameIndex)}: {Ms(e.FrameTimeMs)} ms, {Ms(e.ExcessMs)} ms longer than the {Ms(e.MedianMs)} ms median ({e.Severity})."));
        }

        var median = MedianFrameTime(inWindow);
        AddCollectorGaps(inWindow, median, mark, options, candidates);
        AddWeakSpike(inWindow, median, eventFrames, mark, options, stutterOptions, candidates);

        var ordered = candidates
            .OrderBy(c => c.Kind == AnchorKind.WeakSpike ? 1 : 0)
            .ThenByDescending(c => c.SeverityMs)
            .ThenBy(c => c.SecondsBeforeMark)
            .Take(options.MaxCandidates)
            .Select((c, i) => c with { Rank = i + 1 })
            .ToList();

        var notes = new List<string>();
        if (ordered.Count == 0)
        {
            notes.Add($"No frame in the previous {Number(options.WindowMs / 1000.0)} s met the stutter definition " +
                $"({Ms(stutterOptions.Multiplier)}x the median and {Ms(stutterOptions.MinExcessMs)} ms more), so no moment is offered. " +
                "If it felt bad, the cause may be a steady slowdown rather than a spike, or something this version does not measure.");
        }

        if (!mark.UncertaintyIsMeaningful)
        {
            notes.Add($"Only {mark.Observations.ToString(CultureInfo.InvariantCulture)} frame(s) had been seen when the key was pressed, too few to estimate how much their delivery delay varies, so the search window was not widened for it.");
        }
        else if (mark.UncertaintyMs >= 20)
        {
            notes.Add($"The mark time is uncertain by about {Ms(mark.UncertaintyMs)} ms because frames reach this program with varying delay.");
        }

        notes.Add(InputNote);
        return new AnchorSearchResult(ordered, windowStart, windowEnd, inWindow.Length, notes);
    }

    private static void AddCollectorGaps(
        ReadOnlySpan<FrameSample> frames, double median, CaptureTime mark, AnchorSearchOptions options, List<CandidateAnchor> into)
    {
        var threshold = Math.Max(options.GapMinMs, options.GapMedianFactor * (double.IsFinite(median) ? median : 0));
        for (var i = 1; i < frames.Length; i++)
        {
            var gap = frames[i].TimestampMs - frames[i - 1].TimestampMs - frames[i].FrameTimeMs;
            if (!double.IsFinite(gap) || gap <= threshold)
            {
                continue;
            }

            var start = frames[i - 1].TimestampMs;
            into.Add(new CandidateAnchor(
                0,
                AnchorKind.CollectorGap,
                start,
                start,
                start + gap,
                gap,
                (mark.Ms - start) / 1000.0,
                null,
                $"Collector gap: no frames were logged for {Ms(gap)} ms after frame {Number(frames[i - 1].Index)}. What happened in that stretch cannot be seen."));
        }
    }

    private static void AddWeakSpike(
        ReadOnlySpan<FrameSample> frames,
        double median,
        HashSet<long> eventFrames,
        CaptureTime mark,
        AnchorSearchOptions options,
        StutterOptions stutterOptions,
        List<CandidateAnchor> into)
    {
        if (!double.IsFinite(median) || median <= 0)
        {
            return;
        }

        var best = -1;
        for (var i = 0; i < frames.Length; i++)
        {
            if (eventFrames.Contains(frames[i].Index) || !double.IsFinite(frames[i].FrameTimeMs))
            {
                continue;
            }

            if (best < 0 || frames[i].FrameTimeMs > frames[best].FrameTimeMs)
            {
                best = i;
            }
        }

        if (best < 0)
        {
            return;
        }

        var frame = frames[best];
        var excess = frame.FrameTimeMs - median;
        if (frame.FrameTimeMs < options.WeakRatio * median || excess < options.WeakMinExcessMs)
        {
            return;
        }

        into.Add(new CandidateAnchor(
            0,
            AnchorKind.WeakSpike,
            frame.TimestampMs,
            frame.TimestampMs - frame.FrameTimeMs,
            frame.TimestampMs,
            excess,
            (mark.Ms - frame.TimestampMs) / 1000.0,
            frame.Index,
            $"Frame {Number(frame.Index)}: {Ms(frame.FrameTimeMs)} ms against a {Ms(median)} ms median; this is below the stutter definition " +
            $"(it needs {Ms(stutterOptions.Multiplier)}x the median and {Ms(stutterOptions.MinExcessMs)} ms more) but is the longest frame in the window."));
    }

    private static double MedianFrameTime(ReadOnlySpan<FrameSample> frames)
    {
        var times = new List<double>(frames.Length);
        foreach (var frame in frames)
        {
            if (double.IsFinite(frame.FrameTimeMs) && frame.FrameTimeMs > 0)
            {
                times.Add(frame.FrameTimeMs);
            }
        }

        if (times.Count == 0)
        {
            return double.NaN;
        }

        times.Sort();
        var middle = times.Count / 2;
        return times.Count % 2 == 1 ? times[middle] : (times[middle - 1] + times[middle]) / 2.0;
    }

    private static string Ms(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);

    private static string Number(double value) => value.ToString("0", CultureInfo.InvariantCulture);
}
