using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;

namespace Jankscope.Core.Hardware;

/// <summary>One processor-group affinity: which logical processors of which group (GROUP_AFFINITY).</summary>
public readonly record struct GroupAffinity(ushort Group, ulong Mask)
{
    public int LogicalCount => BitOperations.PopCount(Mask);
}

/// <summary>A physical core as <c>GetLogicalProcessorInformationEx</c> reports it.</summary>
public sealed record RawProcessorCore(byte EfficiencyClass, bool SmtFlag, IReadOnlyList<GroupAffinity> Affinity)
{
    public int LogicalCount => Affinity.Sum(a => a.LogicalCount);
}

public enum CacheKind
{
    Unified,
    Instruction,
    Data,
    Trace,
    Unknown,
}

public sealed record RawCache(byte Level, CacheKind Kind, uint SizeBytes, IReadOnlyList<GroupAffinity> Affinity);

/// <summary>What the processor records say, before any interpretation. <see cref="Problems"/> lists everything that could not be read.</summary>
public sealed record RawProcessorTopology(
    IReadOnlyList<RawProcessorCore> Cores,
    IReadOnlyList<RawCache> Caches,
    int PackageCount,
    int NumaNodeCount,
    int ProcessorGroupCount,
    IReadOnlyList<string> Problems);

/// <summary>
/// Pure parser for the buffer <c>GetLogicalProcessorInformationEx(RelationAll)</c> fills in (SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX,
/// 64-bit layout). Nothing here trusts a size field: a bad record becomes a problem string and parsing stops or skips, it never
/// throws and never loops. Records of kinds this code does not interpret are skipped by their size (forward compatibility).
/// </summary>
public static class ProcessorInfoParser
{
    private const int RelationProcessorCore = 0;
    private const int RelationNumaNode = 1;
    private const int RelationCache = 2;
    private const int RelationProcessorPackage = 3;
    private const int RelationGroup = 4;
    private const int RelationNumaNodeEx = 6;

    private const int HeaderSize = 8;
    private const int GroupAffinitySize = 16;
    private const int ProcessorPayloadFixed = 24; // Flags, EfficiencyClass, Reserved[20], GroupCount
    private const int CachePayloadFixed = 32;     // Level .. GroupCount, before the group masks
    private const byte SmtFlagBit = 0x1;          // LTP_PC_SMT

    public static RawProcessorTopology Parse(ReadOnlySpan<byte> buffer)
    {
        var cores = new List<RawProcessorCore>();
        var caches = new List<RawCache>();
        var problems = new List<string>();
        var packages = 0;
        var numaNodes = 0;
        var activeGroups = 0;

        if (buffer.IsEmpty)
        {
            problems.Add("the processor information buffer is empty");
        }

        var offset = 0;
        while (offset < buffer.Length)
        {
            var remaining = buffer.Length - offset;
            if (remaining < HeaderSize)
            {
                problems.Add($"{remaining} trailing byte(s) at offset {offset} are too short for a record header (buffer truncated)");
                break;
            }

            var relationship = BinaryPrimitives.ReadInt32LittleEndian(buffer[offset..]);
            var size = BinaryPrimitives.ReadInt32LittleEndian(buffer[(offset + 4)..]);
            if (size < HeaderSize)
            {
                problems.Add($"record at offset {offset} has invalid size {size}; parsing stopped");
                break;
            }

            if (size > remaining)
            {
                problems.Add($"record at offset {offset} needs {size} bytes but only {remaining} remain (buffer truncated)");
                break;
            }

            var payload = buffer.Slice(offset + HeaderSize, size - HeaderSize);
            switch (relationship)
            {
                case RelationProcessorCore:
                    if (TryReadProcessor(payload, out var efficiency, out var flags, out var affinity))
                    {
                        cores.Add(new RawProcessorCore(efficiency, (flags & SmtFlagBit) != 0, affinity));
                    }
                    else
                    {
                        problems.Add($"processor core record at offset {offset} is malformed (group count does not fit the record); skipped");
                    }

                    break;
                case RelationCache:
                    if (TryReadCache(payload, out var cache))
                    {
                        caches.Add(cache);
                    }
                    else
                    {
                        problems.Add($"cache record at offset {offset} is malformed (group count does not fit the record); skipped");
                    }

                    break;
                case RelationProcessorPackage:
                    packages++;
                    break;
                case RelationNumaNode:
                case RelationNumaNodeEx:
                    numaNodes++;
                    break;
                case RelationGroup:
                    if (payload.Length >= 4)
                    {
                        activeGroups = Math.Max(activeGroups, BinaryPrimitives.ReadUInt16LittleEndian(payload[2..]));
                    }

                    break;
            }

            offset += size;
        }

        var groupsSeen = cores.SelectMany(c => c.Affinity).Select(a => a.Group).Distinct().Count();
        return new RawProcessorTopology(cores, caches, packages, numaNodes, Math.Max(activeGroups, groupsSeen), problems);
    }

