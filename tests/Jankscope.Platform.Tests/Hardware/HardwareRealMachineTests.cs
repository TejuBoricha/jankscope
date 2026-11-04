using System.Text.RegularExpressions;
using Jankscope.Core;
using Jankscope.Core.Hardware;
using Jankscope.Platform.Hardware;

namespace Jankscope.Platform.Tests.Hardware;

/// <summary>
/// Structural checks against the machine the tests run on: no assertion names a GPU, CPU, vendor or core count. They hold for an
/// unelevated user, for machines without a battery or discrete GPU, and when a source is unavailable (then the row must say why).
/// </summary>
public class HardwareRealMachineTests
{
    private static void AssertWellFormed(CapabilityEntry row)
    {
        Assert.False(string.IsNullOrWhiteSpace(row.Source), row.Id);
        if (row.Availability == Availability.Available)
        {
            Assert.False(string.IsNullOrWhiteSpace(row.Value), row.Id);
        }
        else
        {
            Assert.Null(row.Value);
            Assert.False(string.IsNullOrWhiteSpace(row.Reason), row.Id);
        }
    }

    [Fact]
    public void Performance_and_efficiency_logical_processors_sum_to_the_os_logical_processor_count()
    {
        var topology = CpuTopologyProbe.ReadTopology();
        Assert.NotNull(topology);
        var osCount = (int)NativeHardware.GetActiveProcessorCount(NativeHardware.AllProcessorGroups);

        Assert.True(osCount > 0);
        Assert.Equal(osCount, topology!.LogicalProcessors);
        Assert.Equal(osCount, topology.PerformanceLogical + topology.EfficiencyLogical);
        Assert.Equal(topology.PhysicalCores, topology.PerformanceCores + topology.EfficiencyCores);
        Assert.InRange(topology.PhysicalCores, 1, topology.LogicalProcessors);
        Assert.Equal(topology.IsHybrid, topology.EfficiencyCores > 0);
        Assert.True(topology.PerformanceCores > 0);
        Assert.Empty(topology.Problems);
        Assert.True(topology.PackageCount >= 1);
        Assert.True(topology.ProcessorGroupCount >= 1);
    }

    [Fact]
    public void The_dotnet_processor_count_never_exceeds_what_the_topology_found()
    {
        var topology = CpuTopologyProbe.ReadTopology();
        Assert.NotNull(topology);
        Assert.True(Environment.ProcessorCount <= topology!.LogicalProcessors);
    }

    [Fact]
    public void The_cpu_row_on_this_machine_is_well_formed()
    {
        var row = Assert.Single(new CpuTopologyProbe().Run());
        AssertWellFormed(row);
        Assert.Equal("cpu.topology", row.Id);
        Assert.Equal(Availability.Available, row.Availability);
    }

    [Fact]
    public void Adapters_are_unique_by_luid_and_each_carries_numeric_ids()
    {
        var enumeration = new DxgiAdapterSource().Enumerate();
        if (enumeration.Adapters.Count == 0)
        {
            return; // a machine without any adapter is legitimate (the row then says so)
        }

        Assert.Equal(enumeration.Adapters.Count, enumeration.Adapters.Select(a => a.Luid).Distinct().Count());
        Assert.All(enumeration.Adapters, a =>
        {
            Assert.False(string.IsNullOrWhiteSpace(a.Description));
            Assert.NotEqual(0u, a.VendorId);
            Assert.True(a.IsSoftware || a.DedicatedVideoMemoryBytes + a.SharedSystemMemoryBytes > 0);
        });
        Assert.Null(enumeration.Failure);
    }

