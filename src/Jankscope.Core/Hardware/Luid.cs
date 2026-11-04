using System.Globalization;

namespace Jankscope.Core.Hardware;

/// <summary>
/// A locally unique adapter identifier (the Windows LUID). It is the only key used for GPUs: adapter enumeration order is not
/// stable and the performance counters name adapters by LUID, never by index (spec section 7.2).
/// </summary>
public readonly record struct Luid(int High, uint Low) : IComparable<Luid>
{
    public static Luid FromInt64(long value) => new((int)(value >> 32), (uint)(value & 0xFFFFFFFFL));

    public long ToInt64() => ((long)High << 32) | Low;

    /// <summary>The spelling used inside performance counter instance names, for example <c>0x00000000_0x00011B0C</c>.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"0x{unchecked((uint)High):X8}_0x{Low:X8}");

    public int CompareTo(Luid other) => ToInt64().CompareTo(other.ToInt64());

    /// <summary>Parses exactly <c>0x&lt;1-8 hex digits&gt;_0x&lt;1-8 hex digits&gt;</c> (the "0x" prefix may be upper or lower case).</summary>
    public static bool TryParse(ReadOnlySpan<char> text, out Luid luid)
    {
        luid = default;
        var separator = text.IndexOf('_');
        if (separator <= 0 || !TryHexWord(text[..separator], out var high) || !TryHexWord(text[(separator + 1)..], out var low))
        {
            return false;
        }

        luid = new Luid(unchecked((int)high), low);
        return true;
    }

    private static bool TryHexWord(ReadOnlySpan<char> text, out uint value)
    {
        value = 0;
        if (text.Length < 3 || text[0] != '0' || (text[1] != 'x' && text[1] != 'X'))
        {
            return false;
        }

        var digits = text[2..];
        if (digits.Length > 8)
        {
            return false;
        }

        foreach (var c in digits)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                return false;
            }
        }

        return uint.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
    }
}
