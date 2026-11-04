using System.Globalization;

namespace Jankscope.Core;

/// <summary>Pure classification of enumerated ETW session names (spec §14): we only ever stop sessions carrying our own prefix.</summary>
public sealed record EtwSessionSummary(int Total, int Limit, IReadOnlyList<string> OwnPrefixSessions, int ForeignSessions)
{
    public int Headroom => Limit - Total;
}

public static class EtwSessionClassifier
{
    public const int SystemSessionLimit = 64;

    public static EtwSessionSummary Summarize(IReadOnlyCollection<string> sessionNames, string ownPrefix)
    {
        var own = sessionNames
            .Where(n => n.StartsWith(ownPrefix, StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();
        return new EtwSessionSummary(sessionNames.Count, SystemSessionLimit, own, sessionNames.Count - own.Length);
    }

    /// <summary>
    /// Extracts the owning process ID from a session name matching <c>&lt;prefix&gt;&lt;pid&gt;[-&lt;label&gt;]</c>
    /// or <c>&lt;prefix&gt;probe-&lt;pid&gt;-...</c>. Returns false for non-matching or malformed names.
    /// </summary>
    public static bool TryExtractOwnerPid(string? sessionName, string prefix, out int ownerPid)
    {
        ownerPid = 0;
        if (string.IsNullOrEmpty(sessionName) || string.IsNullOrEmpty(prefix) || !sessionName.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var remainder = sessionName.AsSpan(prefix.Length);
        if (remainder.StartsWith("probe-", StringComparison.Ordinal))
        {
            remainder = remainder["probe-".Length..];
        }

        var nextDash = remainder.IndexOf('-');
        var pidSpan = nextDash >= 0 ? remainder[..nextDash] : remainder;

        return int.TryParse(pidSpan, NumberStyles.None, CultureInfo.InvariantCulture, out ownerPid) && ownerPid > 0;
    }
}
