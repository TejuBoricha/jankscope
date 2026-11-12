using System.Text.Json;
using System.Text.Json.Nodes;
using Jankscope.Core;
using Jankscope.Core.Reports;
using Jankscope.Frames.Incidents;
using Jankscope.Frames.Reports;

namespace Jankscope.Frames.Tests.Incidents;

public class IncidentBundleTests
{
    private static CapabilityMatrix Matrix(params CapabilityEntry[] entries) =>
        new(DateTimeOffset.Parse("2026-11-04T00:00:00Z"), "0.0.0-test", entries, []);

    private static CapabilityEntry Row(string id, string value, bool sensitive = false) =>
        CapabilityEntry.Available(id, "Title of " + id, CapabilityTier.A, "test", value, DataProvenance.Measured, null, sensitive);

    /// <summary>A mark made 0.4 s after an 80 ms spike in a 60 fps stream, with <paramref name="postRollSeconds"/> of post-roll fed afterwards.</summary>
    private static (RecorderRig Rig, IncidentMark Mark) MarkedStream(double seconds = 40, double postRollSeconds = 31, MarkKind kind = MarkKind.Stutter)
    {
        var rig = new RecorderRig();
        rig.Feed(seconds);
        rig.FeedFrame(RecorderRig.Frame + 80);
        rig.Feed(0.4);
        var mark = rig.Recorder.Mark(kind)!;
        rig.Feed(postRollSeconds);
        return (rig, mark);
    }

    [Fact]
    public void A_complete_mark_becomes_a_bundle_with_report_anchors_frames_and_quality()
    {
        var (rig, mark) = MarkedStream();
        var window = rig.Recorder.CollectWindow(mark);
        var bundle = IncidentBundleBuilder.Build(mark, window, Matrix(Row("os.build", "10.0.26300")));

        Assert.Equal("jankscope.incident/1", bundle.Schema);
        Assert.True(bundle.Report.Metrics.StutterEventsCount >= 1);
        Assert.Equal(bundle.Frames.Count, bundle.Frames.TimestampsMs.Length);
        Assert.Equal(bundle.Frames.Count, bundle.Frames.FrameTimesMs.Length);
        Assert.Equal("stutter", bundle.Mark.Kind);
        Assert.NotEmpty(bundle.Anchors.Candidates);
        Assert.Null(bundle.Anchors.Selected);
        Assert.Contains("No event time was chosen", bundle.Anchors.SelectionStatus);
        Assert.Contains("not used as the event time", bundle.Anchors.SelectionStatus);
        Assert.True(bundle.DataQuality.PostRollComplete);
        Assert.Equal("uncalibrated", bundle.DataQuality.Calibration);
        Assert.Equal("game.exe", bundle.Mark.TargetProcess);
        Assert.Equal(mark.Id, bundle.Report.Metadata.ReportId);
    }

    [Fact]
    public void The_persons_choice_is_recorded_in_words_and_data()
    {
        var (rig, mark) = MarkedStream();
        var matrix = Matrix(Row("os.build", "10.0.26300"));

        var candidate = rig.Recorder.SelectCandidate(mark.Id, 1);
        var b1 = IncidentBundleBuilder.Build(candidate, rig.Recorder.CollectWindow(candidate), matrix);
        Assert.Contains("chose candidate 1", b1.Anchors.SelectionStatus);
        Assert.Equal(AnchorChoiceKind.Candidate, b1.Anchors.Selected!.Kind);

        var key = rig.Recorder.SelectKeyPressTime(mark.Id);
        var b2 = IncidentBundleBuilder.Build(key, rig.Recorder.CollectWindow(key), matrix);
        Assert.Contains("moment the key was pressed", b2.Anchors.SelectionStatus);
        Assert.Contains("reaction time", b2.Anchors.SelectionStatus);

        var adjusted = rig.Recorder.SelectAdjustedTime(mark.Id, mark.CaptureTime!.Value.Ms - 1000);
        var b3 = IncidentBundleBuilder.Build(adjusted, rig.Recorder.CollectWindow(adjusted), matrix);
        Assert.Contains("time on the timeline", b3.Anchors.SelectionStatus);
    }

