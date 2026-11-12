namespace Jankscope.Core.Tests;

/// <summary>Tests of the lint machinery itself: the scanner must follow the compiler's rules, and each rule must catch the evasions found in the Stage 0 reviews.</summary>
public class CSharpScannerTests
{
    private static readonly string Nel = ((char)0x85).ToString();
    private static readonly string LineSep = ((char)0x2028).ToString();
    private static readonly string ParaSep = ((char)0x2029).ToString();

    [Fact]
    public void Line_and_block_comments_are_removed_from_both_views()
    {
        var s = CSharpScanner.Scan("int a; // OpenProcess\n/* WriteProcessMemory\n */ int b;");
        Assert.DoesNotContain("OpenProcess", s.Code);
        Assert.DoesNotContain("OpenProcess", s.CodeAndStrings);
        Assert.DoesNotContain("WriteProcessMemory", s.CodeAndStrings);
        Assert.Contains("int a;", s.Code);
        Assert.Contains("int b;", s.Code);
    }

    [Fact]
    public void Comment_markers_inside_strings_do_not_start_comments()
    {
        var s = CSharpScanner.Scan("var u = \"http://example\"; var f = OpenProcess; // tail");
        Assert.Contains("\"http://example\"", s.CodeAndStrings);
        Assert.Contains("OpenProcess", s.CodeAndStrings); // the old regex stripped everything after '//' in the string
        Assert.DoesNotContain("tail", s.CodeAndStrings);
        Assert.DoesNotContain("example", s.Code); // literal contents are blanked in the code view
    }

    [Fact]
    public void A_quote_inside_a_comment_or_char_literal_does_not_open_a_string()
    {
        var s = CSharpScanner.Scan("var q = '\"'; /* \" */ var n = 1; var e = '\\''; HttpClient c;");
        Assert.Contains("HttpClient", s.Code);
        Assert.Contains("var n = 1;", s.Code);
    }

    [Fact]
    public void Verbatim_strings_handle_doubled_quotes_and_backslashes()
    {
        var s = CSharpScanner.Scan("var p = @\"C:\\dir\\\"\"name\"\" // not a comment\"; Process.Start(p);");
        Assert.Contains("// not a comment", s.CodeAndStrings);
        Assert.Contains("Process.Start(p);", s.Code);
    }

    [Fact]
    public void Interpolation_holes_are_code_and_the_text_around_them_is_literal()
    {
        var s = CSharpScanner.Scan("var t = $\"text {Process.Start(\"x\")} more {{braces}}\";");
        Assert.Contains("Process.Start(", s.Code);
        Assert.DoesNotContain("text", s.Code);
        Assert.DoesNotContain("braces", s.Code);
        Assert.Contains("text", s.CodeAndStrings);
    }

    [Fact]
    public void Raw_strings_keep_their_content_and_interpolated_raw_strings_expose_their_holes()
    {
        var s = CSharpScanner.Scan("var a = \"\"\"\n  // not a comment \"quoted\"\n  \"\"\"; var b = $\"\"\"\n  hi {HttpClient.Name} there\n  \"\"\"; int after;");
        Assert.Contains("// not a comment", s.CodeAndStrings);
        Assert.Contains("HttpClient.Name", s.Code);
        Assert.DoesNotContain("there", s.Code);
        Assert.Contains("int after;", s.Code);
    }

    [Fact]
    public void Literals_split_across_a_concatenation_are_joined()
    {
        var s = CSharpScanner.Scan("var n = \"Open\" + \"Process\"; var m = \"Write\"+\"Process\"+\"Memory\";");
        Assert.Contains("\"OpenProcess\"", s.CodeAndStrings);
        Assert.Contains("\"WriteProcessMemory\"", s.CodeAndStrings);
    }

    [Fact]
    public void Line_structure_is_preserved_in_the_code_view()
    {
        const string Source = "// c1\nint a; /* c2\n c3 */\nvar s = @\"l1\nl2\";\nvar r = \"\"\"\nx\n\"\"\";\n";
        var s = CSharpScanner.Scan(Source);
        Assert.Equal(Source.Count(c => c == '\n'), s.Code.Count(c => c == '\n'));
    }

