using Jankscope.Core;

namespace Jankscope.Platform.Tests;

/// <summary>Regression tests for the independent second-pass review (docs/reviews/stage-0-pass2.md).</summary>
public class AntiCheatStateTests
{
    private static readonly ProtectedTitleEntry Title = new("t", "Test Title", ["testgame.exe"], "unverified");
    private static readonly AntiCheatCatalogEntry Fake = new("fake", "Fake AC", ["FakeSvc"], [], "unverified");

    private static IReadOnlyList<CapabilityEntry> Run(
        CatalogSet catalogs, IGameSessionSource? game, ServiceSnapshot? service = null, IEnumerable<string>? processes = null)
    {
        var services = new Dictionary<string, ServiceSnapshot> { ["FakeSvc"] = service ?? ServiceSnapshot.NotInstalled };
        return new AntiCheatProbe(catalogs, new FakeServiceReader(services), new FakeProcessSource(processes ?? ["explorer"]), game).Run();
    }

    private static CapabilityEntry State(IReadOnlyList<CapabilityEntry> rows) => rows.Single(r => r.Id == "anticheat.state");

    [Fact]
    public void Without_a_game_session_detector_the_state_is_unknown_never_idle()
    {
        // Review pass 2 #5: "Idle" cannot be told apart from "a game session is running and we cannot see it".
        var state = State(Run(new CatalogSet([Fake], [Title]), game: null));
        Assert.Equal(Availability.Unknown, state.Availability);
        Assert.Contains("game-session status unknown", state.Reason);
        Assert.Null(state.Value);
    }

    [Fact]
    public void The_default_game_session_source_answers_unknown()
    {
        Assert.Equal(GameSessionStatus.Unknown, new UnimplementedGameSessionSource().Get());
    }

    [Fact]
    public void A_detector_that_says_inactive_allows_idle_and_resident_only()
    {
        var idle = State(Run(new CatalogSet([Fake], [Title]), new FakeGameSource(GameSessionStatus.Inactive)));
        Assert.Equal(Availability.Available, idle.Availability);
        Assert.StartsWith("Idle", idle.Value);

        var resident = State(Run(new CatalogSet([Fake], [Title]), new FakeGameSource(GameSessionStatus.Inactive),
            new ServiceSnapshot(ServiceInstallation.Installed, ServiceRunState.Running, 1)));
        Assert.StartsWith("ResidentOnly", resident.Value);
        Assert.Contains("not suppressed", resident.Value);
    }

    [Fact]
    public void An_active_game_session_is_reported_and_suppresses_the_restricted_collectors()
    {
        var state = State(Run(new CatalogSet([Fake], [Title]), new FakeGameSource(GameSessionStatus.Active)));
        Assert.Equal(Availability.Available, state.Availability);
        Assert.StartsWith("GameSession", state.Value);
        Assert.DoesNotContain("not suppressed", state.Value);
    }

    [Fact]
    public void An_active_game_session_holds_even_when_something_else_is_unknown()
    {
        var state = State(Run(new CatalogSet([Fake], [Title]), new FakeGameSource(GameSessionStatus.Active), ServiceSnapshot.Unknown));
        Assert.StartsWith("GameSession", state.Value);
    }

    [Fact]
    public void A_missing_protected_titles_catalog_alone_makes_the_state_unknown()
    {
        // The anti-cheat catalog is loaded and the game source says inactive; only the protected-titles catalog is missing.
        var rows = Run(new CatalogSet([Fake], []), new FakeGameSource(GameSessionStatus.Inactive));
        var state = State(rows);
        Assert.Equal(Availability.Unknown, state.Availability);
        Assert.Contains("protected-titles catalog not loaded", state.Reason);
        Assert.Equal(Availability.Unknown, rows.Single(r => r.Id == "anticheat.protected_running").Availability);
    }

    [Fact]
    public void A_known_resident_anti_cheat_does_not_turn_a_gap_into_a_claim()
    {
        var rows = Run(new CatalogSet([Fake], []), new FakeGameSource(GameSessionStatus.Inactive),
            new ServiceSnapshot(ServiceInstallation.Installed, ServiceRunState.Running, 1));
        Assert.Equal(Availability.Unknown, State(rows).Availability); // not ResidentOnly: a protected title might be running
    }

    [Fact]
    public void The_none_rows_say_none_of_the_catalogued_products_and_are_inferred_not_measured()
    {
        var rows = Run(new CatalogSet([Fake], [Title]), new FakeGameSource(GameSessionStatus.Inactive));
        foreach (var id in new[] { "anticheat.resident", "anticheat.installed_not_running", "anticheat.protected_running" })
        {
            var row = rows.Single(r => r.Id == id);
            Assert.StartsWith("none of the 1 catalogued", row.Value);
            Assert.Equal(DataProvenance.Inferred, row.Provenance);
        }
    }

