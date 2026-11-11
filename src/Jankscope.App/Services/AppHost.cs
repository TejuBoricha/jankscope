using Jankscope.Core;
using Jankscope.Core.Hotkeys;
using Jankscope.Core.Reports;
using Jankscope.Frames.Incidents;
using Jankscope.Platform;
using Jankscope.Platform.Hotkeys;

namespace Jankscope.App.Services;

public enum MarkOutcomeKind
{
    /// <summary>A mark was recorded with a known place on the timeline.</summary>
    Recorded = 0,

    /// <summary>The same press as a moment ago; nothing was added.</summary>
    Debounced = 1,

    /// <summary>No capture is running, so there is nothing to mark.</summary>
    NotCapturing = 2,

    /// <summary>A mark was recorded, but no frame had arrived yet, so it has no place on the timeline.</summary>
    RecordedWithoutTime = 3,
}

public sealed record MarkOutcome(MarkOutcomeKind Kind, IncidentMark? Mark, string Message);

/// <summary>
/// The program's shared state and the flows that windows, the tray icon and the hotkeys all use: settings, the capture controller, the (slow) read-only
/// capability checks, the mark flow with its audible cue, and the state the tray icon shows. Holds no window, so it is tested without a screen.
/// </summary>
public sealed class AppHost : IAsyncDisposable
{
    private readonly SettingsStore _store;
    private readonly IAudioCue _audio;
    private readonly object _gate = new();
    private AppSettings _settings;

    public AppHost(
        string baseDirectory,
        SettingsStore store,
        ICaptureFactory? factory = null,
        IAudioCue? audio = null,
        Func<CapabilityMatrix>? matrixBuilder = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _settings = store.Load(out var problem);
        Problems = problem is null ? [] : [problem];
        _audio = audio ?? new AudioCue(() => Settings.SoundEnabled);

        string? pinProblem = null;
        var pin = factory is null ? PresentMonLocator.Find(baseDirectory, out pinProblem) : null;
        if (pinProblem is not null)
        {
            Problems = [.. Problems, pinProblem];
        }

        Factory = factory ?? (pin is null ? null : new PresentMonCaptureFactory(pin));

        var catalogs = PresentMonLocator.LoadCatalogs(baseDirectory, out var catalogProblem);
        if (catalogProblem is not null)
        {
            Problems = [.. Problems, catalogProblem];
        }

        // The checks open nothing of any game and change nothing, but they take seconds, so they run once in the background.
        MatrixTask = Task.Run(matrixBuilder ?? (() => new CapabilityMatrixBuilder(DefaultProbes.Create(catalogs)).Build()));
        Controller = new CaptureController(_settings, Factory ?? new UnavailableCaptureFactory(), () => MatrixTask.GetAwaiter().GetResult());
        Controller.Recorder.MarkChanged += (_, mark) => MarkChanged?.Invoke(mark);
    }

    public CaptureController Controller { get; }

    public ICaptureFactory? Factory { get; }

    public Task<CapabilityMatrix> MatrixTask { get; }

    /// <summary>Things that did not load (settings, catalogs, the capture tool); shown on the About page, never fatal.</summary>
    public IReadOnlyList<string> Problems { get; private set; }

    public bool CanCapture => Factory is not null;

    /// <summary>Builds the redacted Compatibility Report export preview (HC-16, Acceptance #19).</summary>
    public ExportPreview PrepareCompatibilityExport(bool redact, RedactionContext? context = null)
    {
        var matrix = MatrixTask.GetAwaiter().GetResult();
        var report = CompatibilityReportBuilder.Build(matrix, Problems, redact: redact);
        return ExportPipeline.Prepare(report, context ?? RedactionContext.ForThisMachine(), redact);
    }

    /// <summary>Plays a test audio cue when the user clicks the preview button in Settings.</summary>
    public void PreviewTone(MarkKind? kind)
    {
        if (kind is { } k)
        {
            _audio.Marked(k);
        }
        else
        {
            _audio.NotRecorded();
        }
    }

    /// <summary>Raised from whichever thread changed a mark (added, chosen anchor, note).</summary>
    public event Action<IncidentMark>? MarkChanged;

