using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Jankscope.Core.Hardware;

/// <summary>One <c>\GPU Engine(...)</c> performance counter instance, decoded.</summary>
public sealed record GpuEngineInstance(uint Pid, Luid Luid, uint Phys, uint EngineIndex, string EngineType, int? DuplicateSuffix);

/// <summary>One <c>\GPU Adapter Memory(...)</c> performance counter instance, decoded.</summary>
public sealed record GpuAdapterMemoryInstance(Luid Luid, uint Phys, int? DuplicateSuffix);

/// <summary>
/// Pure parsers for the GPU counter instance names. Windows names engines
/// <c>pid_&lt;n&gt;_luid_0x&lt;hi&gt;_0x&lt;lo&gt;_phys_&lt;n&gt;_eng_&lt;n&gt;_engtype_&lt;name&gt;</c> and adapter memory
/// <c>luid_0x&lt;hi&gt;_0x&lt;lo&gt;_phys_&lt;n&gt;</c>; a duplicate instance gets a <c>#&lt;n&gt;</c> suffix. The parsers are strict: anything else
/// is rejected with a reason so the caller can report it (an engine type or adapter is never guessed).
/// </summary>
public static class GpuCounterNames
{
    private const int MaxEngineTypeLength = 64;

    public static bool TryParseEngine(string name, [NotNullWhen(true)] out GpuEngineInstance? instance, [NotNullWhen(false)] out string? reason)
    {
        instance = null;
        if (!SplitSuffix(name, out var body, out var suffix, out reason))
        {
            return false;
        }

        // pid, <n>, luid, 0x.., 0x.., phys, <n>, eng, <n>, engtype, <name...> : the engine type may itself contain underscores.
        var parts = body.Split('_');
        if (parts.Length < 11 || parts[0] != "pid" || parts[2] != "luid" || parts[5] != "phys" || parts[7] != "eng" || parts[9] != "engtype")
        {
            reason = "not of the form pid_<n>_luid_0x<hi>_0x<lo>_phys_<n>_eng_<n>_engtype_<name>";
            return false;
        }

        if (!TryNumber(parts[1], out var pid) || !TryNumber(parts[6], out var phys) || !TryNumber(parts[8], out var engine))
        {
            reason = "pid, phys and eng must be unsigned decimal numbers";
            return false;
        }

        if (!Luid.TryParse(parts[3] + "_" + parts[4], out var luid))
        {
            reason = "the LUID is not two 0x-prefixed hexadecimal words";
            return false;
        }

        // An empty type is real: some drivers report engines without a type name (observed on an integrated adapter). It is kept as
        // the empty string, never replaced by a guess.
        var type = string.Join('_', parts[10..]);
        if (type.Length > MaxEngineTypeLength || !type.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') || (type.Length > 0 && (type[0] == '_' || type[^1] == '_')))
        {
            reason = "the engine type must be empty or up to 64 letters, digits or underscores";
            return false;
        }

        instance = new GpuEngineInstance(pid, luid, phys, engine, type, suffix);
        reason = null;
        return true;
    }

    public static bool TryParseAdapterMemory(string name, [NotNullWhen(true)] out GpuAdapterMemoryInstance? instance, [NotNullWhen(false)] out string? reason)
    {
        instance = null;
        if (!SplitSuffix(name, out var body, out var suffix, out reason))
        {
            return false;
        }

        // luid, 0x.., 0x.., phys, <n>
        var parts = body.Split('_');
        if (parts.Length != 5 || parts[0] != "luid" || parts[3] != "phys")
        {
            reason = "not of the form luid_0x<hi>_0x<lo>_phys_<n>";
            return false;
        }

        if (!Luid.TryParse(parts[1] + "_" + parts[2], out var luid) || !TryNumber(parts[4], out var phys))
        {
            reason = "the LUID or the phys number is malformed";
            return false;
        }

        instance = new GpuAdapterMemoryInstance(luid, phys, suffix);
        reason = null;
        return true;
    }

    private static bool SplitSuffix(string name, out string body, out int? suffix, [NotNullWhen(false)] out string? reason)
    {
        body = name;
        suffix = null;
        reason = null;
        if (string.IsNullOrEmpty(name))
        {
            reason = "the instance name is empty";
            return false;
        }

        var hash = name.IndexOf('#');
        if (hash < 0)
        {
            return true;
        }

        if (!int.TryParse(name.AsSpan(hash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            reason = "the duplicate-instance suffix after '#' is not a number";
            return false;
        }

        body = name[..hash];
        suffix = number;
        return true;
    }

    private static bool TryNumber(string text, out uint value)
    {
        value = 0;
        return text.Length > 0 && text.All(char.IsAsciiDigit) && uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}

public sealed record EngineInstanceValue(string InstanceName, Reading Utilization);

/// <param name="EngineCount">Distinct engines (by phys and engine number) of this type that have at least one instance.</param>
/// <param name="PeakEnginePercent">The utilization of the busiest engine of this type (per engine: all processes added).</param>
/// <param name="SummedPercent">The utilization of all engines of this type added together.</param>
/// <param name="UnavailableInstances">Instances whose value could not be read; they are never counted as zero.</param>
public sealed record GpuEngineTypeUtilization(string EngineType, int EngineCount, Reading PeakEnginePercent, Reading SummedPercent, int UnavailableInstances);

/// <param name="NotAvailableReason">Non-null when the adapter has no GPU Engine counter instance at all (never reported as zero utilization).</param>
public sealed record GpuAdapterUtilization(Luid Luid, IReadOnlyList<GpuEngineTypeUtilization> ByEngineType, int InstanceCount, string? NotAvailableReason);

public sealed record UnparsedInstance(string Name, string Reason);

public sealed record GpuEngineAggregation(IReadOnlyList<GpuAdapterUtilization> Adapters, IReadOnlyList<UnparsedInstance> Unparsed);

/// <summary>
/// Aggregates GPU Engine utilization per adapter (by LUID) and per engine type. Per engine, all processes are added; per type, the
/// busiest engine (peak) and the total are reported, because the percentage of "the GPU" is not one number. Values are not clamped:
/// sampling skew can legitimately add up to slightly more than 100.
/// </summary>
public static class GpuEngineAggregator
{
    private sealed record EngineKey(Luid Luid, uint Phys, uint EngineIndex, string EngineType);

    public static GpuEngineAggregation Aggregate(IEnumerable<EngineInstanceValue> values, IEnumerable<Luid> expectedAdapters)
    {
        var unparsed = new List<UnparsedInstance>();
        var byEngine = new Dictionary<EngineKey, (double Sum, int Available, int Unavailable, string? Why)>();

        foreach (var item in values)
        {
            if (!GpuCounterNames.TryParseEngine(item.InstanceName, out var instance, out var why))
            {
                unparsed.Add(new UnparsedInstance(item.InstanceName, why));
                continue;
            }

            var key = new EngineKey(instance.Luid, instance.Phys, instance.EngineIndex, instance.EngineType);
            byEngine.TryGetValue(key, out var state);
            if (item.Utilization.Value is { } v)
            {
                byEngine[key] = (state.Sum + v, state.Available + 1, state.Unavailable, state.Why);
            }
            else
            {
                byEngine[key] = (state.Sum, state.Available, state.Unavailable + 1, state.Why ?? item.Utilization.UnavailableReason);
            }
        }

        var luids = byEngine.Keys.Select(k => k.Luid).Concat(expectedAdapters).Distinct().Order().ToList();
        var adapters = new List<GpuAdapterUtilization>();
        foreach (var luid in luids)
        {
            var engines = byEngine.Where(e => e.Key.Luid == luid).ToList();
            if (engines.Count == 0)
            {
                adapters.Add(new GpuAdapterUtilization(luid, [], 0, "no GPU Engine counter instance is reported for this adapter"));
                continue;
            }

            var types = engines
                .GroupBy(e => e.Key.EngineType)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => TypeRow(g.Key, g.Select(e => e.Value).ToList()))
                .ToList();
            adapters.Add(new GpuAdapterUtilization(luid, types, engines.Sum(e => e.Value.Available + e.Value.Unavailable), null));
        }

        return new GpuEngineAggregation(adapters, unparsed.OrderBy(u => u.Name, StringComparer.Ordinal).ToList());
    }

    private static GpuEngineTypeUtilization TypeRow(string type, List<(double Sum, int Available, int Unavailable, string? Why)> engines)
    {
        var unavailable = engines.Sum(e => e.Unavailable);
        var readable = engines.Where(e => e.Available > 0).ToList();
        if (readable.Count == 0)
        {
            var why = engines.Select(e => e.Why).FirstOrDefault(w => w is not null) ?? "no instance had a value";
            var none = Reading.NotAvailable($"all {unavailable} instance(s) of engine type {type} are unavailable: {why}");
            return new GpuEngineTypeUtilization(type, engines.Count, none, none, unavailable);
        }

        return new GpuEngineTypeUtilization(type, engines.Count, Reading.Of(readable.Max(e => e.Sum)), Reading.Of(readable.Sum(e => e.Sum)), unavailable);
    }
}
