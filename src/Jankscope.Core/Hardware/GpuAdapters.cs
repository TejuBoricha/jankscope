using System.Globalization;
using System.Text;

namespace Jankscope.Core.Hardware;

/// <summary>
/// One graphics adapter as DXGI reports it, keyed by <see cref="Luid"/> (never by enumeration index). Vendor and device are the numeric
/// PCI ids: naming them is data (a catalog), not code. These are facts; what they mean for a game is decided elsewhere.
/// </summary>
public sealed record GpuAdapterInfo(
    Luid Luid,
    string Description,
    uint VendorId,
    uint DeviceId,
    uint SubsystemId,
    uint Revision,
    ulong DedicatedVideoMemoryBytes,
    ulong DedicatedSystemMemoryBytes,
    ulong SharedSystemMemoryBytes,
    bool IsSoftware,
    bool IsRemote);

public static class GpuAdapterReport
{
    private const int MaxDescriptionLength = 128;
    private const ulong MiB = 1024UL * 1024UL;

    /// <param name="adapters">Any enumeration order; the text is sorted by LUID.</param>
    /// <param name="drivenDisplays">Active displays per driving adapter, or null when the display data is not known (nothing is then claimed).</param>
    public static string Describe(IEnumerable<GpuAdapterInfo> adapters, IReadOnlyDictionary<Luid, int>? drivenDisplays = null)
    {
        var distinct = new Dictionary<Luid, GpuAdapterInfo>();
        foreach (var adapter in adapters)
        {
            distinct.TryAdd(adapter.Luid, adapter);
        }

        var inv = CultureInfo.InvariantCulture;
        var parts = distinct.Values.OrderBy(a => a.Luid).Select(a =>
        {
            var sb = new StringBuilder();
            sb.Append(inv, $"[LUID {a.Luid}, {CleanDescription(a.Description)}, vendor 0x{a.VendorId:X4}, device 0x{a.DeviceId:X4}, ")
              .Append(inv, $"dedicated {a.DedicatedVideoMemoryBytes / MiB} MiB, shared {a.SharedSystemMemoryBytes / MiB} MiB");
            if (a.IsSoftware)
            {
                sb.Append(", software renderer");
            }

            if (a.IsRemote)
            {
                sb.Append(", remote adapter");
            }

            if (drivenDisplays is not null)
            {
                sb.Append(drivenDisplays.TryGetValue(a.Luid, out var n) && n > 0
                    ? string.Create(inv, $", drives {n} active display(s)")
                    : ", drives no active display");
            }

            return sb.Append(']').ToString();
        });

        return string.Create(inv, $"{distinct.Count} adapter(s): {string.Join("; ", parts)}");
    }

    /// <summary>The driver supplies the description, so it is treated as untrusted text: whitespace is normalised, other control characters are dropped, the length is bounded.</summary>
    public static string CleanDescription(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "(no description)";
        }

        var sb = new StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            if (char.IsWhiteSpace(c))
            {
                if (sb.Length > 0 && sb[^1] != ' ')
                {
                    sb.Append(' ');
                }
            }
            else if (!char.IsControl(c))
            {
                sb.Append(c);
            }
        }

        var text = sb.ToString().Trim();
        if (text.Length > MaxDescriptionLength)
        {
            text = text[..MaxDescriptionLength];
        }

        return text.Length == 0 ? "(no description)" : text;
    }
}
