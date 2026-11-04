using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Claims;
using System.Security.Principal;
using Jankscope.Core;
using Microsoft.Win32;

namespace Jankscope.Platform;

/// <summary>Outcome of a registry read. Absent and unreadable are different facts and neither is ever "off" (review #12).</summary>
internal enum RegState
{
    Present,
    Absent,
    Unreadable,
}

/// <summary>A DWORD registry value with its read outcome. <see cref="Value"/> is meaningful only when <see cref="State"/> is Present.</summary>
internal readonly record struct RegInt(RegState State, int Value)
{
    public static RegInt Of(int value) => new(RegState.Present, value);

    public static RegInt Absent => new(RegState.Absent, 0);

    public static RegInt Unreadable => new(RegState.Unreadable, 0);

    public int? ValueOrNull => State == RegState.Present ? Value : null;

    /// <summary>Human-readable reason this value cannot be interpreted (used in "unknown" rows).</summary>
    public string Why(string subject) => State switch
    {
        RegState.Absent => $"{subject} not present",
        RegState.Unreadable => $"{subject} unreadable or not a DWORD",
        _ => $"{subject} = {Value} is not a recognized value",
    };
}

internal static class Reg
{
    public static RegInt ReadInt(string subKey, string name)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(subKey);
            return key?.GetValue(name) switch
            {
                null => RegInt.Absent,
                int i => RegInt.Of(i),
                _ => RegInt.Unreadable,
            };
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return RegInt.Unreadable;
        }
    }

    public static string? GetString(string subKey, string name)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(subKey);
            return key?.GetValue(name) as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    public static string[]? GetSubKeyNames(string subKey)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(subKey);
            return key?.GetSubKeyNames();
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}

internal enum SModeState
{
    Off,
    On,
    Unknown,
}

/// <summary>
/// S mode indicator shared by the OS and managed-device probes. <c>SkuPolicyRequired</c> is documented as an unattend
/// setting for Windows 10 and the runtime API is <c>WindowsIntegrityPolicy</c>; verify on a real S-mode device before relying on
/// this value (FACTS). Absent, unreadable or unexpected values are Unknown, never "off".
/// </summary>
internal static class SModePolicy
{
    public const string Key = @"SYSTEM\CurrentControlSet\Control\CI\Policy";
    public const string ValueName = "SkuPolicyRequired";
    public const string Source = "HKLM CI Policy (informally documented)";

    public static RegInt Read() => Reg.ReadInt(Key, ValueName);

    public static SModeState Interpret(RegInt value) => value.State != RegState.Present
        ? SModeState.Unknown
        : value.Value switch
        {
            0 => SModeState.Off,
            1 => SModeState.On,
            _ => SModeState.Unknown,
        };
}

/// <summary>Windows version, edition, support class, locale and architecture. Windows 11 is detected by build number, never by name.</summary>
public sealed class OsProbe : IProbe
{
    private const string CurrentVersionKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    public string Id => "os";

    public IReadOnlyList<CapabilityEntry> Run()
    {
        var version = Environment.OSVersion.Version;
        var ubr = Reg.ReadInt(CurrentVersionKey, "UBR").ValueOrNull;
        var edition = Reg.GetString(CurrentVersionKey, "EditionID") ?? "unknown";
        var installationType = Reg.GetString(CurrentVersionKey, "InstallationType") ?? "unknown";
        var family = version.Build >= 22000 ? "Windows 11" : "Windows 10";
        var build = ubr is null ? $"{version.Build}" : $"{version.Build}.{ubr}";

        var entries = new List<CapabilityEntry>
        {
            CapabilityEntry.Available("os.version", "Windows version", CapabilityTier.A,
                "Environment.OSVersion + HKLM CurrentVersion (informally documented)",
                $"{family} build {build}, edition {edition}, installation type {installationType}"),
            CapabilityEntry.Available("os.architecture", "OS and process architecture", CapabilityTier.A,
                "RuntimeInformation", $"OS {RuntimeInformation.OSArchitecture}, process {RuntimeInformation.ProcessArchitecture}"),
            CapabilityEntry.Available("os.locale", "Display language and region", CapabilityTier.A,
                "CultureInfo", $"UI {CultureInfo.CurrentUICulture.Name}, regional {CultureInfo.CurrentCulture.Name}", isSensitive: true),
        };

        entries.Add(Support(version.Build, Reg.GetString(CurrentVersionKey, "InstallationType"), SModePolicy.Interpret(SModePolicy.Read())));
        return entries;
    }

