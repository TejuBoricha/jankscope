using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Jankscope.Core.Reports;

/// <summary>
/// How strictly a known value (the person's own user name, PC name and so on) is looked for inside a string. A name that is also an ordinary word
/// ("al", "mark") must not corrupt this tool's own text, but a person's name glued to other characters ("markgames") must not survive in text the person
/// typed, so the rule depends on where the string sits.
/// </summary>
public enum KnownValueMode
{
    /// <summary>A JSON property name (this tool's own fixed schema): substrings only from <see cref="Redactor.SubstringMinLength"/> characters, else whole words.</summary>
    Name = 0,

    /// <summary>A string value: substrings from <see cref="Redactor.ValueSubstringMinLength"/> characters, shorter names as whole words.</summary>
    Value = 1,

    /// <summary>Text the person typed or a program name (<see cref="Redactor.IsFreeTextName"/>): substrings from <see cref="Redactor.FreeTextSubstringMinLength"/> characters.</summary>
    FreeText = 2,
}

/// <summary>
/// Replaces personal values in text and in whole JSON documents, and counts what it replaced per category (spec section 3.6, 16).
/// Not thread-safe: one instance per export.
/// <para>
/// Known values (the context) are matched case-insensitively, after Unicode normalisation (NFC), so a name typed with a combining accent matches the
/// same name written as one character. How a name is matched inside a string depends on its <see cref="KnownValueMode"/>: in the person's own free text
/// (the note, the program name) a name of two characters or more is matched anywhere in the string; in this tool's own values from four characters; in
/// property names from six characters. Below that a name is matched as a whole word only (letters and digits are word characters), because a short
/// name such as "al" or "mark" occurs inside ordinary words and inside this tool's own field names. A name written in a script without word
/// boundaries (Chinese, Japanese, Korean, Thai) is always matched as a substring, because "whole word" has no meaning there.
/// Known limitation: a name of one or two characters outside free text, or of two or three characters in this tool's own values, that is glued to other
/// letters is not caught; profile paths are cut by pattern whatever the name.
/// </para>
/// </summary>
public sealed partial class Redactor
{
    /// <summary>In a property name, known values at least this long are replaced as substrings; shorter ones as whole words.</summary>
    public const int SubstringMinLength = 6;

    /// <summary>In a string value, known values at least this long are replaced as substrings; shorter ones as whole words.</summary>
    public const int ValueSubstringMinLength = 4;

    /// <summary>In text the person typed, known values at least this long are replaced as substrings; a single character as a whole word.</summary>
    public const int FreeTextSubstringMinLength = 2;

