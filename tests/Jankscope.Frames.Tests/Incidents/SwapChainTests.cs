using Jankscope.Core;
using Jankscope.Frames.Incidents;

namespace Jankscope.Frames.Tests.Incidents;

/// <summary>
/// A program that presents on two swap chains writes two interleaved series (independent Stage 1b review, finding 7). The review's case: a 144 fps chain plus a 1 fps chain made
/// all five offered candidates the slow chain's 1,000 ms rows. These tests prove the summary, the sentence, and that the finder says so; they also pin that it changes nothing else.
/// </summary>
public class SwapChainTests
{
    private const string Main = "0x103E8";
    private const string Other = "0x20010";

    private static FrameSample Frame(long index, double timestampMs, double frameTimeMs, string? chain) =>
        new(index, timestampMs, frameTimeMs, Provenance: DataProvenance.Fixture, SwapChain: chain);

    /// <summary>
    /// <paramref name="seconds"/> of a 144 fps main chain, plus (when <paramref name="withOverlay"/>) a 1 fps overlay chain whose every frame is 1,000 ms long, merged in time order and
    /// numbered in arrival order, as the capture tool would log them. A spike makes the main-chain frame nearest <paramref name="spikeAtMs"/> 80 ms longer.
    /// </summary>
    private static FrameSample[] TwoChains(int seconds, bool withOverlay, string? overlayChain = Other, string? mainChain = Main, double? spikeAtMs = null, double overlayFrameMs = 1000.0)
    {
        var frames = new List<(double Time, double Length, string? Chain)>();
        var length = 1000.0 / 144.0;
        for (var t = length; t <= seconds * 1000.0; t += length)
        {
            var extra = spikeAtMs is { } at && Math.Abs(t - at) < length / 2 ? 80.0 : 0.0;
            frames.Add((t, length + extra, mainChain));
        }

        if (withOverlay)
        {
            for (var t = 500.0; t <= seconds * 1000.0; t += 1000.0)
            {
                frames.Add((t, overlayFrameMs, overlayChain));
            }
        }

        return frames.OrderBy(f => f.Time).Select((f, i) => Frame(i, f.Time, f.Length, f.Chain)).ToArray();
    }

    private static CaptureTime Mark(double ms) => new(ms, 0, 100, 0);

    // ---- the summary and the sentence ----

    [Fact]
    public void The_summary_counts_frames_per_chain_busiest_first_and_leaves_out_frames_that_name_none()
    {
        var frames = new[]
        {
            Frame(0, 1, 7, Other), Frame(1, 2, 7, Main), Frame(2, 3, 7, Main), Frame(3, 4, 7, null), Frame(4, 5, 7, Main),
        };

        var uses = SwapChainSummary.Summarize(frames);

        Assert.Equal([new SwapChainUse(Main, 3), new SwapChainUse(Other, 1)], uses);
        Assert.Equal(Main, SwapChainSummary.MainChain(uses));
    }

    [Fact]
    public void Equal_chains_are_ordered_by_address_so_the_answer_does_not_depend_on_arrival_order()
    {
        var a = SwapChainSummary.Summarize([Frame(0, 1, 7, Other), Frame(1, 2, 7, Main)]);
        var b = SwapChainSummary.Summarize([Frame(0, 1, 7, Main), Frame(1, 2, 7, Other)]);

        Assert.Equal(a, b);
        Assert.Equal(Main, SwapChainSummary.MainChain(a)); // "0x103E8" sorts before "0x20010"
    }

    [Fact]
    public void With_no_chain_known_the_summary_is_empty_and_there_is_no_main_chain_and_nothing_to_say()
    {
        var uses = SwapChainSummary.Summarize([Frame(0, 1, 7, null), Frame(1, 2, 7, null)]);

        Assert.Empty(uses);
        Assert.Null(SwapChainSummary.MainChain(uses));
        Assert.Null(SwapChainSummary.Notice(uses));
        Assert.Null(SwapChainSummary.Notice(null));
        Assert.Empty(SwapChainSummary.Summarize([]));
    }

    [Fact]
    public void One_chain_has_nothing_to_say()
    {
        Assert.Null(SwapChainSummary.Notice([new SwapChainUse(Main, 1440)]));
    }

    [Fact]
    public void The_sentence_gives_counts_that_add_up_and_never_an_address()
    {
        var two = SwapChainSummary.Notice([new SwapChainUse(Main, 1440), new SwapChainUse(Other, 10)])!;
        var four = SwapChainSummary.Notice([new SwapChainUse(Main, 1000), new SwapChainUse(Other, 20), new SwapChainUse("0x3", 5), new SwapChainUse("0x4", 1)])!;

        Assert.Contains("2 swap chains", two);
        Assert.Contains("1440 frame(s) on the main one and 10 on another", two);
        Assert.Contains("4 swap chains", four);
        Assert.Contains("1000 frame(s) on the main one and 26 on 3 others", four); // 20 + 5 + 1
        Assert.DoesNotContain("0x", two);
        Assert.DoesNotContain("0x", four);
        Assert.Contains("A candidate on a secondary chain says so.", two);
    }