    [Fact]
    public void A_positive_finding_stays_measured()
    {
        var rows = Run(new CatalogSet([Fake], [Title]), new FakeGameSource(GameSessionStatus.Inactive),
            new ServiceSnapshot(ServiceInstallation.Installed, ServiceRunState.Running, 1));
        Assert.Equal(DataProvenance.Measured, rows.Single(r => r.Id == "anticheat.resident").Provenance);
    }
}

public class ScmReaderSeamTests
{
    [Fact]
    public void The_reader_asks_for_connect_and_query_status_only_and_closes_every_handle()
    {
        var native = new FakeScmNative();
        var snapshot = new ScmServiceStateReader(native).Read("SomeService");

        Assert.Equal([0x0001u], native.ManagerAccess); // SC_MANAGER_CONNECT
        Assert.Equal([0x0004u], native.ServiceAccess); // SERVICE_QUERY_STATUS
        Assert.Equal(2, native.Opened);
        Assert.Equal(native.Opened, native.Closed);
        Assert.Equal(ServiceRunState.Running, snapshot.RunState);
    }

    [Theory]
    [InlineData(1060u)]
    [InlineData(123u)]
    public void No_such_service_and_invalid_name_mean_not_installed(uint error)
    {
        var native = new FakeScmNative(openServiceError: error);
        Assert.Equal(ServiceInstallation.NotInstalled, new ScmServiceStateReader(native).Read("X").Installation);
        Assert.Equal(native.Opened, native.Closed);
    }

    [Theory]
    [InlineData(5u)] // access denied: the service may well exist
    [InlineData(1722u)]
    [InlineData(1u)]
    [InlineData(2u)]
    [InlineData(31u)]
    public void Access_denied_and_every_other_open_failure_are_unknown_never_not_installed(uint error)
    {
        var native = new FakeScmNative(openServiceError: error);
        var snapshot = new ScmServiceStateReader(native).Read("X");
        Assert.Equal(ServiceInstallation.Unknown, snapshot.Installation);
        Assert.Equal(ServiceRunState.Unknown, snapshot.RunState);
        Assert.Equal(native.Opened, native.Closed);
    }

    [Fact]
    public void A_failed_service_manager_connection_is_unknown_and_opens_nothing()
    {
        var native = new FakeScmNative(managerOk: false);
        Assert.Equal(ServiceInstallation.Unknown, new ScmServiceStateReader(native).Read("X").Installation);
        Assert.Empty(native.ServiceAccess);
        Assert.Equal(0, native.Opened);
    }

    [Fact]
    public void A_failed_status_query_on_an_installed_service_leaves_the_run_state_unknown_not_stopped()
    {
        var native = new FakeScmNative(queryOk: false);
        var snapshot = new ScmServiceStateReader(native).Read("X");
        Assert.Equal(ServiceInstallation.Installed, snapshot.Installation);
        Assert.Equal(ServiceRunState.Unknown, snapshot.RunState);
        Assert.Equal(native.Opened, native.Closed);
    }

    [Theory]
    [InlineData(1u, ServiceRunState.Stopped)]
    [InlineData(4u, ServiceRunState.Running)]
    [InlineData(2u, ServiceRunState.Running)]
    [InlineData(7u, ServiceRunState.Running)]
    [InlineData(99u, ServiceRunState.Unknown)]
    public void The_reported_state_is_mapped(uint state, ServiceRunState expected)
    {
        Assert.Equal(expected, new ScmServiceStateReader(new FakeScmNative(currentState: state)).Read("X").RunState);
    }

    [Theory]
    [InlineData(1060u, ServiceInstallation.NotInstalled)]
    [InlineData(123u, ServiceInstallation.NotInstalled)]
    [InlineData(5u, ServiceInstallation.Unknown)]
    [InlineData(0u, ServiceInstallation.Unknown)]
    [InlineData(1722u, ServiceInstallation.Unknown)]
    public void The_open_error_mapping_is_a_pure_table(uint error, ServiceInstallation expected)
    {
        Assert.Equal(expected, ScmServiceStateReader.MapOpenServiceError(error).Installation);
    }

    [Fact]
    public void The_query_result_mapping_never_turns_a_failure_into_stopped()
    {
        Assert.Equal(ServiceRunState.Unknown, ScmServiceStateReader.MapQueryResult(false, 1, 3).RunState);
        Assert.Equal(ServiceRunState.Stopped, ScmServiceStateReader.MapQueryResult(true, 1, 3).RunState);
        Assert.Equal(3, ScmServiceStateReader.MapQueryResult(true, 4, 3).StartType);
    }

