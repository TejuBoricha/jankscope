using System.Globalization;
using Jankscope.Core.Hardware;

namespace Jankscope.Platform.Hardware;

/// <summary>
/// Reads the active display paths with the documented connecting and configuring displays (CCD) API:
/// GetDisplayConfigBufferSizes, QueryDisplayConfig (active paths only; retried when the topology changes between the two calls), and
/// DisplayConfigGetDeviceInfo for the advanced color state of each target. The path's target adapter is the adapter the monitor is
/// attached to, which is how "which adapter drives each output" is known without enumerating outputs through DXGI.
/// </summary>
internal sealed class DisplayConfigSource : IDisplayConfigSource
{
    private const int ModeInfoSize = 64; // DISPLAYCONFIG_MODE_INFO
    private const int MaxAttempts = 4;
    private const uint MaxPlausibleElements = 4096;
    private const int NoError = 0;

    public DisplayConfigReading Read()
    {
        var advanced = new Dictionary<(Luid, uint), AdvancedColorState>();
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var sizes = NativeHardware.GetDisplayConfigBufferSizes(NativeHardware.QdcOnlyActivePaths, out var pathCount, out var modeCount);
            if (sizes != NoError)
            {
                return Failed(string.Create(CultureInfo.InvariantCulture, $"GetDisplayConfigBufferSizes failed with Win32 error {sizes}"));
            }

            if (pathCount == 0)
            {
                return new DisplayConfigReading([], advanced, [], null);
            }

            if (pathCount > MaxPlausibleElements || modeCount > MaxPlausibleElements)
            {
                return Failed(string.Create(CultureInfo.InvariantCulture, $"QueryDisplayConfig announced an implausible {pathCount} paths and {modeCount} modes"));
            }

            var paths = new byte[checked((int)pathCount * DisplayConfigParser.PathInfoSize)];
            var modes = new byte[checked((int)modeCount * ModeInfoSize)];
            var query = NativeHardware.QueryDisplayConfig(NativeHardware.QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, 0);
            if (query == NativeHardware.ErrorInsufficientBuffer)
            {
                continue; // the topology changed between the two calls
            }

            if (query != NoError)
            {
                return Failed(string.Create(CultureInfo.InvariantCulture, $"QueryDisplayConfig failed with Win32 error {query}"));
            }

            var parsed = DisplayConfigParser.ParsePaths(paths, (int)pathCount);
            foreach (var path in parsed.Paths.Where(p => p.IsActive))
            {
                advanced[(path.TargetAdapter, path.TargetId)] = ReadAdvancedColor(path);
            }

            return new DisplayConfigReading(parsed.Paths, advanced, parsed.Problems, null);
        }

        return Failed("the display configuration kept changing while it was being read");
    }

    private static AdvancedColorState ReadAdvancedColor(DisplayPath path)
    {
        Span<byte> packet = stackalloc byte[DisplayConfigParser.AdvancedColorInfoSize];
        DisplayConfigParser.WriteAdvancedColorRequest(packet, path.TargetAdapter, path.TargetId);
        return NativeHardware.DisplayConfigGetDeviceInfo(packet) == NoError
            ? DisplayConfigParser.ParseAdvancedColor(packet)
            : AdvancedColorState.Unknown;
    }

    private static DisplayConfigReading Failed(string why) =>
        new([], new Dictionary<(Luid, uint), AdvancedColorState>(), [], why);
}
