using System.Globalization;
using System.Threading.Channels;
using Jankscope.Frames.PresentMon;

namespace Jankscope.Frames.Tests.PresentMon;

/// <summary>
/// A clock that only moves when the test says so. Timers created through it (Task.Delay with a TimeProvider, CancellationTokenSource with a
/// timeout) fire on the thread pool when <see cref="Advance"/> passes their due time, so watchdog, grace period and back-off tests take no real time.
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private long _now;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp()
    {
        lock (_gate)
        {
            return _now;
        }
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(_now);
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        lock (_gate)
        {
            _timers.Add(timer);
        }

        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>Timers that are armed and not disposed.</summary>
    public int PendingTimers
    {
        get
        {
            lock (_gate)
            {
                return _timers.Count(t => t.Due >= 0);
            }
        }
    }

    /// <summary>Time until the earliest armed timer fires, or null when none is armed.</summary>
    public TimeSpan? NextDueIn
    {
        get
        {
            lock (_gate)
            {
                var due = _timers.Where(t => t.Due >= 0).Select(t => t.Due).DefaultIfEmpty(-1).Min();
                return _timers.Any(t => t.Due >= 0) ? TimeSpan.FromTicks(due - _now) : null;
            }
        }
    }

    public void Advance(TimeSpan by)
    {
        long target;
        lock (_gate)
        {
            target = _now + by.Ticks;
        }

        while (true)
        {
            ManualTimer? next;
            lock (_gate)
            {
                next = _timers.Where(t => t.Due >= 0 && t.Due <= target).OrderBy(t => t.Due).FirstOrDefault();
                if (next is null)
                {
                    _now = target;
                    return;
                }

                _now = Math.Max(_now, next.Due);
                next.Rearm(_now);
            }

            next.Fire();
        }
    }

    private void Remove(ManualTimer timer)
    {
        lock (_gate)
        {
            _timers.Remove(timer);
        }
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        private long _period = -1;

        public long Due { get; private set; } = -1;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._gate)
            {
                Due = dueTime == Timeout.InfiniteTimeSpan ? -1 : owner._now + dueTime.Ticks;
                _period = period == Timeout.InfiniteTimeSpan || period == TimeSpan.Zero ? -1 : period.Ticks;
            }

            return true;
        }

        /// <summary>Called under the owner's lock when the timer fires: schedule the next period or disarm.</summary>
        public void Rearm(long now) => Due = _period > 0 ? now + _period : -1;

        public void Fire() => Task.Run(() => callback(state));

        public void Dispose() => owner.Remove(this);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>Minimal log parser for the runner tests: finds TimeInMs and MsBetweenPresents by name; anything else about the log is ignored.</summary>
internal sealed class TestFrameParser : IFrameLogParser
{
    private int _timeColumn = -1;
    private int _frameColumn = -1;
    private long _index;

    public LogHeaderResult ReadHeader(string headerLine)
    {
        var columns = headerLine.Split(',');
        _timeColumn = Array.IndexOf(columns, "TimeInMs");
        _frameColumn = Array.IndexOf(columns, "MsBetweenPresents");
        if (_timeColumn < 0 || _frameColumn < 0)
        {
            return new LogHeaderResult(false, FrameLogFormat.Unknown, "missing TimeInMs or MsBetweenPresents", [], []);
        }

        return new LogHeaderResult(true, FrameLogFormat.PresentMon2, null, [], []);
    }

    public bool TryParseRow(string line, out FrameSample sample, out string? problem)
    {
        var columns = line.Split(',');
        if (columns.Length <= Math.Max(_timeColumn, _frameColumn)
            || !double.TryParse(columns[_timeColumn], NumberStyles.Float, CultureInfo.InvariantCulture, out var time)
            || !double.TryParse(columns[_frameColumn], NumberStyles.Float, CultureInfo.InvariantCulture, out var frame))
        {
            sample = default;
            problem = "not a data row";
            return false;
        }

        sample = new FrameSample(_index++, time, frame);
        problem = null;
        return true;
    }
}

/// <summary>A parser that throws on every row, to prove the runner survives a misbehaving parser.</summary>
internal sealed class ThrowingFrameParser : IFrameLogParser
{
    public LogHeaderResult ReadHeader(string headerLine) => new(true, FrameLogFormat.PresentMon2, null, [], []);

    public bool TryParseRow(string line, out FrameSample sample, out string? problem) => throw new InvalidOperationException("boom");
}

