using System.Runtime.InteropServices;

namespace Jankscope.Platform;

/// <summary>
/// Documented read-only Win32 entry points for hardware discovery (CPU topology and the display configuration). Nothing here touches
/// another process, changes a setting or loads a driver. The DLL search path is restricted to System32 by the assembly attribute in
/// AssemblyInfo.cs.
/// </summary>
internal static partial class NativeHardware
{
    internal const int RelationAll = 0xFFFF;
    internal const ushort AllProcessorGroups = 0xFFFF;
    internal const uint QdcOnlyActivePaths = 0x2;
    internal const int ErrorInsufficientBuffer = 122;

    /// <summary>Documented (sysinfoapi.h). Fills a buffer of variable-sized SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX records.</summary>
    [LibraryImport("kernel32.dll", EntryPoint = "GetLogicalProcessorInformationEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetLogicalProcessorInformationEx(int relationshipType, Span<byte> buffer, ref uint returnedLength);

    /// <summary>Documented (processthreadsapi.h). The number of active logical processors in a group, or in all groups for 0xFFFF.</summary>
    [LibraryImport("kernel32.dll", EntryPoint = "GetActiveProcessorCount")]
    internal static partial uint GetActiveProcessorCount(ushort groupNumber);

    /// <summary>Documented connecting and configuring displays (CCD) API (wingdi.h, user32). Read-only.</summary>
    [LibraryImport("user32.dll", EntryPoint = "GetDisplayConfigBufferSizes")]
    internal static partial int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [LibraryImport("user32.dll", EntryPoint = "QueryDisplayConfig")]
    internal static partial int QueryDisplayConfig(
        uint flags, ref uint numPathArrayElements, Span<byte> pathInfoArray, ref uint numModeInfoArrayElements, Span<byte> modeInfoArray, nint currentTopologyId);

    /// <summary>
    /// Documented (dxgi.h). Called here, not through the binding package, because the package imports it without restricting the library search
    /// path to System32; this import does (AssemblyInfo.cs), and the binding package then only wraps the interface pointer this returns.
    /// </summary>
    [LibraryImport("dxgi.dll", EntryPoint = "CreateDXGIFactory1")]
    internal static partial int CreateDxgiFactory1(in Guid riid, out nint factory);

    /// <summary>Documented (wingdi.h). The request packet starts with a DISPLAYCONFIG_DEVICE_INFO_HEADER; only the advanced color query is used.</summary>
    [LibraryImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    internal static partial int DisplayConfigGetDeviceInfo(Span<byte> requestPacket);
}
