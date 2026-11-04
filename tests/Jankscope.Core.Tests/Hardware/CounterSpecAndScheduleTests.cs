using Jankscope.Core.Hardware;

namespace Jankscope.Core.Tests.Hardware;

public class CounterSpecTests
{
    [Fact]
    public void The_required_signals_have_their_english_paths()
    {
        var paths = CounterSpecs.All.ToDictionary(s => s.Id, s => s.EnglishPath);
        Assert.Equal(@"\Processor Information(*)\% Processor Utility", paths[CounterIds.CpuUtility]);
        Assert.Equal(@"\Processor Information(*)\Actual Frequency", paths[CounterIds.CpuActualFrequency]);
        Assert.Equal(@"\Memory\Available Bytes", paths[CounterIds.MemoryAvailableBytes]);
        Assert.Equal(@"\Memory\Committed Bytes", paths[CounterIds.MemoryCommittedBytes]);
        Assert.Equal(@"\Memory\Commit Limit", paths[CounterIds.MemoryCommitLimitBytes]);
        Assert.Equal(@"\Memory\Pages Input/sec", paths[CounterIds.HardFaultsPerSecond]);
        Assert.Equal(@"\PhysicalDisk(*)\Avg. Disk sec/Transfer", paths[CounterIds.DiskSecondsPerTransfer]);
        Assert.Equal(@"\PhysicalDisk(*)\Current Disk Queue Length", paths[CounterIds.DiskQueueLength]);
        Assert.Equal(@"\GPU Engine(*)\Utilization Percentage", paths[CounterIds.GpuEngineUtilization]);
        Assert.Equal(@"\GPU Adapter Memory(*)\Dedicated Usage", paths[CounterIds.GpuDedicatedUsage]);
        Assert.Equal(@"\GPU Adapter Memory(*)\Shared Usage", paths[CounterIds.GpuSharedUsage]);
    }

    [Fact]
    public void Ids_and_paths_are_unique_ascii_and_shaped_like_counter_paths()
    {
        Assert.Equal(CounterSpecs.All.Count, CounterSpecs.All.Select(s => s.Id).Distinct().Count());
        Assert.Equal(CounterSpecs.All.Count, CounterSpecs.All.Select(s => s.EnglishPath).Distinct().Count());
        foreach (var spec in CounterSpecs.All)
        {
            Assert.StartsWith("\\", spec.EnglishPath, StringComparison.Ordinal);
            Assert.True(spec.EnglishPath.All(c => c is >= ' ' and <= '~'), spec.EnglishPath);
            Assert.Equal(spec.EnglishPath.Contains("(*)", StringComparison.Ordinal), spec.Wildcard);
        }
    }

    [Fact]
    public void Every_counter_id_the_builder_reads_has_a_spec_and_every_spec_has_an_id_constant()
    {
        string[] ids =
        [
            CounterIds.CpuUtility, CounterIds.CpuActualFrequency, CounterIds.MemoryAvailableBytes, CounterIds.MemoryCommittedBytes,
            CounterIds.MemoryCommitLimitBytes, CounterIds.HardFaultsPerSecond, CounterIds.DiskSecondsPerTransfer, CounterIds.DiskQueueLength,
            CounterIds.GpuEngineUtilization, CounterIds.GpuDedicatedUsage, CounterIds.GpuSharedUsage,
        ];
        Assert.Equal(ids.Order(StringComparer.Ordinal).ToArray(), CounterSpecs.All.Select(s => s.Id).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Processor_utility_is_not_capped_at_one_hundred_percent()
    {
        Assert.True(CounterSpecs.All.Single(s => s.Id == CounterIds.CpuUtility).NoCap100);
    }

    [Fact]
    public void A_counter_result_cannot_be_both_available_and_unavailable()
    {
        Assert.Throws<ArgumentException>(() => CounterResult.Unavailable(" "));
        var ok = CounterResult.Of([new InstanceReading("a", Reading.Of(1))]);
        Assert.Null(ok.UnavailableReason);
        Assert.Single(ok.Instances);
        var bad = CounterResult.Unavailable("x");
        Assert.Empty(bad.Instances);
        Assert.Equal("x", bad.UnavailableReason);
    }
}

public class SamplerScheduleTests
{
    private const long Period = 1_000; // ticks; the unit is irrelevant to the arithmetic

    [Fact]
    public void The_first_deadline_is_one_period_after_the_start()
    {
        Assert.Equal(5_000 + Period, new SamplerSchedule(5_000, Period).NextDeadline);
    }

    [Fact]
    public void Finishing_on_time_advances_by_exactly_one_period_regardless_of_how_long_the_work_took()
    {
        var s = new SamplerSchedule(0, Period);
        Assert.Equal(0, s.Advance(nowTicks: 1_030));  // the tick due at 1_000 finished at 1_030
        Assert.Equal(2 * Period, s.NextDeadline);
        Assert.Equal(0, s.Advance(nowTicks: 2_000 + 450));
        Assert.Equal(3 * Period, s.NextDeadline);
    }

    [Fact]
    public void Work_that_takes_longer_than_a_period_skips_the_missed_ticks_and_stays_on_the_grid()
    {
        var s = new SamplerSchedule(0, Period);
        // the tick due at 1_000 finished at 3_250: the ticks due at 2_000 and 3_000 are missed
        Assert.Equal(2, s.Advance(nowTicks: 3_250));
        Assert.Equal(4 * Period, s.NextDeadline);
        Assert.Equal(0, (s.NextDeadline - 0) % Period);
    }

    [Fact]
    public void Finishing_exactly_on_the_next_deadline_counts_it_as_missed()
    {
        var s = new SamplerSchedule(0, Period);
        Assert.Equal(1, s.Advance(nowTicks: 2_000));
        Assert.Equal(3 * Period, s.NextDeadline);
    }

    [Fact]
    public void Many_ticks_do_not_accumulate_drift_even_when_every_tick_costs_a_fifth_of_a_period()
    {
        var s = new SamplerSchedule(0, Period);
        long deadline = s.NextDeadline;
        for (var i = 1; i <= 1000; i++)
        {
            Assert.Equal(i * Period, deadline);
            var now = deadline + 200; // woke on time, work took 200
            Assert.Equal(0, s.Advance(now));
            deadline = s.NextDeadline;
        }
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void A_non_positive_period_is_rejected(long period)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SamplerSchedule(0, period));
    }

    [Fact]
    public void A_clock_that_moves_backwards_does_not_move_the_deadline_backwards()
    {
        var s = new SamplerSchedule(10_000, Period);
        Assert.Equal(0, s.Advance(nowTicks: 5_000));
        Assert.Equal(10_000 + 2 * Period, s.NextDeadline);
    }

    [Theory]
    [InlineData(1.0, 1_000.0)]
    [InlineData(10.0, 100.0)]
    [InlineData(2.0, 500.0)]
    public void The_period_for_a_rate_in_hertz_is_exact_in_milliseconds(double hertz, double milliseconds)
    {
        Assert.Equal(milliseconds, SamplerSchedule.PeriodMilliseconds(hertz));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.99)]
    [InlineData(10.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Rates_outside_one_to_ten_hertz_are_rejected(double hertz)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SamplerSchedule.PeriodMilliseconds(hertz));
    }
}
