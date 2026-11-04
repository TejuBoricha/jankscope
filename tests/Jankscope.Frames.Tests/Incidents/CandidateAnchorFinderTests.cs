using Jankscope.Core;
using Jankscope.Frames.Incidents;

namespace Jankscope.Frames.Tests.Incidents;

public class CandidateAnchorFinderTests
{
    private const double Frame = 1000.0 / 60.0;

    /// <summary>60 fps for <paramref name="seconds"/>, with the given frames lengthened (index, extra ms). Timestamps are present times, as PresentMon logs them.</summary>
    private static FrameSample[] Stream(double seconds, params (int Index, double ExtraMs)[] spikes)
    {
        var count = (int)(seconds * 60);
        var frames = new FrameSample[count];
        var extra = spikes.ToDictionary(s => s.Index, s => s.ExtraMs);
        var clock = 0.0;
        for (var i = 0; i < count; i++)
        {
            var length = Frame + (extra.TryGetValue(i, out var e) ? e : 0.0);
            clock += length;
            frames[i] = new FrameSample(i, clock, length, Provenance: DataProvenance.Fixture);
        }

        return frames;
    }

    private static CaptureTime Mark(double ms, double uncertaintyMs = 0) => new(ms, uncertaintyMs, 100, 0);

    [Fact]
    public void A_spike_a_little_before_the_mark_is_offered_first_with_its_distance_and_size()
    {
        var frames = Stream(30, (1000, 80)); // a long frame near t = 17 s
        var spikeTime = frames[1000].TimestampMs;
        var result = CandidateAnchorFinder.Find(frames, Mark(spikeTime + 1200));

        var first = Assert.Single(result.Candidates, c => c.Kind == AnchorKind.StutterEvent);
        Assert.Equal(1, first.Rank);
        Assert.Equal(spikeTime, first.TimestampMs, 3);
        Assert.Equal(80.0, first.SeverityMs, 3);
        Assert.Equal(1.2, first.SecondsBeforeMark, 3);
        Assert.Equal(1000, first.FrameIndex);
        Assert.Contains("80", first.Description);
    }

    [Fact]
    public void The_biggest_spike_ranks_first_and_ties_prefer_the_one_closer_to_the_mark()
    {
        var frames = Stream(30, (600, 30), (700, 90), (900, 90), (950, 40));
        var mark = frames[1000].TimestampMs;
        var result = CandidateAnchorFinder.Find(frames, Mark(mark));

        var ranked = result.Candidates.Where(c => c.Kind == AnchorKind.StutterEvent).ToArray();
        Assert.Equal(new long?[] { 900, 700, 950, 600 }, ranked.Select(c => c.FrameIndex).ToArray());
        Assert.Equal(new[] { 1, 2, 3, 4 }, ranked.Select(c => c.Rank).ToArray());
    }

    [Fact]
    public void Spikes_older_than_the_window_or_after_the_mark_are_not_offered()
    {
        var frames = Stream(40, (300, 100), (1500, 100), (2000, 100));
        var mark = frames[1700].TimestampMs;
        var result = CandidateAnchorFinder.Find(frames, Mark(mark));

        Assert.Equal(new long?[] { 1500 }, result.Candidates.Select(c => c.FrameIndex).ToArray());
    }

    [Fact]
    public void A_spike_just_after_the_mark_is_allowed_only_within_the_clock_uncertainty()
    {
        var frames = Stream(30, (1000, 100));
        var spike = frames[1000].TimestampMs;

        var tooTight = CandidateAnchorFinder.Find(frames, Mark(spike - 300, uncertaintyMs: 100));
        Assert.DoesNotContain(tooTight.Candidates, c => c.Kind == AnchorKind.StutterEvent);

        var wideEnough = CandidateAnchorFinder.Find(frames, Mark(spike - 300, uncertaintyMs: 400));
        Assert.Contains(wideEnough.Candidates, c => c.Kind == AnchorKind.StutterEvent);
    }

    [Fact]
    public void A_clean_stream_yields_no_candidates_and_says_so()
    {
        var frames = Stream(30);
        var result = CandidateAnchorFinder.Find(frames, Mark(frames[1500].TimestampMs));

        Assert.Empty(result.Candidates);
        Assert.Contains(result.Notes, n => n.Contains("No frame", StringComparison.Ordinal));
        Assert.True(result.FramesExamined > 100);
    }

