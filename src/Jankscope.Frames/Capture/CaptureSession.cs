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
/// <para>
/// A frame stamped a little earlier than the frame before it (at most <see cref="ReorderToleranceMs"/>; Windows can emit such a row when it changes how
/// a window is composed) is left out and counted in <see cref="OutOfOrderFrames"/>. A frame stamped much earlier is not a reordering: either the
/// frame before it carried a wrong time far ahead or the tool's clock started again. It is kept, counted in <see cref="BackwardJumps"/>, and the
/// capture clock map starts a new epoch, so one wrong time cannot silence the rest of the capture (review of Stage 1b, finding 4).
/// </para>
/// </summary>
public sealed class CaptureSession : IAsyncDisposable
{
    /// <summary>How far behind the previous frame a frame may be stamped and still count as a reordering (a few frames) rather than a jump of the clock.</summary>
    public const double ReorderToleranceMs = 100.0;

    private readonly IFrameSource _source;
    private readonly RingBuffer<FrameSample> _history;
    private readonly CaptureClockMap _clock;
    private readonly TimeProvider _time;
    private readonly CancellationTokenSource _stop = new();
    private Task _pump = Task.CompletedTask;
    private int _started;
    private long _received;
    private long _ignored;
    private long _outOfOrder;
    private long _backwardJumps;
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

    /// <summary>Frames left out because they were stamped slightly earlier than the frame before them (at most <see cref="ReorderToleranceMs"/>). Shown and exported.</summary>
    public long OutOfOrderFrames => Interlocked.Read(ref _outOfOrder);

    /// <summary>Times a frame was stamped much earlier than the frame before it; the frame was kept and the capture clock map started a new epoch. Shown and exported.</summary>
    public long BackwardJumps => Interlocked.Read(ref _backwardJumps);

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
        var haveTimestamp = false;
        var lastTimestampMs = 0.0;
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
                    haveTimestamp = false;
                    lastTimestampMs = 0.0;
                }

                // When DWM transitions a windowed flip swapchain between Composed: Flip and DXGI, PresentMon can emit a secondary
                // row whose TimeInMs is ~1 frame earlier than the immediately preceding row within the same capture run. Leaving
                // such a row out prevents FrameRingExtensions.SelectWindow and StutterDetector from treating a single-frame DWM
                // stream interleaving as a capture restart. It is counted on its own (OutOfOrderFrames), never as "ignored".
                // A row far behind is not that: the reference row had a wrong time far ahead, or the clock started again. Comparing
                // every later row with a reference that is too far ahead would drop all of them until the clock caught up (one
                // such row silenced 4,322 of 5,101 rows in the review), so the row is kept and the clock map starts a new epoch.
                if (haveTimestamp && sample.TimestampMs < lastTimestampMs)
                {
                    if (lastTimestampMs - sample.TimestampMs <= ReorderToleranceMs)
                    {
                        Interlocked.Increment(ref _outOfOrder);
                        continue;
                    }

                    Interlocked.Increment(ref _backwardJumps);
                }

                haveTimestamp = true;
                lastTimestampMs = sample.TimestampMs;
                _clock.Observe(captured.ArrivalTicks ?? ticks, sample.TimestampMs);
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
