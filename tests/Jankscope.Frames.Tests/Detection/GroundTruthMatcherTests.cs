using Jankscope.Core;
using Jankscope.Frames.Detection;

namespace Jankscope.Frames.Tests.Detection;

public class GroundTruthMatcherTests
{
    private const double Base60 = 1000.0 / 60.0;

    private static StutterEvent Event(long frameIndex) =>
        new(frameIndex, frameIndex * 16.7, 50.0, 16.7, 33.3, 3.0, StutterSeverity.Major);

    private static GroundTruthReport Match(InjectedHitch[] injected, long[] eventFrames, int tolerance = 1, StutterOptions? options = null, double baseMs = Base60) =>
        GroundTruthMatcher.Match(injected, eventFrames.Select(Event).ToArray(), tolerance, options ?? StutterOptions.Default, baseMs);

    [Fact]
    public void An_event_on_the_injected_frame_is_detected_with_offset_zero()
    {
        var report = Match([new InjectedHitch(100, 40.0)], [100]);

        var m = Assert.Single(report.Hitches);
        Assert.Equal(HitchOutcome.Detected, m.Outcome);
        Assert.True(m.DetectableByDefinition);
        Assert.Equal(0, m.FrameOffset);
        Assert.Equal(100L, m.MatchedEvent!.Value.FrameIndex);
        Assert.Empty(report.FalsePositives);
        Assert.Equal(1.0, report.Precision);
        Assert.Equal(1.0, report.Recall);
    }

    [Theory]
    [InlineData(99, -1)]
    [InlineData(101, 1)]
    public void An_event_one_frame_away_is_within_a_tolerance_of_one(long eventFrame, int expectedOffset)
    {
        var report = Match([new InjectedHitch(100, 40.0)], [eventFrame]);

        Assert.Equal(HitchOutcome.Detected, report.Hitches[0].Outcome);
        Assert.Equal(expectedOffset, report.Hitches[0].FrameOffset);
        Assert.Empty(report.FalsePositives);
    }

    [Theory]
    [InlineData(98)]
    [InlineData(102)]
    public void An_event_two_frames_away_is_outside_a_tolerance_of_one_so_the_hitch_is_missed_and_the_event_is_a_false_positive(long eventFrame)
    {
        var report = Match([new InjectedHitch(100, 40.0)], [eventFrame]);

        Assert.Equal(HitchOutcome.Missed, report.Hitches[0].Outcome);
        Assert.Null(report.Hitches[0].MatchedEvent);
        Assert.Null(report.Hitches[0].FrameOffset);
        Assert.Equal(eventFrame, Assert.Single(report.FalsePositives).FrameIndex);
        Assert.Equal(0.0, report.Precision);
        Assert.Equal(0.0, report.Recall);
    }

    [Fact]
    public void The_tolerance_is_a_parameter()
    {
        Assert.Equal(HitchOutcome.Detected, Match([new InjectedHitch(100, 40.0)], [103], tolerance: 3).Hitches[0].Outcome);
        Assert.Equal(HitchOutcome.Missed, Match([new InjectedHitch(100, 40.0)], [103], tolerance: 2).Hitches[0].Outcome);
        Assert.Equal(HitchOutcome.Missed, Match([new InjectedHitch(100, 40.0)], [101], tolerance: 0).Hitches[0].Outcome);
    }

    [Fact]
    public void A_hitch_without_any_event_is_missed_and_lowers_recall()
    {
        var report = Match([new InjectedHitch(100, 40.0), new InjectedHitch(200, 40.0)], [200]);

        Assert.Equal([HitchOutcome.Missed, HitchOutcome.Detected], report.Hitches.Select(h => h.Outcome).ToArray());
        Assert.Equal(1, report.DetectedCount);
        Assert.Equal(1, report.MissedCount);
        Assert.Equal(0.5, report.Recall);
        Assert.Equal(1.0, report.Precision);
    }

