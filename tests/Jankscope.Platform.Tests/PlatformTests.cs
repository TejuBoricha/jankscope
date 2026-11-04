using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using Jankscope.Core;

namespace Jankscope.Platform.Tests;

internal static class TestCatalogs
{
    public static CatalogSet Shipped()
    {
        var dir = CatalogSet.FindDefaultDirectory(AppContext.BaseDirectory)
                  ?? throw new InvalidOperationException("catalogs folder not found above " + AppContext.BaseDirectory);
        return CatalogSet.Load(dir);
    }
}

[Collection("ETW")]
public class CapabilityMatrixTests
{
    private static readonly string[] ExpectedIds =
    [
        "os.version", "os.support", "privilege.elevation", "privilege.perf_log_users", "etw.sessions", "etw.own_orphans",
        "etw.realtime_session", "security.smart_app_control", "hardware.form_factor", "managed.domain_join", "anticheat.resident",
        "anticheat.installed_not_running", "anticheat.protected_running", "anticheat.state", "frame.capture", "mode.optimize",
        "managed.applied_policies", "managed.entra_join", "game.session",
    ];

    [Fact]
    public void Default_probes_produce_a_complete_well_formed_matrix_on_this_machine()
    {
        var probes = DefaultProbes.Create(TestCatalogs.Shipped());
        var matrix = new CapabilityMatrixBuilder(probes).Build();

        Assert.Equal(probes.Count, matrix.ProbeHealth.Count);
        Assert.Equal(matrix.Entries.Count, matrix.Entries.Select(e => e.Id).Distinct().Count());
        foreach (var id in ExpectedIds)
        {
            Assert.Contains(matrix.Entries, e => e.Id == id);
        }

        Assert.All(matrix.Entries, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Source), e.Id);
            if (e.Availability == Availability.Available)
            {
                Assert.False(string.IsNullOrWhiteSpace(e.Value), e.Id);
            }
            else
            {
                Assert.False(string.IsNullOrWhiteSpace(e.Reason), e.Id);
            }
        });

        using var json = JsonDocument.Parse(matrix.ToJson());
        Assert.Equal(matrix.Entries.Count, json.RootElement.GetProperty("entries").GetArrayLength());
    }

    [Fact]
    public void A_failing_probe_becomes_an_explicit_row_and_does_not_stop_the_others()
    {
        var matrix = new CapabilityMatrixBuilder([new ThrowingProbe(), new OsProbe()]).Build();

        var failed = Assert.Single(matrix.Entries, e => e.Id == "throwing.probe");
        Assert.Equal(Availability.NotAvailable, failed.Availability);
        Assert.Contains("probe failed", failed.Reason);
        Assert.Contains(matrix.Entries, e => e.Id == "os.version");
        Assert.False(matrix.ProbeHealth.Single(h => h.ProbeId == "throwing").Succeeded);
        Assert.True(matrix.ProbeHealth.Single(h => h.ProbeId == "os").Succeeded);
    }

    private sealed class ThrowingProbe : IProbe
    {
        public string Id => "throwing";

        public IReadOnlyList<CapabilityEntry> Run() => throw new InvalidOperationException("boom");
    }
}

public class DerivedCapabilityTests
{
    private const string Standard = "not elevated (standard user token)";

    private static CapabilityEntry Ok(string id, string value) =>
        CapabilityEntry.Available(id, id, CapabilityTier.A, "test", value);

    private static CapabilityEntry Missing(string id, Availability a, string reason = "r") =>
        CapabilityEntry.Unavailable(id, id, CapabilityTier.A, a, "test", reason);

    private static CapabilityEntry Find(IReadOnlyList<CapabilityEntry> list, string id) => list.Single(e => e.Id == id);

