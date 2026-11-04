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
    public FrameSeries ContinueWith(ReadOnlySpan<FrameSample> later, double endMs)
    {
        var lastMs = LastMs ?? double.NegativeInfinity;
        var extra = new List<FrameSample>();
        foreach (var sample in later)
        {
            if (sample.TimestampMs > lastMs && sample.TimestampMs <= endMs)
            {
                extra.Add(sample);
                lastMs = sample.TimestampMs;
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
}