    /// <summary>
    /// Whether this Windows is in the supported matrix. An unreadable InstallationType is Unknown, not "client": a server must not be
    /// reported as the primary target just because a registry value could not be read (review pass 2 #13b).
    /// </summary>
    internal static CapabilityEntry Support(int build, string? installationType, SModeState sMode)
    {
        if (string.IsNullOrWhiteSpace(installationType))
        {
            return CapabilityEntry.Unavailable("os.support", "Supported Windows", CapabilityTier.A, Availability.Unknown,
                "HKLM CurrentVersion InstallationType", "InstallationType is unreadable, so client or server cannot be told apart");
        }

        if (!string.Equals(installationType, "Client", StringComparison.OrdinalIgnoreCase))
        {
            return CapabilityEntry.Unavailable("os.support", "Supported Windows", CapabilityTier.A, Availability.NotAvailable,
                "HKLM CurrentVersion InstallationType", $"Windows Server ({installationType}) is unsupported; the app explains instead of crashing.");
        }

        if (sMode == SModeState.On)
        {
            return CapabilityEntry.Unavailable("os.support", "Supported Windows", CapabilityTier.A, Availability.NotAvailable,
                "HKLM CI Policy SkuPolicyRequired", "Windows in S mode is unsupported.");
        }

        return build switch
        {
            >= 22000 => CapabilityEntry.Available("os.support", "Supported Windows", CapabilityTier.A, "build number", "Windows 11: primary target", DataProvenance.Inferred),
            >= 19045 => CapabilityEntry.Available("os.support", "Supported Windows", CapabilityTier.A, "build number",
                "Windows 10 22H2 or later: best-effort via runtime feature detection (out of mainstream support)", DataProvenance.Inferred),
            _ => CapabilityEntry.Unavailable("os.support", "Supported Windows", CapabilityTier.A, Availability.NotAvailable,
                "build number", $"Build {build} is below the supported matrix (Windows 10 22H2 = 19045)."),
        };
    }
}

/// <summary>Elevation and group membership. A non-elevated admin shell is reported as such: it is not a standard-user test.</summary>
public sealed class PrivilegeProbe : IProbe
{
    internal const string ElevatedPrefix = "elevated";
    internal const string NotElevatedPrefix = "not elevated";
    private const string AdministratorsSid = "S-1-5-32-544";
    private const string PerformanceLogUsersSid = "S-1-5-32-559";
    public string Id => "privilege";

    /// <summary>True only when the row says the token is elevated. Anything else (including a missing row) is not proof of an unelevated process.</summary>
    internal static bool? IsElevatedValue(string? elevationRowValue) => elevationRowValue switch
    {
        null => null,
        var v when v.StartsWith(NotElevatedPrefix, StringComparison.Ordinal) => false,
        var v when v.StartsWith(ElevatedPrefix, StringComparison.Ordinal) => true,
        _ => null,
    };

    internal static bool IsCurrentProcessElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public IReadOnlyList<CapabilityEntry> Run()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        var elevated = principal.IsInRole(WindowsBuiltInRole.Administrator);
        var filteredAdmin = !elevated && identity.Claims.Any(c =>
            c.Type == ClaimTypes.DenyOnlySid && string.Equals(c.Value, AdministratorsSid, StringComparison.Ordinal));

        var elevation = elevated ? $"{ElevatedPrefix} administrator"
            : filteredAdmin ? $"{NotElevatedPrefix} (filtered token of an administrator account; not a standard-user test)"
            : $"{NotElevatedPrefix} (standard user token)";

        var entries = new List<CapabilityEntry>
        {
            CapabilityEntry.Available("privilege.elevation", "Elevation state", CapabilityTier.A, "access token (documented)", elevation),
        };

        var inGroup = identity.Groups?.Any(g => g.Value == PerformanceLogUsersSid) == true;
        entries.Add(inGroup
            ? CapabilityEntry.Available("privilege.perf_log_users", "Performance Log Users membership", CapabilityTier.A,
                "access token groups", "token contains Performance Log Users (the real test is etw.realtime_session)")
            : CapabilityEntry.Unavailable("privilege.perf_log_users", "Performance Log Users membership", CapabilityTier.A,
                Availability.NeedsUserAction, "access token groups",
                "Not a member. Unelevated frame capture needs a one-time user action (INSTALL.md); the app never edits groups."));
        return entries;
    }
}