    [Fact]
    public void An_event_that_matches_no_hitch_is_a_false_positive_and_lowers_precision()
    {
        var report = Match([new InjectedHitch(100, 40.0)], [100, 300, 500, 700]);

        Assert.Equal([300L, 500, 700], report.FalsePositives.Select(e => e.FrameIndex).ToArray());
        Assert.Equal(3, report.FalsePositiveCount);
        Assert.Equal(0.25, report.Precision);
        Assert.Equal(1.0, report.Recall);
    }

    [Fact]
    public void Events_on_a_clean_run_are_all_false_positives_and_with_no_events_nothing_is_reported_as_perfect()
    {
        var withEvents = Match([], [10, 20]);
        Assert.Equal(2, withEvents.FalsePositiveCount);
        Assert.Equal(0.0, withEvents.Precision);
        Assert.Null(withEvents.Recall); // no hitch was injected: recall is undefined, not 100 %

        var clean = Match([], []);
        Assert.Equal(0, clean.FalsePositiveCount);
        Assert.Null(clean.Precision); // nothing was flagged: precision is undefined, not 100 %
        Assert.Null(clean.Recall);
        Assert.Empty(clean.Hitches);
    }

    [Fact]
    public void One_event_cannot_satisfy_two_hitches()
    {
        // Hitches at 100 and 102, a single event at 101 is within +/-1 of both. Only one hitch may claim it.
        var report = Match([new InjectedHitch(100, 40.0), new InjectedHitch(102, 40.0)], [101]);

        Assert.Equal(1, report.DetectedCount);
        Assert.Equal(1, report.MissedCount);
        Assert.Empty(report.FalsePositives);
    }

    [Fact]
    public void Each_hitch_takes_its_nearest_event_and_the_leftover_event_is_a_false_positive()
    {
        var report = Match([new InjectedHitch(100, 40.0)], [99, 100, 101]);

        Assert.Equal(0, report.Hitches[0].FrameOffset);
        Assert.Equal([99L, 101], report.FalsePositives.Select(e => e.FrameIndex).OrderBy(i => i).ToArray());
    }

    [Fact]
    public void Matching_does_not_depend_on_the_order_of_the_inputs()
    {
        var hitches = new[] { new InjectedHitch(300, 40.0), new InjectedHitch(100, 40.0), new InjectedHitch(200, 40.0) };

        var report = Match(hitches, [201, 99, 300]);

        Assert.Equal(3, report.DetectedCount);
        Assert.Equal([300, 100, 200], report.Hitches.Select(h => h.Hitch.FrameIndex).ToArray()); // report keeps the caller's order
        Assert.Equal([0, -1, 1], report.Hitches.Select(h => h.FrameOffset!.Value).ToArray());
    }

    // ---- NotDetectableByDefinition: the extra time cannot reach the detector's thresholds ----

    [Fact]
    public void A_hitch_too_small_for_the_ratio_is_not_detectable_by_definition_and_is_not_a_miss()
    {
        // 60 fps: base 16.67 ms. +15 ms gives 31.67 ms = 1.9x < 2x.
        var report = Match([new InjectedHitch(100, 15.0)], []);

        var m = Assert.Single(report.Hitches);
        Assert.Equal(HitchOutcome.NotDetectableByDefinition, m.Outcome);
        Assert.False(m.DetectableByDefinition);
        Assert.Equal(0, report.MissedCount);
        Assert.Equal(1, report.NotDetectableCount);
        Assert.Equal(0, report.DetectableCount);
        Assert.Null(report.Recall); // no detectable hitch: recall undefined
    }

    [Fact]
    public void A_hitch_too_small_for_the_minimum_excess_is_not_detectable_by_definition()
    {
        // 500 fps: base 2 ms. +3.9 ms is 2.95x but under the 4 ms floor.
        var report = Match([new InjectedHitch(100, 3.9)], [], baseMs: 2.0);

        Assert.Equal(HitchOutcome.NotDetectableByDefinition, report.Hitches[0].Outcome);
    }

