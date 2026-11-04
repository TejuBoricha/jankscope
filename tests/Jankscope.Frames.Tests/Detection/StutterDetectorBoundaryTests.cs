using Jankscope.Frames.Detection;

namespace Jankscope.Frames.Tests.Detection;

/// <summary>The exact edges of the definition (docs/METRICS.md): frame &gt;= 2x the rolling median AND &gt;= 4 ms above it.</summary>
public class StutterDetectorBoundaryTests
{
    /// <summary>Twenty frames of <paramref name="baselineMs"/> followed by one probe frame; returns the analysis.</summary>
    private static StutterResult Probe(double baselineMs, double probeMs, StutterOptions? options = null) =>
        StutterDetector.Analyze(Series.FromFrameTimes(Series.Join(Series.Constant(baselineMs, 20), [probeMs])), options);

    private static void AssertFlagged(StutterResult result, double baselineMs, double probeMs)
    {
        var e = Assert.Single(result.Events);
        Assert.Equal(20L, e.FrameIndex);
        Assert.Equal(probeMs, e.FrameTimeMs);
        Assert.Equal(baselineMs, e.MedianMs);
    }

    [Fact]
    public void Exactly_two_times_the_median_and_far_more_than_four_ms_above_it_is_a_stutter()
    {
        AssertFlagged(Probe(10.0, 20.0), 10.0, 20.0);
    }

    [Fact]
    public void One_point_nine_nine_times_the_median_is_not_a_stutter()
    {
        var result = Probe(10.0, 19.9);

        Assert.Empty(result.Events); // 9.9 ms above the median, but only 1.99x
    }

    [Fact]
    public void Exactly_four_ms_above_the_median_with_ratio_to_spare_is_a_stutter()
    {
        AssertFlagged(Probe(2.0, 6.0), 2.0, 6.0); // 3x, +4.0 ms
    }

    [Fact]
    public void Three_point_nine_ms_above_the_median_is_not_a_stutter_even_though_the_ratio_is_high()
    {
        var result = Probe(2.0, 5.9);

        Assert.Empty(result.Events); // 2.95x, but only +3.9 ms
    }

    [Fact]
    public void Both_conditions_must_hold_exactly_two_times_but_only_one_ms_above_is_not_a_stutter()
    {
        Assert.Empty(Probe(1.0, 2.0).Events); // 2.0x, +1 ms
    }

    [Fact]
    public void Exactly_two_times_and_exactly_four_ms_above_is_a_stutter()
    {
        AssertFlagged(Probe(4.0, 8.0), 4.0, 8.0); // 2.0x and +4.0 ms: both edges at once
    }

    [Fact]
    public void Decimal_values_that_are_exactly_on_the_edge_are_not_lost_to_binary_rounding()
    {
        // 6.1 - 2.1 evaluates to 3.9999999999999996 in binary floating point; the data says "exactly +4.0 ms".
        Assert.True(6.1 - 2.1 < 4.0, "premise: the subtraction really is below 4.0 in IEEE arithmetic");
        AssertFlagged(Probe(2.1, 6.1), 2.1, 6.1);
    }

    [Fact]
    public void The_rounding_tolerance_is_far_smaller_than_any_real_difference()
    {
        Assert.Empty(Probe(2.0, 5.999999).Events);   // +3.999999 ms
        Assert.Empty(Probe(10.0, 19.999999).Events); // 1.9999999x
        Assert.Equal(1e-9, StutterDetector.ToleranceMs);
    }

    [Fact]
    public void The_multiplier_and_the_minimum_excess_come_from_the_options()
    {
        var relaxed = new StutterOptions(Multiplier: 1.5, MinExcessMs: 2.0);

        AssertFlagged(Probe(10.0, 15.0, relaxed), 10.0, 15.0); // 1.5x, +5 ms
        Assert.Empty(Probe(10.0, 14.9, relaxed).Events);        // 1.49x
        Assert.Empty(Probe(1.0, 1.9, relaxed).Events);          // 1.9x but +0.9 ms
        Assert.Empty(Probe(10.0, 15.0, StutterOptions.Default).Events);
    }

    [Theory]
    [InlineData(20.0, 10.0, true)]
    [InlineData(19.9, 10.0, false)]
    [InlineData(6.0, 2.0, true)]
    [InlineData(5.9, 2.0, false)]
    [InlineData(2.0, 1.0, false)]
    [InlineData(8.0, 4.0, true)]
    [InlineData(10.0, 10.0, false)]
    [InlineData(1.0, 10.0, false)]
    public void IsStutter_is_the_single_definition_of_the_rule(double frameMs, double medianMs, bool expected)
    {
        Assert.Equal(expected, StutterDetector.IsStutter(frameMs, medianMs, StutterOptions.Default));
    }

    [Fact]
    public void The_median_excludes_the_frame_being_judged()
    {
        // Window [10, 10, 30, 30] has median 20. Judged against it, 40 is exactly 2.0x and +20 ms: a stutter.
        // If the frame itself were in the window the median would be 30 and 40 would be an ordinary frame.
        var options = new StutterOptions(MinHistoryFrames: 4);
        var result = StutterDetector.Analyze(Series.FromFrameTimes([10.0, 10.0, 30.0, 30.0, 40.0]), options);

        var e = Assert.Single(result.Events);
        Assert.Equal(4L, e.FrameIndex);
        Assert.Equal(20.0, e.MedianMs);
        Assert.Equal(20.0, e.ExcessMs);
        Assert.Equal(2.0, e.Ratio);
    }

    [Fact]
    public void A_stutter_event_reports_the_numbers_it_was_judged_on()
    {
        var result = Probe(10.0, 45.0);

        var e = Assert.Single(result.Events);
        Assert.Equal(20L, e.FrameIndex);
        Assert.Equal(245.0, e.TimestampMs, 9); // 20 frames of 10 ms, then 45 ms (cumulative sum)
        Assert.Equal(45.0, e.FrameTimeMs);
        Assert.Equal(10.0, e.MedianMs);
        Assert.Equal(35.0, e.ExcessMs);
        Assert.Equal(4.5, e.Ratio);
        Assert.Equal(StutterSeverity.Major, e.Severity);
    }
}
