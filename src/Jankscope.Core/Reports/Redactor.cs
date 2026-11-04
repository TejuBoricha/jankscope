using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Jankscope.Core.Reports;

/// <summary>
/// Replaces personal values in text and in whole JSON documents, and counts what it replaced per category (spec section 3.6, 16).
/// Not thread-safe: one instance per export.
/// <para>
/// Known values (the context) are matched case-insensitively. A value of <see cref="SubstringMinLength"/> characters or more is matched anywhere in a
/// string, because a distinctive name embedded in a longer word is still the name; a shorter value is matched as a whole word only (letters and
/// digits are word characters), because a short name such as "al" or "mark" occurs inside ordinary words and inside this tool's own field names.
/// Known limitation: a short name glued to other letters or digits ("markgames") is not caught by that rule; profile paths are cut by pattern whatever the name.
/// </para>
/// </summary>
public sealed partial class Redactor
{
    /// <summary>Known values at least this long are replaced as substrings; shorter ones as whole words.</summary>
    public const int SubstringMinLength = 6;

    private readonly List<(Regex Pattern, RedactionCategory Category)> _known;
    private readonly Dictionary<RedactionCategory, int> _counts = Enum.GetValues<RedactionCategory>().ToDictionary(c => c, _ => 0);

    public Redactor(RedactionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _known = KnownValuePatterns(context);
    }

    /// <summary>How many values were replaced so far, per category. Every category is present (zero when none).</summary>
    public IReadOnlyDictionary<RedactionCategory, int> Counts => _counts;

    internal static List<(Regex Pattern, RedactionCategory Category)> KnownValuePatterns(RedactionContext context) =>
        context.Values
            .OrderByDescending(v => v.Value.Length) // longer first: a host name that contains the user name is replaced whole
            .Select(v => (KnownValuePattern(v.Value), v.Category))
            .ToList();

    internal static Regex KnownValuePattern(string value)
    {
        var escaped = Regex.Escape(value);
        var pattern = value.Length >= SubstringMinLength ? escaped : $@"(?<![\p{{L}}\p{{N}}]){escaped}(?![\p{{L}}\p{{N}}])";
        return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
    }

    /// <summary>Redacts one string. Null stays null.</summary>
    public string? Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var result = text;

        // An e-mail address goes first: its local part is often the user name, and the whole address is the identifier.
        result = EmailRegex().Replace(result, _ => Count(RedactionCategory.EmailAddress));

        foreach (var (pattern, category) in _known)
        {
            result = pattern.Replace(result, _ => Count(category));
        }

        result = ProfilePathRegex().Replace(result, m => m.Groups[1].Value + Count(RedactionCategory.UserName));
        result = OneDriveRegex().Replace(result, m => m.Groups[1].Value + Count(RedactionCategory.OrganizationName));
        result = UncHostRegex().Replace(result, m => @"\\" + Count(RedactionCategory.HostName) + @"\");

        result = MacRegex().Replace(result, _ => Count(RedactionCategory.MacAddress));
        result = Ipv6Regex().Replace(result, _ => Count(RedactionCategory.IpAddress));
        result = Ipv4Regex().Replace(result, _ => Count(RedactionCategory.IpAddress));
        return result;
    }

    /// <summary>
    /// Reduces a command line to the program's file name (no directory, no arguments: arguments carry tokens, paths and user names).
    /// "&lt;ARGS&gt;" marks that arguments existed.
    /// </summary>
    public string? RedactCommandLine(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return commandLine;
        }

        var text = commandLine.Trim();
        string program;
        string rest;
        if (text[0] == '"')
        {
            var close = text.IndexOf('"', 1);
            program = close < 0 ? text[1..] : text[1..close];
            rest = close < 0 ? string.Empty : text[(close + 1)..];
        }
        else
        {
            var space = text.IndexOf(' ');
            program = space < 0 ? text : text[..space];
            rest = space < 0 ? string.Empty : text[space..];
        }

