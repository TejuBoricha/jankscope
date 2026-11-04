using Jankscope.Frames.Incidents;

namespace Jankscope.Frames.Tests.Incidents;

public class CaptureClockMapTests
{
    private const long Frequency = 10_000_000; // QPC ticks per second on typical Windows 10/11 hardware

    private static long Ticks(double ms) => (long)Math.Round(ms * Frequency / 1000.0);

    [Fact]
    public void With_no_frames_the_mark_time_is_unknown_not_zero()
    {
        var map = new CaptureClockMap(Frequency);
        Assert.Null(map.Estimate(Ticks(1234)));
        Assert.Equal(0, map.Observations);
    }

    [Fact]
    public void A_constant_delivery_delay_gives_an_exact_mapping_and_no_uncertainty()
    {
        var map = new CaptureClockMap(Frequency);
        for (var t = 0.0; t <= 1000; t += 8)
        {
            map.Observe(Ticks(t + 50), t); // every frame reaches us 50 ms after it was presented
        }

        var estimate = map.Estimate(Ticks(2000))!.Value;
        Assert.Equal(1950.0, estimate.Ms, 3);
        Assert.Equal(0.0, estimate.UncertaintyMs, 3);
        Assert.True(estimate.Observations > 100);
    }

    [Fact]
    public void Jittery_delivery_is_reported_as_uncertainty_equal_to_the_spread()
    {
        var map = new CaptureClockMap(Frequency);
        for (var i = 0; i < 200; i++)
        {
            var t = i * 8.0;
            map.Observe(Ticks(t + (i % 2 == 0 ? 50 : 80)), t);
        }

        var estimate = map.Estimate(Ticks(5000))!.Value;
        Assert.Equal(4950.0, estimate.Ms, 3);          // the smallest delay is the best estimate of the offset
        Assert.Equal(30.0, estimate.UncertaintyMs, 3); // the spread between the smallest and a typical-worst (95th percentile) delay
    }

    [Fact]
    public void Old_observations_are_forgotten_so_the_estimate_follows_a_changed_delay()
    {
        var map = new CaptureClockMap(Frequency, windowSize: 256);
        var t = 0.0;
        for (var i = 0; i < 100; i++, t += 8)
        {
            map.Observe(Ticks(t + 10), t);
        }

        for (var i = 0; i < 1000; i++, t += 8)
        {
            map.Observe(Ticks(t + 60), t);
        }

        Assert.Equal(1940.0, map.Estimate(Ticks(2000))!.Value.Ms, 3);
        Assert.True(map.Observations <= 256);
    }

    [Fact]
    public void A_capture_clock_that_restarts_starts_a_new_epoch_and_forgets_the_old_mapping()
    {
        var map = new CaptureClockMap(Frequency);
        for (var t = 0.0; t <= 5000; t += 10)
        {
            map.Observe(Ticks(t + 20), t);
        }

        var before = map.Estimate(Ticks(6000))!.Value;
        map.Observe(Ticks(7000 + 20), 100); // the capture was restarted: its clock is near zero again
        var after = map.Estimate(Ticks(7010))!.Value;

        Assert.Equal(before.Epoch + 1, after.Epoch);
        Assert.Equal(1, after.Observations);
    }

    [Fact]
    public void A_short_run_restart_or_explicit_reset_starts_a_new_epoch()
    {
        var map = new CaptureClockMap(Frequency);
        map.Observe(Ticks(500), 480);
        map.Observe(Ticks(900), 880);

        // Sub-second restart (880 ms -> 16 ms) must still trigger a new epoch
        map.Observe(Ticks(1200), 16);
        var estimate = map.Estimate(Ticks(1200))!.Value;
        Assert.Equal(1, estimate.Epoch);
        Assert.Equal(1, estimate.Observations);

        map.Reset();
        Assert.Equal(0, map.Observations);
        Assert.Null(map.Estimate(Ticks(1300)));

        map.Observe(Ticks(1400), 16);
        Assert.Equal(2, map.Estimate(Ticks(1400))!.Value.Epoch);
    }

    [Fact]
    public void Non_finite_capture_times_are_ignored()
    {
        var map = new CaptureClockMap(Frequency);
        map.Observe(Ticks(10), double.NaN);
        map.Observe(Ticks(10), double.PositiveInfinity);
        Assert.Equal(0, map.Observations);
        Assert.Null(map.Estimate(Ticks(10)));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void A_non_positive_frequency_is_refused(long frequency)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CaptureClockMap(frequency));
    }

    [Fact]
    public async Task Concurrent_observing_and_estimating_never_throws_or_returns_nonsense()
    {
        var map = new CaptureClockMap(Frequency);
        using var stop = new CancellationTokenSource();
        var writer = Task.Run(() =>
        {
            for (var i = 0; i < 20_000 && !stop.IsCancellationRequested; i++)
            {
                map.Observe(Ticks(i + 5), i);
            }
        });

        var reads = 0;
        while (!writer.IsCompleted)
        {
            var estimate = map.Estimate(Ticks(10_000));
            if (estimate is { } e)
            {
                Assert.True(double.IsFinite(e.Ms) && e.UncertaintyMs >= 0);
            }

            reads++;
        }

        Assert.True(reads > 0);
        await writer;
    }
}
