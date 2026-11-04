using System.Management;
using System.Runtime.InteropServices;
using Jankscope.Core.Hardware;

namespace Jankscope.Platform.Hardware;

/// <summary>
/// Reads SMBIOS-derived strings through documented WMI classes, read-only queries only: Win32_ComputerSystem (Manufacturer, Model,
/// HypervisorPresent), Win32_BaseBoard (Manufacturer, Product) and Win32_BIOS (Manufacturer, SMBIOSBIOSVersion, BiosCharacteristics).
/// A class that cannot be queried leaves its strings null; nothing is guessed.
/// </summary>
internal sealed class WmiFirmwareStringSource : IFirmwareStringSource
{
    /// <summary>Win32_BIOS.BiosCharacteristics value 44: "The SMBIOS table describes a virtual machine" (SMBIOS BIOS characteristics extension byte 2, bit 4).</summary>
    internal const ushort BiosCharacteristicVirtualMachine = 44;

    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(5);

    public FirmwareReading Read()
    {
        var system = QueryFirst("SELECT Manufacturer, Model, HypervisorPresent FROM Win32_ComputerSystem");
        var board = QueryFirst("SELECT Manufacturer, Product FROM Win32_BaseBoard");
        var bios = QueryFirst("SELECT Manufacturer, SMBIOSBIOSVersion, BiosCharacteristics FROM Win32_BIOS");

        var strings = new FirmwareStrings(
            Text(system, "Manufacturer"),
            Text(system, "Model"),
            Text(bios, "Manufacturer"),
            Text(bios, "SMBIOSBIOSVersion"),
            Text(board, "Manufacturer"),
            Text(board, "Product"),
            VirtualMachineFlag(bios),
            system?.TryGet("HypervisorPresent") as bool?);

        return system is null && board is null && bios is null
            ? new FirmwareReading(strings, "none of Win32_ComputerSystem, Win32_BaseBoard and Win32_BIOS could be queried")
            : new FirmwareReading(strings, null);
    }

    private static string? Text(Row? row, string name) => row?.TryGet(name) as string;

    private static bool? VirtualMachineFlag(Row? bios) => VirtualMachineFlagFrom(bios?.TryGet("BiosCharacteristics"));

    /// <summary>True when the characteristics list holds the documented "SMBIOS describes a virtual machine" value (44, not 43 which is "UEFI supported"); null when the list could not be read.</summary>
    internal static bool? VirtualMachineFlagFrom(object? biosCharacteristics) => biosCharacteristics switch
    {
        ushort[] values => values.Contains(BiosCharacteristicVirtualMachine),
        _ => null,
    };

    private sealed class Row(Dictionary<string, object?> values)
    {
        public object? TryGet(string name) => values.TryGetValue(name, out var v) ? v : null;
    }

    private static Row? QueryFirst(string query)
    {
        try
        {
            var options = new System.Management.EnumerationOptions { Timeout = QueryTimeout };
            using var searcher = new ManagementObjectSearcher(new ManagementScope(@"\\.\root\cimv2"), new ObjectQuery(query), options);
            foreach (var obj in searcher.Get())
            {
                using (obj)
                {
                    var values = new Dictionary<string, object?>(StringComparer.Ordinal);
                    foreach (var property in obj.Properties)
                    {
                        values[property.Name] = property.Value;
                    }

                    return new Row(values);
                }
            }

            return null;
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or COMException or InvalidOperationException)
        {
            return null;
        }
    }
}
