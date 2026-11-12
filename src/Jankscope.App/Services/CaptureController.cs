using Jankscope.Core;
using Jankscope.Core.Reports;
using Jankscope.Frames;
using Jankscope.Frames.Capture;
using Jankscope.Frames.Incidents;
using Jankscope.Frames.PresentMon;
using Jankscope.Frames.Ring;

namespace Jankscope.App.Services;

/// <summary>Creates the frame sources: the real PresentMon in production, a script in tests.</summary>
public interface ICaptureFactory
{
    /// <summary>A capture of one program, until stopped.</summary>
    IFrameSource CreateCapture(string programName);

    /// <summary>A timed look at every program that presents frames.</summary>
    IFrameSource CreateScan(int seconds);
}

/// <summary>
/// The production factory. PresentMon is a user-mode event-trace consumer, one of the collectors HC-1 never suppresses; the check still asks the policy
/// with the most cautious input (state unknown) so that a future change of that policy takes effect here without touching this file.
/// </summary>
public sealed class PresentMonCaptureFactory(PinnedExecutable pin) : ICaptureFactory
{
    private static SuppressionDecision Check() =>
        AntiCheatSafety.Decide(CollectorClass.UnelevatedUserModeEtw, AntiCheatSafetyState.Unknown, userOverridePerGame: false);

    /// <summary>
    /// The parser a live capture of <paramref name="programName"/> uses. PresentMon cannot say that a program is the synthetic harness, so the label
    /// comes from the name (<see cref="CaptureProvenance.ForProgram"/>): a capture of the harness is Simulated, anything else Measured (HC-9).
    /// </summary>
    public static Func<IFrameLogParser> ParserFor(string programName)
    {
        var provenance = CaptureProvenance.ForProgram(programName);
        return () => new PresentMonCsvParser(provenance);
    }

    public IFrameSource CreateCapture(string programName) => new RunnerFrameSource(PresentMonRunner.CreateDefault(
        new PresentMonRunnerOptions
        {
            Executable = pin,
            SessionName = PresentMonArguments.NewSessionName(Environment.ProcessId, "live"),
            ProcessNames = [programName],
            // A program in a menu, minimised or loading presents nothing, and PresentMon prints nothing then: a silence watchdog would mistake that
            // for a stuck tool and burn the restart budget. The interface shows "waiting for frames" instead.
            WatchdogNoOutputTimeout = Timeout.InfiniteTimeSpan,
            AntiCheatSuppressionCheck = Check,
        },
        ParserFor(programName)));

    public IFrameSource CreateScan(int seconds) => new RunnerFrameSource(PresentMonRunner.CreateDefault(
        new PresentMonRunnerOptions
        {
            Executable = pin,
            SessionName = PresentMonArguments.NewSessionName(Environment.ProcessId, "scan"),
            TimedSeconds = seconds,
            WatchdogNoOutputTimeout = Timeout.InfiniteTimeSpan,
            MaxRestarts = 0,
            AntiCheatSuppressionCheck = Check,
        },
        () => new PresentMonCsvParser(DataProvenance.Measured)));
}

public enum CaptureState
{
    /// <summary>Nothing is being captured.</summary>
    Idle = 0,

    Starting = 1,

    /// <summary>Capturing and frames are arriving.</summary>
    Capturing = 2,

    /// <summary>Capturing, but the program presents nothing right now.</summary>
    WaitingForFrames = 3,

    /// <summary>The capture tool ended abnormally and is being started again.</summary>
    Restarting = 4,

    Stopping = 5,

    /// <summary>The capture ended and could not continue: see <see cref="CaptureStatus.Detail"/>.</summary>
    Failed = 6,
}

public sealed record CaptureStatus(
    CaptureState State,
    string Headline,
    string? Detail,
    string? Program,
    long FramesReceived,
    long DroppedFrames,
    long IgnoredFrames,
    long OutOfOrderFrames,
    long BackwardJumps,
    int Restarts,
    IReadOnlyList<string> Problems,
    IReadOnlyList<string> Notes)
{
    public bool IsActive => State is CaptureState.Starting or CaptureState.Capturing or CaptureState.WaitingForFrames or CaptureState.Restarting;
}

public sealed record StartResult(bool Started, string? Message);

