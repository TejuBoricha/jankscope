using Jankscope.Core;

namespace Jankscope.Frames.Detection;

/// <summary>What became of one injected hitch.</summary>
public enum HitchOutcome
{
    /// <summary>A stutter event was found within the tolerance of the injected frame.</summary>
    Detected,

    /// <summary>The definition says the hitch should have been flagged, but no event was found near it.</summary>
    Missed,

    /// <summary>The extra time is too small for the detector's thresholds ever to flag it (see <see cref="GroundTruthMatcher.IsDetectableByDefinition"/>), and no event was found: not a miss.</summary>
    NotDetectableByDefinition,
}

/// <summary>The result for one injected hitch.</summary>
/// <param name="Hitch">The injected hitch.</param>
/// <param name="Outcome">What became of it.</param>
/// <param name="DetectableByDefinition">Whether the thresholds allow this hitch to be flagged at all. A <see cref="HitchOutcome.Detected"/> hitch with this false was flagged although the definition says it should not have been (for example jitter pushed it over the line); that is reported, not hidden.</param>
/// <param name="MatchedEvent">The event matched to it, if any.</param>
/// <param name="FrameOffset">Matched event's frame index minus the injected frame index (negative: flagged early), if matched.</param>
public sealed record HitchMatch(
    InjectedHitch Hitch,
    HitchOutcome Outcome,
    bool DetectableByDefinition,
    StutterEvent? MatchedEvent,
    int? FrameOffset);

/// <summary>Detected-versus-injected comparison. <see cref="Precision"/> and <see cref="Recall"/> are null when undefined (nothing flagged, nothing detectable), never 100 %.</summary>
/// <param name="Hitches">One entry per injected hitch, in the caller's order.</param>
/// <param name="FalsePositives">Events that match no injected hitch, in frame order.</param>
public sealed record GroundTruthReport(
    IReadOnlyList<HitchMatch> Hitches,
    IReadOnlyList<StutterEvent> FalsePositives)
{
    public int DetectedCount => Hitches.Count(h => h.Outcome == HitchOutcome.Detected);

    public int MissedCount => Hitches.Count(h => h.Outcome == HitchOutcome.Missed);

    public int NotDetectableCount => Hitches.Count(h => h.Outcome == HitchOutcome.NotDetectableByDefinition);

    /// <summary>Hitches the thresholds allow the detector to flag.</summary>
    public int DetectableCount => Hitches.Count(h => h.DetectableByDefinition);

    public int FalsePositiveCount => FalsePositives.Count;

    /// <summary>Events that were matched to an injected hitch.</summary>
    public int TruePositiveCount => Hitches.Count(h => h.MatchedEvent is not null);

    /// <summary>Matched events over all events; null when no event was flagged.</summary>
    public double? Precision => TruePositiveCount + FalsePositiveCount == 0
        ? null
        : (double)TruePositiveCount / (TruePositiveCount + FalsePositiveCount);

    /// <summary>Detected hitches among those the definition allows the detector to flag, over all such hitches; null when there are none.</summary>
    public double? Recall => DetectableCount == 0
        ? null
        : (double)Hitches.Count(h => h.DetectableByDefinition && h.Outcome == HitchOutcome.Detected) / DetectableCount;
}

