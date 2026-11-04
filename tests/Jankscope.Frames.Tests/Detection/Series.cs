namespace Jankscope.Frames.Tests.Detection;

/// <summary>Builds frame series for the detector tests. Timestamps are cumulative sums of the frame times (PresentMon style).</summary>
internal static class Series
{
    public static FrameSample[] FromFrameTimes(IReadOnlyList<double> frameTimesMs)
    {
        var samples = new FrameSample[frameTimesMs.Count];
        var timestamp = 0.0;
        for (var i = 0; i < samples.Length; i++)
        {
            timestamp += frameTimesMs[i];
            samples[i] = new FrameSample(i, timestamp, frameTimesMs[i]);
        }

        return samples;
    }

    public static double[] Constant(double frameTimeMs, int count) => Enumerable.Repeat(frameTimeMs, count).ToArray();

    public static double[] Join(params IEnumerable<double>[] parts) => parts.SelectMany(p => p).ToArray();
}
