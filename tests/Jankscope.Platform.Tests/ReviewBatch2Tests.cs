using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Jankscope.Core;

namespace Jankscope.Platform.Tests;

/// <summary>Regression tests for the Stage 0 review, Batch 2 (docs/reviews/stage-0.md #2, #3, #6, #7, #8, #12, #13, #14, #16, #17, #18).</summary>
public class NativeHardeningTests
{
    private static readonly BindingFlags AllMethods =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    [Fact]
    public void Native_libraries_resolve_from_system32_only()
    {
        var attribute = typeof(Native).Assembly.GetCustomAttribute<DefaultDllImportSearchPathsAttribute>();
        Assert.NotNull(attribute);
        Assert.Equal(DllImportSearchPath.System32, attribute.Paths);

        // Guard against a vacuous pass: the assembly really does contain P/Invoke stubs.
        var stubs = typeof(Native).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(AllMethods))
            .Count(m => (m.Attributes & MethodAttributes.PinvokeImpl) != 0);
        Assert.True(stubs >= 10, $"expected the generated P/Invoke stubs to be found, saw {stubs}");
    }

    [Fact]
    public void The_hand_laid_etw_layout_is_enabled_only_for_64_bit_processes()
    {
        Assert.Equal(Environment.Is64BitProcess, EtwNative.LayoutSupported);
    }
}

[Collection("ETW")]
public class EtwTests
{
    [EtwStartFact]
    public void A_probe_session_is_stopped_and_absent_from_the_session_list_afterwards()
    {
        var (start, cleaned, name) = EtwNative.TryOpenAndCloseRealTimeSession();
        Assert.Equal(EtwNative.ErrorSuccess, start);
        Assert.True(cleaned);

        var (status, names) = EtwNative.QueryAllSessionNames();
        Assert.Equal(EtwNative.ErrorSuccess, status);
        Assert.DoesNotContain(name, names); // exact name: parallel probes in other tests cannot interfere
    }

    [EtwStartFact]
    public void The_probe_reports_success_and_leaves_no_session_behind()
    {
        var entries = new EtwProbe().Run();
        var access = entries.Single(e => e.Id == "etw.realtime_session");
        Assert.Equal(Availability.Available, access.Availability);
        Assert.Null(access.Reason); // no "could not be stopped" warning

        var before = EtwNative.QueryAllSessionNames().Names.Count(n => n.StartsWith(Branding.EtwSessionPrefix, StringComparison.Ordinal));
        _ = new EtwProbe().Run();
        var after = EtwNative.QueryAllSessionNames().Names.Count(n => n.StartsWith(Branding.EtwSessionPrefix, StringComparison.Ordinal));
        Assert.Equal(before, after);
    }

    [EtwStartFact]
    public void Enumeration_retries_when_the_first_buffer_is_too_small()
    {
        var (fullStatus, full) = EtwNative.QueryAllSessionNames();
        Assert.Equal(EtwNative.ErrorSuccess, fullStatus);
        Assert.True(full.Count >= 2, "the retry path needs at least two running ETW sessions to be exercised");

        // A capacity of one forces ERROR_MORE_DATA; the second attempt must use the count Windows reports.
        var (status, names) = EtwNative.QueryAllSessionNames(initialCapacity: 1);
        Assert.Equal(EtwNative.ErrorSuccess, status);
        Assert.InRange(names.Count, full.Count - 5, full.Count + 5);
    }

