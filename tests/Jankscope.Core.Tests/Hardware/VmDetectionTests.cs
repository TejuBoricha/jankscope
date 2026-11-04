using System.Globalization;
using Jankscope.Core.Hardware;

namespace Jankscope.Core.Tests.Hardware;

public class VmDetectionTests
{
    // An invented catalog: the logic is driven by data, so the tests need no real product names.
    private static readonly VmMarkerEntry Acme = new("acme", "Acme Virtualizer", ["AcmeSoft"], ["Acme Virtual Box"], [], "community");
    private static readonly VmMarkerEntry Orbit = new("orbit", "Orbit Hypervisor", [], [], ["OrbitBIOS"], "community");
    private static readonly VmMarkerEntry Zed = new("zed", "Zed Cloud", ["Zed"], ["Zed Instance"], ["zedboot"], "unverified");

    private static readonly IReadOnlyList<VmMarkerEntry> Catalog = [Acme, Orbit, Zed];

    private static FirmwareStrings Physical(bool? hypervisorPresent = false, bool? smbiosVm = false) =>
        new("Plain Maker", "Plain Model 1", "Plain BIOS Corp", "1.0", "Plain Maker", "Board 7", smbiosVm, hypervisorPresent);

    [Fact]
    public void A_manufacturer_marker_names_the_virtual_machine_from_the_catalog()
    {
        var detection = VmDetector.Detect(Catalog, Physical() with { SystemManufacturer = "AcmeSoft Inc." });
        Assert.Equal(VmVerdict.Virtual, detection.Verdict);
        Assert.Equal("Acme Virtualizer", detection.Name);
        Assert.Contains("manufacturer", detection.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_model_marker_matches()
    {
        var detection = VmDetector.Detect(Catalog, Physical() with { SystemModel = "Acme Virtual Box 2.0" });
        Assert.Equal(("Acme Virtualizer", VmVerdict.Virtual), (detection.Name, detection.Verdict));
    }

    [Fact]
    public void A_bios_marker_matches_the_bios_vendor_or_version()
    {
        Assert.Equal("Orbit Hypervisor", VmDetector.Detect(Catalog, Physical() with { BiosVendor = "OrbitBIOS Ltd" }).Name);
        Assert.Equal("Orbit Hypervisor", VmDetector.Detect(Catalog, Physical() with { BiosVersion = "OrbitBIOS 3.1" }).Name);
    }

    [Fact]
    public void The_baseboard_strings_are_checked_too()
    {
        Assert.Equal("Acme Virtualizer", VmDetector.Detect(Catalog, Physical() with { BaseBoardManufacturer = "AcmeSoft" }).Name);
        Assert.Equal("Acme Virtualizer", VmDetector.Detect(Catalog, Physical() with { BaseBoardProduct = "Acme Virtual Box" }).Name);
    }

    [Fact]
    public void A_marker_only_counts_in_its_own_field_group()
    {
        // "OrbitBIOS" is a BIOS marker; seeing it in the manufacturer string is not a match.
        var detection = VmDetector.Detect(Catalog, Physical() with { SystemManufacturer = "OrbitBIOS Systems" });
        Assert.Equal(VmVerdict.NotVirtual, detection.Verdict);
    }

    [Fact]
    public void Matching_ignores_case()
    {
        Assert.Equal("Acme Virtualizer", VmDetector.Detect(Catalog, Physical() with { SystemManufacturer = "ACMESOFT" }).Name);
        Assert.Equal("Acme Virtualizer", VmDetector.Detect(Catalog, Physical() with { SystemManufacturer = "acmesoft" }).Name);
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("az-Latn-AZ")]
    [InlineData("lt-LT")]
    public void Matching_is_ordinal_so_the_turkish_dotted_and_dotless_i_cannot_break_it(string culture)
    {
        var catalog = new[] { new VmMarkerEntry("vbox", "Invented Box", ["INNOTEK"], ["VirtualBox"], [], "community") };
        var old = Thread.CurrentThread.CurrentCulture;
        var oldUi = Thread.CurrentThread.CurrentUICulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo(culture);
            Thread.CurrentThread.CurrentUICulture = new CultureInfo(culture);
            var detection = VmDetector.Detect(catalog, Physical() with { SystemManufacturer = "innotek GmbH", SystemModel = "VIRTUALBOX" });
            Assert.Equal(VmVerdict.Virtual, detection.Verdict);
            Assert.Equal("Invented Box", detection.Name);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = old;
            Thread.CurrentThread.CurrentUICulture = oldUi;
        }
    }

    [Fact]
    public void The_first_matching_catalog_entry_wins_not_the_longest_or_the_last()
    {
        var both = Physical() with { SystemManufacturer = "Zed AcmeSoft" };
        Assert.Equal("Acme Virtualizer", VmDetector.Detect(Catalog, both).Name);
        Assert.Equal("Zed Cloud", VmDetector.Detect([Zed, Acme], both).Name);
    }

    [Fact]
    public void A_physical_machine_with_readable_strings_and_no_marker_is_not_virtual()
    {
        var detection = VmDetector.Detect(Catalog, Physical());
        Assert.Equal(VmVerdict.NotVirtual, detection.Verdict);
        Assert.Null(detection.Name);
        Assert.Contains("no catalogued marker", detection.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_hypervisor_that_is_merely_present_does_not_make_a_physical_host_a_virtual_machine()
    {
        // Virtualization-based security runs the host OS on top of a hypervisor; that host is not a guest.
        var detection = VmDetector.Detect(Catalog, Physical(hypervisorPresent: true));
        Assert.Equal(VmVerdict.NotVirtual, detection.Verdict);
        Assert.Contains("hypervisor present", detection.Reason, StringComparison.Ordinal);
        Assert.Contains("not a virtual machine", detection.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void The_documented_smbios_virtual_machine_flag_is_conclusive_even_without_a_catalog_match()
    {
        var detection = VmDetector.Detect(Catalog, Physical(smbiosVm: true));
        Assert.Equal(VmVerdict.Virtual, detection.Verdict);
        Assert.Equal(VmDetector.UnidentifiedHypervisorName, detection.Name);
    }

    [Fact]
    public void A_catalog_match_is_preferred_over_the_generic_smbios_flag()
    {
        var detection = VmDetector.Detect(Catalog, Physical(smbiosVm: true) with { SystemManufacturer = "AcmeSoft" });
        Assert.Equal("Acme Virtualizer", detection.Name);
    }

    [Fact]
    public void The_unidentified_name_is_not_a_vendor_and_not_empty()
    {
        Assert.False(string.IsNullOrWhiteSpace(VmDetector.UnidentifiedHypervisorName));
    }

    [Theory]
    [InlineData(null, "Model")]
    [InlineData("", "Model")]
    [InlineData("   ", "Model")]
    [InlineData("Maker", null)]
    [InlineData("Maker", "")]
    [InlineData(null, null)]
    public void Without_both_system_strings_the_answer_is_unknown_never_not_virtual(string? manufacturer, string? model)
    {
        var strings = Physical() with { SystemManufacturer = manufacturer, SystemModel = model };
        var detection = VmDetector.Detect(Catalog, strings);
        Assert.Equal(VmVerdict.Unknown, detection.Verdict);
        Assert.False(string.IsNullOrWhiteSpace(detection.Reason));
    }

    [Fact]
    public void A_match_is_conclusive_even_when_other_strings_could_not_be_read()
    {
        var strings = new FirmwareStrings("AcmeSoft", null, null, null, null, null, null, null);
        Assert.Equal(VmVerdict.Virtual, VmDetector.Detect(Catalog, strings).Verdict);
    }

    [Fact]
    public void An_empty_catalog_means_unknown_not_a_clean_machine()
    {
        var detection = VmDetector.Detect([], Physical());
        Assert.Equal(VmVerdict.Unknown, detection.Verdict);
        Assert.Contains("catalog", detection.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_catalog_marker_that_is_blank_matches_nothing()
    {
        var degenerate = new VmMarkerEntry("blank", "Blank", [" ", ""], [], [], "unverified");
        Assert.Equal(VmVerdict.NotVirtual, VmDetector.Detect([degenerate], Physical()).Verdict);
    }

    [Fact]
    public void The_reason_never_echoes_the_machines_own_strings()
    {
        // Firmware strings can identify a person's machine (serial-like text in a model field); only catalog data is quoted.
        var strings = Physical() with { SystemManufacturer = "AcmeSoft SECRET-SERIAL-12345" };
        var detection = VmDetector.Detect(Catalog, strings);
        Assert.DoesNotContain("SECRET", detection.Reason, StringComparison.Ordinal);
        var none = VmDetector.Detect(Catalog, Physical() with { SystemModel = "SECRET-MODEL-99" });
        Assert.DoesNotContain("SECRET", none.Reason, StringComparison.Ordinal);
    }
}
