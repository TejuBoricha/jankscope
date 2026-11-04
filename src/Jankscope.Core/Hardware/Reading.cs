using System.Globalization;

namespace Jankscope.Core.Hardware;

/// <summary>
/// One measured number or an explicit "not available (reason)" (spec section 6.4). There is deliberately no way to build an
/// unavailable reading that carries a zero: a missing counter, instance or sample is never a 0. A default-initialised value is
/// unavailable too, with the reason "not read".
/// </summary>
public readonly record struct Reading
{
    private readonly double _value;
    private readonly bool _available;
    private readonly string? _reason;

    private Reading(double value, bool available, string? reason)
    {
        _value = value;
        _available = available;
        _reason = reason;
    }

    public bool IsAvailable => _available;

    /// <summary>The number, or null when the reading is not available.</summary>
    public double? Value => _available ? _value : null;

    /// <summary>Why there is no number; null when the reading is available.</summary>
    public string? UnavailableReason => _available ? null : _reason ?? "not read";

    /// <summary>A real measurement. Zero is a valid measurement. NaN and infinities are not measurements and are rejected.</summary>
    public static Reading Of(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "A reading must be a finite number; use NotAvailable for a value that could not be obtained.");
        }

        return new Reading(value, true, null);
    }

    public static Reading NotAvailable(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new Reading(0, false, reason);
    }

    public override string ToString() =>
        _available ? _value.ToString("R", CultureInfo.InvariantCulture) : $"not available ({UnavailableReason})";
}
