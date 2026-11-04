using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
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
}

/// <summary>One candidate anchor in the list.</summary>
public sealed record CandidateItem(int Rank, string Text)
{
    public static CandidateItem From(CandidateAnchor candidate)
    {
        var when = candidate.SecondsBeforeMark >= 0
            ? $"{candidate.SecondsBeforeMark.ToString("0.0", CultureInfo.InvariantCulture)} s before the key press"
            : "at the key press";
        return new CandidateItem(candidate.Rank, $"{candidate.Rank.ToString(CultureInfo.InvariantCulture)}.  {candidate.Description}  ({when})");
    }
}