    [Fact]
    public void An_incomplete_post_roll_a_short_pre_roll_and_dropped_frames_are_all_stated()
    {
        var (rig, mark) = MarkedStream(seconds: 20, postRollSeconds: 5);
        var bundle = IncidentBundleBuilder.Build(mark, rig.Recorder.CollectWindow(mark), Matrix(Row("a", "b")), droppedFrames: 7);

        Assert.False(bundle.DataQuality.PostRollComplete);
        Assert.Contains(bundle.DataQuality.Notes, n => n.Contains("post-roll is incomplete"));
        Assert.Contains(bundle.DataQuality.Notes, n => n.Contains("pre-roll existed") && n.Contains("not padded"));
        Assert.Contains(bundle.DataQuality.Notes, n => n.Contains("7 frame(s) were dropped"));
        Assert.Equal(7, bundle.DataQuality.DroppedFrames);
    }

    [Fact]
    public void Frames_left_out_for_being_out_of_order_and_jumps_of_the_capture_clock_are_stated_in_the_export()
    {
        var (rig, mark) = MarkedStream();
        var bundle = IncidentBundleBuilder.Build(
            mark, rig.Recorder.CollectWindow(mark), Matrix(Row("a", "b")), outOfOrderFrames: 12, backwardJumps: 2);

        Assert.Equal(12, bundle.DataQuality.OutOfOrderFrames);
        Assert.Equal(2, bundle.DataQuality.BackwardJumps);
        Assert.Contains(bundle.DataQuality.Notes, n => n.Contains("12 frame(s) were left out of the series"));
        Assert.Contains(bundle.DataQuality.Notes, n => n.Contains("jumped back 2 time(s)"));
    }

    [Fact]
    public void A_clean_capture_states_no_out_of_order_frames_and_no_clock_jumps()
    {
        var (rig, mark) = MarkedStream();
        var bundle = IncidentBundleBuilder.Build(mark, rig.Recorder.CollectWindow(mark), Matrix(Row("a", "b")));

        Assert.Equal(0, bundle.DataQuality.OutOfOrderFrames);
        Assert.Equal(0, bundle.DataQuality.BackwardJumps);
        Assert.DoesNotContain(bundle.DataQuality.Notes, n => n.Contains("left out of the series") || n.Contains("jumped back"));
    }

    [Fact]
    public void With_enough_frames_the_export_gives_the_jitter_and_always_says_it_is_the_jitter_only()
    {
        var (rig, mark) = MarkedStream();
        Assert.True(mark.CaptureTime!.Value.UncertaintyIsMeaningful);

        var bundle = IncidentBundleBuilder.Build(mark, rig.Recorder.CollectWindow(mark), Matrix(Row("a", "b")));

        Assert.NotNull(bundle.Mark.CaptureUncertaintyMs);
        Assert.Contains(bundle.DataQuality.Notes, n => n.Contains("jitter only") && n.Contains("minimum delay") && n.Contains("reaction time"));
    }

    [Fact]
    public void With_too_few_frames_the_export_gives_no_uncertainty_number_and_says_unknown_not_zero()
    {
        // Review of Stage 1b, finding 3: "uncertainty 0.0" after one observation read as an exact time.
        var (rig, mark) = MarkedStream();
        var window = rig.Recorder.CollectWindow(mark);
        var few = mark with { CaptureTime = new CaptureTime(mark.CaptureTime!.Value.Ms, 0.0, 1, 0) };

        var bundle = IncidentBundleBuilder.Build(few, window, Matrix(Row("a", "b")));

        Assert.Null(bundle.Mark.CaptureUncertaintyMs);
        Assert.Contains(bundle.DataQuality.Notes, n => n.Contains("only 1 frame(s)") && n.Contains("unknown, not zero") && n.Contains("minimum delay"));
    }

    [Fact]
    public void An_input_stall_mark_says_input_timing_is_not_collected_yet()
    {
        var (rig, mark) = MarkedStream(kind: MarkKind.InputStall);
        var bundle = IncidentBundleBuilder.Build(mark, rig.Recorder.CollectWindow(mark), Matrix(Row("a", "b")));

        Assert.Equal("input stall", bundle.Mark.Kind);
        Assert.Contains(bundle.DataQuality.Notes, n => n.Contains("input-stall mark") && n.Contains("not collected in this version"));
    }

