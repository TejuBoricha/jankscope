using System.Buffers.Binary;

namespace Jankscope.Core.Tests.Hardware;

/// <summary>
/// Builds <c>GetLogicalProcessorInformationEx(RelationAll)</c> buffers byte for byte from the documented
/// SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX layout (winnt.h, 64-bit): an 8-byte header (Relationship, Size) followed by the union.
/// Fixtures that are not observed on a machine are labeled synthetic by the machine they model.
/// </summary>
internal sealed class LpieBuilder
{
    public const int RelationCore = 0;
    public const int RelationNumaNode = 1;
    public const int RelationCache = 2;
    public const int RelationPackage = 3;
    public const int RelationGroup = 4;
    public const int RelationModule = 7;

    private readonly List<byte[]> _records = [];

    public LpieBuilder Core(ushort group, ulong mask, byte efficiencyClass, bool smt) =>
        Add(Processor(RelationCore, smt ? (byte)1 : (byte)0, efficiencyClass, [(group, mask)]));

    public LpieBuilder Package(ushort group, ulong mask) => Add(Processor(RelationPackage, 0, 0, [(group, mask)]));

    public LpieBuilder Module(ushort group, ulong mask) => Add(Processor(RelationModule, 0, 0, [(group, mask)]));

    public LpieBuilder Cache(byte level, int type, uint sizeBytes, params (ushort Group, ulong Mask)[] masks) =>
        Add(CacheRecord(level, type, sizeBytes, (ushort)masks.Length, masks));

    /// <summary>A cache record written the way older Windows builds do: the group count field is zero but one group mask follows.</summary>
    public LpieBuilder CacheWithZeroGroupCount(byte level, uint sizeBytes, ushort group, ulong mask) =>
        Add(CacheRecord(level, 0, sizeBytes, 0, [(group, mask)]));

    public LpieBuilder NumaNode(uint node, ushort group, ulong mask)
    {
        var record = new byte[48];
        Header(record, RelationNumaNode);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(8), node);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(8 + 22), 1);
        GroupAffinity(record.AsSpan(8 + 24), group, mask);
        return Add(record);
    }

    public LpieBuilder Groups(int active)
    {
        var record = new byte[8 + 24 + 48 * active];
        Header(record, RelationGroup);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(8), (ushort)active);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(10), (ushort)active);
        return Add(record);
    }

    /// <summary>A record of a relationship kind this code does not interpret (forward compatibility).</summary>
    public LpieBuilder Unknown(int relationship, int payloadBytes)
    {
        var record = new byte[8 + payloadBytes];
        Header(record, relationship);
        return Add(record);
    }

    public LpieBuilder Raw(byte[] record) => Add(record);

    public byte[] Build() => _records.SelectMany(r => r).ToArray();

    private LpieBuilder Add(byte[] record)
    {
        _records.Add(record);
        return this;
    }

    private static void Header(byte[] record, int relationship)
    {
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(0), relationship);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(4), record.Length);
    }

    private static void GroupAffinity(Span<byte> destination, ushort group, ulong mask)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(destination, mask);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[8..], group);
    }

    /// <summary>PROCESSOR_RELATIONSHIP: Flags, EfficiencyClass, Reserved[20], GroupCount, GroupMask[]. Used by cores, packages and modules.</summary>
    private static byte[] Processor(int relationship, byte flags, byte efficiencyClass, (ushort Group, ulong Mask)[] masks)
    {
        var record = new byte[8 + 24 + 16 * masks.Length];
        Header(record, relationship);
        record[8] = flags;
        record[9] = efficiencyClass;
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(8 + 22), (ushort)masks.Length);
        for (var i = 0; i < masks.Length; i++)
        {
            GroupAffinity(record.AsSpan(8 + 24 + 16 * i), masks[i].Group, masks[i].Mask);
        }

        return record;
    }

    /// <summary>CACHE_RELATIONSHIP: Level, Associativity, LineSize, CacheSize, Type, Reserved[18], GroupCount, GroupMasks[].</summary>
    private static byte[] CacheRecord(byte level, int type, uint sizeBytes, ushort groupCountField, (ushort Group, ulong Mask)[] masks)
    {
        var record = new byte[8 + 32 + 16 * masks.Length];
        Header(record, RelationCache);
        record[8] = level;
        record[9] = 8;
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(10), 64);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(12), sizeBytes);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(16), type);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(8 + 30), groupCountField);
        for (var i = 0; i < masks.Length; i++)
        {
            GroupAffinity(record.AsSpan(8 + 32 + 16 * i), masks[i].Group, masks[i].Mask);
        }

        return record;
    }
}