/// <summary>Read-only security posture (HC-8): reported, never changed, never recommended for disabling.</summary>
public sealed class SecurityPostureProbe : IProbe
{
    private const string SacSource = "HKLM CI Policy (informally documented)";
    public string Id => "security";

    public IReadOnlyList<CapabilityEntry> Run() =>
    [
        SmartAppControl(Reg.ReadInt(@"SYSTEM\CurrentControlSet\Control\CI\Policy", "VerifiedAndReputablePolicyState")),
        SecureBoot(Reg.ReadInt(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled")),
        MemoryIntegrity(Reg.ReadInt(@"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled")),
    ];

    // Values 1 and 2 for Smart App Control are unverified (FACTS); anything else is Unknown, never "off".
    internal static CapabilityEntry SmartAppControl(RegInt value) => value switch
    {
        { State: RegState.Present, Value: 0 } => CapabilityEntry.Available("security.smart_app_control", "Smart App Control", CapabilityTier.A, SacSource, "off"),
        { State: RegState.Present, Value: 1 } => CapabilityEntry.Available("security.smart_app_control", "Smart App Control", CapabilityTier.A, SacSource, "on: unsigned builds may be blocked"),
        { State: RegState.Present, Value: 2 } => CapabilityEntry.Available("security.smart_app_control", "Smart App Control", CapabilityTier.A, SacSource, "evaluation mode: unsigned builds may be blocked"),
        _ => CapabilityEntry.Unavailable("security.smart_app_control", "Smart App Control", CapabilityTier.A, Availability.Unknown, SacSource,
            $"{value.Why("VerifiedAndReputablePolicyState")}; state unknown (never reported as off)"),
    };

    internal static CapabilityEntry SecureBoot(RegInt value) => value switch
    {
        { State: RegState.Present, Value: 1 } => CapabilityEntry.Available("security.secure_boot", "Secure Boot", CapabilityTier.A, "HKLM SecureBoot State", "on"),
        { State: RegState.Present, Value: 0 } => CapabilityEntry.Available("security.secure_boot", "Secure Boot", CapabilityTier.A, "HKLM SecureBoot State", "off"),
        _ => CapabilityEntry.Unavailable("security.secure_boot", "Secure Boot", CapabilityTier.A, Availability.Unknown, "HKLM SecureBoot State",
            $"{value.Why("UEFISecureBootEnabled")} (legacy BIOS, unreadable or unexpected); state unknown (never reported as off)"),
    };

    internal static CapabilityEntry MemoryIntegrity(RegInt value) => value switch
    {
        { State: RegState.Present, Value: 1 } => CapabilityEntry.Available("security.memory_integrity", "Memory integrity (HVCI) policy", CapabilityTier.A,
            "HKLM DeviceGuard Scenarios", "configured on (running state not checked)"),
        { State: RegState.Present, Value: 0 } => CapabilityEntry.Available("security.memory_integrity", "Memory integrity (HVCI) policy", CapabilityTier.A,
            "HKLM DeviceGuard Scenarios", "configured off"),
        _ => CapabilityEntry.Unavailable("security.memory_integrity", "Memory integrity (HVCI) policy", CapabilityTier.A, Availability.Unknown,
            "HKLM DeviceGuard Scenarios", $"{value.Why("Enabled")}; state unknown (never reported as off)"),
    };
}

/// <summary>Form factor and power state. Battery logic applies only when a battery exists.</summary>
public sealed class HardwareProbe : IProbe
{
    private const byte BatteryFlagNoBattery = 128;
    private const byte BatteryFlagUnknown = 255;
    public string Id => "hardware";

    /// <summary>GetSystemPowerStatus BatteryFlag: 255 = unknown, 128 = no system battery, anything else = a battery exists. Null = unknown.</summary>
    internal static bool? BatteryPresence(bool powerStatusRead, byte batteryFlag)
    {
        if (!powerStatusRead || batteryFlag == BatteryFlagUnknown)
        {
            return null;
        }

        return (batteryFlag & BatteryFlagNoBattery) == 0;
    }

    public IReadOnlyList<CapabilityEntry> Run()
    {
        var entries = new List<CapabilityEntry>();
        var hasPower = Native.GetSystemPowerStatus(out var power);
        var battery = BatteryPresence(hasPower, power.BatteryFlag);

        entries.Add(FormFactor(ReadChassisTypes(), battery));

        if (battery == true)
        {
            var ac = power.AcLineStatus switch { 0 => "on battery", 1 => "on AC power", _ => "unknown" };
            entries.Add(CapabilityEntry.Available("hardware.power_source", "Power source", CapabilityTier.A, "GetSystemPowerStatus", ac));
        }
        else
        {
            entries.Add(CapabilityEntry.Unavailable("hardware.power_source", "Power source", CapabilityTier.A, Availability.NotAvailable,
                "GetSystemPowerStatus", battery == false ? "no battery: AC/DC logic does not apply" : "battery state unknown"));
        }

        return entries;
    }

    /// <summary>The form factor comes from the chassis type; a battery alone does not establish it, so an empty chassis list is Unknown.</summary>
    internal static CapabilityEntry FormFactor(IReadOnlyCollection<int> chassis, bool? battery)
    {
        var batteryText = battery is null ? "unknown" : battery.Value ? "present" : "absent";
        return chassis.Count == 0
            ? CapabilityEntry.Unavailable("hardware.form_factor", "Form factor", CapabilityTier.A, Availability.Unknown,
                "WMI Win32_SystemEnclosure + GetSystemPowerStatus", $"chassis type unreadable (battery {batteryText})")
            : CapabilityEntry.Available("hardware.form_factor", "Form factor", CapabilityTier.A,
                "WMI Win32_SystemEnclosure + GetSystemPowerStatus (documented)",
                $"{SmbiosChassis.Classify(chassis)}; chassis types [{string.Join(",", chassis)}]; battery {batteryText}");
    }

    private static List<int> ReadChassisTypes()
    {
        var types = new List<int>();
        try
        {
            var options = new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(5) };
            using var searcher = new ManagementObjectSearcher(new ManagementScope(@"\\.\root\cimv2"),
                new ObjectQuery("SELECT ChassisTypes FROM Win32_SystemEnclosure"), options);
            foreach (var obj in searcher.Get())
            {
                using (obj)
                {
                    if (obj["ChassisTypes"] is ushort[] values)
                    {
                        types.AddRange(values.Select(v => (int)v));
                    }
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or COMException or InvalidOperationException)
        {
            types.Clear();
        }

        return types;
    }
}

/// <summary>SMBIOS chassis type values (DMTF DSP0134 / Win32_SystemEnclosure) mapped to a form-factor class.</summary>
public static class SmbiosChassis
{
    public static string Classify(IReadOnlyCollection<int> types)
    {
        static string One(int t) => t switch
        {
            3 or 4 or 5 or 6 or 7 or 15 or 16 => "desktop",
            13 => "all-in-one",
            8 or 9 or 10 or 14 => "laptop",
            11 => "handheld",
            30 or 31 or 32 => "tablet or convertible",
            35 or 36 => "mini PC",
            17 or 23 or 25 or 28 => "server",
            _ => "other",
        };

        var classes = types.Select(One).Distinct().ToList();
        return classes.Count == 1 ? classes[0] : string.Join("/", classes);
    }
}

/// <summary>
/// The values the managed-device probes report. The optimize-mode gate (HC-11) compares against these constants, not against free text
/// or prefixes: a reworded value cannot silently turn a managed device into a clean one (review pass 2 #8).
/// </summary>
internal static class ManagedValues
{
    public const string DomainJoined = "domain-joined";
    public const string Workgroup = "workgroup";
    public const string NotJoined = "not joined";
    public const string MdmEnrolled = "enrolled (registered with a management service)";
    public const string MdmNone = "none";
    public const string SModeOn = "on";
    public const string SModeOff = "off";

    // Rows whose probes arrive in Stage 1a. The gate already insists on these exact forms, so a new probe must emit a known value.
    public const string VmNone = "not a virtual machine";
    public const string VmPrefix = "virtual machine: "; // followed by the hypervisor name
    public const string PoliciesNone = "none";
    public const string PoliciesApplied = "policies applied";
    public const string EntraNotJoined = "not joined";
    public const string EntraJoined = "joined";
    public const string GameNone = "no game session";
    public const string GameActivePrefix = "game session active: "; // followed by the title

    public static bool IsVirtualMachine(string v) => v.StartsWith(VmPrefix, StringComparison.Ordinal) && v.Length > VmPrefix.Length;

    public static bool IsKnownVirtualMachineValue(string v) => v == VmNone || IsVirtualMachine(v);

    public static bool IsKnownGameSessionValue(string v) => v == GameNone || (v.StartsWith(GameActivePrefix, StringComparison.Ordinal) && v.Length > GameActivePrefix.Length);

    /// <summary>The four states <c>AntiCheatProbe</c> can report as Available (Unknown is never Available). An active game session or protected title does not make the device ineligible: per-action rules (Stage 4) decide.</summary>
    public static bool IsKnownAntiCheatStateValue(string v) =>
        new[] { "Idle", "ResidentOnly", "ProtectedTitle", "GameSession" }.Any(s => v.StartsWith(s + ";", StringComparison.Ordinal));
}

/// <summary>Domain, MDM and S-mode indicators. Managed or unknown configurations default to Recommend-only (HC-11).</summary>
public sealed class ManagedDeviceProbe : IProbe
{
    private const int NetSetupDomainName = 3;
    private const int NetSetupWorkgroupName = 2;
    private const int NetSetupUnjoined = 1;
    public string Id => "managed";

    public IReadOnlyList<CapabilityEntry> Run() =>
    [
        DomainJoin(),
        MdmEnrollment(),
        SMode(SModePolicy.Read()),
        AppliedPolicies(),
        EntraJoin(),
    ];

    private static CapabilityEntry DomainJoin()
    {
        var status = Native.NetGetJoinInformation(null, out var buffer, out var join);
        if (buffer != 0)
        {
            _ = Native.NetApiBufferFree(buffer);
        }

        return DomainJoinEntry(status, join);
    }

    internal static CapabilityEntry DomainJoinEntry(uint status, int join)
    {
        if (status != 0)
        {
            return CapabilityEntry.Unavailable("managed.domain_join", "Domain membership", CapabilityTier.A, Availability.Unknown,
                "NetGetJoinInformation (documented)", $"query failed with Win32 error {status}");
        }

        var value = join switch
        {
            NetSetupDomainName => ManagedValues.DomainJoined,
            NetSetupWorkgroupName => ManagedValues.Workgroup,
            NetSetupUnjoined => ManagedValues.NotJoined,
            _ => null,
        };
        return value is null
            ? CapabilityEntry.Unavailable("managed.domain_join", "Domain membership", CapabilityTier.A, Availability.Unknown,
                "NetGetJoinInformation (documented)", $"unrecognized join status {join}")
            : CapabilityEntry.Available("managed.domain_join", "Domain membership", CapabilityTier.A, "NetGetJoinInformation (documented)", value);
    }

    private static CapabilityEntry MdmEnrollment()
    {
        // The HKLM Enrollments key is NOT a reliable signal: stock Windows ships built-in "Cloud/Local/Deploy Authority"
        // entries with EnrollmentState 1 on unmanaged PCs (found by testing on the reference bench). The documented
        // API answers the actual question.
        const uint UpnChars = 256;
        var upn = Marshal.AllocHGlobal((int)UpnChars * sizeof(char));
        try
        {
            var hr = Native.IsDeviceRegisteredWithManagement(out var registered, UpnChars, upn);
            return MdmEntry(hr, registered);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return CapabilityEntry.Unavailable("managed.mdm_enrollment", "MDM enrollment", CapabilityTier.A, Availability.Unknown,
                "IsDeviceRegisteredWithManagement (documented)", $"API not present on this Windows ({ex.GetType().Name}); state unknown");
        }
        finally
        {
            Marshal.FreeHGlobal(upn);
        }
    }

    internal static CapabilityEntry MdmEntry(int hresult, int registered) =>
        hresult != 0
            ? CapabilityEntry.Unavailable("managed.mdm_enrollment", "MDM enrollment", CapabilityTier.A, Availability.Unknown,
                "IsDeviceRegisteredWithManagement (documented)", $"query failed with HRESULT 0x{hresult:X8}; state unknown")
            : CapabilityEntry.Available("managed.mdm_enrollment", "MDM enrollment", CapabilityTier.A,
                "IsDeviceRegisteredWithManagement (documented)", registered != 0 ? ManagedValues.MdmEnrolled : ManagedValues.MdmNone);

    internal static CapabilityEntry SMode(RegInt value) => SModePolicy.Interpret(value) switch
    {
        SModeState.On => CapabilityEntry.Available("managed.s_mode", "S mode", CapabilityTier.A, SModePolicy.Source, ManagedValues.SModeOn),
        SModeState.Off => CapabilityEntry.Available("managed.s_mode", "S mode", CapabilityTier.A, SModePolicy.Source, ManagedValues.SModeOff),
        _ => CapabilityEntry.Unavailable("managed.s_mode", "S mode", CapabilityTier.A, Availability.Unknown, SModePolicy.Source,
            $"{value.Why("SkuPolicyRequired")}; state unknown (never reported as off)"),
    };

    private static CapabilityEntry EntraJoin()
    {
        try
        {
            var hr = Native.NetGetAadJoinInformation(null, out var joinInfo);
            if (hr == 0)
            {
                var isJoined = joinInfo != 0;
                if (isJoined)
                {
                    Native.NetFreeAadJoinInformation(joinInfo);
                }

                return EntraJoinEntry(0, isJoined);
            }

            // On Windows Home editions or client installs where AAD join is not supported or returns Win32 error (e.g. ERROR_INVALID_FUNCTION = 1),
            // fall back to the CloudDomainJoin registry state (HC-11).
            return EntraJoinRegistryFallback();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return EntraJoinRegistryFallback();
        }
    }

    internal static CapabilityEntry EntraJoinEntry(int hr, bool isJoined)
    {
        const string Source = "NetGetAadJoinInformation (documented)";
        if (hr != 0)
        {
            return CapabilityEntry.Unavailable("managed.entra_join", "Entra (Azure AD) join state", CapabilityTier.A, Availability.Unknown,
                Source, $"query failed with HRESULT 0x{hr:X8}; state unknown");
        }

        return CapabilityEntry.Available("managed.entra_join", "Entra (Azure AD) join state", CapabilityTier.A,
            Source, isJoined ? ManagedValues.EntraJoined : ManagedValues.EntraNotJoined);
    }

    internal static CapabilityEntry EntraJoinRegistryFallback()
    {
        const string SubKey = @"SYSTEM\CurrentControlSet\Control\CloudDomainJoin\JoinInfo";
        var subKeys = Reg.GetSubKeyNames(SubKey);
        var isJoined = subKeys is { Length: > 0 };
        return CapabilityEntry.Available("managed.entra_join", "Entra (Azure AD) join state", CapabilityTier.A,
            @"HKLM\" + SubKey, isJoined ? ManagedValues.EntraJoined : ManagedValues.EntraNotJoined);
    }

    private static CapabilityEntry AppliedPolicies()
    {
        const string GpoListSubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Group Policy\State\Machine\GPO-List";
        var subKeys = Reg.GetSubKeyNames(GpoListSubKey);

        bool? mdmRegistered = null;
        const uint UpnChars = 256;
        var upn = Marshal.AllocHGlobal((int)UpnChars * sizeof(char));
        try
        {
            var hr = Native.IsDeviceRegisteredWithManagement(out var registered, UpnChars, upn);
            if (hr == 0)
            {
                mdmRegistered = registered != 0;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            mdmRegistered = false;
        }
        finally
        {
            Marshal.FreeHGlobal(upn);
        }

        return EvaluateAppliedPolicies(subKeys, keyName => Reg.GetString($@"{GpoListSubKey}\{keyName}", "SOM"), mdmRegistered);
    }

    internal static CapabilityEntry EvaluateAppliedPolicies(string[]? gpoSubKeys, Func<string, string?> getSom, bool? mdmRegistered)
    {
        const string Id = "managed.applied_policies";
        const string Title = "Applied group and MDM policies";
        const string Source = "Group Policy State (GPO-List) + MDM registration";

        if (mdmRegistered == true)
        {
            return CapabilityEntry.Available(Id, Title, CapabilityTier.A, Source, ManagedValues.PoliciesApplied);
        }

        if (gpoSubKeys is not null)
        {
            foreach (var keyName in gpoSubKeys)
            {
                var som = getSom(keyName);
                if (som is not null && !string.Equals(som, "Local", StringComparison.OrdinalIgnoreCase))
                {
                    return CapabilityEntry.Available(Id, Title, CapabilityTier.A, Source, ManagedValues.PoliciesApplied);
                }
            }
        }

        return CapabilityEntry.Available(Id, Title, CapabilityTier.A, Source, ManagedValues.PoliciesNone);
    }
}
