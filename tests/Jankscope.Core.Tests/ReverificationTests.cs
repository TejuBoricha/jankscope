namespace Jankscope.Core.Tests;

/// <summary>Regression tests for the re-verification of the pass-2 fixes (docs/reviews/stage-0-pass2-reverify.md, R1, R3, R5, R6, R7).</summary>
public class InvisibleCharacterTests
{
    [Theory]
    [InlineData(0x00AD)] // soft hyphen
    [InlineData(0x200B)] // zero width space
    [InlineData(0x200C)] // zero width non-joiner
    [InlineData(0x200D)] // zero width joiner
    [InlineData(0x2060)] // word joiner
    [InlineData(0xFEFF)] // byte order mark inside a file
    public void A_format_character_inside_an_identifier_is_reported_because_the_compiler_ignores_it(int codePoint)
    {
        var scanned = CSharpScanner.Scan("[Library" + (char)codePoint + "Import(\"user32.dll\", EntryPoint = \"GetAsyncKeyState\")]");
        Assert.Contains((char)codePoint, scanned.NonAsciiCode);
    }

    [Fact]
    public void The_same_character_produced_by_a_unicode_escape_is_reported_too()
    {
        var scanned = CSharpScanner.Scan("[Library" + "\\" + "u00ADImport(\"user32.dll\", EntryPoint = \"X\")]");
        Assert.Contains((char)0x00AD, scanned.NonAsciiCode);
    }

    [Fact]
    public void Non_ascii_text_in_comments_strings_char_literals_and_directives_is_not_code()
    {
        var e = ((char)0xE9).ToString();
        var source = "// caf" + e + "\nvar s = \"caf" + e + "\"; var c = '" + e + "';\n#region caf" + e + "\n/* caf" + e + " */ int x;";
        var scanned = CSharpScanner.Scan(source);
        Assert.Equal(string.Empty, scanned.NonAsciiCode);
        Assert.True(scanned.EndedInCode);
    }

    [Fact]
    public void Ordinary_ascii_code_has_no_non_ascii_characters()
    {
        Assert.Equal(string.Empty, CSharpScanner.Scan("class A { int B() => 1; }").NonAsciiCode);
    }
}

public class WiderNameAndFamilyTests
{
    private static ScannedSource Scan(string code) => CSharpScanner.Scan(code);

    [Theory]
    [InlineData("GetAsyncKeyState")]
    [InlineData("GetKeyState")]
    [InlineData("GetKeyboardState")]
    [InlineData("BlockInput")]
    [InlineData("SetCursorPos")]
    [InlineData("SetPriorityClass")]
    [InlineData("SetProcessAffinityMask")]
    [InlineData("EmptyWorkingSet")]
    [InlineData("ZwSetInformationProcess")]
    [InlineData("PostMessageW")]
    [InlineData("SendMessageW")]
    [InlineData("EndTask")]
    [InlineData("DeviceIoControl")]
    [InlineData("NtDeviceIoControlFile")]
    [InlineData("NtSetValueKey")]
    [InlineData("SHSetValueW")]
    [InlineData("RegSaveKeyExW")]
    [InlineData("PowerSetActiveScheme")]
    [InlineData("PowerWriteACValueIndex")]
    [InlineData("SystemParametersInfoW")]
    [InlineData("ChangeDisplaySettingsExW")]
    [InlineData("NtSetTimerResolution")]
    [InlineData("timeBeginPeriod")]
    [InlineData("WSAStartup")]
    [InlineData("WinHttpOpen")]
    [InlineData("InternetOpenW")]
    [InlineData("URLDownloadToFileW")]
    [InlineData("getaddrinfo")]
    [InlineData("AdjustTokenPrivileges")]
    [InlineData("LogonUserW")]
    public void More_native_api_families_are_recognised_by_name(string name)
    {
        Assert.NotEmpty(ProductLints.NativeNameHits(Scan("var n = \"" + name + "\";")));
    }

    [Fact]
    public void A_hex_escape_in_a_string_is_decoded_before_matching()
    {
        var hexEscaped = "var n = \"" + "\\" + "x4fpenProcess\";";
        Assert.NotEmpty(ProductLints.NativeNameHits(Scan(hexEscaped)));
    }

