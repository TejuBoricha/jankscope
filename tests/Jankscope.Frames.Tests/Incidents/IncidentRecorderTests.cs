using Jankscope.Core;
using Jankscope.Frames.Incidents;
using Jankscope.Frames.Ring;
using Jankscope.Frames.Tests.PresentMon;

namespace Jankscope.Frames.Tests.Incidents;

/// <summary>A simulated live capture: frames are presented on a 60 fps schedule and reach the recorder 30 ms later, on a clock the test controls.</summary>
internal sealed class RecorderRig
{
    public const double Frame = 1000.0 / 60.0;
    private const double DeliveryDelayMs = 30.0;
    private double _presentMs;
    private long _index;

    public RecorderRig(int ringCapacity = 65_536, IncidentRecorderOptions? options = null)
    {
        Time = new ManualTimeProvider();
        Ring = new RingBuffer<FrameSample>(ringCapacity);
        Clock = new CaptureClockMap(Time.TimestampFrequency);
        Recorder = new IncidentRecorder(Ring, Clock, Time, options);
    }

    public ManualTimeProvider Time { get; }

    public RingBuffer<FrameSample> Ring { get; }

    public CaptureClockMap Clock { get; }

    public IncidentRecorder Recorder { get; }

    public double LastPresentMs => _presentMs;

    public void FeedFrame(double lengthMs)
    {
        _presentMs += lengthMs;
        var nowMs = Time.GetTimestamp() * 1000.0 / Time.TimestampFrequency;
        var arrivalMs = _presentMs + DeliveryDelayMs;
        if (arrivalMs > nowMs)
        {
            Time.Advance(TimeSpan.FromTicks((long)((arrivalMs - nowMs) * 10_000.0)));
        }

        Clock.Observe(Time.GetTimestamp(), _presentMs);
        Ring.Write(new FrameSample(_index++, _presentMs, lengthMs, Application: "game.exe", Provenance: DataProvenance.Fixture));
    }

    public void Feed(double seconds)
    {
        var count = (int)Math.Round(seconds * 60.0);
        for (var i = 0; i < count; i++)
        {
            FeedFrame(Frame);
        }
    }

    public void Wait(double milliseconds) => Time.Advance(TimeSpan.FromTicks((long)(milliseconds * 10_000.0)));
}

public class IncidentRecorderTests
{
    [Fact]
    public void A_mark_after_a_spike_offers_it_and_selects_nothing()
    {
        var rig = new RecorderRig();
        rig.Feed(20);
        rig.FeedFrame(RecorderRig.Frame + 80);
        var spikeMs = rig.LastPresentMs;
        rig.Feed(0.4); // the person reacts, then presses the key

        var mark = rig.Recorder.Mark(MarkKind.Stutter)!;

        Assert.Equal(MarkKind.Stutter, mark.Kind);
        Assert.Null(mark.Selection);
        Assert.Equal(rig.LastPresentMs, mark.CaptureTime!.Value.Ms, 1);
        var top = mark.Search.Candidates[0];
        Assert.Equal(AnchorKind.StutterEvent, top.Kind);
        Assert.Equal(spikeMs, top.TimestampMs, 3);
        Assert.InRange(top.SecondsBeforeMark, 0.38, 0.42);
        Assert.Equal("game.exe", mark.TargetProcess);
        Assert.True(mark.PreRoll.Count > 1000);
    }

    [Fact]
    public void Pressing_the_same_mark_again_within_the_debounce_time_is_one_press()
    {
        var rig = new RecorderRig();
        rig.Feed(5);
        Assert.NotNull(rig.Recorder.Mark(MarkKind.Stutter));
        rig.Wait(300);
        Assert.Null(rig.Recorder.Mark(MarkKind.Stutter));
        Assert.Equal(1, rig.Recorder.DebouncedPresses);
        Assert.NotNull(rig.Recorder.Mark(MarkKind.InputStall)); // a different mark is not the same press

        rig.Wait(1200);
        Assert.NotNull(rig.Recorder.Mark(MarkKind.Stutter));
        Assert.Equal(3, rig.Recorder.Marks.Count);
    }

    [Fact]
    public void A_mark_before_any_frame_is_still_recorded_with_an_unknown_time()
    {
        var rig = new RecorderRig();
        var mark = rig.Recorder.Mark(MarkKind.Stutter)!;

        Assert.Null(mark.CaptureTime);
        Assert.Empty(mark.Search.Candidates);
        Assert.Null(mark.Window.StartMs);
        Assert.Equal(0, mark.PreRoll.Count);
        Assert.Throws<InvalidOperationException>(() => rig.Recorder.SelectKeyPressTime(mark.Id));
        Assert.Throws<ArgumentOutOfRangeException>(() => rig.Recorder.SelectAdjustedTime(mark.Id, 0));
    }

