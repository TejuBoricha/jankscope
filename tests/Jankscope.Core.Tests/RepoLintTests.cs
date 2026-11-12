using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jankscope.Core.Tests;

/// <summary>
/// Static checks over the repository itself: acceptance #13 (HC-1 static scan), #17 (no hardcoded vendor logic), §6.6 (.NET 10 only),
/// HC-10 (dependencies recorded), and script/encoding hygiene. The rules live in <see cref="ProductLints"/>; these tests apply them.
/// "Product code" is every C# file in the repository outside <c>tests/</c>, so a new directory cannot escape the lints.
/// </summary>
public class RepoLintTests
{
    /// <summary>
    /// Every native entry point the product may import, as <c>dll!EntryPoint</c>. A new native call is a conscious review point: it
    /// must be added here in the same change, with the reviewer asking "does this touch another process?" (HC-1, HC-2).
    /// </summary>
    private static readonly string[] ApprovedLibraryImports =
    [
        "advapi32.dll!CloseServiceHandle",
        "advapi32.dll!ControlTraceW",
        "advapi32.dll!OpenSCManagerW",
        "advapi32.dll!OpenServiceW",
        "advapi32.dll!QueryAllTracesW",
        "advapi32.dll!QueryServiceStatusEx",
        "advapi32.dll!StartTraceW",
        "dwmapi.dll!DwmSetWindowAttribute",
        "dxgi.dll!CreateDXGIFactory1",
        "kernel32.dll!GetActiveProcessorCount",
        "kernel32.dll!GetLogicalProcessorInformationEx",
        "kernel32.dll!GetSystemPowerStatus",
        "mdmregistration.dll!IsDeviceRegisteredWithManagement",
        "netapi32.dll!NetApiBufferFree",
        "netapi32.dll!NetFreeAadJoinInformation",
        "netapi32.dll!NetGetAadJoinInformation",
        "netapi32.dll!NetGetJoinInformation",
        "pdh.dll!PdhAddEnglishCounterW",
        "pdh.dll!PdhCloseQuery",
        "pdh.dll!PdhCollectQueryData",
        "pdh.dll!PdhGetFormattedCounterArrayW",
        "pdh.dll!PdhOpenQueryW",
        "user32.dll!DisplayConfigGetDeviceInfo",
        "user32.dll!GetDisplayConfigBufferSizes",
        "user32.dll!QueryDisplayConfig",
        "user32.dll!RegisterHotKey",
        "user32.dll!UnregisterHotKey",
    ];

    /// <summary>
    /// Narrow, reviewed exceptions to <see cref="ProductLints.ManagedBans"/> or the native-name list: (file suffix, rule, reason). Each entry
    /// must still be needed; an unused entry fails the test so the list cannot rot.
    /// </summary>
    private static readonly (string FileSuffix, string Rule, string Reason)[] Allowed =
    [
        // The PresentMon launcher starts the pinned PresentMon executable (HC-7, ADR 0003).
        ("src/Jankscope.Frames/PresentMon/PresentMonProcessLauncher.cs", "Process.Start",
            "Starts exactly one kind of program: the pinned, hash-verified PresentMon console executable (HC-7, ADR 0003), with an argument vector re-checked against an allowlist grammar that refuses --restart_as_admin and every unlisted option."),
        ("src/Jankscope.Frames/PresentMon/PresentMonProcessLauncher.cs", "ProcessStartInfo",
            "UseShellExecute=false, ArgumentList (never a joined command line), redirected streams, no window, minimal environment, private temporary working directory."),
        ("src/Jankscope.Frames/PresentMon/PresentMonProcessLauncher.cs", "Process members that open handles",
            "Only on the child this class started: its streams, exit code, exit wait, and the plain Kill() / dispose that guarantee no child outlives its owner. The tree form (Kill(entireProcessTree: true)) is forbidden because it opens a handle to every process on the machine; LauncherLintTests pins the exact set of Process members this file may use."),

        // Opens user-clicked static sponsor/store links in default browser (HC-13, Acceptance #22).
        ("src/Jankscope.App/Services/ExternalUrlOpener.cs", "Process.Start",
            "Opens user-clicked external HTTPS sponsor or store URL in the default browser per HC-13; never fetches, strictly HTTPS-only."),
        ("src/Jankscope.App/Services/ExternalUrlOpener.cs", "ProcessStartInfo",
            "UseShellExecute=true for default OS protocol handler to open HTTPS browser URLs on user click only (HC-13)."),

        // Keyboard (HC-4, review of Stage 1b finding 5). Both are pinned further by tests below.
        ("src/Jankscope.App/Views/TimelineView.cs", "managed keyboard reading",
            "Moves the cursor of the timeline control with Left, Right, Home, End, Enter and Space, and Shift for a larger step, only while that control has the keyboard focus. Nothing is stored or sent; The_timeline_reads_only_its_six_navigation_keys pins the exact keys."),
        ("src/Jankscope.App/Services/HotkeyHost.cs", "managed keyboard reading",
            "A message hook on its own hidden window that handles WM_HOTKEY only, the message RegisterHotKey delivers; it never sees a key."),
    ];

