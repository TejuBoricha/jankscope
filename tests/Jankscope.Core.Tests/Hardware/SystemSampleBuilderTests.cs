using System.Globalization;
using Jankscope.Core.Hardware;

namespace Jankscope.Core.Tests.Hardware;

public class SystemSampleBuilderTests
{
    private static readonly Luid Igpu = new(0, 0x11B0C);
    private static readonly Luid Dgpu = new(0, 0x11FD6);
    private static readonly Luid Soft = new(0, 0x11FA1);
    private static readonly DateTimeOffset Utc = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

    private static InstanceReading I(string instance, double value) => new(instance, Reading.Of(value));

    private static CounterResult Of(params InstanceReading[] items) => CounterResult.Of(items);

    private static CounterResult Single(double value) => Of(I("", value));

    private static CounterSnapshot Snapshot(params (string Id, CounterResult Result)[] items) =>
        new(items.Select(i => new KeyValuePair<string, CounterResult>(i.Id, i.Result)));

    private static SystemSample Build(CounterSnapshot snapshot, IReadOnlyCollection<Luid>? adapters = null) =>
        SystemSampleBuilder.Build(7, 123456789, Utc, snapshot, adapters ?? []);

    private static string EngineName(uint luidLow, uint eng, string type, uint pid = 1000) =>
        $"pid_{pid}_luid_0x00000000_0x{luidLow:X8}_phys_0_eng_{eng}_engtype_{type}";

    private static (string Id, CounterResult Result)[] Healthy16Core()
    {
        var cores = Enumerable.Range(0, 16).Select(i => I($"0,{i}", 10 + i)).Concat([I("0,_Total", 20), I("_Total", 17.5)]).ToArray();
        var freq = Enumerable.Range(0, 16).Select(i => I($"0,{i}", 2000 + 100 * i)).Concat([I("0,_Total", 2700), I("_Total", 2650)]).ToArray();
        return
        [
            (CounterIds.CpuUtility, Of(cores)),
            (CounterIds.CpuActualFrequency, Of(freq)),
            (CounterIds.MemoryAvailableBytes, Single(8_000_000_000)),
            (CounterIds.MemoryCommittedBytes, Single(12_000_000_000)),
            (CounterIds.MemoryCommitLimitBytes, Single(40_000_000_000)),
            (CounterIds.HardFaultsPerSecond, Single(3.5)),
            (CounterIds.DiskSecondsPerTransfer, Of(I("0 C:", 0.0004), I("1 T:", 0.002), I("_Total", 0.0007))),
            (CounterIds.DiskQueueLength, Of(I("0 C:", 0), I("1 T:", 2), I("_Total", 2))),
            (CounterIds.GpuEngineUtilization, Of(
                I(EngineName(0x11B0C, 0, "3D"), 12), I(EngineName(0x11B0C, 0, "3D", pid: 2000), 3), I(EngineName(0x11FD6, 0, "3D"), 80), I(EngineName(0x11FD6, 1, "Copy"), 5))),
            (CounterIds.GpuDedicatedUsage, Of(I("luid_0x00000000_0x00011B0C_phys_0", 100_000_000), I("luid_0x00000000_0x00011FD6_phys_0", 2_000_000_000))),
            (CounterIds.GpuSharedUsage, Of(I("luid_0x00000000_0x00011B0C_phys_0", 300_000_000), I("luid_0x00000000_0x00011FD6_phys_0", 10_000_000))),
        ];
    }

    [Fact]
    public void A_healthy_snapshot_is_mapped_field_by_field_and_carries_sequence_and_time()
    {
        var s = Build(Snapshot(Healthy16Core()), [Igpu, Dgpu]);

        Assert.Equal(7, s.Sequence);
        Assert.Equal(123456789, s.QpcTicks);
        Assert.Equal(Utc, s.UtcTime);
        Assert.Equal(Reading.Of(17.5), s.CpuTotalUtility);
        Assert.Equal(Reading.Of(2650), s.CpuActualFrequencyMhz);
        Assert.Equal(Reading.Of(8_000_000_000), s.MemoryAvailableBytes);
        Assert.Equal(Reading.Of(12_000_000_000), s.MemoryCommittedBytes);
        Assert.Equal(Reading.Of(40_000_000_000), s.MemoryCommitLimitBytes);
        Assert.Equal(Reading.Of(3.5), s.HardFaultsPerSecond);
        Assert.Null(s.CoresUnavailableReason);
        Assert.Null(s.DisksUnavailableReason);
        Assert.Null(s.GpusUnavailableReason);
    }