    /// <summary>Every input to optimize mode known and clean.</summary>
    private static List<CapabilityEntry> Clean() =>
    [
        Ok("os.support", "Windows 11: primary target"),
        Ok("managed.domain_join", ManagedValues.Workgroup),
        Ok("managed.mdm_enrollment", ManagedValues.MdmNone),
        Ok("managed.s_mode", ManagedValues.SModeOff),
        Ok("managed.applied_policies", "none"),
        Ok("managed.entra_join", "not joined"),
        Ok("environment.virtual_machine", "not a virtual machine"),
        Ok("game.session", "no game session"),
        Ok("anticheat.state", "Idle; elevated and kernel collectors not suppressed"),
    ];

    private static List<CapabilityEntry> CleanWith(CapabilityEntry replacement)
    {
        var list = Clean();
        list.RemoveAll(e => e.Id == replacement.Id);
        list.Add(replacement);
        return list;
    }

    [Fact]
    public void Frame_capture_follows_the_coverage_matrix()
    {
        var withAccess = DerivedCapabilities.Compute([Ok("etw.realtime_session", "yes"), Ok("privilege.elevation", Standard)]);
        Assert.Equal(Availability.NeedsOptionalTool, Find(withAccess, "frame.capture").Availability);

        var denied = DerivedCapabilities.Compute([Missing("etw.realtime_session", Availability.NeedsUserAction), Ok("privilege.elevation", Standard)]);
        var capture = Find(denied, "frame.capture");
        Assert.Equal(Availability.NeedsUserAction, capture.Availability);
        Assert.Contains("Performance Log Users", capture.Reason);

        var absent = DerivedCapabilities.Compute([]);
        Assert.Equal(Availability.Unknown, Find(absent, "frame.capture").Availability);
    }

    [Fact]
    public void An_elevated_or_unknown_probe_never_proves_unelevated_frame_capture()
    {
        var elevated = DerivedCapabilities.Compute([Ok("etw.realtime_session", "yes"), Ok("privilege.elevation", "elevated administrator")]);
        var elevatedCapture = Find(elevated, "frame.capture");
        Assert.Equal(Availability.Unknown, elevatedCapture.Availability);
        Assert.Contains("unproven", elevatedCapture.Reason);

        var noElevationRow = DerivedCapabilities.Compute([Ok("etw.realtime_session", "yes")]);
        Assert.Equal(Availability.Unknown, Find(noElevationRow, "frame.capture").Availability);
    }

    [Theory]
    [InlineData(Availability.NotAvailable)]
    [InlineData(Availability.Unknown)]
    public void Only_access_denied_is_a_user_action_other_failures_are_not_blamed_on_group_membership(Availability etwAvailability)
    {
        var entries = DerivedCapabilities.Compute([Missing("etw.realtime_session", etwAvailability, "StartTrace failed with Win32 error 8"), Ok("privilege.elevation", Standard)]);
        var capture = Find(entries, "frame.capture");
        Assert.Equal(Availability.Unknown, capture.Availability);
        Assert.DoesNotContain("Performance Log Users", capture.Reason);
    }

    [Fact]
    public void Optimize_mode_is_recommend_only_while_any_state_is_unknown()
    {
        // All managed indicators clean, but VM detection is not implemented: unknown state defaults to Recommend-only (HC-11, §3.5).
        var entries = CleanWith(CapabilityEntry.Planned("environment.virtual_machine", "VM", CapabilityTier.A, "Stage 1a"));
        var mode = Find(DerivedCapabilities.Compute(entries), "mode.optimize");
        Assert.Equal(Availability.NotAvailable, mode.Availability);
        Assert.Contains("virtual-machine state unknown", mode.Reason);
    }

    [Theory]
    [InlineData("managed.domain_join", ManagedValues.DomainJoined, "domain-joined device")]
    [InlineData("managed.mdm_enrollment", ManagedValues.MdmEnrolled, "MDM-enrolled device")]
    [InlineData("managed.s_mode", ManagedValues.SModeOn, "S mode")]
    public void Managed_indicators_force_recommend_only(string id, string value, string expectedBlocker)
    {
        var mode = Find(DerivedCapabilities.Compute(CleanWith(Ok(id, value))), "mode.optimize");
        Assert.Equal(Availability.NotAvailable, mode.Availability);
        Assert.Contains($"Recommend-only: {expectedBlocker}.", mode.Reason); // exactly the blocking label, not "... unknown" or "... unrecognized"
    }

