using System.Globalization;
using Jankscope.Frames.Detection;

namespace Jankscope.App.Views;

/// <summary>
/// How the app says how many stutters there are and how they group (GUI-run report, defect 15). A stutter is one slow frame; stutters
/// that follow each other closely form a burst (a cluster), and one on its own is a single spike. Saying only "3 isolated | 4 cluster(s)"
/// next to "6" made the person do the arithmetic: 3 of the 4 clusters are single spikes and the fourth holds the other 3 stutters.
/// </summary>
internal static class StutterWording
{
    /// <summary>
    /// The groups in words: "3 single, 1 burst of 3" (short, for a tile) or "3 single spikes, 1 burst of 3" (spelled out, for a sentence).
    /// The numbers add up to the stutter count: the singles plus the sizes of the bursts.
    /// </summary>
    public static string Breakdown(IReadOnlyList<StutterCluster> clusters, bool spelledOut = false)
    {
        var singles = clusters.Count(c => c.IsIsolated);
        var bursts = clusters.Where(c => !c.IsIsolated).Select(c => c.EventCount).ToList();
        var parts = new List<string>(2);
        if (singles > 0)
        {
            var count = singles.ToString(CultureInfo.InvariantCulture);
            parts.Add(spelledOut ? (singles == 1 ? "1 single spike" : count + " single spikes") : count + " single");
        }

        if (bursts.Count == 1)
        {
            parts.Add("1 burst of " + bursts[0].ToString(CultureInfo.InvariantCulture));
        }
        else if (bursts.Count > 1)
        {
            parts.Add(bursts.Count.ToString(CultureInfo.InvariantCulture) + " bursts (largest " + bursts.Max().ToString(CultureInfo.InvariantCulture) + ")");
        }

        return parts.Count == 0 ? "none" : string.Join(", ", parts);
    }

    /// <summary>"1 cluster" or "4 clusters", never "cluster(s)".</summary>
    public static string Clusters(int count) =>
        count.ToString(CultureInfo.InvariantCulture) + (count == 1 ? " cluster" : " clusters");
}
