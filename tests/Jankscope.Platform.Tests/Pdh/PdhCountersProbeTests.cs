using Jankscope.Core;
using Jankscope.Core.Hardware;
using Jankscope.Platform.Pdh;

namespace Jankscope.Platform.Tests.Pdh;

public class PdhCountersProbeTests
{
    private static InstanceReading I(string instance, double value) => new(instance, Reading.Of(value));

    private static KeyValuePair<string, CounterResult> C(string id, params InstanceReading[] items) => new(id, CounterResult.Of(items));

    private static KeyValuePair<string, CounterResult> Missing(string id, string why) => new(id, CounterResult.Unavailable(why));

    private static CounterSnapshot Healthy() => new(
    [
        C(CounterIds.CpuUtility, I("0,0", 5), I("0,1", 6), I("0,_Total", 5.5), I("_Total", 5.5)),
        C(CounterIds.CpuActualFrequency, I("0,0", 2400), I("0,1", 2500), I("_Total", 2450)),
        C(CounterIds.MemoryAvailableBytes, I("", 1e9)),
        C(CounterIds.MemoryCommittedBytes, I("", 2e9)),
        C(CounterIds.MemoryCommitLimitBytes, I("", 4e9)),
        C(CounterIds.HardFaultsPerSecond, I("", 1)),
        C(CounterIds.DiskSecondsPerTransfer, I("0 C:", 0.001), I("_Total", 0.001)),
        C(CounterIds.DiskQueueLength, I("0 C:", 0), I("_Total", 0)),
        C(CounterIds.GpuEngineUtilization, I("pid_4_luid_0x00000000_0x0000000A_phys_0_eng_0_engtype_3D", 1)),
        C(CounterIds.GpuDedicatedUsage, I("luid_0x00000000_0x0000000A_phys_0", 100)),
        C(CounterIds.GpuSharedUsage, I("luid_0x00000000_0x0000000A_phys_0", 200)),
    ]);

    private static IReadOnlyList<CapabilityEntry> Rows(CounterSnapshot s) => PdhCountersProbe.Rows(s);

    [Fact]
    public void A_healthy_machine_gives_seven_available_rows_with_english_paths_as_their_source()
    {
        var rows = Rows(Healthy());
        Assert.Equal(
            ["pdh.cpu_frequency", "pdh.cpu_utility", "pdh.disk", "pdh.gpu_engine", "pdh.gpu_memory", "pdh.hard_faults", "pdh.memory"],
            rows.Select(r => r.Id).Order(StringComparer.Ordinal).ToArray());
        Assert.All(rows, r =>
        {
            Assert.Equal(Availability.Available, r.Availability);
            Assert.Equal(CapabilityTier.A, r.Tier);
            Assert.Contains("PdhAddEnglishCounterW", r.Source, StringComparison.Ordinal);
            Assert.StartsWith("\\", r.Source[(r.Source.IndexOf(": ", StringComparison.Ordinal) + 2)..], StringComparison.Ordinal);
        });
        Assert.Contains("2 per-processor instance(s)", rows.Single(r => r.Id == "pdh.cpu_utility").Value, StringComparison.Ordinal);
        Assert.Contains("1 disk instance(s)", rows.Single(r => r.Id == "pdh.disk").Value, StringComparison.Ordinal);
        Assert.Contains("1 adapter(s) by LUID", rows.Single(r => r.Id == "pdh.gpu_engine").Value, StringComparison.Ordinal);
    }

    [Fact]
    public void A_machine_without_gpu_counters_has_explicit_unavailable_gpu_rows_and_healthy_others()
    {
        const string why = "the performance object does not exist on this system (PDH_CSTATUS_NO_OBJECT)";
        var snapshot = new CounterSnapshot(Enumerate(Healthy()).Where(k => !k.Key.StartsWith("gpu.", StringComparison.Ordinal))
            .Concat([Missing(CounterIds.GpuEngineUtilization, why), Missing(CounterIds.GpuDedicatedUsage, why), Missing(CounterIds.GpuSharedUsage, why)]));
        var rows = Rows(snapshot);

        foreach (var id in new[] { "pdh.gpu_engine", "pdh.gpu_memory" })
        {
            var row = rows.Single(r => r.Id == id);
            Assert.Equal(Availability.NotAvailable, row.Availability);
            Assert.Null(row.Value);
            Assert.Contains("PDH_CSTATUS_NO_OBJECT", row.Reason, StringComparison.Ordinal);
        }

        Assert.Equal(5, rows.Count(r => r.Availability == Availability.Available));
    }

