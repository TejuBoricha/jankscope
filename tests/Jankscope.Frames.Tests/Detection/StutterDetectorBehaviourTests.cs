using Jankscope.Frames.Detection;

namespace Jankscope.Frames.Tests.Detection;

public class StutterDetectorBehaviourTests
{
    private static StutterResult Analyze(IReadOnlyList<double> frameTimesMs, StutterOptions? options = null) =>
        StutterDetector.Analyze(Series.FromFrameTimes(frameTimesMs), options);

    // ---- warm-up: "insufficient history" is a status of its own, never "no stutter" ----

    [Fact]
    public void The_first_MinHistoryFrames_frames_are_insufficient_history_and_later_frames_are_judged()
    {
        var detector = new StutterDetector();
        var verdicts = Series.FromFrameTimes(Series.Constant(10.0, 30)).Select(detector.Push).ToArray();

        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(FrameStatus.InsufficientHistory, verdicts[i].Status);
            Assert.True(double.IsNaN(verdicts[i].MedianMs), "no median was computed, so none is reported");
        }

        for (var i = 10; i < 30; i++)
        {
            Assert.Equal(FrameStatus.NoStutter, verdicts[i].Status);
            Assert.Equal(10.0, verdicts[i].MedianMs);
        }

        var result = detector.GetResult();
        Assert.Equal(10L, result.InsufficientHistoryFrames);
        Assert.Equal(20L, result.FramesEvaluated);
        Assert.Equal(30L, result.FramesAccepted);
        Assert.Equal(30L, result.FramesSeen);
        Assert.Empty(result.Events);
    }

    [Fact]
    public void A_spike_during_the_warm_up_is_counted_as_insufficient_history_never_as_a_clean_frame()
    {
        var times = Series.Join(Series.Constant(10.0, 5), [500.0], Series.Constant(10.0, 24));
        var detector = new StutterDetector();
        var verdicts = Series.FromFrameTimes(times).Select(detector.Push).ToArray();

        Assert.Equal(FrameStatus.InsufficientHistory, verdicts[5].Status);
        Assert.NotEqual(FrameStatus.NoStutter, verdicts[5].Status);
        Assert.Empty(detector.GetResult().Events);
        Assert.Equal(10L, detector.GetResult().InsufficientHistoryFrames);
    }

    [Fact]
    public void A_spike_right_after_the_warm_up_is_detected()
    {
        var result = Analyze(Series.Join(Series.Constant(10.0, 10), [30.0], Series.Constant(10.0, 5)));

        var e = Assert.Single(result.Events);
        Assert.Equal(10L, e.FrameIndex); // exactly MinHistoryFrames frames of history
    }

    [Fact]
    public void MinHistoryFrames_counts_frames_not_time_a_slow_series_never_leaves_the_warm_up()
    {
        // 200 ms frames: a 1 s window holds at most 6, fewer than 10. The honest answer is "cannot judge", not "no stutters".
        var result = Analyze(Series.Constant(200.0, 40));

        Assert.Equal(0L, result.FramesEvaluated);
        Assert.Equal(40L, result.InsufficientHistoryFrames);
        Assert.Empty(result.Events);
    }

    [Fact]
    public void A_freeze_longer_than_the_window_is_still_judged_against_the_history_before_it()
    {
        // The window ends where the frame began, so a 3 s freeze cannot empty it and hide itself.
        var result = Analyze(Series.Join(Series.Constant(10.0, 40), [3000.0], Series.Constant(10.0, 40)));

        var e = Assert.Single(result.Events);
        Assert.Equal(40L, e.FrameIndex);
        Assert.Equal(10.0, e.MedianMs);
        Assert.Equal(300.0, e.Ratio);
        Assert.Equal(StutterSeverity.Severe, e.Severity);
    }

    // ---- outliers are never silently dropped ----

    [Fact]
    public void Hitch_frames_stay_in_later_windows()
    {
        // Four 10 ms frames, then a run of 30 ms frames. Each 30 ms frame is judged against a window that still holds
        // every earlier frame. The fifth 30 ms frame sees [10 x4, 30 x4]: median 20, so 30 is no longer 2x. If hitch
        // frames were dropped from the window the median would stay 10 and the fifth frame would be flagged too.
        var options = new StutterOptions(MinHistoryFrames: 4);
        var result = Analyze(Series.Join(Series.Constant(10.0, 4), Series.Constant(30.0, 6)), options);

        Assert.Equal([4L, 5, 6, 7], result.Events.Select(e => e.FrameIndex).ToArray());
        Assert.All(result.Events, e => Assert.Equal(10.0, e.MedianMs));
    }

    [Fact]
    public void One_hitch_does_not_distort_the_median_used_for_the_next_one()
    {
        var result = Analyze(Series.Join(Series.Constant(10.0, 30), [100.0], Series.Constant(10.0, 3), [25.0], Series.Constant(10.0, 5)));

        Assert.Equal([30L, 34], result.Events.Select(e => e.FrameIndex).ToArray());
        Assert.All(result.Events, e => Assert.Equal(10.0, e.MedianMs)); // a mean-based baseline would have drifted up
    }

    // ---- low average FPS is not a stutter ----

    [Fact]
    public void A_steadily_slow_series_has_no_stutters()
    {
        var result = Analyze(Series.Constant(50.0, 400)); // a solid 20 fps

        Assert.Empty(result.Events);
        Assert.True(result.FramesEvaluated > 300);
    }

    [Fact]
    public void A_noisy_slow_series_without_spikes_has_no_stutters()
    {
        var random = new Random(7);
        var times = Enumerable.Range(0, 600).Select(_ => 30.0 + (random.NextDouble() * 40.0)).ToArray(); // 30..70 ms, no outlier beyond 2.3x

        Assert.Empty(Analyze(times).Events);
    }

    [Fact]
    public void Isolated_spikes_in_a_slow_series_are_found_and_the_slow_baseline_is_not()
    {
        var times = Series.Constant(50.0, 100);
        times[40] = 200.0;
        times[70] = 160.0;

        var result = Analyze(times);

        Assert.Equal([40L, 70], result.Events.Select(e => e.FrameIndex).ToArray());
        Assert.Equal(2, result.IsolatedSpikeCount);
    }

    // ---- invalid input is counted with a reason, never thrown and never silently ignored ----

    [Theory]
    [InlineData(double.NaN, FrameRejectionReason.FrameTimeNotFinite)]
    [InlineData(double.PositiveInfinity, FrameRejectionReason.FrameTimeNotFinite)]
    [InlineData(double.NegativeInfinity, FrameRejectionReason.FrameTimeNotFinite)]
    [InlineData(-0.001, FrameRejectionReason.FrameTimeNegative)]
    [InlineData(-16.7, FrameRejectionReason.FrameTimeNegative)]
    [InlineData(0.0, FrameRejectionReason.FrameTimeZero)]
    public void A_bad_frame_time_is_rejected_with_its_reason(double badFrameTime, FrameRejectionReason reason)
    {
        var detector = new StutterDetector();
        foreach (var s in Series.FromFrameTimes(Series.Constant(10.0, 15)))
        {
            detector.Push(s);
        }

        var verdict = detector.Push(new FrameSample(15, 160.0, badFrameTime));

        Assert.Equal(FrameStatus.Rejected, verdict.Status);
        Assert.Equal(reason, verdict.Rejection);
        var result = detector.GetResult();
        Assert.Equal(1L, result.RejectedFrames);
        Assert.Equal(1L, result.RejectedByReason[reason]);
        Assert.Single(result.RejectedByReason);
        Assert.Equal(16L, result.FramesSeen);
        Assert.Equal(15L, result.FramesAccepted);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void A_non_finite_timestamp_is_rejected_with_its_reason(double badTimestamp)
    {
        var detector = new StutterDetector();

        var verdict = detector.Push(new FrameSample(0, badTimestamp, 10.0));

        Assert.Equal(FrameStatus.Rejected, verdict.Status);
        Assert.Equal(FrameRejectionReason.TimestampNotFinite, verdict.Rejection);
        Assert.Equal(1L, detector.GetResult().RejectedByReason[FrameRejectionReason.TimestampNotFinite]);
    }

    [Fact]
    public void Rejected_frames_leave_the_window_and_every_other_verdict_untouched()
    {
        var clean = Series.FromFrameTimes(Series.Join(Series.Constant(10.0, 30), [60.0], Series.Constant(10.0, 10)));
        var dirty = new List<FrameSample>();
        var next = 0L;
        foreach (var s in clean)
        {
            dirty.Add(s with { Index = next++ });
            dirty.Add(new FrameSample(next++, s.TimestampMs, double.NaN));          // garbage after every real frame
            dirty.Add(new FrameSample(next++, s.TimestampMs, -3.0));
            dirty.Add(new FrameSample(next++, s.TimestampMs, 0.0));
        }

        var expected = StutterDetector.Analyze(clean);
        var actual = StutterDetector.Analyze(dirty.ToArray());

        Assert.Equal(expected.Events.Select(e => (e.TimestampMs, e.FrameTimeMs, e.MedianMs, e.ExcessMs)),
                     actual.Events.Select(e => (e.TimestampMs, e.FrameTimeMs, e.MedianMs, e.ExcessMs)));
        Assert.Equal(expected.InsufficientHistoryFrames, actual.InsufficientHistoryFrames);
        Assert.Equal(expected.FramesEvaluated, actual.FramesEvaluated);
        Assert.Equal(clean.Length * 3L, actual.RejectedFrames);
        Assert.Equal(clean.Length * 4L, actual.FramesSeen);
        Assert.Equal(clean.Length, actual.RejectedByReason[FrameRejectionReason.FrameTimeNotFinite]);
        Assert.Equal(clean.Length, actual.RejectedByReason[FrameRejectionReason.FrameTimeNegative]);
        Assert.Equal(clean.Length, actual.RejectedByReason[FrameRejectionReason.FrameTimeZero]);
    }

    [Fact]
    public void A_clock_that_goes_backwards_is_counted_and_restarts_the_history()
    {
        var detector = new StutterDetector();
        foreach (var s in Series.FromFrameTimes(Series.Constant(10.0, 40)))
        {
            detector.Push(s); // timestamps 10..400
        }

        var verdict = detector.Push(new FrameSample(40, 100.0, 10.0)); // jumps back to 100 ms

        Assert.Equal(FrameStatus.InsufficientHistory, verdict.Status);
        Assert.Equal(1L, detector.GetResult().ClockDiscontinuities);

        // History must be rebuilt from the new clock: nothing is judged until 10 frames exist again, then judging resumes.
        var statuses = new List<FrameStatus>();
        for (var i = 1; i <= 12; i++)
        {
            statuses.Add(detector.Push(new FrameSample(40 + i, 100.0 + (i * 10.0), 10.0)).Status);
        }

        Assert.All(statuses.Take(9), s => Assert.Equal(FrameStatus.InsufficientHistory, s));
        Assert.All(statuses.Skip(9), s => Assert.Equal(FrameStatus.NoStutter, s));
        Assert.Equal(1L, detector.GetResult().ClockDiscontinuities);
        Assert.Equal(0L, detector.GetResult().RejectedFrames); // not rejected: counted as a discontinuity instead
    }

    [Fact]
    public void Garbage_never_throws()
    {
        var detector = new StutterDetector();
        double[] nasty = [double.NaN, double.PositiveInfinity, double.NegativeInfinity, double.MaxValue, double.MinValue, double.Epsilon, -0.0, 0.0, 1e-300, 1e300];
        foreach (var a in nasty)
        {
            foreach (var b in nasty)
            {
                detector.Push(new FrameSample(0, a, b));
            }
        }

        Assert.Equal(100L, detector.GetResult().FramesSeen);
    }

    // ---- calibration label ----

    [Fact]
    public void Results_carry_the_calibration_flag_and_a_label_so_grade_caps_can_apply()
    {
        var frames = Series.FromFrameTimes(Series.Constant(10.0, 20));

        var uncalibrated = StutterDetector.Analyze(frames);
        var calibrated = StutterDetector.Analyze(frames, new StutterOptions(Calibrated: true));

        Assert.False(uncalibrated.Calibrated);
        Assert.Equal("uncalibrated", uncalibrated.CalibrationLabel);
        Assert.True(calibrated.Calibrated);
        Assert.Equal("calibrated", calibrated.CalibrationLabel);
        Assert.Equal(2.0, uncalibrated.Options.Multiplier);
    }

    // ---- severity ----

    [Theory]
    [InlineData(4.0, StutterSeverity.Minor)]
    [InlineData(9.999, StutterSeverity.Minor)]
    [InlineData(10.0, StutterSeverity.Moderate)]
    [InlineData(24.999, StutterSeverity.Moderate)]
    [InlineData(25.0, StutterSeverity.Major)]
    [InlineData(49.999, StutterSeverity.Major)]
    [InlineData(50.0, StutterSeverity.Severe)]
    [InlineData(5000.0, StutterSeverity.Severe)]
    public void Severity_tiers_follow_the_excess_over_the_median(double excessMs, StutterSeverity expected)
    {
        Assert.Equal(expected, StutterSeverities.Classify(excessMs));
    }

    [Fact]
    public void Severity_tier_thresholds_are_the_documented_constants()
    {
        Assert.Equal(10.0, StutterSeverities.ModerateExcessMs);
        Assert.Equal(25.0, StutterSeverities.MajorExcessMs);
        Assert.Equal(50.0, StutterSeverities.SevereExcessMs);
    }

    [Fact]
    public void Each_event_carries_the_severity_of_its_own_excess()
    {
        var times = Series.Constant(4.0, 200); // 250 fps, so even a +5 ms frame is more than 2x the median
        times[50] = 4.0 + 5.0;    // +5  ms  Minor
        times[80] = 4.0 + 20.0;   // +20 ms  Moderate
        times[110] = 4.0 + 40.0;  // +40 ms  Major
        times[140] = 4.0 + 90.0;  // +90 ms  Severe

        var result = Analyze(times);

        Assert.Equal([StutterSeverity.Minor, StutterSeverity.Moderate, StutterSeverity.Major, StutterSeverity.Severe],
                     result.Events.Select(e => e.Severity).ToArray());
    }

    // ---- clustering ----

    private static StutterEvent EventAt(double timestampMs, double excessMs = 10.0, long frameIndex = 0) =>
        new(frameIndex, timestampMs, 10.0 + excessMs, 10.0, excessMs, (10.0 + excessMs) / 10.0, StutterSeverities.Classify(excessMs));

    [Fact]
    public void Events_closer_than_the_gap_form_one_cluster_and_an_event_exactly_the_gap_away_starts_a_new_one()
    {
        var events = new[] { EventAt(1000), EventAt(1499.5), EventAt(1999.5), EventAt(2499.5) };

        // 1000 -> 1499.5: 499.5 < 500, same cluster. 1499.5 -> 1999.5: exactly 500, a new cluster. Then 500 again.
        var clusters = StutterClusters.Build(events, 500.0);

        Assert.Equal([2, 1, 1], clusters.Select(c => c.EventCount).ToArray());
    }

    [Fact]
    public void Clustering_chains_events_so_a_long_burst_is_one_cluster()
    {
        var events = new[] { EventAt(0), EventAt(400), EventAt(800), EventAt(1200), EventAt(2000) };

        var clusters = StutterClusters.Build(events, 500.0);

        Assert.Equal(2, clusters.Count);
        Assert.Equal(4, clusters[0].EventCount);
        Assert.Equal(0.0, clusters[0].StartTimestampMs);
        Assert.Equal(1200.0, clusters[0].EndTimestampMs);
        Assert.Equal(1, clusters[1].EventCount);
        Assert.True(clusters[1].IsIsolated);
        Assert.False(clusters[0].IsIsolated);
    }

    [Fact]
    public void A_cluster_summarises_its_events()
    {
        var events = new[]
        {
            EventAt(100, excessMs: 6.0, frameIndex: 10),
            EventAt(200, excessMs: 30.0, frameIndex: 20),
            EventAt(300, excessMs: 12.0, frameIndex: 30),
        };

        var cluster = Assert.Single(StutterClusters.Build(events, 500.0));

        Assert.Equal(0, cluster.FirstEventPosition);
        Assert.Equal(3, cluster.EventCount);
        Assert.Equal(10L, cluster.FirstFrameIndex);
        Assert.Equal(30L, cluster.LastFrameIndex);
        Assert.Equal(100.0, cluster.StartTimestampMs);
        Assert.Equal(300.0, cluster.EndTimestampMs);
        Assert.Equal(48.0, cluster.TotalExcessMs);
        Assert.Equal(30.0, cluster.WorstExcessMs);
        Assert.Equal(StutterSeverity.Major, cluster.Severity); // the worst event decides
    }

    [Fact]
    public void A_gap_of_zero_never_merges_distinct_events_and_a_timestamp_that_goes_back_starts_a_new_cluster()
    {
        Assert.Equal(2, StutterClusters.Build([EventAt(100), EventAt(100.0001)], 0.0).Count);
        Assert.Equal(2, StutterClusters.Build([EventAt(1000), EventAt(900)], 500.0).Count);
        Assert.Empty(StutterClusters.Build([], 500.0));
    }

    [Fact]
    public void Clustering_rejects_invalid_arguments()
    {
        Assert.Throws<ArgumentNullException>(() => StutterClusters.Build(null!, 500.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => StutterClusters.Build([], -1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => StutterClusters.Build([], double.NaN));
    }

    [Fact]
    public void The_detector_result_clusters_use_ClusterGapMs_from_the_options()
    {
        var times = Series.Constant(10.0, 300);
        times[100] = 60.0;
        times[120] = 60.0;  // about 200 ms after the first
        times[250] = 60.0;  // about 1.4 s later

        var result = Analyze(times);

        Assert.Equal(3, result.Events.Count);
        Assert.Equal([2, 1], result.Clusters.Select(c => c.EventCount).ToArray());
        Assert.Equal(1, result.IsolatedSpikeCount);

        var tight = Analyze(times, new StutterOptions(ClusterGapMs: 100.0));
        Assert.Equal([1, 1, 1], tight.Clusters.Select(c => c.EventCount).ToArray());
    }

    [Fact]
    public void Total_excess_is_the_sum_over_all_events()
    {
        var times = Series.Constant(10.0, 100);
        times[40] = 40.0;
        times[80] = 25.0;

        Assert.Equal(45.0, Analyze(times).TotalExcessMs, 9);
    }
}
