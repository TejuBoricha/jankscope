using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Jankscope.Core;
using Jankscope.Frames.Incidents;

namespace Jankscope.App.Views;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}

/// <summary>
/// What the investigator says about how well the time of a key press is known (review of Stage 1b, finding 3). The figure is the jitter of frame delivery
/// only; the capture tool's own minimum delay cannot be measured here, and with very few frames there is no figure at all (unknown, not zero).
/// </summary>
internal static class KeyPressTimeText
{
    public static string Describe(CaptureTime time) => time.UncertaintyIsMeaningful
        ? $"Key-press time: frame-delivery jitter of about {time.UncertaintyMs.ToString("0", CultureInfo.InvariantCulture)} ms. That is the jitter only; the capture tool's own minimum delay is not measured and is not included."
        : $"Key-press time: only {time.Observations.ToString(CultureInfo.InvariantCulture)} frame(s) had been seen, too few to say how exact it is. The capture tool's own minimum delay is not measured either.";
}

/// <summary>One mark in the list on the investigator page.</summary>
public sealed class MarkItem : ObservableObject
{
    private IncidentMark _mark;
    private string _title = string.Empty;
    private string _subtitle = string.Empty;

    public MarkItem(IncidentMark mark, int number)
    {
        _mark = mark;
        Number = number;
        Update(mark, 0.0, false);
    }

    public int Number { get; }

    public IncidentMark Mark => _mark;

    public string Id => _mark.Id;

    public string Title
    {
        get => _title;
        private set => Set(ref _title, value);
    }

    public string Subtitle
    {
        get => _subtitle;
        private set => Set(ref _subtitle, value);
    }

    /// <param name="postRollSeconds">How much of the after-mark recording exists so far.</param>
    /// <param name="postRollComplete">True once all of it exists.</param>
    public void Update(IncidentMark mark, double postRollSeconds, bool postRollComplete)
    {
        _mark = mark;
        var kind = mark.Kind == MarkKind.Stutter ? "Stutter" : "Input stall";
        Title = $"#{Number.ToString(CultureInfo.InvariantCulture)}  {kind} at {mark.PressedUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)}";

        var parts = new List<string>();
        if (mark.CaptureTime is null)
        {
            parts.Add("no place on the timeline (no frames had arrived)");
        }
        else if (postRollComplete)
        {
            parts.Add("recording complete");
        }
        else
        {
            parts.Add($"recording after the mark: {postRollSeconds.ToString("0", CultureInfo.InvariantCulture)} of {mark.Window.PostRollSeconds.ToString("0", CultureInfo.InvariantCulture)} s");
        }

        parts.Add(mark.Selection is null ? "event not chosen yet" : "event chosen");
        Subtitle = string.Join(" | ", parts);
    }

    /// <summary>What a screen reader announces for the list item (otherwise it reads the type name).</summary>
    public override string ToString() => $"{Title}. {Subtitle}";
}

/// <summary>One candidate anchor in the list.</summary>
public sealed record CandidateItem(int Rank, string Text)
{
    /// <summary>What a screen reader announces for the list item. Without this it read the record itself ("CandidateItem { Rank = 1, Text = ... }").</summary>
    public override string ToString() => Text;

    public static CandidateItem From(CandidateAnchor candidate)
    {
        var when = candidate.SecondsBeforeMark >= 0
            ? $"{candidate.SecondsBeforeMark.ToString("0.0", CultureInfo.InvariantCulture)} s before the key press"
            : "at the key press";
        return new CandidateItem(candidate.Rank, $"{candidate.Rank.ToString(CultureInfo.InvariantCulture)}.  {candidate.Description}  ({when})");
    }
}

/// <summary>One evidence-graded root-cause hypothesis card shown in the Forensic Investigator.</summary>
public sealed record HypothesisCardItem(
    string GradeBadge,
    string Name,
    string Summary,
    string SupportingEvidence,
    string FalsifierText)
{
    /// <summary>What a screen reader announces for the list item (otherwise it reads the record itself).</summary>
    public override string ToString() => $"{GradeBadge}: {Name}. {Summary}";
}

/// <summary>One capability probe entry shown in the System Readiness &amp; Capability Radar.</summary>
public sealed record CapabilityRadarItem(
    string Id,
    string Domain,
    string Title,
    string StatusBadge,
    string ValueOrReason,
    string SourceInfo,
    string PlainEnglishImpact,
    bool IsReady,
    bool NeedsAttention)
{
    /// <summary>What a screen reader announces for the list item (otherwise it reads the record itself).</summary>
    public override string ToString() => $"{Title}: {StatusBadge}. {ValueOrReason}";
}

/// <summary>
/// The words the capability check uses for a row's state, in one place so they can be tested without a screen. A row the program can do nothing about
/// (not available on this PC, not built yet) is not "needs attention": that is kept for what the person could set up. Unknown is never shown as clear.
/// </summary>
internal static class CapabilityText
{
    /// <summary>The badge on a row, in plain words (never the raw enum name such as "NOTIMPLEMENTED").</summary>
    public static string Badge(CapabilityEntry entry) => entry.Availability switch
    {
        Availability.Available => "READY [TIER " + entry.Tier + "]",
        Availability.NeedsElevation => "NEEDS ADMIN",
        Availability.NeedsOptionalTool => "OPTIONAL TOOL",
        Availability.NeedsUserAction => "YOUR ACTION",
        Availability.NotMeasurableBySoftware => "NOT MEASURABLE",
        Availability.NotAvailable => "NOT AVAILABLE",
        Availability.NotImplemented => "NOT BUILT YET",
        _ => "UNKNOWN",
    };

    /// <summary>The value of a ready row, or the stated reason of any other row (the badge already says which state, so the reason is not prefixed with it).</summary>
    public static string Detail(CapabilityEntry entry) => entry.Availability == Availability.Available
        ? (string.IsNullOrWhiteSpace(entry.Value) ? "Available" : entry.Value!)
        : entry.Reason ?? "Not available on this system";

    /// <summary>True for a row the person could act on: an admin right, an optional tool or a setting. Not for "not available" or "not built yet".</summary>
    public static bool NeedsAttention(Availability availability) =>
        availability is Availability.NeedsElevation or Availability.NeedsOptionalTool or Availability.NeedsUserAction;

    /// <summary>The anti-cheat card. The state the program reached is shown as it is; when the state could not be read it says Unknown, never "clear".</summary>
    public static string AntiCheatPosture(CapabilityEntry? entry)
    {
        if (entry is not { Availability: Availability.Available, Value: { Length: > 0 } value })
        {
            return "Unknown";
        }

        // The value reads "<state>; elevated and kernel collectors suppressed" (or "not suppressed"): the state is the part before the semicolon.
        return value.Split(';')[0].Trim() switch
        {
            nameof(AntiCheatSafetyState.Idle) => "No game running",
            nameof(AntiCheatSafetyState.ResidentOnly) => "Resident, idle",
            nameof(AntiCheatSafetyState.GameSession) => "Game session",
            nameof(AntiCheatSafetyState.ProtectedTitle) => "Protected title",
            _ => "Unknown",
        };
    }
}

