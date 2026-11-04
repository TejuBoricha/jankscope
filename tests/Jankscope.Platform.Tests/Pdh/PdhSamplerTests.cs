using System.Collections.Concurrent;
using System.Diagnostics;
using Jankscope.Core.Hardware;
using Jankscope.Platform.Pdh;
using Xunit.Abstractions;

namespace Jankscope.Platform.Tests.Pdh;

/// <summary>Timer-sensitive tests run alone so they are not measuring each other.</summary>
[CollectionDefinition("PdhTiming", DisableParallelization = true)]
public sealed class PdhTimingCollection;

internal sealed class FakeCounterSource(Func<int, CounterSnapshot> onCollect) : ICounterSource
{
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    public bool Disposed { get; private set; }

    public CounterSnapshot Collect() => onCollect(Interlocked.Increment(ref _calls));

    public void Dispose() => Disposed = true;

    public static CounterSnapshot Snapshot(double total = 10) => new(
    [
        new(CounterIds.CpuUtility, CounterResult.Of([new InstanceReading("0,0", Reading.Of(total)), new InstanceReading("_Total", Reading.Of(total))])),
        new(CounterIds.MemoryAvailableBytes, CounterResult.Of([new InstanceReading("", Reading.Of(1_000_000))])),
    ]);
}

[Collection("PdhTiming")]
public class PdhSamplerTests(ITestOutputHelper output)
{
    private sealed class Collected
    {
        public ConcurrentQueue<SystemSample> Samples { get; } = new();

        public void Add(SystemSample sample) => Samples.Enqueue(sample);
    }

    private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    /// <summary>The ticks a sampler delivered plus the grid points it skipped must account for the time that passed: that is "no accumulated drift".</summary>
    private void AssertOnTheGrid(IReadOnlyList<SystemSample> samples, SamplerStats stats, double periodMs)
    {
        Assert.True(samples.Count >= 5, $"only {samples.Count} samples");
        var elapsedMs = Ms(samples[^1].QpcTicks - samples[0].QpcTicks);
        var gridPoints = samples.Count - 1 + stats.MissedTicks + stats.FailedCollections;
        var driftMs = elapsedMs - gridPoints * periodMs;
        output.WriteLine($"samples={samples.Count} missed={stats.MissedTicks} elapsed={elapsedMs:F1} ms grid={gridPoints * periodMs:F1} ms drift={driftMs:F1} ms mean-cost={stats.MeanCollectMilliseconds:F2} ms max-late={stats.MaxLatenessMilliseconds:F1} ms");
        Assert.InRange(driftMs, -periodMs, periodMs);
    }

    [Fact]
    public void Samples_arrive_in_order_with_contiguous_sequence_numbers_and_increasing_time()
    {
        var fake = new FakeCounterSource(_ => FakeCounterSource.Snapshot());
        var collected = new Collected();
        using var sampler = new PdhSampler(fake, new SamplerOptions(10), collected.Add);

        sampler.Start();
        Thread.Sleep(1200);
        sampler.Stop();

        var samples = collected.Samples.ToList();
        Assert.True(samples.Count >= 8, $"only {samples.Count} samples in 1.2 s at 10 Hz");
        Assert.Equal(Enumerable.Range(0, samples.Count).Select(i => (long)i).ToArray(), samples.Select(s => s.Sequence).ToArray());
        for (var i = 1; i < samples.Count; i++)
        {
            Assert.True(samples[i].QpcTicks > samples[i - 1].QpcTicks);
            Assert.True(samples[i].UtcTime >= samples[i - 1].UtcTime);
        }

        Assert.InRange((DateTimeOffset.UtcNow - samples[^1].UtcTime).TotalSeconds, 0, 5);
        Assert.Equal(Reading.Of(10), samples[0].CpuTotalUtility);
    }

    [Fact]
    public void The_priming_collection_is_made_but_not_delivered()
    {
        var fake = new FakeCounterSource(_ => FakeCounterSource.Snapshot());
        var collected = new Collected();
        using var sampler = new PdhSampler(fake, new SamplerOptions(10), collected.Add);

        sampler.Start();
        Thread.Sleep(550);
        sampler.Stop();

        var stats = sampler.GetStats();
        Assert.Equal(stats.Ticks + 1, fake.Calls);          // one extra call: the priming collection
        Assert.Equal(stats.Delivered, collected.Samples.Count);
        Assert.Equal(0, collected.Samples.First().Sequence);
    }

