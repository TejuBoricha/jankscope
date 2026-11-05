using Jankscope.Core;
using Jankscope.Frames.Incidents;

namespace Jankscope.Frames.Tests.Incidents;

/// <summary>
/// The edges of the anchor search (independent Stage 1b review, finding 17: mutants of these rules survived). The detector reads some context before the
/// look-back, so a spike a little older than 10 s is detected and has to be left out by the look-back rule itself; a spike far older is never even read.
/// </summary>
public class CandidateAnchorFinderBoundaryTests
{
    private const double Frame = 1000.0 / 60.0;

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

    [Theory]
    [InlineData(9.5, true)]    // inside the 10 s look-back
    [InlineData(10.5, false)]  // just outside it, but inside the context the detector reads
    [InlineData(11.0, false)]
    [InlineData(12.5, false)]
    public void The_look_back_is_ten_seconds_and_a_spike_just_older_than_that_is_not_offered(double secondsBeforeTheMark, bool offered)
    {
        var frames = Stream(40, (1500, 80));
        var spikeMs = frames[1500].TimestampMs;

        var result = CandidateAnchorFinder.Find(frames, new CaptureTime(spikeMs + secondsBeforeTheMark * 1000.0, 0.0, 100, 0));

        Assert.Equal(offered, result.Candidates.Any(c => c.Kind == AnchorKind.StutterEvent));
    }

    [Fact]
    public void The_default_look_back_is_ten_seconds()
    {
        Assert.Equal(10_000.0, AnchorSearchOptions.Default.WindowMs);
    }

    [Theory]
    [InlineData(double.NaN, 0.0)]
    [InlineData(double.PositiveInfinity, 0.0)]
    [InlineData(double.NegativeInfinity, 0.0)]
    [InlineData(25_000.0, double.NaN)]
    [InlineData(25_000.0, double.PositiveInfinity)]
    public void A_mark_time_that_is_not_a_finite_number_gives_no_candidates_and_a_reason_never_a_guess(double ms, double uncertaintyMs)
    {
        var frames = Stream(40, (1500, 80));

        var result = CandidateAnchorFinder.Find(frames, new CaptureTime(ms, uncertaintyMs, 100, 0));

        Assert.Empty(result.Candidates);
        Assert.Contains(result.Notes, n => n.Contains("not known yet"));
    }
}
