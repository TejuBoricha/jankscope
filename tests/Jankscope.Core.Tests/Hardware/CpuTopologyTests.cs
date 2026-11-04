using System.Buffers.Binary;
using System.Globalization;
using Jankscope.Core.Hardware;

namespace Jankscope.Core.Tests.Hardware;

public class CpuTopologyTests
{
    private static CpuTopology Map(byte[] buffer, int? osLogical = null)
    {
        var raw = ProcessorInfoParser.Parse(buffer);
        var topology = CpuTopologyMapper.Map(raw, osLogical);
        Assert.NotNull(topology);
        return topology!;
    }

    // ---- the observed fixture (6 SMT performance cores + 4 efficiency cores on the reference bench) ----

    [Fact]
    public void Observed_bytes_from_the_reference_bench_map_to_10_cores_and_16_threads()
    {
        var bytes = HardwareFixtures.Hex("lpie-relationall.observed-6p-smt-4e.hex.txt");
        Assert.Equal(2560, bytes.Length);

        var t = Map(bytes, osLogical: 16);

        Assert.Equal(10, t.PhysicalCores);
        Assert.Equal(16, t.LogicalProcessors);
        Assert.True(t.IsHybrid);
        Assert.Equal(6, t.PerformanceCores);
        Assert.Equal(12, t.PerformanceLogical);
        Assert.Equal(4, t.EfficiencyCores);
        Assert.Equal(4, t.EfficiencyLogical);
        Assert.Equal(6, t.SmtCores);
        Assert.True(t.SmtEnabled);
        Assert.Equal(1, t.L3Domains);
        Assert.Equal([25165824u], t.L3SizesBytes);
        Assert.Equal(1, t.PackageCount);
        Assert.Equal(1, t.ProcessorGroupCount);
        Assert.Equal(1, t.NumaNodeCount);
        Assert.Empty(t.Problems);
        Assert.Equal([new EfficiencyClassSummary(0, 4, 4), new EfficiencyClassSummary(1, 6, 12)], t.Classes);
    }

    [Fact]
    public void Observed_bytes_parse_without_leftover_problems()
    {
        var raw = ProcessorInfoParser.Parse(HardwareFixtures.Hex("lpie-relationall.observed-6p-smt-4e.hex.txt"));
        Assert.Empty(raw.Problems);
        Assert.Equal(10, raw.Cores.Count);
        Assert.Equal(20, raw.Caches.Count(c => c.Level == 1)); // data + instruction cache per core
        Assert.Equal(7, raw.Caches.Count(c => c.Level == 2));  // one per performance core, one shared by the 4 efficiency cores
        Assert.Equal(1, raw.Caches.Count(c => c.Level == 3));
    }

    // ---- synthetic machines ----

    [Fact]
    public void Hybrid_6p_plus_8e_is_14_cores_20_threads_with_performance_and_efficiency_counted_separately()
    {
        var t = Map(SyntheticMachines.Hybrid6P8E(), osLogical: 20);
        Assert.Equal(14, t.PhysicalCores);
        Assert.Equal(20, t.LogicalProcessors);
        Assert.True(t.IsHybrid);
        Assert.Equal((6, 12), (t.PerformanceCores, t.PerformanceLogical));
        Assert.Equal((8, 8), (t.EfficiencyCores, t.EfficiencyLogical));
        Assert.Equal(t.LogicalProcessors, t.PerformanceLogical + t.EfficiencyLogical);
        Assert.Equal(6, t.SmtCores);
        Assert.True(t.SmtEnabled);
        Assert.Empty(t.Problems);
    }

    [Fact]
    public void Non_hybrid_with_smt_reports_every_core_as_performance_and_none_as_efficiency()
    {
        var t = Map(SyntheticMachines.Smt8C16T(), osLogical: 16);
        Assert.False(t.IsHybrid);
        Assert.Equal((8, 16), (t.PerformanceCores, t.PerformanceLogical));
        Assert.Equal((0, 0), (t.EfficiencyCores, t.EfficiencyLogical));
        Assert.Equal(8, t.SmtCores);
        Assert.True(t.SmtEnabled);
    }