    [Fact]
    public void Near_the_start_the_pre_roll_is_short_and_stated_never_padded()
    {
        var rig = new RecorderRig();
        rig.Feed(20);
        var mark = rig.Recorder.Mark(MarkKind.Stutter)!;

        Assert.InRange(mark.Window.AvailablePreRollSeconds, 19, 20.5);
        Assert.True(mark.Window.PreRollShort);
        Assert.Equal(1.0, mark.PreRoll.FirstMs!.Value / RecorderRig.Frame, 1); // starts at the first frame; nothing invented before it
    }

    [Fact]
    public void With_enough_history_the_pre_roll_is_the_requested_length()
    {
        var rig = new RecorderRig();
        rig.Feed(150);
        var mark = rig.Recorder.Mark(MarkKind.Stutter)!;

        Assert.Equal(120.0, mark.Window.AvailablePreRollSeconds, 1);
        Assert.False(mark.Window.PreRollShort);
        var span = (mark.PreRoll.LastMs!.Value - mark.PreRoll.FirstMs!.Value) / 1000.0;
        Assert.InRange(span, 119, 121);
        Assert.Equal(mark.CaptureTime!.Value.Ms + 30_000, mark.Window.EndMs!.Value, 3);
    }

    [Fact]
    public void The_pre_roll_is_frozen_when_the_ring_is_later_overwritten()
    {
        var rig = new RecorderRig(ringCapacity: 1200);
        rig.Feed(15);
        var mark = rig.Recorder.Mark(MarkKind.Stutter)!;
        var first = mark.PreRoll.FirstMs;
        var count = mark.PreRoll.Count;

        rig.Feed(60); // the ring holds 20 s; every old frame is gone
        Assert.True(rig.Ring.TotalEvicted > 0);

        var same = rig.Recorder.Marks[0];
        Assert.Equal(first, same.PreRoll.FirstMs);
        Assert.Equal(count, same.PreRoll.Count);
    }

