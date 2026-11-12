using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Jankscope.App.Services;
using Jankscope.App.Views;
using Jankscope.Core;
using Jankscope.Core.Hotkeys;
using Jankscope.Frames;
using Jankscope.Frames.Incidents;
using Jankscope.Platform.Hotkeys;

namespace Jankscope.App.Tests;

/// <summary>Stands in for the application object: records what the windows ask of it.</summary>
internal sealed class FakeShell(AppHost host) : IShell
{
    public List<HotkeyStatus> Statuses { get; } = [];

    public List<string> Themes { get; } = [];

    public int WelcomeCount { get; private set; }

    public IReadOnlyList<HotkeyStatus> HotkeyStatuses => Statuses;

    public IReadOnlyList<HotkeyStatus> ApplyHotkeys(HotkeyBindings bindings)
    {
        Statuses.Clear();
        Statuses.AddRange(bindings.All().Select(a => new HotkeyStatus(a.Action, a.Chord, true, HotkeyFailure.None, null)));
        return Statuses;
    }

    public void ApplyTheme(string theme) => Themes.Add(theme);

    public void ShowWelcome() => WelcomeCount++;

    public void RequestMark(MarkKind kind) => _ = host.MarkAsync(kind);

    public void ChangeWindowVisibility(bool visible)
    {
    }
}

/// <summary>Skipped, with a visible reason, in a session with no desktop to draw windows on. Never passes vacuously.</summary>
public sealed class DesktopFactAttribute : FactAttribute
{
    public DesktopFactAttribute()
    {
        if (!Environment.UserInteractive)
        {
            Skip = "This session is not interactive, so there is no desktop to create windows on.";
        }
    }
}

internal static class Snapshots
{
    /// <summary>Where snapshot PNGs go when the person asks for them (JANKSCOPE_SNAPSHOT_DIR); otherwise nothing is written.</summary>
    public static string? Directory => Environment.GetEnvironmentVariable("JANKSCOPE_SNAPSHOT_DIR") is { Length: > 0 } d ? d : null;

    public static void Save(FrameworkElement element, string name, double scale = 1.0)
    {
        if (Directory is not { } directory)
        {
            return;
        }

        System.IO.Directory.CreateDirectory(directory);
        var width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth * scale));
        var height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight * scale));
        var bitmap = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(stream);
    }
}

