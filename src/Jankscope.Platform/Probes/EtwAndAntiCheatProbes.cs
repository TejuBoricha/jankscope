using Jankscope.Core;

namespace Jankscope.Platform;

/// <summary>
/// ETW session census and real-time access test (spec §14, §6.6). Opening a uniquely named real-time session is how
/// unelevated frame capture is really detected; sessions we did not start are only counted, never stopped.
/// </summary>
public sealed class EtwProbe : IProbe
{
    private const string SessionsSource = "QueryAllTracesW (documented)";
    private const string RealtimeSource = "StartTraceW (documented)";
    public string Id => "etw";

    public IReadOnlyList<CapabilityEntry> Run()
    {
        var cleanResult = OrphanSessionCleaner.CleanOrphans();
        var entries = new List<CapabilityEntry>();

        var (status, names) = EtwNative.QueryAllSessionNames();
        entries.AddRange(SessionEntries(status, names, cleanResult));

        var (startStatus, cleaned, sessionName) = EtwNative.TryOpenAndCloseRealTimeSession();
        entries.Add(RealtimeSessionEntry(startStatus, cleaned, sessionName, PrivilegeProbe.IsCurrentProcessElevated()));
        return entries;
    }

    /// <summary>Always returns both rows (spec §6.4): a failed enumeration becomes explicit "unavailable" rows, never silence.</summary>
    internal static IReadOnlyList<CapabilityEntry> SessionEntries(uint status, IReadOnlyList<string> names, OrphanCleanResult? cleanResult = null)
    {
        var orphanTitle = $"ETW sessions carrying the '{Branding.EtwSessionPrefix}' prefix (possible orphans)";
        if (status != EtwNative.ErrorSuccess)
        {
            var reason = status == EtwNative.ErrorNotSupported
                ? "32-bit process: the hand-laid ETW structures are validated for 64-bit processes only"
                : $"enumeration failed with Win32 error {status}";
            return
            [
                CapabilityEntry.Unavailable("etw.sessions", "Running ETW sessions", CapabilityTier.A,
                    status == EtwNative.ErrorAccessDenied ? Availability.NeedsUserAction : Availability.NotAvailable, SessionsSource, reason),
                CapabilityEntry.Unavailable("etw.own_orphans", orphanTitle, CapabilityTier.A, Availability.Unknown, SessionsSource,
                    $"cannot tell: {reason}"),
            ];
        }

        var summary = EtwSessionClassifier.Summarize(names.ToList(), Branding.EtwSessionPrefix);
        string own;
        if (summary.OwnPrefixSessions.Count == 0)
        {
            own = cleanResult is { CleanedSessions.Count: > 0 }
                ? $"none (cleaned {cleanResult.CleanedSessions.Count} orphan session(s) from exited processes: {string.Join(", ", cleanResult.CleanedSessions)})"
                : "none";
        }
        else
        {
            own = $"{summary.OwnPrefixSessions.Count}: {string.Join(", ", summary.OwnPrefixSessions)}. A running second instance also appears here, " +
                  "and no automatic cleanup exists yet";
        }

        return
        [
            CapabilityEntry.Available("etw.sessions", "Running ETW sessions", CapabilityTier.A, SessionsSource,
                $"{summary.Total} of {summary.Limit} system limit; headroom {summary.Headroom}"),
            CapabilityEntry.Available("etw.own_orphans", orphanTitle, CapabilityTier.A, SessionsSource, own),
        ];
    }