    // Property names whose values are typed by the person or name a program: matched strictly. This tool's own prose is not in this list, so a person
    // named "ian" does not turn "median" into "med<USER>" in every export.
    private static readonly HashSet<string> FreeTextNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "note", "notes", "comment", "comments", "targetProcess", "process", "processName", "program", "programName", "application",
    };

    private readonly RedactionContext _context;
    private readonly Dictionary<KnownValueMode, List<(Regex Pattern, RedactionCategory Category)>> _known = [];
    private readonly Dictionary<RedactionCategory, int> _counts = Enum.GetValues<RedactionCategory>().ToDictionary(c => c, _ => 0);

    public Redactor(RedactionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <summary>How many values were replaced so far, per category. Every category is present (zero when none).</summary>
    public IReadOnlyDictionary<RedactionCategory, int> Counts => _counts;

    /// <summary>True for a property name whose string values are text the person typed (or a program name) and so are matched in <see cref="KnownValueMode.FreeText"/> mode.</summary>
    internal static bool IsFreeTextName(string name) => FreeTextNames.Contains(name);

    internal static List<(Regex Pattern, RedactionCategory Category)> KnownValuePatterns(RedactionContext context, KnownValueMode mode = KnownValueMode.Value) =>
        context.Values
            .OrderByDescending(v => v.Value.Length) // longer first: a host name that contains the user name is replaced whole
            .Select(v => (KnownValuePattern(v.Value, mode), v.Category))
            .ToList();

    internal static Regex KnownValuePattern(string value, KnownValueMode mode = KnownValueMode.Value)
    {
        var escaped = Regex.Escape(value);
        var minLength = mode switch
        {
            KnownValueMode.Name => SubstringMinLength,
            KnownValueMode.FreeText => FreeTextSubstringMinLength,
            _ => ValueSubstringMinLength,
        };
        var substring = value.Length >= minLength || (value.Length >= 2 && UnsegmentedScriptRegex().IsMatch(value));
        var pattern = substring ? escaped : $@"(?<![\p{{L}}\p{{N}}]){escaped}(?![\p{{L}}\p{{N}}])";
        return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
    }

    /// <summary>The text in Unicode normal form C (a name typed with a combining accent equals the same name as one character). Text that cannot be normalised is returned unchanged.</summary>
    internal static string Nfc(string text)
    {
        try
        {
            return text.IsNormalized(NormalizationForm.FormC) ? text : text.Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            return text;
        }
    }

    /// <summary>Redacts one string. Null stays null.</summary>
    public string? Redact(string? text, KnownValueMode mode = KnownValueMode.Value)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var result = Nfc(text);

        // An e-mail address goes first: its local part is often the user name, and the whole address is the identifier.
        result = EmailRegex().Replace(result, _ => Count(RedactionCategory.EmailAddress));

        // Structured path, OneDrive and UNC patterns run before known values so a user/host segment that starts with a known
        // name (for example "C:\Users\alice_01\...") is replaced as one whole segment rather than leaving a partial suffix behind.
        result = ProfilePathRegex().Replace(result, m => m.Groups[1].Value + Count(RedactionCategory.UserName));
        result = OneDriveRegex().Replace(result, m => m.Groups[1].Value + Count(RedactionCategory.OrganizationName));
        result = UncHostRegex().Replace(result, m => @"\\" + Count(RedactionCategory.HostName) + @"\");

        foreach (var (pattern, category) in Known(mode))
        {
            result = pattern.Replace(result, _ => Count(category));
        }

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
    public JsonNode? RedactTree(JsonNode? node) => RedactTree(node, freeText: false);

    private JsonNode? RedactTree(JsonNode? node, bool freeText)
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
                    var newName = Redact(name, KnownValueMode.Name) ?? string.Empty;
                    var unique = newName;
                    for (var n = 2; copy.ContainsKey(unique); n++)
                    {
                        unique = newName + "#" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }

                    copy[unique] = IsCommandLineName(name) && value is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.String
                        ? JsonValue.Create(RedactCommandLine(v.GetValue<string>()))
                        : RedactTree(value, freeText || IsFreeTextName(name));
                }

                return copy;
            }

            case JsonArray array:
            {
                var copy = new JsonArray();
                foreach (var item in array)
                {
                    copy.Add(RedactTree(item, freeText));
                }

                return copy;
            }

            case JsonValue value when value.GetValueKind() == System.Text.Json.JsonValueKind.String:
                return JsonValue.Create(Redact(value.GetValue<string>(), freeText ? KnownValueMode.FreeText : KnownValueMode.Value));
            default:
                return node.DeepClone();
        }
    }

    private List<(Regex Pattern, RedactionCategory Category)> Known(KnownValueMode mode)
    {
        if (!_known.TryGetValue(mode, out var list))
        {
            list = KnownValuePatterns(_context, mode);
            _known[mode] = list;
        }

        return list;
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

    // A MAC address in the three common notations: aa:bb:cc:dd:ee:ff (or with '-'), aabb.ccdd.eeff, and twelve hex digits in a row (which must contain a
    // letter, so a plain 12-digit number is left alone, and must not be the tail of a longer hex run or of a GUID). The boundaries are "not next to another
    // hex digit" rather than \b, so "mac=AA:BB:CC:DD:EE:FF_x" and "xAA:BB:..." are still found.
    [GeneratedRegex(
        @"(?<![0-9A-Fa-f])(?:[0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}(?![0-9A-Fa-f])|(?<![0-9A-Fa-f.])(?:[0-9A-Fa-f]{4}\.){2}[0-9A-Fa-f]{4}(?![0-9A-Fa-f.])|(?<![0-9A-Fa-f-])(?=[0-9]*[A-Fa-f])[0-9A-Fa-f]{12}(?![0-9A-Fa-f-])")]
    internal static partial Regex MacRegex();

    // The boundaries are "not next to another digit", so "ip=192.0.2.5_x" and "x192.0.2.5" are found (a \b would miss them: letters, digits and
    // underscores are word characters) while a four-digit last part ("31.0.101.5186") is still not an address.
    [GeneratedRegex(@"(?<![0-9])(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)(?![0-9])")]
    internal static partial Regex Ipv4Regex();

    // Chinese, Japanese, Korean and Thai text has no spaces between words, so a name in these scripts cannot be matched as a "whole word".
    [GeneratedRegex(@"[\p{IsCJKUnifiedIdeographs}\p{IsCJKUnifiedIdeographsExtensionA}\p{IsHiragana}\p{IsKatakana}\p{IsHangulSyllables}\p{IsThai}]")]
    private static partial Regex UnsegmentedScriptRegex();

    // Full form (eight groups, or six groups + IPv4), or compressed with "::" (including IPv4-mapped tails and optional %zone).
    // A bare "12:34:56" (a time) has neither and is never an address.
    [GeneratedRegex(
        @"(?<![0-9A-Za-z:])(?:(?:[0-9A-Fa-f]{1,4}:){6}(?:[0-9A-Fa-f]{1,4}:[0-9A-Fa-f]{1,4}|(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?))|(?:[0-9A-Fa-f]{1,4}:){1,7}:(?:(?:[0-9A-Fa-f]{1,4}:){0,5}(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)|[0-9A-Fa-f]{1,4}(?::[0-9A-Fa-f]{1,4}){0,6})?|::(?:(?:[0-9A-Fa-f]{1,4}:){0,5}(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)|(?:[0-9A-Fa-f]{1,4}:){0,6}[0-9A-Fa-f]{1,4}))(?:%[0-9A-Za-z_]+)?(?![0-9A-Za-z:])")]
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
