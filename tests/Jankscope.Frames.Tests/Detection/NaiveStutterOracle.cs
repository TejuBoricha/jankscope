using Jankscope.Frames.Detection;

namespace Jankscope.Frames.Tests.Detection;

/// <summary>
/// A deliberately naive, independent restatement of the detection definition: it keeps the window in a plain list and sorts a
/// copy for every frame (O(n * w log w)). It exists to cross-check the production detector's sliding-median structure.
/// </summary>
internal static class NaiveStutterOracle
{
    public sealed record Outcome(
        List<StutterEvent> Events,
        long Evaluated,
        long InsufficientHistory,
        long Rejected,
        long ClockDiscontinuities);

    public static Outcome Run(IReadOnlyList<FrameSample> frames, StutterOptions options)
    {
        var events = new List<StutterEvent>();
        long evaluated = 0, insufficient = 0, rejected = 0, discontinuities = 0;
        var window = new List<(double Timestamp, double FrameTime)>();
        var haveLast = false;
        var lastTimestamp = 0.0;

        foreach (var frame in frames)
        {
            var frameTime = frame.FrameTimeMs;
            var timestamp = frame.TimestampMs;
            if (!double.IsFinite(frameTime) || frameTime <= 0 || !double.IsFinite(timestamp))
            {
                rejected++;
                continue;
            }

            if (haveLast && timestamp < lastTimestamp)
            {
                discontinuities++;
                window.Clear();
                haveLast = false;
            }

            if (haveLast)
            {
                window.RemoveAll(w => w.Timestamp < lastTimestamp - options.RollingWindowMs);
            }

            if (window.Count < options.MinHistoryFrames)
            {
                insufficient++;
            }
            else
            {
                evaluated++;
                var sorted = window.Select(w => w.FrameTime).OrderBy(x => x).ToArray();
                var mid = sorted.Length / 2;
                var median = sorted.Length % 2 == 1 ? sorted[mid] : sorted[mid - 1] + ((sorted[mid] - sorted[mid - 1]) * 0.5);
                if (frameTime + 1e-9 >= options.Multiplier * median && (frameTime - median) + 1e-9 >= options.MinExcessMs)
                {
                    var excess = frameTime - median;
                    events.Add(new StutterEvent(frame.Index, timestamp, frameTime, median, excess, frameTime / median, StutterSeverities.Classify(excess)));
                }
            }

            window.Add((timestamp, frameTime));
            lastTimestamp = timestamp;
            haveLast = true;
        }

        return new Outcome(events, evaluated, insufficient, rejected, discontinuities);
    }
}
