using Jankscope.Core;
using Jankscope.Core.Hardware;
using Jankscope.Platform.Hardware;

namespace Jankscope.Platform.Tests.Hardware;

public class CpuTopologyProbeTests
{
    private static CapabilityEntry Row(ProcessorInfoReading reading) => Assert.Single(new CpuTopologyProbe(new FakeProcessorSource(reading)).Run());

    [Fact]
    public void The_observed_bytes_become_an_available_row_with_the_replaced_id_and_title()
    {
        var row = Row(new ProcessorInfoReading(HardwareRepo.ObservedProcessorBytes(), 16, null));
        Assert.Equal("cpu.topology", row.Id);
        Assert.Equal("CPU topology (hybrid cores, CCDs, SMT)", row.Title);
        Assert.Equal(Availability.Available, row.Availability);
        Assert.Equal(DataProvenance.Measured, row.Provenance);
        Assert.Contains("10 cores / 16 logical processors", row.Value, StringComparison.Ordinal);
        Assert.Contains("hybrid (6 performance cores / 12 logical, 4 efficiency cores / 4 logical)", row.Value, StringComparison.Ordinal);
        Assert.Contains("GetLogicalProcessorInformationEx", row.Source, StringComparison.Ordinal);
        Assert.Equal(CapabilityTier.A, row.Tier);
        Assert.Null(row.Reason);
    }

