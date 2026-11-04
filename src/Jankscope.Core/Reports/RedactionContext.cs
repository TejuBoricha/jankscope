namespace Jankscope.Core.Reports;

/// <summary>What kind of personal or identifying value a piece of text is (spec section 16: redaction ON by default).</summary>
public enum RedactionCategory
{
    UserName = 0,
    HostName = 1,
    WifiSsid = 2,
    Bssid = 3,
    MacAddress = 4,
    IpAddress = 5,
    DeviceSerial = 6,
    CommandLine = 7,
    EmailAddress = 8,
    OrganizationName = 9,
}

public static class RedactionTokens
{
    /// <summary>The placeholder written in place of a value. The first four are older than this version and are pinned by golden files.</summary>
    public static string For(RedactionCategory category) => category switch
    {
        RedactionCategory.UserName => "<USER>",
        RedactionCategory.HostName => "<HOST>",
        RedactionCategory.WifiSsid => "<SSID>",
        RedactionCategory.Bssid => "<BSSID>",
        RedactionCategory.MacAddress => "<MAC>",
        RedactionCategory.IpAddress => "<IP>",
        RedactionCategory.DeviceSerial => "<SERIAL>",
        RedactionCategory.CommandLine => "<ARGS>",
        RedactionCategory.EmailAddress => "<EMAIL>",
        RedactionCategory.OrganizationName => "<ORG>",
        _ => "<REDACTED>",
    };

    /// <summary>Every placeholder this code can write, longest first. The guard removes them before it looks for a sensitive value, so a user named "user" does not trip on <c>&lt;USER&gt;</c>.</summary>
    public static IReadOnlyList<string> All { get; } =
        Enum.GetValues<RedactionCategory>().Select(For).Append("<REDACTED>").Distinct().OrderByDescending(t => t.Length).ToArray();
}

/// <summary>A value that must never appear in an export, with the category it is reported under.</summary>
public readonly record struct SensitiveValue(RedactionCategory Category, string Value);

/// <summary>
/// The values this machine and this session know to be personal: the Windows user name, the profile folder, the host name, and (as collectors
/// arrive) the Wi-Fi network, device serials and so on. Immutable; <see cref="With"/> returns a new context. Planted test values (canaries) go
/// in the same way.
/// </summary>
public sealed class RedactionContext
{
    private readonly List<SensitiveValue> _values;

    private RedactionContext(List<SensitiveValue> values)
    {
        _values = values;
    }

    public static RedactionContext Empty { get; } = new([]);

    public IReadOnlyList<SensitiveValue> Values => _values;

    /// <summary>Adds a value. Blank values are ignored and a value already present (any case, same category) is not added twice.</summary>
    public RedactionContext With(RedactionCategory category, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return this;
        }

        var trimmed = value.Trim();
        if (_values.Any(v => v.Category == category && string.Equals(v.Value, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            return this;
        }

        return new RedactionContext([.. _values, new SensitiveValue(category, trimmed)]);
    }

    /// <summary>The user name, the profile folder name (it can differ from the user name), the domain when it is not just the host, and the host name.</summary>
    public static RedactionContext ForThisMachine()
    {
        var context = Empty
            .With(RedactionCategory.UserName, Environment.UserName)
            .With(RedactionCategory.HostName, Environment.MachineName);

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(profile))
        {
            context = context.With(RedactionCategory.UserName, Path.GetFileName(profile.TrimEnd('\\', '/')));
        }

        var domain = Environment.UserDomainName;
        if (!string.IsNullOrWhiteSpace(domain) && !string.Equals(domain, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
        {
            context = context.With(RedactionCategory.HostName, domain);
        }

        return context;
    }
}