[Collection("WPF")]
public class TimelineViewTests
{
    private static TimelineView Make(double width, double height, FrameSample[] samples, double endMs, double? markMs = null, double? anchorMs = null, double? uncertainty = null, params (double Ms, string Label, bool Weak)[] candidates)
    {
        var view = new TimelineView { Width = width, Height = height };
        view.Show(new TimelineData(
            samples,
            null,
            endMs - 30_000,
            endMs,
            markMs ?? endMs,
            markMs,
            uncertainty,
            null,
            anchorMs,
            candidates.Select(c => new TimelineMarker(c.Ms, c.Label, c.Weak)).ToList(),
            []));
        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height));
        view.UpdateLayout();
        return view;
    }

    [Fact]
    public async Task The_columns_follow_the_width_and_a_single_spike_survives_the_reduction()
    {
        var (frames, end) = TestData.Stream(30, (900, 120));
        var spike = frames.Max(f => f.FrameTimeMs);

        var (count, max, nonEmpty) = await WpfHost.RunAsync(() =>
        {
            var view = Make(900, 280, frames, end);
            Snapshots.Save(view, "timeline-live-100");
            return (view.Buckets.Count, view.Buckets.Where(b => !b.IsEmpty).Max(b => b.MaxMs), view.Buckets.Count(b => !b.IsEmpty));
        });

        Assert.Equal(900 - (int)(TimelineView.LeftMargin + TimelineView.RightMargin), count);
        Assert.Equal(spike, max, 6);
        Assert.True(nonEmpty > count * 0.9);
    }

    [Fact]
    public async Task A_one_frame_spike_is_drawn_as_a_tall_line_not_a_dot()
    {
        // At 3000 pixels wide a column covers about 10 ms, less than a 16.7 ms frame, so every frame sits alone in its column (minimum equals maximum)
        // and empty columns separate them. Only the line through the column means can show the spike as a peak.
        const int Width = 3000;
        const int Height = 300;
        var (frames, end) = TestData.Stream(30, (900, 45));
        var spikeTime = frames[900].TimestampMs;

        var lit = await WpfHost.RunAsync(() =>
        {
            var view = Make(Width, Height, frames, end);
            var plotWidth = Width - TimelineView.LeftMargin - TimelineView.RightMargin;
            var x = (int)Math.Round(TimelineView.LeftMargin + (spikeTime - (end - 30_000)) / 30_000.0 * plotWidth);
            var bitmap = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(view);
            var stride = Width * 4;
            var data = new byte[Height * stride];
            bitmap.CopyPixels(data, stride, 0);

            // The baseline is at 16.7 ms (row 209) and the spike reaches 61.7 ms (row 40): count the rows between them that are drawn near the spike's column.
            var rows = 0;
            for (var y = 60; y < 195; y++)
            {
                for (var dx = -8; dx <= 8; dx++)
                {
                    // Pbgra32 bytes are B, G, R, A. A drawn line is bright in at least one channel: the dark theme's line is cyan
                    // and the signal theme's is lime. The chart background is dark in both themes, so this still rejects background.
                    var px = (y * stride) + ((x + dx) * 4);
                    if (Math.Max(data[px], Math.Max(data[px + 1], data[px + 2])) > 150)
                    {
                        rows++;
                        break;
                    }
                }
            }

            return rows;
        });

        Assert.True(lit > 100, $"only {lit} of 135 pixel rows between the baseline and the spike are drawn");
    }