internal static class SampleLog
{
    public const string Header = "Application,ProcessID,SwapChainAddress,PresentRuntime,SyncInterval,PresentFlags,AllowsTearing,PresentMode,TimeInMs,MsBetweenSimulationStart,MsBetweenPresents,MsBetweenDisplayChange,MsInPresentAPI,MsRenderPresentLatency,MsUntilDisplayed,CPUStartTimeInMs,MsBetweenAppStart,MsCPUBusy,MsCPUWait,MsGPULatency,MsGPUTime,MsGPUBusy,MsGPUWait,MsAnimationError,AnimationTime,MsFlipDelay,MsAllInputToPhotonLatency,MsClickToPhotonLatency";

    /// <summary>A row in the 2.6.0 layout whose TimeInMs is <paramref name="index"/> * 7 and whose frame time is 6.9 ms.</summary>
    public static string Row(int index) =>
        $"TestAppA.exe,1000,0x103E8,DXGI,0,0,0,Hardware Composed: Independent Flip,{((index + 1) * 7.0).ToString("F4", CultureInfo.InvariantCulture)},NA,6.90000000000000,6.95100000000000,5.18820000000000,19.75560000000000,25.8716,13.6823,6.8608,1.6726,5.1882,14.4858,6.9424,6.9424,0.0000,NA,13.6823,NA,NA,NA";

    public const string ElevationWarning = "warning: PresentMon requires elevated privilege in order to query processes that are\n" +
        "         short-running or started on another account.  Without it, those processes will\n" +
        "         be listed as '<unknown>' and they can't be targeted by --process_name nor trigger\n" +
        "         --terminate_on_proc_exit.";
}

/// <summary>
/// An in-memory launcher: no process exists. It models what the runner depends on, including the ETW session that a real PresentMon
/// owns: a capture adds its session, a clean exit removes it, a crash or a kill leaves it behind, and a terminate-existing-session
/// start removes it and makes the matching capture leave with code 0 (observed with PresentMon 2.6.0).
/// </summary>
internal sealed class FakeProcessLauncher : IProcessLauncher
{
    private readonly object _gate = new();
    private readonly List<FakeChild> _children = [];
    private readonly HashSet<string> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<Action<FakeChild>> _captureScripts = [];
    private int _nextProcessId = 5000;

    /// <summary>Applied to a capture when no queued script is left.</summary>
    public Action<FakeChild>? DefaultCapture { get; set; }

    /// <summary>Thrown by the next launches, one per launch, before anything is started.</summary>
    public Queue<Exception> LaunchFailures { get; } = [];

    public TerminatorBehavior Terminator { get; set; } = TerminatorBehavior.Normal;

    public IReadOnlyList<FakeChild> Children
    {
        get
        {
            lock (_gate)
            {
                return [.. _children];
            }
        }
    }

    public IReadOnlyList<FakeChild> Captures => [.. Children.Where(c => !c.IsTerminator)];

    public IReadOnlyList<FakeChild> Terminators => [.. Children.Where(c => c.IsTerminator)];

    public IReadOnlyCollection<string> LiveSessions
    {
        get
        {
            lock (_gate)
            {
                return [.. _sessions];
            }
        }
    }

    public void EnqueueCapture(Action<FakeChild> script)
    {
        lock (_gate)
        {
            _captureScripts.Enqueue(script);
        }
    }

    /// <summary>Called first in every launch, while the runner still holds the verified file: lets a test act at the moment the real launcher would create the process.</summary>
    public Action<ProcessLaunchRequest>? OnStart { get; set; }

    public IChildProcess Start(ProcessLaunchRequest request)
    {
        OnStart?.Invoke(request);

        // The real launcher refuses any vector that is not on the grammar; a runner that built one would fail every test that launches.
        PresentMonArguments.EnsureSafe(request.Arguments);

        FakeChild child;
        Action<FakeChild>? script = null;
        lock (_gate)
        {
            if (LaunchFailures.TryDequeue(out var failure))
            {
                throw failure;
            }

            var terminating = request.Arguments[0] == "--terminate_existing_session";
            var session = request.Arguments[request.Arguments.ToList().IndexOf("--session_name") + 1];
            child = new FakeChild(this, _nextProcessId++, request, terminating, session);
            _children.Add(child);
            if (!terminating)
            {
                _sessions.Add(session);
                script = _captureScripts.TryDequeue(out var queued) ? queued : DefaultCapture;
            }
        }

        if (child.IsTerminator)
        {
            RunTerminator(child);
        }
        else
        {
            script?.Invoke(child);
        }

        return child;
    }

    internal void OnExit(FakeChild child, int code)
    {
        if (!child.IsTerminator && code == 0)
        {
            lock (_gate)
            {
                _sessions.Remove(child.Session);
            }
        }
    }

