using System.Globalization;
using System.Text;
using System.Threading.Channels;
using Jankscope.Core;

namespace Jankscope.Frames.PresentMon;

/// <summary>
/// Runs the pinned PresentMon console executable and turns its standard output into frames.
/// <list type="bullet">
/// <item>The executable is verified (size and SHA-256, on a handle held across the launch) before EVERY launch, restarts included.</item>
/// <item>The only processes ever started are PresentMon captures and the short PresentMon invocation that ends one of our own ETW sessions.</item>
/// <item>Frames go to a bounded queue that drops the OLDEST frame when full and counts it: the pipe reader never blocks, because a blocked reader
///   would stall PresentMon itself while a game is running.</item>
/// <item>A watchdog ends a capture that prints nothing for too long; abnormal ends restart with exponential back-off, a bounded number of times.</item>
/// <item>Stop is graceful first (the capture's own session is ended, which makes PresentMon leave), then the process is ended after a grace period
///   (the process itself, never a tree: see <see cref="IChildProcess.TryKill"/>). A killed PresentMon leaves its ETW session behind (observed on 2.6.0),
///   so after any kill the session is removed again by name.</item>
/// <item>Every wait uses the injected <see cref="TimeProvider"/>, so tests drive time instead of sleeping.</item>
/// </list>
/// Dispose (or <see cref="StopAsync"/>) before dropping the runner: a hard crash of the owning process cannot be handled here and would
/// leave the child and its session until the orphan cleaner of a later start removes them.
/// </summary>
public sealed class PresentMonRunner : IAsyncDisposable
{
    private const int MaxProblemsKept = 20;
    private const int MaxNotesKept = 10;
    private const int MaxNoteLength = 400;

    /// <summary>Exit code PresentMon 2.6.0 returns, with "no existing sessions found" on stderr, when asked to end a session that is not there.</summary>
    private const int NoSuchSessionExitCode = 7;

    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan KillWait = TimeSpan.FromSeconds(5);

    private readonly PresentMonRunnerOptions _options;
    private readonly IProcessLauncher _launcher;
    private readonly Func<IFrameLogParser> _parserFactory;
    private readonly TimeProvider _time;
    private readonly Channel<CapturedFrame> _channel;
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _gate = new();
    private readonly List<string> _problems = [];
    private readonly List<string> _notes = [];

    private RunnerState _state = RunnerState.NotStarted;
    private int _lifecycle; // 0 = new, 1 = started, 2 = stopped before it ever started
    private int _restarts;
    private int _stalls;
    private string? _lastError;
    private string? _sessionName;
    private string? _uncleanedSession;
    private long _linesRead;
    private long _rowsParsed;
    private long _problemCount;
    private long _dropped;

    public PresentMonRunner(
        PresentMonRunnerOptions options,
        IProcessLauncher launcher,
        Func<IFrameLogParser> parserFactory,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(parserFactory);
        ArgumentNullException.ThrowIfNull(options.Executable);

        if (options.FrameQueueCapacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "The frame queue needs room for at least one frame.");
        }

        if (options.MaxRestarts < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxRestarts cannot be negative.");
        }

