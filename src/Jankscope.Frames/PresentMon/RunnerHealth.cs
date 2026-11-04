using Jankscope.Core;

namespace Jankscope.Frames.PresentMon;

public enum RunnerState
{
    /// <summary>Created, <see cref="PresentMonRunner.Start"/> not called yet.</summary>
    NotStarted,

    /// <summary>Verifying the executable and starting a capture.</summary>
    Starting,

    /// <summary>A capture is running.</summary>
    Running,

    /// <summary>The watchdog saw no output for too long and is ending the capture; a restart may follow.</summary>
    Stalled,

    /// <summary>A capture ended abnormally; waiting before the next attempt.</summary>
    BackingOff,

    /// <summary>A stop was requested; the capture and its ETW session are being ended.</summary>
    Stopping,

    /// <summary>Stopped on request. Nothing is left running.</summary>
    Stopped,

    /// <summary>The capture ended by itself as configured (timed capture finished, or the target exited).</summary>
    Completed,

    /// <summary>Gave up: see <see cref="RunnerHealth.LastError"/>. Nothing is left running unless <see cref="RunnerHealth.UncleanedSession"/> says otherwise.</summary>
    Failed,
}

/// <summary>
/// An immutable snapshot of the runner. Counters are totals over all restarts. <see cref="Problems"/> keeps the first few distinct
/// problem texts (the rest are only counted in <see cref="ProblemCount"/>), <see cref="Notes"/> holds informational messages from
/// PresentMon itself, for example that processes started by another account appear as unknown when running unelevated.
/// </summary>
public sealed record RunnerHealth(
    RunnerState State,
    int Restarts,
    string? LastError,
    long LinesRead,
    long RowsParsed,
    IReadOnlyList<string> Problems,
    long ProblemCount,
    long DroppedFrames,
    IReadOnlyList<string> Notes,
    string? SessionName,
    int Stalls,
    string? UncleanedSession);

/// <summary>A parsed frame together with the capture it belongs to. A restart begins a new <see cref="Run"/> whose clock and indices start again.</summary>
public readonly record struct CapturedFrame(int Run, FrameSample Sample);

/// <summary>Exponential back-off between restarts: the k-th restart waits <c>initial * 2^(k-1)</c>, never more than <c>max</c>.</summary>
public static class RestartBackoff
{
    public static TimeSpan DelayFor(int restartNumber, TimeSpan initial, TimeSpan max)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(restartNumber, 1);
        if (initial <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(initial), "The first back-off must be positive.");
        }

        if (max < initial)
        {
            throw new ArgumentOutOfRangeException(nameof(max), "The back-off limit must not be below the first back-off.");
        }

        var shift = Math.Min(restartNumber - 1, 20);
        var ticks = initial.Ticks > (long.MaxValue >> shift) ? max.Ticks : Math.Min(max.Ticks, initial.Ticks << shift);
        return TimeSpan.FromTicks(ticks);
    }
}

public sealed record PresentMonRunnerOptions
{
    /// <summary>The pinned executable; verified before every launch.</summary>
    public required PinnedExecutable Executable { get; init; }

    /// <summary>Base session name. It must start with the product prefix; run <c>n</c> uses <c>{SessionName}-{n}</c>, so a session left behind by one run can never block the next.</summary>
    public string SessionName { get; init; } = PresentMonArguments.NewSessionName(Environment.ProcessId);

    /// <summary>Plain executable names to capture; empty captures every process.</summary>
    public IReadOnlyList<string> ProcessNames { get; init; } = [];

    public bool TerminateOnProcessExit { get; init; }

    /// <summary>When set, PresentMon records for this many seconds and exits (the runner then reports <see cref="RunnerState.Completed"/>).</summary>
    public int? TimedSeconds { get; init; }

    /// <summary>
    /// No output for this long means PresentMon is stuck: it is ended and, if restarts remain, started again. PresentMon prints
    /// nothing while the captured program presents no frames, so a watchdog cannot tell a stuck PresentMon from an idle target;
    /// choose this per use, or <see cref="Timeout.InfiniteTimeSpan"/> to switch the watchdog off.
    /// </summary>
    public TimeSpan WatchdogNoOutputTimeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>How long a stop waits for PresentMon to leave on its own before the process is ended.</summary>
    public TimeSpan StopGracePeriod { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>How long the helper invocation that ends an ETW session may take.</summary>
    public TimeSpan CleanupTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Restarts allowed in a row after abnormal ends; the next failure makes the runner give up.</summary>
    public int MaxRestarts { get; init; } = 3;

    public TimeSpan RestartBackoffInitial { get; init; } = TimeSpan.FromMilliseconds(500);

    public TimeSpan RestartBackoffMax { get; init; } = TimeSpan.FromSeconds(8);

    /// <summary>A capture that produced frames for at least this long counts as healthy and resets the restart budget.</summary>
    public TimeSpan StableRunDuration { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Size of the frame queue. When it is full the oldest frame is dropped and counted, so reading the pipe never blocks.</summary>
    public int FrameQueueCapacity { get; init; } = 8192;

    /// <summary>
    /// Optional suppression check invoked before starting capture. When provided and <see cref="SuppressionDecision.Suppressed"/>
    /// is true, <see cref="PresentMonRunner.Start"/> transitions directly to <see cref="RunnerState.Failed"/> without launching PresentMon (HC-1).
    /// </summary>
    public Func<SuppressionDecision>? AntiCheatSuppressionCheck { get; init; }
}