    [Fact]
    public void Provenance_is_carried_and_non_measurements_are_labelled_as_such()
    {
        var (rig, mark) = MarkedStream();
        var bundle = IncidentBundleBuilder.Build(mark, rig.Recorder.CollectWindow(mark), Matrix(Row("a", "b")));
        Assert.Equal(DataProvenance.Fixture, bundle.Report.Metadata.Provenance);
        Assert.Contains("FIXTURE", bundle.ProvenanceNotice);
        Assert.Equal(IncidentTrigger.UserMarked, bundle.Report.Metadata.Trigger);

        Assert.Null(IncidentBundleBuilder.ProvenanceNotice(DataProvenance.Measured));
        Assert.Null(IncidentBundleBuilder.ProvenanceNotice(DataProvenance.Inferred));
        Assert.Contains("SIMULATED", IncidentBundleBuilder.ProvenanceNotice(DataProvenance.Simulated));
        Assert.Contains("ASSUMED", IncidentBundleBuilder.ProvenanceNotice(DataProvenance.Assumed));
        Assert.NotNull(IncidentBundleBuilder.ProvenanceNotice(DataProvenance.None));
    }

    [Fact]
    public void Simulated_frames_make_a_synthetic_trigger_and_a_simulated_notice()
    {
        var rig = new RecorderRig();
        for (var i = 0; i < 3000; i++)
        {
            rig.Ring.Write(new FrameSample(i, (i + 1) * RecorderRig.Frame, RecorderRig.Frame, Application: "harness.exe", Provenance: DataProvenance.Simulated));
            rig.Clock.Observe(rig.Time.GetTimestamp(), (i + 1) * RecorderRig.Frame);
            rig.Wait(RecorderRig.Frame);
        }

        var mark = rig.Recorder.Mark(MarkKind.Stutter)!;
        var bundle = IncidentBundleBuilder.Build(mark, rig.Recorder.CollectWindow(mark), Matrix(Row("a", "b")));

        Assert.Equal(DataProvenance.Simulated, bundle.Report.Metadata.Provenance);
        Assert.Equal(IncidentTrigger.SyntheticHarness, bundle.Report.Metadata.Trigger);
        Assert.Contains("SIMULATED", bundle.ProvenanceNotice);
        Assert.False(bundle.Report.Metadata.Provenance.MayBeExportedAsMeasurement());
    }

    [Fact]
    public void A_window_with_no_frames_cannot_be_exported()
    {
        var rig = new RecorderRig();
        var mark = rig.Recorder.Mark(MarkKind.Stutter)!;
        Assert.Throws<InvalidOperationException>(() => IncidentBundleBuilder.Build(mark, rig.Recorder.CollectWindow(mark), Matrix(Row("a", "b"))));
    }

    [Fact]
    public void Frames_with_an_unusable_time_are_left_out_and_counted_not_turned_into_zeros()
    {
        var rig = new RecorderRig();
        rig.Feed(20);
        rig.Ring.Write(new FrameSample(9999, rig.LastPresentMs + 5, 0.0, Application: "game.exe", Provenance: DataProvenance.Fixture));
        rig.Ring.Write(new FrameSample(10000, rig.LastPresentMs + 10, double.NaN, Application: "game.exe", Provenance: DataProvenance.Fixture));
        rig.Feed(1);
        var mark = rig.Recorder.Mark(MarkKind.Stutter)!;
        var window = rig.Recorder.CollectWindow(mark);

        var bundle = IncidentBundleBuilder.Build(mark, window, Matrix(Row("a", "b")));
        Assert.Equal(2, bundle.DataQuality.RejectedFrames);
        Assert.DoesNotContain(0.0, bundle.Frames.FrameTimesMs);
        Assert.All(bundle.Frames.FrameTimesMs, t => Assert.True(double.IsFinite(t) && t > 0));
        Assert.Contains(bundle.DataQuality.Notes, n => n.Contains("2 frame(s) with an unusable frame time"));
    }

    [Fact]
    public void Sensitive_capability_rows_are_masked_by_the_matrix_policy()
    {
        var (rig, mark) = MarkedStream();
        var bundle = IncidentBundleBuilder.Build(mark, rig.Recorder.CollectWindow(mark), Matrix(Row("os.locale", "en-IN", sensitive: true), Row("os.build", "26300")));
        var locale = bundle.Report.SystemEnvironment.Single(e => e.Id == "os.locale");
        Assert.Equal("[REDACTED: sensitive]", locale.Value);
        Assert.Equal("26300", bundle.Report.SystemEnvironment.Single(e => e.Id == "os.build").Value);
    }

