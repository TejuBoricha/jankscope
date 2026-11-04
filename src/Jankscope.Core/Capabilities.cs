using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jankscope.Core;

/// <summary>Spec §3.3 capability tiers.</summary>
public enum CapabilityTier
{
    /// <summary>Works everywhere, unelevated where Windows permits.</summary>
    A,
    /// <summary>Vendor- or hardware-conditional.</summary>
    B,
    /// <summary>Elevated or external tool.</summary>
    C,
}

public enum Availability
{
    Available,
    NeedsElevation,
    NeedsOptionalTool,
    NeedsUserAction,
    NotMeasurableBySoftware,
    NotAvailable,
    NotImplemented,
    Unknown,
}

/// <summary>
/// One row of the runtime capability matrix (spec §6.4/§6.5). Every signal carries its source and, when it is not
/// available, an explicit reason: never a fabricated zero.
/// </summary>
public sealed record CapabilityEntry
{
    public CapabilityEntry(
        string id,
        string title,
        CapabilityTier tier,
        Availability availability,
        string source,
        DataProvenance provenance,
        string? value = null,
        string? reason = null,
        bool isSensitive = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (availability != Availability.Available && string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException($"Entry '{id}' is not available and must state a reason.", nameof(reason));
        }

        if (availability == Availability.Available && string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Entry '{id}' is available and must carry a value.", nameof(value));
        }

        Id = id;
        Title = title;
        Tier = tier;
        Availability = availability;
        Source = source;
        Provenance = provenance;
        Value = value;
        Reason = reason;
        IsSensitive = isSensitive;
    }

    public string Id { get; }
    public string Title { get; }
    public CapabilityTier Tier { get; }
    public Availability Availability { get; }
    public string Source { get; }
    public DataProvenance Provenance { get; }
    public string? Value { get; }
    public string? Reason { get; }
    public bool IsSensitive { get; init; }

    public static CapabilityEntry Available(
        string id, string title, CapabilityTier tier, string source, string value,
        DataProvenance provenance = DataProvenance.Measured, string? reason = null, bool isSensitive = false) =>
        new(id, title, tier, Availability.Available, source, provenance, value, reason, isSensitive);

    public static CapabilityEntry Unavailable(
        string id, string title, CapabilityTier tier, Availability availability, string source, string reason) =>
        new(id, title, tier, availability, source, DataProvenance.None, null, reason);

    public static CapabilityEntry Planned(string id, string title, CapabilityTier tier, string plannedFor) =>
        Unavailable(id, title, tier, Availability.NotImplemented, "not implemented",
            $"Not implemented yet; planned for {plannedFor}.");

    /// <summary>Returns a copy of this entry with sensitive value redacted if <paramref name="redact"/> is true and this entry is sensitive.</summary>
    public CapabilityEntry WithRedaction(bool redact)
    {
        if (!redact || !IsSensitive || Value is null)
        {
            return this;
        }

        return new CapabilityEntry(Id, Title, Tier, Availability, Source, Provenance, "[REDACTED: sensitive]", Reason, IsSensitive);
    }
}

public sealed record ProbeHealth(string ProbeId, bool Succeeded, double DurationMs, string? Error);

public sealed record CapabilityMatrix(
    DateTimeOffset GeneratedUtc,
    string ToolVersion,
    IReadOnlyList<CapabilityEntry> Entries,
    IReadOnlyList<ProbeHealth> ProbeHealth)
{
    public CapabilityMatrix Redacted() =>
        new(GeneratedUtc, ToolVersion, Entries.Select(e => e.WithRedaction(true)).ToList(), ProbeHealth);

    public string ToJson(bool redactSensitive = false) =>
        JsonSerializer.Serialize(redactSensitive ? Redacted() : this, JankscopeJson.Options);
}

public static class JankscopeJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
