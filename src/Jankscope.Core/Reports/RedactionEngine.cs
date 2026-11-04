namespace Jankscope.Core.Reports;

/// <summary>
/// Redacts identifying and personal information from incident reports before export (spec section 3.6, section 7.3).
/// Delegates to <see cref="Redactor"/> so CLI and desktop exports share identical word-boundary, path, email, MAC, IPv4, IPv6, UNC, and OneDrive rules.
/// </summary>
public static class RedactionEngine
{
    public static string RedactText(string input, string? userName = null, string? hostName = null)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var context = userName is null && hostName is null
            ? RedactionContext.ForThisMachine()
            : RedactionContext.Empty
                .With(RedactionCategory.UserName, userName ?? Environment.UserName)
                .With(RedactionCategory.HostName, hostName ?? Environment.MachineName);

        return new Redactor(context).Redact(input) ?? string.Empty;
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