    [Theory]
    [InlineData(10.0, 10.0, true)]    // exactly 2x and +10 ms
    [InlineData(10.0, 9.9, false)]    // 1.99x
    [InlineData(2.0, 4.0, true)]      // +4.0 ms exactly (3x)
    [InlineData(2.0, 3.9, false)]     // +3.9 ms
    [InlineData(4.0, 4.0, true)]      // exactly 2x and exactly +4 ms
    [InlineData(1.0, 1.0, false)]     // 2x but +1 ms
    [InlineData(16.0, 100.0, true)]
    [InlineData(16.0, 0.0, false)]
    public void Detectability_follows_the_thresholds_exactly(double baseMs, double extraMs, bool expected)
    {
        Assert.Equal(expected, GroundTruthMatcher.IsDetectableByDefinition(extraMs, StutterOptions.Default, baseMs));

        var report = GroundTruthMatcher.Match([new InjectedHitch(50, extraMs)], [], 1, StutterOptions.Default, baseMs);
        Assert.Equal(expected, report.Hitches[0].DetectableByDefinition);
        Assert.Equal(expected ? HitchOutcome.Missed : HitchOutcome.NotDetectableByDefinition, report.Hitches[0].Outcome);
    }

    [Fact]
    public void Detectability_uses_the_options_it_is_given()
    {
        var relaxed = new StutterOptions(Multiplier: 1.5, MinExcessMs: 2.0);

        Assert.True(GroundTruthMatcher.IsDetectableByDefinition(8.4, relaxed, 16.67));  // 1.5x of 16.67 is +8.335 ms
        Assert.False(GroundTruthMatcher.IsDetectableByDefinition(8.3, relaxed, 16.67));
        Assert.False(GroundTruthMatcher.IsDetectableByDefinition(8.4, StutterOptions.Default, 16.67));
    }

    [Fact]
    public void An_unexpected_detection_of_a_sub_threshold_hitch_is_reported_as_detected_but_flagged_as_not_expected()
    {
        // The detector flagged a hitch the definition says it should not have (jitter pushed it over): never hide that.
        var report = Match([new InjectedHitch(100, 15.0)], [100]);

        var m = Assert.Single(report.Hitches);
        Assert.Equal(HitchOutcome.Detected, m.Outcome);
        Assert.False(m.DetectableByDefinition);
        Assert.Empty(report.FalsePositives);
        Assert.Equal(0, report.DetectableCount);
        Assert.Equal(1.0, report.Precision);
    }

    // ---- argument validation ----

    [Fact]
    public void Invalid_arguments_are_rejected()
    {
        var h = new[] { new InjectedHitch(1, 20.0) };
        var e = Array.Empty<StutterEvent>();

        Assert.Throws<ArgumentNullException>(() => GroundTruthMatcher.Match(null!, e, 1, StutterOptions.Default, 16.7));
        Assert.Throws<ArgumentNullException>(() => GroundTruthMatcher.Match(h, null!, 1, StutterOptions.Default, 16.7));
        Assert.Throws<ArgumentNullException>(() => GroundTruthMatcher.Match(h, e, 1, null!, 16.7));
        Assert.Throws<ArgumentOutOfRangeException>(() => GroundTruthMatcher.Match(h, e, -1, StutterOptions.Default, 16.7));
        Assert.Throws<ArgumentOutOfRangeException>(() => GroundTruthMatcher.Match(h, e, 1, StutterOptions.Default, 0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => GroundTruthMatcher.Match(h, e, 1, StutterOptions.Default, -1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => GroundTruthMatcher.Match(h, e, 1, StutterOptions.Default, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => GroundTruthMatcher.Match(h, e, 1, StutterOptions.Default, double.PositiveInfinity));
    }

    [Fact]
    public void A_hitch_with_a_non_finite_extra_is_rejected_not_classified()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GroundTruthMatcher.IsDetectableByDefinition(double.NaN, StutterOptions.Default, 16.7));
        Assert.Throws<ArgumentOutOfRangeException>(() => GroundTruthMatcher.IsDetectableByDefinition(-1.0, StutterOptions.Default, 16.7));
    }
}