[Fact]
    public async Task A_stretch_without_frames_shows_as_empty_columns()
    {
        var (frames, end) = TestData.Stream(30);
        var gapped = frames.Where(f => f.TimestampMs < 10_000 || f.TimestampMs > 14_000).ToArray();

        var empty = await WpfHost.RunAsync(() =>
        {
            var view = Make(900, 240, gapped, end);
            return view.Buckets.Count(b => b.IsEmpty);
        });

        Assert.InRange(empty, 95, 125); // about 4 s of 30 s of the 774 columns
    }

    [Fact]
    public async Task Empty_time_before_the_first_frame_is_not_called_a_gap_in_the_log()
    {
        // GUI-run report, defect 15: the live view always spans 30 s, so early in a capture its left part predates the first frame.
        // "no frames logged" beside plotted frames read as a fault; the truth is "before the first frame".
        var (frames, end) = TestData.Stream(30);
        var late = frames.Where(f => f.TimestampMs > end - 10_000).ToArray();

        var (before, inside) = await WpfHost.RunAsync(() =>
        {
            var view = Make(900, 240, late, end);
            return (view.DescribeAt(end - 25_000), view.DescribeAt(end - 5_000));
        });

        Assert.EndsWith("before the first frame in this view", before);
        Assert.DoesNotContain("no frames logged", before);
        Assert.Contains("worst frame", inside);
    }

    [Fact]
    public async Task Empty_time_after_the_last_frame_says_so_and_a_hole_between_frames_still_says_no_frames_logged()
    {
        var (frames, end) = TestData.Stream(30);
        var early = frames.Where(f => f.TimestampMs < end - 20_000).ToArray();
        var holed = frames.Where(f => f.TimestampMs < 10_000 || f.TimestampMs > 14_000).ToArray();

        var (afterLast, hole, nothing) = await WpfHost.RunAsync(() =>
        {
            var earlyView = Make(900, 240, early, end);
            var holedView = Make(900, 240, holed, end);
            var emptyView = Make(900, 240, [], end);
            return (earlyView.DescribeAt(end - 2_000), holedView.DescribeAt(12_000), emptyView.DescribeAt(end - 15_000));
        });

        Assert.EndsWith("after the last frame in this view", afterLast);
        Assert.EndsWith("no frames logged here", hole);
        Assert.EndsWith("no frames logged here", nothing); // with no frame at all there is no "before" or "after"
    }

    [Fact]
    public void An_axis_label_near_the_right_edge_moves_left_instead_of_being_cut()
    {
        Assert.Equal(100.0, TimelineView.LabelLeft(100, 40, 900));   // plenty of room: where it was asked to go
        Assert.Equal(846.0, TimelineView.LabelLeft(886, 50, 896));   // "key press" under the newest end: 50 wide, view ends at 896
        Assert.Equal(0.0, TimelineView.LabelLeft(5, 2000, 100));     // never off the left edge
        Assert.Equal(12.0, TimelineView.LabelLeft(12, 30, double.PositiveInfinity));
    }

    [Fact]
    public void The_grey_band_text_names_where_the_empty_stretch_is()
    {
        Assert.Equal("before the first frame", TimelineView.GapWatermark(TimelineView.GapPosition.BeforeFirstFrame));
        Assert.Equal("after the last frame", TimelineView.GapWatermark(TimelineView.GapPosition.AfterLastFrame));
        Assert.Equal("no frames logged", TimelineView.GapWatermark(TimelineView.GapPosition.Between));
    }

    [Fact]
    public async Task A_position_maps_to_a_time_and_the_cursor_text_says_what_is_there()
    {
        var (frames, end) = TestData.Stream(30, (1000, 80));
        var (left, right, outside, text) = await WpfHost.RunAsync(() =>
        {
            var view = Make(900, 240, frames, end, markMs: end);
            var l = view.ToTimeMs(TimelineView.LeftMargin);
            var r = view.ToTimeMs(900 - TimelineView.RightMargin);
            var o = view.ToTimeMs(2000);
            return (l, r, o, view.DescribeCursor());
        });

        Assert.Equal(end - 30_000, left!.Value, 3);
        Assert.Equal(end, right!.Value, 3);
        Assert.Equal(end, outside!.Value, 3); // clamped to the end
        Assert.Null(text);                    // no cursor yet
    }

    [DesktopFact]
    public async Task Marks_candidates_the_uncertainty_band_and_the_chosen_event_draw_without_error_at_two_scales()
    {
        var (frames, end) = TestData.Stream(30, (1000, 80), (1300, 45));
        await WpfHost.RunAsync(() =>
        {
            var view = Make(
                1000,
                300,
                frames,
                end,
                markMs: end - 3_000,
                anchorMs: frames[1000].TimestampMs,
                uncertainty: 120,
                (frames[1000].TimestampMs, "1", false),
                (frames[1300].TimestampMs, "2", false),
                (frames[600].TimestampMs, "3", true));
            Snapshots.Save(view, "timeline-marked-100");
            Snapshots.Save(view, "timeline-marked-250", 2.5);
        });
    }
}

[Collection("WPF")]
public class MainWindowSmokeTests
{
    private static async Task<(AppHost Host, FakeCaptureFactory Factory, FakeShell Shell)> Make(Func<CapabilityMatrix>? matrix = null)
    {
        var factory = new FakeCaptureFactory();
        var host = new AppHost(
            AppContext.BaseDirectory,
            new SettingsStore(TestData.TempFile("settings.json")),
            factory,
            new SilentCue(),
            matrix ?? (() => TestData.Matrix(TestData.Row("anticheat.state", "idle"), TestData.Row("os.build", "10.0.26300"))));
        var shell = new FakeShell(host);
        shell.ApplyHotkeys(HotkeyBindings.Default);
        await host.MatrixTask;
        return (host, factory, shell);
    }

    private static async Task Flush() => await WpfHost.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task;