    private static bool TryReadProcessor(ReadOnlySpan<byte> payload, out byte efficiencyClass, out byte flags, out List<GroupAffinity> affinity)
    {
        efficiencyClass = 0;
        flags = 0;
        affinity = [];
        if (payload.Length < ProcessorPayloadFixed)
        {
            return false;
        }

        flags = payload[0];
        efficiencyClass = payload[1];
        var count = BinaryPrimitives.ReadUInt16LittleEndian(payload[22..]);
        return TryReadAffinities(payload[ProcessorPayloadFixed..], count, affinity);
    }

    private static bool TryReadCache(ReadOnlySpan<byte> payload, out RawCache cache)
    {
        cache = null!;
        if (payload.Length < CachePayloadFixed)
        {
            return false;
        }

        var level = payload[0];
        var size = BinaryPrimitives.ReadUInt32LittleEndian(payload[4..]);
        var type = BinaryPrimitives.ReadInt32LittleEndian(payload[8..]);
        int count = BinaryPrimitives.ReadUInt16LittleEndian(payload[30..]);
        if (count == 0)
        {
            count = 1; // older Windows builds leave the group count at zero and write one GroupMask
        }

        var affinity = new List<GroupAffinity>();
        if (!TryReadAffinities(payload[CachePayloadFixed..], count, affinity))
        {
            return false;
        }

        var kind = type switch
        {
            0 => CacheKind.Unified,
            1 => CacheKind.Instruction,
            2 => CacheKind.Data,
            3 => CacheKind.Trace,
            _ => CacheKind.Unknown,
        };
        cache = new RawCache(level, kind, size, affinity);
        return true;
    }

    private static bool TryReadAffinities(ReadOnlySpan<byte> bytes, int count, List<GroupAffinity> into)
    {
        if (count > bytes.Length / GroupAffinitySize)
        {
            return false;
        }

        for (var i = 0; i < count; i++)
        {
            var at = bytes[(i * GroupAffinitySize)..];
            into.Add(new GroupAffinity(BinaryPrimitives.ReadUInt16LittleEndian(at[8..]), BinaryPrimitives.ReadUInt64LittleEndian(at)));
        }

        return true;
    }
}

public sealed record EfficiencyClassSummary(byte Class, int Cores, int LogicalProcessors);

