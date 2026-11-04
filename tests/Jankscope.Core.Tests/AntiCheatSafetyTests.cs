namespace Jankscope.Core.Tests;

/// <summary>The HC-1 decision table: the app suppresses only its own restricted collectors, only in game sessions or with a protected title.</summary>
public class AntiCheatSafetyTests
{
    public static TheoryData<bool, bool, bool, AntiCheatSafetyState> States => new()
    {
        { false, false, false, AntiCheatSafetyState.Idle },
        { true, false, false, AntiCheatSafetyState.ResidentOnly },
        { false, true, false, AntiCheatSafetyState.GameSession },
        { true, true, false, AntiCheatSafetyState.GameSession },
        { false, false, true, AntiCheatSafetyState.ProtectedTitle },
        { true, true, true, AntiCheatSafetyState.ProtectedTitle },
    };

    [Theory]
    [MemberData(nameof(States))]
    public void Classify_follows_the_state_table(bool resident, bool session, bool protectedTitle, AntiCheatSafetyState expected)
    {
        Assert.Equal(expected, AntiCheatSafety.Classify(new AntiCheatInputs(resident, session, protectedTitle)));
    }

    [Theory]
    [InlineData(false, false, false, AntiCheatSafetyState.Unknown)]
    [InlineData(true, false, false, AntiCheatSafetyState.Unknown)]
    [InlineData(false, true, false, AntiCheatSafetyState.GameSession)]
    [InlineData(true, true, false, AntiCheatSafetyState.GameSession)]
    [InlineData(false, false, true, AntiCheatSafetyState.ProtectedTitle)]
    [InlineData(true, true, true, AntiCheatSafetyState.ProtectedTitle)]
    public void Incomplete_inputs_never_yield_a_claim_that_is_less_restrictive_than_what_is_known(bool resident, bool session, bool protectedTitle, AntiCheatSafetyState expected)
    {
        Assert.Equal(expected, AntiCheatSafety.Classify(new AntiCheatInputs(resident, session, protectedTitle), inputsComplete: false));
    }

    [Fact]
    public void Unknown_is_treated_like_safe_mode_and_numbering_of_the_documented_states_is_stable()
    {
        Assert.True(AntiCheatSafety.IsSafeModeActive(AntiCheatSafetyState.Unknown));
        Assert.False(AntiCheatSafety.IsSafeModeActive(AntiCheatSafetyState.Idle));
        Assert.False(AntiCheatSafety.IsSafeModeActive(AntiCheatSafetyState.ResidentOnly));
        Assert.Equal([0, 1, 2, 3, 4], Enum.GetValues<AntiCheatSafetyState>().Select(s => (int)s).Order().ToArray());
    }

    [Theory]
    [MemberData(nameof(AllCollectors))]
    public void Unknown_state_suppresses_exactly_the_restricted_collectors_and_no_override_applies(CollectorClass collector)
    {
        foreach (var overridden in new[] { false, true })
        {
            var decision = AntiCheatSafety.Decide(collector, AntiCheatSafetyState.Unknown, overridden);
            Assert.Equal(LiteralRestrictedSet.Contains(collector), decision.Suppressed);
            Assert.Equal(decision.Suppressed ? AntiCheatSafety.UnknownReason : null, decision.Reason);
        }
    }

    private static readonly HashSet<CollectorClass> LiteralRestrictedSet =
    [
        CollectorClass.ElevatedHelper, CollectorClass.KernelEtw, CollectorClass.Wpr,
        CollectorClass.UsbAnalyticTrace, CollectorClass.DeepProfile, CollectorClass.ForeignProcessHandle,
    ];

    [Fact]
    public void Restricted_set_is_exactly_the_hc1_list()
    {
        var actual = Enum.GetValues<CollectorClass>().Where(AntiCheatSafety.IsRestrictedClass).ToHashSet();
        Assert.True(actual.SetEquals(LiteralRestrictedSet), string.Join(",", actual));
    }

    public static TheoryData<CollectorClass> AllCollectors()
    {
        var data = new TheoryData<CollectorClass>();
        foreach (var c in Enum.GetValues<CollectorClass>())
        {
            data.Add(c);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllCollectors))]
    public void Resident_only_and_idle_suppress_nothing(CollectorClass collector)
    {
        Assert.False(AntiCheatSafety.Decide(collector, AntiCheatSafetyState.Idle, false).Suppressed);
        Assert.False(AntiCheatSafety.Decide(collector, AntiCheatSafetyState.ResidentOnly, false).Suppressed);
    }

    [Theory]
    [MemberData(nameof(AllCollectors))]
    public void In_game_sessions_and_protected_titles_only_restricted_collectors_are_suppressed(CollectorClass collector)
    {
        foreach (var state in new[] { AntiCheatSafetyState.GameSession, AntiCheatSafetyState.ProtectedTitle })
        {
            var decision = AntiCheatSafety.Decide(collector, state, userOverridePerGame: false);
            // A literal expectation, not IsRestrictedClass(): removing a collector from the restricted set must fail this test.
            Assert.Equal(LiteralRestrictedSet.Contains(collector), decision.Suppressed);
            Assert.Equal(decision.Suppressed ? AntiCheatSafety.SuppressedReason : null, decision.Reason);
        }
    }

    [Fact]
    public void Allow_list_is_never_suppressed()
    {
        CollectorClass[] allowed =
        [
            CollectorClass.UnelevatedUserModeEtw, CollectorClass.Pdh, CollectorClass.WmiCim,
            CollectorClass.EventLog, CollectorClass.RawInputTiming, CollectorClass.NetworkProbe,
        ];
        Assert.All(allowed, c => Assert.False(AntiCheatSafety.Decide(c, AntiCheatSafetyState.ProtectedTitle, false).Suppressed));
    }

    [Fact]
    public void Per_game_override_re_enables_restricted_collectors_except_foreign_process_handles()
    {
        Assert.False(AntiCheatSafety.Decide(CollectorClass.ElevatedHelper, AntiCheatSafetyState.GameSession, true).Suppressed);
        Assert.False(AntiCheatSafety.Decide(CollectorClass.KernelEtw, AntiCheatSafetyState.ProtectedTitle, true).Suppressed);
        Assert.True(AntiCheatSafety.Decide(CollectorClass.ForeignProcessHandle, AntiCheatSafetyState.ProtectedTitle, true).Suppressed);
    }
}