    [Fact]
    public void Per_core_rows_exclude_the_total_instances_and_are_ordered_numerically()
    {
        var s = Build(Snapshot(Healthy16Core()));
        Assert.Equal(16, s.Cores.Count);
        Assert.Equal(Enumerable.Range(0, 16).ToArray(), s.Cores.Select(c => c.Index).ToArray());
        Assert.All(s.Cores, c => Assert.Equal(0, c.Node));
        Assert.Equal(Reading.Of(21), s.Cores[11].Utility);
        Assert.Equal(Reading.Of(3100), s.Cores[11].ActualFrequencyMhz);
    }

    [Fact]
    public void Core_ten_sorts_after_core_nine_not_after_core_one()
    {
        var s = Build(Snapshot((CounterIds.CpuUtility, Of(I("0,10", 1), I("0,9", 2), I("0,1", 3), I("1,0", 4), I("_Total", 5)))));
        Assert.Equal(["0,1", "0,9", "0,10", "1,0"], s.Cores.Select(c => c.Instance).ToArray());
    }

    [Fact]
    public void Utility_above_one_hundred_percent_is_kept_because_the_counter_is_relative_to_the_base_frequency()
    {
        var s = Build(Snapshot((CounterIds.CpuUtility, Of(I("0,0", 187.3), I("_Total", 150.25)))));
        Assert.Equal(Reading.Of(187.3), s.Cores.Single().Utility);
        Assert.Equal(Reading.Of(150.25), s.CpuTotalUtility);
    }

    [Fact]
    public void An_unrecognised_processor_instance_is_noted_and_skipped_not_guessed()
    {
        var s = Build(Snapshot((CounterIds.CpuUtility, Of(I("0,0", 1), I("weird name", 2), I("_Total", 3)))));
        Assert.Single(s.Cores);
        Assert.Contains(s.Notes, n => n.Contains("weird name", StringComparison.Ordinal));
    }

    [Fact]
    public void A_core_missing_from_the_frequency_counter_gets_an_unavailable_frequency_not_zero()
    {
        var s = Build(Snapshot((CounterIds.CpuUtility, Of(I("0,0", 1), I("0,1", 2))), (CounterIds.CpuActualFrequency, Of(I("0,0", 3000)))));
        Assert.Equal(Reading.Of(3000), s.Cores[0].ActualFrequencyMhz);
        Assert.False(s.Cores[1].ActualFrequencyMhz.IsAvailable);
        Assert.False(string.IsNullOrWhiteSpace(s.Cores[1].ActualFrequencyMhz.UnavailableReason));
    }

