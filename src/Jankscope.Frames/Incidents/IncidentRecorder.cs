using Jankscope.Core;
using Jankscope.Frames.Detection;
using Jankscope.Frames.Ring;

namespace Jankscope.Frames.Incidents;

public enum MarkKind
{
    /// <summary>"It just stuttered": a frame-pacing complaint.</summary>
    Stutter = 0,

    /// <summary>"The input just froze": recorded now, diagnosed from Stage 2 (Raw Input timing). The mark is keyboard-triggered because the mouse may be what froze.</summary>
    InputStall = 1,
}

/// <param name="StartMs">Start of the incident window on the capture clock; null when the clock was not known.</param>
/// <param name="EndMs">End of the window (mark time plus post-roll).</param>
/// <param name="RequestedPreRollSeconds">Pre-roll asked for.</param>
/// <param name="AvailablePreRollSeconds">Pre-roll actually held when the mark was made (less than requested near the start of a capture or after a restart).</param>
/// <param name="PostRollSeconds">Post-roll asked for.</param>
public sealed record MarkWindow(
    double? StartMs,
    double? EndMs,
    double RequestedPreRollSeconds,
    double AvailablePreRollSeconds,
    double PostRollSeconds)
{
    /// <summary>True when less than the requested pre-roll exists. Stated in the export; the window is never padded.</summary>
    public bool PreRollShort => AvailablePreRollSeconds < RequestedPreRollSeconds - 1.0;
}

/// <summary>
/// One press of a mark hotkey (or the tray menu). <see cref="Selection"/> is null until the person chooses where the event was; the key press is never
/// used as the event time unless the person picks it (spec section 7.3).
/// </summary>
public sealed record IncidentMark(
    string Id,
    MarkKind Kind,
    DateTimeOffset PressedUtc,
    long PressedTicks,
    CaptureTime? CaptureTime,
    AnchorSearchResult Search,
    AnchorSelection? Selection,
    string? Note,
    MarkWindow Window,
    FrameSeries PreRoll,
    string? TargetProcess);

/// <param name="Frames">The pre-roll frozen at the mark plus the post-roll collected so far.</param>
/// <param name="PostRollComplete">False while the post-roll is still being collected, or when capture ended before it finished.</param>
/// <param name="PostRollCollectedSeconds">How much post-roll is in <paramref name="Frames"/>.</param>
public sealed record CollectedWindow(FrameSeries Frames, bool PostRollComplete, double PostRollCollectedSeconds);

/// <param name="PreRollSeconds">Default 120; 30 to 600 (spec section 7.3).</param>
/// <param name="PostRollSeconds">Default 30.</param>
/// <param name="DebounceMs">A second press of the same mark within this time is the same press (a held key, a double tap).</param>
/// <param name="MaxMarks">Marks kept in memory (each holds its frozen pre-roll); the oldest is dropped beyond this.</param>
public sealed record IncidentRecorderOptions(
    double PreRollSeconds = 120.0,
    double PostRollSeconds = 30.0,
    double DebounceMs = 1000.0,
    int MaxMarks = 10,
    AnchorSearchOptions? Search = null)
{
    public const int MaxNoteLength = 2000;

    public static IncidentRecorderOptions Default { get; } = new();

    public void Validate()
    {
        if (!double.IsFinite(PreRollSeconds) || PreRollSeconds is < 30 or > 600)
        {
            throw new ArgumentOutOfRangeException(nameof(PreRollSeconds), PreRollSeconds, "must be between 30 and 600 seconds");
        }

        if (!double.IsFinite(PostRollSeconds) || PostRollSeconds is < 0 or > 300)
        {
            throw new ArgumentOutOfRangeException(nameof(PostRollSeconds), PostRollSeconds, "must be between 0 and 300 seconds");
        }

        if (!double.IsFinite(DebounceMs) || DebounceMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(DebounceMs), DebounceMs, "must be finite and not negative");
        }

        if (MaxMarks < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxMarks), MaxMarks, "must be at least 1");
        }

        Search?.Validate();
    }

    /// <summary>
    /// How many frames the history ring should hold: the pre-roll plus the post-roll plus a margin, at an assumed top frame rate, capped so that
    /// a very long pre-roll cannot make the history unreasonably large. A cap that bites shows up as a short pre-roll on the mark, never silently.
    /// </summary>
    public int RecommendedHistoryCapacity(double marginSeconds = 30.0, double assumedMaxFps = 360.0, int hardCap = 131_072) =>
        (int)Math.Clamp((PreRollSeconds + PostRollSeconds + marginSeconds) * assumedMaxFps, 1024.0, hardCap);
}

