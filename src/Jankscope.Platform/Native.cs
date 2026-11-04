using System.Runtime.InteropServices;
using Jankscope.Core;

namespace Jankscope.Platform;

[StructLayout(LayoutKind.Sequential)]
internal struct SystemPowerStatus
{
    public byte AcLineStatus;
    public byte BatteryFlag;
    public byte BatteryLifePercent;
    public byte SystemStatusFlag;
    public uint BatteryLifeTime;
    public uint BatteryFullLifeTime;
}

/// <summary>SERVICE_STATUS_PROCESS (winsvc.h). Only <see cref="CurrentState"/> is read; the process id is deliberately ignored.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ServiceStatusProcess
{
    public uint ServiceType;
    public uint CurrentState;
    public uint ControlsAccepted;
    public uint Win32ExitCode;
    public uint ServiceSpecificExitCode;
    public uint CheckPoint;
    public uint WaitHint;
    public uint ProcessId;
    public uint ServiceFlags;
}

/// <summary>Documented Win32 entry points used by the capability probes. Nothing here touches another process.</summary>
internal static partial class Native
{
    internal const uint ScManagerConnect = 0x0001;
    internal const uint ServiceQueryStatus = 0x0004;
    internal const int ScStatusProcessInfo = 0;

    [LibraryImport("kernel32.dll", EntryPoint = "GetSystemPowerStatus")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetSystemPowerStatus(out SystemPowerStatus status);

    [LibraryImport("netapi32.dll", EntryPoint = "NetGetJoinInformation", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint NetGetJoinInformation(string? server, out nint nameBuffer, out int joinStatus);

    [LibraryImport("netapi32.dll", EntryPoint = "NetApiBufferFree")]
    internal static partial uint NetApiBufferFree(nint buffer);

    [LibraryImport("netapi32.dll", EntryPoint = "NetGetAadJoinInformation", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int NetGetAadJoinInformation(string? tenantId, out nint joinInfo);

    [LibraryImport("netapi32.dll", EntryPoint = "NetFreeAadJoinInformation")]
    internal static partial void NetFreeAadJoinInformation(nint joinInfo);

    /// <summary>Documented (mdmregistration.h). The UPN buffer is required by the API but never read or stored.</summary>
    [LibraryImport("mdmregistration.dll", EntryPoint = "IsDeviceRegisteredWithManagement")]
    internal static partial int IsDeviceRegisteredWithManagement(out int isRegistered, uint upnBufferChars, nint upnBuffer);

    [LibraryImport("advapi32.dll", EntryPoint = "StartTraceW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint StartTrace(out ulong sessionHandle, string sessionName, nint properties);

    [LibraryImport("advapi32.dll", EntryPoint = "ControlTraceW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint ControlTrace(ulong sessionHandle, string? sessionName, nint properties, uint controlCode);

    [LibraryImport("advapi32.dll", EntryPoint = "QueryAllTracesW")]
    internal static partial uint QueryAllTraces(Span<nint> propertyArray, uint propertyArrayCount, out uint loggerCount);

    /// <summary>Service Control Manager connection with SC_MANAGER_CONNECT only (no enumerate, no create).</summary>
    [LibraryImport("advapi32.dll", EntryPoint = "OpenSCManagerW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

    /// <summary>Opens a service for SERVICE_QUERY_STATUS only. This is a service object, never a process handle.</summary>
    [LibraryImport("advapi32.dll", EntryPoint = "OpenServiceW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint OpenService(nint scManager, string serviceName, uint desiredAccess);

    [LibraryImport("advapi32.dll", EntryPoint = "QueryServiceStatusEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool QueryServiceStatusEx(nint service, int infoLevel, out ServiceStatusProcess buffer, uint bufferSize, out uint bytesNeeded);

    [LibraryImport("advapi32.dll", EntryPoint = "CloseServiceHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseServiceHandle(nint handle);
}

/// <summary>
/// Minimal ETW session management through documented advapi32 APIs. EVENT_TRACE_PROPERTIES is laid out by hand
/// (x64/ARM64: 120 bytes, 8-byte HANDLE) so no struct marshalling is involved. The hand-laid offsets are valid only
/// for a 64-bit process; a 32-bit process reports <see cref="ErrorNotSupported"/> instead of guessing (review #8).
/// </summary>
internal static unsafe class EtwNative
{
    public const uint ErrorSuccess = 0;
    public const uint ErrorAccessDenied = 5;
    public const uint ErrorNotSupported = 50;
    public const uint ErrorAlreadyExists = 183;
    public const uint ErrorMoreData = 234;

    private const int PropertiesSize = 120;
    private const int MaxNameChars = 1024;
    private const int QueryBufferBytes = PropertiesSize + (MaxNameChars + MaxNameChars) * sizeof(char);
    private const int MaxRetryCapacity = 1024;

    private const int OffsetWnodeBufferSize = 0;
    private const int OffsetWnodeGuid = 24;
    private const int OffsetWnodeClientContext = 40;
    private const int OffsetWnodeFlags = 44;
    private const int OffsetLogFileMode = 64;
    private const int OffsetLogFileNameOffset = 112;
    private const int OffsetLoggerNameOffset = 116;

    private const int WnodeFlagTracedGuid = 0x00020000;
    private const int RealTimeMode = 0x00000100;
    private const int QpcClockType = 1;
    private const uint ControlStop = 1;

    /// <summary>True when the hand-laid EVENT_TRACE_PROPERTIES offsets are valid for this process (8-byte HANDLE).</summary>
    public static bool LayoutSupported => nint.Size == 8;

    /// <summary>
    /// Lists the names of all running ETW sessions. Status is the Win32 error (0 = success). A session whose name cannot
    /// be read is still counted and returned as an empty string: it can never be mistaken for one of ours.
    /// </summary>
    public static (uint Status, IReadOnlyList<string> Names) QueryAllSessionNames(int initialCapacity = EtwSessionClassifier.SystemSessionLimit)
    {
        if (!LayoutSupported)
        {
            return (ErrorNotSupported, []);
        }

        var capacity = Math.Clamp(initialCapacity, 1, MaxRetryCapacity);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var (status, names, needed) = QueryOnce(capacity);
            if (status != ErrorMoreData)
            {
                return (status, names);
            }

            if (needed <= capacity || needed > MaxRetryCapacity)
            {
                return (status, []);
            }

            capacity = (int)needed;
        }

        return (ErrorMoreData, []);
    }

    private static (uint Status, IReadOnlyList<string> Names, uint Needed) QueryOnce(int capacity)
    {
        // One zeroed block holds every EVENT_TRACE_PROPERTIES record back to back.
        var block = (byte*)NativeMemory.AllocZeroed((nuint)capacity * QueryBufferBytes);
        try
        {
            var pointers = new nint[capacity];
            for (var i = 0; i < capacity; i++)
            {
                var record = block + (long)i * QueryBufferBytes;
                *(int*)(record + OffsetWnodeBufferSize) = QueryBufferBytes;
                pointers[i] = (nint)record;
            }

            var status = Native.QueryAllTraces(pointers, (uint)capacity, out var count);
            if (status != ErrorSuccess)
            {
                return (status, [], count);
            }

            var names = new List<string>((int)Math.Min(count, (uint)capacity));
            for (var i = 0; i < count && i < capacity; i++)
            {
                var record = block + (long)i * QueryBufferBytes;
                var offset = *(int*)(record + OffsetLoggerNameOffset);
                var valid = offset >= PropertiesSize && offset <= QueryBufferBytes - sizeof(char);
                names.Add(valid ? ReadName(record + offset, (QueryBufferBytes - offset) / sizeof(char)) : string.Empty);
            }

            return (ErrorSuccess, names, count);
        }
        finally
        {
            NativeMemory.Free(block);
        }
    }

    /// <summary>
    /// Reads a NUL-terminated name inside its slot, never past it. A slot without a terminator is reported as an empty (unreadable) name
    /// instead of running into the next field or off the end of the allocation (review pass 2 #14).
    /// </summary>
    private static string ReadName(byte* start, int maxChars) => DecodeName(new ReadOnlySpan<char>(start, maxChars));

    /// <summary>Pure core of <see cref="ReadName"/>, so the bound can be tested without unsafe code.</summary>
    internal static string DecodeName(ReadOnlySpan<char> slot)
    {
        var end = slot.IndexOf('\0');
        return end < 0 ? string.Empty : new string(slot[..end]);
    }

    /// <summary>
    /// Starts a uniquely named real-time session and stops it immediately: this is how access to unelevated frame
    /// capture is really detected (the app never edits group membership). Returns the start status and whether
    /// cleanup succeeded; a session we cannot stop is reported, never silently leaked.
    /// </summary>
    /// <param name="whileRunning">
    /// Optional test hook, called with the session name while the session is live (before it is stopped); the session is stopped even if
    /// it throws. It gives tests a positive control: our own session must be visible by exact name while it runs.
    /// </param>
    public static (uint StartStatus, bool CleanedUp, string SessionName) TryOpenAndCloseRealTimeSession(Action<string>? whileRunning = null)
    {
        var name = $"{Branding.EtwSessionPrefix}probe-{Environment.ProcessId}-{Guid.NewGuid().ToString("N")[..8]}";
        if (!LayoutSupported)
        {
            return (ErrorNotSupported, true, name);
        }

        var total = PropertiesSize + (name.Length + 1) * sizeof(char);
        var props = (byte*)NativeMemory.AllocZeroed((nuint)total);
        try
        {
            *(int*)(props + OffsetWnodeBufferSize) = total;
            Guid.NewGuid().TryWriteBytes(new Span<byte>(props + OffsetWnodeGuid, 16));
            *(int*)(props + OffsetWnodeClientContext) = QpcClockType;
            *(int*)(props + OffsetWnodeFlags) = WnodeFlagTracedGuid;
            *(int*)(props + OffsetLogFileMode) = RealTimeMode;
            *(int*)(props + OffsetLogFileNameOffset) = 0;
            *(int*)(props + OffsetLoggerNameOffset) = PropertiesSize;
            name.AsSpan().CopyTo(new Span<char>(props + PropertiesSize, name.Length));

            var start = Native.StartTrace(out var handle, name, (nint)props);
            if (start != ErrorSuccess)
            {
                return (start, true, name);
            }

            uint stop;
            try
            {
                whileRunning?.Invoke(name);
            }
            finally
            {
                stop = Native.ControlTrace(handle, name, (nint)props, ControlStop);
                if (stop != ErrorSuccess)
                {
                    stop = Native.ControlTrace(0, name, (nint)props, ControlStop);
                }
            }

            return (ErrorSuccess, stop == ErrorSuccess, name);
        }
        finally
        {
            NativeMemory.Free(props);
        }
    }

    /// <summary>
    /// Stops an active ETW session by name. Only sessions starting with <see cref="Branding.EtwSessionPrefix"/> are ever stopped.
    /// </summary>
    public static uint TryStopSessionByName(string sessionName)
    {
        if (string.IsNullOrEmpty(sessionName) || !sessionName.StartsWith(Branding.EtwSessionPrefix, StringComparison.Ordinal))
        {
            return ErrorAccessDenied;
        }

        if (!LayoutSupported)
        {
            return ErrorNotSupported;
        }

        var total = PropertiesSize + (sessionName.Length + 1) * sizeof(char);
        var props = (byte*)NativeMemory.AllocZeroed((nuint)total);
        try
        {
            *(int*)(props + OffsetWnodeBufferSize) = total;
            *(int*)(props + OffsetLogFileNameOffset) = 0;
            *(int*)(props + OffsetLoggerNameOffset) = PropertiesSize;
            sessionName.AsSpan().CopyTo(new Span<char>(props + PropertiesSize, sessionName.Length));

            return Native.ControlTrace(0, sessionName, (nint)props, ControlStop);
        }
        finally
        {
            NativeMemory.Free(props);
        }
    }
}
