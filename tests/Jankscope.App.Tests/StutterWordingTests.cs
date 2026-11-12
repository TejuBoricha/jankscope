using Jankscope.App.Views;
using Jankscope.Frames.Detection;

namespace Jankscope.App.Tests;

/// <summary>GUI-run report, defect 15: the counts under the stutter number add up, and the tier labels carry their unit once.</summary>
public class StutterWordingTests
{
    private static StutterCluster Cluster(int events) =>
        new(0, events, 0, 0, 0.0, 0.0, 0.0, 0.0, StutterSeverity.Minor);

    [Fact]
    public void Three_single_spikes_and_a_burst_of_three_are_said_so_not_as_isolated_and_clusters()
    {
        // The report's case: 6 stutters, "3 isolated | 4 cluster(s)". Three single spikes plus one burst holding the other three.
        var clusters = new[] { Cluster(1), Cluster(3), Cluster(1), Cluster(1) };

        Assert.Equal("3 single, 1 burst of 3", StutterWording.Breakdown(clusters));
        Assert.Equal("3 single spikes, 1 burst of 3", StutterWording.Breakdown(clusters, spelledOut: true));
        Assert.Equal(6, clusters.Sum(c => c.EventCount));
    }

    [Fact]
    public void Only_singles_only_bursts_and_several_bursts_each_read_naturally()
    {
        Assert.Equal("1 single spike", StutterWording.Breakdown([Cluster(1)], spelledOut: true));
        Assert.Equal("2 single", StutterWording.Breakdown([Cluster(1), Cluster(1)]));
        Assert.Equal("1 burst of 2", StutterWording.Breakdown([Cluster(2)]));
        Assert.Equal("2 bursts (largest 5)", StutterWording.Breakdown([Cluster(2), Cluster(5)]));
        Assert.Equal("1 single, 2 bursts (largest 4)", StutterWording.Breakdown([Cluster(4), Cluster(1), Cluster(2)]));
        Assert.Equal("none", StutterWording.Breakdown([]));
    }

    [Fact]
    public void The_cluster_count_is_never_written_with_a_bracketed_plural()
    {
        Assert.Equal("1 cluster", StutterWording.Clusters(1));
        Assert.Equal("4 clusters", StutterWording.Clusters(4));
        Assert.Equal("0 clusters", StutterWording.Clusters(0));
    }
}

[Collection("WPF")]
public class FrameDistributionLabelTests
{
    [Fact]
    public async Task The_narrow_tier_labels_drop_the_unit_and_the_heading_carries_it_once()
    {
        var (heading, wideHeading, compact, wide) = await WpfHost.RunAsync(() => (
            FrameDistributionView.TierHeading(compact: true),
            FrameDistributionView.TierHeading(compact: false),
            Enumerable.Range(0, FrameDistributionView.TierCount).Select(i => FrameDistributionView.TierLabel(i, compact: true)).ToArray(),
            Enumerable.Range(0, FrameDistributionView.TierCount).Select(i => FrameDistributionView.TierLabel(i, compact: false)).ToArray()));

        Assert.Contains("(ms)", heading);
        Assert.Equal(["< 4.2", "4.2-8.3", "8.3-16.7", "16.7-33.3", "> 33.3"], compact);
        Assert.All(compact, label => Assert.DoesNotContain("ms", label)); // one rule for all five, not "ms" on the two ends only
        Assert.All(wide, label => Assert.EndsWith(" ms", label));         // the wide form states its own unit
        Assert.DoesNotContain("(ms)", wideHeading);
    }
}
