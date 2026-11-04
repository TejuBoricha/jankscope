using System.Text.RegularExpressions;

namespace Jankscope.Core.Reports;

/// <summary>
/// Redacts identifying and personal information from incident reports before export (spec §3.6, §7.3).
/// Handles Windows usernames, hostnames, MAC addresses, and IPv4 addresses.
/// </summary>
public static partial class RedactionEngine
{
    [GeneratedRegex(@"\b(?:[0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}\b")]
    private static partial Regex MacRegex();

    [GeneratedRegex(@"\b(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\b")]
    private static partial Regex Ipv4Regex();

    [GeneratedRegex(@"([a-zA-Z]:\\Users\\)([^\\]+)", RegexOptions.IgnoreCase)]
    private static partial Regex UserPathRegex();

    public static string RedactText(string input, string? userName = null, string? hostName = null)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var text = input;

        // User name in path or standalone
        var user = userName ?? Environment.UserName;
        if (!string.IsNullOrWhiteSpace(user) && user.Length > 1)
        {
            text = text.Replace(user, "<USER>", StringComparison.OrdinalIgnoreCase);
        }

        // Generic C:\Users\<name> pattern
        text = UserPathRegex().Replace(text, "$1<USER>");

        // Hostname
        var host = hostName ?? Environment.MachineName;
        if (!string.IsNullOrWhiteSpace(host) && host.Length > 1)
        {
            text = text.Replace(host, "<HOST>", StringComparison.OrdinalIgnoreCase);
        }

        // Network identifiers
        text = MacRegex().Replace(text, "<MAC>");
        text = Ipv4Regex().Replace(text, "<IP>");

        return text;
    }

    /// <summary>
    /// Verifies that none of the planted canary strings survive in the exported text.
    /// Fails closed (throws <see cref="InvalidOperationException"/>) if any canary is detected.
    /// </summary>
    public static void AssertCanariesRedacted(string exportedText, IEnumerable<string> canaries)
    {
        foreach (var canary in canaries)
        {
            if (string.IsNullOrWhiteSpace(canary))
            {
                continue;
            }

            if (exportedText.Contains(canary, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Export redaction failed: canary string '{canary}' survived in exported report!");
            }
        }
    }
}