    /// <summary>Files allowed to switch the banned-API rule off, each with the number of regions (a written reason is required on the pragma line).</summary>
    private static readonly (string FileSuffix, int Regions)[] BannedApiExceptions =
    [
        ("src/Jankscope.App/Services/ExternalUrlOpener.cs", 1), // user-clicked HTTPS sponsor/store URL in default browser (HC-13)
        ("src/Jankscope.Frames/PresentMon/PresentMonProcessLauncher.cs", 1), // the PresentMon launcher: only a child it started is ever touched (HC-1, HC-7)
        ("src/Jankscope.Platform/Probes/ServiceState.cs", 1), // name-only process snapshot (HC-1, LIMITATIONS L8)
    ];

    private static IEnumerable<(string Path, string Rel, string Text)> AllSources() =>
        Repo.AllFiles("*.cs").Select(f => (f, Repo.Rel(f), File.ReadAllText(f)));

    private static IEnumerable<(string Path, string Rel, string Text)> ProductFiles() =>
        AllSources().Where(f => !f.Rel.StartsWith("tests/", StringComparison.Ordinal));

    private static bool IsAllowed(string rel, string rule, HashSet<int> used)
    {
        for (var i = 0; i < Allowed.Length; i++)
        {
            if (rel.EndsWith(Allowed[i].FileSuffix, StringComparison.Ordinal) && Allowed[i].Rule == rule)
            {
                used.Add(i);
                return true;
            }
        }

        return false;
    }

    [Fact]
    public void Product_code_contains_no_hooks_injection_memory_access_driver_loading_or_dynamic_native_loading()
    {
        var used = new HashSet<int>();
        var hits = new List<string>();
        foreach (var (_, rel, text) in ProductFiles())
        {
            foreach (var name in ProductLints.NativeNameHits(CSharpScanner.Scan(text)).Where(n => !IsAllowed(rel, n, used)))
            {
                hits.Add($"{rel}: {name}");
            }
        }

        Assert.True(hits.Count == 0, "Forbidden native APIs (HC-1/HC-2): " + string.Join("; ", hits));
    }

    [Fact]
    public void Product_code_uses_no_banned_managed_apis_outside_the_reviewed_allowlist()
    {
        var used = new HashSet<int>();
        var hits = new List<string>();
        foreach (var (_, rel, text) in ProductFiles())
        {
            foreach (var rule in ProductLints.ManagedBanHits(CSharpScanner.Scan(text)).Where(r => !IsAllowed(rel, r, used)))
            {
                hits.Add($"{rel}: {rule}");
            }
        }

        Assert.True(hits.Count == 0, "Banned managed APIs (HC-1/HC-5/HC-7/HC-8): " + string.Join("; ", hits));
        var stale = Enumerable.Range(0, Allowed.Length).Where(i => !used.Contains(i)).Select(i => $"{Allowed[i].FileSuffix} / {Allowed[i].Rule}").ToList();
        Assert.True(stale.Count == 0, "Allowlist entries that no code needs any more (remove them): " + string.Join("; ", stale));
        Assert.All(Allowed, a => Assert.False(string.IsNullOrWhiteSpace(a.Reason), $"{a.FileSuffix}: an allowlist entry needs a reason"));
    }

