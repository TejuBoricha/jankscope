namespace Jankscope.Frames.Detection;

/// <summary>
/// How bad one stutter was, by how much longer than its median the frame took (<see cref="StutterEvent.ExcessMs"/>): the length of the
/// stall the player sat through. These are presentation tiers, not part of the detection rule, and they are not calibrated against
/// human perception; they only order events (see <see cref="StutterSeverities"/>).
/// </summary>
public enum StutterSeverity
{
    /// <summary>Excess under 10 ms.</summary>
    Minor = 0,

    /// <summary>Excess from 10 ms up to (not including) 25 ms.</summary>
    Moderate = 1,

    /// <summary>Excess from 25 ms up to (not including) 50 ms.</summary>
    Major = 2,

    /// <summary>Excess of 50 ms or more.</summary>
    Severe = 3,
}

/// <summary>The severity tiers and how an excess maps onto them.</summary>
public static class StutterSeverities
{
    public const double ModerateExcessMs = 10.0;
    public const double MajorExcessMs = 25.0;
    public const double SevereExcessMs = 50.0;

    public static StutterSeverity Classify(double excessMs) => excessMs switch
    {
        >= SevereExcessMs => StutterSeverity.Severe,
        >= MajorExcessMs => StutterSeverity.Major,
        >= ModerateExcessMs => StutterSeverity.Moderate,
        _ => StutterSeverity.Minor,
    };
}

/// <summary>One frame that met the stutter definition.</summary>
/// <param name="FrameIndex"><see cref="FrameSample.Index"/> of the frame.</param>
/// <param name="TimestampMs"><see cref="FrameSample.TimestampMs"/> of the frame.</param>
/// <param name="FrameTimeMs">How long the frame took.</param>
/// <param name="MedianMs">Median frame time of the preceding window, excluding this frame.</param>
/// <param name="ExcessMs"><paramref name="FrameTimeMs"/> minus <paramref name="MedianMs"/>.</param>
/// <param name="Ratio"><paramref name="FrameTimeMs"/> divided by <paramref name="MedianMs"/>.</param>
/// <param name="Severity">Tier of <paramref name="ExcessMs"/>.</param>
public readonly record struct StutterEvent(
    long FrameIndex,
    double TimestampMs,
    double FrameTimeMs,
    double MedianMs,
    double ExcessMs,
    double Ratio,
    StutterSeverity Severity);

/// <summary>Consecutive events whose gaps are all smaller than <see cref="StutterOptions.ClusterGapMs"/>.</summary>
/// <param name="FirstEventPosition">Position of the first event of the cluster in <see cref="StutterResult.Events"/>.</param>
/// <param name="EventCount">Number of events (1 is an isolated spike).</param>
/// <param name="FirstFrameIndex">Frame index of the first event.</param>
/// <param name="LastFrameIndex">Frame index of the last event.</param>
/// <param name="StartTimestampMs">Timestamp of the first event.</param>
/// <param name="EndTimestampMs">Timestamp of the last event.</param>
/// <param name="TotalExcessMs">Sum of the events' excess.</param>
/// <param name="WorstExcessMs">Largest excess among the events.</param>
/// <param name="Severity">Severity of the worst event.</param>
public sealed record StutterCluster(
    int FirstEventPosition,
    int EventCount,
    long FirstFrameIndex,
    long LastFrameIndex,
    double StartTimestampMs,
    double EndTimestampMs,
    double TotalExcessMs,
    double WorstExcessMs,
    StutterSeverity Severity)
{
    /// <summary>A cluster of one event: a spike on its own, as opposed to a burst.</summary>
    public bool IsIsolated => EventCount == 1;
}

/// <summary>Groups events into clusters.</summary>
public static class StutterClusters
{
    /// <summary>
    /// Events are taken in order. An event joins the previous one's cluster when its timestamp is later than the previous event's by
    /// strictly less than <paramref name="clusterGapMs"/> (a gap of exactly the limit starts a new cluster; so does a timestamp that went
    /// backwards). Chains therefore stay together: events 400 ms apart form one cluster however long the chain.
    /// </summary>
    public static IReadOnlyList<StutterCluster> Build(IReadOnlyList<StutterEvent> events, double clusterGapMs)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (!double.IsFinite(clusterGapMs) || clusterGapMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(clusterGapMs), clusterGapMs, "must be finite and not negative");
        }

        var clusters = new List<StutterCluster>();
        var i = 0;
        while (i < events.Count)
        {
            var first = i;
            var totalExcess = events[i].ExcessMs;
            var worst = events[i].ExcessMs;
            var worstSeverity = events[i].Severity;
            while (i + 1 < events.Count && IsSameCluster(events[i], events[i + 1], clusterGapMs))
            {
                i++;
                totalExcess += events[i].ExcessMs;
                if (events[i].ExcessMs > worst)
                {
                    worst = events[i].ExcessMs;
                }

                if (events[i].Severity > worstSeverity)
                {
                    worstSeverity = events[i].Severity;
                }
            }

            clusters.Add(new StutterCluster(
                first,
                i - first + 1,
                events[first].FrameIndex,
                events[i].FrameIndex,
                events[first].TimestampMs,
                events[i].TimestampMs,
                totalExcess,
                worst,
                worstSeverity));
            i++;
        }

        return clusters;
    }

    private static bool IsSameCluster(StutterEvent earlier, StutterEvent later, double clusterGapMs)
    {
        var gap = later.TimestampMs - earlier.TimestampMs;
        return gap >= 0 && gap < clusterGapMs;
    }
}
