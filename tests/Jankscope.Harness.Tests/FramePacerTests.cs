namespace Jankscope.Harness.Tests;

public class FramePacerTests
{
    private const long Ms = 10_000; // ticks per millisecond at the fake 10 MHz clock

    [Fact]
    public void Sleeps_while_far_from_the_target_then_spins_the_last_margin()
    {
        var clock = new FakeClock { Now = 0 };
        var pacer = new FramePacer(clock, 2 * Ms);

        var target = 69_440L; // 6.944 ms: the 144 fps frame time
        var onTime = pacer.WaitUntil(target);

        Assert.True(onTime);
        // 6.944 -> 5.944 -> 4.944 -> 3.944 -> 2.944 -> 1.944 ms left (inside the 2 ms margin): five one-millisecond sleeps, then the spin.
        Assert.Equal(5, clock.SleepCalls);
        Assert.All(clock.SleepArguments, ms => Assert.Equal(1, ms));
        // The remaining 1.944 ms (19 440 ticks) are spun in 10-tick steps: exactly 1 944 steps, landing on the target.
        Assert.Equal(1_944, clock.SpinCalls);
        Assert.Equal(target, clock.Now);
    }

    [Fact]
    public void Never_sleeps_once_inside_the_margin()
    {
        var clock = new FakeClock { Now = 0 };
        var pacer = new FramePacer(clock, 2 * Ms);

        pacer.WaitUntil(15_000); // 1.5 ms away, margin is 2 ms

        Assert.Equal(0, clock.SleepCalls);
        Assert.True(clock.Now >= 15_000);
    }

    [Fact]
    public void Sleeps_exactly_down_to_the_margin_boundary_and_not_below_it()
    {
        var clock = new FakeClock { Now = 0 };
        var pacer = new FramePacer(clock, 2 * Ms);

        pacer.WaitUntil(3 * Ms); // 3 ms away: one sleep leaves exactly 2 ms, which is not above the margin

        Assert.Equal(1, clock.SleepCalls);
    }

    [Fact]
    public void A_target_already_in_the_past_returns_at_once_and_counts_as_late()
    {
        var clock = new FakeClock { Now = 100_000 };
        var pacer = new FramePacer(clock, 2 * Ms);

        var onTime = pacer.WaitUntil(90_000);

        Assert.False(onTime);
        Assert.Equal(1, pacer.LateArrivals);
        Assert.Equal(0, clock.SleepCalls);
        Assert.Equal(0, clock.SpinCalls);
        Assert.Equal(100_000, clock.Now);
    }

    [Fact]
    public void A_sleep_that_carries_past_the_target_is_late_and_raises_the_margin()
    {
        // The OS rounds Sleep(1) up to 4 ms; the 2 ms margin assumed less. The target is 2.5 ms away: the sleep overshoots it.
        var clock = new FakeClock { Now = 0, SleepTicks = _ => 4 * Ms };
        var pacer = new FramePacer(clock, 2 * Ms);

        var onTime = pacer.WaitUntil(25_000);

        Assert.False(onTime);
        Assert.Equal(1, pacer.LateArrivals);
        Assert.Equal(1, pacer.Sleeps);
        Assert.True(pacer.MarginTicks >= 4 * Ms + FramePacer.SlackTicks(clock.Frequency), $"margin {pacer.MarginTicks}");

        // With the raised margin the next wait spins instead of sleeping.
        clock.Now = 100_000;
        clock.SleepArguments.Clear();
        Assert.True(pacer.WaitUntil(100_000 + 6 * Ms));
        Assert.Single(clock.SleepArguments); // 6 ms away: one sleep leaves 2 ms, inside the raised 4.5 ms margin, so the rest is spun
    }

    [Fact]
    public void The_margin_never_shrinks()
    {
        var clock = new FakeClock { Now = 0, SleepTicks = _ => Ms };
        var pacer = new FramePacer(clock, 5 * Ms);
        var before = pacer.MarginTicks;

        for (var i = 1; i <= 20; i++)
        {
            pacer.WaitUntil(clock.Now + 8 * Ms);
        }

        Assert.True(pacer.MarginTicks >= before);
    }

    [Fact]
    public void Calibration_takes_the_slowest_sleep_plus_half_a_millisecond()
    {
        var elapsed = new Queue<long>([10_500, 12_000, 11_000, 10_900]);
        var clock = new FakeClock { SleepTicks = _ => elapsed.Count > 0 ? elapsed.Dequeue() : 10_000 };

        var margin = FramePacer.CalibrateMarginTicks(clock, samples: 4);

        Assert.Equal(12_000 + 5_000, margin);
        Assert.Equal(4, clock.SleepCalls);
    }

    [Fact]
    public void Calibration_is_clamped_to_between_one_and_twenty_five_milliseconds()
    {
        var instant = new FakeClock { SleepTicks = _ => 0 };
        Assert.Equal(1 * Ms, FramePacer.CalibrateMarginTicks(instant, samples: 3));

        var coarse = new FakeClock { SleepTicks = _ => 400_000 }; // a 40 ms sleep: slower than the 25 ms ceiling
        Assert.Equal(coarse.Frequency / 1000 * 25, FramePacer.CalibrateMarginTicks(coarse, samples: 3));
    }

    [Fact]
    public void A_coarse_timer_means_pure_spinning_for_a_short_frame()
    {
        // With a 15.6 ms tick the calibrated margin is 16.1 ms, longer than a 6.9 ms frame: the pacer must not sleep at all.
        var clock = new FakeClock { SleepTicks = _ => 156_000 };
        var pacer = new FramePacer(clock, FramePacer.CalibrateMarginTicks(clock, samples: 3));
        clock.SleepArguments.Clear();
        var sleepsBefore = clock.SleepCalls;

        pacer.WaitUntil(clock.Now + 69_440);

        Assert.Equal(sleepsBefore, clock.SleepCalls);
    }

    [Fact]
    public void Invalid_arguments_are_rejected()
    {
        var clock = new FakeClock();
        Assert.Throws<ArgumentOutOfRangeException>(() => new FramePacer(clock, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => FramePacer.CalibrateMarginTicks(clock, samples: 0));
    }
}
