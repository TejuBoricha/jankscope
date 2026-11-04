using System.Collections.ObjectModel;
using Jankscope.Core;

namespace Jankscope.Frames.Detection;

/// <summary>
/// Finds stutters in a frame series by the definition in docs/METRICS.md: a frame is a stutter if its frame time is at least
/// <see cref="StutterOptions.Multiplier"/> times the median frame time of the preceding window AND at least
/// <see cref="StutterOptions.MinExcessMs"/> above that median.
/// </summary>
/// <remarks>
/// <para><b>Window.</b> The window holds the accepted frames whose timestamps lie in
/// <c>[T - RollingWindowMs, T]</c>, where <c>T</c> is the timestamp of the previous accepted frame, i.e. the instant the judged frame
/// began. Anchoring it there (not at the judged frame's own timestamp) means a long freeze cannot empty the window and so hide itself.
/// The judged frame is never part of its own median. Every accepted frame, hitches included, is added to the window afterwards: outliers
/// are never silently dropped, and the median is robust to them.</para>
/// <para><b>Honest states.</b> With fewer than <see cref="StutterOptions.MinHistoryFrames"/> frames in the window a frame gets
/// <see cref="FrameStatus.InsufficientHistory"/> and is counted; it is never reported as "no stutter". Samples with a non-finite, negative
/// or zero frame time, or a non-finite timestamp, are rejected with a reason and counted; nothing throws. A timestamp that goes backwards
/// is counted as a clock discontinuity and restarts the history (the old frames cannot be compared by time any more).</para>
/// <para><b>Comparison.</b> Both conditions are compared with an absolute tolerance of <see cref="ToleranceMs"/> (one picosecond) so that
/// values that are exactly on an edge in decimal (6.1 ms against a 2.1 ms median is exactly +4 ms) are not lost to binary rounding
/// (6.1 - 2.1 is 3.9999999999999996 in IEEE arithmetic). The tolerance is far below the resolution of any frame source.</para>
/// <para><b>Cost.</b> O(log w) per frame for a window of w frames (two indexed heaps), and no allocation per frame once the window has
/// reached its steady size; storing an event appends to a list (amortised). The batch call <see cref="Analyze"/> is the streaming
/// call run over the span, so the two cannot disagree. Not thread-safe: one detector, one thread.</para>
/// </remarks>
public sealed class StutterDetector
{
    /// <summary>Absolute tolerance, in milliseconds, applied to both comparisons of the definition.</summary>
    public const double ToleranceMs = 1e-9;

    private static readonly int ReasonCount = Enum.GetValues<FrameRejectionReason>().Length;

    private readonly SlidingMedian _window = new();
    private readonly List<StutterEvent> _events = [];
    private readonly long[] _rejectedByReason = new long[ReasonCount];
    private readonly double _multiplier;
    private readonly double _minExcessMs;
    private readonly double _windowMs;
    private readonly int _minHistory;

    private long _framesSeen;
    private long _framesEvaluated;
    private long _insufficient;
    private long _rejected;
    private long _discontinuities;
    private bool _haveLast;
    private double _lastTimestampMs;
    private DataProvenance _provenance = DataProvenance.None;

    public StutterDetector(StutterOptions? options = null)
    {
        Options = options ?? StutterOptions.Default;
        Options.Validate();
        _multiplier = Options.Multiplier;
        _minExcessMs = Options.MinExcessMs;
        _windowMs = Options.RollingWindowMs;
        _minHistory = Options.MinHistoryFrames;
    }

    public StutterOptions Options { get; }

    /// <summary>
    /// The one definition of the rule: is a frame of <paramref name="frameTimeMs"/> a stutter against a window median of
    /// <paramref name="medianMs"/>? Used by the detector itself; exposed so tests and tools state the rule in one place.
    /// </summary>
    public static bool IsStutter(double frameTimeMs, double medianMs, StutterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return Meets(frameTimeMs, medianMs, options.Multiplier, options.MinExcessMs);
    }

    /// <summary>Runs the whole series through a fresh detector. Identical to pushing the frames one by one and calling <see cref="GetResult"/>.</summary>
    public static StutterResult Analyze(ReadOnlySpan<FrameSample> frames, StutterOptions? options = null)
    {
        var detector = new StutterDetector(options);
        foreach (var frame in frames)
        {
            detector.Push(frame);
        }

        return detector.GetResult();
    }

