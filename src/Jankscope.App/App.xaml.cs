using System.Windows;
using System.Windows.Threading;
using Jankscope.App.Services;
using Jankscope.App.Views;
using Jankscope.Core;
using Jankscope.Core.Hotkeys;
using Jankscope.Core.Reports;
using Jankscope.Frames.Incidents;
using Jankscope.Platform.Hotkeys;

namespace Jankscope.App;

/// <summary>
/// Starts the program: one copy per person, the settings, the hotkeys, the notification-area icon and the window. Closing the window only hides it, so a
/// capture can continue during a game; Quit in the tray menu stops everything (spec section 6.1, ADR 0008).
/// </summary>
public partial class App : Application, IShell
{
    private const string InstanceName = @"Local\" + Branding.ProductName + ".App.SingleInstance";
    private const string ShowEventName = @"Local\" + Branding.ProductName + ".App.Show";
    private const long MaxErrorLogBytes = 256 * 1024;

    private Mutex? _instance;
    private EventWaitHandle? _showEvent;
    private AppHost? _host;
    private HotkeyHost? _hotkeys;
    private TrayIcon? _tray;
    private MainWindow? _main;
    private DispatcherTimer? _trayTimer;
    private bool _quitting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _instance = new Mutex(true, InstanceName, out var first);
        if (!first)
        {
            AskTheRunningCopyToShow();
            Shutdown();
            return;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        new Thread(WaitForShowRequests) { IsBackground = true, Name = "Jankscope show requests" }.Start();

        DispatcherUnhandledException += OnUnhandled;
        SessionEnding += (_, _) => StopCaptureNow();

        _host = new AppHost(AppContext.BaseDirectory, new SettingsStore(SettingsStore.DefaultPath));
        ThemeService.Apply(this, _host.Settings.Theme);

        _hotkeys = new HotkeyHost();
        _hotkeys.Pressed += action => RequestMark(action == HotkeyAction.MarkStutter ? MarkKind.Stutter : MarkKind.InputStall);
        _hotkeys.Apply(_host.Settings.Bindings);

        _tray = new TrayIcon();
        _tray.OpenRequested += ShowMain;
        _tray.MarkStutterRequested += () => RequestMark(MarkKind.Stutter);
        _tray.MarkInputStallRequested += () => RequestMark(MarkKind.InputStall);
        _tray.QuitRequested += Quit;

        _main = new MainWindow(_host, this);
        _trayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _trayTimer.Tick += (_, _) => UpdateTray();
        _trayTimer.Start();
        UpdateTray();

        if (!_host.Settings.FirstRunCompleted)
        {
            ShowWelcome();
            _host.Update(s => s with { FirstRunCompleted = true }, out _);
        }

        if (!e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase))
        {
            ShowMain();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _instance?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // not owned by this thread: nothing to release
        }

        _instance?.Dispose();
        _showEvent?.Dispose();
        base.OnExit(e);
    }

    // ---- IShell ----

    public IReadOnlyList<HotkeyStatus> HotkeyStatuses => _hotkeys?.Statuses ?? [];

    public IReadOnlyList<HotkeyStatus> ApplyHotkeys(HotkeyBindings bindings)
    {
        var statuses = _hotkeys!.Apply(bindings);
        UpdateTray();
        return statuses;
    }

    public void ApplyTheme(string theme) => ThemeService.Apply(this, theme);

    public void ShowWelcome() => new WelcomeWindow(HotkeyStatuses).ShowDialog();

    public void ChangeWindowVisibility(bool visible)
    {
        // The notification-area icon and its tooltip already say the program is running; a pop-up here could land on top of a game.
    }

    public async void RequestMark(MarkKind kind)
    {
        var outcome = await _host!.MarkAsync(kind);
        if (outcome.Kind != MarkOutcomeKind.Debounced)
        {
            _main?.ShowNotice(outcome.Message, outcome.Kind is MarkOutcomeKind.NotCapturing or MarkOutcomeKind.RecordedWithoutTime);
        }

        UpdateTray();
    }

    // ---- lifetime ----

    private void ShowMain()
    {
        if (_main is null)
        {
            return;
        }

        _main.Show();
        if (_main.WindowState == WindowState.Minimized)
        {
            _main.WindowState = WindowState.Normal;
        }

        _main.Activate();
    }

    private void UpdateTray()
    {
        if (_host is null || _tray is null || _hotkeys is null)
        {
            return;
        }

        var statuses = _hotkeys.Statuses;
        var (state, tooltip) = _host.TrayStatus(statuses);
        _tray.Update(state, tooltip);
        _tray.UpdateMenu(AppHost.MenuText(HotkeyAction.MarkStutter, statuses), AppHost.MenuText(HotkeyAction.MarkInputStall, statuses));
    }

    private async void Quit()
    {
        if (_quitting)
        {
            return;
        }

        _quitting = true;

        // Whatever goes wrong while stopping, the program must still end: a failure here used to leave it running with no tray icon and no way to
        // quit again (review of Stage 1b, finding 11). The error is logged and the shutdown goes on.
        try
        {
            _trayTimer?.Stop();
            _hotkeys?.Dispose();
            if (_host is not null)
            {
                await _host.DisposeAsync();
            }
        }
        catch (Exception e)
        {
            LogError(e);
        }
        finally
        {
            try
            {
                _tray?.Dispose();
                _main?.AllowClose();
                _main?.Close();
                _showEvent?.Set(); // lets the listener thread see that we are quitting
            }
            catch (Exception e)
            {
                LogError(e);
            }

            Shutdown();
        }
    }

    private void StopCaptureNow()
    {
        if (_host is null)
        {
            return;
        }

        try
        {
            Task.Run(() => _host.Controller.StopAsync()).Wait(TimeSpan.FromSeconds(8));
        }
        catch (Exception e) when (e is AggregateException or InvalidOperationException)
        {
            LogError(e);
        }
    }

    private void WaitForShowRequests()
    {
        try
        {
            while (_showEvent is { } handle && handle.WaitOne())
            {
                if (_quitting)
                {
                    return;
                }

                Dispatcher.BeginInvoke(ShowMain);
            }
        }
        catch (ObjectDisposedException)
        {
            // the program is exiting and released the event while this thread waited on it: nothing left to do
        }
    }

    private static void AskTheRunningCopyToShow()
    {
        try
        {
            using var existing = EventWaitHandle.OpenExisting(ShowEventName);
            existing.Set();
        }
        catch (Exception e) when (e is WaitHandleCannotBeOpenedException or UnauthorizedAccessException)
        {
            // the first copy is still starting; it will show its window itself
        }
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogError(e.Exception);
        _main?.ShowNotice("Something went wrong (" + e.Exception.GetType().Name + "). Details were saved to a log on this PC; nothing was sent anywhere.", problem: true);
        e.Handled = true;
    }

    private static void LogError(Exception exception)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Branding.DataFolderName);
            Directory.CreateDirectory(directory);
            var logPath = Path.Combine(directory, "error.log");
            if (File.Exists(logPath) && new FileInfo(logPath).Length >= MaxErrorLogBytes)
            {
                File.Move(logPath, logPath + ".old", overwrite: true);
            }

            var redacted = RedactionEngine.RedactText(exception.ToString());
            File.AppendAllText(logPath, DateTime.UtcNow.ToString("O") + " " + redacted + Environment.NewLine);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // nowhere to write: the notice in the window is all there is
        }
    }
}