    [Fact]
    public void The_person_picks_a_candidate_the_key_press_or_a_time_and_can_clear_it()
    {
        var rig = new RecorderRig();
        rig.Feed(20);
        rig.FeedFrame(RecorderRig.Frame + 80);
        rig.Feed(0.5);
        var mark = rig.Recorder.Mark(MarkKind.Stutter)!;
        var changes = new List<IncidentMark>();
        rig.Recorder.MarkChanged += (_, m) => changes.Add(m);

        var picked = rig.Recorder.SelectCandidate(mark.Id, 1);
        Assert.Equal(AnchorChoiceKind.Candidate, picked.Selection!.Kind);
        Assert.Equal(mark.Search.Candidates[0].TimestampMs, picked.Selection.TimestampMs);

        var key = rig.Recorder.SelectKeyPressTime(mark.Id);
        Assert.Equal(AnchorChoiceKind.KeyPressTime, key.Selection!.Kind);
        Assert.Equal(mark.CaptureTime!.Value.Ms, key.Selection.TimestampMs);

        var adjusted = rig.Recorder.SelectAdjustedTime(mark.Id, mark.CaptureTime.Value.Ms - 2000);
        Assert.Equal(AnchorChoiceKind.Adjusted, adjusted.Selection!.Kind);

        Assert.Throws<ArgumentOutOfRangeException>(() => rig.Recorder.SelectAdjustedTime(mark.Id, mark.Window.StartMs!.Value - 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => rig.Recorder.SelectAdjustedTime(mark.Id, mark.Window.EndMs!.Value + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => rig.Recorder.SelectAdjustedTime(mark.Id, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => rig.Recorder.SelectCandidate(mark.Id, 99));
        Assert.Throws<KeyNotFoundException>(() => rig.Recorder.SelectCandidate("no-such-mark", 1));

        Assert.Null(rig.Recorder.ClearSelection(mark.Id).Selection);
        Assert.Equal(4, changes.Count); // candidate, key, adjusted, cleared: failed attempts change nothing
        Assert.Null(rig.Recorder.Marks[0].Selection);
    }

    [Fact]
    public void Notes_are_trimmed_blank_clears_and_a_very_long_note_is_refused()
    {
        var rig = new RecorderRig();
        rig.Feed(5);
        var mark = rig.Recorder.Mark(MarkKind.Stutter)!;

        Assert.Equal("fight at the bridge", rig.Recorder.SetNote(mark.Id, "  fight at the bridge \n").Note);
        Assert.Null(rig.Recorder.SetNote(mark.Id, "   ").Note);
        Assert.Null(rig.Recorder.SetNote(mark.Id, null).Note);
        Assert.Throws<ArgumentOutOfRangeException>(() => rig.Recorder.SetNote(mark.Id, new string('x', IncidentRecorderOptions.MaxNoteLength + 1)));
        Assert.Equal(IncidentRecorderOptions.MaxNoteLength, rig.Recorder.SetNote(mark.Id, new string('x', IncidentRecorderOptions.MaxNoteLength)).Note!.Length);
    }

    [Fact]
    public void The_post_roll_is_collected_as_it_arrives_without_duplicating_frames()
    {
        var rig = new RecorderRig();
        rig.Feed(40);
        var mark = rig.Recorder.Mark(MarkKind.Stutter)!;
        var preCount = mark.PreRoll.Count;

        var early = rig.Recorder.CollectWindow(mark);
        Assert.False(early.PostRollComplete);
        Assert.Equal(preCount, early.Frames.Count);
        Assert.Equal(0.0, early.PostRollCollectedSeconds, 1);

        rig.Feed(10);
        var middle = rig.Recorder.CollectWindow(mark);
        Assert.False(middle.PostRollComplete);
        Assert.InRange(middle.PostRollCollectedSeconds, 9.5, 10.5);
        Assert.InRange(middle.Frames.Count - preCount, 590, 610);

        rig.Feed(25);
        var done = rig.Recorder.CollectWindow(mark);
        Assert.True(done.PostRollComplete);
        Assert.Equal(30.0, done.PostRollCollectedSeconds, 1);
        Assert.True(done.Frames.LastMs <= mark.Window.EndMs);

        var indexes = done.Frames.Indexes.ToArray();
        Assert.Equal(indexes.Distinct().Count(), indexes.Length);
        Assert.Equal(indexes.Order().ToArray(), indexes);
    }

    [Fact]
    public void A_capture_that_ends_early_never_reports_a_complete_post_roll()
    {
        var rig = new RecorderRig();
        rig.Feed(30);
        var mark = rig.Recorder.Mark(MarkKind.Stutter)!;
        rig.Feed(5); // then the capture stops
        rig.Wait(60_000);

        var window = rig.Recorder.CollectWindow(mark);
        Assert.False(window.PostRollComplete);
        Assert.InRange(window.PostRollCollectedSeconds, 4.5, 5.5);
    }

    [Fact]
    public void Only_the_newest_marks_are_kept()
    {
        var rig = new RecorderRig(options: new IncidentRecorderOptions(MaxMarks: 2, DebounceMs: 0));
        rig.Feed(5);
        var a = rig.Recorder.Mark(MarkKind.Stutter)!;
        var b = rig.Recorder.Mark(MarkKind.Stutter)!;
        var c = rig.Recorder.Mark(MarkKind.Stutter)!;

        Assert.Equal([b.Id, c.Id], rig.Recorder.Marks.Select(m => m.Id).ToArray());
        Assert.Throws<KeyNotFoundException>(() => rig.Recorder.SetNote(a.Id, "gone"));
    }

    [Fact]
    public void Mark_identifiers_are_unique_and_ordered()
    {
        var rig = new RecorderRig(options: new IncidentRecorderOptions(DebounceMs: 0));
        rig.Feed(5);
        var ids = Enumerable.Range(0, 5).Select(_ => rig.Recorder.Mark(MarkKind.InputStall)!.Id).ToArray();
        Assert.Equal(ids.Distinct().Count(), ids.Length);
        Assert.Equal(ids.Order(StringComparer.Ordinal).ToArray(), ids);
        Assert.All(rig.Recorder.Marks, m => Assert.Equal(MarkKind.InputStall, m.Kind));
    }

    [Theory]
    [InlineData(10.0, 30.0)]
    [InlineData(700.0, 30.0)]
    [InlineData(double.NaN, 30.0)]
    [InlineData(120.0, -1.0)]
    [InlineData(120.0, 301.0)]
    public void Pre_and_post_roll_outside_the_documented_range_are_refused(double preRoll, double postRoll)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new IncidentRecorderOptions(PreRollSeconds: preRoll, PostRollSeconds: postRoll).Validate());
    }

    [Fact]
    public void The_history_size_follows_the_roll_lengths_and_is_capped()
    {
        Assert.Equal(64_800, IncidentRecorderOptions.Default.RecommendedHistoryCapacity());
        Assert.Equal(131_072, new IncidentRecorderOptions(PreRollSeconds: 600).RecommendedHistoryCapacity());
        Assert.Equal(1024, new IncidentRecorderOptions(PreRollSeconds: 30, PostRollSeconds: 0).RecommendedHistoryCapacity(marginSeconds: 0, assumedMaxFps: 1));
    }

    [Fact]
    public void A_cap_that_bites_shows_up_as_a_short_pre_roll_not_silently()
    {
        var rig = new RecorderRig(ringCapacity: 3600); // room for one minute at 60 fps, but 120 s are wanted
        rig.Feed(150);
        var mark = rig.Recorder.Mark(MarkKind.Stutter)!;

        Assert.True(mark.Window.PreRollShort);
        Assert.InRange(mark.Window.AvailablePreRollSeconds, 55, 61);
    }
}

public class FrameSeriesTests
{
    [Fact]
    public void Samples_round_trip_with_index_time_and_provenance()
    {
        var samples = new[]
        {
            new FrameSample(10, 100.5, 16.667, Application: "a.exe", Provenance: DataProvenance.Measured),
            new FrameSample(11, 117.25, 16.75, Application: "a.exe", Provenance: DataProvenance.Measured),
        };
        var series = FrameSeries.FromSamples(samples);
        var back = series.ToSamples();

        Assert.Equal(2, series.Count);
        Assert.Equal([10L, 11L], series.Indexes.ToArray());
        Assert.Equal(100.5, back[0].TimestampMs);
        Assert.Equal(16.667, back[0].FrameTimeMs, 3);
        Assert.Equal("a.exe", back[1].Application);
        Assert.Equal(DataProvenance.Measured, back[1].Provenance);
        Assert.Equal(100.5, series.FirstMs);
        Assert.Equal(117.25, series.LastMs);
    }

