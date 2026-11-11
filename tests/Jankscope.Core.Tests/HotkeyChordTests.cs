using Jankscope.Core.Hotkeys;

namespace Jankscope.Core.Tests;

public class HotkeyChordTests
{
    [Theory]
    [InlineData("Ctrl+Alt+F9", HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x78, "Ctrl+Alt+F9")]
    [InlineData("ctrl+alt+f10", HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x79, "Ctrl+Alt+F10")]
    [InlineData("  Alt + Ctrl + F9 ", HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x78, "Ctrl+Alt+F9")]
    [InlineData("Control+Shift+K", HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x4B, "Ctrl+Shift+K")]
    [InlineData("Alt+Shift+7", HotkeyModifiers.Alt | HotkeyModifiers.Shift, 0x37, "Alt+Shift+7")]
    [InlineData("Ctrl+F1", HotkeyModifiers.Control, 0x70, "Ctrl+F1")]
    [InlineData("Ctrl+Alt+Shift+F24", HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift, 0x87, "Ctrl+Alt+Shift+F24")]
    public void Valid_chords_parse_and_print_in_canonical_form(string text, HotkeyModifiers modifiers, int virtualKey, string canonical)
    {
        Assert.True(HotkeyChord.TryParse(text, out var chord, out var error), error);
        Assert.Equal(modifiers, chord.Modifiers);
        Assert.Equal(virtualKey, chord.VirtualKey);
        Assert.Equal(canonical, chord.ToString());
        Assert.True(HotkeyChord.TryParse(chord.ToString(), out var again, out _));
        Assert.Equal(chord, again);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("F9")]                 // a bare key would be taken away from typing
    [InlineData("Shift+F9")]           // Shift alone is not enough
    [InlineData("Shift+A")]
    [InlineData("Ctrl+Alt")]           // no key
    [InlineData("Ctrl+Alt+F9+F10")]    // two keys
    [InlineData("Ctrl+Ctrl+F9")]       // repeated modifier
    [InlineData("Ctrl++F9")]           // empty part
    [InlineData("Ctrl+Alt+Delete")]    // not a supported key
    [InlineData("Ctrl+Alt+Esc")]
    [InlineData("Ctrl+Alt+F25")]
    [InlineData("Ctrl+Alt+F0")]
    [InlineData("Ctrl+F25")]           // Ctrl alone, so only the F1-F24 range can refuse these (the AltGr rule would mask Ctrl+Alt+F25)
    [InlineData("Ctrl+F0")]
    [InlineData("Ctrl+F100")]
    [InlineData("Ctrl+@")]             // the characters just outside A-Z and 0-9 (and one more of each kind), refused by the key rule itself
    [InlineData("Ctrl+[")]
    [InlineData("Ctrl+/")]
    [InlineData("Ctrl+:")]
    [InlineData("Ctrl+;")]
    [InlineData("Ctrl+Space")]
    [InlineData("Win+F9")]             // reserved by Windows
    [InlineData("Ctrl+Win+F9")]
    [InlineData("Alt+F4")]             // closes the foreground window
    [InlineData("Ctrl+Alt+Q")]         // AltGr on many layouts: would swallow typed characters
    [InlineData("Ctrl+Alt+5")]
    [InlineData("Ctrl+Alt+Shift+Z")]
    [InlineData("Ctrl+Alt+F9\u0000")]
    public void Unsafe_or_malformed_chords_are_refused_with_a_reason(string? text)
    {
        Assert.False(HotkeyChord.TryParse(text, out var chord, out var error));
        Assert.Null(chord);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Alt_with_another_key_is_allowed_only_when_it_is_not_the_window_close_chord()
    {
        Assert.True(HotkeyChord.TryParse("Alt+F9", out _, out _));
        Assert.False(HotkeyChord.TryParse("alt+f4", out _, out _));
    }

    [Fact]
    public void Defaults_are_the_documented_chords_and_differ()
    {
        var defaults = HotkeyBindings.Default;
        Assert.Equal("Ctrl+Alt+F9", defaults.MarkStutter.ToString());
        Assert.Equal("Ctrl+Alt+F10", defaults.MarkInputStall.ToString());
        Assert.NotEqual(defaults.MarkStutter, defaults.MarkInputStall);
    }

    [Fact]
    public void Two_actions_cannot_share_a_chord_even_when_spelled_differently()
    {
        Assert.False(HotkeyBindings.TryCreate("Ctrl+Alt+F9", "alt+ctrl+f9", out var bindings, out var error));
        Assert.Null(bindings);
        Assert.Contains("same", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Bindings_report_which_action_has_the_bad_chord()
    {
        Assert.False(HotkeyBindings.TryCreate("Ctrl+Alt+F9", "F10", out _, out var error));
        Assert.Contains("input stall", error, StringComparison.OrdinalIgnoreCase);
        Assert.False(HotkeyBindings.TryCreate("nonsense", "Ctrl+Alt+F10", out _, out error));
        Assert.Contains("stutter", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Bindings_enumerate_both_actions()
    {
        var all = HotkeyBindings.Default.All().ToArray();
        Assert.Equal([HotkeyAction.MarkStutter, HotkeyAction.MarkInputStall], all.Select(a => a.Action).ToArray());
    }

    [Fact]
    public void Modifier_flag_values_match_the_win32_constants()
    {
        // RegisterHotKey takes MOD_ALT=1, MOD_CONTROL=2, MOD_SHIFT=4, MOD_WIN=8. A wrong value would register a different chord.
        Assert.Equal(1, (int)HotkeyModifiers.Alt);
        Assert.Equal(2, (int)HotkeyModifiers.Control);
        Assert.Equal(4, (int)HotkeyModifiers.Shift);
        Assert.Equal(8, (int)HotkeyModifiers.Windows);
    }
}
