using System.Text.RegularExpressions;

namespace Jankscope.Harness.Tests;

/// <summary>Source-level facts about the harness that the build also enforces, restated here so a regression names the harness.</summary>
public class HarnessSourceTests
{
    private static string HarnessDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Jankscope.slnx")))
            {
                return Path.Combine(dir.FullName, "tools", "Jankscope.Harness");
            }
        }

        throw new InvalidOperationException("Repository root (Jankscope.slnx) not found above " + AppContext.BaseDirectory);
    }

    private static IEnumerable<(string File, string Text)> Sources() =>
        Directory.EnumerateFiles(HarnessDirectory(), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .Select(f => (f, File.ReadAllText(f)));

    [Fact]
    public void The_harness_has_no_native_imports_unsafe_code_or_ETW_session_start()
    {
        var sources = Sources().ToList();
        Assert.True(sources.Count >= 10, "harness sources not found");

        foreach (var (file, text) in sources)
        {
            Assert.DoesNotMatch(@"\b(LibraryImport|DllImport)\b", text);
            Assert.DoesNotMatch(@"\bunsafe\b", text);
            Assert.DoesNotMatch(@"\b(StartTrace|ControlTrace|EnableTraceEx2?|OpenTrace|ProcessTrace|TraceEvent|EventSource)\b", text);
            Assert.False(Regex.IsMatch(text, @"\btimeBeginPeriod\b|\bNtSetTimerResolution\b"), $"{Path.GetFileName(file)}: no timer-resolution change");
        }

        var project = File.ReadAllText(Path.Combine(HarnessDirectory(), "Jankscope.Harness.csproj"));
        Assert.DoesNotContain("AllowUnsafeBlocks", project);
        Assert.Matches("<TargetFramework>net10.0-windows</TargetFramework>", project);
    }

    [Fact]
    public void The_harness_references_only_the_core_project_and_the_two_approved_graphics_packages()
    {
        var project = File.ReadAllText(Path.Combine(HarnessDirectory(), "Jankscope.Harness.csproj"));

        Assert.Equal(["..\\..\\src\\Jankscope.Core\\Jankscope.Core.csproj"], Regex.Matches(project, "<ProjectReference Include=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToArray());
        var packages = Regex.Matches(project, "<PackageReference Include=\"([^\"]+)\"").Select(m => m.Groups[1].Value);
        Assert.Equal(new[] { "Vortice.Direct3D11", "Vortice.DXGI" }.Order(StringComparer.Ordinal), packages.Order(StringComparer.Ordinal));
    }
}
