using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Jankscope.App.Services;
using Jankscope.Core;
using Jankscope.Core.Hotkeys;
using Jankscope.Frames;
using Jankscope.Frames.Detection;
using Jankscope.Frames.Incidents;
using Jankscope.Frames.Ring;
using Jankscope.Platform.Hotkeys;

namespace Jankscope.App.Views;

public partial class MainWindow : Window
{
    private const double LiveWindowMs = 30_000;

    private readonly AppHost _host;
    private readonly IShell _shell;
    private readonly DispatcherTimer _tick = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _noticeTimer = new() { Interval = TimeSpan.FromSeconds(9) };
    private readonly ObservableCollection<MarkItem> _marks = [];
    private readonly FrameSample[] _liveBuffer = new FrameSample[32_768];
    private readonly FrameSample[] _newest = new FrameSample[1];
    private bool _updating;
    private bool _aboutFilled;
    private bool _quitting;
    private int _markCounter;
    private string? _shownMarkId;
    private CollectedWindow? _window;
    private DateTime _windowAt = DateTime.MinValue;
    private bool _collecting;
    private (double Start, double End)? _view;

    /// <summary>The live (last 30 s) timeline on the capture page. Created in code, not in XAML (see the note in MainWindow.xaml).</summary>
    public TimelineView LiveTimeline { get; } = new() { Height = 210 };

    /// <summary>The timeline of the selected mark on the investigator page.</summary>
    public TimelineView InvestigatorTimeline { get; } = new() { Height = 260 };

    public MainWindow(AppHost host, IShell shell)
    {
        _host = host;
        _shell = shell;
        InitializeComponent();
        Icon = IconFactory.WindowIcon();
        MarksList.ItemsSource = _marks;
        System.Windows.Automation.AutomationProperties.SetName(LiveTimeline, "Live frame times for the last 30 seconds");
        System.Windows.Automation.AutomationProperties.SetName(InvestigatorTimeline, "Frame times around the mark. Arrow keys move a cursor; Enter chooses that time as the event.");
        LiveTimelineHost.Content = LiveTimeline;
        InvestigatorTimelineHost.Content = InvestigatorTimeline;
        ProgramsList.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, new RoutedEventHandler(OnTrackProgramClicked));

        var settings = host.Settings;
        StutterChordBox.Text = settings.MarkStutterChord;
        StallChordBox.Text = settings.MarkInputStallChord;
        PreRollBox.Text = settings.PreRollSeconds.ToString("0", CultureInfo.InvariantCulture);
        PostRollBox.Text = settings.PostRollSeconds.ToString("0", CultureInfo.InvariantCulture);
        PreRollBox.LostFocus += OnRollBoxLostFocus;
        PostRollBox.LostFocus += OnRollBoxLostFocus;
        SoundCheck.IsChecked = settings.SoundEnabled;
        ThemeSystem.IsChecked = settings.Theme == "System";
        ThemeLight.IsChecked = settings.Theme == "Light";
        ThemeDark.IsChecked = settings.Theme == "Dark";
        ProgramBox.Text = settings.LastProgram ?? string.Empty;
        AboutVersion.Text = "Version " + (typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0.1.0") + " (Stage 1b preview). Free and open source.";
        AboutPrivacy.Text =
            "Nothing leaves this PC: there is no telemetry, no update check and no upload. A report is written only where you choose, after you have seen exactly what it contains. " +
            "Jankscope reads the timing of frames being presented by the one program you pick. It does not read what you type (the mark hotkeys are registered with Windows, which " +
            "tells Jankscope only when exactly that chord is pressed), mouse positions, screen contents, documents, browser data or passwords. In this version it changes no setting of Windows or of any program.";
        AboutAntiCheat.Text =
            "Jankscope uses only documented, user-mode Windows interfaces. It does not open or modify the game, does not inject code, does not install drivers and does not draw anything over the game. " +
            "No anti-cheat provider has approved it, and none publishes a list of what is safe, so whether a particular game accepts a capture tool is for you to confirm. If you are unsure, do not use it with that game.";

        _tick.Tick += (_, _) => OnTick();
        _noticeTimer.Tick += (_, _) =>
        {
            _noticeTimer.Stop();
            NoticeBar.Visibility = Visibility.Collapsed;
        };
        _host.MarkChanged += OnHostMarkChanged;
        Loaded += (_, _) =>
        {
            RefreshHotkeyTexts();
            RefreshStatus();
            _tick.Start();
            foreach (var problem in _host.Problems)
            {
                ShowNotice(problem, problem: true);
            }
        };
        Closing += OnClosing;
    }