        if ((options.WatchdogNoOutputTimeout <= TimeSpan.Zero && options.WatchdogNoOutputTimeout != Timeout.InfiniteTimeSpan)
            || options.StopGracePeriod < TimeSpan.Zero || options.CleanupTimeout <= TimeSpan.Zero || options.StableRunDuration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "A timeout is zero or negative.");
        }

        _ = RestartBackoff.DelayFor(1, options.RestartBackoffInitial, options.RestartBackoffMax); // validates both values
        PresentMonArguments.ValidateSessionName(options.SessionName);
        // Fail now, not at the first launch, for a bad session name, process name or timed value.
        _ = PresentMonArguments.Build(SessionNameFor(options.SessionName, int.MaxValue), options.ProcessNames, options.TerminateOnProcessExit, options.TimedSeconds);

        _options = options;
        _launcher = launcher;
        _parserFactory = parserFactory;
        _time = timeProvider ?? TimeProvider.System;
        _channel = Channel.CreateBounded<CapturedFrame>(
            new BoundedChannelOptions(options.FrameQueueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleWriter = true,
                SingleReader = false,
                AllowSynchronousContinuations = false,
            },
            _ => Interlocked.Increment(ref _dropped));
    }

    /// <summary>
    /// The production runner: it starts the pinned executable through the reviewed launcher. The launcher itself is internal to this assembly,
    /// so nothing outside it can use it to start another program (review 1a, finding 2).
    /// </summary>
    public static PresentMonRunner CreateDefault(PresentMonRunnerOptions options, Func<IFrameLogParser> parserFactory, TimeProvider? timeProvider = null) =>
        new(options, new PresentMonProcessLauncher(), parserFactory, timeProvider);

    /// <summary>Parsed frames. The channel completes when the runner has stopped for good; check <see cref="Health"/> to see why.</summary>
    public ChannelReader<CapturedFrame> Samples => _channel.Reader;

    /// <summary>Completes when the runner has stopped for good and nothing it started is left running. Never faults.</summary>
    public Task Completion => _completion.Task;

    public RunnerHealth Health
    {
        get
        {
            lock (_gate)
            {
                return new RunnerHealth(
                    _state,
                    _restarts,
                    _lastError,
                    Interlocked.Read(ref _linesRead),
                    Interlocked.Read(ref _rowsParsed),
                    [.. _problems],
                    Interlocked.Read(ref _problemCount),
                    Interlocked.Read(ref _dropped),
                    [.. _notes],
                    _sessionName,
                    _stalls,
                    _uncleanedSession);
            }
        }
    }

    /// <summary>Starts supervising. Returns at once; frames arrive on <see cref="Samples"/>.</summary>
    public void Start()
    {
        if (Interlocked.CompareExchange(ref _lifecycle, 1, 0) != 0)
        {
            throw new InvalidOperationException("The runner was already started or stopped.");
        }

        if (_options.AntiCheatSuppressionCheck?.Invoke() is { Suppressed: true } suppression)
        {
            var reason = suppression.Reason ?? AntiCheatSafety.SuppressedReason;
            AddProblem($"Capture suppressed by anti-cheat safety: {reason}");
            lock (_gate)
            {
                _lastError = reason;
                _state = RunnerState.Failed;
            }

            _channel.Writer.Complete();
            _completion.TrySetResult();
            throw new InvalidOperationException($"Capture suppressed by anti-cheat safety: {reason}");
        }

        SetState(RunnerState.Starting);
        _ = Task.Run(SuperviseAsync);
    }

    /// <summary>
    /// The one-click stop: ends the capture gracefully, then the process after <see cref="PresentMonRunnerOptions.StopGracePeriod"/>,
    /// and removes the ETW session. Safe to call repeatedly and from any state. Completes when nothing is left running.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_completion.Task.IsCompleted)
        {
            return;
        }

        await _stop.CancelAsync().ConfigureAwait(false);
        if (Interlocked.CompareExchange(ref _lifecycle, 2, 0) == 0)
        {
            Finish(RunnerState.Stopped, null);
            _channel.Writer.TryComplete();
            _completion.TrySetResult();
            return;
        }

        await _completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _stop.Dispose();
    }

    // ---- supervision ----

    private async Task SuperviseAsync()
    {
        try
        {
            await SuperviseCoreAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            AddProblem("internal error (" + e.GetType().Name + ")");
            Finish(RunnerState.Failed, "internal error (" + e.GetType().Name + ")");
        }
        finally
        {
            _channel.Writer.TryComplete();
            _completion.TrySetResult();
        }
    }

    private async Task SuperviseCoreAsync()
    {
        var consecutiveFailures = 0;
        for (var run = 1; ; run++)
        {
            var result = await RunOnceAsync(run).ConfigureAwait(false);
            switch (result.Kind)
            {
                case RunKind.Completed:
                    Finish(RunnerState.Completed, null);
                    return;
                case RunKind.Stopped:
                    Finish(RunnerState.Stopped, null);
                    return;
                case RunKind.Fatal:
                    Finish(RunnerState.Failed, result.Reason);
                    return;
            }

            if (result.Stable)
            {
                consecutiveFailures = 0;
            }

            consecutiveFailures++;
            if (consecutiveFailures > _options.MaxRestarts)
            {
                Finish(RunnerState.Failed, _options.MaxRestarts == 0
                    ? result.Reason
                    : $"gave up after {_options.MaxRestarts.ToString(CultureInfo.InvariantCulture)} restart(s) in a row: {result.Reason}");
                return;
            }

            lock (_gate)
            {
                _restarts++;
                _lastError = result.Reason;
            }

            SetState(RunnerState.BackingOff);
            var delay = RestartBackoff.DelayFor(consecutiveFailures, _options.RestartBackoffInitial, _options.RestartBackoffMax);
            try
            {
                await Task.Delay(delay, _time, _stop.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Finish(RunnerState.Stopped, null);
                return;
            }
        }
    }

    private async Task<RunResult> RunOnceAsync(int run)
    {
        if (_stop.IsCancellationRequested)
        {
            return RunResult.Stopped();
        }

        var session = SessionNameFor(_options.SessionName, run);
        lock (_gate)
        {
            _sessionName = session;
        }

        SetState(RunnerState.Starting);
        var arguments = PresentMonArguments.Build(session, _options.ProcessNames, _options.TerminateOnProcessExit, _options.TimedSeconds);

        IChildProcess child;
        using (var check = FileHashVerifier.Open(_options.Executable))
        {
            if (!check.IsVerified)
            {
                AddProblem("Refused to start PresentMon: " + check.Reason);
                return check.IsPermanent ? RunResult.Fatal(check.Reason) : RunResult.Failure(check.Reason, stable: false);
            }

            try
            {
                child = _launcher.Start(new ProcessLaunchRequest(check.Executable!, arguments));
            }
            catch (ProcessLaunchException e)
            {
                AddProblem(e.Message);
                return RunResult.Failure(e.Message, stable: false);
            }
        }

        return await SuperviseChildAsync(child, run, session).ConfigureAwait(false);
    }

    /// <summary>
    /// Watches one running child until it ends, stalls, fails fatally or is stopped. Everything after the launch is inside the
    /// <c>try</c>/<c>finally</c>, so a failure while setting the run up (a throwing parser factory, say) still ends the child (review 1a, finding 11).
    /// </summary>
    private async Task<RunResult> SuperviseChildAsync(IChildProcess child, int run, string session)
    {
        using var pumps = new CancellationTokenSource();
        Task<int>? exit = null;
        var output = Task.CompletedTask;
        var errors = Task.CompletedTask;
        var setUp = false;
        try
        {
            var context = new RunContext(run, _time.GetTimestamp());
            var stopSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var stopRegistration = _stop.Token.Register(static state => ((TaskCompletionSource)state!).TrySetResult(), stopSignal);

            var exitTask = child.WaitForExitAsync(CancellationToken.None);
            exit = exitTask;
            output = PumpOutputAsync(child, _parserFactory(), context, pumps.Token);
            errors = PumpErrorsAsync(child, context, pumps.Token);
            var watchdog = WatchdogAsync(context, pumps.Token);
            SetState(RunnerState.Running);
            setUp = true;

            var first = await Task.WhenAny(exitTask, watchdog, context.Fatal.Task, stopSignal.Task).ConfigureAwait(false);

            if (first == exitTask)
            {
                var code = await exitTask.ConfigureAwait(false);
                await WaitAsync(Task.WhenAll(output, errors), DrainTimeout).ConfigureAwait(false);
                return await EndedOnItsOwnAsync(code, context, session).ConfigureAwait(false);
            }

            if (first == context.Fatal.Task)
            {
                var reason = await context.Fatal.Task.ConfigureAwait(false);
                AddProblem(reason);
                await KillAsync(child, exitTask).ConfigureAwait(false);
                await EnsureSessionGoneAsync(session).ConfigureAwait(false);
                return RunResult.Fatal(reason);
            }

            if (first == watchdog && await watchdog.ConfigureAwait(false))
            {
                SetState(RunnerState.Stalled);
                var silent = _options.WatchdogNoOutputTimeout.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
                var reason = $"stalled: no output from PresentMon for {silent} s (it is stuck, or the captured program is not presenting frames)";
                lock (_gate)
                {
                    _stalls++;
                }

                AddProblem(reason);
                await KillAsync(child, exitTask).ConfigureAwait(false);
                await EnsureSessionGoneAsync(session).ConfigureAwait(false);
                return RunResult.Failure(reason, IsStable(context));
            }

            return await StopRunAsync(child, exitTask, session).ConfigureAwait(false);
        }
        finally
        {
            await pumps.CancelAsync().ConfigureAwait(false);
            if (exit is null || !exit.IsCompleted)
            {
                // Only reachable when something above threw: a child must never outlive the run that started it.
                child.TryKill(out _);
                if (exit is not null)
                {
                    await WaitAsync(exit, KillWait).ConfigureAwait(false);
                }

                if (!setUp)
                {
                    // The run never got going, so no stop path has removed the capture's ETW session: a killed PresentMon leaves it behind.
                    await EnsureSessionGoneAsync(session).ConfigureAwait(false);
                }
            }

            await WaitAsync(Task.WhenAll(output, errors), DrainTimeout).ConfigureAwait(false);
            child.Dispose();
        }
    }

    private async Task<RunResult> EndedOnItsOwnAsync(int exitCode, RunContext context, string session)
    {
        var expectsToEnd = _options.TimedSeconds is not null || _options.TerminateOnProcessExit;
        if (_stop.IsCancellationRequested)
        {
            if (exitCode != 0)
            {
                await EnsureSessionGoneAsync(session).ConfigureAwait(false);
            }

            return RunResult.Stopped();
        }

        if (exitCode == 0 && expectsToEnd)
        {
            return RunResult.Completed();
        }

        var reason = exitCode == 0
            ? "PresentMon exited unexpectedly (exit code 0) although it was not asked to stop"
            : $"PresentMon exited with code {exitCode.ToString(CultureInfo.InvariantCulture)}";
        if (context.LastStderrError is { } stderr)
        {
            reason += ": " + stderr;
        }

        AddProblem(reason);
        await EnsureSessionGoneAsync(session).ConfigureAwait(false);
        return RunResult.Failure(reason, IsStable(context));
    }

    private async Task<RunResult> StopRunAsync(IChildProcess child, Task<int> exit, string session)
    {
        SetState(RunnerState.Stopping);

        // Graceful: end our own ETW session by name. PresentMon then flushes and leaves by itself (observed on 2.6.0).
        var asked = await TerminateSessionAsync(session).ConfigureAwait(false);
        if (asked && await WaitAsync(exit, _options.StopGracePeriod).ConfigureAwait(false))
        {
            return RunResult.Stopped();
        }

        AddProblem(asked
            ? "PresentMon did not leave within the grace period after its session was ended; the process was ended"
            : "The capture could not be asked to stop; the process was ended");
        await KillAsync(child, exit).ConfigureAwait(false);
        await EnsureSessionGoneAsync(session).ConfigureAwait(false);
        return RunResult.Stopped();
    }

    private async Task KillAsync(IChildProcess child, Task<int> exit)
    {
        if (!child.TryKill(out var failure))
        {
            AddProblem("Could not end the PresentMon process" + (failure is null ? string.Empty : " (" + failure + ")"));
        }

        if (!await WaitAsync(exit, KillWait).ConfigureAwait(false))
        {
            AddProblem("PresentMon was still running after it was killed");
        }
    }

    private async Task EnsureSessionGoneAsync(string session)
    {
        if (!await TerminateSessionAsync(session).ConfigureAwait(false))
        {
            lock (_gate)
            {
                _uncleanedSession = session;
            }

            AddProblem($"The ETW session {session} could not be confirmed ended; a later start must remove it (only names with our prefix are ever touched)");
        }
        else
        {
            lock (_gate)
            {
                if (_uncleanedSession == session)
                {
                    _uncleanedSession = null;
                }
            }
        }
    }

    /// <summary>
    /// Starts the pinned executable with <c>--terminate_existing_session --session_name &lt;ours&gt;</c>. True when the session was ended or
    /// was not there (PresentMon 2.6.0 answers exit code 7 and "no existing sessions found"); false when that could not be established.
    /// </summary>
    private async Task<bool> TerminateSessionAsync(string session)
    {
        IChildProcess helper;
        try
        {
            var arguments = PresentMonArguments.BuildTerminateExistingSession(session);
            using var check = FileHashVerifier.Open(_options.Executable);
            if (!check.IsVerified)
            {
                AddProblem("Refused to start PresentMon to end its session: " + check.Reason);
                return false;
            }

            helper = _launcher.Start(new ProcessLaunchRequest(check.Executable!, arguments));
        }
        catch (ProcessLaunchException e)
        {
            AddProblem(e.Message);
            return false;
        }

        using (helper)
        {
            using var drainCts = new CancellationTokenSource();
            var noSuchSession = false;
            var exit = helper.WaitForExitAsync(CancellationToken.None);
            var drainOutput = DrainAsync(helper.ReadOutputLineAsync, static _ => { }, drainCts.Token);
            var drainErrors = DrainAsync(helper.ReadErrorLineAsync, text => noSuchSession |= text.Contains("no existing sessions found", StringComparison.OrdinalIgnoreCase), drainCts.Token);
            try
            {
                if (!await WaitAsync(exit, _options.CleanupTimeout).ConfigureAwait(false))
                {
                    helper.TryKill(out _);
                    AddProblem("Ending the ETW session took longer than the cleanup timeout");
                    return false;
                }

                var code = await exit.ConfigureAwait(false);
                await WaitAsync(Task.WhenAll(drainOutput, drainErrors), DrainTimeout).ConfigureAwait(false);
                return code == 0 || (code == NoSuchSessionExitCode && noSuchSession);
            }
            finally
            {
                await drainCts.CancelAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task DrainAsync(Func<CancellationToken, ValueTask<ChildLine?>> read, Action<string> onLine, CancellationToken cancellationToken)
    {
        try
        {
            while (await read(cancellationToken).ConfigureAwait(false) is { } line)
            {
                if (!line.WasTruncated)
                {
                    onLine(line.Text);
                }
            }
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException or InvalidOperationException)
        {
            // the stream ended abnormally or the helper was disposed: the exit code is what counts
        }
    }

    // ---- pumps ----

    private async Task PumpOutputAsync(IChildProcess child, IFrameLogParser parser, RunContext context, CancellationToken cancellationToken)
    {
        var headerSeen = false;
        try
        {
            while (await child.ReadOutputLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                Interlocked.Exchange(ref context.LastOutputTimestamp, _time.GetTimestamp());
                var lineNumber = Interlocked.Increment(ref _linesRead);
                if (line.WasTruncated)
                {
                    AddProblem($"line {lineNumber.ToString(CultureInfo.InvariantCulture)}: longer than {BoundedLineReader.DefaultMaxLineChars.ToString(CultureInfo.InvariantCulture)} characters, dropped");
                    continue;
                }

                if (line.Unterminated)
                {
                    // The pipe ended in the middle of a line (the child was ended): the text may be cut off anywhere, so it is never parsed.
                    AddProblem($"line {lineNumber.ToString(CultureInfo.InvariantCulture)}: the output ended before the line break, dropped");
                    continue;
                }

                if (line.Text.Length == 0)
                {
                    continue;
                }

                if (!headerSeen)
                {
                    headerSeen = true;
                    LogHeaderResult header;
                    try
                    {
                        header = parser.ReadHeader(line.Text);
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        context.Fatal.TrySetResult("the log parser failed on the header (" + e.GetType().Name + ")");
                        return;
                    }

                    if (!header.Supported)
                    {
                        context.Fatal.TrySetResult("unsupported PresentMon log: " + (header.UnsupportedReason ?? "no reason given"));
                        return;
                    }

                    continue;
                }

                FrameSample sample;
                string? problem;
                bool parsed;
                try
                {
                    parsed = parser.TryParseRow(line.Text, out sample, out problem);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    sample = default;
                    parsed = false;
                    problem = "the log parser threw " + e.GetType().Name;
                }

                if (parsed)
                {
                    Interlocked.Increment(ref _rowsParsed);
                    Interlocked.Increment(ref context.RowsThisRun);
                    _channel.Writer.TryWrite(new CapturedFrame(context.Run, sample));
                }
                else
                {
                    AddProblem($"line {lineNumber.ToString(CultureInfo.InvariantCulture)}: {problem ?? "malformed row"}");
                }
            }
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The child was ended or disposed under the reader; the exit path decides what that means.
        }
    }

    private async Task PumpErrorsAsync(IChildProcess child, RunContext context, CancellationToken cancellationToken)
    {
        // PresentMon writes "warning: ..." blocks (continuation lines are indented) and "error: ..." lines to stderr. Warnings are notes, never errors.
        var note = new StringBuilder();
        try
        {
            while (await child.ReadErrorLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                if (line.WasTruncated || line.Text.Length == 0)
                {
                    continue;
                }

                var text = line.Text;
                if (char.IsWhiteSpace(text[0]) && note.Length > 0)
                {
                    note.Append(' ').Append(text.Trim());
                    continue;
                }

                FlushNote(note);
                if (text.StartsWith("warning:", StringComparison.OrdinalIgnoreCase))
                {
                    note.Append(text.Trim());
                }
                else if (text.StartsWith("error:", StringComparison.OrdinalIgnoreCase))
                {
                    context.LastStderrError = Truncate(text.Trim(), MaxNoteLength);
                }
            }
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException or InvalidOperationException)
        {
            // see PumpOutputAsync
        }
        finally
        {
            FlushNote(note);
        }
    }

    private void FlushNote(StringBuilder note)
    {
        if (note.Length == 0)
        {
            return;
        }

        var text = Truncate(note.ToString(), MaxNoteLength);
        note.Clear();
        lock (_gate)
        {
            if (_notes.Count < MaxNotesKept && !_notes.Contains(text))
            {
                _notes.Add(text);
            }
        }
    }

    private async Task<bool> WatchdogAsync(RunContext context, CancellationToken cancellationToken)
    {
        var timeout = _options.WatchdogNoOutputTimeout;
        try
        {
            while (true)
            {
                if (timeout == Timeout.InfiniteTimeSpan)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                }

                var silent = _time.GetElapsedTime(Interlocked.Read(ref context.LastOutputTimestamp));
                var remaining = timeout - silent;
                if (remaining <= TimeSpan.Zero)
                {
                    return true;
                }

                await Task.Delay(remaining, _time, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    // ---- helpers ----

    private bool IsStable(RunContext context) =>
        Interlocked.Read(ref context.RowsThisRun) > 0 && _time.GetElapsedTime(context.StartTimestamp) >= _options.StableRunDuration;

    /// <summary>Waits for <paramref name="task"/> for at most <paramref name="timeout"/> on the injected clock; true when it finished.</summary>
    private async Task<bool> WaitAsync(Task task, TimeSpan timeout)
    {
        if (task.IsCompleted)
        {
            return true;
        }

        using var cts = new CancellationTokenSource();
        var delay = Task.Delay(timeout, _time, cts.Token);
        var finished = await Task.WhenAny(task, delay).ConfigureAwait(false) == task;
        await cts.CancelAsync().ConfigureAwait(false);
        return finished;
    }

    private static string SessionNameFor(string baseName, int run) => baseName + "-" + run.ToString(CultureInfo.InvariantCulture);

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];

    private void SetState(RunnerState state)
    {
        lock (_gate)
        {
            if (!IsTerminal(_state))
            {
                _state = state;
            }
        }
    }

    private void Finish(RunnerState state, string? error)
    {
        lock (_gate)
        {
            if (error is not null)
            {
                _lastError = error;
            }

            if (!IsTerminal(_state))
            {
                _state = state;
            }
        }
    }

    private static bool IsTerminal(RunnerState state) => state is RunnerState.Stopped or RunnerState.Completed or RunnerState.Failed;

    private void AddProblem(string text)
    {
        Interlocked.Increment(ref _problemCount);
        lock (_gate)
        {
            if (_problems.Count < MaxProblemsKept)
            {
                _problems.Add(Truncate(text, MaxNoteLength));
            }
        }
    }

    private enum RunKind
    {
        Completed,
        Stopped,
        Fatal,
        Failure,
    }

    private readonly record struct RunResult(RunKind Kind, string Reason, bool Stable)
    {
        public static RunResult Completed() => new(RunKind.Completed, string.Empty, false);

        public static RunResult Stopped() => new(RunKind.Stopped, string.Empty, false);

        /// <summary>The configuration or the file is wrong; trying again cannot help.</summary>
        public static RunResult Fatal(string reason) => new(RunKind.Fatal, reason, false);

        /// <summary>An abnormal end that a restart may cure.</summary>
        public static RunResult Failure(string reason, bool stable) => new(RunKind.Failure, reason, stable);
    }

    private sealed class RunContext(int run, long startTimestamp)
    {
        public readonly int Run = run;
        public readonly long StartTimestamp = startTimestamp;
        public readonly TaskCompletionSource<string> Fatal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public long LastOutputTimestamp = startTimestamp;
        public long RowsThisRun;
        public volatile string? LastStderrError;
    }
}
