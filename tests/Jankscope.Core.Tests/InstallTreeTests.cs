namespace Jankscope.Core.Tests;

/// <summary>The search for shipped files never reaches the root of the drive (review of Stage 1b, finding 15).</summary>
public class InstallTreeTests
{
    private static string Root => Path.GetPathRoot(Path.GetTempPath())!;

    [Fact]
    public void The_search_goes_from_the_start_folder_upwards_and_stops_below_the_root()
    {
        var start = Path.Combine(Root, "apps", "jankscope", "bin");

        var searched = InstallTree.SearchDirectories(start).ToArray();

        Assert.Equal([start, Path.Combine(Root, "apps", "jankscope"), Path.Combine(Root, "apps")], searched);
        Assert.DoesNotContain(Root, searched);
    }

    [Fact]
    public void Starting_at_the_root_searches_nothing()
    {
        Assert.Empty(InstallTree.SearchDirectories(Root));
    }

    [Fact]
    public void Catalogs_directly_under_the_root_are_not_what_a_program_deeper_down_finds()
    {
        // The search only ever builds candidates from the folders above; the root is not one of them, so <root>/catalogs can never be a candidate.
        var candidates = InstallTree.SearchDirectories(Path.Combine(Root, "apps", "jankscope")).Select(d => Path.Combine(d, "catalogs"));

        Assert.DoesNotContain(Path.Combine(Root, "catalogs"), candidates);
    }

    [Fact]
    public void Catalogs_in_a_folder_above_the_program_are_still_found()
    {
        var top = Directory.CreateTempSubdirectory("jankscope-tree-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(top, "catalogs"));
            File.WriteAllText(Path.Combine(top, "catalogs", "anticheat.json"), "{}");
            var deep = Directory.CreateDirectory(Path.Combine(top, "bin", "Release")).FullName;

            Assert.Equal(Path.Combine(top, "catalogs"), CatalogSet.FindDefaultDirectory(deep));
        }
        finally
        {
            Directory.Delete(top, recursive: true);
        }
    }
}