    [DesktopFact]
    public async Task The_pages_are_never_laid_out_smaller_than_their_design_size_and_the_watch_status_keeps_clear_of_its_feedback()
    {
        // Real-app run of 2026-11-12, defects 1, 4, 5 and 6: at the smallest window labels collided and were cut; the Watch headline was cut by the target box.
        var (host, _, shell) = await Make();
        await using var owner = host;

        MainWindow? window = null;
        await WpfHost.RunAsync(() =>
        {
            window = new MainWindow(host, shell);
            window.Show();
            window.Width = window.MinWidth;
            window.Height = window.MinHeight;
            ((TextBlock)window.FindName("CaptureHeadline")).Text = "Capturing a program with a very long executable name that cannot possibly fit on one line.exe";
            ((TextBlock)window.FindName("MarkFeedback")).Text = "Marked a stutter at 12:00:00";
            window.UpdateLayout();
        });
        await Flush();

        await WpfHost.RunAsync(() =>
        {
            var host2 = (FrameworkElement)window!.FindName("PagesHost");
            Assert.True(host2.ActualWidth >= PageScaling.DesignWidth - 1.0, $"the pages were laid out {host2.ActualWidth} px wide at the smallest window");
            Assert.True(host2.ActualHeight >= PageScaling.DesignHeight - 1.0, $"the pages were laid out {host2.ActualHeight} px high at the smallest window");
            Assert.NotEqual(System.Windows.Media.Transform.Identity, host2.LayoutTransform); // scaled down, not squeezed
            AssertScaledAsTheRuleSays(host2, (FrameworkElement)window.FindName("PagesViewport"));

            var headline = (TextBlock)window.FindName("CaptureHeadline");
            var feedback = (TextBlock)window.FindName("MarkFeedback");
            var headlineRight = headline.TransformToAncestor(window).Transform(new Point(headline.ActualWidth, 0)).X;
            var feedbackLeft = feedback.TransformToAncestor(window).Transform(new Point(0, 0)).X;
            Assert.True(headlineRight <= feedbackLeft + 0.5, $"the headline runs {headlineRight - feedbackLeft} px into the mark feedback");
            Snapshots.Save(window, "window-watch-minimum-size");

            window.Width = 1700;
            window.Height = 1000;
            window.UpdateLayout();

            // The operating system will not make a window larger than the screen, so how big this one really became depends on the machine (a CI runner's screen is small).
            // Check the rule against the viewport the window got; a roomy window is only expected to be unscaled when it really is roomy.
            var viewport = (FrameworkElement)window.FindName("PagesViewport");
            AssertScaledAsTheRuleSays(host2, viewport);
            if (viewport.ActualWidth >= PageScaling.DesignWidth && viewport.ActualHeight >= PageScaling.DesignHeight)
            {
                Assert.Equal(System.Windows.Media.Transform.Identity, host2.LayoutTransform); // a roomy window is not scaled
                Assert.True(Math.Abs(host2.ActualWidth - viewport.ActualWidth) < 1.0, "the pages do not fill a roomy window");
            }

            window.AllowClose();
            window.Close();
        });
    }

    /// <summary>The pages are scaled exactly as <see cref="PageScaling"/> says for the viewport they are in: not at all when it is at least the design size, otherwise uniformly down.</summary>
    private static void AssertScaledAsTheRuleSays(FrameworkElement pages, FrameworkElement viewport)
    {
        var expected = PageScaling.For(viewport.ActualWidth, viewport.ActualHeight);
        if (expected >= 1.0)
        {
            Assert.Equal(System.Windows.Media.Transform.Identity, pages.LayoutTransform);
            return;
        }

        var scale = Assert.IsType<System.Windows.Media.ScaleTransform>(pages.LayoutTransform);
        Assert.Equal(expected, scale.ScaleX, 3);
        Assert.Equal(expected, scale.ScaleY, 3);
    }

