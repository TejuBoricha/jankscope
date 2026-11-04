using Jankscope.Core;

namespace Jankscope.Frames.Incidents;

/// <summary>
/// A compact, immutable run of frames: index, capture time and frame time per frame (about 20 bytes a frame against about 100 for a
/// <see cref="FrameSample"/>), so that every mark can keep its own frozen pre-roll without the memory cost of a copy of the ring.
/// Frame times are held as single-precision numbers (a resolution far below a microsecond for any real frame time).
/// </summary>
public sealed class FrameSeries
{
    private readonly long[] _indexes;
    private readonly double[] _timestampsMs;
    private readonly float[] _frameTimesMs;

    private FrameSeries(long[] indexes, double[] timestampsMs, float[] frameTimesMs, string? application, DataProvenance provenance)
    {
        _indexes = indexes;
        _timestampsMs = timestampsMs;
        _frameTimesMs = frameTimesMs;
        Application = application;
        Provenance = provenance;
    }

    public static FrameSeries Empty { get; } = new([], [], [], null, DataProvenance.None);

    public int Count => _timestampsMs.Length;

    public ReadOnlySpan<long> Indexes => _indexes;

    public ReadOnlySpan<double> TimestampsMs => _timestampsMs;

    public ReadOnlySpan<float> FrameTimesMs => _frameTimesMs;

    /// <summary>The program the frames came from (the first frame that named one), or null.</summary>
    public string? Application { get; }

    /// <summary>The weakest provenance of any frame (HC-9): a series that holds one simulated frame is simulated.</summary>
    public DataProvenance Provenance { get; }

    public double? FirstMs => Count == 0 ? null : _timestampsMs[0];

    public double? LastMs => Count == 0 ? null : _timestampsMs[^1];

    public static FrameSeries FromSamples(ReadOnlySpan<FrameSample> samples)
    {
        if (samples.Length == 0)
        {
            return Empty;
        }

        var indexes = new long[samples.Length];
        var timestamps = new double[samples.Length];
        var frameTimes = new float[samples.Length];
        string? application = null;
        var provenance = DataProvenance.None;
        for (var i = 0; i < samples.Length; i++)
        {
            indexes[i] = samples[i].Index;
            timestamps[i] = samples[i].TimestampMs;
            frameTimes[i] = (float)samples[i].FrameTimeMs;
            application ??= samples[i].Application;
            provenance = ProvenanceRules.Combine(provenance, samples[i].Provenance);
        }

        return new FrameSeries(indexes, timestamps, frameTimes, application, provenance);
    }

    public FrameSample[] ToSamples()
    {
        var samples = new FrameSample[Count];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = new FrameSample(_indexes[i], _timestampsMs[i], _frameTimesMs[i], Application: Application, Provenance: Provenance);
        }

        return samples;
    }

    /// <summary>
    /// This series followed by the frames of <paramref name="later"/> that come after its last frame in time. Frames of <paramref name="later"/> that
    /// do not continue the series (the same frames again, or a clock that restarted) are left out, so a frame is never counted twice.
    /// </summary>
    public FrameSeries ContinueWith(ReadOnlySpan<FrameSample> later, double endMs) =>
        ContinueWith(later, endMs, out _);

    /// <summary>
    /// This series followed by the frames of <paramref name="later"/> that continue the same capture run up to <paramref name="endMs"/>.
    /// Sets <paramref name="reachedEnd"/> when that same continuous run reached <paramref name="endMs"/> without restarting.
    /// </summary>
    public FrameSeries ContinueWith(ReadOnlySpan<FrameSample> later, double endMs, out bool reachedEnd)
    {
        var lastMs = LastMs ?? double.NegativeInfinity;
        reachedEnd = lastMs >= endMs;
        if (later.IsEmpty)
        {
            return this;
        }

        var startIndex = FindContinuationStart(later, out var canContinue);
        if (!canContinue)
        {
            return this;
        }

        var extra = new List<FrameSample>();
        var previousMs = lastMs;
        for (var i = startIndex; i < later.Length; i++)
        {
            var sample = later[i];
            if (sample.TimestampMs < previousMs)
            {
                break;
            }

            if (Application is not null
                && sample.Application is not null
                && !string.Equals(Application, sample.Application, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            previousMs = sample.TimestampMs;
            if (sample.TimestampMs >= endMs)
            {
                reachedEnd = true;
            }

            if (sample.TimestampMs > lastMs && sample.TimestampMs <= endMs)
            {
                extra.Add(sample);
                lastMs = sample.TimestampMs;
            }
            else if (sample.TimestampMs > endMs)
            {
                break;
            }
        }

        if (extra.Count == 0)
        {
            return this;
        }

        var tail = FromSamples([.. extra]);
        return new FrameSeries(
            [.. _indexes, .. tail._indexes],
            [.. _timestampsMs, .. tail._timestampsMs],
            [.. _frameTimesMs, .. tail._frameTimesMs],
            Application ?? tail.Application,
            ProvenanceRules.Combine(Provenance, tail.Provenance));
    }

    private int FindContinuationStart(ReadOnlySpan<FrameSample> later, out bool canContinue)
    {
        if (Count == 0)
        {
            canContinue = true;
            return 0;
        }

        var lastIndex = _indexes[^1];
        var lastMs = _timestampsMs[^1];
        for (var i = later.Length - 1; i >= 0; i--)
        {
            if (later[i].Index == lastIndex && later[i].TimestampMs == lastMs)
            {
                canContinue = true;
                return i + 1;
            }
        }

        // The last pre-roll frame is not in `later` (for example it was evicted from the front of the ring).
        // Only continue if `later` starts strictly after our last frame in both index and timestamp without an earlier epoch boundary.
        if (later[0].TimestampMs > lastMs && later[0].Index > lastIndex)
        {
            canContinue = true;
            return 0;
        }

        canContinue = false;
        return later.Length;
    }
}
