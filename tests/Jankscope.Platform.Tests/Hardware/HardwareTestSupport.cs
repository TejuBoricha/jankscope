using Jankscope.Core;
using Jankscope.Core.Hardware;
using Jankscope.Platform.Hardware;

namespace Jankscope.Platform.Tests.Hardware;

internal static class HardwareRepo
{
    public static string Root { get; } = FindRoot();

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Jankscope.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Repository root (Jankscope.slnx) not found above " + AppContext.BaseDirectory);
    }

    public static byte[] ObservedProcessorBytes()
    {
        var path = Path.Combine(Root, "tests", "Jankscope.Core.Tests", "Hardware", "Fixtures", "lpie-relationall.observed-6p-smt-4e.hex.txt");
        var hex = string.Concat(File.ReadAllLines(path).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')));
        return Convert.FromHexString(hex);
    }
}

internal sealed class FakeProcessorSource(ProcessorInfoReading reading) : IProcessorInfoSource
{
    public ProcessorInfoReading Read() => reading;
}

internal sealed class FakeAdapterSource(AdapterEnumeration result) : IGpuAdapterSource
{
    public AdapterEnumeration Enumerate() => result;
}

internal sealed class FakeDisplaySource(DisplayConfigReading result) : IDisplayConfigSource
{
    public DisplayConfigReading Read() => result;
}

internal sealed class FakeFirmwareSource(FirmwareReading result) : IFirmwareStringSource
{
    public FirmwareReading Read() => result;
}

internal static class Fakes
{
    public static readonly Luid Igpu = new(0, 0x11B0C);
    public static readonly Luid Dgpu = new(0, 0x11FD6);
    public static readonly Luid Soft = new(0, 0x11FA1);

    public static GpuAdapterInfo Adapter(Luid luid, string description, uint vendor = 0x1234, bool software = false) =>
        new(luid, description, vendor, 0x5678, 0, 1, software ? 0UL : 4UL << 30, 0, 8UL << 30, software, false);

    public static DisplayPath Path(Luid adapter, uint target, uint num, uint den, uint tech = 5) =>
        new(adapter, 0, adapter, target, tech, new RationalHz(num, den), true, true);

    public static DisplayConfigReading Displays(params DisplayPath[] paths) =>
        new(paths, new Dictionary<(Luid, uint), AdvancedColorState>(), [], null);

    public static DisplayConfigReading DisplayFailure(string why) =>
        new([], new Dictionary<(Luid, uint), AdvancedColorState>(), [], why);

    public static FirmwareStrings Strings(string? manufacturer, string? model, bool? smbiosVm = false, bool? hypervisor = false) =>
        new(manufacturer, model, "Some BIOS Corp", "1.0", manufacturer, "Board", smbiosVm, hypervisor);

    public static readonly VmMarkerEntry InventedGuest =
        new("invented", "Invented Hypervisor", ["InventedSoft"], ["Invented Virtual"], [], "community");
}