/// <summary>
/// What the interface talks to: one capture at a time into one history, one recorder that makes the marks, and the export. Holds no window and no
/// dispatcher, so the same code is tested without a screen. Thread-safe where it says so.
/// </summary>
public sealed class CaptureController : IAsyncDisposable
{
    /// <summary>How long a capture may be silent before the interface says it is waiting for frames.</summary>
    public static readonly TimeSpan QuietFor = TimeSpan.FromSeconds(2);

    private readonly ICaptureFactory _factory;
    private readonly Func<CapabilityMatrix> _matrix;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private CaptureSession? _session;
    private string? _program;
    private string? _lastError;

    public CaptureController(AppSettings settings, ICaptureFactory factory, Func<CapabilityMatrix> matrix, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(matrix);
        _factory = factory;
        _matrix = matrix;
        _time = timeProvider ?? TimeProvider.System;

        var normalized = settings.Normalized();
        var options = new IncidentRecorderOptions(normalized.PreRollSeconds, normalized.PostRollSeconds);
        History = new RingBuffer<FrameSample>(options.RecommendedHistoryCapacity());
        Clock = new CaptureClockMap(_time.TimestampFrequency);
        Recorder = new IncidentRecorder(History, Clock, _time, options);
    }

    public RingBuffer<FrameSample> History { get; }

    public CaptureClockMap Clock { get; }

    public IncidentRecorder Recorder { get; }

    public string? Program
    {
        get
        {
            lock (_gate)
            {
                return _program;
            }
        }
    }

    public CaptureSession? Session
    {
        get
        {
            lock (_gate)
            {
                return _session;
            }
        }
    }

    public bool IsCapturing => Status().IsActive;

    /// <summary>A snapshot for the interface. Cheap; safe to call from a timer.</summary>
    public CaptureStatus Status()
    {
        CaptureSession? session;
        string? program;
        string? error;
        lock (_gate)
        {
            session = _session;
            program = _program;
            error = _lastError;
        }

        if (session is null)
        {
            return error is null
                ? new CaptureStatus(CaptureState.Idle, "Not capturing", null, null, 0, 0, 0, 0, 0, 0, [], [])
                : new CaptureStatus(CaptureState.Failed, "Could not capture", error, program, 0, 0, 0, 0, 0, 0, [], []);
        }

        var health = session.Health;
        var name = program ?? "the program";
        var (state, headline, detail) = health.State switch
        {
            RunnerState.NotStarted or RunnerState.Starting => (CaptureState.Starting, $"Starting the capture of {name}...", (string?)null),
            RunnerState.Running when session.WaitingForFrames(QuietFor) =>
                (CaptureState.WaitingForFrames, $"Waiting for frames from {name}", "It is not presenting frames right now (a menu, a minimised window or a loading screen), or its name is not exactly as typed."),
            RunnerState.Running => (CaptureState.Capturing, $"Capturing {name}", null),
            RunnerState.Stalled or RunnerState.BackingOff => (CaptureState.Restarting, "Restarting the capture tool", health.LastError),
            RunnerState.Stopping => (CaptureState.Stopping, "Stopping...", null),
            RunnerState.Failed => (CaptureState.Failed, "The capture stopped", health.LastError ?? "The capture tool ended and could not be restarted."),
            _ => (CaptureState.Idle, "Not capturing", null),
        };

        return new CaptureStatus(
            state,
            headline,
            detail,
            program,
            session.FramesReceived,
            health.DroppedFrames,
            session.IgnoredFrames,
            session.OutOfOrderFrames,
            session.BackwardJumps,
            health.Restarts,
            health.Problems,
            health.Notes);
    }

