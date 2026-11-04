using Jankscope.Core;

namespace Jankscope.Platform.Tests;

/// <summary>Regression tests for the re-verification of the pass-2 fixes (docs/reviews/stage-0-pass2-reverify.md, R4, R5).</summary>
public class GateExactValueTests
{
    private static CapabilityEntry Ok(string id, string value) =>
        CapabilityEntry.Available(id, id, CapabilityTier.A, "test", value);

    private static List<CapabilityEntry> Clean() =>
    [
        Ok("os.support", "Windows 11: primary target"),
        Ok("managed.domain_join", ManagedValues.Workgroup),
        Ok("managed.mdm_enrollment", ManagedValues.MdmNone),
        Ok("managed.s_mode", ManagedValues.SModeOff),
        Ok("managed.applied_policies", ManagedValues.PoliciesNone),
        Ok("managed.entra_join", ManagedValues.EntraNotJoined),
        Ok("environment.virtual_machine", ManagedValues.VmNone),
        Ok("game.session", ManagedValues.GameNone),
        Ok("anticheat.state", "Idle; elevated and kernel collectors not suppressed"),
    ];

    private static CapabilityEntry Mode(string id, string value)
    {
        var entries = Clean();
        entries.RemoveAll(e => e.Id == id);
        entries.Add(Ok(id, value));
        return DerivedCapabilities.Compute(entries).Single(e => e.Id == "mode.optimize");
    }

    [Theory]
    [InlineData("environment.virtual_machine", "VM Hyper-V", "virtual-machine state unrecognized")]
    [InlineData("environment.virtual_machine", "virtual machine: ", "virtual-machine state unrecognized")]
    [InlineData("managed.applied_policies", "policies applied: 12 group policy objects", "applied-policy state unrecognized")]
    [InlineData("managed.applied_policies", "None", "applied-policy state unrecognized")]
    [InlineData("managed.entra_join", "Entra joined", "Entra-join state unrecognized")]
    [InlineData("game.session", "a game is running", "game-session state unrecognized")]
    [InlineData("game.session", "game session active: ", "game-session state unrecognized")]
    [InlineData("anticheat.state", "Whatever", "anti-cheat state unrecognized")]
    [InlineData("anticheat.state", "idle; elevated", "anti-cheat state unrecognized")]
    [InlineData("anticheat.state", "Unknown; elevated", "anti-cheat state unrecognized")]
    public void A_value_nobody_recognises_blocks_optimize_mode_for_every_input_row(string id, string value, string expectedBlocker)
    {
        var mode = Mode(id, value);
        Assert.Equal(Availability.NotAvailable, mode.Availability);
        Assert.Contains(expectedBlocker, mode.Reason);
    }

    [Theory]
    [InlineData("managed.applied_policies", "policies applied", "applied-policy state")]
    [InlineData("managed.entra_join", "joined", "Entra-join state")]
    public void A_known_managed_value_blocks_with_exactly_its_label(string id, string value, string label)
    {
        var mode = Mode(id, value);
        Assert.Equal(Availability.NotAvailable, mode.Availability);
        Assert.Contains($"Recommend-only: {label}.", mode.Reason);
    }

    [Theory]
    [InlineData("game.session", "game session active: example.exe")]
    [InlineData("anticheat.state", "ResidentOnly; elevated and kernel collectors not suppressed")]
    [InlineData("anticheat.state", "ProtectedTitle; elevated and kernel collectors suppressed")]
    [InlineData("anticheat.state", "GameSession; elevated and kernel collectors suppressed")]
    public void Known_values_that_do_not_make_the_device_ineligible_leave_optimize_mode_available(string id, string value)
    {
        // An active game or a protected title does not make the device ineligible; per-action rules (Stage 4) decide.
        Assert.Equal(Availability.Available, Mode(id, value).Availability);
    }

    [Theory]
    [InlineData("virtual machine: Hyper-V")]
    [InlineData("virtual machine: an unidentified hypervisor")]
    public void A_detected_virtual_machine_makes_optimize_mode_recommend_only(string value)
    {
        // Review 1a, finding 5: the spec lists "VM, managed or unknown configuration" as Recommend-only; the gate used to treat a VM as clean.
        var mode = Mode("environment.virtual_machine", value);
        Assert.Equal(Availability.NotAvailable, mode.Availability);
        Assert.Contains("Recommend-only: virtual machine detected.", mode.Reason);
        Assert.Equal(Availability.Available, Mode("environment.virtual_machine", ManagedValues.VmNone).Availability); // the physical-machine value stays clean
    }

    [Theory]
    [InlineData(GameSessionStatus.Inactive, null, "Idle")]
    [InlineData(GameSessionStatus.Inactive, "resident", "ResidentOnly")]
    [InlineData(GameSessionStatus.Active, null, "GameSession")]
    public void The_anti_cheat_probes_own_states_are_understood_by_the_gate(GameSessionStatus game, string? service, string expectedState)
    {
        var fake = new AntiCheatCatalogEntry("fake", "Fake AC", ["FakeSvc"], [], "unverified");
        var snapshots = new Dictionary<string, ServiceSnapshot>();
        if (service is not null)
        {
            snapshots["FakeSvc"] = new ServiceSnapshot(ServiceInstallation.Installed, ServiceRunState.Running, 1);
        }

        var rows = new AntiCheatProbe(new CatalogSet([fake], [new ProtectedTitleEntry("t", "T", ["t.exe"], "unverified")]),
            new FakeServiceReader(snapshots), new FakeProcessSource(["explorer"]), new FakeGameSource(game)).Run();
        var state = rows.Single(r => r.Id == "anticheat.state");
        Assert.StartsWith(expectedState, state.Value);
        Assert.True(ManagedValues.IsKnownAntiCheatStateValue(state.Value!));
        Assert.Equal(Availability.Available, Mode("anticheat.state", state.Value!).Availability);
    }
}

public class UndefinedStatusTests
{
    private sealed class OddGameSource(int value) : IGameSessionSource
    {
        public GameSessionStatus Get() => (GameSessionStatus)value;
    }

    [Theory]
    [InlineData(7)]
    [InlineData(-1)]
    public void An_undefined_game_session_status_is_unknown_not_inactive(int value)
    {
        var fake = new AntiCheatCatalogEntry("fake", "Fake AC", ["FakeSvc"], [], "unverified");
        var rows = new AntiCheatProbe(new CatalogSet([fake], [new ProtectedTitleEntry("t", "T", ["t.exe"], "unverified")]),
            new FakeServiceReader(new Dictionary<string, ServiceSnapshot>()), new FakeProcessSource(["explorer"]), new OddGameSource(value)).Run();
        var state = rows.Single(r => r.Id == "anticheat.state");
        Assert.Equal(Availability.Unknown, state.Availability);
        Assert.Contains("game-session status unknown", state.Reason);
    }
}
