namespace Jankscope.Core.Hardware;

/// <summary>
/// Deadline arithmetic of the sampler: ticks are placed on a fixed grid (start + n * period) and never relative to when the previous
/// tick finished, so slow ticks cannot accumulate drift. A tick that overruns skips the missed grid points instead of firing a burst.
/// </summary>
public sealed class SamplerSchedule
{
    public const double MinHertz = 1.0;
    public const double MaxHertz = 10.0;

    private readonly long _period;

    public SamplerSchedule(long startTicks, long periodTicks)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(periodTicks, 0);
        _period = periodTicks;
        NextDeadline = startTicks + periodTicks;
    }

    /// <summary>The time (in the caller's tick unit) the next tick is due.</summary>
    public long NextDeadline { get; private set; }

    /// <summary>
    /// Call when the work of the current tick has finished at <paramref name="nowTicks"/>. Moves the deadline one grid step forward and past
    /// every grid point that is already due. Returns how many grid points were missed (0 when on time).
    /// </summary>
    public int Advance(long nowTicks)
    {
        NextDeadline += _period;
        if (NextDeadline > nowTicks)
        {
            return 0;
        }

        var missed = (nowTicks - NextDeadline) / _period + 1;
        NextDeadline += missed * _period;
        return (int)Math.Min(missed, int.MaxValue);
    }

    /// <summary>The period in milliseconds for a rate in hertz; the sampler runs at 1 to 10 Hz (spec section 7.1).</summary>
    public static double PeriodMilliseconds(double hertz)
    {
        if (!double.IsFinite(hertz) || hertz < MinHertz || hertz > MaxHertz)
        {
            throw new ArgumentOutOfRangeException(nameof(hertz), hertz, $"The sampling rate must be between {MinHertz} and {MaxHertz} Hz.");
        }

        return 1000.0 / hertz;
    }
}
