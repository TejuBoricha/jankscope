using System.Buffers.Binary;
using System.Globalization;
using Jankscope.Core.Hardware;

namespace Jankscope.Core.Tests.Hardware;

/// <summary>Writes DISPLAYCONFIG_PATH_INFO (72 bytes) byte for byte from the documented wingdi.h layout.</summary>
internal static class CcdBuilder
{
    public const uint InternalTechnology = 0x80000000;
    public const uint HdmiTechnology = 5;
    public const uint DisplayPortExternalTechnology = 10;

    public static byte[] Path(Luid sourceAdapter, uint sourceId, Luid targetAdapter, uint targetId, uint technology,
        uint refreshNumerator, uint refreshDenominator, bool active = true, bool targetAvailable = true)
    {
        var b = new byte[72];
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0), sourceAdapter.Low);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(4), sourceAdapter.High);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(8), sourceId);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(12), 0xFFFFFFFF);  // modeInfoIdx: invalid
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(16), 1);           // statusFlags: DISPLAYCONFIG_SOURCE_IN_USE

        const int t = 20;
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(t), targetAdapter.Low);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(t + 4), targetAdapter.High);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(t + 8), targetId);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(t + 12), 0xFFFFFFFF);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(t + 16), technology);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(t + 20), 1);      // rotation: identity
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(t + 24), 1);      // scaling: identity
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(t + 28), refreshNumerator);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(t + 32), refreshDenominator);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(t + 36), 1);      // scanline ordering: progressive
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(t + 40), targetAvailable ? 1 : 0);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(t + 44), 1);      // statusFlags: DISPLAYCONFIG_TARGET_IN_USE
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(68), active ? 1u : 0u); // DISPLAYCONFIG_PATH_ACTIVE
        return b;
    }

    public static byte[] Many(params byte[][] paths) => paths.SelectMany(p => p).ToArray();

    /// <summary>The 32-byte DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO reply with the given flag word.</summary>
    public static byte[] AdvancedColorReply(uint flags, int type = 9)
    {
        var b = new byte[32];
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(0), type);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(4), 32);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(20), flags);
        return b;
    }
}

public class DisplayTopologyTests
{
    private static readonly Luid Igpu = new(0, 0x11B0C);
    private static readonly Luid Dgpu = new(0, 0x11FD6);

    private static DisplayPath Active(Luid adapter, uint target, uint num, uint den, uint tech = CcdBuilder.HdmiTechnology) =>
        new(adapter, 0, adapter, target, tech, new RationalHz(num, den), TargetAvailable: true, IsActive: true);

    // ---- parsing the observed bytes ----

    [Fact]
    public void The_observed_path_from_the_reference_bench_parses_into_an_internal_display_on_the_first_adapter()
    {
        var bytes = HardwareFixtures.Hex("ccd-paths.observed-single-internal.hex.txt");
        Assert.Equal(72, bytes.Length);

        var result = DisplayConfigParser.ParsePaths(bytes, 1);

        Assert.Empty(result.Problems);
        var path = Assert.Single(result.Paths);
        Assert.Equal(Igpu, path.TargetAdapter);
        Assert.Equal(Igpu, path.SourceAdapter);
        Assert.Equal(8388688u, path.TargetId);
        Assert.Equal(0x80000000u, path.OutputTechnology);
        Assert.True(path.IsActive);
        Assert.True(path.TargetAvailable);
        Assert.Equal(34690000u, path.Refresh.Numerator);
        Assert.Equal(240864u, path.Refresh.Denominator);
        Assert.InRange(path.Refresh.Hz!.Value, 144.0, 144.05);
    }

    [Fact]
    public void The_observed_advanced_color_reply_is_supported_but_not_enabled()
    {
        var reply = HardwareFixtures.Hex("ccd-advanced-color.observed.hex.txt");
        Assert.Equal(32, reply.Length);
        Assert.Equal(AdvancedColorState.SupportedNotEnabled, DisplayConfigParser.ParseAdvancedColor(reply));
    }

    [Theory]
    [InlineData(0u, AdvancedColorState.NotSupported)]
    [InlineData(1u, AdvancedColorState.SupportedNotEnabled)]
    [InlineData(3u, AdvancedColorState.Enabled)]
    [InlineData(7u, AdvancedColorState.Enabled)]
    [InlineData(5u, AdvancedColorState.SupportedNotEnabled)]
    [InlineData(2u, AdvancedColorState.Unknown)]   // enabled without supported cannot happen: do not guess
    [InlineData(8u, AdvancedColorState.NotSupported)]
    public void Advanced_color_flags_map_to_a_state(uint flags, AdvancedColorState expected)
    {
        Assert.Equal(expected, DisplayConfigParser.ParseAdvancedColor(CcdBuilder.AdvancedColorReply(flags)));
    }