/// <summary>Compares the stutter events a detector found with the hitches a synthetic workload injected (spec section 18: ground truth).</summary>
public static class GroundTruthMatcher
{
    /// <summary>
    /// Matches events to hitches one to one: an event is a candidate for a hitch when its frame index is within
    /// <paramref name="toleranceFrames"/> of the hitch's; candidates are taken nearest first, so each event satisfies at most one hitch and
    /// each hitch takes at most one event. A hitch with no event is <see cref="HitchOutcome.Missed"/> if the definition allows it to be
    /// flagged and <see cref="HitchOutcome.NotDetectableByDefinition"/> otherwise. Events left over are false positives.
    /// </summary>
    /// <param name="injected">The ground truth.</param>
    /// <param name="detected">The events found by the detector.</param>
    /// <param name="toleranceFrames">Largest accepted distance in frames (the exit criterion uses 1).</param>
    /// <param name="options">The options the detector ran with; they decide what is detectable.</param>
    /// <param name="baseFrameTimeMs">The nominal frame time the hitches were injected on (1000 / target fps).</param>
    public static GroundTruthReport Match(
        IReadOnlyList<InjectedHitch> injected,
        IReadOnlyList<StutterEvent> detected,
        int toleranceFrames,
        StutterOptions options,
        double baseFrameTimeMs)
    {
        ArgumentNullException.ThrowIfNull(injected);
        ArgumentNullException.ThrowIfNull(detected);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegative(toleranceFrames);
        options.Validate();
        ValidateBase(baseFrameTimeMs);

        var events = detected.OrderBy(e => e.FrameIndex).ToArray();

        // Every (hitch, event) pair within the tolerance, nearest first; ties go to the earlier hitch, then the earlier event.
        var candidates = new List<(long Distance, int Hitch, int Event)>();
        for (var h = 0; h < injected.Count; h++)
        {
            var frame = (long)injected[h].FrameIndex;
            for (var e = LowerBound(events, frame - toleranceFrames); e < events.Length && events[e].FrameIndex <= frame + toleranceFrames; e++)
            {
                candidates.Add((Math.Abs(events[e].FrameIndex - frame), h, e));
            }
        }

        candidates.Sort();

        var eventOfHitch = new int[injected.Count];
        Array.Fill(eventOfHitch, -1);
        var eventTaken = new bool[events.Length];
        foreach (var (_, h, e) in candidates)
        {
            if (eventOfHitch[h] < 0 && !eventTaken[e])
            {
                eventOfHitch[h] = e;
                eventTaken[e] = true;
            }
        }

        var matches = new List<HitchMatch>(injected.Count);
        for (var h = 0; h < injected.Count; h++)
        {
            var hitch = injected[h];
            var detectable = IsDetectableByDefinition(hitch.ExtraMs, options, baseFrameTimeMs);
            if (eventOfHitch[h] >= 0)
            {
                var matched = events[eventOfHitch[h]];
                matches.Add(new HitchMatch(hitch, HitchOutcome.Detected, detectable, matched, (int)(matched.FrameIndex - hitch.FrameIndex)));
            }
            else
            {
                matches.Add(new HitchMatch(hitch, detectable ? HitchOutcome.Missed : HitchOutcome.NotDetectableByDefinition, detectable, null, null));
            }
        }

        var falsePositives = events.Where((_, e) => !eventTaken[e]).ToArray();
        return new GroundTruthReport(matches, falsePositives);
    }

    /// <summary>
    /// Whether a hitch of <paramref name="extraMs"/> on a frame of nominal <paramref name="baseFrameTimeMs"/> can ever be flagged: the
    /// frame then takes base + extra against a median of about base, so it needs extra &gt;= (Multiplier - 1) x base AND extra &gt;=
    /// MinExcessMs. (Jitter moves a hitch that sits within about its own size of the line either way; such a hitch is reported as
    /// what the detector actually did, never forced into a class.)
    /// </summary>
    public static bool IsDetectableByDefinition(double extraMs, StutterOptions options, double baseFrameTimeMs)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        ValidateBase(baseFrameTimeMs);
        if (!double.IsFinite(extraMs) || extraMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(extraMs), extraMs, "must be finite and not negative");
        }

        var required = Math.Max((options.Multiplier - 1.0) * baseFrameTimeMs, options.MinExcessMs);
        return extraMs + StutterDetector.ToleranceMs >= required;
    }

    private static void ValidateBase(double baseFrameTimeMs)
    {
        if (!double.IsFinite(baseFrameTimeMs) || baseFrameTimeMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(baseFrameTimeMs), baseFrameTimeMs, "must be finite and positive");
        }
    }

    private static int LowerBound(StutterEvent[] sortedByFrame, long frameIndex)
    {
        int low = 0, high = sortedByFrame.Length;
        while (low < high)
        {
            var mid = low + ((high - low) / 2);
            if (sortedByFrame[mid].FrameIndex < frameIndex)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        return low;
    }
}
