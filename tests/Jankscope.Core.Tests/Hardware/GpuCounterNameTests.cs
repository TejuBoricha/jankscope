using Jankscope.Core.Hardware;

namespace Jankscope.Core.Tests.Hardware;

public class GpuCounterNameTests
{
    private const string Engine = "pid_1000_luid_0x00000000_0x00011FD6_phys_0_eng_13_engtype_Security_1";

    [Fact]
    public void An_engine_instance_name_is_split_into_pid_luid_engine_and_engine_type()
    {
        Assert.True(GpuCounterNames.TryParseEngine(Engine, out var i, out var reason), reason);
        Assert.Equal(new GpuEngineInstance(1000, new Luid(0, 0x11FD6), 0, 13, "Security_1", null), i);
    }

    [Theory]
    [InlineData("pid_4_luid_0x00000000_0x00011B0C_phys_0_eng_0_engtype_3D", 4u, 0x11B0Cu, 0u, 0u, "3D")]
    [InlineData("pid_0_luid_0x00000000_0x00011B0C_phys_1_eng_7_engtype_VideoDecode", 0u, 0x11B0Cu, 1u, 7u, "VideoDecode")]
    [InlineData("pid_4294967295_luid_0x00000001_0xFFFFFFFF_phys_0_eng_0_engtype_OFA_0", 4294967295u, 0xFFFFFFFFu, 0u, 0u, "OFA_0")]
    public void Valid_names_parse_into_their_parts(string name, uint pid, uint luidLow, uint phys, uint eng, string type)
    {
        Assert.True(GpuCounterNames.TryParseEngine(name, out var i, out var reason), reason);
        Assert.Equal(pid, i!.Pid);
        Assert.Equal(luidLow, i.Luid.Low);
        Assert.Equal(phys, i.Phys);
        Assert.Equal(eng, i.EngineIndex);
        Assert.Equal(type, i.EngineType);
    }

    [Fact]
    public void A_duplicate_instance_suffix_is_recognised_and_kept_apart_from_the_engine_type()
    {
        Assert.True(GpuCounterNames.TryParseEngine(Engine + "#2", out var i, out var reason), reason);
        Assert.Equal("Security_1", i!.EngineType);
        Assert.Equal(2, i.DuplicateSuffix);
    }

    [Theory]
    [InlineData("")]
    [InlineData("pid_x_luid_0x00000000_0x00011FD6_phys_0_eng_0_engtype_3D")]
    [InlineData("pid_-1_luid_0x00000000_0x00011FD6_phys_0_eng_0_engtype_3D")]
    [InlineData("pid_4294967296_luid_0x00000000_0x00011FD6_phys_0_eng_0_engtype_3D")]
    [InlineData("PID_1_luid_0x00000000_0x00011FD6_phys_0_eng_0_engtype_3D")]
    [InlineData("pid_1_luid_0x00000000_0x00011FD6_phys_0_eng_0")]
    [InlineData("pid_1_luid_0x00000000_0x00011FD6_eng_0_engtype_3D")]
    [InlineData("pid_1_luid_0x00000000_phys_0_eng_0_engtype_3D")]
    [InlineData("pid_1_luid_00000000_00011FD6_phys_0_eng_0_engtype_3D")]
    [InlineData("pid_1_luid_0x00000000_0x00011FD6_phys_0_eng_0_engtype_3D;x")]
    [InlineData("pid_1_luid_0x00000000_0x00011FD6_phys_0_eng_0_engtype_3 D")]
    [InlineData(" pid_1_luid_0x00000000_0x00011FD6_phys_0_eng_0_engtype_3D")]
    [InlineData("pid_1_luid_0x00000000_0x00011FD6_phys_0_eng_0_engtype_3D#")]
    [InlineData("pid_1_luid_0x00000000_0x00011FD6_phys_0_eng_0_engtype_3D#-1")]
    [InlineData("pid_1_luid_0x00000000_0x00011FD6_phys_0_eng_0_engtype_3D#1x")]
    [InlineData("luid_0x00000000_0x00011FD6_phys_0_eng_0_engtype_3D_pid_1")]
    [InlineData("pid_1_luid_0x00000000_0x00011FD6_phys_a_eng_0_engtype_3D")]
    [InlineData("pid_1_luid_0x00000000_0x00011FD6_phys_0_eng_b_engtype_3D")]
    [InlineData("_Total")]
    [InlineData("pid__luid_0x00000000_0x00011FD6_phys_0_eng_0_engtype_3D")]
    public void Anything_that_is_not_exactly_the_documented_shape_is_rejected_with_a_reason(string name)
    {
        Assert.False(GpuCounterNames.TryParseEngine(name, out var i, out var reason));
        Assert.Null(i);
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Fact]
    public void An_engine_without_a_type_name_is_real_and_is_kept_as_an_empty_type()
    {
        // Observed on the reference bench: an integrated adapter reports several engines with nothing after "engtype_".
        Assert.True(GpuCounterNames.TryParseEngine("pid_1_luid_0x00000000_0x00011B0C_phys_0_eng_11_engtype_", out var i, out var reason), reason);
        Assert.Equal("", i!.EngineType);
        Assert.Equal(11u, i.EngineIndex);
    }