/// <summary>
/// Turns a hotkey press into a mark: finds when it was on the capture clock, offers candidate anchors from the seconds before it, freezes the
/// pre-roll, and later collects the post-roll. Thread-safe. <see cref="Mark"/> copies the history and so takes milliseconds: call it from a worker,
/// not from the thread that must keep a window responsive.
/// </summary>
public sealed class IncidentRecorder
{
    private readonly RingBuffer<FrameSample> _history;
    private readonly CaptureClockMap _clock;
    private readonly TimeProvider _time;
    private readonly IncidentRecorderOptions _options;
    private readonly StutterOptions _stutter;
    private readonly object _gate = new();
    private readonly List<IncidentMark> _marks = [];
    private readonly Dictionary<MarkKind, long> _lastPress = [];
    private int _counter;
    private long _debounced;

    public IncidentRecorder(
        RingBuffer<FrameSample> history,
        CaptureClockMap clock,
        TimeProvider? timeProvider = null,
        IncidentRecorderOptions? options = null,
        StutterOptions? stutterOptions = null)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(clock);
        _options = options ?? IncidentRecorderOptions.Default;
        _options.Validate();
        _history = history;
        _clock = clock;
        _time = timeProvider ?? TimeProvider.System;
        _stutter = stutterOptions ?? StutterOptions.Default;
    }

    /// <summary>Raised after a mark is added or changed, on the thread that made the change.</summary>
    public event EventHandler<IncidentMark>? MarkChanged;

    /// <summary>Presses ignored because the same mark was pressed again within the debounce time.</summary>
    public long DebouncedPresses => Interlocked.Read(ref _debounced);

    /// <summary>The marks held, oldest first.</summary>
    public IReadOnlyList<IncidentMark> Marks
    {
        get
        {
            lock (_gate)
            {
                return [.. _marks];
            }
        }
    }

    public IncidentRecorderOptions Options => _options;

    /// <summary>Makes a mark for a press happening now. Null when it is the same press as one a moment ago.</summary>
    public IncidentMark? Mark(MarkKind kind)
    {
        var ticks = _time.GetTimestamp();
        var utc = _time.GetUtcNow();
        int number;
        lock (_gate)
        {
            if (_lastPress.TryGetValue(kind, out var last) && _time.GetElapsedTime(last, ticks).TotalMilliseconds < _options.DebounceMs)
            {
                Interlocked.Increment(ref _debounced);
                return null;
            }

            _lastPress[kind] = ticks;
            number = ++_counter;
        }

        var snapshot = _history.Snapshot();
        var captureTime = _clock.Estimate(ticks);
        var search = CandidateAnchorFinder.Find(snapshot, captureTime, _options.Search, _stutter);

        var preRollMs = _options.PreRollSeconds * 1000.0;
        var postRollMs = _options.PostRollSeconds * 1000.0;
        FrameSeries preRoll;
        MarkWindow window;
        if (captureTime is { } c)
        {
            var span = FrameRingExtensions.SelectWindow(snapshot, c.Ms + c.UncertaintyMs, preRollMs + c.UncertaintyMs);
            preRoll = FrameSeries.FromSamples(span);
            var available = span.Length == 0 ? 0.0 : Math.Max(0.0, (c.Ms - span[0].TimestampMs) / 1000.0);
            window = new MarkWindow(c.Ms - preRollMs, c.Ms + postRollMs, _options.PreRollSeconds, Math.Min(available, _options.PreRollSeconds), _options.PostRollSeconds);
        }
        else
        {
            preRoll = FrameSeries.Empty;
            window = new MarkWindow(null, null, _options.PreRollSeconds, 0.0, _options.PostRollSeconds);
        }

        var mark = new IncidentMark(
            Id: "mark-" + number.ToString("D3", System.Globalization.CultureInfo.InvariantCulture),
            Kind: kind,
            PressedUtc: utc,
            PressedTicks: ticks,
            CaptureTime: captureTime,
            Search: search,
            Selection: null,
            Note: null,
            Window: window,
            PreRoll: preRoll,
            TargetProcess: preRoll.Application ?? (snapshot.Length > 0 ? snapshot[^1].Application : null));

        lock (_gate)
        {
            _marks.Add(mark);
            while (_marks.Count > _options.MaxMarks)
            {
                _marks.RemoveAt(0);
            }
        }

        MarkChanged?.Invoke(this, mark);
        return mark;
    }

    /// <summary>The person picked one of the offered candidates by its rank.</summary>
    public IncidentMark SelectCandidate(string id, int rank)
    {
        var mark = Get(id);
        var candidate = mark.Search.Candidates.FirstOrDefault(c => c.Rank == rank)
            ?? throw new ArgumentOutOfRangeException(nameof(rank), rank, "no such candidate");
        return Replace(mark with { Selection = AnchorSelection.FromCandidate(candidate) });
    }

    /// <summary>The person chose the moment the key was pressed as the event time (a labelled choice, never a default).</summary>
    public IncidentMark SelectKeyPressTime(string id)
    {
        var mark = Get(id);
        var time = mark.CaptureTime ?? throw new InvalidOperationException("The capture clock was not known when the key was pressed, so the press has no place on the timeline.");
        return Replace(mark with { Selection = AnchorSelection.FromKeyPress(time) });
    }

    /// <summary>The person placed the event at a time of their own choosing (a click on the timeline). It must lie inside the mark's window.</summary>
    public IncidentMark SelectAdjustedTime(string id, double captureMs)
    {
        var mark = Get(id);
        if (mark.Window.StartMs is not { } start || mark.Window.EndMs is not { } end || !double.IsFinite(captureMs) || captureMs < start || captureMs > end)
        {
            throw new ArgumentOutOfRangeException(nameof(captureMs), captureMs, "the time must lie inside the mark's window");
        }

        return Replace(mark with { Selection = AnchorSelection.FromAdjusted(captureMs) });
    }

    public IncidentMark ClearSelection(string id) => Replace(Get(id) with { Selection = null });

    /// <summary>Sets the free-text note (trimmed; blank clears it; longer than <see cref="IncidentRecorderOptions.MaxNoteLength"/> is refused).</summary>
    public IncidentMark SetNote(string id, string? note)
    {
        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmed is { Length: > IncidentRecorderOptions.MaxNoteLength })
        {
            throw new ArgumentOutOfRangeException(nameof(note), "the note is longer than " + IncidentRecorderOptions.MaxNoteLength + " characters");
        }

        return Replace(Get(id) with { Note = trimmed });
    }

    /// <summary>The mark's frozen pre-roll followed by the post-roll the ring holds now (frames after the mark up to the end of the window).</summary>
    public CollectedWindow CollectWindow(IncidentMark mark)
    {
        ArgumentNullException.ThrowIfNull(mark);
        if (mark.Window.EndMs is not { } end || mark.CaptureTime is not { } time)
        {
            return new CollectedWindow(mark.PreRoll, false, 0.0);
        }

        var snapshot = _history.Snapshot();
        var frames = mark.PreRoll.ContinueWith(snapshot, end);
        var complete = snapshot.Length > 0 && snapshot[^1].TimestampMs >= end;
        var collected = frames.LastMs is { } last ? Math.Clamp((last - time.Ms) / 1000.0, 0.0, mark.Window.PostRollSeconds) : 0.0;
        return new CollectedWindow(frames, complete, collected);
    }

    private IncidentMark Get(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        lock (_gate)
        {
            return _marks.FirstOrDefault(m => m.Id == id) ?? throw new KeyNotFoundException("no mark with that id (it may have been dropped)");
        }
    }

    private IncidentMark Replace(IncidentMark updated)
    {
        lock (_gate)
        {
            var index = _marks.FindIndex(m => m.Id == updated.Id);
            if (index < 0)
            {
                throw new KeyNotFoundException("the mark was dropped while it was being changed");
            }

            _marks[index] = updated;
        }

        MarkChanged?.Invoke(this, updated);
        return updated;
    }
}
