using Jankscope.Frames.Detection;

namespace Jankscope.Frames.Tests.Detection;

/// <summary>Tests that came out of the independent Stage 1a review (docs/reviews/stage-1a.md, findings 10 and 20).</summary>
public class StutterDetectorReviewTests
{
    [Fact]
    public void A_timestamp_that_goes_back_by_even_a_microsecond_is_a_clock_discontinuity_that_restarts_the_history()
    {
        // Mutation D3 survived every repo test: a tolerance of a few milliseconds on "goes backwards" went unnoticed.
        var detector = new StutterDetector();
        foreach (var sample in Series.FromFrameTimes(Series.Constant(10.0, 21))) // timestamps 10 ... 210
        {
            detector.Push(sample);
        }

        var verdict = detector.Push(new FrameSample(21, 210.0 - 0.001, 10.0));

        Assert.Equal(1, detector.GetResult().ClockDiscontinuities);
        Assert.Equal(FrameStatus.InsufficientHistory, verdict.Status); // the old window cannot be compared by time any more
    }

    [Fact]
    public void The_same_timestamp_twice_is_not_a_discontinuity()
    {
        var detector = new StutterDetector();
        foreach (var sample in Series.FromFrameTimes(Series.Constant(10.0, 21)))
        {
            detector.Push(sample);
        }

        detector.Push(new FrameSample(21, 210.0, 10.0));

        Assert.Equal(0, detector.GetResult().ClockDiscontinuities);
    }

    [Fact]
    public void A_freeze_as_long_as_the_window_leaves_the_frames_after_it_unjudged_and_counts_them_as_insufficient_history()
    {
        // Documented behaviour (docs/METRICS.md, "known blind spot"): after a frame of at least RollingWindowMs the window holds only that frame, so a second
        // hitch right behind it is not judged. It is counted as insufficient history, never reported as "no stutter".
        var frames = Series.Join(Series.Constant(10.0, 120), [1500.0], Series.Constant(10.0, 8), [120.0]);

        var result = StutterDetector.Analyze(Series.FromFrameTimes(frames));

        var only = Assert.Single(result.Events);
        Assert.Equal(1500.0, only.FrameTimeMs);
        Assert.Equal(19, result.InsufficientHistoryFrames); // 10 of warm-up and 9 after the freeze
        Assert.Equal(result.FramesSeen, result.FramesEvaluated + result.InsufficientHistoryFrames + result.RejectedFrames);
    }
}
