using System.Diagnostics;
using System.Runtime.InteropServices;
using Jankscope.Core;

namespace Jankscope.Platform;

public enum ServiceInstallation
{
    NotInstalled,
    Installed,
    /// <summary>The service could not be queried (access denied, SCM unavailable). This is not the same as not installed.</summary>
    Unknown,
}

public enum ServiceRunState
{
    /// <summary>Any state other than stopped: running, starting, stopping, paused. A loaded driver counts.</summary>
    Running,
    Stopped,
    Unknown,
}

/// <summary>
/// Actual state of one Windows service or driver. <see cref="StartType"/> is configuration (0 boot, 1 system, 2 automatic,
/// 3 demand, 4 disabled), not residency: a demand-start service can be running and an automatic one can be stopped (review #3).
/// </summary>
public sealed record ServiceSnapshot(ServiceInstallation Installation, ServiceRunState RunState, int? StartType)
{
    public static ServiceSnapshot NotInstalled { get; } = new(ServiceInstallation.NotInstalled, ServiceRunState.Stopped, null);

    public static ServiceSnapshot Unknown { get; } = new(ServiceInstallation.Unknown, ServiceRunState.Unknown, null);

    public string DescribeStartType() => StartType switch
    {
        0 => "boot-start",
        1 => "system-start",
        2 => "auto-start",
        3 => "demand-start",
        4 => "disabled",
        null => "start type unknown",
        var other => $"start type {other}",
    };
}

/// <summary>Seam for the service state source, so residency logic is testable without installing anything.</summary>
public interface IServiceStateReader
{
    ServiceSnapshot Read(string serviceName);
}

/// <summary>Seam for the running-process name snapshot. Null means the snapshot could not be taken; that is unknown, not empty.</summary>
public interface IProcessNameSource
{
    IReadOnlySet<string>? GetNames();
}

/// <summary>The four Service Control Manager calls the reader makes, so a test can see exactly which access rights it asks for.</summary>
internal interface IScmNative
{
    /// <summary>Returns 0 on failure.</summary>
    nint OpenManager(uint access);

    /// <summary>Returns 0 on failure and the Win32 error in <paramref name="error"/>.</summary>
    nint OpenService(nint manager, string name, uint access, out uint error);

    bool QueryState(nint service, out uint currentState);

    void Close(nint handle);
}

internal sealed class WindowsScmNative : IScmNative
{
    private const int ServiceStatusProcessSize = 36;

    public nint OpenManager(uint access) => Native.OpenSCManager(null, null, access);

    public nint OpenService(nint manager, string name, uint access, out uint error)
    {
        var handle = Native.OpenService(manager, name, access);
        error = handle == 0 ? (uint)Marshal.GetLastPInvokeError() : 0; // read immediately after the call
        return handle;
    }

    public bool QueryState(nint service, out uint currentState)
    {
        var ok = Native.QueryServiceStatusEx(service, Native.ScStatusProcessInfo, out var status, ServiceStatusProcessSize, out _);
        currentState = ok ? status.CurrentState : 0;
        return ok;
    }

    public void Close(nint handle) => _ = Native.CloseServiceHandle(handle);
}

/// <summary>
/// Reads service state through the Service Control Manager with SC_MANAGER_CONNECT and SERVICE_QUERY_STATUS only. These are
/// service objects, not process handles; nothing about another process is opened (HC-1).
/// </summary>
public sealed class ScmServiceStateReader : IServiceStateReader
{
    private const uint ErrorServiceDoesNotExist = 1060;
    private const uint ErrorInvalidName = 123;

    private readonly IScmNative _native;

    public ScmServiceStateReader()
        : this(new WindowsScmNative())
    {
    }

    internal ScmServiceStateReader(IScmNative native) => _native = native;

    public ServiceSnapshot Read(string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName) || serviceName.Length > 256 || serviceName.AsSpan().IndexOfAny('\\', '/') >= 0)
        {
            return ServiceSnapshot.Unknown;
        }

        var scm = _native.OpenManager(Native.ScManagerConnect);
        if (scm == 0)
        {
            return ServiceSnapshot.Unknown;
        }

        try
        {
            var service = _native.OpenService(scm, serviceName, Native.ServiceQueryStatus, out var error);
            if (service == 0)
            {
                return MapOpenServiceError(error);
            }

            try
            {
                var startType = Reg.ReadInt($@"SYSTEM\CurrentControlSet\Services\{serviceName}", "Start").ValueOrNull;
                var ok = _native.QueryState(service, out var state);
                return MapQueryResult(ok, state, startType);
            }
            finally
            {
                _native.Close(service);
            }
        }
        finally
        {
            _native.Close(scm);
        }
    }

    /// <summary>Only "no such service" (1060) and "invalid name" (123) mean not installed; access denied and everything else is Unknown.</summary>
    internal static ServiceSnapshot MapOpenServiceError(uint error) =>
        error is ErrorServiceDoesNotExist or ErrorInvalidName ? ServiceSnapshot.NotInstalled : ServiceSnapshot.Unknown;

    /// <summary>A failed status query on an installed service leaves its run state Unknown, never Stopped.</summary>
    internal static ServiceSnapshot MapQueryResult(bool queried, uint currentState, int? startType) =>
        new(ServiceInstallation.Installed, queried ? MapCurrentState(currentState) : ServiceRunState.Unknown, startType);

    /// <summary>SERVICE_STOPPED = 1; start/stop/continue/pause pending, running and paused (2-7) all mean the image is active or about to be.</summary>
    internal static ServiceRunState MapCurrentState(uint currentState) => currentState switch
    {
        1 => ServiceRunState.Stopped,
        >= 2 and <= 7 => ServiceRunState.Running,
        _ => ServiceRunState.Unknown,
    };
}

// The Process type is banned product-wide (BannedSymbols.txt). This is the single reviewed exception, and RepoLintTests pins this
// file as the only one allowed to disable RS0030: a name-only snapshot of the running processes. Process.ProcessName is taken
// from the snapshot; none of the handle-opening members (MainModule, StartTime, Handle, ...) is used (HC-1, LIMITATIONS L8).
#pragma warning disable RS0030 // reviewed exception: name-only process snapshot (HC-1), see comment above
/// <summary>Process names and liveness from the system snapshot (<c>Process.GetProcesses</c>); names and IDs only, no handle is opened by this code.</summary>
public sealed class SystemProcessNameSource : IProcessNameSource, IProcessLiveness
{
    public IReadOnlySet<string>? GetNames()
    {
        try
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    names.Add(ProcessNames.Normalize(process.ProcessName));
                }
            }

            return names;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return null;
        }
    }

    public bool IsAlive(int processId)
    {
        if (processId <= 0)
        {
            return false;
        }

        try
        {
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    if (process.Id == processId)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return false;
        }
    }
}
#pragma warning restore RS0030

public static class ProcessNames
{
    public static string Normalize(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
}
