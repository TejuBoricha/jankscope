using System.Globalization;
using Jankscope.Core;
using Jankscope.Core.Hardware;
using Jankscope.Platform.Hardware;
using Jankscope.Platform.Pdh;

namespace Jankscope.Platform.Tests.Pdh;

/// <summary>
/// PDH against the machine the tests run on. Assertions are structural: nothing names a GPU, vendor or core count, and every counter that
/// does not exist here must come back with a reason instead of failing the test.
/// </summary>
public class PdhCounterSourceTests
{
    private static CounterSnapshot TwoCollections(PdhCounterSource source)
    {
        _ = source.Collect();
        Thread.Sleep(300);
        return source.Collect();
    }

    [Fact]
    public void Every_default_counter_returns_instances_or_a_reason_and_no_reading_is_a_silent_zero()
    {
        using var source = new PdhCounterSource();
        var snapshot = TwoCollections(source);

        foreach (var spec in CounterSpecs.All)
        {
            var result = snapshot.Get(spec.Id);
            if (result.UnavailableReason is { } why)
            {
                Assert.False(string.IsNullOrWhiteSpace(why), spec.Id);
                Assert.Empty(result.Instances);
                continue;
            }

            Assert.Equal(result.Instances.Count, result.Instances.Select(i => i.Instance).Distinct(StringComparer.Ordinal).Count());
            foreach (var instance in result.Instances)
            {
                if (instance.Value.IsAvailable)
                {
                    Assert.True(double.IsFinite(instance.Value.Value!.Value), $"{spec.Id}/{instance.Instance}");
                }
                else
                {
                    Assert.False(string.IsNullOrWhiteSpace(instance.Value.UnavailableReason), $"{spec.Id}/{instance.Instance}");
                }
            }
        }
    }

    [Fact]
    public void Memory_counters_are_single_instance_and_positive()
    {
        using var source = new PdhCounterSource();
        var sample = SystemSampleBuilder.Build(0, 0, DateTimeOffset.UtcNow, TwoCollections(source), []);

        foreach (var reading in new[] { sample.MemoryAvailableBytes, sample.MemoryCommittedBytes, sample.MemoryCommitLimitBytes })
        {
            Assert.True(reading.IsAvailable, reading.UnavailableReason);
            Assert.True(reading.Value > 0);
        }
    }

    [Fact]
    public void The_hard_fault_counter_is_present_and_readable_after_the_second_collection()
    {
        using var source = new PdhCounterSource();
        var sample = SystemSampleBuilder.Build(0, 0, DateTimeOffset.UtcNow, TwoCollections(source), []);
        Assert.True(sample.HardFaultsPerSecond.IsAvailable, sample.HardFaultsPerSecond.UnavailableReason);
        Assert.True(sample.HardFaultsPerSecond.Value >= 0);
    }