    // ---- review pass 2 #2: the lexer must end a line comment where the compiler does ----

    [Theory]
    [InlineData("\r")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void A_line_comment_ends_at_cr_lf_and_crlf(string newline)
    {
        var s = CSharpScanner.Scan("// comment" + newline + "OpenProcess();");
        Assert.Contains("OpenProcess", s.Code);
        Assert.Contains("OpenProcess", s.CodeAndStrings);
        Assert.DoesNotContain("comment", s.Code);
    }

    [Fact]
    public void A_line_comment_ends_at_next_line_line_separator_and_paragraph_separator()
    {
        foreach (var newline in new[] { Nel, LineSep, ParaSep })
        {
            var s = CSharpScanner.Scan("// comment" + newline + "Process.Start(\"x\");");
            Assert.Contains("Process.Start(", s.Code);
            Assert.DoesNotContain("comment", s.Code);
        }
    }

    [Fact]
    public void A_stray_quote_on_a_preprocessor_line_does_not_open_a_string()
    {
        var s = CSharpScanner.Scan("#region x \"\nProcess.Start(\"a\");\n#endregion \"\nint after;\n");
        Assert.Contains("Process.Start(", s.Code);
        Assert.Contains("int after;", s.Code);
        Assert.True(s.EndedInCode);
    }

    [Fact]
    public void Preprocessor_lines_are_blanked_in_both_views_but_a_hash_inside_a_literal_is_not_a_directive()
    {
        var s = CSharpScanner.Scan("#if FLAG\nint a;\n#endif\nvar t = @\"x\n#not a directive\ny\"; int b;");
        Assert.DoesNotContain("FLAG", s.Code);
        Assert.Contains("int a;", s.Code);
        Assert.Contains("#not a directive", s.CodeAndStrings);
        Assert.Contains("int b;", s.Code);
        Assert.True(s.EndedInCode);
    }

    [Theory]
    [InlineData("var s = \"never closed;")]
    [InlineData("/* never closed")]
    [InlineData("var c = 'x")]
    [InlineData("var r = \"\"\"never closed")]
    [InlineData("var i = $\"{a + \"")]
    public void A_file_that_ends_inside_a_comment_or_literal_is_reported(string source)
    {
        Assert.False(CSharpScanner.Scan(source).EndedInCode);
    }

    [Fact]
    public void A_well_formed_file_ends_in_code()
    {
        Assert.True(CSharpScanner.Scan("class A { string s = \"x\"; char c = '\\''; /* c */ // d\n}").EndedInCode);
    }

    [Fact]
    public void Unicode_escapes_in_identifiers_and_strings_are_decoded_for_matching()
    {
        var slash = "\\";
        var escapedIdentifier = "var x = Open" + slash + "u0050rocess(1);";
        var s = CSharpScanner.Scan(escapedIdentifier);
        Assert.Contains("OpenProcess", s.Code);
        Assert.Contains("OpenProcess", s.CodeAndStrings);

        var inString = CSharpScanner.Scan("[LibraryImport(\"k.dll\", EntryPoint = \"Open" + slash + "u0050rocess\")]");
        Assert.Contains("OpenProcess", inString.CodeAndStrings);
    }

    [Fact]
    public void Every_real_source_file_scans_to_the_end_and_keeps_its_line_count()
    {
        var files = Repo.AllFiles("*.cs").ToList();
        Assert.True(files.Count > 10);
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            var scanned = CSharpScanner.Scan(text);
            Assert.True(scanned.EndedInCode, $"{Repo.Rel(file)}: the lexer ended inside a literal");
            Assert.Equal(text.Count(CSharpScanner.IsNewLine), scanned.Code.Count(CSharpScanner.IsNewLine));
        }
    }
}

public class ProductLintEngineTests
{
    private static ScannedSource Scan(string code) => CSharpScanner.Scan(code);