    private void RunTerminator(FakeChild terminator)
    {
        terminator.WriteError("warning: --terminate_existing_session exits without capturing anything; ignoring all other options.");
        switch (Terminator)
        {
            case TerminatorBehavior.Hang:
                return;
            case TerminatorBehavior.Fail:
                terminator.Exit(2);
                return;
        }

        bool had;
        FakeChild? capture;
        lock (_gate)
        {
            had = _sessions.Remove(terminator.Session);
            capture = _children.FirstOrDefault(c => !c.IsTerminator && c.Session == terminator.Session && !c.HasExited);
        }

        if (!had)
        {
            terminator.WriteError("error: no existing sessions found: " + terminator.Session);
            terminator.Exit(7);
            return;
        }

        if (capture is { IgnoresTerminate: false })
        {
            _ = Task.Run(() => capture.Exit(0));
        }

        terminator.Exit(0);
    }
}

internal enum TerminatorBehavior
{
    Normal,
    Hang,
    Fail,
}

internal sealed class FakeChild : IChildProcess
{
    private readonly FakeProcessLauncher _owner;
    private readonly Channel<ChildLine> _output = Channel.CreateUnbounded<ChildLine>();
    private readonly Channel<ChildLine> _errors = Channel.CreateUnbounded<ChildLine>();
    private readonly TaskCompletionSource<int> _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposed;

    public FakeChild(FakeProcessLauncher owner, int processId, ProcessLaunchRequest request, bool isTerminator, string session)
    {
        _owner = owner;
        ProcessId = processId;
        Request = request;
        IsTerminator = isTerminator;
        Session = session;
    }

    public int ProcessId { get; }

    public ProcessLaunchRequest Request { get; }

    public bool IsTerminator { get; }

    public string Session { get; }

    /// <summary>A stubborn child: asking it to leave by ending its session does nothing, only a kill ends it.</summary>
    public bool IgnoresTerminate { get; set; }

    /// <summary>The next kill attempt fails (the child stays alive) and reports why.</summary>
    public bool KillFails { get; set; }

    public bool Killed { get; private set; }

    public bool Disposed => Volatile.Read(ref _disposed) == 1;

    public bool HasExited => _exit.Task.IsCompleted;

    public int? ExitCode => _exit.Task.IsCompletedSuccessfully ? _exit.Task.Result : null;

    public void WriteLine(string text) => _output.Writer.TryWrite(new ChildLine(text, WasTruncated: false));

    public void WriteLines(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            WriteLine(line);
        }
    }

    public void WriteOverlongLine() => _output.Writer.TryWrite(new ChildLine(string.Empty, WasTruncated: true));

    /// <summary>The pipe ended in the middle of this line (the child was ended while it was writing).</summary>
    public void WriteUnterminatedLine(string text) => _output.Writer.TryWrite(new ChildLine(text, WasTruncated: false, Unterminated: true));

    public void WriteError(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            _errors.Writer.TryWrite(new ChildLine(line, WasTruncated: false));
        }
    }

    public void WriteHeaderAndRows(int rows, int firstIndex = 0)
    {
        WriteLine(SampleLog.Header);
        for (var i = 0; i < rows; i++)
        {
            WriteLine(SampleLog.Row(firstIndex + i));
        }
    }

    /// <summary>The child leaves with <paramref name="code"/>: its streams end, then its exit completes. Code 0 removes its ETW session, anything else leaves it behind.</summary>
    public void Exit(int code)
    {
        if (_exit.Task.IsCompleted)
        {
            return;
        }

        _output.Writer.TryComplete();
        _errors.Writer.TryComplete();
        _owner.OnExit(this, code);
        _exit.TrySetResult(code);
    }

    public ValueTask<ChildLine?> ReadOutputLineAsync(CancellationToken cancellationToken) => Read(_output, cancellationToken);

    public ValueTask<ChildLine?> ReadErrorLineAsync(CancellationToken cancellationToken) => Read(_errors, cancellationToken);

    public Task<int> WaitForExitAsync(CancellationToken cancellationToken) => _exit.Task.WaitAsync(cancellationToken);

    public bool TryKill(out string? failure)
    {
        if (HasExited)
        {
            failure = null;
            return true;
        }

        if (KillFails)
        {
            failure = "access denied";
            return false;
        }

        Killed = true;
        failure = null;
        Exit(-1);
        return true;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        if (!HasExited)
        {
            Killed = true;
            Exit(-1); // like the real child wrapper: letting go of a running child ends it
        }
    }

    private static async ValueTask<ChildLine?> Read(Channel<ChildLine> channel, CancellationToken cancellationToken)
    {
        while (await channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (channel.Reader.TryRead(out var line))
            {
                return line;
            }
        }

        return null;
    }
}

/// <summary>Everything a supervision test needs: a pinned dummy executable on disk, the fake launcher, the manual clock and a runner with short, test-friendly options.</summary>
internal sealed class RunnerRig : IAsyncDisposable
{
    private static readonly byte[] ExecutableBytes = Enumerable.Range(0, 2048).Select(i => (byte)(i % 253)).ToArray();