    [Fact]
    public void An_unavailable_cpu_counter_gives_unavailable_readings_with_the_reason_and_no_core_rows()
    {
        var s = Build(Snapshot((CounterIds.CpuUtility, CounterResult.Unavailable("counter object not found (PDH_CSTATUS_NO_OBJECT)"))));
        Assert.False(s.CpuTotalUtility.IsAvailable);
        Assert.Contains("PDH_CSTATUS_NO_OBJECT", s.CpuTotalUtility.UnavailableReason, StringComparison.Ordinal);
        Assert.Empty(s.Cores);
        Assert.Contains("PDH_CSTATUS_NO_OBJECT", s.CoresUnavailableReason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_counter_that_was_never_requested_is_not_available_never_zero()
    {
        var s = Build(Snapshot());
        foreach (var reading in new[] { s.CpuTotalUtility, s.CpuActualFrequencyMhz, s.MemoryAvailableBytes, s.MemoryCommittedBytes, s.MemoryCommitLimitBytes, s.HardFaultsPerSecond })
        {
            Assert.False(reading.IsAvailable);
            Assert.False(string.IsNullOrWhiteSpace(reading.UnavailableReason));
        }

        Assert.Empty(s.Cores);
        Assert.Empty(s.Disks);
        Assert.Empty(s.Gpus);
        Assert.NotNull(s.CoresUnavailableReason);
        Assert.NotNull(s.DisksUnavailableReason);
        Assert.NotNull(s.GpusUnavailableReason);
    }

    [Fact]
    public void A_single_instance_counter_that_returns_two_instances_is_not_available_instead_of_picking_one()
    {
        var s = Build(Snapshot((CounterIds.MemoryAvailableBytes, Of(I("a", 1), I("b", 2)))));
        Assert.False(s.MemoryAvailableBytes.IsAvailable);
        Assert.Contains("2", s.MemoryAvailableBytes.UnavailableReason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_single_instance_counter_with_no_instance_is_not_available()
    {
        var s = Build(Snapshot((CounterIds.HardFaultsPerSecond, Of())));
        Assert.False(s.HardFaultsPerSecond.IsAvailable);
    }

    [Fact]
    public void An_unavailable_instance_value_stays_unavailable_with_its_own_reason()
    {
        var items = new[] { new InstanceReading("", Reading.NotAvailable("needs a second sample (PDH_CALC_NEGATIVE_DENOMINATOR)")) };
        var s = Build(Snapshot((CounterIds.HardFaultsPerSecond, CounterResult.Of(items))));
        Assert.False(s.HardFaultsPerSecond.IsAvailable);
        Assert.Contains("second sample", s.HardFaultsPerSecond.UnavailableReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Disks_join_latency_and_queue_by_instance_and_skip_the_total()
    {
        var s = Build(Snapshot(Healthy16Core()));
        Assert.Equal(["0 C:", "1 T:"], s.Disks.Select(d => d.Instance).ToArray());
        Assert.Equal(Reading.Of(0.0004), s.Disks[0].SecondsPerTransfer);
        Assert.Equal(Reading.Of(2), s.Disks[1].QueueLength);
    }

    [Fact]
    public void A_disk_missing_from_one_of_the_counters_is_unavailable_on_that_side_only()
    {
        var s = Build(Snapshot((CounterIds.DiskSecondsPerTransfer, Of(I("0 C:", 0.001))), (CounterIds.DiskQueueLength, Of(I("0 C:", 1), I("1 D:", 3)))));
        Assert.Equal(2, s.Disks.Count);
        Assert.Equal(Reading.Of(0.001), s.Disks[0].SecondsPerTransfer);
        Assert.False(s.Disks[1].SecondsPerTransfer.IsAvailable);
        Assert.Equal(Reading.Of(3), s.Disks[1].QueueLength);
    }

    [Fact]
    public void Disk_counters_both_unavailable_give_no_rows_and_one_reason()
    {
        var s = Build(Snapshot(
            (CounterIds.DiskSecondsPerTransfer, CounterResult.Unavailable("no PhysicalDisk object")),
            (CounterIds.DiskQueueLength, CounterResult.Unavailable("no PhysicalDisk object"))));
        Assert.Empty(s.Disks);
        Assert.Contains("no PhysicalDisk object", s.DisksUnavailableReason, StringComparison.Ordinal);
    }

    // ---- GPU ----

    [Fact]
    public void Gpu_engines_are_aggregated_per_luid_and_memory_is_joined_by_luid()
    {
        var s = Build(Snapshot(Healthy16Core()), [Igpu, Dgpu]);
        Assert.Equal([Igpu, Dgpu], s.Gpus.Select(g => g.Luid).ToArray());

        var igpu = s.Gpus[0];
        var threeD = Assert.Single(igpu.Engines.ByEngineType);
        Assert.Equal(15.0, threeD.SummedPercent.Value);   // 12 + 3: two processes on one engine
        Assert.Equal(Reading.Of(100_000_000), igpu.DedicatedUsageBytes);
        Assert.Equal(Reading.Of(300_000_000), igpu.SharedUsageBytes);

        var dgpu = s.Gpus[1];
        Assert.Equal(2, dgpu.Engines.ByEngineType.Count);
        Assert.Equal(Reading.Of(2_000_000_000), dgpu.DedicatedUsageBytes);
    }

    [Fact]
    public void A_known_adapter_without_any_counter_instance_is_not_available_with_reasons_and_never_zero()
    {
        var s = Build(Snapshot(Healthy16Core()), [Igpu, Dgpu, Soft]);
        var soft = s.Gpus.Single(g => g.Luid == Soft);
        Assert.NotNull(soft.Engines.NotAvailableReason);
        Assert.Empty(soft.Engines.ByEngineType);
        Assert.False(soft.DedicatedUsageBytes.IsAvailable);
        Assert.False(soft.SharedUsageBytes.IsAvailable);
        Assert.False(string.IsNullOrWhiteSpace(soft.DedicatedUsageBytes.UnavailableReason));
    }

    [Fact]
    public void An_adapter_seen_only_in_the_counters_is_still_reported()
    {
        var s = Build(Snapshot(Healthy16Core()), []);
        Assert.Equal(2, s.Gpus.Count);
    }

    [Fact]
    public void Without_gpu_counters_every_known_adapter_says_why_and_the_sample_is_otherwise_intact()
    {
        var snap = Snapshot(Healthy16Core().Where(c => !c.Id.StartsWith("gpu.", StringComparison.Ordinal))
            .Concat([
                (CounterIds.GpuEngineUtilization, CounterResult.Unavailable("GPU Engine counters are not present on this Windows")),
                (CounterIds.GpuDedicatedUsage, CounterResult.Unavailable("GPU Adapter Memory counters are not present on this Windows")),
                (CounterIds.GpuSharedUsage, CounterResult.Unavailable("GPU Adapter Memory counters are not present on this Windows")),
            ]).ToArray());
        var s = Build(snap, [Igpu, Dgpu]);

        Assert.Equal(2, s.Gpus.Count);
        Assert.All(s.Gpus, g =>
        {
            Assert.Contains("GPU Engine counters are not present", g.Engines.NotAvailableReason, StringComparison.Ordinal);
            Assert.False(g.DedicatedUsageBytes.IsAvailable);
            Assert.Contains("GPU Adapter Memory", g.DedicatedUsageBytes.UnavailableReason, StringComparison.Ordinal);
        });
        Assert.NotNull(s.GpusUnavailableReason);
        Assert.Equal(Reading.Of(17.5), s.CpuTotalUtility);
    }

    [Fact]
    public void Without_gpu_counters_and_without_known_adapters_the_list_is_empty_with_one_reason()
    {
        var s = Build(Snapshot(
            (CounterIds.GpuEngineUtilization, CounterResult.Unavailable("no GPU counters")),
            (CounterIds.GpuDedicatedUsage, CounterResult.Unavailable("no GPU counters")),
            (CounterIds.GpuSharedUsage, CounterResult.Unavailable("no GPU counters"))));
        Assert.Empty(s.Gpus);
        Assert.Contains("no GPU counters", s.GpusUnavailableReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Engine_counters_present_but_memory_counters_missing_keeps_the_engines()
    {
        var s = Build(Snapshot(
            (CounterIds.GpuEngineUtilization, Of(I(EngineName(0x11B0C, 0, "3D"), 9))),
            (CounterIds.GpuDedicatedUsage, CounterResult.Unavailable("memory counters missing")),
            (CounterIds.GpuSharedUsage, CounterResult.Unavailable("memory counters missing"))), [Igpu]);
        var gpu = Assert.Single(s.Gpus);
        Assert.Equal(9.0, Assert.Single(gpu.Engines.ByEngineType).SummedPercent.Value);
        Assert.False(gpu.DedicatedUsageBytes.IsAvailable);
        Assert.Null(s.GpusUnavailableReason);
    }

    [Fact]
    public void Memory_of_one_adapter_over_several_phys_nodes_is_added_up()
    {
        var s = Build(Snapshot((CounterIds.GpuDedicatedUsage, Of(I("luid_0x00000000_0x00011B0C_phys_0", 100), I("luid_0x00000000_0x00011B0C_phys_1", 50)))), [Igpu]);
        Assert.Equal(Reading.Of(150), s.Gpus.Single().DedicatedUsageBytes);
    }

    [Fact]
    public void An_unparsable_gpu_instance_is_noted_and_does_not_disturb_the_others()
    {
        var s = Build(Snapshot((CounterIds.GpuEngineUtilization, Of(I("garbage", 99), I(EngineName(0x11B0C, 0, "3D"), 9)))), [Igpu]);
        Assert.Equal(9.0, Assert.Single(s.Gpus.Single().Engines.ByEngineType).SummedPercent.Value);
        Assert.Contains(s.Notes, n => n.Contains("garbage", StringComparison.Ordinal));
    }

    [Fact]
    public void The_observed_reference_bench_instances_build_a_three_adapter_sample()
    {
        var names = HardwareFixtures.Lines("gpu-engine-instances.observed-hybrid.txt");
        var memory = HardwareFixtures.Lines("gpu-adapter-memory-instances.observed-hybrid.txt");
        var snap = Snapshot(
            (CounterIds.GpuEngineUtilization, CounterResult.Of(names.Select(n => I(n, 1)).ToList())),
            (CounterIds.GpuDedicatedUsage, CounterResult.Of(memory.Select(n => I(n, 1000)).ToList())),
            (CounterIds.GpuSharedUsage, CounterResult.Of(memory.Select(n => I(n, 2000)).ToList())));

        var s = Build(snap, [Igpu, Dgpu, Soft]);

        Assert.Equal(3, s.Gpus.Count);
        Assert.All(s.Gpus, g =>
        {
            Assert.Null(g.Engines.NotAvailableReason);
            Assert.Equal(Reading.Of(1000), g.DedicatedUsageBytes);
            Assert.Equal(Reading.Of(2000), g.SharedUsageBytes);
        });
        Assert.Empty(s.Notes);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("tr-TR")]
    public void Building_does_not_depend_on_the_thread_culture(string culture)
    {
        var expected = Build(Snapshot(Healthy16Core()), [Igpu, Dgpu]);
        var old = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo(culture);
            var actual = Build(Snapshot(Healthy16Core()), [Igpu, Dgpu]);
            Assert.Equal(expected.CpuTotalUtility, actual.CpuTotalUtility);
            Assert.Equal(expected.Cores.Count, actual.Cores.Count);
            Assert.Equal(expected.Cores[3].Utility, actual.Cores[3].Utility);
            Assert.Equal(expected.Disks.Count, actual.Disks.Count);
            Assert.Equal(expected.Gpus.Count, actual.Gpus.Count);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = old;
        }
    }

    [Fact]
    public void Machines_with_two_or_four_logical_processors_build_the_same_shape()
    {
        foreach (var n in new[] { 2, 4 })
        {
            var cores = Enumerable.Range(0, n).Select(i => I($"0,{i}", 5)).Concat([I("0,_Total", 5), I("_Total", 5)]).ToArray();
            var s = Build(Snapshot((CounterIds.CpuUtility, Of(cores))));
            Assert.Equal(n, s.Cores.Count);
            Assert.Equal(Reading.Of(5), s.CpuTotalUtility);
        }
    }

    [Fact]
    public void Machines_with_several_numa_nodes_keep_the_node_in_the_row()
    {
        var s = Build(Snapshot((CounterIds.CpuUtility, Of(I("0,0", 1), I("0,1", 2), I("1,0", 3), I("1,1", 4), I("0,_Total", 1.5), I("1,_Total", 3.5), I("_Total", 2.5)))));
        Assert.Equal([(0, 0), (0, 1), (1, 0), (1, 1)], s.Cores.Select(c => (c.Node, c.Index)).ToArray());
    }
}