    /// <summary>A short capture of every program that presents frames, for the person to choose from. Nothing is saved.</summary>
    public async Task<IReadOnlyList<PresentingProgram>> ScanAsync(int seconds, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(seconds, 1);
        await using var source = _factory.CreateScan(seconds);
        return await PresentingProgramScanner.ScanAsync(source, seconds, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Starts capturing <paramref name="programName"/> (a plain executable file name). Refuses, with a reason, when one is already running or the name is not valid.</summary>
    public StartResult Start(string programName)
    {
        var name = programName?.Trim() ?? string.Empty;
        try
        {
            PresentMonArguments.ValidateProcessName(name);
        }
        catch (ArgumentException e)
        {
            return new StartResult(false, e.Message.Split('(')[0].Trim());
        }

        CaptureSession? previous;
        lock (_gate)
        {
            if (_session is not null)
            {
                var status = Status();
                if (status.IsActive || status.State == CaptureState.Stopping || !_session.Completion.IsCompleted)
                {
                    return new StartResult(false, $"Already capturing {_program}. Stop that first.");
                }
            }

            _lastError = null;
            previous = _session;
        }

        CaptureSession? session = null;
        try
        {
            Clock.Reset();
            session = new CaptureSession(_factory.CreateCapture(name), History, Clock, _time);
            session.Start();
        }
        catch (InvalidOperationException e)
        {
            lock (_gate)
            {
                _lastError = e.Message;
            }

            if (session is not null)
            {
                _ = session.DisposeAsync().AsTask();
            }

            return new StartResult(false, e.Message);
        }

        lock (_gate)
        {
            _session = session;
            _program = name;
        }

        if (previous is not null)
        {
            _ = previous.DisposeAsync().AsTask(); // the earlier, finished capture: nothing of it is left running, this only releases it
        }

        return new StartResult(true, null);
    }

    /// <summary>Starts a built-in simulated stutter session (<see cref="DataProvenance.Simulated"/>, HC-9) so the user can explore the live oscilloscope and Investigator immediately.</summary>
    public StartResult StartSimulation(string programName = "simulated-workbench.exe")
    {
        CaptureSession? previous;
        lock (_gate)
        {
            if (_session is not null)
            {
                var status = Status();
                if (status.IsActive || status.State == CaptureState.Stopping || !_session.Completion.IsCompleted)
                {
                    return new StartResult(false, $"Already capturing {_program}. Stop that first.");
                }
            }

            _lastError = null;
            previous = _session;
        }

        Clock.Reset();
        var session = new CaptureSession(new SimulatedFrameSource(programName, time: _time), History, Clock, _time);
        session.Start();

        lock (_gate)
        {
            _session = session;
            _program = programName;
        }

        if (previous is not null)
        {
            _ = previous.DisposeAsync().AsTask();
        }

        return new StartResult(true, null);
    }

    /// <summary>Builds a live <see cref="IncidentBundle"/> diagnosis for <paramref name="mark"/> so the Investigator workspace can display KPI metrics and evidence-graded hypotheses directly.</summary>
    public IncidentBundle? TryBuildBundle(IncidentMark mark)
    {
        ArgumentNullException.ThrowIfNull(mark);
        try
        {
            var window = Recorder.CollectWindow(mark);
            var dropped = Session?.Health.DroppedFrames ?? 0;
            return IncidentBundleBuilder.Build(mark, window, _matrix(), dropped, redactSensitiveRows: false);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Ends the capture gracefully; safe to call when nothing is running.</summary>
    public async Task StopAsync()
    {
        CaptureSession? session;
        lock (_gate)
        {
            session = _session;
        }

        if (session is not null)
        {
            await session.StopAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Makes a mark for a key press (or a tray click) now. Null when it is the same press as one a moment ago. A mark is recorded even before the
    /// capture clock is known, with an unknown time; <see cref="IsCapturing"/> says whether anything is running at all.
    /// </summary>
    public IncidentMark? Mark(MarkKind kind, long? pressedTicks = null) => Recorder.Mark(kind, pressedTicks);

    /// <summary>
    /// Builds the redacted export of a mark and its preview. Runs the whole pipeline (bundle, redaction, guard) and writes nothing. Throws
    /// <see cref="ExportBlockedException"/> when anything personal survives, and <see cref="InvalidOperationException"/> when the window holds no frame.
    /// Takes time (a copy of the history and a few megabytes of text): call it from a worker.
    /// </summary>
    public ExportPreview PrepareExport(IncidentMark mark, bool redact, RedactionContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(mark);
        var window = Recorder.CollectWindow(mark);
        var session = Session;
        var dropped = session?.Health.DroppedFrames ?? 0;
        var bundle = IncidentBundleBuilder.Build(
            mark, window, _matrix(), dropped, redactSensitiveRows: redact, outOfOrderFrames: session?.OutOfOrderFrames ?? 0, backwardJumps: session?.BackwardJumps ?? 0);
        return ExportPipeline.Prepare(bundle, context ?? RedactionContext.ForThisMachine(), redact);
    }

    public async ValueTask DisposeAsync()
    {
        CaptureSession? session;
        lock (_gate)
        {
            session = _session;
            _session = null;
        }

        if (session is not null)
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }
}