    [Theory]
    [InlineData("var h = OpenProcess(0x1000, false, pid);")]
    [InlineData("[LibraryImport(\"kernel32.dll\", EntryPoint = \"OpenProcess\")] static partial nint Foo();")]
    [InlineData("var n = \"Open\" + \"Process\";")]
    [InlineData("var n = \"Write\"+\"Process\"+\"Memory\";")]
    [InlineData("/* harmless */ ReadProcessMemory(h, a, b, 1, out _);")]
    [InlineData("var f = NativeLibrary.GetExport(h, name);")]
    [InlineData("var f = GetProcAddress(m, \"x\");")]
    [InlineData("[DllImport(\"user32.dll\")] static extern bool SetWinEventHook();")]
    [InlineData("CreateServiceW(scm, n, n, 0xF01FF, 1, 3, 1, p, null, out _, null, null, null);")]
    [InlineData("QueueUserAPC(fn, thread, 0);")]
    [InlineData("NtWriteVirtualMemory(h, a, b, n, out _);")]
    [InlineData("LoadLibraryExW(path, 0, 0);")]
    // review pass 2 #1 and #4: families and spellings the first list missed
    [InlineData("ZwOpenProcess(out h, 0x1000, ref oa, ref cid);")]
    [InlineData("[LibraryImport(\"kernel32.dll\", EntryPoint = \"TerminateProcess\")] static partial int T();")]
    [InlineData("[LibraryImport(\"kernel32.dll\", EntryPoint = \"CreateProcessW\")] static partial int C();")]
    [InlineData("[LibraryImport(\"shell32.dll\", EntryPoint = \"ShellExecuteExW\")] static partial int S();")]
    [InlineData("var n = \"ZwWriteVirtualMemory\";")]
    [InlineData("var n = \"Toolhelp32ReadProcessMemory\";")]
    [InlineData("var n = \"SendInput\";")]
    [InlineData("var n = \"RegSetValueExW\";")]
    [InlineData("[DllImportAttribute(\"ntdll.dll\", EntryPoint = \"x\")] static extern int Z();")]
    [InlineData("Assembly\n   .Load(bytes);")]
    [InlineData("AssemblyLoadContext.Default.LoadFromAssemblyPath(p);")]
    public void Forbidden_native_names_are_found_including_evasions(string code)
    {
        Assert.NotEmpty(ProductLints.NativeNameHits(Scan(code)));
    }

    [Theory]
    [InlineData("// OpenProcess is forbidden, see HC-1")]
    [InlineData("/* WriteProcessMemory */ int x;")]
    [InlineData("var token = OpenProcessToken(h, 8, out t);")] // a different API: not a foreign-process handle by itself
    [InlineData("[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]")]
    [InlineData("var s = \"service state\"; var StartTime = 1;")]
    public void Comments_and_unrelated_names_do_not_trigger_the_native_name_lint(string code)
    {
        Assert.Empty(ProductLints.NativeNameHits(Scan(code)));
    }