/// <summary>Synthetic machines (documented layouts, invented values) for the topology mapper. None of these is an observation.</summary>
internal static class SyntheticMachines
{
    private const uint MiB = 1024 * 1024;

    /// <summary>Hybrid 6 performance cores (SMT, class 1) + 8 efficiency cores (no SMT, class 0): 14 cores, 20 logical processors, one L3.</summary>
    public static byte[] Hybrid6P8E()
    {
        var b = new LpieBuilder().Package(0, 0xFFFFF).NumaNode(0, 0, 0xFFFFF).Groups(1);
        for (var i = 0; i < 6; i++)
        {
            b.Core(0, 0x3UL << (2 * i), 1, smt: true);
        }

        for (var i = 0; i < 8; i++)
        {
            b.Core(0, 1UL << (12 + i), 0, smt: false);
        }

        return b.Cache(3, 0, 24 * MiB, (0, 0xFFFFF)).Build();
    }

    /// <summary>Non-hybrid 8 cores with SMT, 16 logical processors, one L3.</summary>
    public static byte[] Smt8C16T()
    {
        var b = new LpieBuilder().Package(0, 0xFFFF).NumaNode(0, 0, 0xFFFF).Groups(1);
        for (var i = 0; i < 8; i++)
        {
            b.Core(0, 0x3UL << (2 * i), 0, smt: true);
        }

        return b.Cache(3, 0, 16 * MiB, (0, 0xFFFF)).Build();
    }

    /// <summary>Non-hybrid 8 cores, SMT disabled: 8 logical processors.</summary>
    public static byte[] SmtOff8C8T()
    {
        var b = new LpieBuilder().Package(0, 0xFF).NumaNode(0, 0, 0xFF).Groups(1);
        for (var i = 0; i < 8; i++)
        {
            b.Core(0, 1UL << i, 0, smt: false);
        }

        return b.Cache(3, 0, 16 * MiB, (0, 0xFF)).Build();
    }

    /// <summary>16 cores / 32 threads in two core complexes, each with its own L3 of the given size.</summary>
    public static byte[] TwoL3Domains(uint firstL3Bytes, uint secondL3Bytes)
    {
        var b = new LpieBuilder().Package(0, 0xFFFFFFFF).NumaNode(0, 0, 0xFFFFFFFF).Groups(1);
        for (var i = 0; i < 16; i++)
        {
            b.Core(0, 0x3UL << (2 * i), 0, smt: true);
        }

        return b.Cache(3, 0, firstL3Bytes, (0, 0xFFFF)).Cache(3, 0, secondL3Bytes, (0, 0xFFFF0000)).Build();
    }

    /// <summary>Two processor groups of 64 logical processors each: 64 cores with SMT, 128 logical processors.</summary>
    public static byte[] TwoProcessorGroups()
    {
        var b = new LpieBuilder().Package(0, ulong.MaxValue).Package(1, ulong.MaxValue).Groups(2);
        for (ushort g = 0; g < 2; g++)
        {
            for (var i = 0; i < 32; i++)
            {
                b.Core(g, 0x3UL << (2 * i), 0, smt: true);
            }
        }

        return b.Build();
    }

    /// <summary>Three efficiency classes (low-power efficiency 0, efficiency 1, performance 2): 2 + 4 + 4 cores, no SMT.</summary>
    public static byte[] ThreeClasses()
    {
        var b = new LpieBuilder().Package(0, 0x3FF).Groups(1);
        for (var i = 0; i < 2; i++)
        {
            b.Core(0, 1UL << i, 0, smt: false);
        }

        for (var i = 2; i < 6; i++)
        {
            b.Core(0, 1UL << i, 1, smt: false);
        }

        for (var i = 6; i < 10; i++)
        {
            b.Core(0, 1UL << i, 2, smt: false);
        }

        return b.Build();
    }

    /// <summary>Two cores with 2 and 4 logical processors in a 4-way machine used by the scaling tests (at most 4 logical processors).</summary>
    public static byte[] Small2C4T()
    {
        var b = new LpieBuilder().Package(0, 0xF).NumaNode(0, 0, 0xF).Groups(1);
        b.Core(0, 0x3, 0, smt: true).Core(0, 0xC, 0, smt: true);
        return b.Cache(3, 0, 4 * MiB, (0, 0xF)).Build();
    }
}

internal static class HardwareFixtures
{
    public static string Dir => Path.Combine(Repo.Root, "tests", "Jankscope.Core.Tests", "Hardware", "Fixtures");

    /// <summary>Non-blank, non-comment lines of a fixture file.</summary>
    public static string[] Lines(string file) =>
        File.ReadAllLines(Path.Combine(Dir, file)).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToArray();

    public static byte[] Hex(string file) => Convert.FromHexString(string.Concat(Lines(file)));
}