    [Fact]
    public void A_steady_ten_hertz_run_does_not_drift()
    {
        var fake = new FakeCounterSource(_ => FakeCounterSource.Snapshot());
        var collected = new Collected();
        using var sampler = new PdhSampler(fake, new SamplerOptions(10), collected.Add);

        sampler.Start();
        Thread.Sleep(3200);
        sampler.Stop();

        AssertOnTheGrid(collected.Samples.ToList(), sampler.GetStats(), 100.0);
    }

    [Fact]
    public void A_costly_collection_of_thirty_milliseconds_per_tick_still_does_not_accumulate_drift()
    {
        var fake = new FakeCounterSource(_ =>
        {
            Thread.Sleep(30);
            return FakeCounterSource.Snapshot();
        });
        var collected = new Collected();
        using var sampler = new PdhSampler(fake, new SamplerOptions(10), collected.Add);

        sampler.Start();
        Thread.Sleep(3200);
        sampler.Stop();

        var stats = sampler.GetStats();
        Assert.InRange(stats.MeanCollectMilliseconds, 25, 90);
        AssertOnTheGrid(collected.Samples.ToList(), stats, 100.0);
    }

    [Fact]
    public void A_tick_that_overruns_the_period_skips_grid_points_instead_of_firing_a_burst()
    {
        var fake = new FakeCounterSource(call =>
        {
            if (call % 4 == 0)
            {
                Thread.Sleep(260);
            }

            return FakeCounterSource.Snapshot();
        });
        var collected = new Collected();
        using var sampler = new PdhSampler(fake, new SamplerOptions(10), collected.Add);

        sampler.Start();
        Thread.Sleep(3500);
        sampler.Stop();

        var stats = sampler.GetStats();
        var samples = collected.Samples.ToList();
        Assert.True(stats.MissedTicks >= 2, $"missed {stats.MissedTicks}");
        for (var i = 1; i < samples.Count; i++)
        {
            // never two samples closer than most of a period (a burst would catch up on the missed ones)
            Assert.True(Ms(samples[i].QpcTicks - samples[i - 1].QpcTicks) > 60, $"gap {Ms(samples[i].QpcTicks - samples[i - 1].QpcTicks):F1} ms before sample {i}");
        }
    }

    [Fact]
    public void A_throwing_source_is_counted_and_sampling_continues_with_a_type_name_only()
    {
        var fake = new FakeCounterSource(call =>
        {
            if (call is 3 or 4)
            {
                throw new InvalidOperationException("secret path C:\\Users\\someone\\file.txt");
            }

            return FakeCounterSource.Snapshot();
        });
        var collected = new Collected();
        using var sampler = new PdhSampler(fake, new SamplerOptions(10), collected.Add);

        sampler.Start();
        Thread.Sleep(1200);
        sampler.Stop();

        var stats = sampler.GetStats();
        Assert.Equal(2, stats.FailedCollections);
        Assert.Equal("InvalidOperationException", stats.LastError);
        Assert.DoesNotContain("secret", stats.LastError, StringComparison.Ordinal);
        Assert.True(stats.Delivered >= 6, $"delivered {stats.Delivered}");
        Assert.Equal(stats.Ticks - 2, stats.Delivered);
    }

    [Fact]
    public void A_failing_priming_collection_is_counted_and_does_not_stop_the_sampler()
    {
        var fake = new FakeCounterSource(call => call == 1 ? throw new IOException("x") : FakeCounterSource.Snapshot());
        var collected = new Collected();
        using var sampler = new PdhSampler(fake, new SamplerOptions(10), collected.Add);

        sampler.Start();
        Thread.Sleep(600);
        sampler.Stop();

        Assert.Equal(1, sampler.GetStats().FailedCollections);
        Assert.NotEmpty(collected.Samples);
    }

    [Fact]
    public void A_throwing_callback_is_isolated_too()
    {
        var count = 0;
        var fake = new FakeCounterSource(_ => FakeCounterSource.Snapshot());
        using var sampler = new PdhSampler(fake, new SamplerOptions(10), _ =>
        {
            if (Interlocked.Increment(ref count) == 2)
            {
                throw new ApplicationException("callback broke");
            }
        });

        sampler.Start();
        Thread.Sleep(800);
        sampler.Stop();

        var stats = sampler.GetStats();
        Assert.Equal(1, stats.FailedCollections);
        Assert.True(stats.Delivered >= 4);
    }