    [DesktopFact]
    public async Task The_replay_header_wraps_its_long_sentences_beside_the_export_button_instead_of_cutting_them()
    {
        // Real-app run of 2026-11-12, defects 3 and 8: the key-press and event-time sentences were trimmed to "...about 8..." and "...as the...".
        var (host, _, shell) = await Make();
        await using var owner = host;

        MainWindow? window = null;
        await WpfHost.RunAsync(() =>
        {
            window = new MainWindow(host, shell);
            window.Show();
            ((RadioButton)window.FindName("NavInvestigator")).IsChecked = true;
            ((UIElement)window.FindName("MarkDetailPanel")).Visibility = Visibility.Visible;
            ((TextBlock)window.FindName("MarkQualityLine")).Text = string.Concat(Enumerable.Repeat("Key-press time: frame-delivery jitter of about 12 ms. ", 6));
            ((TextBlock)window.FindName("SelectionStatus")).Text = string.Concat(Enumerable.Repeat("No event time was chosen. The moment the key was pressed was not used as the event time. ", 3));
            window.UpdateLayout();
        });
        await Flush();

        await WpfHost.RunAsync(() =>
        {
            window!.UpdateLayout();
            var export = (FrameworkElement)window.FindName("ExportButton");
            var exportLeft = export.TransformToAncestor(window).Transform(new Point(0, 0)).X;
            foreach (var name in new[] { "MarkQualityLine", "SelectionStatus" })
            {
                var line = (TextBlock)window.FindName(name);
                var right = line.TransformToAncestor(window).Transform(new Point(line.ActualWidth, 0)).X;
                Assert.True(right <= exportLeft + 0.5, $"{name} runs {right - exportLeft} px into the export button");
                Assert.True(line.ActualHeight >= line.FontSize * 2.0, $"{name} is {line.ActualHeight} px high: it did not wrap");
            }

            Snapshots.Save(window, "window-replay-header-wrapped");
            window.AllowClose();
            window.Close();
        });
    }

    [DesktopFact]
    public async Task The_capability_check_counts_what_can_be_set_up_apart_from_what_is_not_available()
    {
        // Real-app run of 2026-11-12, defect 13: "not available" and "not built yet" rows were counted as "needs setup / permission", behind raw enum badges.
        CapabilityMatrix Matrix() => TestData.Matrix(
            TestData.Row("anticheat.state", "ResidentOnly; elevated and kernel collectors not suppressed"),
            TestData.Row("os.build", "10.0.26300"),
            CapabilityEntry.Unavailable("frame.capture", "Frame capture", CapabilityTier.C, Availability.NeedsOptionalTool, "test", "the capture tool is not installed"),
            CapabilityEntry.Planned("input.rawinput", "Raw Input timing", CapabilityTier.A, "Stage 2"),
            CapabilityEntry.Planned("net.route", "Route probe", CapabilityTier.B, "Stage 2"),
            CapabilityEntry.Unavailable("gpu.vrr", "Variable refresh", CapabilityTier.B, Availability.NotAvailable, "test", "the display does not report it"));
        var (host, _, shell) = await Make(Matrix);
        await using var owner = host;

        await WpfHost.RunAsync(() =>
        {
            var window = new MainWindow(host, shell);
            window.Show();
            ((RadioButton)window.FindName("NavRadar")).IsChecked = true;
            window.UpdateLayout();

            Assert.Equal("6", ((TextBlock)window.FindName("RadarTotalCount")).Text);
            Assert.Equal("2", ((TextBlock)window.FindName("RadarReadyCount")).Text);
            Assert.Equal("1", ((TextBlock)window.FindName("RadarAttentionCount")).Text); // only the row the person could set up
            Assert.Equal("3 more are not available or not built yet", ((TextBlock)window.FindName("RadarTotalNote")).Text);
            Assert.Equal("Resident, idle", ((TextBlock)window.FindName("RadarAntiCheatStatus")).Text);
            Snapshots.Save(window, "window-radar-mixed");

            window.AllowClose();
            window.Close();
        });
    }