    [Theory]
    [InlineData("managed.domain_join", "joined to a domain", "domain-joined device state unrecognized")]
    [InlineData("managed.domain_join", "DOMAIN-JOINED", "domain-joined device state unrecognized")]
    [InlineData("managed.mdm_enrollment", "registered with a management service", "MDM-enrolled device state unrecognized")]
    [InlineData("managed.mdm_enrollment", "enrolled", "MDM-enrolled device state unrecognized")]
    [InlineData("managed.s_mode", "enabled", "S mode state unrecognized")]
    [InlineData("managed.s_mode", "ON", "S mode state unrecognized")]
    public void A_managed_value_nobody_recognises_blocks_instead_of_passing(string id, string value, string expectedBlocker)
    {
        // Fail closed (review pass 2 #8): rewording a probe's value can only make the gate stricter, never let a managed device through.
        var mode = Find(DerivedCapabilities.Compute(CleanWith(Ok(id, value))), "mode.optimize");
        Assert.Equal(Availability.NotAvailable, mode.Availability);
        Assert.Contains(expectedBlocker, mode.Reason);
    }

    [Theory]
    [InlineData("os.support", Availability.Unknown, "Windows support state unknown")]
    [InlineData("os.support", Availability.NotAvailable, "unsupported Windows")]
    [InlineData("managed.domain_join", Availability.Unknown, "domain-joined device unknown")]
    [InlineData("managed.mdm_enrollment", Availability.Unknown, "MDM-enrolled device unknown")]
    [InlineData("managed.s_mode", Availability.Unknown, "S mode unknown")]
    [InlineData("managed.applied_policies", Availability.NotImplemented, "applied-policy state unknown")]
    [InlineData("managed.entra_join", Availability.NotImplemented, "Entra-join state unknown")]
    [InlineData("environment.virtual_machine", Availability.NotAvailable, "virtual-machine state unknown")]
    [InlineData("game.session", Availability.NotImplemented, "game-session state unknown")]
    [InlineData("anticheat.state", Availability.Unknown, "anti-cheat state unknown")]
    [InlineData("anticheat.state", Availability.NeedsUserAction, "anti-cheat state unknown")]
    public void Any_input_that_is_not_available_blocks_optimize_mode(string id, Availability availability, string expectedBlocker)
    {
        var mode = Find(DerivedCapabilities.Compute(CleanWith(Missing(id, availability))), "mode.optimize");
        Assert.Equal(Availability.NotAvailable, mode.Availability);
        Assert.Contains(expectedBlocker, mode.Reason);
    }

    [Theory]
    [InlineData("os.support")]
    [InlineData("managed.domain_join")]
    [InlineData("managed.mdm_enrollment")]
    [InlineData("managed.s_mode")]
    [InlineData("managed.applied_policies")]
    [InlineData("managed.entra_join")]
    [InlineData("environment.virtual_machine")]
    [InlineData("game.session")]
    [InlineData("anticheat.state")]
    public void A_missing_input_row_blocks_optimize_mode(string id)
    {
        var entries = Clean();
        entries.RemoveAll(e => e.Id == id);
        var mode = Find(DerivedCapabilities.Compute(entries), "mode.optimize");
        Assert.Equal(Availability.NotAvailable, mode.Availability);
        Assert.Contains("unknown", mode.Reason);
    }

    [Fact]
    public void Optimize_mode_is_eligible_only_when_everything_is_known_and_clean()
    {
        Assert.Equal(Availability.Available, Find(DerivedCapabilities.Compute(Clean()), "mode.optimize").Availability);
    }

    // ---- contract: the gate must understand the probes' own output (review pass 2 #8) ----