    [Fact]
    public void A_failed_read_is_unknown_with_the_failure_as_reason()
    {
        var row = Row(new ProcessorInfoReading(null, null, "GetLogicalProcessorInformationEx failed with Win32 error 5"));
        Assert.Equal(Availability.Unknown, row.Availability);
        Assert.Null(row.Value);
        Assert.Contains("Win32 error 5", row.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_record_buffer_is_unknown_with_a_reason_not_zero_cores()
    {
        var row = Row(new ProcessorInfoReading([], 8, null));
        Assert.Equal(Availability.Unknown, row.Availability);
        Assert.False(string.IsNullOrWhiteSpace(row.Reason));
        Assert.Null(row.Value);
    }

    [Fact]
    public void Disagreeing_counts_keep_the_row_available_but_state_the_discrepancy()
    {
        var row = Row(new ProcessorInfoReading(HardwareRepo.ObservedProcessorBytes(), 12, null));
        Assert.Equal(Availability.Available, row.Availability);
        Assert.Contains("12", row.Reason, StringComparison.Ordinal);
        Assert.Contains("16", row.Reason, StringComparison.Ordinal);
    }
}

public class GpuAdaptersProbeTests
{
    private static CapabilityEntry Row(AdapterEnumeration adapters, DisplayConfigReading displays) =>
        Assert.Single(new GpuAdaptersProbe(new FakeAdapterSource(adapters), new FakeDisplaySource(displays)).Run());

    private static readonly AdapterEnumeration HybridLaptop = new(
        [Fakes.Adapter(Fakes.Dgpu, "Discrete Test GPU", 0xAAAA), Fakes.Adapter(Fakes.Igpu, "Integrated Test GPU", 0xBBBB), Fakes.Adapter(Fakes.Soft, "Basic Render", 0x1414, software: true)],
        null);

    [Fact]
    public void Three_adapters_are_listed_by_luid_with_the_one_that_drives_the_display_marked()
    {
        var row = Row(HybridLaptop, Fakes.Displays(Fakes.Path(Fakes.Igpu, 10, 144000, 1000)));
        Assert.Equal("gpu.adapters", row.Id);
        Assert.Equal("GPU adapters keyed by LUID", row.Title);
        Assert.Equal(Availability.Available, row.Availability);
        Assert.StartsWith("3 adapter(s): ", row.Value, StringComparison.Ordinal);
        Assert.Contains($"LUID {Fakes.Igpu}", row.Value, StringComparison.Ordinal);
        Assert.Contains($"LUID {Fakes.Dgpu}", row.Value, StringComparison.Ordinal);
        Assert.Contains("software renderer", row.Value, StringComparison.Ordinal);
        Assert.Contains("drives 1 active display(s)", row.Value, StringComparison.Ordinal);
        Assert.Equal(2, row.Value!.Split("drives no active display").Length - 1);
    }

    [Fact]
    public void A_display_driven_by_the_discrete_adapter_is_attributed_to_it()
    {
        var row = Row(HybridLaptop, Fakes.Displays(Fakes.Path(Fakes.Dgpu, 10, 240000, 1000)));
        var dgpuPart = row.Value!.Split("; ").Single(p => p.Contains($"LUID {Fakes.Dgpu}", StringComparison.Ordinal));
        Assert.Contains("drives 1 active display(s)", dgpuPart, StringComparison.Ordinal);
    }

    [Fact]
    public void When_the_display_data_is_unreadable_the_adapters_are_still_listed_and_no_ownership_is_claimed()
    {
        var row = Row(HybridLaptop, Fakes.DisplayFailure("QueryDisplayConfig failed with Win32 error 50"));
        Assert.Equal(Availability.Available, row.Availability);
        Assert.DoesNotContain("drives", row.Value, StringComparison.Ordinal);
        Assert.Contains("Win32 error 50", row.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_enumeration_is_unknown_with_the_reason()
    {
        var row = Row(new AdapterEnumeration([], "the DXGI factory could not be created (HRESULT 0x80004005)"), Fakes.Displays());
        Assert.Equal(Availability.Unknown, row.Availability);
        Assert.Contains("0x80004005", row.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_successful_enumeration_with_no_adapter_is_not_available_not_zero_adapters()
    {
        var row = Row(new AdapterEnumeration([], null), Fakes.Displays());
        Assert.Equal(Availability.NotAvailable, row.Availability);
        Assert.False(string.IsNullOrWhiteSpace(row.Reason));
    }

    [Fact]
    public void A_partial_enumeration_is_reported_as_possibly_incomplete()
    {
        var row = Row(new AdapterEnumeration([Fakes.Adapter(Fakes.Igpu, "Only one")], "adapter enumeration failed (HRESULT 0x887A0005)"), Fakes.Displays());
        Assert.Equal(Availability.Available, row.Availability);
        Assert.Contains("incomplete", row.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void The_text_states_facts_and_draws_no_conclusion_about_where_a_game_runs()
    {
        var row = Row(HybridLaptop, Fakes.Displays(Fakes.Path(Fakes.Igpu, 10, 144000, 1000)));
        foreach (var word in new[] { "game on", "should", "recommend", "slow", "primary" })
        {
            Assert.DoesNotContain(word, row.Value, StringComparison.OrdinalIgnoreCase);
        }
    }
}

public class DisplayTopologyProbeTests
{
    private static IReadOnlyList<CapabilityEntry> Rows(DisplayConfigReading reading) => new DisplayTopologyProbe(new FakeDisplaySource(reading)).Run();

    [Fact]
    public void Two_displays_with_different_rates_are_reported_as_mixed_refresh()
    {
        var rows = Rows(Fakes.Displays(Fakes.Path(Fakes.Igpu, 1, 144000, 1000, tech: 0x80000000), Fakes.Path(Fakes.Dgpu, 2, 60000, 1000)));
        var topology = rows.Single(r => r.Id == "display.topology");
        Assert.Equal(Availability.Available, topology.Availability);
        Assert.Equal("Displays, refresh rates, VRR", topology.Title);
        Assert.Contains("2 active display(s)", topology.Value, StringComparison.Ordinal);
        Assert.Contains("mixed refresh rates: yes", topology.Value, StringComparison.Ordinal);
        Assert.Contains("144 Hz", topology.Value, StringComparison.Ordinal);
        Assert.Contains("60 Hz", topology.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Vrr_and_hdr_are_always_explicit_not_available_rows_with_a_reason()
    {
        foreach (var reading in new[] { Fakes.Displays(Fakes.Path(Fakes.Igpu, 1, 60000, 1000)), Fakes.Displays(), Fakes.DisplayFailure("boom") })
        {
            var rows = Rows(reading);
            var vrr = rows.Single(r => r.Id == "display.vrr");
            var hdr = rows.Single(r => r.Id == "display.hdr");
            Assert.Equal(Availability.NotAvailable, vrr.Availability);
            Assert.Equal(Availability.NotAvailable, hdr.Availability);
            Assert.Null(vrr.Value);
            Assert.Null(hdr.Value);
            Assert.Contains("variable-refresh-rate", vrr.Reason, StringComparison.Ordinal);
            Assert.Contains("wide color gamut", hdr.Reason, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void No_active_path_is_not_available_with_a_reason_not_zero_displays()
    {
        var topology = Rows(Fakes.Displays()).Single(r => r.Id == "display.topology");
        Assert.Equal(Availability.NotAvailable, topology.Availability);
        Assert.Contains("no active display", topology.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_query_is_unknown_with_the_reason()
    {
        var topology = Rows(Fakes.DisplayFailure("QueryDisplayConfig failed with Win32 error 50")).Single(r => r.Id == "display.topology");
        Assert.Equal(Availability.Unknown, topology.Availability);
        Assert.Contains("Win32 error 50", topology.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void The_advanced_color_state_per_target_reaches_the_text()
    {
        var path = Fakes.Path(Fakes.Igpu, 7, 60000, 1000);
        var reading = new DisplayConfigReading([path], new Dictionary<(Luid, uint), AdvancedColorState> { [(Fakes.Igpu, 7u)] = AdvancedColorState.Enabled }, [], null);
        var topology = Rows(reading).Single(r => r.Id == "display.topology");
        Assert.Contains("advanced color (HDR or wide color gamut) on", topology.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void The_replacement_row_keeps_the_id_and_title_of_the_planned_row()
    {
        // The title and tier the Planned row used to carry, now fixed here because the Planned row was removed when the probe was registered.
        var row = Rows(Fakes.Displays(Fakes.Path(Fakes.Igpu, 1, 60000, 1000))).Single(r => r.Id == "display.topology");
        Assert.Equal("Displays, refresh rates, VRR", row.Title);
        Assert.Equal(CapabilityTier.A, row.Tier);
    }
}

public class VirtualMachineProbeTests
{
    private static CapabilityEntry Row(IReadOnlyList<VmMarkerEntry> markers, FirmwareReading reading) =>
        Assert.Single(new VirtualMachineProbe(markers, new FakeFirmwareSource(reading)).Run());

    private static readonly IReadOnlyList<VmMarkerEntry> Catalog = [Fakes.InventedGuest];

    [Fact]
    public void A_guest_is_reported_with_the_exact_prefix_and_the_catalog_name()
    {
        var row = Row(Catalog, new FirmwareReading(Fakes.Strings("InventedSoft Inc.", "Whatever"), null));
        Assert.Equal("environment.virtual_machine", row.Id);
        Assert.Equal("Virtual machine detection", row.Title);
        Assert.Equal(Availability.Available, row.Availability);
        Assert.Equal(ManagedValues.VmPrefix + "Invented Hypervisor", row.Value);
        Assert.Equal("virtual machine: Invented Hypervisor", row.Value);
        Assert.True(ManagedValues.IsKnownVirtualMachineValue(row.Value!));
        Assert.Equal(DataProvenance.Inferred, row.Provenance);
    }

    [Fact]
    public void A_physical_machine_is_reported_with_the_exact_none_value()
    {
        var row = Row(Catalog, new FirmwareReading(Fakes.Strings("Plain Maker", "Plain Model"), null));
        Assert.Equal(Availability.Available, row.Availability);
        Assert.Equal(ManagedValues.VmNone, row.Value);
        Assert.Equal("not a virtual machine", row.Value);
        Assert.True(ManagedValues.IsKnownVirtualMachineValue(row.Value!));
    }

    [Fact]
    public void A_host_with_a_hypervisor_but_ordinary_strings_is_still_not_a_virtual_machine_and_says_why()
    {
        var row = Row(Catalog, new FirmwareReading(Fakes.Strings("Plain Maker", "Plain Model", hypervisor: true), null));
        Assert.Equal(ManagedValues.VmNone, row.Value);
        Assert.Contains("hypervisor present: yes", row.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void The_smbios_flag_alone_makes_an_unidentified_guest_with_the_generic_name()
    {
        var row = Row(Catalog, new FirmwareReading(Fakes.Strings("Plain Maker", "Plain Model", smbiosVm: true), null));
        Assert.Equal(ManagedValues.VmPrefix + VmDetector.UnidentifiedHypervisorName, row.Value);
        Assert.True(ManagedValues.IsKnownVirtualMachineValue(row.Value!));
    }

    [Fact]
    public void The_smbios_virtual_machine_flag_is_characteristic_44_and_not_43_which_means_uefi_is_supported()
    {
        // Review 1a, finding 14 (mutation L3): 43 is "UEFI Specification is supported" and is on nearly every modern PC; only 44 is the VM bit.
        Assert.True(WmiFirmwareStringSource.VirtualMachineFlagFrom(new ushort[] { 7, 11, 43, 44 }));
        Assert.False(WmiFirmwareStringSource.VirtualMachineFlagFrom(new ushort[] { 7, 11, 40, 42, 43 }));
        Assert.False(WmiFirmwareStringSource.VirtualMachineFlagFrom(Array.Empty<ushort>()));
        Assert.Null(WmiFirmwareStringSource.VirtualMachineFlagFrom(null)); // not readable is not "no"
        Assert.Null(WmiFirmwareStringSource.VirtualMachineFlagFrom("44"));
        Assert.Equal(44, WmiFirmwareStringSource.BiosCharacteristicVirtualMachine);
    }

    [Fact]
    public void A_physical_verdict_says_it_is_an_inference_so_the_row_does_not_claim_proof()
    {
        var row = Row(Catalog, new FirmwareReading(Fakes.Strings("Plain Maker", "Plain Model"), null));
        Assert.Equal(ManagedValues.VmNone, row.Value);
        Assert.Equal(DataProvenance.Inferred, row.Provenance);
        Assert.Contains("an inference, not proof", row.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Unreadable_strings_are_an_unavailable_unknown_row_never_the_none_value()
    {
        var row = Row(Catalog, new FirmwareReading(Fakes.Strings(null, null), null));
        Assert.Equal(Availability.Unknown, row.Availability);
        Assert.Null(row.Value);
        Assert.False(string.IsNullOrWhiteSpace(row.Reason));
    }

    [Fact]
    public void A_source_failure_is_an_unavailable_unknown_row()
    {
        var row = Row(Catalog, new FirmwareReading(Fakes.Strings(null, null), "none of Win32_ComputerSystem, Win32_BaseBoard and Win32_BIOS could be queried"));
        Assert.Equal(Availability.Unknown, row.Availability);
        Assert.Contains("Win32_ComputerSystem", row.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_catalog_is_unknown_never_none()
    {
        var row = Row([], new FirmwareReading(Fakes.Strings("Plain Maker", "Plain Model"), null));
        Assert.Equal(Availability.Unknown, row.Availability);
        Assert.Contains("catalog", row.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_optimize_mode_gate_understands_every_available_value_this_probe_can_produce_and_a_detected_vm_blocks()
    {
        // Review 1a, finding 5: a detected virtual machine is Recommend-only (spec section 6.6, ADR 0003); "not a virtual machine" is not a blocker.
        foreach (var (strings, detected) in new[]
        {
            (Fakes.Strings("InventedSoft", "x"), true),
            (Fakes.Strings("Plain", "Model"), false),
            (Fakes.Strings("Plain", "Model", smbiosVm: true), true),
        })
        {
            var vm = Row(Catalog, new FirmwareReading(strings, null));
            var blockers = Blockers(vm);
            Assert.DoesNotContain("virtual-machine state", blockers, StringComparison.Ordinal); // never "unknown" or "unrecognized": the gate reads the exact forms
            Assert.Equal(detected, blockers.Contains("virtual machine detected", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void An_unavailable_vm_row_makes_the_gate_report_the_state_as_unknown()
    {
        var vm = Row(Catalog, new FirmwareReading(Fakes.Strings(null, null), null));
        Assert.Contains("virtual-machine state unknown", Blockers(vm), StringComparison.Ordinal);
    }

    [Fact]
    public void A_reworded_value_would_be_a_blocker_so_the_exact_forms_matter()
    {
        var reworded = CapabilityEntry.Available("environment.virtual_machine", "x", CapabilityTier.A, "test", "bare metal");
        Assert.Contains("virtual-machine state", Blockers(reworded), StringComparison.Ordinal);
    }

    [Fact]
    public void Matching_the_catalog_is_not_affected_by_the_turkish_culture()
    {
        var old = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");
            var markers = new[] { new VmMarkerEntry("v", "Invented Box", ["INNOTEK"], [], [], "community") };
            var row = Row(markers, new FirmwareReading(Fakes.Strings("innotek GmbH", "VirtualBox"), null));
            Assert.Equal("virtual machine: Invented Box", row.Value);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = old;
        }
    }

    private static string Blockers(CapabilityEntry vm)
    {
        var derived = DerivedCapabilities.Compute([vm]);
        return derived.Single(e => e.Id == "mode.optimize").Reason ?? string.Empty;
    }
}

public class HardwareProbeSetTests
{
    [Fact]
    public void The_four_probes_replace_exactly_the_four_planned_rows_with_the_same_ids_and_titles()
    {
        // Titles the Planned rows carried before they were replaced (the Planned rows no longer exist).
        var titles = new Dictionary<string, string>
        {
            ["gpu.adapters"] = "GPU adapters keyed by LUID",
            ["cpu.topology"] = "CPU topology (hybrid cores, CCDs, SMT)",
            ["display.topology"] = "Displays, refresh rates, VRR",
            ["environment.virtual_machine"] = "Virtual machine detection",
        };
        var planned = new PlannedProbe().Run().Select(e => e.Id).ToHashSet();
        Assert.All(titles.Keys, id => Assert.DoesNotContain(id, planned)); // each id is produced by exactly one probe

        var matrix = new CapabilityMatrixBuilder(HardwareDiscoveryProbes.Create(CatalogSet.Empty)).Build();
        foreach (var (id, title) in titles)
        {
            var mine = Assert.Single(matrix.Entries, e => e.Id == id);
            Assert.Equal(title, mine.Title);
            Assert.NotEqual(Availability.NotImplemented, mine.Availability);
        }

        Assert.Equal(matrix.Entries.Count, matrix.Entries.Select(e => e.Id).Distinct().Count());
        Assert.All(matrix.ProbeHealth, h => Assert.True(h.Succeeded, $"{h.ProbeId}: {h.Error}"));
    }

    [Fact]
    public void The_probe_ids_are_unique_and_all_registered_in_the_default_set()
    {
        var mine = HardwareDiscoveryProbes.Create(CatalogSet.Empty).Select(p => p.Id).ToList();
        Assert.Equal(mine.Count, mine.Distinct().Count());
        var all = DefaultProbes.Create(CatalogSet.Empty).Select(p => p.Id).ToList();
        Assert.Equal(all.Count, all.Distinct().Count()); // no clash with the existing probes
        Assert.All(mine, id => Assert.Contains(id, all));
        Assert.Contains("pdh", all);
    }

    [Fact]
    public void Every_row_is_well_formed_with_a_source_and_a_value_or_a_reason()
    {
        var matrix = new CapabilityMatrixBuilder(HardwareDiscoveryProbes.Create(TestCatalogs.Shipped())).Build();
        Assert.All(matrix.Entries, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Source), e.Id);
            if (e.Availability == Availability.Available)
            {
                Assert.False(string.IsNullOrWhiteSpace(e.Value), e.Id);
            }
            else
            {
                Assert.False(string.IsNullOrWhiteSpace(e.Reason), e.Id);
            }
        });
    }
}