    [Fact]
    public void After_stop_returns_no_further_callback_happens()
    {
        var fake = new FakeCounterSource(_ => FakeCounterSource.Snapshot());
        var collected = new Collected();
        using var sampler = new PdhSampler(fake, new SamplerOptions(10), collected.Add);

        sampler.Start();
        Thread.Sleep(400);
        sampler.Stop();
        var countAtStop = collected.Samples.Count;
        var callsAtStop = fake.Calls;
        Thread.Sleep(400);

        Assert.Equal(countAtStop, collected.Samples.Count);
        Assert.Equal(callsAtStop, fake.Calls);
        Assert.False(sampler.IsRunning);
    }

    [Fact]
    public void Stop_dispose_and_a_second_stop_are_idempotent_and_the_sampler_does_not_dispose_its_source()
    {
        var fake = new FakeCounterSource(_ => FakeCounterSource.Snapshot());
        var sampler = new PdhSampler(fake, new SamplerOptions(10), _ => { });
        sampler.Start();
        sampler.Stop();
        sampler.Stop();
        sampler.Dispose();
        sampler.Dispose();
        sampler.Stop();
        Assert.False(fake.Disposed);
        Assert.Throws<ObjectDisposedException>(sampler.Start);
    }

    [Fact]
    public void Starting_twice_is_rejected_and_stopping_a_never_started_sampler_is_harmless()
    {
        var fake = new FakeCounterSource(_ => FakeCounterSource.Snapshot());
        using var sampler = new PdhSampler(fake, new SamplerOptions(1), _ => { });
        sampler.Stop();
        sampler.Start();
        Assert.Throws<InvalidOperationException>(sampler.Start);
        sampler.Stop();
    }

