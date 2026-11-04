using System.Threading.Channels;
using Jankscope.Frames.Incidents;
using Jankscope.Frames.PresentMon;
using Jankscope.Frames.Ring;

namespace Jankscope.Frames.Capture;

/// <summary>Where frames come from: the PresentMon runner in production, a script in tests.</summary>
public interface IFrameSource : IAsyncDisposable
{
    ChannelReader<CapturedFrame> Samples { get; }

    Task Completion { get; }

    RunnerHealth Health { get; }

    void Start();

    Task StopAsync(CancellationToken cancellationToken = default);
}

/// <summary>The production source: the pinned PresentMon console executable, supervised by <see cref="PresentMonRunner"/>.</summary>
public sealed class RunnerFrameSource(PresentMonRunner runner) : IFrameSource
{
    public ChannelReader<CapturedFrame> Samples => runner.Samples;

    public Task Completion => runner.Completion;

    public RunnerHealth Health => runner.Health;

    public void Start() => runner.Start();

    public Task StopAsync(CancellationToken cancellationToken = default) => runner.StopAsync(cancellationToken);

    public ValueTask DisposeAsync() => runner.DisposeAsync();
}

/// <summary>
/// One live capture of one program: starts a frame source and pumps its frames into the history ring and the capture clock map, on its own thread, so
/// whatever draws the interface never sits on the capture path (spec section 6.1, HC-6). The pump is the only writer of the ring.
/// <para>
/// Frames whose present mode is "Other" are not swap-chain presents of a game (the CLI report ignores them the same way) and are counted in
/// <see cref="IgnoredFrames"/>, never silently dropped. Frames the source's queue dropped are in <c>Health.DroppedFrames</c>.
/// </para>
/// </summary>
public sealed class CaptureSession : IAsyncDisposable
{
    private readonly IFrameSource _source;
    private readonly RingBuffer<FrameSample> _history;
    private readonly CaptureClockMap _clock;
    private readonly TimeProvider _time;
    private readonly CancellationTokenSource _stop = new();
    private Task _pump = Task.CompletedTask;
    private int _started;
    private long _received;
    private long _ignored;
    private long _writeFailures;
    private long _lastFrameTicks;
    private int _run;

    public CaptureSession(IFrameSource source, RingBuffer<FrameSample> history, CaptureClockMap clock, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(clock);
        _source = source;
        _history = history;
        _clock = clock;
        _time = timeProvider ?? TimeProvider.System;
    }

    public RingBuffer<FrameSample> History => _history;

    public CaptureClockMap Clock => _clock;

    /// <summary>Frames written to the history.</summary>
    public long FramesReceived => Interlocked.Read(ref _received);

    /// <summary>Frames not written because their present mode is "Other".</summary>
    public long IgnoredFrames => Interlocked.Read(ref _ignored);

    /// <summary>Frames the ring refused (only possible if the single-writer rule were broken); should always be zero.</summary>
    public long WriteFailures => Interlocked.Read(ref _writeFailures);

    /// <summary>The capture the latest frame belongs to; it increases when PresentMon is restarted.</summary>
    public int Run => Volatile.Read(ref _run);

    public RunnerHealth Health => _source.Health;

    /// <summary>Completes when the source has ended and every queued frame has been written.</summary>
    public Task Completion => _pump;

    /// <summary>How long ago the latest frame arrived, or null when none has.</summary>
    public TimeSpan? SinceLastFrame
    {
        get
        {
            var last = Interlocked.Read(ref _lastFrameTicks);
            return last == 0 ? null : _time.GetElapsedTime(last);
        }
    }

    /// <summary>True while the capture runs but the program presents nothing (a menu, a minimised window, a loading screen): the interface says "waiting for frames", not "0 fps".</summary>
    public bool WaitingForFrames(TimeSpan quietFor) =>
        Health.State is RunnerState.Running or RunnerState.Starting && (SinceLastFrame is not { } since || since > quietFor);

    /// <summary>Starts capturing. Throws what the source throws (for example when anti-cheat safety suppresses capture) and starts nothing then.</summary>
    public void Start()
    {
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
        {
            throw new InvalidOperationException("The session was already started.");
        }

        _source.Start();
        _pump = Task.Run(PumpAsync);
    }

    /// <summary>Ends the capture gracefully and waits until nothing is left running and every queued frame is written. Safe to repeat and from any state.</summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _source.StopAsync(cancellationToken).ConfigureAwait(false);
        await _pump.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        finally
        {
            await _source.DisposeAsync().ConfigureAwait(false);
            _stop.Dispose();
        }
    }

    private async Task PumpAsync()
    {
        try
        {
            await foreach (var captured in _source.Samples.ReadAllAsync(_stop.Token).ConfigureAwait(false))
            {
                var sample = captured.Sample;
                if (string.Equals(sample.PresentMode, "Other", StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref _ignored);
                    continue;
                }

                var ticks = _time.GetTimestamp();
                var previousRun = Volatile.Read(ref _run);
                if (previousRun != 0 && captured.Run != previousRun)
                {
                    _clock.Reset();
                }

                _clock.Observe(ticks, sample.TimestampMs);
                if (_history.TryWrite(in sample))
                {
                    Interlocked.Increment(ref _received);
                }
                else
                {
                    Interlocked.Increment(ref _writeFailures);
                }

                Volatile.Write(ref _run, captured.Run);
                Interlocked.Exchange(ref _lastFrameTicks, Math.Max(1, ticks));
            }
        }
        catch (OperationCanceledException)
        {
            // disposed while reading
        }
    }
}
