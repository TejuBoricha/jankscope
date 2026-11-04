namespace Jankscope.Frames.Incidents;

/// <summary>One horizontal slice of the timeline: the frame times that fall in it. <see cref="Count"/> 0 means no frame was logged there (a gap), and its numbers are NaN, never zero.</summary>
public readonly record struct TimelineBucket(double StartMs, double EndMs, int Count, double MinMs, double MaxMs, double MeanMs)
{
    public bool IsEmpty => Count == 0;
}

/// <summary>
/// Reduces a long frame series to a fixed number of buckets (one per pixel column) by keeping each bucket's minimum, maximum and mean, so that a one-frame
/// spike stays visible at every zoom level; plain decimation would drop it.
/// </summary>
public static class TimelineBuckets
{
    public static TimelineBucket[] Build(ReadOnlySpan<FrameSample> frames, double startMs, double endMs, int bucketCount)
    {
        var builder = new Accumulator(startMs, endMs, bucketCount);
        foreach (var frame in frames)
        {
            builder.Add(frame.TimestampMs, frame.FrameTimeMs);
        }

        return builder.Result();
    }

    public static TimelineBucket[] Build(FrameSeries series, double startMs, double endMs, int bucketCount)
    {
        ArgumentNullException.ThrowIfNull(series);
        var builder = new Accumulator(startMs, endMs, bucketCount);
        var timestamps = series.TimestampsMs;
        var frameTimes = series.FrameTimesMs;
        for (var i = 0; i < timestamps.Length; i++)
        {
            builder.Add(timestamps[i], frameTimes[i]);
        }

        return builder.Result();
    }

    private sealed class Accumulator
    {
        private readonly double _start;
        private readonly double _width;
        private readonly int[] _count;
        private readonly double[] _min;
        private readonly double[] _max;
        private readonly double[] _sum;

        public Accumulator(double startMs, double endMs, int bucketCount)
        {
            if (!double.IsFinite(startMs) || !double.IsFinite(endMs) || endMs <= startMs)
            {
                throw new ArgumentOutOfRangeException(nameof(endMs), endMs, "the range must be finite with an end after its start");
            }

            ArgumentOutOfRangeException.ThrowIfLessThan(bucketCount, 1);
            _start = startMs;
            _width = (endMs - startMs) / bucketCount;
            _count = new int[bucketCount];
            _min = new double[bucketCount];
            _max = new double[bucketCount];
            _sum = new double[bucketCount];
            Array.Fill(_min, double.PositiveInfinity);
            Array.Fill(_max, double.NegativeInfinity);
        }

        public void Add(double timestampMs, double frameTimeMs)
        {
            if (!double.IsFinite(timestampMs) || !double.IsFinite(frameTimeMs) || frameTimeMs <= 0)
            {
                return; // an unusable frame is a gap in the picture, not a zero
            }

            var position = (timestampMs - _start) / _width;
            if (position < 0 || position > _count.Length) // the end of the range belongs to the last bucket
            {
                return;
            }

            var index = Math.Min((int)position, _count.Length - 1);
            _count[index]++;
            _sum[index] += frameTimeMs;
            if (frameTimeMs < _min[index])
            {
                _min[index] = frameTimeMs;
            }

            if (frameTimeMs > _max[index])
            {
                _max[index] = frameTimeMs;
            }
        }

        public TimelineBucket[] Result()
        {
            var result = new TimelineBucket[_count.Length];
            for (var i = 0; i < result.Length; i++)
            {
                var start = _start + i * _width;
                result[i] = _count[i] == 0
                    ? new TimelineBucket(start, start + _width, 0, double.NaN, double.NaN, double.NaN)
                    : new TimelineBucket(start, start + _width, _count[i], _min[i], _max[i], _sum[i] / _count[i]);
            }

            return result;
        }
    }
}