    /// <summary>
    /// Maps the result of the start/stop attempt to a row. Success only proves what the current token can do: when this
    /// process is elevated it says nothing about an unelevated process (review #2).
    /// </summary>
    internal static CapabilityEntry RealtimeSessionEntry(uint startStatus, bool cleaned, string sessionName, bool elevated)
    {
        const string Id = "etw.realtime_session";
        const string Title = "Real-time ETW session access";
        switch (startStatus)
        {
            case EtwNative.ErrorSuccess:
                var value = elevated
                    ? "this process is elevated and can create real-time sessions; that does not show whether an unelevated process can"
                    : "this process is not elevated and can create real-time sessions";
                return CapabilityEntry.Available(Id, Title, CapabilityTier.A, RealtimeSource, value,
                    reason: cleaned
                        ? null
                        : $"WARNING: probe session '{sessionName}' started but could not be stopped; it keeps running until it is stopped " +
                          "manually or the machine restarts (no automatic cleanup exists yet).");
            case EtwNative.ErrorAccessDenied when elevated:
                return CapabilityEntry.Unavailable(Id, Title, CapabilityTier.A, Availability.NotAvailable, RealtimeSource,
                    "Access denied even though this process is elevated; the cause was not determined.");
            case EtwNative.ErrorAccessDenied:
                return CapabilityEntry.Unavailable(Id, Title, CapabilityTier.A, Availability.NeedsUserAction, RealtimeSource,
                    "Access denied: needs membership in Performance Log Users (one-time user action) or an elevated helper.");
            case EtwNative.ErrorNotSupported:
                return CapabilityEntry.Unavailable(Id, Title, CapabilityTier.A, Availability.NotAvailable, RealtimeSource,
                    "32-bit process: the hand-laid ETW structures are validated for 64-bit processes only");
            default:
                return CapabilityEntry.Unavailable(Id, Title, CapabilityTier.A, Availability.NotAvailable, RealtimeSource,
                    $"StartTrace failed with Win32 error {startStatus}");
        }
    }
}