    [Fact]
    public void The_first_collection_never_reports_a_rate_as_zero_it_says_it_has_none_yet()
    {
        using var source = new PdhCounterSource();
        var first = SystemSampleBuilder.Build(0, 0, DateTimeOffset.UtcNow, source.Collect(), []);

        // Observed on Windows 11: a rate counter returns "invalid data" until the second collection. It must surface as unavailable with
        // that reason, for the single-instance counter (function-level status) and for the wildcard ones (per-item status) alike.
        Assert.False(first.HardFaultsPerSecond.IsAvailable);
        Assert.Contains("second sample", first.HardFaultsPerSecond.UnavailableReason, StringComparison.Ordinal);
        Assert.False(first.CpuTotalUtility.IsAvailable);
        Assert.Contains("second sample", first.CpuTotalUtility.UnavailableReason, StringComparison.Ordinal);

        // The per-item status path: an averaged counter reports "invalid data" per instance until it has two samples.
        Assert.All(first.Disks, d =>
        {
            Assert.False(d.SecondsPerTransfer.IsAvailable, d.Instance);
            Assert.Contains("second sample", d.SecondsPerTransfer.UnavailableReason, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void The_per_core_counter_has_one_instance_per_logical_processor_and_a_total()
    {
        using var source = new PdhCounterSource();
        var sample = SystemSampleBuilder.Build(0, 0, DateTimeOffset.UtcNow, TwoCollections(source), []);
        var logical = (int)NativeHardware.GetActiveProcessorCount(NativeHardware.AllProcessorGroups);

        Assert.True(sample.CpuTotalUtility.IsAvailable, sample.CpuTotalUtility.UnavailableReason);
        Assert.True(sample.CpuTotalUtility.Value >= 0);
        Assert.Null(sample.CoresUnavailableReason);
        Assert.Equal(logical, sample.Cores.Count);
        Assert.All(sample.Cores, c => Assert.True(c.Utility.IsAvailable, c.Utility.UnavailableReason));
        Assert.DoesNotContain(sample.Notes, n => n.Contains("processor", StringComparison.Ordinal));
    }

    [Fact]
    public void A_missing_object_and_a_missing_counter_are_unavailable_with_their_pdh_reason_and_do_not_disturb_a_good_counter()
    {
        CounterSpec[] specs =
        [
            new("test.missing_object", @"\NoSuchObjectForJankscope(*)\Nope", true, false),
            new("test.missing_counter", @"\Memory\NoSuchCounterForJankscope", false, false),
            new("test.good", @"\Memory\Available Bytes", false, false),
        ];
        using var source = new PdhCounterSource(specs);
        var snapshot = TwoCollections(source);

        Assert.Contains("PDH_CSTATUS_NO_OBJECT", snapshot.Get("test.missing_object").UnavailableReason, StringComparison.Ordinal);
        Assert.Contains("PDH_CSTATUS_NO_COUNTER", snapshot.Get("test.missing_counter").UnavailableReason, StringComparison.Ordinal);
        var good = snapshot.Get("test.good");
        Assert.Null(good.UnavailableReason);
        Assert.True(Assert.Single(good.Instances).Value.Value > 0);
    }

    [Fact]
    public void A_counter_that_was_not_asked_for_is_reported_as_not_requested()
    {
        using var source = new PdhCounterSource([new CounterSpec("test.good", @"\Memory\Available Bytes", false, false)]);
        Assert.Equal("the counter was not requested", source.Collect().Get(CounterIds.CpuUtility).UnavailableReason);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("tr-TR")]
    [InlineData("ja-JP")]
    public void The_shape_of_the_result_does_not_depend_on_the_display_or_thread_culture(string culture)
    {
        using var source = new PdhCounterSource();
        var baseline = TwoCollections(source);
        var old = Thread.CurrentThread.CurrentCulture;
        var oldUi = Thread.CurrentThread.CurrentUICulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo(culture);
            Thread.CurrentThread.CurrentUICulture = new CultureInfo(culture);
            using var other = new PdhCounterSource();
            var snapshot = TwoCollections(other);
            foreach (var spec in CounterSpecs.All.Where(s => !s.Wildcard || s.Id is CounterIds.CpuUtility or CounterIds.CpuActualFrequency or CounterIds.DiskQueueLength))
            {
                Assert.Equal(baseline.Get(spec.Id).UnavailableReason is null, snapshot.Get(spec.Id).UnavailableReason is null);
                Assert.Equal(baseline.Get(spec.Id).Instances.Count, snapshot.Get(spec.Id).Instances.Count);
            }
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = old;
            Thread.CurrentThread.CurrentUICulture = oldUi;
        }
    }

    [Fact]
    public void Gpu_counters_when_present_parse_completely_and_name_adapters_that_dxgi_also_lists()
    {
        using var source = new PdhCounterSource();
        var snapshot = TwoCollections(source);
        var engines = snapshot.Get(CounterIds.GpuEngineUtilization);
        var adapters = new DxgiAdapterSource().Enumerate().Adapters.Select(a => a.Luid).ToHashSet();
        if (engines.UnavailableReason is not null || engines.Instances.Count == 0 || adapters.Count == 0)
        {
            return; // no GPU counters on this machine: the sample reports why (covered by the fake-source tests)
        }

        var aggregation = GpuEngineAggregator.Aggregate(engines.Instances.Select(i => new EngineInstanceValue(i.Instance, i.Value)), []);
        Assert.Empty(aggregation.Unparsed);
        Assert.All(aggregation.Adapters, a => Assert.Contains(a.Luid, adapters));

        var memory = snapshot.Get(CounterIds.GpuDedicatedUsage);
        if (memory.UnavailableReason is null)
        {
            foreach (var instance in memory.Instances)
            {
                Assert.True(GpuCounterNames.TryParseAdapterMemory(instance.Instance, out var parsed, out var why), why);
                Assert.Contains(parsed!.Luid, adapters);
            }
        }
    }

    [Fact]
    public void Collecting_after_dispose_throws_and_disposing_twice_is_harmless()
    {
        var source = new PdhCounterSource();
        source.Dispose();
        source.Dispose();
        Assert.Throws<ObjectDisposedException>(() => source.Collect());
    }
}