    [Theory]
    [InlineData("var o = new ManagementObject(path);")]
    [InlineData("[ComImport, Guid(\"72C24DD5-D70A-438B-8A42-98424B88AFB8\")] class WshShell { }")]
    [InlineData("[CoClass(typeof(WshShell))] interface IWshShell { }")]
    [InlineData("var o = Marshal.GetObjectForIUnknown(p);")]
    [InlineData("var f = Marshal.GetFunctionPointerForDelegate(d);")]
    [InlineData("var a = Assembly.UnsafeLoadFrom(p);")]
    [InlineData("AppDomain.CurrentDomain.Load(bytes);")]
    [InlineData("var p = new NamedPipeClientStream(\"server\", \"pipe\");")]
    [InlineData("var k = RegistryKey.OpenRemoteBaseKey(RegistryHive.LocalMachine, \"pc\");")]
    [InlineData("var o = Assembly.CreateInstance(\"X\");")]
    public void Families_both_layers_once_missed_are_caught_by_the_text_lint(string code)
    {
        Assert.NotEmpty(ProductLints.ManagedBanHits(Scan(code)));
    }
}

public class SafetyStateRobustnessTests
{
    [Theory]
    [InlineData(42)]
    [InlineData(-1)]
    [InlineData(5)]
    public void An_undefined_state_value_is_treated_as_safe_mode_not_as_idle(int value)
    {
        var state = (AntiCheatSafetyState)value;
        Assert.True(AntiCheatSafety.IsSafeModeActive(state));
        Assert.True(AntiCheatSafety.Decide(CollectorClass.ElevatedHelper, state, userOverridePerGame: false).Suppressed);
        Assert.False(AntiCheatSafety.Decide(CollectorClass.Pdh, state, userOverridePerGame: false).Suppressed); // allow-list stays allowed
    }

    [Fact]
    public void Only_idle_and_resident_only_are_not_safe_mode()
    {
        var notSafe = Enum.GetValues<AntiCheatSafetyState>().Where(s => !AntiCheatSafety.IsSafeModeActive(s)).ToArray();
        Assert.Equal([AntiCheatSafetyState.Idle, AntiCheatSafetyState.ResidentOnly], notSafe.Order().ToArray());
    }
}

public class MojibakeReverificationTests
{
    [Fact]
    public void A_corrupted_run_next_to_a_character_windows_1252_cannot_hold_is_still_found()
    {
        var arrow = ((char)0x2192).ToString();
        var check = ((char)0x2713).ToString();
        var e = ((char)0xE9).ToString();
        Assert.NotNull(Mojibake.Find(Mojibake.Corrupt(e) + arrow));
        Assert.NotNull(Mojibake.Find(check + Mojibake.Corrupt(e)));
        Assert.NotNull(Mojibake.Find("a " + arrow + " " + Mojibake.Corrupt(e) + " b"));
    }

    [Theory]
    [InlineData(0xD7)] // multiplication sign
    [InlineData(0xD6)] // O umlaut
    [InlineData(0x2014)] // em dash
    [InlineData(0xA7)] // section sign
    [InlineData(0x0905)] // Devanagari
    public void Latin_1_style_corruption_that_leaves_c1_control_characters_is_found(int codePoint)
    {
        var good = "x" + char.ConvertFromUtf32(codePoint) + "y";
        Assert.Null(Mojibake.Find(good));
        Assert.NotNull(Mojibake.Find(Mojibake.CorruptAsLatin1(good)));
    }

    [Fact]
    public void A_lone_c1_control_character_is_always_suspicious()
    {
        Assert.NotNull(Mojibake.Find("before " + (char)0x97 + " after"));
    }

    [Fact]
    public void Sound_text_mixing_accents_and_arrows_is_still_clean()
    {
        var text = "caf" + (char)0xE9 + " " + (char)0x2192 + " na" + (char)0xEF + "ve " + (char)0x2713 + " " + (char)0xA7 + (char)0xF7 + (char)0xD7;
        Assert.Null(Mojibake.Find(text));
    }
}
