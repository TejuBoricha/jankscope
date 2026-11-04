namespace Jankscope.Core;

/// <summary>Machine state for HC-1's context-aware anti-cheat handling (spec HC-1, §2.2 #8).</summary>
public enum AntiCheatSafetyState
{
    /// <summary>No game session and no protected process.</summary>
    Idle = 0,
    /// <summary>An anti-cheat service or driver is resident (e.g. loaded at boot) but no game session is active. Nothing is suppressed.</summary>
    ResidentOnly = 1,
    /// <summary>A game session is active (consented list or sustained full-screen presenter).</summary>
    GameSession = 2,
    /// <summary>A catalogued protected-title process is running.</summary>
    ProtectedTitle = 3,
    /// <summary>
    /// The state cannot be determined (an input could not be read or a detector does not exist yet). Idle is a claim, Unknown is not
    /// one: it is treated like safe mode and fails closed (HC-1).
    /// </summary>
    Unknown = 4,
}

public readonly record struct AntiCheatInputs(bool ResidentAntiCheatPresent, bool GameSessionActive, bool ProtectedProcessRunning);

/// <summary>Classes of collector for suppression decisions. The app suppresses only its own collectors.</summary>
public enum CollectorClass
{
    // HC-1 allow list: never suppressed.
    UnelevatedUserModeEtw,
    Pdh,
    WmiCim,
    EventLog,
    RawInputTiming,
    NetworkProbe,
    // Suppressed while anti-cheat-safe mode is active.
    ElevatedHelper,
    KernelEtw,
    Wpr,
    UsbAnalyticTrace,
    DeepProfile,
    /// <summary>Any handle to a foreign process beyond PROCESS_QUERY_LIMITED_INFORMATION. Never overridable.</summary>
    ForeignProcessHandle,
}

public sealed record SuppressionDecision(bool Suppressed, string? Reason);

public static class AntiCheatSafety
{
    public const string SuppressedReason = "suppressed (game session / protected title)";
    public const string UnknownReason = "suppressed (anti-cheat state unknown)";

    /// <param name="inputsComplete">
    /// False when any input could not be established. A state that is not already restrictive then becomes Unknown instead of a
    /// claim such as Idle; a restrictive state (game session, protected title) stands whatever else is unknown.
    /// </param>
    public static AntiCheatSafetyState Classify(AntiCheatInputs inputs, bool inputsComplete = true)
    {
        var state = inputs.ProtectedProcessRunning ? AntiCheatSafetyState.ProtectedTitle
            : inputs.GameSessionActive ? AntiCheatSafetyState.GameSession
            : inputs.ResidentAntiCheatPresent ? AntiCheatSafetyState.ResidentOnly
            : AntiCheatSafetyState.Idle;
        return !inputsComplete && !IsSafeModeActive(state) ? AntiCheatSafetyState.Unknown : state;
    }

    /// <summary>
    /// Everything except the two states that positively mean "nothing to protect" is restrictive, so an undefined value (a corrupt or
    /// newer-version number) fails closed instead of reading as idle.
    /// </summary>
    public static bool IsSafeModeActive(AntiCheatSafetyState state) =>
        state is not (AntiCheatSafetyState.Idle or AntiCheatSafetyState.ResidentOnly);

    public static bool IsRestrictedClass(CollectorClass collector) => collector is
        CollectorClass.ElevatedHelper or CollectorClass.KernelEtw or CollectorClass.Wpr or
        CollectorClass.UsbAnalyticTrace or CollectorClass.DeepProfile or CollectorClass.ForeignProcessHandle;

    /// <param name="userOverridePerGame">The user accepted the plain-language warning for this game (HC-1).</param>
    public static SuppressionDecision Decide(CollectorClass collector, AntiCheatSafetyState state, bool userOverridePerGame)
    {
        if (!IsSafeModeActive(state) || !IsRestrictedClass(collector))
        {
            return new SuppressionDecision(false, null);
        }

        // A per-game override says "I accept this for this game"; it cannot apply when we do not know whether a game is running.
        if (userOverridePerGame && collector != CollectorClass.ForeignProcessHandle && state != AntiCheatSafetyState.Unknown)
        {
            return new SuppressionDecision(false, null);
        }

        return new SuppressionDecision(true, state == AntiCheatSafetyState.Unknown ? UnknownReason : SuppressedReason);
    }
}
