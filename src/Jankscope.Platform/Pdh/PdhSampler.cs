using System.Diagnostics;
using Jankscope.Core.Hardware;

namespace Jankscope.Platform.Pdh;

/// <param name="Hertz">Sampling rate, 1 to 10 Hz (spec section 7.1).</param>
/// <param name="KnownAdapters">Adapters (from the DXGI listing) the GPU part of every sample must account for; one without counter instances says so instead of being omitted.</param>
public sealed record SamplerOptions(double Hertz = 2.0, IReadOnlyCollection<Luid>? KnownAdapters = null);

/// <param name="Ticks">Collection attempts since start (priming excluded).</param>
/// <param name="Delivered">Samples handed to the callback.</param>
/// <param name="FailedCollections">Attempts that threw (in the source or in the callback); the sampler continues after each.</param>
/// <param name="MissedTicks">Grid points skipped because a tick ran longer than the period.</param>
/// <param name="MeanCollectMilliseconds">Mean wall time of a tick (collect, build and callback): the sampler's measured self-cost in time.</param>
/// <param name="MaxLatenessMilliseconds">Largest delay between a tick's grid point and its start.</param>
/// <param name="LastError">The exception type name of the last failure (never its message, which can hold paths).</param>
public sealed record SamplerStats(
    long Ticks,
    long Delivered,
    long FailedCollections,
    long MissedTicks,
    double MeanCollectMilliseconds,
    double MaxCollectMilliseconds,
    double MaxLatenessMilliseconds,
    string? LastError);

/// <summary>
/// Samples system context at 1 to 10 Hz on its own thread, off the capture path. Ticks sit on a fixed grid (<see cref="SamplerSchedule"/>),
/// so a slow tick cannot make later ones drift; an overrun skips grid points instead of firing a burst. A failure in the source or in
/// the callback is counted and the sampler goes on. The callback runs on the sampler thread and must return quickly (hand the sample to a
/// queue). The sampler does not own the source and does not dispose it.
/// </summary>
public sealed class PdhSampler : IDisposable
{
    private readonly ICounterSource _source;
    private readonly SamplerOptions _options;
    private readonly Action<SystemSample> _onSample;
    private readonly long _periodTicks;
    private readonly ManualResetEventSlim _stop = new(false);
    private readonly object _gate = new();
    private readonly object _statsGate = new();
    private Thread? _thread;
    private bool _disposed;

    private long _ticks;
    private long _delivered;
    private long _failed;
    private long _missed;
    private double _collectSumMs;
    private double _collectMaxMs;
    private double _latenessMaxMs;
    private string? _lastError;

    public PdhSampler(ICounterSource source, SamplerOptions options, Action<SystemSample> onSample)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(onSample);
        _source = source;
        _options = options;
        _onSample = onSample;
        _periodTicks = Math.Max(1, (long)Math.Round(SamplerSchedule.PeriodMilliseconds(options.Hertz) * Stopwatch.Frequency / 1000.0));
    }

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _thread is { IsAlive: true };
            }
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_thread is { IsAlive: true })
            {
                throw new InvalidOperationException("The sampler is already running.");
            }

            _stop.Reset();
            _thread = new Thread(Loop) { IsBackground = true, Name = "Jankscope.PdhSampler" };
            _thread.Start();
        }
    }

    /// <summary>How long <see cref="Stop()"/> waits for the sampler thread: a counter provider that never returns must not hang a shutdown.</summary>
    public static readonly TimeSpan DefaultStopTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Stops the sampler and waits (up to <see cref="DefaultStopTimeout"/>) for the current tick to finish. True when the thread has ended. Safe to call repeatedly.</summary>
    public bool Stop() => Stop(DefaultStopTimeout);

    /// <summary>
    /// Asks the sampler to stop and waits at most <paramref name="timeout"/> for its thread. False means a tick was still running when the wait
    /// ran out (the thread is a background thread, so it cannot keep the process alive); the sampler then still reports <see cref="IsRunning"/>.
    /// </summary>
    public bool Stop(TimeSpan timeout)
    {
        Thread? thread;
        lock (_gate)
        {
            _stop.Set();
            thread = _thread;
        }

        if (thread is null || thread == Thread.CurrentThread)
        {
            return true;
        }

        return thread.Join(timeout);
    }

    public SamplerStats GetStats()
    {
        lock (_statsGate)
        {
            return new SamplerStats(_ticks, _delivered, _failed, _missed, _ticks == 0 ? 0 : _collectSumMs / _ticks, _collectMaxMs, _latenessMaxMs, _lastError);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        Stop(); // the stop event holds no operating-system handle unless WaitHandle is touched, so it needs no disposal
    }

    private void Loop()
    {
        // Prime the rate counters: the first collection after creation has no rate yet. Its result is discarded.
        try
        {
            _ = _source.Collect();
        }
        catch (Exception ex)
        {
            Record(failure: ex.GetType().Name, 0, 0, 0, tick: false);
        }

        var schedule = new SamplerSchedule(Stopwatch.GetTimestamp(), _periodTicks);
        long sequence = 0;
        while (WaitUntil(schedule.NextDeadline))
        {
            var started = Stopwatch.GetTimestamp();
            var lateMs = Math.Max(0, (started - schedule.NextDeadline) * 1000.0 / Stopwatch.Frequency);
            string? failure = null;
            var delivered = false;
            try
            {
                var snapshot = _source.Collect();
                var sample = SystemSampleBuilder.Build(sequence, started, DateTimeOffset.UtcNow, snapshot, _options.KnownAdapters ?? []);
                sequence++;
                _onSample(sample);
                delivered = true;
            }
            catch (Exception ex)
            {
                // Isolation: a failing source or callback must not end sampling (spec section 6.4). Only the type name is kept.
                failure = ex.GetType().Name;
            }

            var finished = Stopwatch.GetTimestamp();
            var missed = schedule.Advance(finished);
            Record(failure, (finished - started) * 1000.0 / Stopwatch.Frequency, lateMs, missed, tick: true, delivered);
        }
    }

    private void Record(string? failure, double collectMs, double lateMs, int missed, bool tick, bool delivered = false)
    {
        lock (_statsGate)
        {
            if (tick)
            {
                _ticks++;
                _collectSumMs += collectMs;
                _collectMaxMs = Math.Max(_collectMaxMs, collectMs);
                _latenessMaxMs = Math.Max(_latenessMaxMs, lateMs);
                _missed += missed;
            }

            if (delivered)
            {
                _delivered++;
            }

            if (failure is not null)
            {
                _failed++;
                _lastError = failure;
            }
        }
    }

    /// <summary>Waits until the deadline; returns false when the sampler was stopped first.</summary>
    private bool WaitUntil(long deadlineTicks)
    {
        while (true)
        {
            if (_stop.IsSet)
            {
                return false;
            }

            var remainingMs = (deadlineTicks - Stopwatch.GetTimestamp()) * 1000.0 / Stopwatch.Frequency;
            if (remainingMs < 1.0)
            {
                return !_stop.IsSet; // within a millisecond: go now rather than spin
            }

            if (_stop.Wait(TimeSpan.FromMilliseconds(remainingMs)))
            {
                return false;
            }
        }
    }
}