/// <summary>
/// CPU topology as facts. Windows reports an efficiency class per core and a higher class means higher performance; the highest class
/// present is called "performance" here and every lower class "efficiency". On a machine with a single class nothing is hybrid and every
/// core counts as performance. No vendor, model or core count is assumed anywhere.
/// </summary>
public sealed record CpuTopology(
    int LogicalProcessors,
    int PhysicalCores,
    bool IsHybrid,
    int PerformanceCores,
    int PerformanceLogical,
    int EfficiencyCores,
    int EfficiencyLogical,
    IReadOnlyList<EfficiencyClassSummary> Classes,
    int SmtCores,
    int? L3Domains,
    IReadOnlyList<uint> L3SizesBytes,
    int PackageCount,
    int ProcessorGroupCount,
    int NumaNodeCount,
    IReadOnlyList<string> Problems)
{
    public bool SmtEnabled => SmtCores > 0;

    /// <summary>True when the L3 domains differ in size (a cache-asymmetric part).</summary>
    public bool L3SizesDiffer => L3SizesBytes.Distinct().Count() > 1;

    public string Describe()
    {
        var inv = CultureInfo.InvariantCulture;
        var hybrid = IsHybrid
            ? string.Create(inv, $"hybrid ({PerformanceCores} performance cores / {PerformanceLogical} logical, {EfficiencyCores} efficiency cores / {EfficiencyLogical} logical)")
            : "not hybrid";
        var smt = SmtEnabled ? string.Create(inv, $"SMT on ({SmtCores} of {PhysicalCores} cores)") : "SMT off";
        string l3;
        if (L3Domains is null)
        {
            l3 = "L3 cache: not reported";
        }
        else
        {
            var sizes = string.Join(", ", L3SizesBytes.Select(s => string.Create(inv, $"{s / 1048576.0:0.##} MiB")));
            var domains = L3Domains == 1 ? "1 domain" : string.Create(inv, $"{L3Domains} domains");
            l3 = $"L3 cache: {domains} ({sizes}{(L3SizesDiffer ? "; sizes differ" : string.Empty)})";
        }

        return string.Create(inv,
            $"{PhysicalCores} cores / {LogicalProcessors} logical processors; {hybrid}; {smt}; {l3}; {PackageCount} package(s); {ProcessorGroupCount} processor group(s); {NumaNodeCount} NUMA node(s)");
    }
}

/// <summary>Pure mapper from the raw processor records to <see cref="CpuTopology"/>.</summary>
public static class CpuTopologyMapper
{
    /// <param name="raw">The parsed records.</param>
    /// <param name="osLogicalProcessorCount">The count the OS reports separately (GetActiveProcessorCount); a disagreement is recorded as a problem. Null when unknown.</param>
    /// <returns>The topology, or null when the records contain no processor core at all.</returns>
    public static CpuTopology? Map(RawProcessorTopology raw, int? osLogicalProcessorCount)
    {
        if (raw.Cores.Count == 0)
        {
            return null;
        }

        var problems = new List<string>(raw.Problems);
        var logical = raw.Cores.Sum(c => c.LogicalCount);

        var classes = raw.Cores
            .GroupBy(c => c.EfficiencyClass)
            .OrderBy(g => g.Key)
            .Select(g => new EfficiencyClassSummary(g.Key, g.Count(), g.Sum(c => c.LogicalCount)))
            .ToList();
        var isHybrid = classes.Count > 1;
        var performanceClass = classes[^1];
        var performance = isHybrid ? performanceClass : new EfficiencyClassSummary(performanceClass.Class, raw.Cores.Count, logical);
        var efficiencyCores = raw.Cores.Count - performance.Cores;
        var efficiencyLogical = logical - performance.LogicalProcessors;

        var smtCores = raw.Cores.Count(c => c.LogicalCount > 1);
        var flagWithoutThreads = raw.Cores.Count(c => c.SmtFlag && c.LogicalCount <= 1);
        var threadsWithoutFlag = raw.Cores.Count(c => !c.SmtFlag && c.LogicalCount > 1);
        if (flagWithoutThreads > 0)
        {
            problems.Add($"{flagWithoutThreads} core(s) carry the SMT flag but expose a single logical processor; SMT is counted from the logical processors");
        }

        if (threadsWithoutFlag > 0)
        {
            problems.Add($"{threadsWithoutFlag} core(s) expose several logical processors without the SMT flag; SMT is counted from the logical processors");
        }

        if (osLogicalProcessorCount is { } os && os != logical)
        {
            problems.Add($"the processor records account for {logical} logical processors but the operating system reports {os}");
        }

        var l3 = raw.Caches.Where(c => c.Level == 3).ToList();
        return new CpuTopology(
            logical,
            raw.Cores.Count,
            isHybrid,
            performance.Cores,
            performance.LogicalProcessors,
            efficiencyCores,
            efficiencyLogical,
            classes,
            smtCores,
            l3.Count == 0 ? null : l3.Count,
            l3.Select(c => c.SizeBytes).ToList(),
            raw.PackageCount,
            raw.ProcessorGroupCount,
            raw.NumaNodeCount,
            problems);
    }
}