    [Fact]
    public void The_timeline_reads_only_its_six_navigation_keys()
    {
        // HC-4 (review of Stage 1b, finding 5): the timeline control is the one place that reads keys. The allowlist above would let it read any key,
        // so the exact set is pinned here: adding a key (a letter, a digit) means changing this test, which is a review.
        var file = Repo.AllFiles("*.cs").Single(f => Repo.Rel(f).EndsWith("src/Jankscope.App/Views/TimelineView.cs", StringComparison.Ordinal));
        var code = CSharpScanner.Scan(File.ReadAllText(file)).Code;

        var keys = Regex.Matches(code, @"\bKey\s*\.\s*(\w+)\b").Select(m => m.Groups[1].Value).Distinct().OrderBy(k => k, StringComparer.Ordinal).ToArray();

        Assert.Equal(["End", "Enter", "Home", "Left", "Right", "Space"], keys);
    }

    [Fact]
    public void Xaml_files_wire_no_keyboard_events_or_input_bindings()
    {
        // HC-4: key events can also be wired in markup, which the C# lint cannot see. The handler would need a key-event argument in C#, which is also refused.
        var pattern = new Regex(@"\b(?:Preview)?(?:KeyDown|KeyUp|TextInput)\s*=|<\s*(?:\w+\s*\.\s*)?(?:KeyBinding|InputBinding|MouseBinding)\b|\bInputBindings\b|\bKeyGesture\b|\bTextCompositionManager\b", RegexOptions.CultureInvariant);
        var files = Repo.AllFiles("*.xaml").ToList();

        var hits = files.Where(f => pattern.IsMatch(File.ReadAllText(f))).Select(Repo.Rel).ToList();

        Assert.True(files.Count >= 5, $"only {files.Count} XAML files scanned; the test would pass vacuously");
        Assert.True(hits.Count == 0, "XAML wires keyboard input (HC-4): " + string.Join("; ", hits));
    }

