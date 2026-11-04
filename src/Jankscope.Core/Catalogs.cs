using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jankscope.Core.Hardware;

namespace Jankscope.Core;

/// <summary>Verification level of a catalog entry's claim (spec §3.4).</summary>
public static class CatalogVerification
{
    public static readonly IReadOnlySet<string> Allowed =
        new HashSet<string>(StringComparer.Ordinal) { "observed", "documented", "community", "unverified" };
}

public sealed record CatalogIssue(string Catalog, string EntryId, string Message)
{
    public override string ToString() => $"{Catalog}:{EntryId}: {Message}";
}

/// <summary>
/// Schema validation for the data-only knowledge catalogs (spec §3.4, acceptance #18): data only, every entry has a
/// source and a review date, no unknown properties (so no smuggled commands or scripts).
/// </summary>
public static partial class CatalogValidator
{
    private static readonly string[] CommonProps = ["id", "displayName", "source", "lastReviewed", "verification", "notes"];

    private static readonly Dictionary<string, string[]> KindProps = new(StringComparer.Ordinal)
    {
        ["anticheat"] = ["serviceNames", "processNames"],
        ["protected_titles"] = ["processNames"],
        ["vendor_terms"] = ["terms"],
        ["vm_markers"] = ["manufacturerMarkers", "modelMarkers", "biosMarkers"],
    };

    // A marker is matched as a substring of firmware strings; a very short one would match almost any of them and turn every machine into a VM.
    private const int MinVmMarkerLength = 3;

    private static readonly string[] TopLevelProps = ["schemaVersion", "catalog", "description", "entries"];

    // \A ... \z, not ^ ... $: in .NET '$' also matches before a trailing newline, which would accept "demo\n".
    [GeneratedRegex(@"\A[a-z0-9_]+\z")]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"\A[A-Za-z0-9_.\- ]+\z")]
    private static partial Regex NamePattern();

    /// <param name="json">The catalog text.</param>
    /// <param name="expectedKind">When given, the file must declare exactly this catalog kind (a vendor_terms file saved as anticheat.json must not load).</param>
    public static IReadOnlyList<CatalogIssue> Validate(string json, string? expectedKind = null)
    {
        var issues = new List<CatalogIssue>();
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return [new CatalogIssue("?", "-", $"invalid JSON: {ex.GetType().Name}")];
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return [new CatalogIssue("?", "-", "root must be an object")];
            }

            foreach (var p in root.EnumerateObject().Where(p => !TopLevelProps.Contains(p.Name)))
            {
                issues.Add(new CatalogIssue("?", "-", $"unknown top-level property '{p.Name}'"));
            }

            var kind = root.TryGetProperty("catalog", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString()! : "?";
            if (!KindProps.TryGetValue(kind, out var extra))
            {
                issues.Add(new CatalogIssue(kind, "-", "unknown or missing 'catalog' kind"));
                return issues;
            }

            if (expectedKind is not null && kind != expectedKind)
            {
                issues.Add(new CatalogIssue(kind, "-", $"this file must declare the '{expectedKind}' catalog kind"));
                return issues;
            }

            if (!root.TryGetProperty("schemaVersion", out var sv) || sv.ValueKind != JsonValueKind.Number ||
                !sv.TryGetInt32(out var version) || version != 1)
            {
                issues.Add(new CatalogIssue(kind, "-", "schemaVersion must be the integer 1"));
            }

            if (root.TryGetProperty("description", out var description))
            {
                FreeText(description, "description", 1000, required: false, message => issues.Add(new CatalogIssue(kind, "-", message)));
            }

            if (!root.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
            {
                issues.Add(new CatalogIssue(kind, "-", "'entries' must be an array"));
                return issues;
            }

            if (entries.GetArrayLength() == 0)
            {
                // An empty catalog would make every probe report "none found": a silent fail-open.
                issues.Add(new CatalogIssue(kind, "-", "'entries' must not be empty"));
                return issues;
            }

            var allowed = CommonProps.Concat(extra).ToHashSet(StringComparer.Ordinal);
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in entries.EnumerateArray())
            {
                ValidateEntry(kind, entry, allowed, extra, seenIds, issues);
            }
        }

        return issues;
    }

    private static void ValidateEntry(
        string kind, JsonElement entry, HashSet<string> allowed, string[] extra, HashSet<string> seenIds, List<CatalogIssue> issues)
    {
        if (entry.ValueKind != JsonValueKind.Object)
        {
            issues.Add(new CatalogIssue(kind, "-", "entry must be an object"));
            return;
        }

        var id = entry.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String ? idEl.GetString()! : "?";
        void Add(string message) => issues.Add(new CatalogIssue(kind, id, message));

        foreach (var p in entry.EnumerateObject().Where(p => !allowed.Contains(p.Name)))
        {
            Add($"unknown property '{p.Name}' (catalogs are data-only)");
        }

        if (id == "?" || !IdPattern().IsMatch(id))
        {
            Add("id must match [a-z0-9_]+");
        }
        else if (!seenIds.Add(id))
        {
            Add("duplicate id");
        }

        FreeTextProperty(entry, "displayName", 120, required: true, Add);
        FreeTextProperty(entry, "source", 400, required: true, Add);
        FreeTextProperty(entry, "notes", 1000, required: false, Add);

        if (!entry.TryGetProperty("lastReviewed", out var lr) || lr.ValueKind != JsonValueKind.String ||
            !DateOnly.TryParseExact(lr.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            Add("lastReviewed must be an ISO date (yyyy-MM-dd)");
        }

        if (!entry.TryGetProperty("verification", out var v) || v.ValueKind != JsonValueKind.String ||
            !CatalogVerification.Allowed.Contains(v.GetString()!))
        {
            Add($"verification must be one of: {string.Join(", ", CatalogVerification.Allowed.Order())}");
        }

        var hasAnyName = false;
        foreach (var arrayProp in extra)
        {
            if (!entry.TryGetProperty(arrayProp, out var arr))
            {
                continue;
            }

            if (arr.ValueKind != JsonValueKind.Array)
            {
                Add($"'{arrayProp}' must be an array of strings");
                continue;
            }

            foreach (var item in arr.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()) ||
                    !NamePattern().IsMatch(item.GetString()!))
                {
                    Add($"'{arrayProp}' items must be plain names (letters, digits, . _ - space)");
                }
                else
                {
                    hasAnyName = true;
                    if (kind == "vm_markers" && item.GetString()!.Trim().Length < MinVmMarkerLength)
                    {
                        Add($"'{arrayProp}' items must be at least {MinVmMarkerLength} characters (a shorter marker would match almost any firmware string)");
                    }
                }
            }
        }

        if (!hasAnyName)
        {
            Add($"at least one of: {string.Join(", ", extra)} is required");
        }
    }

    private static void FreeTextProperty(JsonElement entry, string name, int maxLength, bool required, Action<string> add)
    {
        if (!entry.TryGetProperty(name, out var el))
        {
            if (required)
            {
                add($"'{name}' is required");
            }

            return;
        }

        FreeText(el, name, maxLength, required, add);
    }

    /// <summary>Free text must be a bounded string without control characters (they would reach the console as escape sequences).</summary>
    private static void FreeText(JsonElement el, string name, int maxLength, bool required, Action<string> add)
    {
        if (el.ValueKind != JsonValueKind.String)
        {
            add($"'{name}' must be a string");
            return;
        }

        var text = el.GetString()!;
        if (required && string.IsNullOrWhiteSpace(text))
        {
            add($"'{name}' is required");
        }

        if (text.Length > maxLength)
        {
            add($"'{name}' is longer than {maxLength} characters");
        }

        if (text.Any(char.IsControl))
        {
            add($"'{name}' must not contain control characters");
        }
    }
}

