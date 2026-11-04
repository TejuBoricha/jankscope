namespace Jankscope.Core.Hardware;

/// <summary>Stable ids of the performance counters the sampler reads. They key the snapshot the platform layer fills and the builder reads.</summary>
public static class CounterIds
{
    public const string CpuUtility = "cpu.utility";
    public const string CpuActualFrequency = "cpu.actual_frequency";
    public const string MemoryAvailableBytes = "memory.available_bytes";
    public const string MemoryCommittedBytes = "memory.committed_bytes";
    public const string MemoryCommitLimitBytes = "memory.commit_limit_bytes";
    public const string HardFaultsPerSecond = "memory.pages_input_per_sec";
    public const string DiskSecondsPerTransfer = "disk.avg_sec_per_transfer";
    public const string DiskQueueLength = "disk.queue_length";
    public const string GpuEngineUtilization = "gpu.engine_utilization";
    public const string GpuDedicatedUsage = "gpu.adapter_dedicated_usage";
    public const string GpuSharedUsage = "gpu.adapter_shared_usage";
}

/// <param name="EnglishPath">The counter path in English, added through the PDH English-name entry point so it resolves on any display language.</param>
/// <param name="Wildcard">True when the path has a wildcard instance (<c>(*)</c>); such a counter returns one value per live instance.</param>
/// <param name="NoCap100">True when the formatted value must not be capped at 100 (processor utility is relative to the base frequency and exceeds 100 in turbo).</param>
public sealed record CounterSpec(string Id, string EnglishPath, bool Wildcard, bool NoCap100);

/// <summary>
/// The counters of spec section 7.1 "System context". Hard faults are <c>Pages Input/sec</c>, the documented count of pages read from disk
/// to resolve hard page faults. GPU data is the vendor-neutral baseline (spec section 3.1): PDH only, no vendor API, which also means a
/// sleeping GPU is not woken.
/// </summary>
public static class CounterSpecs
{
    public static IReadOnlyList<CounterSpec> All { get; } =
    [
        New(CounterIds.CpuUtility, @"\Processor Information(*)\% Processor Utility", noCap100: true),
        New(CounterIds.CpuActualFrequency, @"\Processor Information(*)\Actual Frequency"),
        New(CounterIds.MemoryAvailableBytes, @"\Memory\Available Bytes"),
        New(CounterIds.MemoryCommittedBytes, @"\Memory\Committed Bytes"),
        New(CounterIds.MemoryCommitLimitBytes, @"\Memory\Commit Limit"),
        New(CounterIds.HardFaultsPerSecond, @"\Memory\Pages Input/sec"),
        New(CounterIds.DiskSecondsPerTransfer, @"\PhysicalDisk(*)\Avg. Disk sec/Transfer"),
        New(CounterIds.DiskQueueLength, @"\PhysicalDisk(*)\Current Disk Queue Length"),
        New(CounterIds.GpuEngineUtilization, @"\GPU Engine(*)\Utilization Percentage"),
        New(CounterIds.GpuDedicatedUsage, @"\GPU Adapter Memory(*)\Dedicated Usage"),
        New(CounterIds.GpuSharedUsage, @"\GPU Adapter Memory(*)\Shared Usage"),
    ];

    private static CounterSpec New(string id, string path, bool noCap100 = false) =>
        new(id, path, path.Contains("(*)", StringComparison.Ordinal), noCap100);
}

public sealed record InstanceReading(string Instance, Reading Value);

/// <summary>What one counter returned: its instances, or the reason it could not be read (never both, and never a silent empty list).</summary>
public sealed record CounterResult
{
    private CounterResult(IReadOnlyList<InstanceReading> instances, string? reason)
    {
        Instances = instances;
        UnavailableReason = reason;
    }

    public IReadOnlyList<InstanceReading> Instances { get; }

    public string? UnavailableReason { get; }

    public static CounterResult Of(IReadOnlyList<InstanceReading> instances) => new(instances, null);

    public static CounterResult Unavailable(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new CounterResult([], reason);
    }
}

/// <summary>One collection of every counter, by <see cref="CounterIds"/>.</summary>
public sealed class CounterSnapshot
{
    private readonly Dictionary<string, CounterResult> _results;

    public CounterSnapshot(IEnumerable<KeyValuePair<string, CounterResult>> results)
    {
        _results = new Dictionary<string, CounterResult>(results, StringComparer.Ordinal);
    }

    public CounterResult Get(string counterId) =>
        _results.TryGetValue(counterId, out var result) ? result : CounterResult.Unavailable("the counter was not requested");
}