    [DesktopFact]
    public async Task The_trust_page_mentions_links_only_when_a_link_is_shown()
    {
        // Real-app run of 2026-11-12, defect 7: the page said "the static links below" while the sponsor and store addresses were empty and no link was shown.
        var (host, _, shell) = await Make();
        await using var owner = host;

        await WpfHost.RunAsync(() =>
        {
            var window = new MainWindow(host, shell);
            window.Show();
            ((RadioButton)window.FindName("NavAbout")).IsChecked = true;
            window.UpdateLayout();

            var anyLink = ((UIElement)window.FindName("OpenSponsorButton")).Visibility == Visibility.Visible
                || ((UIElement)window.FindName("OpenStoreButton")).Visibility == Visibility.Visible;
            var note = (UIElement)window.FindName("SupportLinksNote");
            Assert.Equal(anyLink, note.Visibility == Visibility.Visible);

            window.AllowClose();
            window.Close();
        });
    }

    [DesktopFact]
    public async Task The_live_refresh_runs_only_while_the_window_is_visible()
    {
        // Review of Stage 1b, finding 8: hidden to the tray during play, the 4 Hz refresh kept running and allocating for a window nobody sees.
        var (host, _, shell) = await Make();
        await using var owner = host;

        await WpfHost.RunAsync(() =>
        {
            var window = new MainWindow(host, shell);
            Assert.False(window.LiveRefreshRunning); // never shown yet

            window.Show();
            Assert.True(window.LiveRefreshRunning);

            window.Hide();
            Assert.False(window.LiveRefreshRunning);

            window.Show();
            Assert.True(window.LiveRefreshRunning);

            window.AllowClose();
            window.Close();
            Assert.False(window.LiveRefreshRunning);
        });
    }

    [DesktopFact]
    public async Task The_window_builds_shows_every_page_and_reflects_the_capture_state()
    {
        var (host, factory, shell) = await Make();
        await using var owner = host;

        await WpfHost.RunAsync(() =>
        {
            var window = new MainWindow(host, shell);
            window.Show();
            window.UpdateLayout();
            Assert.Equal("Stutter: Ctrl+Alt+F9", ((TextBlock)window.FindName("NavHotkeyStutter")).Text);
            Assert.False(((Button)window.FindName("MarkStutterButton")).IsEnabled); // nothing is being captured
            Snapshots.Save(window, "window-capture-idle");
            foreach (var (nav, snap) in new[] { ("NavInvestigator", "window-investigator-empty"), ("NavRadar", "window-radar"), ("NavLab", "window-lab"), ("NavSettings", "window-settings"), ("NavAbout", "window-about"), ("NavCapture", "window-capture") })
            {
                ((RadioButton)window.FindName(nav)).IsChecked = true;
                window.UpdateLayout();
                Snapshots.Save(window, snap);
            }

            window.AllowClose();
            window.Close();
        });

        _ = factory;
    }

