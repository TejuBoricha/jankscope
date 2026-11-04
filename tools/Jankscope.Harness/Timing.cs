using System.Diagnostics;

namespace Jankscope.Harness;

/// <summary>
/// The clock the harness paces and timestamps with. The production implementation is <see cref="Stopwatch"/>, which on Windows is a thin
/// wrapper over QueryPerformanceCounter, so the harness needs no native import of its own. The seam exists so the pacing rules can be
/// proven on a virtual clock, independent of this machine's timer resolution.
/// </summary>
internal interface IClock
{
    /// <summary>Ticks per second of <see cref="GetTimestamp"/> (the QPC frequency).</summary>
    long Frequency { get; }

    long GetTimestamp();

    /// <summary>Gives up the CPU for at least <paramref name="milliseconds"/>; the real duration is rounded up to the OS timer tick.</summary>
    void Sleep(int milliseconds);

    /// <summary>One busy-wait step. Cheap, and never gives up the time slice.</summary>
    void SpinOnce();
}

internal sealed class StopwatchClock : IClock
{
    public long Frequency => Stopwatch.Frequency;

    public long GetTimestamp() => Stopwatch.GetTimestamp();

    public void Sleep(int milliseconds) => Thread.Sleep(milliseconds);

    public void SpinOnce() => Thread.SpinWait(4);
}

/// <summary>Conversions between clock ticks and milliseconds.</summary>
internal static class QpcTime
{
    public static double ToMs(long ticks, long frequency) => ticks * 1000.0 / frequency;

    public static long FromMs(double milliseconds, long frequency) =>
        (long)Math.Round(milliseconds * frequency / 1000.0, MidpointRounding.AwayFromZero);
}
