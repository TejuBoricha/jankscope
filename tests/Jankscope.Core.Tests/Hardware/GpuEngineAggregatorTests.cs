using Jankscope.Core.Hardware;

namespace Jankscope.Core.Tests.Hardware;

public class GpuEngineAggregatorTests
{
    private static string Name(uint pid, uint luidLow, uint phys, uint eng, string type) =>
        $"pid_{pid}_luid_0x00000000_0x{luidLow:X8}_phys_{phys}_eng_{eng}_engtype_{type}";

    private static EngineInstanceValue V(string name, double value) => new(name, Reading.Of(value));

    private static EngineInstanceValue Missing(string name, string why = "invalid data") => new(name, Reading.NotAvailable(why));

    private static readonly Luid A = new(0, 0xA);
    private static readonly Luid B = new(0, 0xB);

    [Fact]
    public void Processes_sharing_an_engine_add_up_and_the_busiest_engine_of_a_type_is_the_peak()
    {
        var result = GpuEngineAggregator.Aggregate(
        [
            V(Name(100, 0xA, 0, 0, "3D"), 10),
            V(Name(200, 0xA, 0, 0, "3D"), 5),   // same engine, other process: 15 in total
            V(Name(100, 0xA, 0, 1, "3D"), 40),  // a second 3D engine
            V(Name(100, 0xA, 0, 2, "Copy"), 7),
        ], [A]);

        var adapter = Assert.Single(result.Adapters);
        Assert.Equal(A, adapter.Luid);
        Assert.Null(adapter.NotAvailableReason);
        Assert.Equal(4, adapter.InstanceCount);
        var threeD = Assert.Single(adapter.ByEngineType, t => t.EngineType == "3D");
        Assert.Equal(2, threeD.EngineCount);
        Assert.Equal(40.0, threeD.PeakEnginePercent.Value);
        Assert.Equal(55.0, threeD.SummedPercent.Value);
        var copy = Assert.Single(adapter.ByEngineType, t => t.EngineType == "Copy");
        Assert.Equal(7.0, copy.PeakEnginePercent.Value);
        Assert.Equal(7.0, copy.SummedPercent.Value);
    }

    [Fact]
    public void Adapters_are_keyed_by_luid_and_never_mixed_even_when_their_engine_numbers_collide()
    {
        var result = GpuEngineAggregator.Aggregate(
        [
            V(Name(1, 0xA, 0, 0, "3D"), 90),
            V(Name(1, 0xB, 0, 0, "3D"), 3),
        ], [A, B]);

        Assert.Equal(2, result.Adapters.Count);
        Assert.Equal(90.0, result.Adapters.Single(a => a.Luid == A).ByEngineType.Single().PeakEnginePercent.Value);
        Assert.Equal(3.0, result.Adapters.Single(a => a.Luid == B).ByEngineType.Single().PeakEnginePercent.Value);
    }

    [Fact]
    public void The_same_engine_number_on_a_different_phys_is_a_different_engine()
    {
        var result = GpuEngineAggregator.Aggregate([V(Name(1, 0xA, 0, 0, "3D"), 10), V(Name(1, 0xA, 1, 0, "3D"), 20)], [A]);
        var threeD = Assert.Single(Assert.Single(result.Adapters).ByEngineType);
        Assert.Equal(2, threeD.EngineCount);
        Assert.Equal(20.0, threeD.PeakEnginePercent.Value);
    }

    [Fact]
    public void An_unavailable_instance_is_skipped_counted_and_never_added_as_zero()
    {
        var result = GpuEngineAggregator.Aggregate(
            [V(Name(1, 0xA, 0, 0, "3D"), 10), Missing(Name(2, 0xA, 0, 0, "3D"))], [A]);
        var threeD = Assert.Single(Assert.Single(result.Adapters).ByEngineType);
        Assert.Equal(10.0, threeD.SummedPercent.Value);
        Assert.Equal(1, threeD.UnavailableInstances);
    }

    [Fact]
    public void When_every_instance_of_a_type_is_unavailable_the_type_is_not_available_with_a_reason_not_zero()
    {
        var result = GpuEngineAggregator.Aggregate(
            [Missing(Name(1, 0xA, 0, 0, "3D"), "needs a second sample"), Missing(Name(2, 0xA, 0, 1, "3D"), "needs a second sample")], [A]);
        var threeD = Assert.Single(Assert.Single(result.Adapters).ByEngineType);
        Assert.False(threeD.PeakEnginePercent.IsAvailable);
        Assert.False(threeD.SummedPercent.IsAvailable);
        Assert.Contains("needs a second sample", threeD.PeakEnginePercent.UnavailableReason, StringComparison.Ordinal);
        Assert.Equal(2, threeD.UnavailableInstances);
    }

    [Fact]
    public void An_expected_adapter_without_any_instance_is_not_available_with_a_reason_and_has_no_engine_rows()
    {
        var result = GpuEngineAggregator.Aggregate([V(Name(1, 0xA, 0, 0, "3D"), 10)], [A, B]);
        var missing = result.Adapters.Single(a => a.Luid == B);
        Assert.Empty(missing.ByEngineType);
        Assert.Equal(0, missing.InstanceCount);
        Assert.False(string.IsNullOrWhiteSpace(missing.NotAvailableReason));
    }

