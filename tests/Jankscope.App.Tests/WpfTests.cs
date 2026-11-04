using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Jankscope.App.Services;
using Jankscope.App.Views;
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
                    if (data[(y * stride) + ((x + dx) * 4)] > 150) // the blue channel of the line colour; the background is dark
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
    private static async Task<(AppHost Host, FakeCaptureFactory Factory, FakeShell Shell)> Make()
    {
        var factory = new FakeCaptureFactory();
        var host = new AppHost(
            AppContext.BaseDirectory,
            new SettingsStore(TestData.TempFile("settings.json")),
            factory,
            new SilentCue(),
            () => TestData.Matrix(TestData.Row("anticheat.state", "idle"), TestData.Row("os.build", "10.0.26300")));
        var shell = new FakeShell(host);
        shell.ApplyHotkeys(HotkeyBindings.Default);
        await host.MatrixTask;
        return (host, factory, shell);
    }

    private static async Task Flush() => await WpfHost.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task;

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
            foreach (var (nav, snap) in new[] { ("NavInvestigator", "window-investigator-empty"), ("NavSettings", "window-settings"), ("NavAbout", "window-about"), ("NavCapture", "window-capture") })
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
}