        var name = program[(program.LastIndexOfAny(['\\', '/']) + 1)..];
        var safeName = Redact(name) ?? string.Empty;
        if (rest.Trim().Length == 0)
        {
            return safeName;
        }

        return safeName + " " + Count(RedactionCategory.CommandLine);
    }

    /// <summary>
    /// Redacts every string value and every property name in a JSON tree, whatever the field. A property whose name says it holds a command
    /// line is cut to the program name. Numbers, booleans and nulls are copied unchanged.
    /// </summary>
    public JsonNode? RedactTree(JsonNode? node)
    {
        switch (node)
        {
            case null:
                return null;
            case JsonObject obj:
            {
                var copy = new JsonObject();
                foreach (var (name, value) in obj)
                {
                    var newName = Redact(name) ?? string.Empty;
                    var unique = newName;
                    for (var n = 2; copy.ContainsKey(unique); n++)
                    {
                        unique = newName + "#" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }

                    copy[unique] = IsCommandLineName(name) && value is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.String
                        ? JsonValue.Create(RedactCommandLine(v.GetValue<string>()))
                        : RedactTree(value);
                }

                return copy;
            }

            case JsonArray array:
            {
                var copy = new JsonArray();
                foreach (var item in array)
                {
                    copy.Add(RedactTree(item));
                }

                return copy;
            }

            case JsonValue value when value.GetValueKind() == System.Text.Json.JsonValueKind.String:
                return JsonValue.Create(Redact(value.GetValue<string>()));
            default:
                return node.DeepClone();
        }
    }

    private static bool IsCommandLineName(string name) => CommandLineNameRegex().IsMatch(name);

    private string Count(RedactionCategory category)
    {
        _counts[category]++;
        return RedactionTokens.For(category);
    }

    // ---- patterns shared with the export guard ----

    [GeneratedRegex(@"[\w.+\-]+@[\w\-]+(?:\.[\w\-]+)+", RegexOptions.CultureInvariant)]
    internal static partial Regex EmailRegex();

    [GeneratedRegex(@"\b(?:[0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}\b")]
    internal static partial Regex MacRegex();

    [GeneratedRegex(@"\b(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\b")]
    internal static partial Regex Ipv4Regex();

    // Full form (eight groups), or compressed with "::". A bare "12:34:56" (a time) has neither and is never an address.
    [GeneratedRegex(
        @"(?<![0-9A-Za-z:])(?:(?:[0-9A-Fa-f]{1,4}:){7}[0-9A-Fa-f]{1,4}|(?:[0-9A-Fa-f]{1,4}:){1,7}:(?:[0-9A-Fa-f]{1,4}(?::[0-9A-Fa-f]{1,4}){0,6})?|::(?:[0-9A-Fa-f]{1,4}:){0,6}[0-9A-Fa-f]{1,4})(?![0-9A-Za-z:])")]
    internal static partial Regex Ipv6Regex();

    // C:\Users\<name>, c:/users/<name>, \\?\C:\Users\<name>. A folder name may contain spaces. The class excludes '<', so an existing token is never replaced twice.
    [GeneratedRegex(@"([A-Za-z]:[\\/]+Users[\\/]+)([^\\/""'<>|:*?\r\n]+)", RegexOptions.IgnoreCase)]
    internal static partial Regex ProfilePathRegex();

    [GeneratedRegex(@"(OneDrive - )([^\\/\r\n""<]+)", RegexOptions.IgnoreCase)]
    internal static partial Regex OneDriveRegex();

    // \\host\share, but not the \\?\ or \\.\ device prefixes and not an existing token.
    [GeneratedRegex(@"\\\\(?![?.]\\)(?!<)[^\\\s""]+\\")]
    internal static partial Regex UncHostRegex();

    [GeneratedRegex(@"command.?line|cmd.?line", RegexOptions.IgnoreCase)]
    private static partial Regex CommandLineNameRegex();

    internal static string StripTokens(string text)
    {
        var builder = new StringBuilder(text);
        foreach (var token in RedactionTokens.All)
        {
            builder.Replace(token, " ");
        }

        return builder.ToString();
    }
}