    [Theory]
    [InlineData("Process.Start(\"x.exe\");", "Process.Start")]
    [InlineData("var i = new ProcessStartInfo(\"x\");", "ProcessStartInfo")]
    [InlineData("var p = new Process { StartInfo = i }; p.Start();", "new Process()")]
    [InlineData("var p = new System.Diagnostics.Process(); p.StartInfo.FileName = \"x\"; p.Start();", "new Process()")]
    [InlineData("System.Diagnostics.Process p = new();", "new Process()")]
    [InlineData("var m = Process.GetProcessById(4).MainModule;", "Process.GetProcessById")]
    [InlineData("var p = Process.GetProcessesByName(\"x\");", "Process.GetProcessesByName")]
    [InlineData("using System.Diagnostics; var t = Process.GetCurrentProcess().StartTime;", "Process members that open handles")]
    [InlineData("using System.Diagnostics; Process p = x; var h = p.Handle;", "Process members that open handles")]
    [InlineData("using System.Diagnostics; foreach (var p in Process.GetProcesses()) { p.CloseMainWindow(); }", "Process members that open handles")]
    [InlineData("using System.Diagnostics; Process p = x; p.WaitForExit();", "Process members that open handles")]
    [InlineData("using System.Diagnostics; Process p = x; p.EnableRaisingEvents = true;", "Process members that open handles")]
    [InlineData("var c = new HttpClient();", "network types")]
    [InlineData("var c = new TcpClient();", "network types")]
    [InlineData("var c = new UdpClient();", "network types")]
    [InlineData("var l = new HttpListener();", "network types")]
    [InlineData("var q = await QuicConnection.ConnectAsync(o);", "network types")]
    [InlineData("var e = await Dns.GetHostEntryAsync(\"x\");", "network types")]
    [InlineData("var r = await new Ping().SendPingAsync(\"x\");", "network types")]
    [InlineData("using System.Net.Sockets;", "network namespaces")]
    [InlineData("using System.Net.Http;", "network namespaces")]
    [InlineData("using System.Net.Quic;", "network namespaces")]
    [InlineData("key.SetValue(\"x\", 1);", "registry writes")]
    [InlineData("Action<string, object> w = key.SetValue;", "registry writes")]
    [InlineData("var k = Registry.LocalMachine.OpenSubKey(p, writable: true);", "registry writes")]
    [InlineData("var k = Registry.LocalMachine.OpenSubKey(p, true);", "registry writes")]
    [InlineData("var k = key.OpenSubKey(p, RegistryKeyPermissionCheck.ReadWriteSubTree);", "registry writes")]
    [InlineData("Registry.CurrentUser.CreateSubKey(\"x\");", "registry writes")]
    [InlineData("key.DeleteValue(\"x\");", "registry writes")]
    [InlineData("using P = System.Diagnostics.Process;", "Process type alias")]
    [InlineData("using static System.Diagnostics.Process;", "Process type alias")]
    [InlineData("var m = typeof(object).GetMethod(\"X\");", "reflection invocation")]
    [InlineData("var o = Activator.CreateInstance(t);", "reflection invocation")]
    [InlineData("var t = Type.GetType(name);", "reflection invocation")]
    [InlineData("var c = new ManagementClass(\"Win32_Process\"); c.InvokeMethod(\"Create\", a);", "WMI methods, shell and COM activation")]
    [InlineData("Microsoft.VisualBasic.Interaction.Shell(\"calc\");", "WMI methods, shell and COM activation")]
    [InlineData("var t = Type.GetTypeFromProgID(\"WScript.Shell\");", "WMI methods, shell and COM activation")]
    [InlineData("var g = new DynamicMethod(\"m\", typeof(void), null);", "code generation")]
    [InlineData("dynamic d = something; d.Run();", "dynamic keyword")]
    [InlineData("protected override void OnPreviewKeyDown(KeyEventArgs e) { log.Add(e.Key); }", "managed keyboard reading")]
    [InlineData("var down = Keyboard.IsKeyDown(Key.A);", "managed keyboard reading")]
    [InlineData("var m = Keyboard.Modifiers;", "managed keyboard reading")]
    [InlineData("window.PreviewKeyDown += (s, e) => keys.Add(e.Key);", "managed keyboard reading")]
    [InlineData("window.KeyUp += OnKey;", "managed keyboard reading")]
    [InlineData("box.TextInput += OnText;", "managed keyboard reading")]
    [InlineData("InputManager.Current.PreProcessInput += Handler;", "managed keyboard reading")]
    [InlineData("ComponentDispatcher.ThreadFilterMessage += Filter;", "managed keyboard reading")]
    [InlineData("Application.AddMessageFilter(filter);", "managed keyboard reading")]
    [InlineData("source.AddHook(WndProc);", "managed keyboard reading")]
    [InlineData("var vk = KeyInterop.VirtualKeyFromKey(e.Key);", "managed keyboard reading")]
    [InlineData("var b = new KeyBinding(cmd, Key.A, ModifierKeys.None);", "managed keyboard reading")]
    public void Banned_managed_apis_are_found(string code, string expectedRule)
    {
        Assert.Contains(expectedRule, ProductLints.ManagedBanHits(Scan(code)));
    }