    [Fact]
    public void A_rate_counter_that_has_no_value_yet_is_unavailable_with_the_reason_not_zero()
    {
        var snapshot = new CounterSnapshot(Enumerate(Healthy()).Where(k => k.Key != CounterIds.HardFaultsPerSecond)
            .Concat([new(CounterIds.HardFaultsPerSecond, CounterResult.Of([new InstanceReading("", Reading.NotAvailable("needs a second sample (PDH_CALC_NEGATIVE_DENOMINATOR)"))]))]));
        var row = Rows(snapshot).Single(r => r.Id == "pdh.hard_faults");
        Assert.Equal(Availability.NotAvailable, row.Availability);
        Assert.Contains("second sample", row.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_counter_with_no_instance_is_unavailable_with_a_reason()
    {
        var snapshot = new CounterSnapshot(Enumerate(Healthy()).Where(k => k.Key != CounterIds.DiskQueueLength && k.Key != CounterIds.DiskSecondsPerTransfer)
            .Concat([C(CounterIds.DiskQueueLength), C(CounterIds.DiskSecondsPerTransfer)]));
        var row = Rows(snapshot).Single(r => r.Id == "pdh.disk");
        Assert.Equal(Availability.NotAvailable, row.Availability);
        Assert.Contains("no instance", row.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void If_one_of_the_memory_counters_is_missing_the_memory_row_is_unavailable_and_names_the_reason()
    {
        var snapshot = new CounterSnapshot(Enumerate(Healthy()).Where(k => k.Key != CounterIds.MemoryCommitLimitBytes)
            .Concat([Missing(CounterIds.MemoryCommitLimitBytes, "the counter does not exist on this system (PDH_CSTATUS_NO_COUNTER)")]));
        var row = Rows(snapshot).Single(r => r.Id == "pdh.memory");
        Assert.Equal(Availability.NotAvailable, row.Availability);
        Assert.Contains("PDH_CSTATUS_NO_COUNTER", row.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_snapshot_makes_every_row_unavailable_never_available_or_zero()
    {
        var rows = Rows(new CounterSnapshot([]));
        Assert.Equal(7, rows.Count);
        Assert.All(rows, r =>
        {
            Assert.Equal(Availability.NotAvailable, r.Availability);
            Assert.False(string.IsNullOrWhiteSpace(r.Reason));
        });
    }

    [Fact]
    public void The_probe_creates_collects_twice_and_disposes_its_source()
    {
        FakeCounterSource? created = null;
        var probe = new PdhCountersProbe(() => created = new FakeCounterSource(_ => Healthy()));
        var rows = probe.Run();
        Assert.Equal("pdh", probe.Id);
        Assert.Equal(2, created!.Calls);
        Assert.True(created.Disposed);
        Assert.Equal(7, rows.Count);
    }

    [Fact]
    public void The_real_probe_on_this_machine_reports_every_row_well_formed_and_cpu_and_memory_available()
    {
        var rows = new PdhCountersProbe().Run();
        Assert.Equal(7, rows.Count);
        Assert.All(rows, r =>
        {
            Assert.False(string.IsNullOrWhiteSpace(r.Source));
            if (r.Availability == Availability.Available)
            {
                Assert.False(string.IsNullOrWhiteSpace(r.Value));
            }
            else
            {
                Assert.False(string.IsNullOrWhiteSpace(r.Reason));
            }
        });
        Assert.Equal(Availability.Available, rows.Single(r => r.Id == "pdh.cpu_utility").Availability);
        Assert.Equal(Availability.Available, rows.Single(r => r.Id == "pdh.memory").Availability);
        Assert.Equal(Availability.Available, rows.Single(r => r.Id == "pdh.hard_faults").Availability);
    }

    private static IEnumerable<KeyValuePair<string, CounterResult>> Enumerate(CounterSnapshot snapshot) =>
        CounterSpecs.All.Select(s => new KeyValuePair<string, CounterResult>(s.Id, snapshot.Get(s.Id)));
}