public sealed record AntiCheatCatalogEntry(string Id, string DisplayName, IReadOnlyList<string> ServiceNames, IReadOnlyList<string> ProcessNames, string Verification);

public sealed record ProtectedTitleEntry(string Id, string DisplayName, IReadOnlyList<string> ProcessNames, string Verification);

public sealed record CatalogSet(
    IReadOnlyList<AntiCheatCatalogEntry> AntiCheat,
    IReadOnlyList<ProtectedTitleEntry> ProtectedTitles)
{
    public static readonly CatalogSet Empty = new([], []);

    /// <summary>Firmware-string markers of virtual machines (catalogs/vm_markers.json). Empty means "no catalog loaded", which the VM probe reports as unknown.</summary>
    public IReadOnlyList<VmMarkerEntry> VmMarkers { get; init; } = [];

    /// <summary>Loads catalogs from <paramref name="directory"/>, validating each file first. Invalid catalogs throw: fail closed.</summary>
    public static CatalogSet Load(string directory)
    {
        var anticheat = ReadEntries(Path.Combine(directory, "anticheat.json"), "anticheat",
            e => new AntiCheatCatalogEntry(Str(e, "id"), Str(e, "displayName"), Strings(e, "serviceNames"), Strings(e, "processNames"), Str(e, "verification")));
        var titles = ReadEntries(Path.Combine(directory, "protected_titles.json"), "protected_titles",
            e => new ProtectedTitleEntry(Str(e, "id"), Str(e, "displayName"), Strings(e, "processNames"), Str(e, "verification")));
        var vmMarkers = ReadEntries(Path.Combine(directory, "vm_markers.json"), "vm_markers",
            e => new VmMarkerEntry(Str(e, "id"), Str(e, "displayName"), Strings(e, "manufacturerMarkers"), Strings(e, "modelMarkers"), Strings(e, "biosMarkers"), Str(e, "verification")));
        return new CatalogSet(anticheat, titles) { VmMarkers = vmMarkers };
    }

    /// <summary>Finds the catalogs folder next to the executable, or by walking up to the repository root.</summary>
    public static string? FindDefaultDirectory(string startDirectory)
    {
        for (var dir = new DirectoryInfo(startDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "catalogs");
            if (File.Exists(Path.Combine(candidate, "anticheat.json")))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Every failure surfaces as <see cref="InvalidDataException"/> so callers have one fail-closed path.</summary>
    private static List<T> ReadEntries<T>(string path, string kind, Func<JsonElement, T> map)
    {
        try
        {
            var json = File.ReadAllText(path);
            var issues = CatalogValidator.Validate(json, kind);
            if (issues.Count > 0)
            {
                throw new InvalidDataException($"Catalog '{kind}' is invalid: {string.Join("; ", issues)}");
            }

            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("entries").EnumerateArray().Select(map).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException($"Catalog '{kind}' could not be loaded ({ex.GetType().Name}).", ex);
        }
    }

    private static string Str(JsonElement e, string name) => e.GetProperty(name).GetString()!;

    private static string[] Strings(JsonElement e, string name) =>
        e.TryGetProperty(name, out var a) ? a.EnumerateArray().Select(x => x.GetString()!).ToArray() : [];
}
