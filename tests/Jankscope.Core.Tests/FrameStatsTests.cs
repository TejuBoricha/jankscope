namespace Jankscope.Core.Tests;

public class FrameStatsTests
{
    [Fact]
    public void AverageFps_is_frames_over_duration_not_mean_of_per_frame_fps()
    {
        // Frames of 10 ms and 30 ms: 2 frames in 40 ms = 50 fps. The mean of 100 and 33.3 fps would wrongly give 66.7.
        Assert.Equal(50.0, FrameStats.AverageFps([10.0, 30.0]), precision: 9);
    }

    [Fact]
    public void AverageFps_of_nothing_is_not_a_number()
    {
        Assert.True(double.IsNaN(FrameStats.AverageFps([])));
    }

    [Theory]
    [InlineData(99.0, 99.0)]
    [InlineData(100.0, 100.0)]
    [InlineData(1.0, 1.0)]
    [InlineData(50.0, 50.0)]
    public void Percentile_uses_nearest_rank(double percentile, double expectedMs)
    {
        var times = Enumerable.Range(1, 100).Select(i => (double)i).Reverse().ToArray(); // unsorted on purpose
        Assert.Equal(expectedMs, FrameStats.PercentileFrameTimeMs(times, percentile));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    [InlineData(100.1)]
    public void Percentile_rejects_out_of_range(double percentile)
    {
        Assert.True(double.IsNaN(FrameStats.PercentileFrameTimeMs([1.0, 2.0], percentile)));
    }

    [Fact]
    public void OnePercentLow_needs_at_least_1000_frames()
    {
        var tooFew = Enumerable.Repeat(10.0, FrameStats.MinFramesForOnePercentLow - 1).ToArray();
        Assert.Null(FrameStats.OnePercentLowFps(tooFew));
    }

    [Fact]
    public void OnePercentLow_ignores_a_few_outliers_but_not_one_percent_of_them()
    {
        var steady = Enumerable.Repeat(10.0, 1000).ToArray();
        for (var i = 0; i < 10; i++)
        {
            steady[i * 50] = 50.0; // 10 slow frames of 1000 = 1.0%: the 99th percentile is still 10 ms
        }

        Assert.Equal(100.0, FrameStats.OnePercentLowFps(steady));

        for (var i = 0; i < 11; i++)
        {
            steady[i * 50] = 50.0; // 11 slow frames: rank 990 now lands on a 50 ms frame
        }

        Assert.Equal(20.0, FrameStats.OnePercentLowFps(steady));
    }

    [Fact]
    public void PointOnePercentLow_is_session_level_only()
    {
        Assert.Null(FrameStats.PointOnePercentLowFps(Enumerable.Repeat(10.0, 9_999).ToArray()));
        Assert.Equal(100.0, FrameStats.PointOnePercentLowFps(Enumerable.Repeat(10.0, 10_000).ToArray()));
    }
}