    [Fact]
    public void A_missing_stretch_of_rows_is_a_collector_gap_candidate()
    {
        // 90 frames vanish between index 800 and 891: timestamps jump while the frame times stay normal.
        var original = Stream(30);
        var kept = original.Where(f => f.Index < 800 || f.Index >= 890).ToArray();
        var mark = original[1000].TimestampMs; // about 16.7 s; the gap (13.3 s to 14.8 s) is inside the ten seconds before it
        var result = CandidateAnchorFinder.Find(kept, Mark(mark));

        var gap = Assert.Single(result.Candidates, c => c.Kind == AnchorKind.CollectorGap);
        Assert.InRange(gap.SeverityMs, 1400, 1600);
        Assert.Contains("gap", gap.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_largest_frame_below_the_stutter_definition_is_offered_only_as_a_weak_candidate_after_the_real_ones()
    {
        var frames = Stream(30, (800, 12)); // 28.7 ms against 16.7: 1.7x and +12 ms is not 2x
        var mark = frames[900].TimestampMs;
        var weakOnly = CandidateAnchorFinder.Find(frames, Mark(mark));
        var weak = Assert.Single(weakOnly.Candidates);
        Assert.Equal(AnchorKind.WeakSpike, weak.Kind);
        Assert.Contains("below the stutter definition", weak.Description, StringComparison.OrdinalIgnoreCase);

        var mixed = Stream(30, (800, 12), (850, 60));
        var both = CandidateAnchorFinder.Find(mixed, Mark(mixed[900].TimestampMs));
        Assert.Equal([AnchorKind.StutterEvent, AnchorKind.WeakSpike], both.Candidates.Select(c => c.Kind).ToArray());
    }

    [Fact]
    public void Ordinary_frame_time_variation_is_never_offered_as_a_candidate()
    {
        var rng = new Random(7);
        var frames = new FrameSample[1800];
        var clock = 0.0;
        for (var i = 0; i < frames.Length; i++)
        {
            var length = Frame + (rng.NextDouble() - 0.5) * 2.0; // +-1 ms jitter
            clock += length;
            frames[i] = new FrameSample(i, clock, length, Provenance: DataProvenance.Fixture);
        }

        var result = CandidateAnchorFinder.Find(frames, Mark(frames[1500].TimestampMs));
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void The_list_is_capped_and_keeps_the_best()
    {
        var spikes = Enumerable.Range(0, 10).Select(i => (Index: 700 + i * 20, ExtraMs: 40.0 + i)).ToArray();
        var frames = Stream(30, spikes);
        var result = CandidateAnchorFinder.Find(frames, Mark(frames[1000].TimestampMs), new AnchorSearchOptions(MaxCandidates: 3));

        Assert.Equal(3, result.Candidates.Count);
        Assert.Equal([49.0, 48.0, 47.0], result.Candidates.Select(c => Math.Round(c.SeverityMs)).ToArray());
    }

    [Fact]
    public void An_unknown_capture_clock_gives_no_candidates_and_a_reason_never_a_guess()
    {
        var frames = Stream(10, (200, 100));
        var result = CandidateAnchorFinder.Find(frames, markTime: null);

        Assert.Empty(result.Candidates);
        Assert.Contains(result.Notes, n => n.Contains("clock", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Input_and_device_events_are_stated_as_not_searched()
    {
        var frames = Stream(30, (1000, 80));
        var result = CandidateAnchorFinder.Find(frames, Mark(frames[1000].TimestampMs + 500));
        Assert.Contains(result.Notes, n => n.Contains("input", StringComparison.OrdinalIgnoreCase) && n.Contains("not collected", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_input_is_not_modified_and_an_empty_input_is_fine()
    {
        var frames = Stream(20, (500, 70));
        var copy = frames.ToArray();
        _ = CandidateAnchorFinder.Find(frames, Mark(frames[600].TimestampMs));
        Assert.Equal(copy, frames);

        var empty = CandidateAnchorFinder.Find([], Mark(1000));
        Assert.Empty(empty.Candidates);
        Assert.Equal(0, empty.FramesExamined);
    }

    [Fact]
    public void Choosing_an_anchor_is_explicit_and_the_key_press_is_a_distinct_labelled_choice()
    {
        var frames = Stream(30, (1000, 80));
        var mark = Mark(frames[1000].TimestampMs + 700, 12);
        var candidate = CandidateAnchorFinder.Find(frames, mark).Candidates[0];

        var picked = AnchorSelection.FromCandidate(candidate);
        Assert.Equal(AnchorChoiceKind.Candidate, picked.Kind);
        Assert.Equal(candidate.TimestampMs, picked.TimestampMs);
        Assert.Equal(1, picked.CandidateRank);

        var pressed = AnchorSelection.FromKeyPress(mark);
        Assert.Equal(AnchorChoiceKind.KeyPressTime, pressed.Kind);
        Assert.Equal(mark.Ms, pressed.TimestampMs);
        Assert.Null(pressed.CandidateRank);
        Assert.Contains("key", pressed.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reaction", pressed.Description, StringComparison.OrdinalIgnoreCase);
    }
}