    [Fact]
    public void The_gpu_row_on_this_machine_is_well_formed_and_never_says_zero_adapters_as_available()
    {
        var row = Assert.Single(new GpuAdaptersProbe().Run());
        AssertWellFormed(row);
        if (row.Availability == Availability.Available)
        {
            Assert.DoesNotContain("0 adapter(s)", row.Value, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Every_display_is_driven_by_an_adapter_dxgi_also_lists_and_has_a_plausible_refresh_rate()
    {
        var displays = new DisplayConfigSource().Read();
        var adapters = new DxgiAdapterSource().Enumerate();
        if (displays.Failure is not null || adapters.Adapters.Count == 0)
        {
            return; // nothing to compare on a headless or adapter-less machine
        }

        var topology = DisplayTopologyAnalyzer.Analyze(displays.Paths);
        var known = adapters.Adapters.Select(a => a.Luid).ToHashSet();
        foreach (var output in topology.Outputs)
        {
            // Two independent documented APIs (CCD and DXGI) must agree on the adapter identity.
            Assert.Contains(output.DrivingAdapter, known);
            if (output.RefreshHz is { } hz)
            {
                Assert.InRange(hz, 20.0, 1000.0);
            }
        }

        Assert.All(topology.DrivingAdapters, a => Assert.Contains(a, known));
    }

    [Fact]
    public void The_display_rows_on_this_machine_are_well_formed_including_the_explicit_vrr_and_hdr_rows()
    {
        var rows = new DisplayTopologyProbe().Run();
        Assert.All(rows, AssertWellFormed);
        Assert.Contains(rows, r => r.Id == "display.topology");
        Assert.Contains(rows, r => r.Id == "display.vrr" && r.Availability == Availability.NotAvailable);
        Assert.Contains(rows, r => r.Id == "display.hdr" && r.Availability == Availability.NotAvailable);
    }

    [Fact]
    public void The_vm_row_on_this_machine_is_a_gate_accepted_value_or_an_unavailable_row_with_a_reason()
    {
        var row = Assert.Single(new VirtualMachineProbe(TestCatalogs.Shipped()).Run());
        AssertWellFormed(row);
        if (row.Availability == Availability.Available)
        {
            Assert.True(ManagedValues.IsKnownVirtualMachineValue(row.Value!), row.Value);
        }
    }

    [Fact]
    public void The_firmware_strings_are_either_readable_or_the_reading_says_why_not()
    {
        var reading = new WmiFirmwareStringSource().Read();
        if (reading.Failure is not null)
        {
            Assert.False(string.IsNullOrWhiteSpace(reading.Failure));
        }
        else
        {
            Assert.True(reading.Strings.SystemManufacturer is not null || reading.Strings.BaseBoardManufacturer is not null || reading.Strings.BiosVendor is not null);
        }
    }

    // ---- the discovery code may not wake a sleeping GPU or reach for a vendor API (spec section 7.2) ----

    private static readonly string[] ForbiddenInDiscovery =
    [
        "EnumOutputs", "CreateDevice", "D3D11CreateDevice", "D3D12CreateDevice", "CheckInterfaceSupport", "CreateSwapChain",
        "D3DKMT", "LoadLibrary", "nvml", "nvapi", "atiadlxx", "igcl", "ADLX",
    ];

    private static string StripComments(string text) =>
        Regex.Replace(text, @"//[^\r\n]*|/\*.*?\*/", string.Empty, RegexOptions.Singleline);

    [Fact]
    public void The_hardware_and_counter_sources_use_no_device_creation_output_enumeration_or_vendor_library()
    {
        var files = new[] { "Hardware", "Pdh" }
            .SelectMany(d => Directory.EnumerateFiles(Path.Combine(HardwareRepo.Root, "src", "Jankscope.Platform", d), "*.cs", SearchOption.AllDirectories))
            .Concat([Path.Combine(HardwareRepo.Root, "src", "Jankscope.Platform", "NativeHardware.cs")])
            .ToList();
        Assert.True(files.Count >= 8, "the discovery sources were not found; the check would pass vacuously");

        foreach (var file in files)
        {
            var code = StripComments(File.ReadAllText(file));
            foreach (var token in ForbiddenInDiscovery)
            {
                Assert.False(code.Contains(token, StringComparison.OrdinalIgnoreCase), $"{Path.GetFileName(file)} mentions '{token}'");
            }
        }
    }

    [Fact]
    public void The_forbidden_token_check_can_fail()
    {
        // Proves the scan above is not vacuous: a planted call is found, a comment mention is not.
        Assert.Contains("EnumOutputs", StripComments("adapter.EnumOutputs(0, out var o);"), StringComparison.Ordinal);
        Assert.DoesNotContain("EnumOutputs", StripComments("// EnumOutputs is deliberately not called\nvar x = 1;"), StringComparison.Ordinal);
        Assert.DoesNotContain("EnumOutputs", StripComments("/* EnumOutputs */ var x = 1;"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_adapter_source_calls_only_the_factory_enumeration_and_the_description()
    {
        var code = StripComments(File.ReadAllText(Path.Combine(HardwareRepo.Root, "src", "Jankscope.Platform", "Hardware", "DxgiAdapterSource.cs")));
        Assert.Contains("EnumAdapters1", code, StringComparison.Ordinal);
        Assert.Contains("Description1", code, StringComparison.Ordinal);
        Assert.Contains("CreateDxgiFactory1", code, StringComparison.Ordinal);
    }

    [Fact]
    public void The_factory_is_created_through_our_system32_only_import_not_the_binding_packages_unrestricted_one()
    {
        var code = StripComments(File.ReadAllText(Path.Combine(HardwareRepo.Root, "src", "Jankscope.Platform", "Hardware", "DxgiAdapterSource.cs")));
        Assert.DoesNotContain("DXGI.CreateDXGIFactory", code, StringComparison.Ordinal);
        Assert.Contains("NativeHardware.CreateDxgiFactory1", code, StringComparison.Ordinal);
    }

    [Fact]
    public void After_enumerating_the_dxgi_library_in_this_process_is_the_one_from_the_system_directory()
    {
        _ = new DxgiAdapterSource().Enumerate();
        using var self = System.Diagnostics.Process.GetCurrentProcess();
        var modules = self.Modules.Cast<System.Diagnostics.ProcessModule>().Where(m => string.Equals(m.ModuleName, "dxgi.dll", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.NotEmpty(modules);
        Assert.All(modules, m => Assert.Equal(Environment.SystemDirectory, Path.GetDirectoryName(m.FileName), ignoreCase: true));
    }
}
