namespace Jankscope.Harness;

/// <summary>
/// Waits until an absolute timestamp: sleeps in one-millisecond steps while the target is farther away than a safety margin, then spins
/// for the rest. The margin is calibrated from how long this machine's <c>Sleep(1)</c> really takes, so a coarse OS timer tick makes the
/// pacer spin more (more CPU) instead of sleeping past the target (less accuracy). It never changes the system timer resolution, which would be a
/// global side effect on every program on the machine.
/// </summary>
internal sealed class FramePacer
{
    public const double MinMarginMs = 1.0;
    public const double MaxMarginMs = 25.0;
    public const double SlackMs = 0.5;

    private readonly IClock _clock;

    public FramePacer(IClock clock, long initialMarginTicks)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentOutOfRangeException.ThrowIfNegative(initialMarginTicks);
        _clock = clock;
        MarginTicks = initialMarginTicks;
    }

    /// <summary>Distance from the target at which sleeping stops and spinning starts. Only ever grows.</summary>
    public long MarginTicks { get; private set; }

    public int Sleeps { get; private set; }

    public long SpinIterations { get; private set; }

    /// <summary>Waits that began after the target or were carried past it by a sleep.</summary>
    public int LateArrivals { get; private set; }

    public static long SlackTicks(long frequency) => QpcTime.FromMs(SlackMs, frequency);

    /// <summary>Measures <paramref name="samples"/> real sleeps and returns the slowest plus half a millisecond, clamped to 1-25 ms.</summary>
    public static long CalibrateMarginTicks(IClock clock, int samples = 20)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentOutOfRangeException.ThrowIfLessThan(samples, 1);

        var slowest = 0L;
        for (var i = 0; i < samples; i++)
        {
            var before = clock.GetTimestamp();
            clock.Sleep(1);
            slowest = Math.Max(slowest, clock.GetTimestamp() - before);
        }

        var min = QpcTime.FromMs(MinMarginMs, clock.Frequency);
        var max = QpcTime.FromMs(MaxMarginMs, clock.Frequency);
        return Math.Clamp(slowest + SlackTicks(clock.Frequency), min, max);
    }

    /// <summary>Returns true when it reached the target by waiting; false when the target had already passed (or a sleep overshot it).</summary>
    public bool WaitUntil(long targetTimestamp)
    {
        var now = _clock.GetTimestamp();
        if (now >= targetTimestamp)
        {
            LateArrivals++;
            return false;
        }

        var slack = SlackTicks(_clock.Frequency);
        while (targetTimestamp - now > MarginTicks)
        {
            var before = now;
            _clock.Sleep(1);
            now = _clock.GetTimestamp();
            Sleeps++;

            // This sleep took longer than the margin assumed it could: keep more of the next wait for spinning.
            MarginTicks = Math.Max(MarginTicks, now - before + slack);

            if (now >= targetTimestamp)
            {
                LateArrivals++;
                return false;
            }
        }

        while (now < targetTimestamp)
        {
            _clock.SpinOnce();
            SpinIterations++;
            now = _clock.GetTimestamp();
        }

        return true;
    }
}