    [Theory]
    [InlineData(3, true)] // NetSetupDomainName
    [InlineData(2, false)] // workgroup
    [InlineData(1, false)] // unjoined
    public void The_domain_probes_own_output_is_read_correctly_by_the_gate(int joinStatus, bool blocks)
    {
        AssertGate(ManagedDeviceProbe.DomainJoinEntry(0, joinStatus), blocks, "domain-joined device");
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public void The_mdm_probes_own_output_is_read_correctly_by_the_gate(int registered, bool blocks)
    {
        AssertGate(ManagedDeviceProbe.MdmEntry(0, registered), blocks, "MDM-enrolled device");
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public void The_s_mode_probes_own_output_is_read_correctly_by_the_gate(int skuPolicy, bool blocks)
    {
        AssertGate(ManagedDeviceProbe.SMode(RegInt.Of(skuPolicy)), blocks, "S mode");
    }

    private static void AssertGate(CapabilityEntry realProbeOutput, bool blocks, string label)
    {
        var mode = Find(DerivedCapabilities.Compute(CleanWith(realProbeOutput)), "mode.optimize");
        if (blocks)
        {
            Assert.Equal(Availability.NotAvailable, mode.Availability);
            Assert.Contains($"Recommend-only: {label}.", mode.Reason);
        }
        else
        {
            Assert.Equal(Availability.Available, mode.Availability);
        }
    }

    [Theory]
    [InlineData(5u)]
    [InlineData(1722u)]
    public void A_failed_domain_query_is_unknown_and_blocks(uint status)
    {
        var entry = ManagedDeviceProbe.DomainJoinEntry(status, 0);
        Assert.Equal(Availability.Unknown, entry.Availability);
        Assert.Equal(Availability.NotAvailable, Find(DerivedCapabilities.Compute(CleanWith(entry)), "mode.optimize").Availability);
    }

    [Fact]
    public void A_failed_mdm_query_is_unknown_and_blocks()
    {
        var entry = ManagedDeviceProbe.MdmEntry(unchecked((int)0x80070005), 0);
        Assert.Equal(Availability.Unknown, entry.Availability);
        Assert.Equal(Availability.NotAvailable, Find(DerivedCapabilities.Compute(CleanWith(entry)), "mode.optimize").Availability);
    }
}

public class SmbiosChassisTests
{
    [Theory]
    [InlineData(new[] { 10 }, "laptop")]
    [InlineData(new[] { 9 }, "laptop")]
    [InlineData(new[] { 3 }, "desktop")]
    [InlineData(new[] { 13 }, "all-in-one")]
    [InlineData(new[] { 11 }, "handheld")]
    [InlineData(new[] { 35 }, "mini PC")]
    [InlineData(new[] { 31 }, "tablet or convertible")]
    [InlineData(new[] { 99 }, "other")]
    public void Chassis_types_map_to_form_factor_classes(int[] types, string expected)
    {
        Assert.Equal(expected, SmbiosChassis.Classify(types));
    }

    /// <summary>
    /// The chassis-type table, written out independently of the implementation. Types 1-24 and 30-32 were checked against Microsoft's
    /// Win32_SystemEnclosure page on 2026-10-16; types 25-29 and 33-36 follow SMBIOS (DSP0134) numbering and were NOT re-read (FACTS F7).
    /// </summary>
    private static readonly (int Type, string Class)[] FullTable =
    [
        (1, "other"), (2, "other"), (3, "desktop"), (4, "desktop"), (5, "desktop"), (6, "desktop"), (7, "desktop"),
        (8, "laptop"), (9, "laptop"), (10, "laptop"), (11, "handheld"), (12, "other"), (13, "all-in-one"), (14, "laptop"),
        (15, "desktop"), (16, "desktop"), (17, "server"), (18, "other"), (19, "other"), (20, "other"), (21, "other"),
        (22, "other"), (23, "server"), (24, "other"), (25, "server"), (26, "other"), (27, "other"), (28, "server"),
        (29, "other"), (30, "tablet or convertible"), (31, "tablet or convertible"), (32, "tablet or convertible"),
        (33, "other"), (34, "other"), (35, "mini PC"), (36, "mini PC"),
    ];

    [Fact]
    public void Every_documented_chassis_type_maps_as_expected_and_sealed_case_pc_is_not_a_server()
    {
        Assert.Equal(36, FullTable.Length);
        foreach (var (type, expected) in FullTable)
        {
            Assert.True(expected == SmbiosChassis.Classify([type]), $"chassis type {type}: expected '{expected}', got '{SmbiosChassis.Classify([type])}'");
        }

        Assert.Equal("other", SmbiosChassis.Classify([24]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(37)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void Values_outside_the_documented_table_are_other(int type)
    {
        Assert.Equal("other", SmbiosChassis.Classify([type]));
    }

    [Fact]
    public void Mixed_types_are_listed_not_collapsed()
    {
        Assert.Equal("tablet or convertible/laptop", SmbiosChassis.Classify([31, 10]));
    }
}

public class HardwareProbeTests
{
    [Theory]
    [InlineData(true, 255, null)]
    [InlineData(false, 0, null)]
    [InlineData(false, 128, null)]
    [InlineData(true, 128, false)]
    [InlineData(true, 129, false)]
    [InlineData(true, 0, true)]
    [InlineData(true, 1, true)]
    [InlineData(true, 2, true)]
    [InlineData(true, 4, true)]
    [InlineData(true, 8, true)]
    [InlineData(true, 9, true)]
    [InlineData(true, 12, true)]
    public void Battery_flag_maps_to_present_absent_or_unknown(bool read, int flag, bool? expected)
    {
        Assert.Equal(expected, HardwareProbe.BatteryPresence(read, (byte)flag));
    }
}

[Collection("ETW")]
public class ProbeBehaviourTests
{
    [Fact]
    public void Windows_11_is_detected_by_build_number_never_by_name()
    {
        var value = new OsProbe().Run().Single(e => e.Id == "os.version").Value!;
        var isWin11Build = Environment.OSVersion.Version.Build >= 22000;
        Assert.Equal(isWin11Build, value.StartsWith("Windows 11", StringComparison.Ordinal));
    }

    [Fact]
    public void Elevation_report_matches_the_token()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        var value = new PrivilegeProbe().Run().Single(e => e.Id == "privilege.elevation").Value!;
        Assert.Equal(elevated, value.StartsWith("elevated", StringComparison.Ordinal));
        Assert.Equal(!elevated, value.StartsWith("not elevated", StringComparison.Ordinal));
        Assert.Equal(elevated, PrivilegeProbe.IsElevatedValue(value));
    }

    [Theory]
    [InlineData("elevated administrator", true)]
    [InlineData("not elevated (standard user token)", false)]
    [InlineData("not elevated (filtered token of an administrator account; not a standard-user test)", false)]
    [InlineData("something else", null)]
    [InlineData(null, null)]
    public void Elevation_row_values_are_parsed_conservatively(string? value, bool? expected)
    {
        Assert.Equal(expected, PrivilegeProbe.IsElevatedValue(value));
    }

    [Fact]
    public void Anti_cheat_probe_detects_a_running_protected_title_from_the_catalog()
    {
        // Uses the real process snapshot on purpose: it exercises SystemProcessNameSource end to end.
        var self = Process.GetCurrentProcess().ProcessName;
        var catalogs = new CatalogSet([], [new ProtectedTitleEntry("self", "Test Title", [self + ".exe"], "unverified")]);
        var entries = new AntiCheatProbe(catalogs).Run();
        Assert.Contains("Test Title", entries.Single(e => e.Id == "anticheat.protected_running").Value);
        Assert.StartsWith("ProtectedTitle", entries.Single(e => e.Id == "anticheat.state").Value);
    }
}