    [Fact]
    public void Product_code_names_no_vendor_outside_catalogs_and_allowlisted_adapters()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Repo.Catalogs, "vendor_terms.json")));
        var terms = doc.RootElement.GetProperty("entries").EnumerateArray()
            .SelectMany(e => e.GetProperty("terms").EnumerateArray().Select(t => t.GetString()!))
            .ToList();
        Assert.NotEmpty(terms);

        var hits = new List<string>();
        foreach (var (_, rel, text) in ProductFiles().Where(f => !f.Rel.Contains("VendorAdapters", StringComparison.Ordinal)))
        {
            hits.AddRange(ProductLints.VendorHits(CSharpScanner.Scan(text), terms).Select(t => $"{rel}: '{t}'"));
        }

        Assert.True(hits.Count == 0, "Hardcoded vendor names (HC-12); move them to a catalog or a VendorAdapters assembly: " + string.Join("; ", hits));
    }

    [Fact]
    public void The_set_of_native_entry_points_is_exactly_the_approved_set_and_every_import_is_readable()
    {
        var imports = ProductFiles()
            .SelectMany(f => ProductLints.NativeImports(CSharpScanner.Scan(f.Text)).Select(i => (f.Rel, Import: i)))
            .ToList();

        // Every mention of LibraryImport/DllImport, in any spelling, must be a [LibraryImport] with a literal DLL and a literal EntryPoint.
        var unreadable = imports
            .Where(x => !x.Import.IsFullyLiteral || !x.Import.Token.StartsWith("LibraryImport", StringComparison.Ordinal))
            .Select(x => $"{x.Rel}: {x.Import.Token}")
            .ToList();
        Assert.True(unreadable.Count == 0,
            "Native imports must be [LibraryImport(\"dll\", EntryPoint = \"Name\")] with plain string literals so they can be reviewed as an exact set (DllImport, constants, " +
            "aliases and bare mentions are not allowed): " + string.Join("; ", unreadable));

        var found = imports.Select(x => $"{x.Import.Dll}!{x.Import.EntryPoint}").Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(ApprovedLibraryImports.Order(StringComparer.Ordinal).ToArray(), found);
    }

    [Fact]
    public void Every_project_with_native_imports_restricts_library_search_to_system32()
    {
        var projects = Repo.AllFiles("*.csproj").Where(p => !Repo.Rel(p).StartsWith("tests/", StringComparison.Ordinal))
            .Select(p => Path.GetDirectoryName(p)!).ToList();
        var allSources = Repo.AllFiles("*.cs").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var checkedAny = false;
        foreach (var dir in projects)
        {
            var scans = allSources.Where(f => f.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                .Select(f => CSharpScanner.Scan(File.ReadAllText(f)))
                .ToList();
            if (scans.Any(s => ProductLints.NativeImports(s).Count > 0))
            {
                checkedAny = true;
                Assert.True(scans.Any(ProductLints.DeclaresSystem32OnlyDllSearch),
                    $"{Path.GetFileName(dir)} imports native code but lacks [assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)] (review #6).");
            }
        }

        Assert.True(checkedAny, "no project with native imports was found; the lint would pass vacuously");
    }

    [Fact]
    public void Every_source_file_scans_to_the_end_and_has_balanced_brackets()
    {
        // If the lexer loses track (for example inside a literal it misreads), the file ends inside a literal or the brackets in the code
        // view stop balancing. Either means no lint verdict about that file can be trusted.
        var files = AllSources().ToList();
        Assert.True(files.Count > 10);
        foreach (var (_, rel, text) in files)
        {
            var scanned = CSharpScanner.Scan(text);
            Assert.True(scanned.EndedInCode, $"{rel}: the lexer ended inside a comment or literal");
            Assert.True(scanned.NonAsciiCode.Length == 0,
                $"{rel}: non-ASCII characters in code ({string.Join(", ", scanned.NonAsciiCode.Select(c => $"U+{(int)c:X4}"))}); C# ignores format characters inside identifiers, " +
                "so an invisible character could hide an import or a banned name from every lint");
            foreach (var (open, close) in new[] { ('{', '}'), ('(', ')'), ('[', ']') })
            {
                Assert.True(scanned.Code.Count(c => c == open) == scanned.Code.Count(c => c == close),
                    $"{rel}: '{open}' and '{close}' do not balance in the code view (the lexer probably mis-parsed a literal)");
            }
        }
    }

    [Fact]
    public void Every_project_lives_under_src_tools_or_tests_and_build_files_run_nothing()
    {
        foreach (var project in Repo.AllFiles("*.csproj"))
        {
            var rel = Repo.Rel(project);
            Assert.True(rel.StartsWith("src/", StringComparison.Ordinal) || rel.StartsWith("tools/", StringComparison.Ordinal) || rel.StartsWith("tests/", StringComparison.Ordinal),
                $"{rel}: projects must live under src/, tools/ or tests/ so the per-folder lints and package rules apply");
        }

        // Build files can run commands or pull in source from elsewhere; none may.
        var buildFiles = new[] { "*.csproj", "*.props", "*.targets" }.SelectMany(Repo.AllFiles).ToList();
        Assert.NotEmpty(buildFiles);
        foreach (var file in buildFiles)
        {
            var text = Regex.Replace(File.ReadAllText(file), "<!--.*?-->", string.Empty, RegexOptions.Singleline);
            var rel = Repo.Rel(file);
            Assert.DoesNotMatch(@"<Exec\b|<UsingTask\b|RoslynCodeTaskFactory|CodeTaskFactory|PreBuildEvent|PostBuildEvent|<Target\b[^>]*\bBeforeTargets=""[^""]*Restore", text);
            Assert.False(Regex.IsMatch(text, @"<Compile\s+(?:Include|Update)\s*=\s*""[^""]*\.\.", RegexOptions.IgnoreCase), $"{rel}: pulls source from outside the project directory");
        }
    }

    [Fact]
    public void Only_the_platform_assembly_allows_unsafe_code()
    {
        var offenders = Repo.AllFiles("*.csproj")
            .Where(p => Regex.IsMatch(File.ReadAllText(p), @"<AllowUnsafeBlocks>\s*true\s*</AllowUnsafeBlocks>", RegexOptions.IgnoreCase))
            .Select(p => Path.GetFileName(p))
            .ToList();
        Assert.Equal(["Jankscope.Platform.csproj"], offenders);

        // The shared props files may mention the property in a comment, but must not set it.
        var propsFiles = new[] { "*.props", "*.targets" }.SelectMany(Repo.AllFiles).ToList();
        Assert.NotEmpty(propsFiles);
        foreach (var props in propsFiles)
        {
            var withoutComments = Regex.Replace(File.ReadAllText(props), "<!--.*?-->", string.Empty, RegexOptions.Singleline);
            Assert.DoesNotContain("AllowUnsafeBlocks", withoutComments);
        }
    }

    [Fact]
    public void Product_projects_get_the_banned_api_analyzer_and_test_projects_do_not()
    {
        var targets = File.ReadAllText(Path.Combine(Repo.Root, "Directory.Build.targets"));
        Assert.Matches(@"<ItemGroup\s+Condition=""'\$\(IsTestProject\)'\s*!=\s*'true'"">", targets);
        Assert.Contains(@"<PackageReference Include=""Microsoft.CodeAnalysis.BannedApiAnalyzers"" PrivateAssets=""all"" />", targets);
        Assert.Contains(@"<AdditionalFiles Include=""$(MSBuildThisFileDirectory)BannedSymbols.txt""", targets);

        foreach (var project in Repo.AllFiles("*.csproj").Where(p => !Repo.Rel(p).StartsWith("tests/", StringComparison.Ordinal)))
        {
            Assert.DoesNotMatch(@"<IsTestProject>\s*true\s*</IsTestProject>", File.ReadAllText(project));
        }

        var tests = Repo.AllFiles("*.csproj").Where(p => Repo.Rel(p).StartsWith("tests/", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(tests);
        foreach (var project in tests)
        {
            Assert.True(Regex.IsMatch(File.ReadAllText(project), @"<IsTestProject>\s*true\s*</IsTestProject>"),
                $"{Path.GetFileName(project)} must declare IsTestProject, otherwise it would get the product analyzer.");
        }

        // The list itself: every line is a well-formed documentation ID with a message, and it is not trivially short.
        var lines = File.ReadAllLines(Path.Combine(Repo.Root, "BannedSymbols.txt"))
            .Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith("//", StringComparison.Ordinal)).ToList();
        Assert.True(lines.Count >= 40, $"BannedSymbols.txt has only {lines.Count} entries");
        Assert.All(lines, l => Assert.Matches(@"^[TMP]:[A-Za-z0-9_.`#,\[\]{}]+(\([^;]*\))?;\S.*$", l));

        // The proof that every entry fires must stay wired into the build script.
        Assert.Contains("verify-banned-symbols.ps1", File.ReadAllText(Path.Combine(Repo.Root, "build.ps1")));
        Assert.True(File.Exists(Path.Combine(Repo.Root, "tools", "dev", "analyzer-coverage", "Coverage.cs.txt")));
    }

    [Fact]
    public void The_banned_api_rule_can_be_switched_off_only_in_the_pinned_files_with_a_written_reason()
    {
        var nl = CSharpScanner.NewLineClass;
        var disable = new Regex($@"#pragma\s+warning\s+disable[^{nl}]*\bRS0030\b[^{nl}]*");
        var restore = new Regex($@"#pragma\s+warning\s+restore[^{nl}]*\bRS0030\b");
        var blanket = new Regex($@"#pragma\s+warning\s+disable\s*(?://|[{nl}]|$)");

        var found = new List<(string Rel, int Regions)>();
        foreach (var (_, rel, text) in ProductFiles())
        {
            var disables = disable.Matches(text);
            Assert.True(disables.Count == restore.Matches(text).Count, $"{rel}: every RS0030 disable needs a matching restore");
            Assert.All(disables, d => Assert.Matches(@"//\s*\S", d.Value));
            if (disables.Count > 0)
            {
                found.Add((rel, disables.Count));
            }

            // Other ways to silence it are not allowed anywhere.
            Assert.DoesNotMatch(@"SuppressMessage\s*\([^)]*RS0030", text);
            Assert.False(blanket.IsMatch(text), $"{rel}: a blanket '#pragma warning disable' would hide RS0030");
        }

        Assert.Equal(BannedApiExceptions.OrderBy(e => e.FileSuffix, StringComparer.Ordinal).Select(e => (e.FileSuffix, e.Regions)).ToArray(),
            found.OrderBy(f => f.Rel, StringComparer.Ordinal).Select(f => (f.Rel, f.Regions)).ToArray());

        // ...nor from any project, props/targets, editorconfig or globalconfig file in the repository.
        var configs = new[] { "*.csproj", "*.props", "*.targets", ".editorconfig", "*.globalconfig", "*.ruleset" }.SelectMany(Repo.AllFiles).ToList();
        Assert.NotEmpty(configs);
        foreach (var file in configs)
        {
            var text = File.ReadAllText(file);
            var rel = Repo.Rel(file);
            const RegexOptions Ci = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
            Assert.False(Regex.IsMatch(text, @"<(NoWarn|WarningsNotAsErrors)\b[^>]*>[^<]*RS0030", Ci), $"{rel} silences or downgrades RS0030 through a project property");
            Assert.False(Regex.IsMatch(text, @"dotnet_diagnostic\.RS0030\.severity\s*=\s*(none|silent|suggestion|warning)", Ci), $"{rel} lowers the severity of RS0030");
            Assert.False(Regex.IsMatch(text, @"dotnet_analyzer_diagnostic\.(category-\w+\.)?severity\s*=\s*(none|silent)", Ci), $"{rel} turns analyzer diagnostics off wholesale");
            Assert.False(Regex.IsMatch(text, @"<RunAnalyzers\b[^>]*>\s*false|<EnableNETAnalyzers\b[^>]*>\s*false|<NoWarn\b[^>]*>[^<]*\bRS\d{4}", Ci), $"{rel} disables analyzers");
        }
    }

    [Fact]
    public void Dependency_audit_warnings_stay_visible_but_never_break_a_restore_or_build()
    {
        // Review pass 2 #10: NU1900 (audit data unreachable) must not fail an offline restore when every package is already cached.
        var props = File.ReadAllText(Path.Combine(Repo.Root, "Directory.Build.props"));
        var exempt = Regex.Match(props, @"<WarningsNotAsErrors>([^<]*)</WarningsNotAsErrors>").Groups[1].Value;
        foreach (var code in new[] { "NU1900", "NU1901", "NU1902", "NU1903", "NU1904" })
        {
            Assert.Contains(code, exempt);
        }
    }

    [Fact]
    public void The_smoke_gate_and_its_self_test_stay_wired_into_the_build_script()
    {
        var build = File.ReadAllText(Path.Combine(Repo.Root, "build.ps1"));
        Assert.Contains("test-smoke-gate.ps1", build);
        Assert.Contains("Assert-ProbeMatrix", build);
        Assert.True(File.Exists(Path.Combine(Repo.Root, "tools", "dev", "Assert-ProbeMatrix.ps1")));
        Assert.True(File.Exists(Path.Combine(Repo.Root, "tools", "dev", "test-smoke-gate.ps1")));
    }

    [Fact]
    public void There_is_exactly_one_shared_build_file_of_each_kind_and_nothing_switches_them_off()
    {
        // A nested Directory.Build.targets would shadow the root one (and the analyzer with it); ImportDirectoryBuild* would opt a project out.
        foreach (var name in new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props" })
        {
            var found = Repo.AllFiles(name).Select(Repo.Rel).ToList();
            Assert.Equal([name], found);
        }

        foreach (var file in new[] { "*.csproj", "*.props", "*.targets" }.SelectMany(Repo.AllFiles))
        {
            var text = Regex.Replace(File.ReadAllText(file), "<!--.*?-->", string.Empty, RegexOptions.Singleline);
            Assert.False(Regex.IsMatch(text, @"ImportDirectory(Build|Packages)\w*\s*>|ManagePackageVersionsCentrally\s*>\s*false", RegexOptions.IgnoreCase),
                $"{Repo.Rel(file)} opts out of a shared build file");
        }
    }

    [Fact]
    public void Product_projects_reference_only_product_projects_inside_src_or_tools()
    {
        // Referencing a project under tests/ (exempt from the analyzer) or third_party/ (not product code) would smuggle code past every check.
        var products = Repo.AllFiles("*.csproj").Where(p => !Repo.Rel(p).StartsWith("tests/", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(products);
        foreach (var project in products)
        {
            foreach (Match reference in Regex.Matches(File.ReadAllText(project), @"<ProjectReference\s+[^>]*Include=""([^""]+)"""))
            {
                var target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, reference.Groups[1].Value));
                var rel = Path.GetRelativePath(Repo.Root, target).Replace(Path.DirectorySeparatorChar, '/');
                Assert.True(rel.StartsWith("src/", StringComparison.Ordinal) || rel.StartsWith("tools/", StringComparison.Ordinal),
                    $"{Repo.Rel(project)} references {rel}; product projects may reference only projects under src/ or tools/");
            }
        }
    }

    [Fact]
    public void The_analyzer_wiring_proof_is_part_of_the_build()
    {
        Assert.Contains("verify-analyzer-wiring.ps1", File.ReadAllText(Path.Combine(Repo.Root, "build.ps1")));
        Assert.True(File.Exists(Path.Combine(Repo.Root, "tools", "dev", "verify-analyzer-wiring.ps1")));
    }

    [Fact]
    public void The_context_file_exists_stays_short_and_keeps_its_structure()
    {
        // CONTEXT.md is what a new or compacted session reads first. It must not rot into a second HANDOFF.md.
        var path = Path.Combine(Repo.Root, "CONTEXT.md");
        Assert.True(File.Exists(path), "CONTEXT.md is the first file a new session reads");
        var text = File.ReadAllText(path);
        Assert.True(text.Split('\n').Length <= 200, "CONTEXT.md must stay under 200 lines: move detail to HANDOFF.md or docs/");
        Assert.Matches(@"\*\*Last updated: 20\d\d-\d\d-\d\d\.\*\*", text);
        foreach (var heading in new[]
                 {
                     "## 1. What this is", "## 2. State right now", "## 3. Verify in two minutes", "## 4. Next actions, in order",
                     "## 5. Standing rules from the owner", "## 6. How the code is guarded", "## 7. Pitfalls learned the hard way",
                     "## 8. Where things are", "## 9. Open items", "## 10. Background and scratch",
                 })
        {
            Assert.Contains(heading, text);
        }

        Assert.Contains("CONTEXT.md", File.ReadAllText(Path.Combine(Repo.Root, "README.md")));
        Assert.Contains("CONTEXT.md", File.ReadAllText(Path.Combine(Repo.Root, "HANDOFF.md")));
    }

    [Fact]
    public void Every_project_targets_net10_only()
    {
        var projects = Repo.AllFiles("*.csproj").ToList();
        Assert.NotEmpty(projects);
        foreach (var project in projects)
        {
            var tfm = Regex.Match(File.ReadAllText(project), "<TargetFramework>(.*?)</TargetFramework>").Groups[1].Value;
            Assert.True(tfm.StartsWith("net10.0", StringComparison.Ordinal), $"{Path.GetFileName(project)} targets '{tfm}'; only net10.0 or net10.0-windows are allowed (.NET 8/9 end of support 2026-11-10).");
        }
    }

    [Fact]
    public void Product_version_is_centrally_defined_in_directory_build_props()
    {
        var props = File.ReadAllText(Path.Combine(Repo.Root, "Directory.Build.props"));
        Assert.Matches(@"<Version>\d+\.\d+\.\d+</Version>", props);

        foreach (var project in Repo.AllFiles("*.csproj"))
        {
            var text = Regex.Replace(File.ReadAllText(project), "<!--.*?-->", string.Empty, RegexOptions.Singleline);
            Assert.False(Regex.IsMatch(text, @"<Version\b[^>]*>"), $"{Repo.Rel(project)} defines its own <Version>; version must be managed centrally in Directory.Build.props.");
        }
    }

    [Fact]
    public void Package_references_are_centrally_versioned_and_recorded_with_the_same_version_in_third_party_docs()
    {
        // Shared build files can add package references to every project, so they are scanned like projects.
        var projectText = new[] { "*.csproj", "*.props", "*.targets" }.SelectMany(Repo.AllFiles)
            .Where(f => !Path.GetFileName(f).Equals("Directory.Packages.props", StringComparison.OrdinalIgnoreCase))
            .Select(File.ReadAllText).ToList();
        Assert.DoesNotContain(projectText, t => Regex.IsMatch(t, @"<PackageReference\b[^>]*\b(?:Version|VersionOverride)\s*=", RegexOptions.IgnoreCase));
        Assert.DoesNotContain(projectText, t => Regex.IsMatch(t, @"<PackageReference\b[^>]*>\s*<Version(?:Override)?>", RegexOptions.IgnoreCase));

        var central = File.ReadAllText(Path.Combine(Repo.Root, "Directory.Packages.props"));
        var versions = Regex.Matches(central, @"<PackageVersion\s+Include=""([^""]+)""\s+Version=""([^""]+)""")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value, StringComparer.OrdinalIgnoreCase);
        Assert.NotEmpty(versions);

        // Every referenced package has a central version.
        var referenced = projectText.SelectMany(t => Regex.Matches(t, @"<PackageReference\s+Include=""([^""]+)""").Select(m => m.Groups[1].Value)).Distinct().ToList();
        Assert.All(referenced, r => Assert.True(versions.ContainsKey(r), $"{r} is referenced but has no PackageVersion in Directory.Packages.props"));

        // Every central package is a row of the 'Packages in use' table with the same version (exact cell match, not a substring).
        var rows = PackagesInUseRows();
        foreach (var (name, version) in versions)
        {
            var row = rows.FirstOrDefault(r => r.Names.Contains(name, StringComparer.OrdinalIgnoreCase));
            Assert.True(row != default, $"{name} is not a row of the 'Packages in use' table in docs/THIRD_PARTY.md");
            Assert.True(row.Version == version, $"{name}: THIRD_PARTY.md says {row.Version}, Directory.Packages.props says {version}");
            Assert.False(string.IsNullOrWhiteSpace(row.License), $"{name}: no license recorded");
        }
    }

    private static List<(string[] Names, string Version, string License)> PackagesInUseRows()
    {
        var lines = File.ReadAllLines(Path.Combine(Repo.Root, "docs", "THIRD_PARTY.md"));
        var start = Array.FindIndex(lines, l => l.StartsWith("## Packages in use", StringComparison.Ordinal));
        Assert.True(start >= 0, "docs/THIRD_PARTY.md has no 'Packages in use' section");
        var rows = new List<(string[], string, string)>();
        foreach (var line in lines.Skip(start + 1))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                break;
            }

            if (!line.StartsWith('|') || line.StartsWith("|---", StringComparison.Ordinal) || line.StartsWith("| Package", StringComparison.Ordinal))
            {
                continue;
            }

            var cells = line.Trim('|').Split('|').Select(c => c.Trim()).ToArray();
            rows.Add((cells[0].Split('/').Select(n => n.Trim()).ToArray(), cells[1], cells[2]));
        }

        return rows;
    }

    [Fact]
    public void PowerShell_scripts_are_ascii_or_carry_a_utf8_bom()
    {
        // Windows PowerShell 5.1 reads a BOM-less file as the ANSI code page: non-ASCII text silently turns into mojibake.
        var scripts = Repo.AllFiles("*.ps1").ToList();
        Assert.NotEmpty(scripts);
        foreach (var script in scripts)
        {
            var bytes = File.ReadAllBytes(script);
            var hasBom = bytes is [0xEF, 0xBB, 0xBF, ..];
            Assert.True(hasBom || bytes.All(b => b < 0x80), $"{Repo.Rel(script)} has non-ASCII bytes but no UTF-8 BOM; Windows PowerShell 5.1 would corrupt it.");
        }
    }

    [Fact]
    public void No_project_references_ad_sdks_advertising_identifiers_or_embedded_web_views()
    {
        // Spec HC-13 & Acceptance #21: static scan proving no ad SDK, advertising identifier, or embedded web view in any project.
        string[] bannedTerms =
        [
            "WebBrowser",
            "WebView",
            "WebView2",
            "Microsoft.Web.WebView2",
            "AdvertisingManager",
            "AdControl",
            "GoogleMobileAds",
            "UnityAds",
            "AppLovin",
            "IronSource",
            "Vungle",
        ];

        var scanned = 0;
        var hits = new List<string>();
        var filesToScan = new[] { "*.cs", "*.csproj", "*.xaml" }.SelectMany(Repo.AllFiles)
            .Where(f => !Repo.Rel(f).EndsWith("RepoLintTests.cs", StringComparison.Ordinal)).ToList();

        foreach (var file in filesToScan)
        {
            scanned++;
            var text = File.ReadAllText(file);
            var rel = Repo.Rel(file);
            foreach (var term in bannedTerms)
            {
                if (Regex.IsMatch(text, $@"\b{Regex.Escape(term)}\b"))
                {
                    hits.Add($"{rel}: found banned term '{term}'");
                }
            }
        }

        Assert.True(scanned > 20, $"only {scanned} files scanned; test would pass vacuously");
        Assert.True(hits.Count == 0, "Banned advertising / web view code detected (HC-13, Acceptance #21): " + string.Join("; ", hits));
    }
}