    [Fact]
    public void Names_that_could_leave_the_services_registry_path_never_reach_the_native_layer()
    {
        var native = new FakeScmNative();
        var reader = new ScmServiceStateReader(native);
        foreach (var name in new[] { @"..\..\SAM", "a/b", string.Empty, "   ", new string('x', 257) })
        {
            Assert.Equal(ServiceInstallation.Unknown, reader.Read(name).Installation);
        }

        Assert.Empty(native.ManagerAccess);
    }
}

[Collection("ETW")]
public class EtwPositiveControlTests
{
    [EtwStartFact]
    public void Our_own_live_session_is_decoded_by_exact_name_and_gone_after_it_is_stopped()
    {
        IReadOnlyList<string> during = [];
        var (start, cleaned, name) = EtwNative.TryOpenAndCloseRealTimeSession(_ => during = EtwNative.QueryAllSessionNames().Names);

        Assert.Equal(EtwNative.ErrorSuccess, start);
        Assert.True(cleaned);
        Assert.Contains(name, during); // positive control: if name decoding broke, this fails instead of the absence check passing vacuously
        Assert.Contains(during, n => n.Length > 0);
        Assert.DoesNotContain(name, EtwNative.QueryAllSessionNames().Names);
    }

    [EtwStartFact]
    public void The_session_is_stopped_even_when_the_hook_throws()
    {
        string? name = null;
        Assert.Throws<InvalidOperationException>(() =>
            EtwNative.TryOpenAndCloseRealTimeSession(n =>
            {
                name = n;
                throw new InvalidOperationException("boom");
            }));

        Assert.NotNull(name);
        Assert.DoesNotContain(name, EtwNative.QueryAllSessionNames().Names);
    }

    [EtwStartFact]
    public void Most_session_names_on_a_real_machine_are_readable()
    {
        var names = EtwNative.QueryAllSessionNames().Names;
        Assert.NotEmpty(names);
        Assert.True(names.Count(n => n.Length > 0) * 2 > names.Count, "more than half of the session names decoded to empty strings");
    }
}

public class OsSupportTests
{
    [Theory]
    [InlineData(26300, "Client", 0, true)]
    [InlineData(22000, "Client", 0, true)]
    [InlineData(19045, "Client", 0, true)]
    [InlineData(19044, "Client", 0, false)]
    [InlineData(26300, "Client", 1, false)]
    [InlineData(26300, "Server", 0, false)]
    [InlineData(20348, "Server Core", 0, false)]
    public void Support_follows_edition_s_mode_and_build(int build, string installationType, int sMode, bool supported)
    {
        var entry = OsProbe.Support(build, installationType, (SModeState)sMode);
        Assert.Equal(supported ? Availability.Available : Availability.NotAvailable, entry.Availability);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_unreadable_installation_type_is_unknown_not_the_primary_target(string? installationType)
    {
        var entry = OsProbe.Support(26300, installationType, SModeState.Off);
        Assert.Equal(Availability.Unknown, entry.Availability);
        Assert.Contains("unreadable", entry.Reason);
    }

    [Fact]
    public void An_unknown_s_mode_state_does_not_by_itself_make_windows_unsupported()
    {
        // S mode Unknown is its own blocker (managed.s_mode); os.support only reports what it can establish.
        Assert.Equal(Availability.Available, OsProbe.Support(26300, "Client", SModeState.Unknown).Availability);
    }
}

public class FormFactorTests
{
    [Fact]
    public void A_battery_alone_does_not_establish_a_form_factor()
    {
        var entry = HardwareProbe.FormFactor([], battery: true);
        Assert.Equal(Availability.Unknown, entry.Availability);
        Assert.Contains("chassis type unreadable", entry.Reason);
        Assert.Contains("battery present", entry.Reason);
    }

    [Fact]
    public void A_known_chassis_gives_a_form_factor_with_the_battery_state()
    {
        var entry = HardwareProbe.FormFactor([10], battery: null);
        Assert.Equal(Availability.Available, entry.Availability);
        Assert.StartsWith("laptop", entry.Value);
        Assert.Contains("battery unknown", entry.Value);
    }
}

public class EtwNameDecodeTests
{
    [Fact]
    public void A_terminated_name_is_read_up_to_the_terminator_and_not_beyond()
    {
        Assert.Equal("Eventlog-Security", EtwNative.DecodeName("Eventlog-Security\0leftover-from-the-next-field".AsSpan()));
    }

    [Fact]
    public void A_slot_without_a_terminator_is_unreadable_instead_of_overrunning_into_the_next_field()
    {
        Assert.Equal(string.Empty, EtwNative.DecodeName("NoTerminatorInThisSlot".AsSpan()));
    }

    [Fact]
    public void Empty_and_immediately_terminated_slots_decode_to_an_empty_name()
    {
        Assert.Equal(string.Empty, EtwNative.DecodeName(ReadOnlySpan<char>.Empty));
        Assert.Equal(string.Empty, EtwNative.DecodeName("\0abc".AsSpan()));
    }
}