    [Fact]
    public void A_reply_of_the_wrong_type_or_length_is_unknown_not_guessed()
    {
        Assert.Equal(AdvancedColorState.Unknown, DisplayConfigParser.ParseAdvancedColor(CcdBuilder.AdvancedColorReply(3, type: 15)));
        Assert.Equal(AdvancedColorState.Unknown, DisplayConfigParser.ParseAdvancedColor(new byte[12]));
        Assert.Equal(AdvancedColorState.Unknown, DisplayConfigParser.ParseAdvancedColor([]));
    }

    [Fact]
    public void The_advanced_color_request_names_the_target_in_the_documented_header_layout()
    {
        Span<byte> request = stackalloc byte[32];
        DisplayConfigParser.WriteAdvancedColorRequest(request, new Luid(2, 0x11B0C), 8388688);
        Assert.Equal(9, BinaryPrimitives.ReadInt32LittleEndian(request));
        Assert.Equal(32, BinaryPrimitives.ReadInt32LittleEndian(request[4..]));
        Assert.Equal(0x11B0Cu, BinaryPrimitives.ReadUInt32LittleEndian(request[8..]));
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(request[12..]));
        Assert.Equal(8388688u, BinaryPrimitives.ReadUInt32LittleEndian(request[16..]));
    }

    [Fact]
    public void Two_synthetic_paths_parse_with_their_own_adapters_and_rates()
    {
        var bytes = CcdBuilder.Many(
            CcdBuilder.Path(Igpu, 0, Igpu, 10, CcdBuilder.InternalTechnology, 144000, 1000),
            CcdBuilder.Path(Dgpu, 1, Dgpu, 20, CcdBuilder.DisplayPortExternalTechnology, 60000, 1000));
        var result = DisplayConfigParser.ParsePaths(bytes, 2);
        Assert.Empty(result.Problems);
        Assert.Equal([Igpu, Dgpu], result.Paths.Select(p => p.TargetAdapter).ToArray());
        Assert.Equal([10u, 20u], result.Paths.Select(p => p.TargetId).ToArray());
        Assert.Equal(60.0, result.Paths[1].Refresh.Hz);
    }

    [Fact]
    public void A_buffer_too_short_for_the_stated_path_count_keeps_the_complete_paths_and_states_the_problem()
    {
        var bytes = CcdBuilder.Many(
            CcdBuilder.Path(Igpu, 0, Igpu, 10, CcdBuilder.InternalTechnology, 144000, 1000),
            CcdBuilder.Path(Igpu, 1, Igpu, 11, CcdBuilder.HdmiTechnology, 60000, 1000));
        var result = DisplayConfigParser.ParsePaths(bytes.AsSpan(0, 100), 2);
        Assert.Single(result.Paths);
        Assert.NotEmpty(result.Problems);
    }

    [Fact]
    public void A_zero_path_count_is_an_empty_result_without_a_problem()
    {
        var result = DisplayConfigParser.ParsePaths([], 0);
        Assert.Empty(result.Paths);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void An_inactive_path_is_parsed_but_flagged_inactive()
    {
        var bytes = CcdBuilder.Path(Igpu, 0, Igpu, 10, CcdBuilder.HdmiTechnology, 60000, 1000, active: false);
        Assert.False(Assert.Single(DisplayConfigParser.ParsePaths(bytes, 1).Paths).IsActive);
    }

    // ---- refresh rates ----

    [Fact]
    public void A_zero_denominator_means_the_rate_is_unknown_not_infinite_or_zero()
    {
        Assert.Null(new RationalHz(60, 0).Hz);
        Assert.Null(new RationalHz(0, 0).Hz);
        Assert.Equal(0.0, new RationalHz(0, 1).Hz);
        Assert.Equal(59.94, new RationalHz(59940, 1000).Hz!.Value, 3);
    }

    [Fact]
    public void A_zero_hertz_rational_is_treated_as_unknown_by_the_analyzer_because_no_display_runs_at_0_hz()
    {
        var topology = DisplayTopologyAnalyzer.Analyze([Active(Igpu, 1, 0, 1)]);
        Assert.Null(Assert.Single(topology.Outputs).RefreshHz);
    }

    // ---- topology and mixed refresh ----

    [Fact]
    public void One_display_is_never_mixed_refresh()
    {
        var topology = DisplayTopologyAnalyzer.Analyze([Active(Igpu, 1, 144000, 1000)]);
        Assert.False(topology.MixedRefresh);
        Assert.Equal(144.0, topology.MinRefreshHz);
        Assert.Equal(144.0, topology.MaxRefreshHz);
    }

    [Fact]
    public void Different_nominal_rates_are_mixed_refresh()
    {
        var topology = DisplayTopologyAnalyzer.Analyze([Active(Igpu, 1, 144000, 1000), Active(Dgpu, 2, 60000, 1000)]);
        Assert.True(topology.MixedRefresh);
        Assert.Equal(60.0, topology.MinRefreshHz);
        Assert.Equal(144.0, topology.MaxRefreshHz);
    }

    [Theory]
    [InlineData(60000u, 1000u, 59940u, 1000u, false)]    // 60 vs 59.94 is the same nominal rate
    [InlineData(144000u, 1000u, 143990u, 1000u, false)]
    [InlineData(144000u, 1000u, 144900u, 1000u, false)]  // 0.9 Hz apart
    [InlineData(144000u, 1000u, 145000u, 1000u, false)]  // exactly 1.0 Hz apart: tolerance is inclusive
    [InlineData(144000u, 1000u, 145100u, 1000u, true)]   // 1.1 Hz apart
    [InlineData(60000u, 1000u, 75000u, 1000u, true)]
    [InlineData(120000u, 1000u, 144000u, 1000u, true)]
    public void The_tolerance_is_one_hertz_inclusive(uint n1, uint d1, uint n2, uint d2, bool mixed)
    {
        var topology = DisplayTopologyAnalyzer.Analyze([Active(Igpu, 1, n1, d1), Active(Igpu, 2, n2, d2)]);
        Assert.Equal(mixed, topology.MixedRefresh);
    }

    [Fact]
    public void Two_displays_where_one_rate_is_unknown_leave_mixed_refresh_unknown_when_the_known_ones_agree()
    {
        var topology = DisplayTopologyAnalyzer.Analyze([Active(Igpu, 1, 60000, 1000), Active(Igpu, 2, 60000, 0)]);
        Assert.Null(topology.MixedRefresh);
        Assert.Equal(2, topology.Outputs.Count);
    }

    [Fact]
    public void Three_displays_with_two_known_differing_rates_are_mixed_even_if_a_third_is_unknown()
    {
        var topology = DisplayTopologyAnalyzer.Analyze([Active(Igpu, 1, 60000, 1000), Active(Igpu, 2, 144000, 1000), Active(Igpu, 3, 0, 0)]);
        Assert.True(topology.MixedRefresh);
    }

    [Fact]
    public void No_active_display_is_an_empty_topology_with_unknown_mixed_state()
    {
        var topology = DisplayTopologyAnalyzer.Analyze([]);
        Assert.Empty(topology.Outputs);
        Assert.Null(topology.MixedRefresh);
        Assert.Null(topology.MinRefreshHz);
    }

    [Fact]
    public void Inactive_paths_are_not_displays()
    {
        var paths = new[] { Active(Igpu, 1, 144000, 1000), Active(Dgpu, 2, 60000, 1000) with { IsActive = false } };
        var topology = DisplayTopologyAnalyzer.Analyze(paths);
        Assert.Single(topology.Outputs);
        Assert.False(topology.MixedRefresh);
    }

    [Fact]
    public void The_same_target_listed_twice_is_one_display()
    {
        var topology = DisplayTopologyAnalyzer.Analyze([Active(Igpu, 1, 60000, 1000), Active(Igpu, 1, 60000, 1000)]);
        Assert.Single(topology.Outputs);
    }

    [Fact]
    public void The_adapter_that_drives_each_output_is_the_target_adapter_and_is_listed_once_per_adapter_sorted_by_luid()
    {
        var paths = new[]
        {
            Active(Dgpu, 1, 60000, 1000),
            Active(Igpu, 2, 144000, 1000),
            Active(Igpu, 3, 144000, 1000),
        };
        var topology = DisplayTopologyAnalyzer.Analyze(paths);
        Assert.Equal([Igpu, Dgpu], topology.DrivingAdapters.ToArray());
        Assert.Equal([1u, 2u, 3u], topology.Outputs.Select(o => o.TargetId).Order().ToArray());
        Assert.All(topology.Outputs, o => Assert.Equal(o.TargetId == 1 ? Dgpu : Igpu, o.DrivingAdapter));
    }

    [Fact]
    public void An_indirect_display_keeps_source_and_target_adapters_apart()
    {
        var path = new DisplayPath(Dgpu, 0, Igpu, 5, 16, new RationalHz(60000, 1000), true, true);
        var output = Assert.Single(DisplayTopologyAnalyzer.Analyze([path]).Outputs);
        Assert.Equal(Igpu, output.DrivingAdapter);
        Assert.Equal(Dgpu, output.SourceAdapter);
    }

    [Fact]
    public void Advanced_color_comes_from_the_supplied_lookup_and_defaults_to_unknown()
    {
        var path = Active(Igpu, 1, 60000, 1000);
        Assert.Equal(AdvancedColorState.Unknown, Assert.Single(DisplayTopologyAnalyzer.Analyze([path]).Outputs).AdvancedColor);
        Assert.Equal(AdvancedColorState.Enabled,
            Assert.Single(DisplayTopologyAnalyzer.Analyze([path], _ => AdvancedColorState.Enabled).Outputs).AdvancedColor);
    }

    // ---- technology names ----

    [Theory]
    [InlineData(0x80000000u, "internal", true)]
    [InlineData(5u, "HDMI", false)]
    [InlineData(10u, "DisplayPort (external)", false)]
    [InlineData(11u, "DisplayPort (embedded)", true)]
    [InlineData(6u, "LVDS", true)]
    [InlineData(15u, "Miracast", false)]
    [InlineData(0xFFFFFFFFu, "other", false)]
    [InlineData(12345u, "unrecognized (12345)", false)]
    public void Output_technology_codes_are_named_and_internal_ones_are_recognised(uint code, string name, bool internalPanel)
    {
        Assert.Equal(name, DisplayOutputTechnology.Describe(code));
        Assert.Equal(internalPanel, DisplayOutputTechnology.IsInternal(code));
    }

    // ---- text ----

    [Fact]
    public void The_description_lists_each_display_with_rate_technology_and_driving_adapter()
    {
        var topology = DisplayTopologyAnalyzer.Analyze(
            [Active(Igpu, 1, 34690000, 240864, CcdBuilder.InternalTechnology), Active(Dgpu, 2, 60000, 1000, CcdBuilder.HdmiTechnology)],
            _ => AdvancedColorState.SupportedNotEnabled);
        var text = topology.Describe();
        Assert.Contains("2 active display(s)", text, StringComparison.Ordinal);
        Assert.Contains("144.02 Hz", text, StringComparison.Ordinal);
        Assert.Contains("60 Hz", text, StringComparison.Ordinal);
        Assert.Contains("internal", text, StringComparison.Ordinal);
        Assert.Contains("HDMI", text, StringComparison.Ordinal);
        Assert.Contains(Igpu.ToString(), text, StringComparison.Ordinal);
        Assert.Contains(Dgpu.ToString(), text, StringComparison.Ordinal);
        Assert.Contains("mixed refresh rates: yes", text, StringComparison.Ordinal);
        Assert.Contains("advanced color (HDR or wide color gamut) supported, off", text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_refresh_rate_is_written_as_unknown_never_as_zero()
    {
        var text = DisplayTopologyAnalyzer.Analyze([Active(Igpu, 1, 0, 0)]).Describe();
        Assert.Contains("refresh rate unknown", text, StringComparison.Ordinal);
        Assert.DoesNotContain("0 Hz", text, StringComparison.Ordinal);
        Assert.Contains("mixed refresh rates: no", text, StringComparison.Ordinal); // a single display cannot be mixed
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("tr-TR")]
    public void The_description_does_not_depend_on_the_thread_culture(string culture)
    {
        var topology = DisplayTopologyAnalyzer.Analyze([Active(Igpu, 1, 34690000, 240864)]);
        var expected = topology.Describe();
        var old = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo(culture);
            Assert.Equal(expected, topology.Describe());
            Assert.Contains("144.02 Hz", expected, StringComparison.Ordinal);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = old;
        }
    }

    [Fact]
    public void Random_bytes_never_throw_in_the_parsers()
    {
        var rng = new Random(3);
        for (var i = 0; i < 300; i++)
        {
            var bytes = new byte[rng.Next(0, 400)];
            rng.NextBytes(bytes);
            var result = DisplayConfigParser.ParsePaths(bytes, rng.Next(0, 8));
            _ = DisplayTopologyAnalyzer.Analyze(result.Paths).Describe();
            _ = DisplayConfigParser.ParseAdvancedColor(bytes);
        }
    }
}