    [Fact]
    public void Smt_off_is_reported_as_off_not_as_unknown()
    {
        var t = Map(SyntheticMachines.SmtOff8C8T(), osLogical: 8);
        Assert.False(t.IsHybrid);
        Assert.Equal(8, t.PhysicalCores);
        Assert.Equal(8, t.LogicalProcessors);
        Assert.Equal(0, t.SmtCores);
        Assert.False(t.SmtEnabled);
    }

    [Fact]
    public void Two_l3_domains_are_counted_and_equal_sizes_are_not_asymmetric()
    {
        var t = Map(SyntheticMachines.TwoL3Domains(32 * 1024 * 1024, 32 * 1024 * 1024));
        Assert.Equal(2, t.L3Domains);
        Assert.Equal([33554432u, 33554432u], t.L3SizesBytes);
        Assert.False(t.L3SizesDiffer);
        Assert.Equal(16, t.PhysicalCores);
        Assert.Equal(32, t.LogicalProcessors);
    }

    [Fact]
    public void Cache_asymmetric_parts_are_detected_by_differing_l3_sizes()
    {
        var t = Map(SyntheticMachines.TwoL3Domains(96 * 1024 * 1024, 32 * 1024 * 1024));
        Assert.Equal(2, t.L3Domains);
        Assert.True(t.L3SizesDiffer);
        Assert.Contains("sizes differ", t.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void Two_processor_groups_add_up_to_128_logical_processors()
    {
        var t = Map(SyntheticMachines.TwoProcessorGroups(), osLogical: 128);
        Assert.Equal(64, t.PhysicalCores);
        Assert.Equal(128, t.LogicalProcessors);
        Assert.Equal(2, t.ProcessorGroupCount);
        Assert.Equal(2, t.PackageCount);
        Assert.Null(t.L3Domains);
    }

    [Fact]
    public void The_most_performant_class_is_performance_and_every_lower_class_counts_as_efficiency()
    {
        var t = Map(SyntheticMachines.ThreeClasses(), osLogical: 10);
        Assert.True(t.IsHybrid);
        Assert.Equal((4, 4), (t.PerformanceCores, t.PerformanceLogical));
        Assert.Equal((6, 6), (t.EfficiencyCores, t.EfficiencyLogical));
        Assert.Equal([new EfficiencyClassSummary(0, 2, 2), new EfficiencyClassSummary(1, 4, 4), new EfficiencyClassSummary(2, 4, 4)], t.Classes);
    }

    [Fact]
    public void A_small_machine_with_at_most_four_logical_processors_maps_correctly()
    {
        var t = Map(SyntheticMachines.Small2C4T(), osLogical: 4);
        Assert.Equal((2, 4), (t.PhysicalCores, t.LogicalProcessors));
        Assert.False(t.IsHybrid);
        Assert.Empty(t.Problems);
    }

    [Fact]
    public void A_machine_without_l3_records_reports_l3_as_not_reported_not_as_zero_domains()
    {
        var bytes = new LpieBuilder().Package(0, 3).Core(0, 3, 0, smt: true).Build();
        var t = Map(bytes);
        Assert.Null(t.L3Domains);
        Assert.Empty(t.L3SizesBytes);
        Assert.Contains("not reported", t.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void Only_level_3_records_count_as_l3_and_l1_l2_are_ignored()
    {
        var bytes = new LpieBuilder().Package(0, 3).Core(0, 3, 0, smt: true)
            .Cache(1, 2, 32768, (0, 3)).Cache(2, 0, 1 << 20, (0, 3)).Cache(3, 0, 8 << 20, (0, 3)).Build();
        var t = Map(bytes);
        Assert.Equal(1, t.L3Domains);
        Assert.Equal([8u << 20], t.L3SizesBytes);
    }

    [Fact]
    public void A_cache_record_with_a_zero_group_count_is_read_as_one_group_like_older_windows_builds_write_it()
    {
        var bytes = new LpieBuilder().Package(0, 3).Core(0, 3, 0, smt: true).CacheWithZeroGroupCount(3, 8 << 20, 0, 3).Build();
        var raw = ProcessorInfoParser.Parse(bytes);
        var l3 = Assert.Single(raw.Caches);
        Assert.Equal(new GroupAffinity(0, 3), Assert.Single(l3.Affinity));
        Assert.Empty(raw.Problems);
    }

    [Fact]
    public void Records_of_unknown_kinds_are_skipped_without_a_problem_for_forward_compatibility()
    {
        var bytes = new LpieBuilder().Core(0, 3, 0, smt: true).Unknown(99, 40).Core(0, 12, 0, smt: true).Build();
        var raw = ProcessorInfoParser.Parse(bytes);
        Assert.Equal(2, raw.Cores.Count);
        Assert.Empty(raw.Problems);
    }

    // ---- consistency and defensive parsing ----

    [Fact]
    public void A_disagreement_with_the_os_logical_processor_count_is_reported_not_hidden()
    {
        var t = Map(SyntheticMachines.Smt8C16T(), osLogical: 12);
        Assert.Equal(16, t.LogicalProcessors);
        var problem = Assert.Single(t.Problems);
        Assert.Contains("16", problem, StringComparison.Ordinal);
        Assert.Contains("12", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_os_count_adds_no_problem()
    {
        Assert.Empty(Map(SyntheticMachines.Smt8C16T(), osLogical: null).Problems);
    }

    [Fact]
    public void A_core_whose_smt_flag_disagrees_with_its_thread_count_is_flagged_and_counted_by_the_threads()
    {
        // flag set but one thread; flag clear but two threads
        var bytes = new LpieBuilder().Package(0, 0xF)
            .Core(0, 0x1, 0, smt: true)
            .Core(0, 0xC, 0, smt: false)
            .Build();
        var t = Map(bytes);
        Assert.Equal(2, t.Problems.Count);
        Assert.All(t.Problems, p => Assert.Contains("SMT", p, StringComparison.Ordinal));
        Assert.Equal(1, t.SmtCores); // only the core that really has two threads
        Assert.True(t.SmtEnabled);
    }

    [Fact]
    public void An_empty_buffer_yields_no_cores_and_a_stated_problem_and_no_topology()
    {
        var raw = ProcessorInfoParser.Parse([]);
        Assert.Empty(raw.Cores);
        Assert.NotEmpty(raw.Problems);
        Assert.Null(CpuTopologyMapper.Map(raw, null));
    }

    [Fact]
    public void A_truncated_last_record_is_reported_and_earlier_records_are_kept()
    {
        var full = SyntheticMachines.Smt8C16T();
        var raw = ProcessorInfoParser.Parse(full.AsSpan(0, full.Length - 5));
        Assert.NotEmpty(raw.Problems);
        Assert.Contains(raw.Problems, p => p.Contains("truncated", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(8, raw.Cores.Count); // the cache record was the last one and was cut off
        Assert.Empty(raw.Caches);
    }

    [Fact]
    public void A_zero_size_record_stops_parsing_instead_of_looping_forever()
    {
        var bytes = new LpieBuilder().Core(0, 3, 0, smt: true).Raw(new byte[8]).Core(0, 12, 0, smt: true).Build();
        var raw = ProcessorInfoParser.Parse(bytes);
        Assert.Single(raw.Cores);
        Assert.Contains(raw.Problems, p => p.Contains("size", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_record_larger_than_the_remaining_buffer_stops_parsing()
    {
        var oversize = new byte[48];
        BinaryPrimitives.WriteInt32LittleEndian(oversize.AsSpan(0), LpieBuilder.RelationCore);
        BinaryPrimitives.WriteInt32LittleEndian(oversize.AsSpan(4), 4096);
        var raw = ProcessorInfoParser.Parse(new LpieBuilder().Core(0, 1, 0, smt: false).Raw(oversize).Build());
        Assert.Single(raw.Cores);
        Assert.NotEmpty(raw.Problems);
    }

    [Fact]
    public void A_group_count_larger_than_the_record_can_hold_skips_that_record_with_a_problem()
    {
        var record = new LpieBuilder().Core(0, 3, 0, smt: true).Build();
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(8 + 22), 1000);
        var raw = ProcessorInfoParser.Parse(new LpieBuilder().Core(0, 1, 0, smt: false).Raw(record).Core(0, 4, 0, smt: false).Build());
        Assert.Equal(2, raw.Cores.Count);
        Assert.Single(raw.Problems);
    }

    [Fact]
    public void A_buffer_shorter_than_one_header_is_a_problem_not_an_exception()
    {
        var raw = ProcessorInfoParser.Parse(new byte[5]);
        Assert.Empty(raw.Cores);
        Assert.NotEmpty(raw.Problems);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(int.MinValue)]
    public void A_negative_record_size_is_rejected(int size)
    {
        var record = new byte[48];
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(4), size);
        var raw = ProcessorInfoParser.Parse(record);
        Assert.Empty(raw.Cores);
        Assert.NotEmpty(raw.Problems);
    }

    [Fact]
    public void Random_garbage_never_throws()
    {
        var rng = new Random(20261017);
        for (var i = 0; i < 500; i++)
        {
            var bytes = new byte[rng.Next(0, 400)];
            rng.NextBytes(bytes);
            var raw = ProcessorInfoParser.Parse(bytes);
            _ = CpuTopologyMapper.Map(raw, rng.Next(0, 200));
        }
    }

    // ---- description ----

    [Fact]
    public void The_description_of_the_observed_machine_states_the_facts_plainly()
    {
        var text = Map(HardwareFixtures.Hex("lpie-relationall.observed-6p-smt-4e.hex.txt"), 16).Describe();
        Assert.Contains("10 cores / 16 logical processors", text, StringComparison.Ordinal);
        Assert.Contains("hybrid (6 performance cores / 12 logical, 4 efficiency cores / 4 logical)", text, StringComparison.Ordinal);
        Assert.Contains("SMT on (6 of 10 cores)", text, StringComparison.Ordinal);
        Assert.Contains("L3 cache: 1 domain (24 MiB)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_description_of_a_non_hybrid_machine_does_not_invent_efficiency_cores()
    {
        var text = Map(SyntheticMachines.SmtOff8C8T(), 8).Describe();
        Assert.Contains("not hybrid", text, StringComparison.Ordinal);
        Assert.Contains("SMT off", text, StringComparison.Ordinal);
        Assert.DoesNotContain("efficiency cores", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("tr-TR")]
    [InlineData("ar-SA")]
    public void The_description_does_not_depend_on_the_thread_culture(string culture)
    {
        var expected = Map(SyntheticMachines.TwoL3Domains(96 * 1024 * 1024, 48 * 1024 * 1024 + 512 * 1024)).Describe();
        var old = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo(culture);
            Assert.Equal(expected, Map(SyntheticMachines.TwoL3Domains(96 * 1024 * 1024, 48 * 1024 * 1024 + 512 * 1024)).Describe());
            Assert.Contains("48.5 MiB", expected, StringComparison.Ordinal);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = old;
        }
    }

    [Fact]
    public void Performance_plus_efficiency_always_sum_to_the_logical_processor_count()
    {
        foreach (var bytes in new[]
                 {
                     SyntheticMachines.Hybrid6P8E(), SyntheticMachines.Smt8C16T(), SyntheticMachines.SmtOff8C8T(),
                     SyntheticMachines.TwoL3Domains(1, 2), SyntheticMachines.ThreeClasses(), SyntheticMachines.TwoProcessorGroups(),
                     SyntheticMachines.Small2C4T(), HardwareFixtures.Hex("lpie-relationall.observed-6p-smt-4e.hex.txt"),
                 })
        {
            var t = Map(bytes);
            Assert.Equal(t.LogicalProcessors, t.PerformanceLogical + t.EfficiencyLogical);
            Assert.Equal(t.PhysicalCores, t.PerformanceCores + t.EfficiencyCores);
            Assert.Equal(t.LogicalProcessors, t.Classes.Sum(c => c.LogicalProcessors));
        }
    }
}