    public RunnerRig(Func<PresentMonRunnerOptions, PresentMonRunnerOptions>? configure = null, Func<IFrameLogParser>? parser = null)
    {
        ExecutablePath = Dir.Write("PresentMon-test.exe", ExecutableBytes);
        Pin = new PinnedExecutable(ExecutablePath, ExecutableBytes.Length, TempDir.Sha256Hex(ExecutableBytes));
        var options = new PresentMonRunnerOptions
        {
            Executable = Pin,
            SessionName = "Jankscope-4242",
            WatchdogNoOutputTimeout = TimeSpan.FromSeconds(10),
            StopGracePeriod = TimeSpan.FromSeconds(5),
            CleanupTimeout = TimeSpan.FromSeconds(10),
            MaxRestarts = 3,
            RestartBackoffInitial = TimeSpan.FromSeconds(1),
            RestartBackoffMax = TimeSpan.FromSeconds(8),
            StableRunDuration = TimeSpan.FromSeconds(30),
            FrameQueueCapacity = 1024,
        };
        Options = configure is null ? options : configure(options);
        Runner = new PresentMonRunner(Options, Launcher, parser ?? (() => new TestFrameParser()), Clock);
    }

    public TempDir Dir { get; } = new();

    public string ExecutablePath { get; }

    public PinnedExecutable Pin { get; }

    public FakeProcessLauncher Launcher { get; } = new();

    public ManualTimeProvider Clock { get; } = new();

    public PresentMonRunnerOptions Options { get; }

    public PresentMonRunner Runner { get; }

    public RunnerHealth Health => Runner.Health;

    /// <summary>Changes the pinned file on disk (same size, different bytes) so that the next verification must fail.</summary>
    public void TamperWithExecutable()
    {
        var tampered = (byte[])ExecutableBytes.Clone();
        tampered[100] ^= 0xFF;
        File.WriteAllBytes(ExecutablePath, tampered);
    }

    /// <summary>Waits (in real time, briefly) until <paramref name="condition"/> holds; the clock itself is never advanced here.</summary>
    public static async Task Eventually(Func<bool> condition, string what, int timeoutMs = 10_000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException("Timed out waiting for: " + what);
            }

            await Task.Delay(2);
        }
    }

    public Task WaitForCompletion(int timeoutMs = 10_000) => Runner.Completion.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs));

    /// <summary>Waits for a timer to be armed, then moves the clock: the pattern that makes a time-driven test deterministic.</summary>
    public async Task AdvanceWhenArmed(TimeSpan by)
    {
        await Eventually(() => Clock.PendingTimers > 0, "a timer to be armed before advancing the clock");
        Clock.Advance(by);
    }

    /// <summary>For tests where the runner may wait on several timers in a row: keeps moving the clock whenever one is armed, until the runner has finished.</summary>
    public async Task DriveClockUntilCompleted(TimeSpan step, int timeoutMs = 10_000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!Runner.Completion.IsCompleted)
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException("The runner did not finish while the clock was being advanced");
            }

            if (Clock.PendingTimers > 0)
            {
                Clock.Advance(step);
            }

            await Task.Delay(2);
        }
    }

    /// <summary>The state a stopped runner must leave: every child gone, every one released, no ETW session left behind.</summary>
    public void AssertNothingLeftBehind()
    {
        Assert.All(Launcher.Children, c =>
        {
            Assert.True(c.HasExited, $"child {c.ProcessId} ({string.Join(' ', c.Request.Arguments)}) is still running");
            Assert.True(c.Disposed, $"child {c.ProcessId} was never released");
        });
        Assert.Empty(Launcher.LiveSessions);
        Assert.Null(Runner.Health.UncleanedSession);
    }

    public async ValueTask DisposeAsync()
    {
        // The runner waits on the MANUAL clock. A test that failed halfway leaves a grace period or a clean-up timeout that nobody advances, and an
        // unconditional StopAsync here would then wait for ever: the failure would show up as a hung test host instead of a failed test (review 1a,
        // finding 21d; a likely, unproven cause of the one-off Frames hang). So the clock is kept moving until the runner has stopped, and a runner
        // that still does not stop fails loudly.
        var stopping = Runner.StopAsync();
        var deadline = Environment.TickCount64 + 20_000;
        while (!stopping.IsCompleted && Environment.TickCount64 < deadline)
        {
            if (Clock.PendingTimers > 0)
            {
                Clock.Advance(TimeSpan.FromSeconds(30));
            }

            await Task.Delay(2);
        }

        await stopping.WaitAsync(TimeSpan.FromSeconds(5));
        await Runner.DisposeAsync();
        Dir.Dispose();
    }
}