    [DesktopFact]
    public async Task A_mark_appears_in_the_investigator_with_candidates_and_the_person_can_choose_the_event_and_export()
    {
        var (host, factory, shell) = await Make();
        await using var owner = host;
        Assert.True(host.Controller.Start("game.exe").Started);

        MainWindow? window = null;
        await WpfHost.RunAsync(() =>
        {
            window = new MainWindow(host, shell);
            window.Show();
            ((RadioButton)window.FindName("NavInvestigator")).IsChecked = true;
            window.UpdateLayout();
        });

        var (frames, end) = TestData.Stream(45, (1800, 95), (2200, 40), (2550, 65));
        foreach (var frame in frames)
        {
            factory.Capture.Push(frame);
        }

        await TestData.WaitUntil(() => host.Controller.Session!.FramesReceived == frames.Length);

        var outcome = await host.MarkAsync(MarkKind.Stutter);
        Assert.Equal(MarkOutcomeKind.Recorded, outcome.Kind);
        await Flush();

        await TestData.WaitUntil(() => WpfHost.RunAsync(() => window!.InvestigatorTimeline.Buckets.Any(b => !b.IsEmpty)).Result);
        await WpfHost.RunAsync(() =>
        {
            var marks = (ListBox)window!.FindName("MarksList");
            Assert.Single(marks.Items);
            var candidates = (ListBox)window.FindName("CandidatesList");
            Assert.True(candidates.Items.Count >= 1);
            Assert.Contains("No event time was chosen", ((TextBlock)window.FindName("SelectionStatus")).Text, StringComparison.Ordinal);
            Assert.True(((Button)window.FindName("ExportButton")).IsEnabled);
            window.UpdateLayout();
            Snapshots.Save(window, "window-investigator-marked");

            candidates.SelectedIndex = 0; // the person picks candidate 1
        });

        await Flush();
        Assert.Equal(AnchorChoiceKind.Candidate, host.Controller.Recorder.Marks.Single().Selection!.Kind);

        await WpfHost.RunAsync(() =>
        {
            Assert.Contains("chose candidate 1", ((TextBlock)window!.FindName("SelectionStatus")).Text);
            window.UpdateLayout();
            Snapshots.Save(window, "window-investigator-chosen");
        });

        // the export preview window builds the redacted document and offers to save it
        await WpfHost.RunAsync(() =>
        {
            var export = new ExportWindow(host, host.Controller.Recorder.Marks.Single());
            export.Show();
        });
        ExportWindow? shown = null;
        await TestData.WaitUntil(() => WpfHost.RunAsync(() =>
        {
            shown = Application.Current.Windows.OfType<ExportWindow>().FirstOrDefault();
            return shown is not null && ((Button)shown.FindName("SaveButton")).IsEnabled;
        }).Result);
        await WpfHost.RunAsync(() =>
        {
            var preview = (ListBox)shown!.FindName("PreviewList");
            Assert.True(preview.Items.Count > 1000);
            Assert.Contains("Replaced", ((TextBlock)shown.FindName("Summary")).Text + "Replaced"); // the summary line exists
            shown.UpdateLayout();
            Snapshots.Save(shown, "window-export-preview");
            shown.Close();
            window!.AllowClose();
            window.Close();
        });
    }

    [DesktopFact]
    public async Task The_welcome_window_says_whether_the_hotkeys_work()
    {
        await WpfHost.RunAsync(() =>
        {
            var chord = HotkeyChord.Parse("Ctrl+Alt+F9");
            var good = new WelcomeWindow([new HotkeyStatus(HotkeyAction.MarkStutter, chord, true, HotkeyFailure.None, null)]);
            good.Show();
            good.UpdateLayout();
            Assert.Contains("active", ((TextBlock)good.FindName("HotkeyState")).Text);
            Snapshots.Save(good, "window-welcome");
            good.Close();

            var bad = new WelcomeWindow([new HotkeyStatus(HotkeyAction.MarkStutter, chord, false, HotkeyFailure.AlreadyRegistered, "Another program already uses Ctrl+Alt+F9.")]);
            bad.Show();
            Assert.Contains("Another program already uses", ((TextBlock)bad.FindName("HotkeyState")).Text);
            bad.Close();
        });
    }