    [Theory]
    [InlineData("pid_1_luid_0x00000000_0x00011B0C_phys_0_eng_0_engtype__")]
    [InlineData("pid_1_luid_0x00000000_0x00011B0C_phys_0_eng_0_engtype__3D")]
    [InlineData("pid_1_luid_0x00000000_0x00011B0C_phys_0_eng_0_engtype_3D_")]
    public void A_type_name_may_not_start_or_end_with_an_underscore(string name)
    {
        Assert.False(GpuCounterNames.TryParseEngine(name, out _, out _));
    }

    [Fact]
    public void An_overlong_engine_type_is_rejected()
    {
        var name = "pid_1_luid_0x00000000_0x00011FD6_phys_0_eng_0_engtype_" + new string('a', 65);
        Assert.False(GpuCounterNames.TryParseEngine(name, out _, out _));
    }

    [Fact]
    public void Every_observed_engine_instance_on_the_reference_bench_parses()
    {
        var names = HardwareFixtures.Lines("gpu-engine-instances.observed-hybrid.txt");
        Assert.Equal(525, names.Length);
        foreach (var name in names)
        {
            Assert.True(GpuCounterNames.TryParseEngine(name, out _, out var reason), $"{name}: {reason}");
        }

        var parsed = names.Select(n => { GpuCounterNames.TryParseEngine(n, out var i, out _); return i!; }).ToList();
        Assert.Equal(3, parsed.Select(p => p.Luid).Distinct().Count());
        Assert.Equal(
            ["", "3D", "Copy", "LegacyOverlay", "OFA_0", "Security", "Security_1", "VR", "VideoDecode", "VideoEncode", "VideoProcessing"],
            parsed.Select(p => p.EngineType).Distinct().Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Adapter_memory_instances_parse_into_luid_and_phys()
    {
        Assert.True(GpuCounterNames.TryParseAdapterMemory("luid_0x00000000_0x00011B0C_phys_0", out var m, out var reason), reason);
        Assert.Equal(new GpuAdapterMemoryInstance(new Luid(0, 0x11B0C), 0, null), m);
    }

    [Fact]
    public void Every_observed_adapter_memory_instance_parses_and_names_a_luid_seen_in_the_engine_counters()
    {
        var engineLuids = HardwareFixtures.Lines("gpu-engine-instances.observed-hybrid.txt")
            .Select(n => { GpuCounterNames.TryParseEngine(n, out var i, out _); return i!.Luid; }).ToHashSet();
        var memory = HardwareFixtures.Lines("gpu-adapter-memory-instances.observed-hybrid.txt")
            .Select(n => { Assert.True(GpuCounterNames.TryParseAdapterMemory(n, out var i, out var r), $"{n}: {r}"); return i!.Luid; })
            .ToList();
        Assert.Equal(3, memory.Count);
        Assert.Equal(engineLuids.Order().ToArray(), memory.Order().ToArray());
    }

    [Theory]
    [InlineData("")]
    [InlineData("luid_0x00000000_0x00011B0C")]
    [InlineData("luid_0x00000000_0x00011B0C_phys_")]
    [InlineData("luid_0x00000000_0x00011B0C_phys_0_extra")]
    [InlineData("pid_1_luid_0x00000000_0x00011B0C_phys_0")]
    [InlineData("luid_zero_0x00011B0C_phys_0")]
    [InlineData("_Total")]
    public void Malformed_adapter_memory_instances_are_rejected(string name)
    {
        Assert.False(GpuCounterNames.TryParseAdapterMemory(name, out var i, out var reason));
        Assert.Null(i);
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Fact]
    public void The_parsers_never_throw_on_garbage()
    {
        var rng = new Random(7);
        const string alphabet = "pidluxphsengtyp_#-0123456789abcdefABCDEF 3DVR";
        for (var n = 0; n < 5000; n++)
        {
            var text = new string(Enumerable.Range(0, rng.Next(0, 90)).Select(_ => alphabet[rng.Next(alphabet.Length)]).ToArray());
            _ = GpuCounterNames.TryParseEngine(text, out _, out _);
            _ = GpuCounterNames.TryParseAdapterMemory(text, out _, out _);
        }
    }
}