/// <summary>
/// Resident anti-cheat detection driven by catalogs/anticheat.json and catalogs/protected_titles.json (HC-1, HC-12).
/// Residency is the <em>actual</em> service state from the Service Control Manager plus a process-name snapshot, not the
/// configured start type. Anything that cannot be determined is Unknown, never "none" and never idle (review #3). The state
/// machine has three inputs (resident anti-cheat, a game session, a protected title); the game-session input comes from
/// <see cref="IGameSessionSource"/> and stays Unknown until a detector exists (review pass 2 #5).
/// </summary>
public sealed class AntiCheatProbe(
    CatalogSet catalogs,
    IServiceStateReader? services = null,
    IProcessNameSource? processes = null,
    IGameSessionSource? gameSession = null) : IProbe
{
    private const string ResidentSource = "Service Control Manager state (SERVICE_QUERY_STATUS) + process-name snapshot, catalog-driven";
    private const string ProtectedSource = "process-name snapshot, catalog-driven";
    private readonly IServiceStateReader _services = services ?? new ScmServiceStateReader();
    private readonly IProcessNameSource _processes = processes ?? new SystemProcessNameSource();
    private readonly IGameSessionSource _gameSession = gameSession ?? new UnimplementedGameSessionSource();

    public string Id => "anticheat";

    private enum Presence
    {
        Resident,
        Undetermined,
        InstalledNotRunning,
        NotPresent,
    }

    public IReadOnlyList<CapabilityEntry> Run()
    {
        var processNames = _processes.GetNames();
        var game = _gameSession.Get();
        if (game is not (GameSessionStatus.Inactive or GameSessionStatus.Active))
        {
            game = GameSessionStatus.Unknown; // an undefined status is not "inactive"
        }

        var antiCheatCatalogLoaded = catalogs.AntiCheat.Count > 0;
        var protectedCatalogLoaded = catalogs.ProtectedTitles.Count > 0;

        var resident = new List<string>();
        var installedNotRunning = new List<string>();
        var undetermined = new List<string>();
        foreach (var entry in catalogs.AntiCheat)
        {
            var (presence, detail) = Evaluate(entry, processNames);
            switch (presence)
            {
                case Presence.Resident:
                    resident.Add(detail);
                    break;
                case Presence.Undetermined:
                    undetermined.Add(detail);
                    break;
                case Presence.InstalledNotRunning:
                    installedNotRunning.Add(detail);
                    break;
            }
        }

        var protectedRunning = processNames is null
            ? []
            : catalogs.ProtectedTitles
                .Where(t => t.ProcessNames.Any(p => processNames.Contains(ProcessNames.Normalize(p))))
                .Select(t => t.DisplayName)
                .ToList();

        // Facts we could not establish. A restrictive state that is already known (protected title, game session) still stands.
        var gaps = new List<string>();
        if (!antiCheatCatalogLoaded)
        {
            gaps.Add("anti-cheat catalog not loaded");
        }

        if (!protectedCatalogLoaded)
        {
            gaps.Add("protected-titles catalog not loaded");
        }

        if (processNames is null)
        {
            gaps.Add("process-name snapshot unavailable");
        }

        if (game == GameSessionStatus.Unknown)
        {
            gaps.Add("game-session status unknown (no game-session detector yet)");
        }

        gaps.AddRange(undetermined.Select(u => $"could not query {u}"));

        var state = AntiCheatSafety.Classify(
            new AntiCheatInputs(resident.Count > 0, game == GameSessionStatus.Active, protectedRunning.Count > 0),
            inputsComplete: gaps.Count == 0);

        return
        [
            ResidentRow(antiCheatCatalogLoaded, catalogs.AntiCheat.Count, resident, undetermined),
            InstalledRow(antiCheatCatalogLoaded, catalogs.AntiCheat.Count, installedNotRunning, undetermined),
            ProtectedRow(protectedCatalogLoaded, catalogs.ProtectedTitles.Count, processNames is not null, protectedRunning),
            StateRow(state, gaps),
        ];
    }

    private (Presence Presence, string Detail) Evaluate(AntiCheatCatalogEntry entry, IReadOnlySet<string>? processNames)
    {
        var snapshots = entry.ServiceNames.Select(name => (Name: name, Snapshot: _services.Read(name))).ToList();
        var helperRunning = processNames is not null && entry.ProcessNames.Any(p => processNames.Contains(ProcessNames.Normalize(p)));

        var running = snapshots.Where(s => s.Snapshot is { Installation: ServiceInstallation.Installed, RunState: ServiceRunState.Running }).ToList();
        if (running.Count > 0 || helperRunning)
        {
            var parts = running.Select(s => $"service {s.Name} running, {s.Snapshot.DescribeStartType()}").ToList();
            if (helperRunning)
            {
                parts.Add("helper process running");
            }

            return (Presence.Resident, $"{entry.DisplayName} ({string.Join("; ", parts)})");
        }

        var unqueried = snapshots.Where(s => s.Snapshot.Installation == ServiceInstallation.Unknown
                                             || (s.Snapshot.Installation == ServiceInstallation.Installed && s.Snapshot.RunState == ServiceRunState.Unknown))
            .Select(s => s.Name).ToList();
        if (unqueried.Count > 0 || (processNames is null && entry.ProcessNames.Count > 0))
        {
            var what = unqueried.Count > 0 ? $"{entry.DisplayName} (service {string.Join(", ", unqueried)})" : $"{entry.DisplayName} (helper processes)";
            return (Presence.Undetermined, what);
        }

        var installed = snapshots.Where(s => s.Snapshot.Installation == ServiceInstallation.Installed).ToList();
        return installed.Count > 0
            ? (Presence.InstalledNotRunning,
                $"{entry.DisplayName} ({string.Join("; ", installed.Select(s => $"service {s.Name} stopped, {s.Snapshot.DescribeStartType()}"))})")
            : (Presence.NotPresent, entry.DisplayName);
    }

    // "none" only ever means "none of the catalogued products": the catalog is not the set of all anti-cheat software, so it is inferred.
    private static CapabilityEntry NoneRow(string id, string title, string source, string what, int count) =>
        CapabilityEntry.Available(id, title, CapabilityTier.A, source, $"none of the {count} catalogued {what}", DataProvenance.Inferred);

    private static CapabilityEntry ResidentRow(bool catalogLoaded, int catalogCount, List<string> resident, List<string> undetermined)
    {
        const string Id = "anticheat.resident";
        const string Title = "Resident anti-cheat (service or driver active, or helper running)";
        if (resident.Count > 0)
        {
            return CapabilityEntry.Available(Id, Title, CapabilityTier.A, ResidentSource, string.Join(", ", resident),
                reason: undetermined.Count > 0 ? $"Also could not check: {string.Join(", ", undetermined)}" : null);
        }

        if (!catalogLoaded)
        {
            return CapabilityEntry.Unavailable(Id, Title, CapabilityTier.A, Availability.Unknown, ResidentSource,
                "anti-cheat catalog not loaded; cannot say whether anti-cheat is resident (not the same as none)");
        }

        return undetermined.Count > 0
            ? CapabilityEntry.Unavailable(Id, Title, CapabilityTier.A, Availability.Unknown, ResidentSource,
                $"none confirmed, but could not check: {string.Join(", ", undetermined)}")
            : NoneRow(Id, Title, ResidentSource, "anti-cheat products", catalogCount);
    }

    private static CapabilityEntry InstalledRow(bool catalogLoaded, int catalogCount, List<string> installedNotRunning, List<string> undetermined)
    {
        const string Id = "anticheat.installed_not_running";
        const string Title = "Installed anti-cheat that is not running now";
        if (!catalogLoaded)
        {
            return CapabilityEntry.Unavailable(Id, Title, CapabilityTier.A, Availability.Unknown, ResidentSource,
                "anti-cheat catalog not loaded; cannot say what is installed");
        }

        if (installedNotRunning.Count == 0 && undetermined.Count > 0)
        {
            return CapabilityEntry.Unavailable(Id, Title, CapabilityTier.A, Availability.Unknown, ResidentSource,
                $"none confirmed, but could not check: {string.Join(", ", undetermined)}");
        }

        return installedNotRunning.Count == 0
            ? NoneRow(Id, Title, ResidentSource, "anti-cheat products", catalogCount)
            : CapabilityEntry.Available(Id, Title, CapabilityTier.A, ResidentSource, string.Join(", ", installedNotRunning),
                reason: undetermined.Count > 0 ? $"Also could not check: {string.Join(", ", undetermined)}" : null);
    }

    private static CapabilityEntry ProtectedRow(bool catalogLoaded, int catalogCount, bool snapshotTaken, List<string> protectedRunning)
    {
        const string Id = "anticheat.protected_running";
        const string Title = "Protected titles running now";
        if (!snapshotTaken)
        {
            return CapabilityEntry.Unavailable(Id, Title, CapabilityTier.A, Availability.Unknown, ProtectedSource,
                "process-name snapshot unavailable; cannot say whether a protected title is running");
        }

        if (!catalogLoaded)
        {
            return CapabilityEntry.Unavailable(Id, Title, CapabilityTier.A, Availability.Unknown, ProtectedSource,
                "protected-titles catalog not loaded; cannot say whether a protected title is running");
        }

        return protectedRunning.Count == 0
            ? NoneRow(Id, Title, ProtectedSource, "protected titles", catalogCount)
            : CapabilityEntry.Available(Id, Title, CapabilityTier.A, ProtectedSource, string.Join(", ", protectedRunning));
    }

    private static CapabilityEntry StateRow(AntiCheatSafetyState state, List<string> gaps)
    {
        const string Id = "anticheat.state";
        const string Title = "Anti-cheat safety state (HC-1)";
        const string Source = "AntiCheatSafety.Classify";

        // Unknown only arises when nothing restrictive is already known: a protected title or game session holds whatever else is unknown.
        if (state == AntiCheatSafetyState.Unknown)
        {
            return CapabilityEntry.Unavailable(Id, Title, CapabilityTier.A, Availability.Unknown, Source,
                $"cannot rule out a game session or protected title ({string.Join("; ", gaps)}). " +
                "Elevated and kernel collectors are treated as suppressed until this is known (HC-1).");
        }

        return CapabilityEntry.Available(Id, Title, CapabilityTier.A, Source,
            $"{state}; elevated and kernel collectors {(AntiCheatSafety.IsSafeModeActive(state) ? "suppressed" : "not suppressed")}",
            DataProvenance.Inferred);
    }
}