    public AppSettings Settings
    {
        get
        {
            lock (_gate)
            {
                return _settings;
            }
        }
    }

    /// <summary>Changes and saves the settings. A save that fails is reported through <paramref name="problem"/>; the change still applies for this run.</summary>
    public AppSettings Update(Func<AppSettings, AppSettings> change, out string? problem)
    {
        lock (_gate)
        {
            _settings = change(_settings).Normalized();
            _store.TrySave(_settings, out problem);
            return _settings;
        }
    }

    /// <summary>
    /// The flow behind every mark, from the hotkey, the tray menu or a button: tone first (so the person knows it was heard), then the work, which takes a
    /// few milliseconds. Never throws for a normal condition; the outcome says what happened.
    /// </summary>
    public async Task<MarkOutcome> MarkAsync(MarkKind kind)
    {
        if (!Controller.IsCapturing)
        {
            _audio.NotRecorded();
            return new MarkOutcome(MarkOutcomeKind.NotCapturing, null, "Nothing is being captured, so there is nothing to mark. Start watching a program first.");
        }

        var mark = await Task.Run(() => Controller.Mark(kind)).ConfigureAwait(false);
        if (mark is null)
        {
            return new MarkOutcome(MarkOutcomeKind.Debounced, null, "That was the same key press as a moment ago.");
        }

        if (mark.CaptureTime is null)
        {
            _audio.NotRecorded();
            return new MarkOutcome(MarkOutcomeKind.RecordedWithoutTime, mark, "Marked, but no frames had arrived yet, so this mark has no place on the timeline.");
        }

        _audio.Marked(kind);
        var what = kind == MarkKind.Stutter ? "stutter" : "input stall";
        return new MarkOutcome(MarkOutcomeKind.Recorded, mark, $"Marked a {what}. Open the investigator to choose where it happened.");
    }

    /// <summary>What the tray icon should show, from the capture and the hotkeys.</summary>
    public (TrayState State, string Tooltip) TrayStatus(IReadOnlyList<HotkeyStatus> hotkeys)
    {
        var status = Controller.Status();
        var hotkeyProblem = hotkeys.Any(h => !h.Active);
        var chords = string.Join(", ", hotkeys.Where(h => h.Active).Select(h => h.Chord.ToString()));
        var keys = hotkeys.Count == 0 ? string.Empty : hotkeyProblem && chords.Length == 0 ? " | hotkeys off: use this menu" : chords.Length > 0 ? " | " + chords : string.Empty;

        var state = status.State switch
        {
            CaptureState.Failed => TrayState.Attention,
            CaptureState.WaitingForFrames or CaptureState.Starting or CaptureState.Restarting => TrayState.Waiting,
            CaptureState.Capturing => hotkeyProblem ? TrayState.Attention : TrayState.Capturing,
            _ => hotkeyProblem ? TrayState.Attention : TrayState.Idle,
        };
        return (state, "Jankscope: " + status.Headline.TrimEnd('.') + keys);
    }

    /// <summary>The tray menu text for one action: its chord when it works, or a plain "not registered" when it does not.</summary>
    public static string MenuText(HotkeyAction action, IReadOnlyList<HotkeyStatus> statuses)
    {
        var label = action == HotkeyAction.MarkStutter ? "Mark stutter" : "Mark input stall";
        var status = statuses.FirstOrDefault(s => s.Action == action);
        return status is null ? label : status.Active ? $"{label}   ({status.Chord})" : $"{label}   (hotkey not registered)";
    }

    public async ValueTask DisposeAsync()
    {
        await Controller.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>Stands in when the capture tool cannot be found: every attempt says so, nothing starts.</summary>
internal sealed class UnavailableCaptureFactory : ICaptureFactory
{
    public Jankscope.Frames.Capture.IFrameSource CreateCapture(string programName) =>
        throw new InvalidOperationException("The frame-capture tool is not available (see About and privacy).");

    public Jankscope.Frames.Capture.IFrameSource CreateScan(int seconds) =>
        throw new InvalidOperationException("The frame-capture tool is not available (see About and privacy).");
}
