using Jankscope.App.Views;

namespace Jankscope.App.Tests;

/// <summary>The size the main window opens at (real-app run of 2026-11-12, defect 17).</summary>
public class WindowSizingTests
{
    [Fact]
    public void A_roomy_screen_gets_the_designed_size()
    {
        Assert.Equal((1440.0, 900.0), WindowSizing.Fit(1440, 900, 2560, 1400, 1020, 700));
    }

    [Fact]
    public void The_reference_laptop_at_125_percent_gets_a_window_that_fits_below_the_taskbar()
    {
        // 1920 by 1080 at 125 percent is 1536 by 864, and the taskbar leaves about 824 of that.
        var (width, height) = WindowSizing.Fit(1440, 900, 1536, 824, 1020, 700);

        Assert.Equal(1440.0, width);
        Assert.Equal(824.0 - WindowSizing.Margin, height);
    }

    [Fact]
    public void A_screen_smaller_than_the_minimum_still_gets_the_minimum()
    {
        Assert.Equal((1020.0, 700.0), WindowSizing.Fit(1440, 900, 900, 600, 1020, 700));
    }

    [Theory]
    [InlineData(0.0, 800.0)]
    [InlineData(1500.0, 0.0)]
    [InlineData(double.NaN, 800.0)]
    [InlineData(1500.0, double.PositiveInfinity)]
    public void An_unusable_work_area_leaves_the_designed_size(double workWidth, double workHeight) =>
        Assert.Equal((1440.0, 900.0), WindowSizing.Fit(1440, 900, workWidth, workHeight, 1020, 700));
}
