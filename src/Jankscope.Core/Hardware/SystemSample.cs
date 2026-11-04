using System.Globalization;
using System.Text.RegularExpressions;

namespace Jankscope.Core.Hardware;

/// <param name="Node">The first number of a <c>Processor Information</c> instance name (<c>node,index</c>).</param>
/// <param name="Index">The logical processor number inside that node.</param>
public sealed record CoreSample(string Instance, int Node, int Index, Reading Utility, Reading ActualFrequencyMhz);

public sealed record DiskSample(string Instance, Reading SecondsPerTransfer, Reading QueueLength);

public sealed record GpuAdapterSample(Luid Luid, GpuAdapterUtilization Engines, Reading DedicatedUsageBytes, Reading SharedUsageBytes);

/// <summary>
/// One sample of system context (spec section 7.1), keyed by adapter LUID for GPUs. Every number is a <see cref="Reading"/>: a counter,
/// instance or adapter that could not be read says so and never appears as zero. Collections that could not be produced are empty and the
/// matching <c>...UnavailableReason</c> says why.
/// </summary>
public sealed record SystemSample(
    long Sequence,
    long QpcTicks,
    DateTimeOffset UtcTime,
    Reading CpuTotalUtility,
    Reading CpuActualFrequencyMhz,
    IReadOnlyList<CoreSample> Cores,
    string? CoresUnavailableReason,
    Reading MemoryAvailableBytes,
    Reading MemoryCommittedBytes,
    Reading MemoryCommitLimitBytes,
    Reading HardFaultsPerSecond,
    IReadOnlyList<DiskSample> Disks,
    string? DisksUnavailableReason,
    IReadOnlyList<GpuAdapterSample> Gpus,
    string? GpusUnavailableReason,
    IReadOnlyList<string> Notes);

/// <summary>Pure assembly of a <see cref="SystemSample"/> from one <see cref="CounterSnapshot"/>.</summary>
public static partial class SystemSampleBuilder
{
    [GeneratedRegex(@"\A(\d{1,9}),(\d{1,9})\z")]
    private static partial Regex CoreInstance();

    [GeneratedRegex(@"\A(\d{1,9}),_Total\z")]
    private static partial Regex NodeTotalInstance();

    public static SystemSample Build(long sequence, long qpcTicks, DateTimeOffset utc, CounterSnapshot snapshot, IReadOnlyCollection<Luid> knownAdapters)
    {
        var notes = new List<string>();
        var utility = snapshot.Get(CounterIds.CpuUtility);
        var frequency = snapshot.Get(CounterIds.CpuActualFrequency);
        var (cores, coresReason) = BuildCores(utility, frequency, notes);
        var (disks, disksReason) = BuildDisks(snapshot.Get(CounterIds.DiskSecondsPerTransfer), snapshot.Get(CounterIds.DiskQueueLength));
        var (gpus, gpuReason) = BuildGpus(
            snapshot.Get(CounterIds.GpuEngineUtilization), snapshot.Get(CounterIds.GpuDedicatedUsage), snapshot.Get(CounterIds.GpuSharedUsage), knownAdapters, notes);

        return new SystemSample(
            sequence,
            qpcTicks,
            utc,
            Total(utility),
            Total(frequency),
            cores,
            coresReason,
            Single(snapshot.Get(CounterIds.MemoryAvailableBytes)),
            Single(snapshot.Get(CounterIds.MemoryCommittedBytes)),
            Single(snapshot.Get(CounterIds.MemoryCommitLimitBytes)),
            Single(snapshot.Get(CounterIds.HardFaultsPerSecond)),
            disks,
            disksReason,
            gpus,
            gpuReason,
            notes);
    }

    private static Reading Single(CounterResult result)
    {
        if (result.UnavailableReason is { } why)
        {
            return Reading.NotAvailable(why);
        }

        return result.Instances.Count switch
        {
            1 => result.Instances[0].Value,
            0 => Reading.NotAvailable("the counter reported no instance"),
            var n => Reading.NotAvailable(string.Create(CultureInfo.InvariantCulture, $"the counter reported {n} instances where one was expected")),
        };
    }

    private static Reading Total(CounterResult result)
    {
        if (result.UnavailableReason is { } why)
        {
            return Reading.NotAvailable(why);
        }

        var total = result.Instances.FirstOrDefault(i => i.Instance == "_Total");
        return total is null ? Reading.NotAvailable("the _Total instance was not reported") : total.Value;
    }

    private static (IReadOnlyList<CoreSample>, string?) BuildCores(CounterResult utility, CounterResult frequency, List<string> notes)
    {
        if (utility.UnavailableReason is { } why)
        {
            return ([], why);
        }

        var frequencyByInstance = frequency.UnavailableReason is null
            ? frequency.Instances.GroupBy(i => i.Instance, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Value, StringComparer.Ordinal)
            : null;

        var cores = new List<CoreSample>();
        foreach (var item in utility.Instances)
        {
            if (item.Instance == "_Total" || NodeTotalInstance().IsMatch(item.Instance))
            {
                continue;
            }

            var match = CoreInstance().Match(item.Instance);
            if (!match.Success)
            {
                notes.Add($"unrecognized processor instance '{item.Instance}' was skipped");
                continue;
            }

            Reading hz;
            if (frequencyByInstance is null)
            {
                hz = Reading.NotAvailable(frequency.UnavailableReason!);
            }
            else
            {
                hz = frequencyByInstance.TryGetValue(item.Instance, out var found) ? found : Reading.NotAvailable("the frequency counter did not report this processor");
            }

            cores.Add(new CoreSample(
                item.Instance,
                int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                item.Value,
                hz));
        }

        var ordered = cores.OrderBy(c => c.Node).ThenBy(c => c.Index).ToList();
        return (ordered, ordered.Count == 0 ? "the counter reported no per-processor instance" : null);
    }

