namespace Jankscope.Frames.Incidents;

/// <summary>
/// A moment on the capture timeline (the clock the frame log's <c>TimeInMs</c> counts on) with the uncertainty of how it was obtained.
/// </summary>
/// <param name="Ms">Milliseconds on the capture clock.</param>
/// <param name="UncertaintyMs">How far the estimate may be off because frames reach this program with varying delay (spec section 6.2: the timeline never claims finer ordering).</param>
/// <param name="Observations">How many recent frames the estimate rests on.</param>
/// <param name="Epoch">Increases each time the capture clock restarted; times of different epochs are not comparable.</param>
public readonly record struct CaptureTime(double Ms, double UncertaintyMs, int Observations, int Epoch);

/// <summary>
/// Maps this program's monotonic clock (QPC ticks) onto the capture clock, so that "the key was pressed now" can be placed among the frames.
/// <para>
/// Each frame is observed twice: when it was presented (its capture timestamp) and when it reached us (our QPC). The difference is the offset between
/// the two clocks plus a delivery delay that varies from frame to frame and is never negative. The smallest recent difference therefore estimates the
/// offset best, and the spread between the smallest and a typical-worst (95th percentile) difference is reported as the uncertainty. The absolute minimum
/// delay (how long PresentMon and the pipe take at best) cannot be measured from here and is not included: the true event is always earlier than the
/// mapped time by that delay and by the person's reaction time, which is why the anchor search looks back ten seconds and the person chooses.
/// </para>
/// Thread-safe: one thread observes, any thread estimates.
/// </summary>
public sealed class CaptureClockMap
{
    /// <summary>A capture timestamp this much smaller than the newest one means the capture clock restarted (a new run of the capture tool).</summary>
    private const double RestartThresholdMs = 1000.0;

    private readonly long _frequency;
    private readonly double[] _delays;
    private readonly object _gate = new();
    private int _count;
    private int _next;
    private double _lastCaptureMs = double.NegativeInfinity;
    private int _epoch;

    public CaptureClockMap(long qpcFrequency, int windowSize = 1024)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(qpcFrequency, 0L);
        ArgumentOutOfRangeException.ThrowIfLessThan(windowSize, 2);
        _frequency = qpcFrequency;
        _delays = new double[windowSize];
    }

    /// <summary>Observations currently in the window.</summary>
    public int Observations
    {
        get
        {
            lock (_gate)
            {
                return _count;
            }
        }
    }

    /// <summary>Records that a frame stamped <paramref name="captureMs"/> on the capture clock reached this program at <paramref name="arrivalTicks"/>. Non-finite stamps are ignored.</summary>
    public void Observe(long arrivalTicks, double captureMs)
    {
        if (!double.IsFinite(captureMs))
        {
            return;
        }

        lock (_gate)
        {
            if (captureMs < _lastCaptureMs - RestartThresholdMs)
            {
                _count = 0;
                _next = 0;
                _epoch++;
            }

            _lastCaptureMs = captureMs;
            _delays[_next] = TicksToMs(arrivalTicks) - captureMs;
            _next = (_next + 1) % _delays.Length;
            if (_count < _delays.Length)
            {
                _count++;
            }
        }
    }

    /// <summary>The capture time that corresponds to <paramref name="qpcTicks"/>, or null when no frame has been observed (unknown is never zero).</summary>
    public CaptureTime? Estimate(long qpcTicks)
    {
        lock (_gate)
        {
            if (_count == 0)
            {
                return null;
            }

            var sorted = new double[_count];
            Array.Copy(_delays, sorted, _count);
            Array.Sort(sorted);
            var smallest = sorted[0];
            var typicalWorst = sorted[Math.Max(0, (int)Math.Ceiling(0.95 * _count) - 1)];
            return new CaptureTime(TicksToMs(qpcTicks) - smallest, typicalWorst - smallest, _count, _epoch);
        }
    }

    private double TicksToMs(long ticks) => ticks * 1000.0 / _frequency;
}
