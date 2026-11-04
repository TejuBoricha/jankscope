namespace Jankscope.Harness.Tests;

/// <summary>
/// A virtual clock: time moves only when the code under test sleeps, spins or the test says so. Every pacing decision becomes
/// deterministic, so the pacer's rules can be proven without depending on this machine's timer resolution.
/// </summary>
internal sealed class FakeClock : IClock
{
    public long Frequency { get; init; } = 10_000_000; // the usual QPC rate: 1 tick = 0.1 microsecond

    public long Now { get; set; }

    /// <summary>How many ticks a Sleep(1) really takes (the OS rounds sleeps up to its timer tick).</summary>
    public Func<int, long> SleepTicks { get; set; } = ms => ms * 10_000L;

    public long TicksPerSpin { get; init; } = 10;

    public int SleepCalls { get; private set; }

    public long SpinCalls { get; private set; }

    public List<int> SleepArguments { get; } = [];

    public long GetTimestamp() => Now;

    public void Sleep(int milliseconds)
    {
        SleepCalls++;
        SleepArguments.Add(milliseconds);
        Now += SleepTicks(milliseconds);
    }

    public void SpinOnce()
    {
        SpinCalls++;
        Now += TicksPerSpin;
    }
}

/// <summary>A presenter that advances the fake clock by a fixed cost per Present and records what the loop asked of it.</summary>
internal sealed class FakePresenter(FakeClock clock, long presentCostTicks = 500) : IFramePresenter
{
    public List<(string Call, int Frame, long At)> Calls { get; } = [];

    public Func<int, long>? ExtraCostTicks { get; set; }

    public HashSet<int> HitchFlags { get; } = [];

    public Func<int, PresentOutcome>? Outcome { get; set; }

    private int _current = -1;

    public void Prepare(int frameIndex, bool isHitch)
    {
        _current = frameIndex;
        Calls.Add(("Prepare", frameIndex, clock.Now));
        if (isHitch)
        {
            HitchFlags.Add(frameIndex);
        }
    }

    public PresentOutcome Present()
    {
        Calls.Add(("Present", _current, clock.Now));
        clock.Now += presentCostTicks + (ExtraCostTicks?.Invoke(_current) ?? 0);
        return Outcome?.Invoke(_current) ?? PresentOutcome.Presented;
    }
}
