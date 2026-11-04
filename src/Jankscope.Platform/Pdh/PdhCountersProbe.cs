using System.Globalization;
using Jankscope.Core;
using Jankscope.Core.Hardware;

namespace Jankscope.Platform.Pdh;

/// <summary>
/// Reports which PDH counters exist and can be read on this machine (spec section 6.5): processor utility and frequency, memory, hard
/// faults, disk, GPU engines and GPU adapter memory, each as its own row. A missing object or counter (a machine without GPU counters, a
/// virtual machine, an older Windows) is an unavailable row with the PDH reason, never a zero. Two collections about 200 ms apart are
/// taken so rate counters can prove they work. PDH only: no vendor API and no GPU wake-up.
/// </summary>
public sealed class PdhCountersProbe : IProbe
{
    private const string SourceFormat = "PDH PdhAddEnglishCounterW (documented): {0}";
    private static readonly TimeSpan BetweenCollections = TimeSpan.FromMilliseconds(200);

    private readonly Func<ICounterSource> _create;

    public PdhCountersProbe()
        : this(() => new PdhCounterSource())
    {
    }

    internal PdhCountersProbe(Func<ICounterSource> create) => _create = create;

    public string Id => "pdh";

    public IReadOnlyList<CapabilityEntry> Run()
    {
        CounterSnapshot snapshot;
        using (var source = _create())
        {
            _ = source.Collect();
            Thread.Sleep(BetweenCollections);
            snapshot = source.Collect();
        }

        return Rows(snapshot);
    }

    internal static IReadOnlyList<CapabilityEntry> Rows(CounterSnapshot snapshot)
    {
        var sample = SystemSampleBuilder.Build(0, 0, DateTimeOffset.UnixEpoch, snapshot, []);
        return
        [
            Row("pdh.cpu_utility", "CPU utilization counters (total and per processor)", snapshot, [CounterIds.CpuUtility],
                sample.Cores.Count == 0 ? null : string.Create(CultureInfo.InvariantCulture, $"{sample.Cores.Count} per-processor instance(s) and the total")),
            Row("pdh.cpu_frequency", "CPU actual frequency counters", snapshot, [CounterIds.CpuActualFrequency], null),
            Row("pdh.memory", "Memory counters (available, committed, commit limit)", snapshot,
                [CounterIds.MemoryAvailableBytes, CounterIds.MemoryCommittedBytes, CounterIds.MemoryCommitLimitBytes], null),
            Row("pdh.hard_faults", "Hard page faults (Pages Input/sec)", snapshot, [CounterIds.HardFaultsPerSecond], null),
            Row("pdh.disk", "Disk latency and queue counters", snapshot, [CounterIds.DiskSecondsPerTransfer, CounterIds.DiskQueueLength],
                sample.Disks.Count == 0 ? null : string.Create(CultureInfo.InvariantCulture, $"{sample.Disks.Count} disk instance(s)")),
            Row("pdh.gpu_engine", "GPU engine utilization counters", snapshot, [CounterIds.GpuEngineUtilization],
                sample.Gpus.Count == 0 ? null : string.Create(CultureInfo.InvariantCulture, $"{sample.Gpus.Count(g => g.Engines.NotAvailableReason is null)} adapter(s) by LUID")),
            Row("pdh.gpu_memory", "GPU adapter memory counters", snapshot, [CounterIds.GpuDedicatedUsage, CounterIds.GpuSharedUsage], null),
        ];
    }

    private static CapabilityEntry Row(string id, string title, CounterSnapshot snapshot, string[] counterIds, string? detail)
    {
        var results = counterIds.Select(snapshot.Get).ToList();
        var paths = string.Join("; ", counterIds.Select(c => CounterSpecs.All.Single(s => s.Id == c).EnglishPath));
        var source = string.Format(CultureInfo.InvariantCulture, SourceFormat, paths);

        var reasons = results.Select(r => r.UnavailableReason).Where(r => r is not null).Distinct().ToList();
        if (reasons.Count > 0)
        {
            return CapabilityEntry.Unavailable(id, title, CapabilityTier.A, Availability.NotAvailable, source, string.Join("; ", reasons));
        }

        var instances = results.SelectMany(r => r.Instances).ToList();
        if (instances.Count == 0)
        {
            return CapabilityEntry.Unavailable(id, title, CapabilityTier.A, Availability.NotAvailable, source, "the counter exists but reports no instance right now");
        }

        var readable = instances.Count(i => i.Value.IsAvailable);
        if (readable == 0)
        {
            var why = instances.Select(i => i.Value.UnavailableReason).FirstOrDefault(r => r is not null) ?? "no instance returned a value";
            return CapabilityEntry.Unavailable(id, title, CapabilityTier.A, Availability.NotAvailable, source, why);
        }

        var value = string.Create(CultureInfo.InvariantCulture, $"{readable} of {instances.Count} instance(s) readable") + (detail is null ? string.Empty : "; " + detail);
        return CapabilityEntry.Available(id, title, CapabilityTier.A, source, value);
    }
}
