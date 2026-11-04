using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Jankscope.Core.Hotkeys;

/// <summary>
/// Modifier keys of a global hotkey. The numeric values are the Win32 <c>MOD_ALT</c>, <c>MOD_CONTROL</c>, <c>MOD_SHIFT</c> and <c>MOD_WIN</c>
/// flags, so a value can be handed to <c>RegisterHotKey</c> unchanged.
/// </summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8,
}

/// <summary>What a hotkey does. The marker is keyboard-triggered on purpose: the mouse may be the thing that froze (spec section 8).</summary>
public enum HotkeyAction
{
    MarkStutter = 0,
    MarkInputStall = 1,
}

/// <summary>
/// A validated global hotkey: modifiers plus one key from a small, safe set. Hotkeys are registered with <c>RegisterHotKey</c> only (HC-4); this type
/// never describes a key that would be read, only a chord the person chose to trigger a mark with.
/// <para>
/// Rules, each with the reason shown to the person when it is broken: a key needs Ctrl or Alt (otherwise the key would be taken away from typing);
/// the Windows key is reserved by Windows; Alt+F4 closes the foreground window; Ctrl+Alt with a letter or digit is AltGr on many keyboard layouts and
/// would swallow typed characters (the function keys are safe, which is why the defaults use them); and only F1-F24, A-Z and 0-9 are accepted.
/// </para>
/// </summary>
public sealed record HotkeyChord
{
    private const int VirtualKeyF1 = 0x70;
    private const int VirtualKeyF24 = 0x87;

    private HotkeyChord(HotkeyModifiers modifiers, int virtualKey, string keyName)
    {
        Modifiers = modifiers;
        VirtualKey = virtualKey;
        KeyName = keyName;
    }

    public HotkeyModifiers Modifiers { get; }

    /// <summary>The Windows virtual-key code (<c>VK_F9</c> is 0x78; letters and digits are their upper-case ASCII codes).</summary>
    public int VirtualKey { get; }

    /// <summary>"F9", "K", "7".</summary>
    public string KeyName { get; }

    /// <summary>Canonical text: modifiers in the fixed order Ctrl, Alt, Shift, then the key. Round-trips through <see cref="TryParse"/>.</summary>
    public override string ToString()
    {
        var parts = new List<string>(4);
        if (Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        parts.Add(KeyName);
        return string.Join('+', parts);
    }

    public static HotkeyChord Parse(string text) =>
        TryParse(text, out var chord, out var error) ? chord : throw new FormatException(error);

    public static bool TryParse(string? text, [NotNullWhen(true)] out HotkeyChord? chord, [NotNullWhen(false)] out string? error)
    {
        chord = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Type a chord such as Ctrl+Alt+F9.";
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        string? keyToken = null;
        foreach (var raw in text.Split('+'))
        {
            var token = raw.Trim();
            if (token.Length == 0)
            {
                error = "The chord has an empty part; separate keys with a single +.";
                return false;
            }

            var modifier = ModifierFor(token);
            if (modifier != HotkeyModifiers.None)
            {
                if (modifiers.HasFlag(modifier))
                {
                    error = $"'{token}' appears twice.";
                    return false;
                }

                modifiers |= modifier;
                continue;
            }

            if (keyToken is not null)
            {
                error = "A chord has exactly one key besides its modifiers.";
                return false;
            }

            keyToken = token;
        }

        if (keyToken is null)
        {
            error = "The chord needs a key besides its modifiers (F1-F24, A-Z or 0-9).";
            return false;
        }

        if (!TryKey(keyToken, out var virtualKey, out var keyName))
        {
            error = $"'{Printable(keyToken)}' is not a supported key; use F1-F24, A-Z or 0-9.";
            return false;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Windows))
        {
            error = "Chords with the Windows key are reserved by Windows; use Ctrl and/or Alt.";
            return false;
        }

        if (!modifiers.HasFlag(HotkeyModifiers.Control) && !modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            error = "The chord needs Ctrl or Alt; otherwise the key could no longer be typed.";
            return false;
        }

        if (modifiers == HotkeyModifiers.Alt && virtualKey == VirtualKeyF1 + 3)
        {
            error = "Alt+F4 closes the foreground window; choose another chord.";
            return false;
        }

        var isFunctionKey = virtualKey is >= VirtualKeyF1 and <= VirtualKeyF24;
        if (!isFunctionKey && modifiers.HasFlag(HotkeyModifiers.Control) && modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            error = "Ctrl+Alt with a letter or digit is AltGr on many keyboards and would swallow typed characters; use a function key (F1-F24).";
            return false;
        }

        chord = new HotkeyChord(modifiers, virtualKey, keyName);
        error = null;
        return true;
    }

    private static HotkeyModifiers ModifierFor(string token) => token.ToLowerInvariant() switch
    {
        "ctrl" or "control" => HotkeyModifiers.Control,
        "alt" => HotkeyModifiers.Alt,
        "shift" => HotkeyModifiers.Shift,
        "win" or "windows" or "super" => HotkeyModifiers.Windows,
        _ => HotkeyModifiers.None,
    };

    private static bool TryKey(string token, out int virtualKey, out string keyName)
    {
        virtualKey = 0;
        keyName = string.Empty;
        var upper = token.ToUpperInvariant();
        if (upper.Length >= 2 && upper[0] == 'F' && upper.AsSpan(1).IndexOfAnyExceptInRange('0', '9') < 0
            && int.TryParse(upper.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number is >= 1 and <= 24)
        {
            virtualKey = VirtualKeyF1 + number - 1;
            keyName = "F" + number.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        if (upper.Length == 1 && upper[0] is (>= 'A' and <= 'Z') or (>= '0' and <= '9'))
        {
            virtualKey = upper[0];
            keyName = upper;
            return true;
        }

        return false;
    }

    private static string Printable(string token) =>
        token.Length > 24 ? token[..24] + "..." : string.Concat(token.Select(c => char.IsControl(c) ? '?' : c));
}

/// <summary>The chords for each action. Two actions never share a chord.</summary>
public sealed record HotkeyBindings(HotkeyChord MarkStutter, HotkeyChord MarkInputStall)
{
    public static HotkeyBindings Default { get; } = new(HotkeyChord.Parse("Ctrl+Alt+F9"), HotkeyChord.Parse("Ctrl+Alt+F10"));

    public IEnumerable<(HotkeyAction Action, HotkeyChord Chord)> All()
    {
        yield return (HotkeyAction.MarkStutter, MarkStutter);
        yield return (HotkeyAction.MarkInputStall, MarkInputStall);
    }

    public static bool TryCreate(string stutter, string inputStall, [NotNullWhen(true)] out HotkeyBindings? bindings, [NotNullWhen(false)] out string? error)
    {
        bindings = null;
        if (!HotkeyChord.TryParse(stutter, out var first, out var firstError))
        {
            error = "Stutter mark: " + firstError;
            return false;
        }

        if (!HotkeyChord.TryParse(inputStall, out var second, out var secondError))
        {
            error = "Input stall mark: " + secondError;
            return false;
        }

        if (first == second)
        {
            error = "Both marks use the same chord; each action needs its own.";
            return false;
        }

        bindings = new HotkeyBindings(first, second);
        error = null;
        return true;
    }
}