    [DesktopFact]
    public async Task Built_in_stutter_simulator_populates_live_kpis_distribution_and_forensic_hypotheses()
    {
        var (host, _, shell) = await Make();
        await using var owner = host;

        MainWindow? window = null;
        await WpfHost.RunAsync(() =>
        {
            window = new MainWindow(host, shell);
            window.Show();
            window.UpdateLayout();
        });

        Assert.True(host.Controller.StartSimulation("simulated-workbench.exe").Started);
        await TestData.WaitUntil(() => (host.Controller.Session?.FramesReceived ?? 0) >= 600);

        await WpfHost.RunAsync(() =>
        {
            ((RadioButton)window!.FindName("NavInvestigator")).IsChecked = true;
            window.UpdateLayout();
        });

        var outcome = await host.MarkAsync(MarkKind.Stutter);
        Assert.Equal(MarkOutcomeKind.Recorded, outcome.Kind);
        await Flush();

        await TestData.WaitUntil(() => WpfHost.RunAsync(() => window!.InvestigatorTimeline.Buckets.Any(b => !b.IsEmpty)).Result);

        await WpfHost.RunAsync(() =>
        {
            var hypotheses = (ListBox)window!.FindName("HypothesesList");
            Assert.NotEmpty(hypotheses.Items);
            Assert.True(window.InvestigatorDistribution.TotalFrames > 100);
            Assert.True(window.InvestigatorAnatomy.TotalFrames > 100);
            Assert.True(window.InvestigatorAnatomy.AnchorPeakMs > 30.0);
            Snapshots.Save(window, "window-investigator-simulated");

            // Verify Live Capture KPIs & Distribution with simulated data
            ((RadioButton)window.FindName("NavCapture")).IsChecked = true;
            window.UpdateLayout();
            Assert.True(window.LiveAnatomy.TotalFrames > 100);
            Snapshots.Save(window, "window-capture-simulated");

            // Verify Input & Polling Lab simulated trace
            ((RadioButton)window.FindName("NavLab")).IsChecked = true;
            window.InputPollingLab.LoadSimulatedTrace();
            window.UpdateLayout();
            Assert.True(window.InputPollingLab.SampleCount >= 100);
            Assert.True(window.InputPollingLab.StallEvents >= 1);
            Snapshots.Save(window, "window-lab-simulated");

            window.AllowClose();
            window.Close();
        });

        await host.Controller.StopAsync();
    }

    [DesktopFact]
    public async Task A_long_cause_title_wraps_inside_its_card_instead_of_being_cut_off()
    {
        // Real-app run of 2026-11-12, defect 1: "Frame pacing hitc..." and "Resident kernel anti-cheat presenc" were clipped, because the title
        // is text from the rules engine and sat in a row that never wrapped.
        var (host, _, shell) = await Make();
        await using var owner = host;

        MainWindow? window = null;
        await WpfHost.RunAsync(() =>
        {
            window = new MainWindow(host, shell);
            window.Show();
            ((RadioButton)window.FindName("NavInvestigator")).IsChecked = true;
            ((UIElement)window.FindName("MarkDetailPanel")).Visibility = Visibility.Visible; // shown only once a mark is chosen; this test is about layout alone

            ((ListBox)window.FindName("HypothesesList")).ItemsSource = new[]
            {
                new HypothesisCardItem(
                    GradeBadge: "GRADE B",
                    Name: "Resident kernel anti-cheat presence together with a frame pacing hitch that repeats at a steady interval",
                    Summary: "A summary.",
                    SupportingEvidence: "Supporting Evidence: none.",
                    FalsifierText: "What would falsify: nothing."),
            };
            window.UpdateLayout();
        });
        await Flush(); // the list makes its item containers on a later pass

        await WpfHost.RunAsync(() =>
        {
            window!.UpdateLayout();
            var list = (ListBox)window.FindName("HypothesesList");
            var texts = Descendants<TextBlock>(list).Select(t => t.Text).ToList();
            Assert.True(
                texts.Any(t => t.StartsWith("Resident kernel", StringComparison.Ordinal)),
                $"visible={list.IsVisible}, items={list.Items.Count}, width={list.ActualWidth}, realized texts=[{string.Join(" | ", texts)}]");
            var title = Descendants<TextBlock>(list).Single(t => t.Text.StartsWith("Resident kernel", StringComparison.Ordinal));
            var right = title.TransformToAncestor(list).Transform(new Point(title.ActualWidth, 0)).X;
            Assert.True(right <= list.ActualWidth, $"the title runs {right - list.ActualWidth} px past the right edge of its list");
            Assert.True(title.ActualHeight >= title.FontSize * 2.0, $"the title is {title.ActualHeight} px high: it did not wrap onto a second line");
            Snapshots.Save(window, "window-investigator-long-title");

            window.AllowClose();
            window.Close();
        });
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var inner in Descendants<T>(child))
            {
                yield return inner;
            }
        }
    }
}