    [Fact]
    public void End_to_end_every_planted_canary_is_gone_from_the_exported_file_and_the_file_is_valid_json()
    {
        const string user = "Zanzibar_Quinn";
        const string host = "QUINN-RIG-77";
        const string ssid = "Quinns Flat WiFi";
        const string serial = "SN-9X77-QWERTY-4421";
        var context = RedactionContext.Empty
            .With(RedactionCategory.UserName, user)
            .With(RedactionCategory.HostName, host)
            .With(RedactionCategory.WifiSsid, ssid)
            .With(RedactionCategory.DeviceSerial, serial);

        var rig = new RecorderRig();
        rig.Feed(30);
        rig.FeedFrame(RecorderRig.Frame + 70);
        rig.Feed(0.3);
        var mark = rig.Recorder.Mark(MarkKind.Stutter)!;
        rig.Recorder.SetNote(mark.Id, $"Happened at {user}'s flat on {ssid}, adapter 00:1A:2B:3C:4D:5E, ip 192.168.77.12");
        rig.Recorder.SelectCandidate(mark.Id, 1);
        rig.Feed(31);
        var current = rig.Recorder.Marks.Single();

        var matrix = Matrix(
            Row("os.build", $"Windows build for {user} on {host}"),
            Row("storage.disk", $"C:\\Users\\{user}\\AppData disk serial {serial}"),
            Row("network.adapter", $"{ssid} at 192.168.77.12 / fe80::1c2d:3e4f:5a6b:7c8d"));
        var bundle = IncidentBundleBuilder.Build(current, rig.Recorder.CollectWindow(current), matrix);

        var preview = ExportPipeline.Prepare(bundle, context);

        foreach (var canary in new[] { user, host, ssid, serial, "00:1A:2B:3C:4D:5E", "192.168.77.12", "fe80::1c2d" })
        {
            Assert.DoesNotContain(canary, preview.Text, StringComparison.OrdinalIgnoreCase);
        }

        using var doc = JsonDocument.Parse(preview.Text);
        Assert.Equal("jankscope.incident/1", doc.RootElement.GetProperty("schema").GetString());
        Assert.True(doc.RootElement.GetProperty("redaction").GetProperty("applied").GetBoolean());
        Assert.Contains("<USER>", doc.RootElement.GetProperty("mark").GetProperty("note").GetString());
        Assert.Equal(bundle.Frames.Count, doc.RootElement.GetProperty("frames").GetProperty("count").GetInt32());
        Assert.True(preview.TotalReplacements >= 8);
    }