    /// <summary>Judges the next frame, in capture order.</summary>
    public FrameVerdict Push(FrameSample sample)
    {
        _framesSeen++;
        _provenance = ProvenanceRules.Combine(_provenance, sample.Provenance); // a frame that is later rejected still says where the series came from
        var frameTime = sample.FrameTimeMs;
        var timestamp = sample.TimestampMs;

        var rejection = Validate(frameTime, timestamp);
        if (rejection != FrameRejectionReason.None)
        {
            _rejected++;
            _rejectedByReason[(int)rejection]++;
            return new FrameVerdict(FrameStatus.Rejected, rejection, double.NaN, double.NaN, double.NaN, null);
        }

        if (_haveLast && timestamp < _lastTimestampMs)
        {
            _discontinuities++;
            _window.Clear();
            _haveLast = false;
        }

        if (_haveLast)
        {
            _window.EvictOlderThan(_lastTimestampMs - _windowMs);
        }

        FrameVerdict verdict;
        if (_window.Count < _minHistory)
        {
            _insufficient++;
            verdict = new FrameVerdict(FrameStatus.InsufficientHistory, FrameRejectionReason.None, double.NaN, double.NaN, double.NaN, null);
        }
        else
        {
            _framesEvaluated++;
            var median = _window.Median();
            var excess = frameTime - median;
            var ratio = frameTime / median;
            if (Meets(frameTime, median, _multiplier, _minExcessMs))
            {
                var stutter = new StutterEvent(sample.Index, timestamp, frameTime, median, excess, ratio, StutterSeverities.Classify(excess));
                _events.Add(stutter);
                verdict = new FrameVerdict(FrameStatus.Stutter, FrameRejectionReason.None, median, excess, ratio, stutter);
            }
            else
            {
                verdict = new FrameVerdict(FrameStatus.NoStutter, FrameRejectionReason.None, median, excess, ratio, null);
            }
        }

        _window.Add(timestamp, frameTime);
        _lastTimestampMs = timestamp;
        _haveLast = true;
        return verdict;
    }

    /// <summary>A snapshot of everything concluded so far. Later pushes do not change a result already returned.</summary>
    public StutterResult GetResult()
    {
        var events = _events.ToArray();
        var rejectedByReason = new Dictionary<FrameRejectionReason, long>();
        foreach (var reason in Enum.GetValues<FrameRejectionReason>())
        {
            var count = _rejectedByReason[(int)reason];
            if (count > 0)
            {
                rejectedByReason[reason] = count;
            }
        }

        return new StutterResult(
            Options,
            Array.AsReadOnly(events),
            StutterClusters.Build(events, Options.ClusterGapMs),
            _framesSeen,
            _framesEvaluated,
            _insufficient,
            _rejected,
            new ReadOnlyDictionary<FrameRejectionReason, long>(rejectedByReason),
            _discontinuities,
            _provenance);
    }

    /// <summary>Forgets every frame and count; the options stay.</summary>
    public void Reset()
    {
        _window.Clear();
        _events.Clear();
        Array.Clear(_rejectedByReason);
        _framesSeen = 0;
        _framesEvaluated = 0;
        _insufficient = 0;
        _rejected = 0;
        _discontinuities = 0;
        _haveLast = false;
        _lastTimestampMs = 0;
        _provenance = DataProvenance.None;
    }

    private static bool Meets(double frameTimeMs, double medianMs, double multiplier, double minExcessMs) =>
        frameTimeMs + ToleranceMs >= multiplier * medianMs
        && (frameTimeMs - medianMs) + ToleranceMs >= minExcessMs;

    private static FrameRejectionReason Validate(double frameTimeMs, double timestampMs)
    {
        if (!double.IsFinite(frameTimeMs))
        {
            return FrameRejectionReason.FrameTimeNotFinite;
        }

        if (frameTimeMs < 0)
        {
            return FrameRejectionReason.FrameTimeNegative;
        }

        if (frameTimeMs == 0)
        {
            return FrameRejectionReason.FrameTimeZero;
        }

        return double.IsFinite(timestampMs) ? FrameRejectionReason.None : FrameRejectionReason.TimestampNotFinite;
    }
}