    [Theory]
    [InlineData("// Process.Start is banned")]
    [InlineData("var s = \"Process.Start and HttpClient\";")]
    [InlineData("var ok = items.Handle;")] // `.Handle` only matters in files that use the Process type
    [InlineData("var p = Process.GetProcesses(); var n = p[0].ProcessName;")]
    [InlineData("var k = Registry.LocalMachine.OpenSubKey(p);")]
    [InlineData("var k = Registry.LocalMachine.OpenSubKey(p, writable: false);")]
    [InlineData("var searcher = new ManagementObjectSearcher(scope, query, options);")]
    [InlineData("// Keyboard.IsKeyDown and PreviewKeyDown are banned")]
    [InlineData("var s = \"PreviewKeyDown and KeyEventArgs\";")]
    [InlineData("var chord = HotkeyModifiers.Control | HotkeyModifiers.Alt;")] // naming a modifier of a registered hotkey reads no key
    [InlineData("var name = settings.KeyName; var id = item.Key;")]
    public void Comments_strings_and_allowed_reads_do_not_trigger_the_managed_lint(string code)
    {
        // OpenSubKey(p, writable: false) is allowed by this text lint but banned by the analyzer (BannedSymbols.txt): the layers differ on purpose.
        Assert.Empty(ProductLints.ManagedBanHits(Scan(code)));
    }

    [Theory]
    [InlineData("var isNvidia = true;", "nvidia")]
    [InlineData("class NVIDIAAdapter { }", "nvidia")]
    [InlineData("var s = \"GeForce\";", "geforce")]
    [InlineData("var isAmd = x;", "amd")]
    [InlineData("var AMDGpu = 1;", "amd")]
    [InlineData("var msi_path = 1;", "msi")]
    [InlineData("var a = \"BattlEye\";", "battleye")]
    [InlineData("void Check_vgk() { }", "vgk")]
    public void Vendor_names_are_found_in_identifiers_and_strings(string code, string term)
    {
        Assert.Contains(term, ProductLints.VendorHits(Scan(code), ["nvidia", "geforce", "amd", "msi", "battleye", "vgk", "intel"]));
    }

    [Theory]
    [InlineData("var madmin = 1;")]
    [InlineData("var msiexec = 1;")]
    [InlineData("// nvidia in a comment only")]
    [InlineData("var Admin = 1;")]
    public void Short_vendor_terms_need_a_whole_word_and_comments_are_ignored(string code)
    {
        Assert.Empty(ProductLints.VendorHits(Scan(code), ["nvidia", "amd", "msi"]));
    }

    // ---- review pass 2 #1: the import gate must read every spelling ----

    [Fact]
    public void A_plain_library_import_reports_dll_and_entry_point()
    {
        var import = Assert.Single(ProductLints.NativeImports(Scan(
            "[LibraryImport(\"Advapi32.dll\", EntryPoint = \"StartTraceW\", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]")));
        Assert.Equal("LibraryImport", import.Token);
        Assert.Equal("advapi32.dll", import.Dll);
        Assert.Equal("StartTraceW", import.EntryPoint);
        Assert.True(import.IsFullyLiteral);
    }