    private static (IReadOnlyList<DiskSample>, string?) BuildDisks(CounterResult latency, CounterResult queue)
    {
        if (latency.UnavailableReason is { } a && queue.UnavailableReason is { } b)
        {
            return ([], a == b ? a : $"{a}; {b}");
        }

        static Dictionary<string, Reading> ByInstance(CounterResult r) => r.UnavailableReason is null
            ? r.Instances.Where(i => i.Instance != "_Total").GroupBy(i => i.Instance, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.Ordinal)
            : [];

        var lat = ByInstance(latency);
        var que = ByInstance(queue);
        var rows = lat.Keys.Concat(que.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(name => new DiskSample(
            name,
            Lookup(lat, name, latency.UnavailableReason),
            Lookup(que, name, queue.UnavailableReason))).ToList();
        return (rows, rows.Count == 0 ? "the disk counters reported no instance" : null);
    }

    private static Reading Lookup(Dictionary<string, Reading> values, string instance, string? counterReason) =>
        values.TryGetValue(instance, out var v) ? v : Reading.NotAvailable(counterReason ?? "the counter did not report this instance");

    private static (IReadOnlyList<GpuAdapterSample>, string?) BuildGpus(
        CounterResult engines, CounterResult dedicated, CounterResult shared, IReadOnlyCollection<Luid> known, List<string> notes)
    {
        var dedicatedByLuid = MemoryByLuid(dedicated, notes);
        var sharedByLuid = MemoryByLuid(shared, notes);

        var expected = known.Concat(dedicatedByLuid.Keys).Concat(sharedByLuid.Keys).ToList();
        var utilization = new Dictionary<Luid, GpuAdapterUtilization>();
        if (engines.UnavailableReason is { } engineReason)
        {
            foreach (var luid in expected.Distinct())
            {
                utilization[luid] = new GpuAdapterUtilization(luid, [], 0, engineReason);
            }
        }
        else
        {
            var aggregation = GpuEngineAggregator.Aggregate(engines.Instances.Select(i => new EngineInstanceValue(i.Instance, i.Value)), expected);
            foreach (var adapter in aggregation.Adapters)
            {
                utilization[adapter.Luid] = adapter;
            }

            notes.AddRange(aggregation.Unparsed.Select(u => $"unparsable GPU Engine instance '{u.Name}' was skipped: {u.Reason}"));
        }

        var luids = utilization.Keys.Concat(expected).Distinct().Order().ToList();
        var samples = luids.Select(luid => new GpuAdapterSample(
            luid,
            utilization.TryGetValue(luid, out var u) ? u : new GpuAdapterUtilization(luid, [], 0, "no GPU Engine counter instance is reported for this adapter"),
            MemoryReading(dedicated, dedicatedByLuid, luid),
            MemoryReading(shared, sharedByLuid, luid))).ToList();

        if (engines.UnavailableReason is { } e && dedicated.UnavailableReason is { } d && shared.UnavailableReason is { } s)
        {
            return (samples, string.Join("; ", new[] { e, d, s }.Distinct()));
        }

        return (samples, samples.Count == 0 ? "the GPU counters reported no adapter" : null);
    }

    private static Dictionary<Luid, Reading> MemoryByLuid(CounterResult result, List<string> notes)
    {
        var sums = new Dictionary<Luid, (double Sum, int Available, string? Why)>();
        if (result.UnavailableReason is not null)
        {
            return [];
        }

        foreach (var item in result.Instances)
        {
            if (!GpuCounterNames.TryParseAdapterMemory(item.Instance, out var instance, out var why))
            {
                notes.Add($"unparsable GPU Adapter Memory instance '{item.Instance}' was skipped: {why}");
                continue;
            }

            sums.TryGetValue(instance.Luid, out var state);
            sums[instance.Luid] = item.Value.Value is { } v
                ? (state.Sum + v, state.Available + 1, state.Why)
                : (state.Sum, state.Available, state.Why ?? item.Value.UnavailableReason);
        }

        return sums.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Available > 0 ? Reading.Of(kv.Value.Sum) : Reading.NotAvailable(kv.Value.Why ?? "no value"));
    }

    private static Reading MemoryReading(CounterResult counter, Dictionary<Luid, Reading> byLuid, Luid luid)
    {
        if (counter.UnavailableReason is { } why)
        {
            return Reading.NotAvailable(why);
        }

        return byLuid.TryGetValue(luid, out var value) ? value : Reading.NotAvailable("no GPU Adapter Memory instance is reported for this adapter");
    }
}