    [Fact]
    public void A_failed_enumeration_still_emits_both_rows_as_unavailable()
    {
        var denied = EtwProbe.SessionEntries(EtwNative.ErrorAccessDenied, []);
        Assert.Equal(["etw.sessions", "etw.own_orphans"], denied.Select(e => e.Id).ToArray());
        Assert.Equal(Availability.NeedsUserAction, denied[0].Availability);
        Assert.Equal(Availability.Unknown, denied[1].Availability);

        var other = EtwProbe.SessionEntries(8, []);
        Assert.Equal(Availability.NotAvailable, other[0].Availability);
        Assert.Contains("Win32 error 8", other[0].Reason);
        Assert.Equal(Availability.Unknown, other[1].Availability);

        var unsupported = EtwProbe.SessionEntries(EtwNative.ErrorNotSupported, []);
        Assert.Equal(Availability.NotAvailable, unsupported[0].Availability);
        Assert.Contains("32-bit", unsupported[0].Reason);
    }

    [Fact]
    public void Own_prefix_sessions_are_listed_with_an_honest_cleanup_statement()
    {
        string[] names = ["Other-Session", $"{Branding.EtwSessionPrefix}probe-1-abcd1234"];
        var orphans = EtwProbe.SessionEntries(EtwNative.ErrorSuccess, names).Single(e => e.Id == "etw.own_orphans");
        Assert.Equal(Availability.Available, orphans.Availability);
        Assert.Contains($"{Branding.EtwSessionPrefix}probe-1-abcd1234", orphans.Value);
        Assert.Contains("no automatic cleanup exists yet", orphans.Value);
        Assert.DoesNotContain("cleaned up on next start", orphans.Value ?? string.Empty);

        var none = EtwProbe.SessionEntries(EtwNative.ErrorSuccess, ["Other-Session"]).Single(e => e.Id == "etw.own_orphans");
        Assert.Equal("none", none.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Success_wording_depends_on_elevation_and_never_claims_unelevated_access_when_elevated(bool elevated)
    {
        var entry = EtwProbe.RealtimeSessionEntry(EtwNative.ErrorSuccess, cleaned: true, "s", elevated);
        Assert.Equal(Availability.Available, entry.Availability);
        Assert.Null(entry.Reason);
        if (elevated)
        {
            Assert.Contains("is elevated", entry.Value);
            Assert.Contains("does not show", entry.Value);
            Assert.DoesNotContain("not elevated", entry.Value);
        }
        else
        {
            Assert.Contains("not elevated", entry.Value);
        }
    }

    [Fact]
    public void A_session_that_started_but_could_not_be_stopped_still_proves_access_and_warns()
    {
        var entry = EtwProbe.RealtimeSessionEntry(EtwNative.ErrorSuccess, cleaned: false, "Jankscope-probe-x", elevated: false);
        Assert.Equal(Availability.Available, entry.Availability);
        Assert.Contains("WARNING", entry.Reason);
        Assert.Contains("Jankscope-probe-x", entry.Reason);
        Assert.Contains("no automatic cleanup exists yet", entry.Reason);
        Assert.DoesNotContain("Performance Log Users", entry.Reason);
    }

    [Fact]
    public void Only_access_denied_asks_for_a_user_action_and_other_codes_are_reported_as_codes()
    {
        var denied = EtwProbe.RealtimeSessionEntry(EtwNative.ErrorAccessDenied, true, "s", elevated: false);
        Assert.Equal(Availability.NeedsUserAction, denied.Availability);
        Assert.Contains("Performance Log Users", denied.Reason);

        var deniedWhileElevated = EtwProbe.RealtimeSessionEntry(EtwNative.ErrorAccessDenied, true, "s", elevated: true);
        Assert.Equal(Availability.NotAvailable, deniedWhileElevated.Availability);
        Assert.DoesNotContain("Performance Log Users", deniedWhileElevated.Reason);

        foreach (var code in new uint[] { 8, 87, EtwNative.ErrorAlreadyExists, 1450 })
        {
            var entry = EtwProbe.RealtimeSessionEntry(code, true, "s", elevated: false);
            Assert.Equal(Availability.NotAvailable, entry.Availability);
            Assert.Contains($"Win32 error {code}", entry.Reason);
            Assert.DoesNotContain("Performance Log Users", entry.Reason);
        }

        var unsupported = EtwProbe.RealtimeSessionEntry(EtwNative.ErrorNotSupported, true, "s", elevated: false);
        Assert.Equal(Availability.NotAvailable, unsupported.Availability);
    }
}

public class SecurityPostureInterpretationTests
{
    private static string Text(CapabilityEntry e) => $"{e.Value}{e.Reason}";

    [Theory]
    [InlineData(0, "off")]
    [InlineData(1, "on")]
    [InlineData(2, "evaluation")]
    public void Smart_app_control_known_values(int value, string expected)
    {
        var entry = SecurityPostureProbe.SmartAppControl(RegInt.Of(value));
        Assert.Equal(Availability.Available, entry.Availability);
        Assert.Contains(expected, entry.Value);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(-1)]
    [InlineData(99)]
    public void Smart_app_control_unrecognized_values_are_unknown_not_off(int value)
    {
        var entry = SecurityPostureProbe.SmartAppControl(RegInt.Of(value));
        Assert.Equal(Availability.Unknown, entry.Availability);
        Assert.Contains("not a recognized value", entry.Reason);
    }

    [Theory]
    [InlineData(0, "off")]
    [InlineData(1, "on")]
    public void Secure_boot_known_values(int value, string expected)
    {
        var entry = SecurityPostureProbe.SecureBoot(RegInt.Of(value));
        Assert.Equal(Availability.Available, entry.Availability);
        Assert.Equal(expected, entry.Value);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(-1)]
    [InlineData(255)]
    public void Secure_boot_unrecognized_values_are_unknown_not_off(int value)
    {
        Assert.Equal(Availability.Unknown, SecurityPostureProbe.SecureBoot(RegInt.Of(value)).Availability);
    }

    [Theory]
    [InlineData(0, "configured off")]
    [InlineData(1, "configured on")]
    public void Memory_integrity_known_values(int value, string expected)
    {
        var entry = SecurityPostureProbe.MemoryIntegrity(RegInt.Of(value));
        Assert.Equal(Availability.Available, entry.Availability);
        Assert.StartsWith(expected, entry.Value);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(-1)]
    public void Memory_integrity_unrecognized_values_are_unknown_not_off(int value)
    {
        Assert.Equal(Availability.Unknown, SecurityPostureProbe.MemoryIntegrity(RegInt.Of(value)).Availability);
    }

    [Fact]
    public void Absent_and_unreadable_values_are_unknown_for_every_security_row()
    {
        foreach (var value in new[] { RegInt.Absent, RegInt.Unreadable })
        {
            Assert.Equal(Availability.Unknown, SecurityPostureProbe.SmartAppControl(value).Availability);
            Assert.Equal(Availability.Unknown, SecurityPostureProbe.SecureBoot(value).Availability);
            Assert.Equal(Availability.Unknown, SecurityPostureProbe.MemoryIntegrity(value).Availability);
            Assert.Equal(Availability.Unknown, ManagedDeviceProbe.SMode(value).Availability);
        }

        Assert.Contains("not present", SecurityPostureProbe.SecureBoot(RegInt.Absent).Reason);
        Assert.Contains("unreadable", SecurityPostureProbe.SecureBoot(RegInt.Unreadable).Reason);
    }

    [Theory]
    [InlineData(0, "off")]
    [InlineData(1, "on")]
    public void S_mode_known_values(int value, string expected)
    {
        var entry = ManagedDeviceProbe.SMode(RegInt.Of(value));
        Assert.Equal(Availability.Available, entry.Availability);
        Assert.Equal(expected, entry.Value);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(-1)]
    public void S_mode_unrecognized_values_are_unknown_not_off(int value)
    {
        Assert.Equal(Availability.Unknown, ManagedDeviceProbe.SMode(RegInt.Of(value)).Availability);
    }

    [Fact]
    public void The_registry_reader_distinguishes_absent_from_present()
    {
        var present = Reg.ReadInt(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuildNumber");
        // CurrentBuildNumber is a string value on Windows: a present value of the wrong type is Unreadable, never "0".
        Assert.Equal(RegState.Unreadable, present.State);

        var absent = Reg.ReadInt(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "Jankscope-no-such-value");
        Assert.Equal(RegState.Absent, absent.State);

        var noKey = Reg.ReadInt(@"SOFTWARE\Jankscope-no-such-key", "Anything");
        Assert.Equal(RegState.Absent, noKey.State);

        var real = Reg.ReadInt(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "UBR");
        Assert.Equal(RegState.Present, real.State);
    }

    [Fact]
    public void Security_rows_never_recommend_turning_a_protection_off()
    {
        var entries = new SecurityPostureProbe().Run();
        Assert.Equal(3, entries.Count);
        // Reported only: never an action request, never advice to weaken a protection.
        Assert.All(entries, e => Assert.True(e.Availability is Availability.Available or Availability.Unknown, e.Id));
        string[] advice = ["disable", "turn off", "switch off", "turning off", "recommend", "should be off", "you can remove"];
        Assert.All(entries, e => Assert.DoesNotContain(advice, a => Text(e).Contains(a, StringComparison.OrdinalIgnoreCase)));
    }
}

public class ManagedDeviceTests
{
    [Fact]
    public void Mdm_enrollment_follows_the_documented_api_not_the_builtin_registry_entries()
    {
        // Stock Windows has built-in 'Cloud/Local/Deploy Authority' entries under HKLM\SOFTWARE\Microsoft\Enrollments even
        // on unmanaged PCs; the probe once reported 'enrolled' from them. The row must agree with the API itself.
        var upn = Marshal.AllocHGlobal(256 * sizeof(char));
        try
        {
            var hr = Native.IsDeviceRegisteredWithManagement(out var registered, 256, upn);
            var entry = new ManagedDeviceProbe().Run().Single(e => e.Id == "managed.mdm_enrollment");
            Assert.Contains("IsDeviceRegisteredWithManagement", entry.Source);
            if (hr != 0)
            {
                Assert.Equal(Availability.Unknown, entry.Availability);
            }
            else
            {
                Assert.Equal(Availability.Available, entry.Availability);
                Assert.Equal(registered != 0, entry.Value!.StartsWith("enrolled", StringComparison.Ordinal));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(upn);
        }
    }
}

public class ServiceStateReaderTests
{
    [Fact]
    public void The_real_service_control_manager_reports_a_running_service_a_missing_one_and_a_bad_name()
    {
        var reader = new ScmServiceStateReader();

        var rpc = reader.Read("RpcSs"); // always running on Windows; cannot be stopped
        Assert.Equal(ServiceInstallation.Installed, rpc.Installation);
        Assert.Equal(ServiceRunState.Running, rpc.RunState);
        Assert.NotNull(rpc.StartType);

        var missing = reader.Read("Jankscope-no-such-service-xyz");
        Assert.Equal(ServiceInstallation.NotInstalled, missing.Installation);

        // A name that could escape the registry path is refused as unknown, never looked up.
        Assert.Equal(ServiceInstallation.Unknown, reader.Read(@"..\..\SAM").Installation);
        Assert.Equal(ServiceInstallation.Unknown, reader.Read(string.Empty).Installation);
    }

    [Fact]
    public void Service_queries_request_only_the_least_privilege_access_rights()
    {
        // SC_MANAGER_CONNECT (0x1) and SERVICE_QUERY_STATUS (0x4): no enumerate, no create, no start/stop/delete, no config.
        Assert.Equal(0x0001u, Native.ScManagerConnect);
        Assert.Equal(0x0004u, Native.ServiceQueryStatus);
    }

    [Theory]
    [InlineData(0u, ServiceRunState.Unknown)]
    [InlineData(1u, ServiceRunState.Stopped)]
    [InlineData(2u, ServiceRunState.Running)]
    [InlineData(3u, ServiceRunState.Running)]
    [InlineData(4u, ServiceRunState.Running)]
    [InlineData(5u, ServiceRunState.Running)]
    [InlineData(6u, ServiceRunState.Running)]
    [InlineData(7u, ServiceRunState.Running)]
    [InlineData(8u, ServiceRunState.Unknown)]
    public void Service_states_other_than_stopped_count_as_active(uint state, ServiceRunState expected)
    {
        Assert.Equal(expected, ScmServiceStateReader.MapCurrentState(state));
    }
}

public class AntiCheatProbeTests
{
    private static readonly ProtectedTitleEntry Title = new("t", "Test Title", ["testgame.exe"], "unverified");

    private static AntiCheatProbe Probe(
        IEnumerable<AntiCheatCatalogEntry> antiCheat, IDictionary<string, ServiceSnapshot> services, IEnumerable<string>? processes)
    {
        var catalogs = new CatalogSet(antiCheat.ToList(), [Title]);
        return new AntiCheatProbe(catalogs, new FakeServiceReader(new Dictionary<string, ServiceSnapshot>(services)), new FakeProcessSource(processes), new FakeGameSource(GameSessionStatus.Inactive));
    }

    private static AntiCheatCatalogEntry Fake(string[]? services = null, string[]? processes = null) =>
        new("fake", "Fake AC", services ?? ["FakeSvc"], processes ?? [], "unverified");

    private static CapabilityEntry Row(IReadOnlyList<CapabilityEntry> rows, string id) => rows.Single(r => r.Id == id);

    [Fact]
    public void A_demand_start_service_that_is_running_is_resident()
    {
        var rows = Probe([Fake()], new Dictionary<string, ServiceSnapshot> { ["FakeSvc"] = new(ServiceInstallation.Installed, ServiceRunState.Running, 3) }, ["explorer"]).Run();

        var resident = Row(rows, "anticheat.resident");
        Assert.Equal(Availability.Available, resident.Availability);
        Assert.Contains("Fake AC", resident.Value);
        Assert.Contains("demand-start", resident.Value);
        Assert.StartsWith("none of the", Row(rows, "anticheat.installed_not_running").Value);
        var state = Row(rows, "anticheat.state");
        Assert.Equal(Availability.Available, state.Availability);
        Assert.StartsWith("ResidentOnly", state.Value);
        Assert.Contains("not suppressed", state.Value);
    }

    [Fact]
    public void An_auto_start_service_that_is_stopped_is_installed_but_not_resident()
    {
        var rows = Probe([Fake()], new Dictionary<string, ServiceSnapshot> { ["FakeSvc"] = new(ServiceInstallation.Installed, ServiceRunState.Stopped, 2) }, ["explorer"]).Run();

        Assert.StartsWith("none of the", Row(rows, "anticheat.resident").Value);
        var installed = Row(rows, "anticheat.installed_not_running");
        Assert.Contains("Fake AC", installed.Value);
        Assert.Contains("auto-start", installed.Value);
        Assert.Contains("stopped", installed.Value);
        Assert.StartsWith("Idle", Row(rows, "anticheat.state").Value);
    }

    [Fact]
    public void A_disabled_stopped_service_is_reported_as_installed_not_running()
    {
        var rows = Probe([Fake()], new Dictionary<string, ServiceSnapshot> { ["FakeSvc"] = new(ServiceInstallation.Installed, ServiceRunState.Stopped, 4) }, ["explorer"]).Run();
        Assert.Contains("disabled", Row(rows, "anticheat.installed_not_running").Value);
    }

    [Fact]
    public void An_absent_service_means_none_and_idle()
    {
        var rows = Probe([Fake()], new Dictionary<string, ServiceSnapshot>(), ["explorer"]).Run();

        Assert.StartsWith("none of the", Row(rows, "anticheat.resident").Value);
        Assert.StartsWith("none of the", Row(rows, "anticheat.installed_not_running").Value);
        Assert.StartsWith("none of the", Row(rows, "anticheat.protected_running").Value);
        var state = Row(rows, "anticheat.state");
        Assert.Equal(Availability.Available, state.Availability);
        Assert.StartsWith("Idle", state.Value);
    }

    [Fact]
    public void An_unqueryable_service_is_unknown_never_none_and_never_idle()
    {
        var rows = Probe([Fake()], new Dictionary<string, ServiceSnapshot> { ["FakeSvc"] = ServiceSnapshot.Unknown }, ["explorer"]).Run();

        var resident = Row(rows, "anticheat.resident");
        Assert.Equal(Availability.Unknown, resident.Availability);
        Assert.Contains("Fake AC", resident.Reason);
        Assert.Equal(Availability.Unknown, Row(rows, "anticheat.installed_not_running").Availability);
        var state = Row(rows, "anticheat.state");
        Assert.Equal(Availability.Unknown, state.Availability);
        Assert.Contains("could not query", state.Reason);
        Assert.Contains("treated as suppressed", state.Reason);
    }

    [Fact]
    public void An_installed_service_whose_state_cannot_be_read_is_unknown()
    {
        var rows = Probe([Fake()], new Dictionary<string, ServiceSnapshot> { ["FakeSvc"] = new(ServiceInstallation.Installed, ServiceRunState.Unknown, 3) }, ["explorer"]).Run();
        Assert.Equal(Availability.Unknown, Row(rows, "anticheat.resident").Availability);
        Assert.Equal(Availability.Unknown, Row(rows, "anticheat.state").Availability);
    }

    [Fact]
    public void A_running_helper_process_makes_anti_cheat_resident_even_without_a_service()
    {
        var rows = Probe([Fake(processes: ["FakeHelper.exe"])], new Dictionary<string, ServiceSnapshot>(), ["explorer", "fakehelper"]).Run();
        var resident = Row(rows, "anticheat.resident");
        Assert.Equal(Availability.Available, resident.Availability);
        Assert.Contains("helper process running", resident.Value);
    }

    [Fact]
    public void A_missing_process_snapshot_makes_the_process_dependent_rows_unknown()
    {
        var rows = Probe([Fake(processes: ["FakeHelper.exe"])], new Dictionary<string, ServiceSnapshot>(), processes: null).Run();
        Assert.Equal(Availability.Unknown, Row(rows, "anticheat.protected_running").Availability);
        Assert.Equal(Availability.Unknown, Row(rows, "anticheat.resident").Availability);
        var state = Row(rows, "anticheat.state");
        Assert.Equal(Availability.Unknown, state.Availability);
        Assert.Contains("process-name snapshot unavailable", state.Reason);
    }

    [Fact]
    public void A_running_protected_title_keeps_safe_mode_even_when_something_else_is_unknown()
    {
        var rows = Probe([Fake()], new Dictionary<string, ServiceSnapshot> { ["FakeSvc"] = ServiceSnapshot.Unknown }, ["testgame"]).Run();

        Assert.Contains("Test Title", Row(rows, "anticheat.protected_running").Value);
        var state = Row(rows, "anticheat.state");
        Assert.Equal(Availability.Available, state.Availability);
        Assert.StartsWith("ProtectedTitle", state.Value);
        Assert.Contains("suppressed", state.Value);
        Assert.DoesNotContain("not suppressed", state.Value);
    }

    [Fact]
    public void Without_catalogs_every_anticheat_row_is_present_and_unknown()
    {
        var rows = new AntiCheatProbe(CatalogSet.Empty, new FakeServiceReader(new Dictionary<string, ServiceSnapshot>()), new FakeProcessSource(["explorer"])).Run();

        Assert.Equal(
            ["anticheat.resident", "anticheat.installed_not_running", "anticheat.protected_running", "anticheat.state"],
            rows.Select(r => r.Id).ToArray());
        Assert.All(rows, r => Assert.Equal(Availability.Unknown, r.Availability));
        Assert.Contains("catalog not loaded", Row(rows, "anticheat.state").Reason);
    }

    [Fact]
    public void Only_the_protected_catalog_loaded_cannot_vouch_for_resident_anti_cheat()
    {
        var catalogs = new CatalogSet([], [Title]);
        var rows = new AntiCheatProbe(catalogs, new FakeServiceReader(new Dictionary<string, ServiceSnapshot>()), new FakeProcessSource(["explorer"])).Run();

        Assert.Equal(Availability.Unknown, Row(rows, "anticheat.resident").Availability);
        Assert.StartsWith("none of the", Row(rows, "anticheat.protected_running").Value);
        Assert.Equal(Availability.Unknown, Row(rows, "anticheat.state").Availability);
    }
}

public class MatrixBuilderResilienceTests
{
    private sealed class MessageLeakingProbe : IProbe
    {
        public string Id => "leaky";

        public IReadOnlyList<CapabilityEntry> Run() =>
            throw new InvalidOperationException(@"could not open C:\Users\SomeName\secret.txt");
    }

    private sealed class HangingProbe(ManualResetEventSlim release) : IProbe
    {
        public string Id => "hang";

        public IReadOnlyList<CapabilityEntry> Run()
        {
            release.Wait(TimeSpan.FromSeconds(30));
            return [CapabilityEntry.Available("hang.late", "late", CapabilityTier.A, "test", "never reported")];
        }
    }

    private sealed class TimeoutThrowingProbe : IProbe
    {
        public string Id => "tt";

        public IReadOnlyList<CapabilityEntry> Run() => throw new TimeoutException("inner");
    }

    [Fact]
    public void Only_the_exception_type_is_exported_never_its_message()
    {
        var matrix = new CapabilityMatrixBuilder([new MessageLeakingProbe()]).Build();
        var json = matrix.ToJson();

        Assert.DoesNotContain("SomeName", json);
        Assert.DoesNotContain("secret.txt", json);
        Assert.Contains("InvalidOperationException", json);
        Assert.Equal("InvalidOperationException", matrix.ProbeHealth.Single().Error);
    }

    [Fact]
    public void A_probe_that_throws_a_timeout_exception_is_not_mistaken_for_a_hung_probe()
    {
        var matrix = new CapabilityMatrixBuilder([new TimeoutThrowingProbe()]).Build();
        Assert.Equal("TimeoutException", matrix.ProbeHealth.Single().Error);
    }

    [Fact]
    public void A_hung_probe_becomes_a_timeout_row_and_the_others_still_run()
    {
        using var release = new ManualResetEventSlim(false);
        try
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var matrix = new CapabilityMatrixBuilder([new HangingProbe(release), new OsProbe()], probeTimeout: TimeSpan.FromMilliseconds(300)).Build();
            watch.Stop();

            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"build took {watch.Elapsed}");
            var row = Assert.Single(matrix.Entries, e => e.Id == "hang.probe");
            Assert.Equal(Availability.NotAvailable, row.Availability);
            Assert.Contains("timed out", row.Reason);
            Assert.DoesNotContain(matrix.Entries, e => e.Id == "hang.late");
            Assert.Contains(matrix.Entries, e => e.Id == "os.version");
            var health = matrix.ProbeHealth.Single(h => h.ProbeId == "hang");
            Assert.False(health.Succeeded);
            Assert.StartsWith("timed out", health.Error);
            Assert.True(matrix.ProbeHealth.Single(h => h.ProbeId == "os").Succeeded);
            using var json = JsonDocument.Parse(matrix.ToJson());
            Assert.Equal(matrix.Entries.Count, json.RootElement.GetProperty("entries").GetArrayLength());
        }
        finally
        {
            release.Set();
        }
    }
}
