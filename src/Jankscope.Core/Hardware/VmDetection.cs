namespace Jankscope.Core.Hardware;

/// <summary>
/// One entry of catalogs/vm_markers.json: substrings that appear in the firmware strings of a virtual machine. All names live in the
/// catalog; this code contains no hypervisor or vendor name (HC-12).
/// </summary>
public sealed record VmMarkerEntry(
    string Id,
    string DisplayName,
    IReadOnlyList<string> ManufacturerMarkers,
    IReadOnlyList<string> ModelMarkers,
    IReadOnlyList<string> BiosMarkers,
    string Verification);

/// <summary>
/// The firmware strings read from SMBIOS through documented WMI classes (Win32_ComputerSystem, Win32_BaseBoard, Win32_BIOS). A null is
/// "could not be read". <paramref name="SmbiosVirtualMachineFlag"/> is the documented Win32_BIOS characteristic "the SMBIOS table
/// describes a virtual machine" (documented as: if it is not set, nothing can be inferred); <paramref name="HypervisorPresent"/> is
/// Win32_ComputerSystem.HypervisorPresent, which is also true on a physical host that runs virtualization-based security.
/// </summary>
public sealed record FirmwareStrings(
    string? SystemManufacturer,
    string? SystemModel,
    string? BiosVendor,
    string? BiosVersion,
    string? BaseBoardManufacturer,
    string? BaseBoardProduct,
    bool? SmbiosVirtualMachineFlag,
    bool? HypervisorPresent);

public enum VmVerdict
{
    NotVirtual,
    Virtual,
    Unknown,
}

/// <param name="Name">The catalog display name (or <see cref="VmDetector.UnidentifiedHypervisorName"/>) when the verdict is Virtual.</param>
/// <param name="Reason">Why. It quotes catalog data only, never the machine's own strings.</param>
public sealed record VmDetection(VmVerdict Verdict, string? Name, string Reason);

public static class VmDetector
{
    /// <summary>The one name that is not from the catalog: used when SMBIOS itself says "virtual machine" but no catalog entry matches.</summary>
    public const string UnidentifiedHypervisorName = "unidentified hypervisor (SMBIOS reports a virtual machine)";

    public static VmDetection Detect(IReadOnlyList<VmMarkerEntry> catalog, FirmwareStrings strings)
    {
        if (catalog.Count == 0)
        {
            return new VmDetection(VmVerdict.Unknown, null, "the VM marker catalog is not loaded or is empty, so firmware strings cannot be classified");
        }

        foreach (var entry in catalog)
        {
            if (Match(entry, strings) is { } hit)
            {
                return new VmDetection(VmVerdict.Virtual, entry.DisplayName, $"the {hit.Field} contains the catalog marker '{hit.Marker}' of entry '{entry.Id}'");
            }
        }

        if (strings.SmbiosVirtualMachineFlag == true)
        {
            return new VmDetection(VmVerdict.Virtual, UnidentifiedHypervisorName,
                "the SMBIOS table describes a virtual machine (documented Win32_BIOS characteristic) and no catalog marker matched");
        }

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(strings.SystemManufacturer))
        {
            missing.Add("system manufacturer");
        }

        if (string.IsNullOrWhiteSpace(strings.SystemModel))
        {
            missing.Add("system model");
        }

        if (missing.Count > 0)
        {
            return new VmDetection(VmVerdict.Unknown, null,
                $"the {string.Join(" and ", missing)} could not be read, so a virtual machine cannot be ruled out (unknown is not \"none\")");
        }

        var flag = strings.SmbiosVirtualMachineFlag is null ? "not readable" : "not set";
        var hypervisor = strings.HypervisorPresent switch
        {
            true => "yes (this alone is not a virtual machine: a physical host that runs virtualization-based security reports it too)",
            false => "no",
            null => "unknown",
        };
        return new VmDetection(VmVerdict.NotVirtual, null,
            $"an inference, not proof: no catalogued marker matched the system, board and BIOS strings that were read (a hypervisor missing from the catalog, or firmware strings that are spoofed, would pass too); SMBIOS virtual-machine flag: {flag}; hypervisor present: {hypervisor}");
    }

    private sealed record Hit(string Field, string Marker);

    private static Hit? Match(VmMarkerEntry entry, FirmwareStrings strings) =>
        FirstMatch(entry.ManufacturerMarkers, ("system manufacturer", strings.SystemManufacturer), ("board manufacturer", strings.BaseBoardManufacturer))
        ?? FirstMatch(entry.ModelMarkers, ("system model", strings.SystemModel), ("board product", strings.BaseBoardProduct))
        ?? FirstMatch(entry.BiosMarkers, ("BIOS vendor", strings.BiosVendor), ("BIOS version", strings.BiosVersion));

    private static Hit? FirstMatch(IReadOnlyList<string> markers, params (string Field, string? Value)[] fields)
    {
        foreach (var marker in markers)
        {
            if (string.IsNullOrWhiteSpace(marker))
            {
                continue;
            }

            var needle = marker.Trim();
            foreach (var (field, value) in fields)
            {
                // Ordinal on purpose: a culture-sensitive comparison would make "i" and "I" differ under tr-TR and az-Latn-AZ.
                if (!string.IsNullOrWhiteSpace(value) && value.Contains(needle, StringComparison.OrdinalIgnoreCase))
                {
                    return new Hit(field, needle);
                }
            }
        }

        return null;
    }
}
