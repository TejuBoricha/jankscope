namespace Jankscope.Frames.Tests;

public class SkeletonTests
{
    [Fact]
    public void Real_presentmon_fixture_is_copied_next_to_the_tests()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "presentmon", "v2.6.0-real-sanitized.csv");
        Assert.True(File.Exists(path), path);
    }

    [Fact]
    public void Missing_optional_metrics_default_to_null_not_zero()
    {
        var s = new FrameSample(0, 0.0, 16.7);
        Assert.Null(s.DisplayedFrameTimeMs);
        Assert.Null(s.CpuBusyMs);
        Assert.Null(s.GpuBusyMs);
    }
}