    [Fact]
    public void A_sampler_can_be_restarted_after_stop()
    {
        var fake = new FakeCounterSource(_ => FakeCounterSource.Snapshot());
        var collected = new Collected();
        using var sampler = new PdhSampler(fake, new SamplerOptions(10), collected.Add);
        sampler.Start();
        Thread.Sleep(300);
        sampler.Stop();
        var first = collected.Samples.Count;
        sampler.Start();
        Thread.Sleep(300);
        sampler.Stop();
        Assert.True(collected.Samples.Count > first);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(0.0)]
    [InlineData(11.0)]
    [InlineData(double.NaN)]
    public void A_rate_outside_one_to_ten_hertz_is_rejected_at_construction(double hertz)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PdhSampler(new FakeCounterSource(_ => FakeCounterSource.Snapshot()), new SamplerOptions(hertz), _ => { }));
    }

    [Fact]
    public void The_sampler_runs_on_its_own_thread_not_on_the_thread_pool()
    {
        var threadIds = new ConcurrentBag<(int Id, bool Pool, string? Name)>();
        var fake = new FakeCounterSource(_ => FakeCounterSource.Snapshot());
        using var sampler = new PdhSampler(fake, new SamplerOptions(10), _ =>
            threadIds.Add((Environment.CurrentManagedThreadId, Thread.CurrentThread.IsThreadPoolThread, Thread.CurrentThread.Name)));
        sampler.Start();
        Thread.Sleep(500);
        sampler.Stop();

        Assert.NotEmpty(threadIds);
        Assert.Single(threadIds.Select(t => t.Id).Distinct());
        Assert.All(threadIds, t =>
        {
            Assert.False(t.Pool);
            Assert.Equal("Jankscope.PdhSampler", t.Name);
        });
    }

    [Fact]
    public void Known_adapters_reach_every_sample_even_when_the_counters_have_no_gpu_data()
    {
        var luid = new Luid(0, 0xBEEF);
        var fake = new FakeCounterSource(_ => FakeCounterSource.Snapshot());
        var collected = new Collected();
        using var sampler = new PdhSampler(fake, new SamplerOptions(10, [luid]), collected.Add);
        sampler.Start();
        Thread.Sleep(400);
        sampler.Stop();

        var sample = collected.Samples.First();
        var gpu = Assert.Single(sample.Gpus);
        Assert.Equal(luid, gpu.Luid);
        Assert.False(string.IsNullOrWhiteSpace(gpu.Engines.NotAvailableReason));
        Assert.False(gpu.DedicatedUsageBytes.IsAvailable);
    }

    // ---- the real thing ----

    [Fact]
    public void A_real_ten_hertz_run_delivers_complete_samples()
    {
        using var source = new PdhCounterSource();
        var logical = (int)NativeHardware.GetActiveProcessorCount(NativeHardware.AllProcessorGroups);
        var collected = new Collected();
        using var sampler = new PdhSampler(source, new SamplerOptions(10), collected.Add);

        sampler.Start();
        Thread.Sleep(2000);
        sampler.Stop();

        var samples = collected.Samples.ToList();
        var stats = sampler.GetStats();
        output.WriteLine($"real run: {samples.Count} samples, mean cost {stats.MeanCollectMilliseconds:F2} ms, max {stats.MaxCollectMilliseconds:F2} ms, missed {stats.MissedTicks}, failed {stats.FailedCollections}");
        Assert.True(samples.Count >= 10, $"only {samples.Count} samples in 2 s at 10 Hz");
        Assert.Equal(0, stats.FailedCollections);
        foreach (var sample in samples)
        {
            Assert.True(sample.CpuTotalUtility.IsAvailable, sample.CpuTotalUtility.UnavailableReason);
            Assert.Equal(logical, sample.Cores.Count);
            Assert.True(sample.MemoryAvailableBytes.IsAvailable);
            Assert.True(sample.HardFaultsPerSecond.IsAvailable, sample.HardFaultsPerSecond.UnavailableReason);
        }
    }

    /// <summary>
    /// Measured self-cost (spec section 14): CPU time of this process over 10 s of real sampling at 10 Hz, against 10 s of the same process
    /// idle. The numbers are printed (run with <c>--logger "console;verbosity=detailed"</c>); the assertions are loose guard rails, not the budget.
    /// </summary>
    [Fact]
    [Trait("Category", "Benchmark")]
    public void Self_cost_of_ten_seconds_of_real_sampling_at_ten_hertz_is_measured_and_modest()
    {
        using var process = Process.GetCurrentProcess();
        var logical = Environment.ProcessorCount;

        using var source = new PdhCounterSource();
        var count = 0;
        using var sampler = new PdhSampler(source, new SamplerOptions(10), _ => Interlocked.Increment(ref count));
        process.Refresh();
        var before = process.TotalProcessorTime;
        var wall = Stopwatch.StartNew();
        sampler.Start();
        Thread.Sleep(10_000);
        sampler.Stop();
        wall.Stop();
        process.Refresh();
        var cpuMs = (process.TotalProcessorTime - before).TotalMilliseconds;

        // The same process idle for 5 s afterwards (sampler stopped, JIT warm): what the test host costs by itself.
        process.Refresh();
        var idleBefore = process.TotalProcessorTime;
        Thread.Sleep(5000);
        process.Refresh();
        var idleCpu = (process.TotalProcessorTime - idleBefore).TotalMilliseconds / 5.0;

        var stats = sampler.GetStats();
        var perSecondMs = cpuMs / wall.Elapsed.TotalSeconds;
        var oneCorePercent = perSecondMs / 10.0;
        output.WriteLine($"SELF-COST over {wall.Elapsed.TotalSeconds:F1} s at 10 Hz: {count} samples, process CPU {cpuMs:F0} ms ({perSecondMs:F1} ms/s = {oneCorePercent:F2} % of one logical processor = {oneCorePercent / logical:F3} % of {logical} logical processors); same process idle afterwards {idleCpu:F1} ms/s; net sampler cost about {Math.Max(0, perSecondMs - idleCpu):F1} ms/s");
        output.WriteLine($"tick wall time: mean {stats.MeanCollectMilliseconds:F2} ms, max {stats.MaxCollectMilliseconds:F2} ms; missed ticks {stats.MissedTicks}; max lateness {stats.MaxLatenessMilliseconds:F1} ms; failures {stats.FailedCollections}");

        Assert.True(count >= 80, $"only {count} samples in 10 s");
        Assert.Equal(0, stats.FailedCollections);
        Assert.True(stats.MeanCollectMilliseconds < 100, "a tick must cost less than its 100 ms period");
        Assert.True(oneCorePercent < 50, $"sampling used {oneCorePercent:F1} % of one logical processor");
    }
}