    [Fact]
    public void An_adapter_that_has_instances_but_was_not_expected_is_still_reported()
    {
        var result = GpuEngineAggregator.Aggregate([V(Name(1, 0xC, 0, 0, "3D"), 10)], [A]);
        Assert.Equal([A, new Luid(0, 0xC)], result.Adapters.Select(a => a.Luid).ToArray());
        Assert.NotNull(result.Adapters[0].NotAvailableReason);
        Assert.Null(result.Adapters[1].NotAvailableReason);
    }

    [Fact]
    public void No_counter_instances_at_all_gives_every_expected_adapter_a_reason_not_a_zero()
    {
        var result = GpuEngineAggregator.Aggregate([], [A, B]);
        Assert.Equal(2, result.Adapters.Count);
        Assert.All(result.Adapters, a =>
        {
            Assert.Empty(a.ByEngineType);
            Assert.False(string.IsNullOrWhiteSpace(a.NotAvailableReason));
        });
    }

    [Fact]
    public void An_unparsable_instance_is_reported_and_does_not_disturb_the_others()
    {
        var result = GpuEngineAggregator.Aggregate([V("garbage", 99), V(Name(1, 0xA, 0, 0, "3D"), 10)], [A]);
        var bad = Assert.Single(result.Unparsed);
        Assert.Equal("garbage", bad.Name);
        Assert.False(string.IsNullOrWhiteSpace(bad.Reason));
        Assert.Equal(10.0, Assert.Single(Assert.Single(result.Adapters).ByEngineType).SummedPercent.Value);
    }

    [Fact]
    public void Values_above_one_hundred_percent_are_kept_as_reported_not_clamped()
    {
        var result = GpuEngineAggregator.Aggregate([V(Name(1, 0xA, 0, 0, "3D"), 80), V(Name(2, 0xA, 0, 0, "3D"), 40)], [A]);
        Assert.Equal(120.0, Assert.Single(Assert.Single(result.Adapters).ByEngineType).PeakEnginePercent.Value);
    }

    [Fact]
    public void Input_order_does_not_change_the_result()
    {
        var items = new[]
        {
            V(Name(1, 0xA, 0, 0, "3D"), 10), V(Name(2, 0xA, 0, 0, "3D"), 5), V(Name(1, 0xB, 0, 3, "Copy"), 2),
            V(Name(1, 0xA, 0, 1, "VideoDecode"), 1), Missing(Name(3, 0xB, 0, 3, "Copy")), V("junk", 1),
        };
        var expected = Flatten(GpuEngineAggregator.Aggregate(items, [A, B]));
        var rng = new Random(11);
        for (var n = 0; n < 50; n++)
        {
            var shuffled = items.OrderBy(_ => rng.Next()).ToArray();
            Assert.Equal(expected, Flatten(GpuEngineAggregator.Aggregate(shuffled, [B, A])));
        }
    }

    [Fact]
    public void The_observed_reference_bench_names_aggregate_by_luid_and_engine_type()
    {
        var names = HardwareFixtures.Lines("gpu-engine-instances.observed-hybrid.txt");
        var parsed = names.Select(n => { GpuCounterNames.TryParseEngine(n, out var i, out _); return i!; }).ToList();
        var result = GpuEngineAggregator.Aggregate(names.Select(n => V(n, 1)), parsed.Select(p => p.Luid).Distinct());

        Assert.Empty(result.Unparsed);
        Assert.Equal(3, result.Adapters.Count);
        Assert.Equal(names.Length, result.Adapters.Sum(a => a.InstanceCount));
        foreach (var adapter in result.Adapters)
        {
            Assert.Null(adapter.NotAvailableReason);
            var mine = parsed.Where(p => p.Luid == adapter.Luid).ToList();
            Assert.Equal(mine.Count, adapter.InstanceCount);
            foreach (var type in adapter.ByEngineType)
            {
                // every instance contributes exactly 1.0, so the summed value of a type is its instance count
                Assert.Equal(mine.Count(p => p.EngineType == type.EngineType), (int)type.SummedPercent.Value!.Value);
                Assert.Equal(mine.Where(p => p.EngineType == type.EngineType).Select(p => (p.Phys, p.EngineIndex)).Distinct().Count(), type.EngineCount);
            }
        }
    }

    private static string Flatten(GpuEngineAggregation a) =>
        string.Join("|", a.Adapters.Select(x => $"{x.Luid}:{x.InstanceCount}:{x.NotAvailableReason}:" +
            string.Join(",", x.ByEngineType.Select(t => $"{t.EngineType}/{t.EngineCount}/{t.PeakEnginePercent}/{t.SummedPercent}/{t.UnavailableInstances}")))) +
        "#" + string.Join("|", a.Unparsed.Select(u => u.Name));
}
