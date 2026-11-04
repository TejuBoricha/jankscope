using Jankscope.Core;

namespace Jankscope.Platform;

/// <summary>Outcome of inspecting and cleaning orphaned trace sessions.</summary>
public sealed record OrphanCleanResult(
    IReadOnlyList<string> CleanedSessions,
    IReadOnlyList<string> FailedSessions,
    IReadOnlyList<string> ActiveOwnerSessions);

/// <summary>
/// Discovers and removes orphaned ETW sessions carrying the Jankscope prefix whose creator process has died (spec §14, LIMITATIONS L23).
/// Safe by design:
/// 1. Only sessions matching <see cref="Branding.EtwSessionPrefix"/> are ever considered.
/// 2. If the PID encoded in the session name is the current process, it is preserved.
/// 3. If the PID is currently running according to <see cref="IProcessLiveness"/>, it is preserved.
/// 4. Only confirmed dead-owner sessions are ended via <see cref="EtwNative.TryStopSessionByName"/>.
/// </summary>
public static class OrphanSessionCleaner
{
    public static OrphanCleanResult CleanOrphans(IProcessLiveness? liveness = null, int? currentProcessId = null)
    {
        var pid = currentProcessId ?? Environment.ProcessId;
        liveness ??= new SystemProcessNameSource();

        var (status, allSessions) = EtwNative.QueryAllSessionNames();
        if (status != EtwNative.ErrorSuccess || allSessions.Count == 0)
        {
            return new OrphanCleanResult([], [], []);
        }

        var cleaned = new List<string>();
        var failed = new List<string>();
        var active = new List<string>();

        foreach (var session in allSessions)
        {
            if (string.IsNullOrEmpty(session) || !session.StartsWith(Branding.EtwSessionPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (!EtwSessionClassifier.TryExtractOwnerPid(session, Branding.EtwSessionPrefix, out var ownerPid))
            {
                // Prefix matches but name does not carry a clean PID; keep safe, do not touch
                continue;
            }

            if (ownerPid == pid || liveness.IsAlive(ownerPid))
            {
                active.Add(session);
                continue;
            }

            var stopStatus = EtwNative.TryStopSessionByName(session);
            if (stopStatus == EtwNative.ErrorSuccess)
            {
                cleaned.Add(session);
            }
            else
            {
                failed.Add(session);
            }
        }

        return new OrphanCleanResult(cleaned, failed, active);
    }
}
