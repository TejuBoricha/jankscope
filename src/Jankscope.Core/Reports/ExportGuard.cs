using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jankscope.Core.Reports;

/// <summary>Thrown when text that was meant to be exported still holds something personal. Fail closed: nothing is written. The message names categories, never values.</summary>
public sealed class ExportBlockedException : InvalidOperationException
{
    public ExportBlockedException(IReadOnlyList<RedactionCategory> categories)
        : base($"Export blocked: the text still contains {Describe(categories)} after redaction. Nothing was written.")
    {
        Categories = categories;
    }

    public IReadOnlyList<RedactionCategory> Categories { get; }

    private static string Describe(IReadOnlyList<RedactionCategory> categories) =>
        categories.Count == 0 ? "personal data" : string.Join(", ", categories.Select(c => c.ToString()));
}

/// <summary>
/// The last line of defence before bytes leave the program (spec section 16, canary tests): looks for every known sensitive value and for anything
/// shaped like a MAC address, IP address, e-mail address or profile path, in the text as written, in its percent-decoded form, and in every string of it
/// when it is JSON (so a JSON-escaped path is seen as the path). The guard knows nothing about the redactor: if the redactor misses a place, the guard
/// still finds the value.
/// </summary>
public static class ExportGuard
{
    private static readonly Regex ForeignProfilePath = new(
        @"[A-Za-z]:[\\/]+Users[\\/]+(?!<USER>)[^\\/""'<>|:*?\r\n]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    private static readonly Regex ForeignUncHost = new(
        @"\\\\(?![?.]\\)(?!<)[^\\\s""]+\\",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    /// <summary>Throws <see cref="ExportBlockedException"/> unless <paramref name="text"/> is clean.</summary>
    public static void Verify(string text, RedactionContext context)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(context);

        var found = new HashSet<RedactionCategory>();
        var known = Redactor.KnownValuePatterns(context);
        foreach (var (candidate, uncMeaningful) in Candidates(text))
        {
            Inspect(candidate, uncMeaningful, known, found);
        }

        if (found.Count > 0)
        {
            throw new ExportBlockedException([.. found.OrderBy(c => c)]);
        }
    }

    private static void Inspect(string candidate, bool uncMeaningful, List<(Regex Pattern, RedactionCategory Category)> known, HashSet<RedactionCategory> found)
    {
        // Paths and UNC hosts are judged with the tokens still in place: "C:\Users\<USER>" is fine, "C:\Users\bob" is not.
        if (ForeignProfilePath.IsMatch(candidate))
        {
            found.Add(RedactionCategory.UserName);
        }

        // In the raw text of a JSON document a doubled backslash is one escaped backslash, so "\\Users\" would read as a UNC host; the decoded strings are judged instead.
        if (uncMeaningful && ForeignUncHost.IsMatch(candidate))
        {
            found.Add(RedactionCategory.HostName);
        }

        var bare = Redactor.StripTokens(candidate);
        foreach (var (pattern, category) in known)
        {
            if (pattern.IsMatch(bare))
            {
                found.Add(category);
            }
        }

        if (Redactor.EmailRegex().IsMatch(bare))
        {
            found.Add(RedactionCategory.EmailAddress);
        }

        if (Redactor.MacRegex().IsMatch(bare))
        {
            found.Add(RedactionCategory.MacAddress);
        }

        if (Redactor.Ipv4Regex().IsMatch(bare) || Redactor.Ipv6Regex().IsMatch(bare))
        {
            found.Add(RedactionCategory.IpAddress);
        }
    }

    /// <summary>
    /// The text, its percent-decoded form, and (for JSON) each decoded property name and string value with its percent-decoded form. The flag says
    /// whether a doubled backslash in the candidate is a UNC prefix (true) or possibly just JSON escaping (false).
    /// </summary>
    private static IEnumerable<(string Text, bool UncMeaningful)> Candidates(string text)
    {
        JsonDocument? document = null;
        try
        {
            document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 256 });
        }
        catch (JsonException)
        {
            // not JSON (a bare string in a test, say): the text itself is inspected below
        }

        var isJson = document is not null;
        yield return (text, !isJson);
        var decoded = SafeUnescape(text);
        if (!string.Equals(decoded, text, StringComparison.Ordinal))
        {
            yield return (decoded, !isJson);
        }

        if (document is null)
        {
            yield break;
        }

        using (document)
        {
            foreach (var value in Strings(document.RootElement))
            {
                yield return (value, true);
                var inner = SafeUnescape(value);
                if (!string.Equals(inner, value, StringComparison.Ordinal))
                {
                    yield return (inner, true);
                }
            }
        }
    }

    private static IEnumerable<string> Strings(JsonElement element)
    {
        var stack = new Stack<JsonElement>();
        stack.Push(element);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            switch (current.ValueKind)
            {
                case JsonValueKind.String:
                    yield return current.GetString() ?? string.Empty;
                    break;
                case JsonValueKind.Object:
                    foreach (var property in current.EnumerateObject())
                    {
                        yield return property.Name;
                        stack.Push(property.Value);
                    }

                    break;
                case JsonValueKind.Array:
                    foreach (var item in current.EnumerateArray())
                    {
                        stack.Push(item);
                    }

                    break;
            }
        }
    }

    private static string SafeUnescape(string text)
    {
        try
        {
            return Uri.UnescapeDataString(text);
        }
        catch (UriFormatException)
        {
            return text;
        }
    }
}