    // ---- the finder ----

    [Fact]
    public void The_reviews_case_is_now_said_in_the_notes_and_on_the_candidates_it_affects()
    {
        // E3 of the review: a 144 fps chain plus a 1 fps chain whose every frame takes 1,000 ms. The slow chain's rows are offered as the worst moments.
        var frames = TwoChains(30, withOverlay: true);

        var result = CandidateAnchorFinder.Find(frames, Mark(frames[^1].TimestampMs));

        Assert.NotEmpty(result.Candidates);
        Assert.Equal(2, result.SwapChains!.Count);
        Assert.Equal(Main, SwapChainSummary.MainChain(result.SwapChains));
        Assert.Contains(result.Notes, n => n.Contains("2 swap chains") && n.Contains("on the main one"));
        var slow = result.Candidates.Where(c => c.FrameIndex is not null && c.SeverityMs > 900).ToList();
        Assert.NotEmpty(slow);
        Assert.All(slow, c => Assert.EndsWith(SwapChainSummary.SecondaryChainTag, c.Description));
    }

    [Fact]
    public void A_real_spike_on_the_main_chain_is_not_tagged_even_when_a_second_chain_exists()
    {
        // A mild overlay (30 ms frames once a second) so that the main chain's 80 ms spike is still among the offered moments; with the review's 1,000 ms
        // overlay rows the real spike is pushed out of the list altogether, which is the problem this change makes visible.
        var frames = TwoChains(30, withOverlay: true, spikeAtMs: 12_000, overlayFrameMs: 30.0);
        var spike = frames.Where(f => f.SwapChain == Main).MaxBy(f => f.FrameTimeMs);

        var result = CandidateAnchorFinder.Find(frames, Mark(spike.TimestampMs + 1500));

        var onMain = result.Candidates.Single(c => c.FrameIndex == spike.Index);
        Assert.DoesNotContain("secondary swap chain", onMain.Description);
        Assert.Equal(1, onMain.Rank); // the real spike is the worst moment
        Assert.Contains(result.Candidates, c => c.FrameIndex is { } i && frames[i].SwapChain == Other && c.Description.EndsWith(SwapChainSummary.SecondaryChainTag, StringComparison.Ordinal));
        Assert.Contains(result.Notes, n => n.Contains("2 swap chains")); // the window still holds two chains, and the person is told
    }

    [Fact]
    public void Candidates_from_a_single_chain_are_untouched_and_no_chain_sentence_appears()
    {
        var frames = TwoChains(30, withOverlay: false, spikeAtMs: 12_000);
        var spike = frames.MaxBy(f => f.FrameTimeMs);

        var result = CandidateAnchorFinder.Find(frames, Mark(spike.TimestampMs + 1500));

        Assert.Single(result.SwapChains!);
        Assert.DoesNotContain(result.Notes, n => n.Contains("swap chain"));
        Assert.DoesNotContain(result.Candidates, c => c.Description.Contains("secondary swap chain"));
        Assert.Equal(spike.Index, result.Candidates[0].FrameIndex);
    }

    [Fact]
    public void A_log_that_does_not_name_chains_gets_no_sentence_and_no_tags()
    {
        var frames = TwoChains(30, withOverlay: true, overlayChain: null, mainChain: null);

        var result = CandidateAnchorFinder.Find(frames, Mark(frames[^1].TimestampMs));

        Assert.Empty(result.SwapChains!);
        Assert.DoesNotContain(result.Notes, n => n.Contains("swap chain"));
        Assert.DoesNotContain(result.Candidates, c => c.Description.Contains("secondary swap chain"));
    }

    [Fact]
    public void Knowing_the_chains_changes_no_candidate_it_only_words_them()
    {
        // The same frames with and without chain names: the same candidates in the same order with the same numbers; only the wording differs.
        var named = TwoChains(30, withOverlay: true);
        var unnamed = named.Select(f => f with { SwapChain = null }).ToArray();
        var mark = Mark(named[^1].TimestampMs);

        var with = CandidateAnchorFinder.Find(named, mark);
        var without = CandidateAnchorFinder.Find(unnamed, mark);

        Assert.Equal(without.Candidates.Count, with.Candidates.Count);
        for (var i = 0; i < with.Candidates.Count; i++)
        {
            Assert.Equal(without.Candidates[i] with { Description = string.Empty }, with.Candidates[i] with { Description = string.Empty });
        }

        Assert.Equal(without.FramesExamined, with.FramesExamined);
    }
}