    [Fact]
    public void A_program_on_two_swap_chains_is_said_in_the_export_and_no_swap_chain_address_is_exported()
    {
        // Independent Stage 1b review, finding 7, end to end: recorder -> mark -> bundle -> the exact text that would be saved.
        const string main = "0x103E8";
        const string overlay = "0x7F00BEEF";
        var rig = new RecorderRig { MainChain = main };
        rig.Feed(20);
        rig.FeedFrame(400, overlay); // a slow frame on a second swap chain
        rig.Feed(0.5);
        var mark = rig.Recorder.Mark(MarkKind.Stutter)!;
        rig.Feed(31);
        var current = rig.Recorder.Marks.Single();

        var bundle = IncidentBundleBuilder.Build(current, rig.Recorder.CollectWindow(current), Matrix(Row("os.build", "10.0.26300")));
        var preview = ExportPipeline.Prepare(bundle, RedactionContext.Empty);

        Assert.Equal(2, current.Search.SwapChains!.Count);
        Assert.Contains(bundle.Anchors.Notes, n => n.Contains("2 swap chains"));
        Assert.Contains(bundle.Anchors.Candidates, c => c.Description.Contains("secondary swap chain"));
        Assert.Contains("2 swap chains", preview.Text);
        Assert.DoesNotContain(main, preview.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(overlay, preview.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("0x7F", preview.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_two_and_a_half_minute_window_at_high_frame_rate_stays_a_reasonable_size()
    {
        var rig = new RecorderRig(ringCapacity: 131_072);
        const double highFrame = 1000.0 / 360.0;
        for (var i = 0; i < 360 * 125; i++)
        {
            rig.FeedFrame(highFrame);
        }

        var mark = rig.Recorder.Mark(MarkKind.Stutter)!;
        for (var i = 0; i < 360 * 31; i++)
        {
            rig.FeedFrame(highFrame);
        }

        var bundle = IncidentBundleBuilder.Build(mark, rig.Recorder.CollectWindow(mark), Matrix(Row("a", "b")));
        var preview = ExportPipeline.Prepare(bundle, RedactionContext.Empty);

        Assert.InRange(bundle.Frames.Count, 50_000, 56_000);
        Assert.True(preview.Bytes.Length < 6_000_000, $"{preview.Bytes.Length} bytes");
        Assert.NotNull(JsonNode.Parse(preview.Text));
    }
}

public class TimelineBucketsTests
{
    [Fact]
    public void A_single_frame_spike_survives_reduction_to_a_few_hundred_buckets()
    {
        var frames = new FrameSample[100_000];
        for (var i = 0; i < frames.Length; i++)
        {
            frames[i] = new FrameSample(i, (i + 1) * 1.0, 1.0);
        }

        frames[54_321] = frames[54_321] with { FrameTimeMs = 250.0 };
        var buckets = TimelineBuckets.Build(frames, 0, 100_000, 800);

        Assert.Equal(800, buckets.Length);
        Assert.Equal(250.0, buckets.Max(b => b.MaxMs));
        Assert.Equal(1, buckets.Count(b => b.MaxMs >= 250.0));
        Assert.Equal(100_000, buckets.Sum(b => b.Count));
    }

    [Fact]
    public void A_stretch_with_no_frames_is_empty_buckets_with_nan_not_zero()
    {
        var frames = new[] { new FrameSample(0, 5, 10), new FrameSample(1, 95, 10) };
        var buckets = TimelineBuckets.Build(frames, 0, 100, 10);

        Assert.Equal(1, buckets[0].Count);
        Assert.Equal(1, buckets[9].Count);
        Assert.All(buckets.Skip(1).Take(8), b =>
        {
            Assert.True(b.IsEmpty);
            Assert.True(double.IsNaN(b.MinMs) && double.IsNaN(b.MaxMs) && double.IsNaN(b.MeanMs));
        });
    }

    [Fact]
    public void A_frame_exactly_at_the_end_belongs_to_the_last_bucket_and_outside_frames_are_ignored()
    {
        var frames = new[] { new FrameSample(0, -1, 9), new FrameSample(1, 100, 11), new FrameSample(2, 100.0001, 13) };
        var buckets = TimelineBuckets.Build(frames, 0, 100, 4);

        Assert.Equal(1, buckets[3].Count);
        Assert.Equal(11.0, buckets[3].MaxMs);
        Assert.Equal(1, buckets.Sum(b => b.Count));
    }

    [Fact]
    public void Unusable_frame_times_are_skipped_not_drawn_as_zero()
    {
        var frames = new[]
        {
            new FrameSample(0, 10, 0.0),
            new FrameSample(1, 20, double.NaN),
            new FrameSample(2, 30, -4.0),
            new FrameSample(3, 40, double.PositiveInfinity),
            new FrameSample(4, double.NaN, 5.0),
            new FrameSample(5, 50, 7.0),
        };
        var buckets = TimelineBuckets.Build(frames, 0, 100, 1);
        Assert.Equal(1, buckets[0].Count);
        Assert.Equal(7.0, buckets[0].MinMs);
    }

    [Fact]
    public void The_mean_and_extremes_of_a_bucket_are_exact()
    {
        var frames = new[] { new FrameSample(0, 1, 10), new FrameSample(1, 2, 20), new FrameSample(2, 3, 60) };
        var bucket = TimelineBuckets.Build(frames, 0, 10, 1)[0];
        Assert.Equal(3, bucket.Count);
        Assert.Equal(10.0, bucket.MinMs);
        Assert.Equal(60.0, bucket.MaxMs);
        Assert.Equal(30.0, bucket.MeanMs, 9);
    }

    [Theory]
    [InlineData(0, 0, 10)]
    [InlineData(10, 5, 10)]
    [InlineData(0, 100, 0)]
    [InlineData(double.NaN, 100, 10)]
    [InlineData(0, double.PositiveInfinity, 10)]
    public void An_invalid_range_or_bucket_count_is_refused(double start, double end, int count)
    {
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => TimelineBuckets.Build([], start, end, count));
    }

    [Fact]
    public void The_series_overload_agrees_with_the_sample_overload()
    {
        var rng = new Random(11);
        var frames = new FrameSample[2000];
        var clock = 0.0;
        for (var i = 0; i < frames.Length; i++)
        {
            var ft = 5 + rng.NextDouble() * 20;
            clock += ft;
            frames[i] = new FrameSample(i, clock, ft);
        }

        var a = TimelineBuckets.Build(frames, 0, clock, 97);
        var b = TimelineBuckets.Build(FrameSeries.FromSamples(frames), 0, clock, 97);
        for (var i = 0; i < a.Length; i++)
        {
            Assert.Equal(a[i].Count, b[i].Count);
            if (a[i].Count > 0)
            {
                Assert.Equal(a[i].MaxMs, b[i].MaxMs, 3);
                Assert.Equal(a[i].MinMs, b[i].MinMs, 3);
            }
        }
    }
}