    [Theory]
    [InlineData("[SuppressGCTransition, LibraryImport(\"kernel32.dll\", EntryPoint = \"GetTickCount\")]", "LibraryImport", true)]
    [InlineData("[System.Runtime.InteropServices.LibraryImport(\"kernel32.dll\", EntryPoint = \"GetTickCount\")]", "LibraryImport", true)]
    [InlineData("[LibraryImportAttribute(\"kernel32.dll\", EntryPoint = \"GetTickCount\")]", "LibraryImportAttribute", true)]
    [InlineData("[DllImport(\"kernel32.dll\", EntryPoint = \"GetTickCount\")]", "DllImport", true)]
    [InlineData("[DllImportAttribute(\"kernel32.dll\", EntryPoint = \"GetTickCount\")]", "DllImportAttribute", true)]
    [InlineData("[LibraryImport(Dll, EntryPoint = \"GetTickCount\")]", "LibraryImport", false)]
    [InlineData("[LibraryImport(\"kernel32.dll\", EntryPoint = Name)]", "LibraryImport", false)]
    [InlineData("[LibraryImport(\"kernel32.dll\", EntryPoint = A + B)]", "LibraryImport", false)]
    [InlineData("[LibraryImport(\"kernel32.dll\")]", "LibraryImport", false)]
    [InlineData("[LibraryImport(@\"kernel32.dll\", EntryPoint = \"X\")]", "LibraryImport", false)]
    [InlineData("[LibraryImport($\"kernel32.dll\", EntryPoint = \"X\")]", "LibraryImport", false)]
    [InlineData("using LI = System.Runtime.InteropServices.LibraryImportAttribute;", "LibraryImportAttribute", false)]
    [InlineData("var t = typeof(DllImportAttribute);", "DllImportAttribute", false)]
    [InlineData("[LibraryImport]", "LibraryImport", false)]
    public void Every_spelling_of_a_native_import_is_found_and_only_plain_literals_count_as_readable(string code, string token, bool readable)
    {
        var import = Assert.Single(ProductLints.NativeImports(Scan(code)));
        Assert.Equal(token, import.Token);
        Assert.Equal(readable, import.IsFullyLiteral);
    }

    [Fact]
    public void An_import_spelled_with_an_escape_sequence_is_counted_as_unreadable()
    {
        var code = "[LibraryImpor" + "\\" + "u0074(\"kernel32.dll\", EntryPoint = \"X\")]";
        var import = Assert.Single(ProductLints.NativeImports(Scan(code)));
        Assert.False(import.IsFullyLiteral);
    }

    [Fact]
    public void Imports_in_comments_do_not_count_and_the_search_path_attribute_is_not_an_import()
    {
        Assert.Empty(ProductLints.NativeImports(Scan("// [LibraryImport(\"kernel32.dll\", EntryPoint = \"X\")]")));
        Assert.Empty(ProductLints.NativeImports(Scan("[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]")));
    }

    [Fact]
    public void The_system32_search_path_declaration_is_recognised_only_as_code()
    {
        Assert.True(ProductLints.DeclaresSystem32OnlyDllSearch(Scan("[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]")));
        Assert.False(ProductLints.DeclaresSystem32OnlyDllSearch(Scan("// [assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]")));
        Assert.False(ProductLints.DeclaresSystem32OnlyDllSearch(Scan("[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]")));
    }
}

public class MojibakeDetectorTests
{
    [Theory]
    [InlineData(0xD7)] // multiplication sign: the character HANDOFF.md says was corrupted
    [InlineData(0xF7)] // division sign
    [InlineData(0xA7)] // section sign
    [InlineData(0xE9)] // e acute
    [InlineData(0xC9)] // E acute
    [InlineData(0x2014)] // em dash
    [InlineData(0x2192)] // right arrow
    [InlineData(0x0905)] // Devanagari letter A (relevant for a later Hindi UI)
    [InlineData(0x20AC)] // euro sign
    public void Corrupted_text_is_found_and_sound_text_is_not(int codePoint)
    {
        var good = "before " + char.ConvertFromUtf32(codePoint) + " after";
        Assert.Null(Mojibake.Find(good));
        Assert.NotNull(Mojibake.Find(Mojibake.Corrupt(good)));
    }

    [Fact]
    public void Ordinary_text_with_accents_symbols_and_other_scripts_is_not_flagged()
    {
        var text = "caf" + (char)0xE9 + " na" + (char)0xEF + "ve " + (char)0xD7 + " " + (char)0xF7 + " " + (char)0xA7 + " " + (char)0x2014 + " "
                   + (char)0x00FC + (char)0x00F6 + " " + (char)0x0905 + " plain ascii";
        Assert.Null(Mojibake.Find(text));
    }
}