    [Fact]
    public void One_simulated_frame_makes_the_whole_series_simulated()
    {
        var series = FrameSeries.FromSamples(
        [
            new FrameSample(0, 10, 10, Provenance: DataProvenance.Measured),
            new FrameSample(1, 20, 10, Provenance: DataProvenance.Simulated),
            new FrameSample(2, 30, 10, Provenance: DataProvenance.Measured),
        ]);
        Assert.Equal(DataProvenance.Simulated, series.Provenance);
        Assert.False(series.Provenance.MayBeExportedAsMeasurement());
        Assert.All(series.ToSamples(), s => Assert.Equal(DataProvenance.Simulated, s.Provenance));
    }

    [Fact]
    public void An_empty_series_says_it_has_no_data()
    {
        Assert.Equal(0, FrameSeries.Empty.Count);
        Assert.Null(FrameSeries.Empty.FirstMs);
        Assert.Equal(DataProvenance.None, FrameSeries.Empty.Provenance);
        Assert.Same(FrameSeries.Empty, FrameSeries.FromSamples([]));
    }

    [Fact]
    public void A_capture_that_restarts_isolates_the_new_pre_roll_candidates_and_the_old_post_roll()
    {
        var rig = new RecorderRig();
        rig.Feed(30);
        rig.FeedFrame(RecorderRig.Frame + 120); // a large hitch in run 1 around 30 s
        rig.Feed(10);
        var run1Mark = rig.Recorder.Mark(MarkKind.Stutter)!;

        // Simulate a PresentMon restart (run 2 starts near 0 ms on the same history ring)
        rig.Clock.Reset();
        for (var i = 0; i < 600; i++)
        {
            var ts = (i + 1) * RecorderRig.Frame;
            rig.Wait(RecorderRig.Frame);
            rig.Clock.Observe(rig.Time.GetTimestamp(), ts);
            rig.Ring.Write(new FrameSample(i, ts, RecorderRig.Frame, Application: "game.exe", Provenance: DataProvenance.Fixture));
        }

        // Run 1's post-roll was cut short by the restart and must not absorb run 2 frames or claim completion
        var run1Window = rig.Recorder.CollectWindow(run1Mark);
        Assert.False(run1Window.PostRollComplete);
        Assert.Equal(run1Mark.PreRoll.Count, run1Window.Frames.Count);

        // Run 2's mark at ~10 s must only contain run 2's 600 frames and must not offer run 1's hitch
        rig.Wait(1500);
        var run2Mark = rig.Recorder.Mark(MarkKind.Stutter)!;
        Assert.Equal(600, run2Mark.PreRoll.Count);
        Assert.True(run2Mark.Window.PreRollShort);
        Assert.Empty(run2Mark.Search.Candidates);
    }

    [Fact]
    public void Continuing_a_series_keeps_only_frames_that_come_later_and_stay_inside_the_end()
    {
        var head = FrameSeries.FromSamples([new FrameSample(0, 10, 10), new FrameSample(1, 20, 10)]);
        var later = new[]
        {
            new FrameSample(0, 10, 10),   // the same frame again
            new FrameSample(1, 20, 10),
            new FrameSample(2, 30, 10),
            new FrameSample(3, 40, 10),
            new FrameSample(9, 5, 10),    // the clock restarted
            new FrameSample(10, 25, 10),  // after the restart: inside (20, 40] numerically, but from a new run!
            new FrameSample(4, 50, 10),   // beyond the end
        };
        var joined = head.ContinueWith(later, endMs: 40);
        Assert.Equal([0L, 1L, 2L, 3L], joined.Indexes.ToArray());
        Assert.Same(head, head.ContinueWith([], 100));
        Assert.Same(head, head.ContinueWith(later, 20));
    }
}
