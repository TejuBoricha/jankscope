namespace Jankscope.Core.Tests;

public class CatalogTests
{
    private const string GoodAnticheat = """
        {
          "schemaVersion": 1, "catalog": "anticheat",
          "entries": [
            { "id": "demo", "displayName": "Demo", "serviceNames": ["DemoSvc"], "verification": "community",
              "source": "https://example.invalid/", "lastReviewed": "2026-10-16" }
          ]
        }
        """;

    [Theory]
    [InlineData("anticheat.json")]
    [InlineData("protected_titles.json")]
    [InlineData("vendor_terms.json")]
    public void Shipped_catalogs_pass_schema_validation(string file)
    {
        var issues = CatalogValidator.Validate(File.ReadAllText(Path.Combine(Repo.Catalogs, file)));
        Assert.True(issues.Count == 0, string.Join(Environment.NewLine, issues));
    }

    [Fact]
    public void A_minimal_valid_catalog_passes()
    {
        Assert.Empty(CatalogValidator.Validate(GoodAnticheat));
    }

    [Fact]
    public void Unknown_properties_are_rejected_so_catalogs_stay_data_only()
    {
        var bad = GoodAnticheat.Replace("\"displayName\": \"Demo\"", "\"displayName\": \"Demo\", \"command\": \"del *.*\"");
        Assert.Contains(CatalogValidator.Validate(bad), i => i.Message.Contains("unknown property 'command'"));
    }

    [Fact]
    public void Missing_source_is_rejected()
    {
        var bad = GoodAnticheat.Replace("\"source\": \"https://example.invalid/\",", "");
        Assert.Contains(CatalogValidator.Validate(bad), i => i.Message.Contains("'source' is required"));
    }

    [Theory]
    [InlineData("2026-13-40")]
    [InlineData("16/10/2026")]
    [InlineData("yesterday")]
    public void Bad_review_dates_are_rejected(string date)
    {
        var bad = GoodAnticheat.Replace("2026-10-16", date);
        Assert.Contains(CatalogValidator.Validate(bad), i => i.Message.Contains("lastReviewed"));
    }

    [Fact]
    public void Unknown_verification_level_is_rejected()
    {
        var bad = GoodAnticheat.Replace("community", "trust-me");
        Assert.Contains(CatalogValidator.Validate(bad), i => i.Message.Contains("verification"));
    }

    [Fact]
    public void Duplicate_ids_are_rejected()
    {
        var entry = """{ "id": "demo", "displayName": "Demo", "serviceNames": ["A"], "verification": "community", "source": "s", "lastReviewed": "2026-10-16" }""";
        var bad = $$"""{ "schemaVersion": 1, "catalog": "anticheat", "entries": [ {{entry}}, {{entry}} ] }""";
        Assert.Contains(CatalogValidator.Validate(bad), i => i.Message == "duplicate id");
    }

    [Theory]
    [InlineData(@"..\\evil.exe")]
    [InlineData("C:/Windows/x")]
    [InlineData("*")]
    [InlineData("a;b")]
    public void Names_must_be_plain_names_not_paths_or_wildcards(string name)
    {
        var bad = GoodAnticheat.Replace("DemoSvc", name);
        Assert.Contains(CatalogValidator.Validate(bad), i => i.Message.Contains("plain names"));
    }

    [Fact]
    public void An_entry_without_any_name_is_rejected()
    {
        var bad = GoodAnticheat.Replace("\"serviceNames\": [\"DemoSvc\"], ", "");
        Assert.Contains(CatalogValidator.Validate(bad), i => i.Message.Contains("at least one of"));
    }

    [Fact]
    public void Unknown_catalog_kind_and_invalid_json_fail_closed()
    {
        Assert.NotEmpty(CatalogValidator.Validate(GoodAnticheat.Replace("\"anticheat\"", "\"mystery\"")));
        Assert.NotEmpty(CatalogValidator.Validate("{ not json"));
        Assert.NotEmpty(CatalogValidator.Validate("[]"));
    }

    [Fact]
    public void Loader_reads_the_shipped_catalogs()
    {
        var set = CatalogSet.Load(Repo.Catalogs);
        Assert.Contains(set.AntiCheat, e => e.Id == "vanguard" && e.ServiceNames.Contains("vgk"));
        Assert.Contains(set.ProtectedTitles, e => e.Id == "valorant");
    }

    [Fact]
    public void Loader_throws_on_an_invalid_catalog_instead_of_guessing()
    {
        var dir = Directory.CreateTempSubdirectory("jankscope-catalog-test");
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "anticheat.json"), "{ \"schemaVersion\": 2 }");
            File.WriteAllText(Path.Combine(dir.FullName, "protected_titles.json"), File.ReadAllText(Path.Combine(Repo.Catalogs, "protected_titles.json")));
            Assert.Throws<InvalidDataException>(() => CatalogSet.Load(dir.FullName));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