public class RepoScopeTests
{
    private static string Rel(string root, string path) => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    [Fact]
    public void Build_output_is_excluded_only_directly_inside_a_project_as_the_sdk_does()
    {
        var root = Path.Combine(Path.GetTempPath(), "jankscope-scope-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var dir in new[] { "proj/bin", "proj/obj", "proj/Probes/bin", "proj/Probes/obj", "plain/bin", "plugins/Evil" })
            {
                Directory.CreateDirectory(Path.Combine(root, dir));
            }

            File.WriteAllText(Path.Combine(root, "proj", "proj.csproj"), "<Project />");
            foreach (var file in new[] { "proj/bin/a.cs", "proj/obj/b.cs", "proj/Probes/bin/c.cs", "proj/Probes/obj/d.cs", "proj/Probes/e.cs", "proj/f.cs", "plain/bin/g.cs", "plugins/Evil/h.cs" })
            {
                File.WriteAllText(Path.Combine(root, file), "class X {}");
            }

            var found = Repo.FilesIn(root, "*.cs").Select(p => Rel(root, p)).Order(StringComparer.Ordinal).ToArray();

            // SDK build output (proj/bin, proj/obj) is skipped; a bin folder deeper in a project, or one in a directory that is not a
            // project, is compiled or scanned like any other source (review pass 2 #3).
            Assert.Equal(
                ["plain/bin/g.cs", "plugins/Evil/h.cs", "proj/Probes/bin/c.cs", "proj/Probes/e.cs", "proj/Probes/obj/d.cs", "proj/f.cs"],
                found);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_nested_checkout_is_not_scanned_but_an_ordinary_directory_beside_it_still_is()
    {
        // An agent worktree under .claude/worktrees holds a whole copy of the repository; it has its own .git (a file in a linked worktree).
        // Scanning it doubled every project and failed the local gate. It must be skipped, and nothing else may be.
        var root = Path.Combine(Path.GetTempPath(), "jankscope-nested-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var dir in new[] { "src/real", "tools/inner", ".claude/worktrees/wt/src/Copy", "elsewhere/clone/src" })
            {
                Directory.CreateDirectory(Path.Combine(root, dir));
            }

            File.WriteAllText(Path.Combine(root, ".claude", "worktrees", "wt", ".git"), "gitdir: C:/somewhere/.git/worktrees/wt"); // a linked worktree: .git is a file
            Directory.CreateDirectory(Path.Combine(root, "elsewhere", "clone", ".git"));                                              // a full clone: .git is a directory
            foreach (var file in new[] { "src/real/a.cs", "tools/inner/b.cs", ".claude/worktrees/wt/src/Copy/c.cs", "elsewhere/clone/src/d.cs", "elsewhere/clone/e.cs" })
            {
                File.WriteAllText(Path.Combine(root, file), "class X {}");
            }

            var found = Repo.FilesIn(root, "*.cs").Select(p => Rel(root, p)).Order(StringComparer.Ordinal).ToArray();

            Assert.Equal(["src/real/a.cs", "tools/inner/b.cs"], found);
            Assert.True(Repo.IsNestedCheckout(Path.Combine(root, ".claude", "worktrees", "wt")));
            Assert.True(Repo.IsNestedCheckout(Path.Combine(root, "elsewhere", "clone")));
            Assert.False(Repo.IsNestedCheckout(Path.Combine(root, "src", "real")));
            Assert.False(Repo.IsNestedCheckout(Path.Combine(root, ".claude"))); // .claude itself is not a checkout; only what has its own .git is skipped
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void The_real_repository_scan_finds_the_product_projects_and_skips_their_build_output()
    {
        var sources = Repo.AllFiles("*.cs").Select(Repo.Rel).ToList();
        Assert.Contains("src/Jankscope.Platform/Native.cs", sources);
        Assert.DoesNotContain(sources, s => s.StartsWith("src/Jankscope.Platform/obj/", StringComparison.Ordinal) || s.StartsWith("src/Jankscope.Platform/bin/", StringComparison.Ordinal));
        Assert.DoesNotContain(sources, s => s.StartsWith(".git/", StringComparison.Ordinal));
    }
}
