using Jankscope.Core.Hardware;

namespace Jankscope.Core.Tests.Hardware;

public class VmMarkerCatalogTests
{
    private const string Good = """
        {
          "schemaVersion": 1, "catalog": "vm_markers",
          "entries": [
            { "id": "demo", "displayName": "Demo Hypervisor", "manufacturerMarkers": ["DemoSoft"], "modelMarkers": ["Demo Virtual"],
              "verification": "community", "source": "https://example.invalid/", "lastReviewed": "2026-10-17" }
          ]
        }
        """;

    private static string Shipped => File.ReadAllText(Path.Combine(Repo.Catalogs, "vm_markers.json"));

    [Fact]
    public void The_shipped_vm_marker_catalog_passes_schema_validation()
    {
        var issues = CatalogValidator.Validate(Shipped, "vm_markers");
        Assert.True(issues.Count == 0, string.Join(Environment.NewLine, issues));
    }

    [Fact]
    public void A_minimal_valid_catalog_passes()
    {
        Assert.Empty(CatalogValidator.Validate(Good, "vm_markers"));
    }

    [Fact]
    public void Markers_shorter_than_three_characters_are_rejected_because_they_would_match_almost_everything()
    {
        var bad = Good.Replace("DemoSoft", "De");
        Assert.Contains(CatalogValidator.Validate(bad), i => i.Message.Contains("at least 3", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("*")]
    [InlineData("a;b")]
    [InlineData("C:/x")]
    [InlineData("..\\\\x")]
    public void Markers_must_be_plain_names_not_patterns_or_paths(string marker)
    {
        var bad = Good.Replace("DemoSoft", marker);
        Assert.Contains(CatalogValidator.Validate(bad), i => i.Message.Contains("plain names", StringComparison.Ordinal));
    }

    [Fact]
    public void An_entry_without_any_marker_is_rejected()
    {
        var bad = Good.Replace("\"manufacturerMarkers\": [\"DemoSoft\"], \"modelMarkers\": [\"Demo Virtual\"],", string.Empty);
        Assert.Contains(CatalogValidator.Validate(bad), i => i.Message.Contains("at least one of", StringComparison.Ordinal));
    }

    [Fact]
    public void Properties_of_other_catalogs_are_not_accepted_here()
    {
        var bad = Good.Replace("\"manufacturerMarkers\"", "\"serviceNames\"");
        Assert.Contains(CatalogValidator.Validate(bad), i => i.Message.Contains("unknown property 'serviceNames'", StringComparison.Ordinal));
    }

    [Fact]
    public void Unknown_properties_are_rejected_so_the_catalog_stays_data_only()
    {
        var bad = Good.Replace("\"displayName\": \"Demo Hypervisor\"", "\"displayName\": \"Demo\", \"command\": \"del *.*\"");
        Assert.Contains(CatalogValidator.Validate(bad), i => i.Message.Contains("unknown property 'command'", StringComparison.Ordinal));
    }

    [Fact]
    public void Source_review_date_and_verification_are_required_like_in_every_catalog()
    {
        Assert.Contains(CatalogValidator.Validate(Good.Replace("\"source\": \"https://example.invalid/\",", string.Empty)), i => i.Message.Contains("'source' is required", StringComparison.Ordinal));
        Assert.Contains(CatalogValidator.Validate(Good.Replace("2026-10-17", "soon")), i => i.Message.Contains("lastReviewed", StringComparison.Ordinal));
        Assert.Contains(CatalogValidator.Validate(Good.Replace("community", "certain")), i => i.Message.Contains("verification", StringComparison.Ordinal));
    }

    [Fact]
    public void A_file_of_another_kind_saved_as_vm_markers_does_not_load()
    {
        var other = Good.Replace("\"vm_markers\"", "\"anticheat\"").Replace("manufacturerMarkers", "serviceNames").Replace("modelMarkers", "processNames");
        Assert.Empty(CatalogValidator.Validate(other, "anticheat"));
        Assert.Contains(CatalogValidator.Validate(other, "vm_markers"), i => i.Message.Contains("must declare", StringComparison.Ordinal));
    }

    [Fact]
    public void An_empty_entry_list_is_rejected_so_an_empty_catalog_cannot_silently_say_not_virtual()
    {
        var bad = "{ \"schemaVersion\": 1, \"catalog\": \"vm_markers\", \"entries\": [] }";
        Assert.Contains(CatalogValidator.Validate(bad), i => i.Message.Contains("must not be empty", StringComparison.Ordinal));
    }

    [Fact]
    public void The_loader_reads_the_shipped_vm_markers_in_file_order()
    {
        var set = CatalogSet.Load(Repo.Catalogs);
        Assert.NotEmpty(set.VmMarkers);
        Assert.Equal(set.VmMarkers.Count, set.VmMarkers.Select(e => e.Id).Distinct().Count());
        Assert.All(set.VmMarkers, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.DisplayName));
            Assert.True(e.ManufacturerMarkers.Count + e.ModelMarkers.Count + e.BiosMarkers.Count > 0, e.Id);
            Assert.All(e.ManufacturerMarkers.Concat(e.ModelMarkers).Concat(e.BiosMarkers), m => Assert.True(m.Trim().Length >= 3, $"{e.Id}: '{m}'"));
        });
    }

    [Fact]
    public void The_empty_catalog_set_has_no_vm_markers_and_existing_two_argument_construction_still_works()
    {
        Assert.Empty(CatalogSet.Empty.VmMarkers);
        var set = new CatalogSet([], []);
        Assert.Empty(set.VmMarkers);
        Assert.Single((set with { VmMarkers = [new VmMarkerEntry("x", "X", ["xxx"], [], [], "community")] }).VmMarkers);
    }

    [Fact]
    public void A_missing_vm_marker_file_makes_loading_fail_closed()
    {
        var dir = Directory.CreateTempSubdirectory("jankscope-vm-catalog-test");
        try
        {
            foreach (var name in new[] { "anticheat.json", "protected_titles.json" })
            {
                File.Copy(Path.Combine(Repo.Catalogs, name), Path.Combine(dir.FullName, name));
            }

            Assert.Throws<InvalidDataException>(() => CatalogSet.Load(dir.FullName));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void An_invalid_vm_marker_file_makes_loading_fail_closed()
    {
        var dir = Directory.CreateTempSubdirectory("jankscope-vm-catalog-test");
        try
        {
            foreach (var name in new[] { "anticheat.json", "protected_titles.json" })
            {
                File.Copy(Path.Combine(Repo.Catalogs, name), Path.Combine(dir.FullName, name));
            }

            File.WriteAllText(Path.Combine(dir.FullName, "vm_markers.json"), Good.Replace("DemoSoft", "De"));
            Assert.Throws<InvalidDataException>(() => CatalogSet.Load(dir.FullName));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    // ---- the shipped data, run against firmware strings in the shape those products are widely reported to use ----

    public static TheoryData<string, string, string, string> KnownGuests => new()
    {
        { "innotek GmbH", "VirtualBox", "Oracle VirtualBox", "VirtualBox" },
        { "VMware, Inc.", "VMware Virtual Platform", "VMware", "VMware" },
        { "VMware, Inc.", "VMware7,1", "VMware", "VMware" },
        { "QEMU", "Standard PC (Q35 + ICH9, 2009)", "QEMU", "QEMU" },
        { "Microsoft Corporation", "Virtual Machine", "Microsoft Hyper-V", "Hyper-V" },
        { "Xen", "HVM domU", "Xen", "Xen" },
        { "Parallels Software International Inc.", "Parallels Virtual Platform", "Parallels", "Parallels" },
        { "Google", "Google Compute Engine", "Google", "Google Compute Engine" },
        { "Amazon EC2", "m5.large", "Amazon", "Amazon EC2" },
    };

    [Theory]
    [MemberData(nameof(KnownGuests))]
    public void The_shipped_catalog_recognises_widely_reported_guest_firmware_strings(string manufacturer, string model, string bios, string expectedNamePart)
    {
        var set = CatalogSet.Load(Repo.Catalogs);
        var detection = VmDetector.Detect(set.VmMarkers, new FirmwareStrings(manufacturer, model, bios, "1", manufacturer, model, null, true));
        Assert.Equal(VmVerdict.Virtual, detection.Verdict);
        Assert.Contains(expectedNamePart, detection.Name, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("LENOVO", "83BS")]
    [InlineData("Dell Inc.", "XPS 15 9520")]
    [InlineData("Microsoft Corporation", "Surface Laptop 5")]
    [InlineData("ASUSTeK COMPUTER INC.", "ROG Zephyrus G14")]
    [InlineData("Google", "Pixelbook Go")]
    [InlineData("System manufacturer", "System Product Name")]
    [InlineData("To Be Filled By O.E.M.", "To Be Filled By O.E.M.")]
    public void The_shipped_catalog_does_not_flag_ordinary_physical_machines(string manufacturer, string model)
    {
        var set = CatalogSet.Load(Repo.Catalogs);
        var detection = VmDetector.Detect(set.VmMarkers, new FirmwareStrings(manufacturer, model, "Some BIOS Vendor", "F.12", manufacturer, "Board", false, true));
        Assert.Equal(VmVerdict.NotVirtual, detection.Verdict);
    }

    [Fact]
    public void No_shipped_entry_claims_to_be_observed_because_the_reference_bench_is_a_physical_machine()
    {
        var set = CatalogSet.Load(Repo.Catalogs);
        Assert.DoesNotContain(set.VmMarkers, e => e.Verification == "observed");
    }
}