    public void AllowClose() => _quitting = true;

    public void ShowNotice(string text, bool problem)
    {
        NoticeText.Text = text;
        NoticeText.Foreground = (Brush)FindResource(problem ? "WarningBrush" : "ForegroundBrush");
        NoticeBar.Visibility = Visibility.Visible;
        _noticeTimer.Stop();
        _noticeTimer.Start();
    }

    public void ShowPage(int page)
    {
        (page switch { 1 => NavInvestigator, 2 => NavSettings, 3 => NavAbout, _ => NavCapture }).IsChecked = true;
    }

    // ---- window ----

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_quitting)
        {
            return;
        }

        // Closing the window hides it: capture continues, the tray icon stays, and Quit in the tray menu ends everything.
        e.Cancel = true;
        Hide();
        _shell.ChangeWindowVisibility(false);
    }

    private void OnNavChecked(object sender, RoutedEventArgs e)
    {
        if (PageCapture is null || PageInvestigator is null || PageSettings is null || PageAbout is null)
        {
            return; // fired while the window is still being built
        }

        PageCapture.Visibility = NavCapture.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageInvestigator.Visibility = NavInvestigator.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageSettings.Visibility = NavSettings.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageAbout.Visibility = NavAbout.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (NavAbout.IsChecked == true)
        {
            FillAbout();
        }

        if (NavInvestigator.IsChecked == true)
        {
            RefreshInvestigatorView(forceCollect: true);
        }
    }

    // ---- status and live view ----

    private void OnTick()
    {
        RefreshStatus();
        if (PageCapture.Visibility == Visibility.Visible)
        {
            RefreshLive();
        }

        RefreshMarkProgress();
        if (PageInvestigator.Visibility == Visibility.Visible)
        {
            RefreshInvestigatorView(forceCollect: false);
        }

        if (PageAbout.Visibility == Visibility.Visible && !_aboutFilled)
        {
            FillAbout();
        }
    }

    private void RefreshHotkeyTexts()
    {
        var statuses = _shell.HotkeyStatuses;
        NavHotkeyStutter.Text = HotkeyLine("Stutter", HotkeyAction.MarkStutter, statuses);
        NavHotkeyStall.Text = HotkeyLine("Input stall", HotkeyAction.MarkInputStall, statuses);
        MarkStutterButton.Content = AppHost.MenuText(HotkeyAction.MarkStutter, statuses);
        MarkStallButton.Content = AppHost.MenuText(HotkeyAction.MarkInputStall, statuses);
    }

    private static string HotkeyLine(string label, HotkeyAction action, IReadOnlyList<HotkeyStatus> statuses)
    {
        var status = statuses.FirstOrDefault(s => s.Action == action);
        return status is null ? $"{label}: not set" : status.Active ? $"{label}: {status.Chord}" : $"{label}: not registered";
    }

    private void RefreshStatus()
    {
        var status = _host.Controller.Status();
        StatusHeadline.Text = status.Headline;
        var detail = status.Detail;
        if (status.Problems.Count > 0 && status.State is CaptureState.Failed or CaptureState.Restarting)
        {
            detail = (detail is null ? string.Empty : detail + " ") + "Last problem: " + status.Problems[^1];
        }

        StatusDetail.Text = detail ?? string.Empty;
        StatusDetail.Visibility = string.IsNullOrEmpty(detail) ? Visibility.Collapsed : Visibility.Visible;
        NavCaptureState.Text = status.Headline;
        NavCaptureState.Foreground = (Brush)FindResource(status.State switch
        {
            CaptureState.Capturing => "OkBrush",
            CaptureState.Failed => "DangerBrush",
            CaptureState.Idle => "MutedBrush",
            _ => "WarningBrush",
        });

        StopButton.IsEnabled = status.IsActive;
        MarkStutterButton.IsEnabled = status.IsActive;
        MarkStallButton.IsEnabled = status.IsActive;
        ScanButton.IsEnabled = !status.IsActive && _scanning == false;
    }

    private bool _scanning;

    private void RefreshLive()
    {
        var count = _host.Controller.History.CopyLatest(_liveBuffer);
        var status = _host.Controller.Status();
        if (count == 0)
        {
            LiveSummary.Text = status.IsActive ? "No frames yet." : "Not capturing. Choose a program above and press Watch.";
            LiveTimeline.Show(new TimelineData(default, null, 0, LiveWindowMs, null, null, null, null, null, [], []));
            return;
        }

        var all = new ReadOnlySpan<FrameSample>(_liveBuffer, 0, count);
        var newest = all[^1].TimestampMs;
        var window = FrameRingExtensions.SelectWindow(all, newest, LiveWindowMs);
        var memory = new ReadOnlyMemory<FrameSample>(_liveBuffer, count - window.Length, window.Length);
        var detected = StutterDetector.Analyze(window);
        var last5 = FrameRingExtensions.SelectWindow(window, newest, 5_000);
        var times = new List<double>(last5.Length);
        foreach (var frame in last5)
        {
            if (double.IsFinite(frame.FrameTimeMs) && frame.FrameTimeMs > 0)
            {
                times.Add(frame.FrameTimeMs);
            }
        }

        var all30 = new List<double>(window.Length);
        foreach (var frame in window)
        {
            if (double.IsFinite(frame.FrameTimeMs) && frame.FrameTimeMs > 0)
            {
                all30.Add(frame.FrameTimeMs);
            }
        }

        var fps = FrameStats.AverageFps([.. times]);
        var p99 = FrameStats.PercentileFrameTimeMs([.. all30], 99.0);
        var parts = new List<string>
        {
            double.IsNaN(fps) ? "frame rate not available" : $"{fps.ToString("0", CultureInfo.InvariantCulture)} frames/s (average, last 5 s)",
            double.IsNaN(p99) ? "99th percentile not available" : $"{p99.ToString("0.0", CultureInfo.InvariantCulture)} ms 99th percentile frame time (last 30 s)",
            $"{detected.Events.Count} stutter(s) in the last 30 s, {detected.IsolatedSpikeCount} of them isolated spikes ({detected.CalibrationLabel} threshold)",
        };
        if (status.DroppedFrames > 0)
        {
            parts.Add($"{status.DroppedFrames} frame(s) dropped by the capture queue");
        }

        if (status.IgnoredFrames > 0)
        {
            parts.Add($"{status.IgnoredFrames} frame(s) ignored (not swap-chain presents)");
        }

        LiveSummary.Text = string.Join("  |  ", parts);
        LiveTimeline.Show(new TimelineData(memory, null, newest - LiveWindowMs, newest, newest, null, null, null, null, [], detected.Events.Select(e => e.TimestampMs).ToList()));
    }

    // ---- capture page ----

    private async void OnScanClicked(object sender, RoutedEventArgs e)
    {
        if (!_host.CanCapture)
        {
            ShowNotice("The frame-capture tool is not available (see About and privacy).", problem: true);
            return;
        }

        _scanning = true;
        ScanButton.IsEnabled = false;
        ScanStatus.Text = "  Looking for 5 seconds. Make sure the program is running and showing frames...";
        ProgramsList.ItemsSource = null;
        try
        {
            var programs = await _host.Controller.ScanAsync(5);
            ProgramsList.ItemsSource = programs;
            ScanStatus.Text = programs.Count == 0
                ? "  No program drew frames. Start your game (a windowed or borderless mode is easiest) and try again, or type its file name."
                : "  Pick the one you want to watch.";
        }
        catch (InvalidOperationException ex)
        {
            ScanStatus.Text = string.Empty;
            ShowNotice(ex.Message, problem: true);
        }
        finally
        {
            _scanning = false;
            RefreshStatus();
        }
    }

    private void OnTrackProgramClicked(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is Button { Tag: string name })
        {
            StartCapture(name);
        }
    }

    private void OnWatchTypedClicked(object sender, RoutedEventArgs e) => StartCapture(ProgramBox.Text);

    private void StartCapture(string name)
    {
        var result = _host.Controller.Start(name);
        if (!result.Started)
        {
            ShowNotice(result.Message ?? "The capture could not start.", problem: true);
            return;
        }

        ProgramBox.Text = name.Trim();
        ProgramsList.ItemsSource = null;
        ScanStatus.Text = string.Empty;
        _host.Update(s => s with { LastProgram = name.Trim() }, out var problem);
        if (problem is not null)
        {
            ShowNotice(problem, problem: true);
        }

        RefreshStatus();
    }

    private async void OnStopClicked(object sender, RoutedEventArgs e)
    {
        StopButton.IsEnabled = false;
        await _host.Controller.StopAsync();
        RefreshStatus();
    }

    private void OnMarkStutterClicked(object sender, RoutedEventArgs e) => _shell.RequestMark(MarkKind.Stutter);

    private void OnMarkStallClicked(object sender, RoutedEventArgs e) => _shell.RequestMark(MarkKind.InputStall);

    // ---- marks and the investigator ----

    private void OnHostMarkChanged(IncidentMark mark) => Dispatcher.BeginInvoke(() => UpsertMark(mark));

    private void UpsertMark(IncidentMark mark)
    {
        var existing = _marks.FirstOrDefault(m => m.Id == mark.Id);
        if (existing is not null)
        {
            existing.Update(mark, 0, false);
            RefreshMarkProgress();
            if (_shownMarkId == mark.Id && !_updating)
            {
                ShowMark(mark, keepView: true);
            }

            return;
        }

        var item = new MarkItem(mark, ++_markCounter);
        _marks.Insert(0, item);
        NoMarksText.Visibility = Visibility.Collapsed;
        _updating = true;
        MarksList.SelectedItem = item;
        _updating = false;
        ShowMark(mark, keepView: false);
    }

    private void RefreshMarkProgress()
    {
        if (_marks.Count == 0)
        {
            return;
        }

        var newestTimestamp = _host.Controller.History.CopyLatest(_newest) > 0 ? _newest[0].TimestampMs : (double?)null;
        foreach (var item in _marks)
        {
            if (item.Mark.CaptureTime is not { } time || newestTimestamp is not { } newest)
            {
                item.Update(item.Mark, 0, false);
                continue;
            }

            var done = Math.Clamp((newest - time.Ms) / 1000.0, 0, item.Mark.Window.PostRollSeconds);
            item.Update(item.Mark, done, done >= item.Mark.Window.PostRollSeconds - 0.05);
        }
    }

    private void OnMarkSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || MarksList.SelectedItem is not MarkItem item)
        {
            return;
        }

        ShowMark(item.Mark, keepView: false);
    }

    private IncidentMark? CurrentMark => _marks.FirstOrDefault(m => m.Id == _shownMarkId)?.Mark;

    private void ShowMark(IncidentMark mark, bool keepView)
    {
        var changedMark = _shownMarkId != mark.Id;
        _shownMarkId = mark.Id;
        InvestigatorDetail.Visibility = Visibility.Visible;
        var item = _marks.First(m => m.Id == mark.Id);
        MarkTitle.Text = item.Title;
        MarkQuality.Text = DescribeQuality(mark);

        _updating = true;
        CandidatesList.ItemsSource = mark.Search.Candidates.Select(CandidateItem.From).ToList();
        CandidatesList.SelectedItem = mark.Selection is { Kind: AnchorChoiceKind.Candidate, CandidateRank: { } rank }
            ? ((IEnumerable<CandidateItem>)CandidatesList.ItemsSource).FirstOrDefault(c => c.Rank == rank)
            : null;
        _updating = false;

        CandidatesIntro.Text = mark.Search.Candidates.Count == 0
            ? string.Join(" ", mark.Search.Notes)
            : "These moments in the 10 seconds before the key press stand out. The key press is later than the event, so none is chosen for you: pick one, click the timeline, or choose the key-press moment.";
        SelectionStatus.Text = IncidentBundleBuilder.SelectionStatus(mark.Selection);
        SelectionStatus.Foreground = (Brush)FindResource(mark.Selection is null ? "WarningBrush" : "OkBrush");

        if (changedMark || NoteBox.Text != (mark.Note ?? string.Empty) && !NoteBox.IsKeyboardFocused)
        {
            NoteBox.Text = mark.Note ?? string.Empty;
        }

        ExportButton.IsEnabled = mark.CaptureTime is not null;
        ExportHint.Text = mark.CaptureTime is null ? "  This mark has no frames to export." : string.Empty;
        InvestigatorTimeline.IsInteractive = mark.CaptureTime is not null;
        InvestigatorTimeline.TimeChosen -= OnTimelineChosen;
        InvestigatorTimeline.TimeChosen += OnTimelineChosen;

        if (!keepView)
        {
            _view = null;
        }

        RefreshInvestigatorView(forceCollect: changedMark);
    }

    private static string DescribeQuality(IncidentMark mark)
    {
        var parts = new List<string>();
        if (mark.CaptureTime is { } time)
        {
            parts.Add($"Key-press time is uncertain by about {time.UncertaintyMs.ToString("0", CultureInfo.InvariantCulture)} ms.");
            parts.Add($"Before the mark: {mark.Window.AvailablePreRollSeconds.ToString("0", CultureInfo.InvariantCulture)} s recorded of the {mark.Window.RequestedPreRollSeconds.ToString("0", CultureInfo.InvariantCulture)} s asked for" + (mark.Window.PreRollShort ? " (not enough history yet; it is never padded)." : "."));
        }
        else
        {
            parts.Add("No frames had arrived when the key was pressed, so this mark has no place on the timeline.");
        }

        if (mark.TargetProcess is { } program)
        {
            parts.Add("Program: " + program + ".");
        }

        return string.Join(" ", parts);
    }

    private void RefreshInvestigatorView(bool forceCollect)
    {
        if (CurrentMark is not { } mark || mark.CaptureTime is null)
        {
            return;
        }

        var item = _marks.First(m => m.Id == mark.Id);
        var complete = _window is { PostRollComplete: true } && _windowForMark == mark.Id;
        var stale = (DateTime.UtcNow - _windowAt).TotalSeconds >= 1.0;
        if ((forceCollect || (!complete && stale) || _windowForMark != mark.Id) && !_collecting)
        {
            _collecting = true;
            var snapshot = mark;
            _ = Task.Run(() => _host.Controller.Recorder.CollectWindow(snapshot)).ContinueWith(task =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    _collecting = false;
                    if (task.IsCompletedSuccessfully)
                    {
                        _window = task.Result;
                        _windowForMark = snapshot.Id;
                        _windowAt = DateTime.UtcNow;
                        item.Update(snapshot, task.Result.PostRollCollectedSeconds, task.Result.PostRollComplete);
                        DrawInvestigator();
                    }
                });
            }, TaskScheduler.Default);
        }

        DrawInvestigator();
    }

    private string? _windowForMark;

    private void DrawInvestigator()
    {
        if (CurrentMark is not { } mark || mark.CaptureTime is not { } time || mark.Window.StartMs is not { } windowStart || mark.Window.EndMs is not { } windowEnd)
        {
            return;
        }

        var series = _window is not null && _windowForMark == mark.Id ? _window.Frames : mark.PreRoll;
        var first = series.FirstMs ?? windowStart;
        var last = series.LastMs ?? time.Ms;
        var (start, end) = _view ?? (Math.Max(windowStart, first), Math.Max(Math.Min(windowEnd, last), time.Ms + 1.0));
        if (end <= start)
        {
            end = start + 1.0;
        }

        var candidates = mark.Search.Candidates.Select(c => new TimelineMarker(c.TimestampMs, c.Rank.ToString(CultureInfo.InvariantCulture), c.Kind == AnchorKind.WeakSpike)).ToList();
        InvestigatorTimeline.Show(new TimelineData(
            default,
            series,
            start,
            end,
            time.Ms,
            time.Ms,
            time.UncertaintyMs,
            windowEnd,
            mark.Selection?.TimestampMs,
            candidates,
            []));
    }

    private void OnTimelineChosen(object? sender, double ms)
    {
        if (CurrentMark is not { } mark)
        {
            return;
        }

        try
        {
            _host.Controller.Recorder.SelectAdjustedTime(mark.Id, ms);
        }
        catch (ArgumentOutOfRangeException)
        {
            ShowNotice("That time is outside this mark's recording.", problem: true);
        }
    }

    private void OnCandidateSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || CurrentMark is not { } mark || CandidatesList.SelectedItem is not CandidateItem candidate)
        {
            return;
        }

        _host.Controller.Recorder.SelectCandidate(mark.Id, candidate.Rank);
    }

    private void OnUseKeyPressClicked(object sender, RoutedEventArgs e)
    {
        if (CurrentMark is { } mark)
        {
            try
            {
                _host.Controller.Recorder.SelectKeyPressTime(mark.Id);
            }
            catch (InvalidOperationException ex)
            {
                ShowNotice(ex.Message, problem: true);
            }
        }
    }

    private void OnClearSelectionClicked(object sender, RoutedEventArgs e)
    {
        if (CurrentMark is { } mark)
        {
            _host.Controller.Recorder.ClearSelection(mark.Id);
        }
    }

    private void OnNoteLostFocus(object sender, RoutedEventArgs e)
    {
        if (CurrentMark is { } mark && NoteBox.Text.Trim() != (mark.Note ?? string.Empty))
        {
            _host.Controller.Recorder.SetNote(mark.Id, NoteBox.Text);
        }
    }

    private void OnViewWholeClicked(object sender, RoutedEventArgs e)
    {
        _view = null;
        DrawInvestigator();
    }

    private void OnViewLast30Clicked(object sender, RoutedEventArgs e)
    {
        if (CurrentMark is { CaptureTime: { } time })
        {
            _view = (time.Ms - 30_000, time.Ms + 5_000);
            DrawInvestigator();
        }
    }

    private void OnViewAroundEventClicked(object sender, RoutedEventArgs e)
    {
        if (CurrentMark is not { CaptureTime: { } time } mark)
        {
            return;
        }

        var center = mark.Selection?.TimestampMs ?? mark.Search.Candidates.FirstOrDefault()?.TimestampMs ?? time.Ms;
        _view = (center - 5_000, center + 5_000);
        DrawInvestigator();
    }

    private void OnExportClicked(object sender, RoutedEventArgs e)
    {
        if (CurrentMark is not { } mark)
        {
            return;
        }

        if (NoteBox.IsKeyboardFocused || NoteBox.Text.Trim() != (mark.Note ?? string.Empty))
        {
            mark = _host.Controller.Recorder.SetNote(mark.Id, NoteBox.Text);
        }

        new ExportWindow(_host, mark) { Owner = this }.ShowDialog();
    }

    // ---- settings ----

    private void OnApplyHotkeysClicked(object sender, RoutedEventArgs e)
    {
        if (!HotkeyBindings.TryCreate(StutterChordBox.Text, StallChordBox.Text, out var bindings, out var error))
        {
            HotkeyMessages.Text = error;
            HotkeyMessages.Foreground = (Brush)FindResource("DangerBrush");
            return;
        }

        var statuses = _shell.ApplyHotkeys(bindings);
        _host.Update(s => s with { MarkStutterChord = bindings.MarkStutter.ToString(), MarkInputStallChord = bindings.MarkInputStall.ToString() }, out var problem);
        StutterChordBox.Text = bindings.MarkStutter.ToString();
        StallChordBox.Text = bindings.MarkInputStall.ToString();
        var lines = statuses.Select(s => s.Active
            ? $"{(s.Action == HotkeyAction.MarkStutter ? "Stutter" : "Input stall")}: {s.Chord} is registered."
            : $"{(s.Action == HotkeyAction.MarkStutter ? "Stutter" : "Input stall")}: {s.Reason}").ToList();
        if (problem is not null)
        {
            lines.Add(problem);
        }

        HotkeyMessages.Text = string.Join(Environment.NewLine, lines);
        HotkeyMessages.Foreground = (Brush)FindResource(statuses.All(s => s.Active) ? "OkBrush" : "WarningBrush");
        RefreshHotkeyTexts();
    }

    private void OnRestoreHotkeysClicked(object sender, RoutedEventArgs e)
    {
        StutterChordBox.Text = HotkeyBindings.Default.MarkStutter.ToString();
        StallChordBox.Text = HotkeyBindings.Default.MarkInputStall.ToString();
        OnApplyHotkeysClicked(sender, e);
    }

    private void OnRollBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(PreRollBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var pre))
        {
            pre = _host.Settings.PreRollSeconds;
        }

        if (!double.TryParse(PostRollBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var post))
        {
            post = _host.Settings.PostRollSeconds;
        }

        var saved = _host.Update(s => s with { PreRollSeconds = pre, PostRollSeconds = post }, out var problem);
        PreRollBox.Text = saved.PreRollSeconds.ToString("0", CultureInfo.InvariantCulture);
        PostRollBox.Text = saved.PostRollSeconds.ToString("0", CultureInfo.InvariantCulture);
        if (problem is not null)
        {
            ShowNotice(problem, problem: true);
        }
    }

    private void OnThemeChanged(object sender, RoutedEventArgs e)
    {
        if (ThemeSystem is null || ThemeLight is null || ThemeDark is null || !IsLoaded)
        {
            return;
        }

        var theme = ThemeDark.IsChecked == true ? "Dark" : ThemeLight.IsChecked == true ? "Light" : "System";
        _host.Update(s => s with { Theme = theme }, out _);
        _shell.ApplyTheme(theme);
        DrawInvestigator();
    }

    private void OnSoundChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        _host.Update(s => s with { SoundEnabled = SoundCheck.IsChecked == true }, out _);
    }

    private void OnShowWelcomeClicked(object sender, RoutedEventArgs e) => _shell.ShowWelcome();

    // ---- about ----

    private void FillAbout()
    {
        if (!_host.MatrixTask.IsCompletedSuccessfully)
        {
            AboutEnvironment.Text = "Checking this PC (read-only)...";
            return;
        }

        _aboutFilled = true;
        var rows = _host.MatrixTask.Result.Entries
            .Where(r => r.Id.StartsWith("anticheat.", StringComparison.Ordinal) || r.Id == "game.session" || r.Id.StartsWith("etw.", StringComparison.Ordinal))
            .Select(r => $"{r.Title}: {(r.Availability == Availability.Available ? r.Value : r.Availability + " - " + r.Reason)}");
        var lines = rows.Concat(_host.Problems).ToList();
        AboutEnvironment.Text = lines.Count == 0 ? "Nothing to report." : string.Join(Environment.NewLine, lines);

        // Sponsor and store links appear only on Support & About, never during an active game session (HC-13, Criterion 22/23)
        var isGameSession = _host.Controller.IsCapturing;
        var hasSponsor = !string.IsNullOrWhiteSpace(Branding.SponsorUrl);
        var hasStore = !string.IsNullOrWhiteSpace(Branding.StoreUrl);

        if (!isGameSession && (hasSponsor || hasStore))
        {
            SponsorPanel.Visibility = Visibility.Visible;
            SponsorButton.Visibility = hasSponsor ? Visibility.Visible : Visibility.Collapsed;
            StoreButton.Visibility = hasStore ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            SponsorPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void OnExportCompatibilityClicked(object sender, RoutedEventArgs e)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var fileName = $"jankscope-compatibility-{timestamp}.json";
        new ExportWindow(
            _host,
            redact => _host.PrepareCompatibilityExport(redact),
            fileName,
            "Jankscope compatibility report (*.json)|*.json",
            "Save the compatibility report")
        {
            Owner = this,
        }.ShowDialog();
    }

    private void OnSponsorClicked(object sender, RoutedEventArgs e) =>
        ExternalUrlOpener.TryOpen(Branding.SponsorUrl);

    private void OnStoreClicked(object sender, RoutedEventArgs e) =>
        ExternalUrlOpener.TryOpen(Branding.StoreUrl);
}
