using Jankscope.App.Views;

namespace Jankscope.App.Tests;

/// <summary>How the pages fit a window smaller than the size they are laid out for (real-app run of 2026-11-12, defects 1, 4 and 6).</summary>
public class PageScalingTests
{
    [Theory]
    [InlineData(1040.0, 700.0)]
    [InlineData(1048.0, 714.0)]
    [InlineData(1920.0, 1080.0)]
    [InlineData(3000.0, 700.0)]
    public void A_viewport_at_least_the_design_size_is_not_scaled(double width, double height) =>
        Assert.Equal(1.0, PageScaling.For(width, height));

    [Fact]
    public void A_smaller_viewport_scales_by_the_tighter_side()
    {
        Assert.Equal(0.9, PageScaling.For(PageScaling.DesignWidth * 0.9, PageScaling.DesignHeight * 2), 6); // width is the tight side
        Assert.Equal(0.85, PageScaling.For(PageScaling.DesignWidth * 2, PageScaling.DesignHeight * 0.85), 6); // height is the tight side
    }

    [Fact]
    public void The_smallest_window_the_program_allows_scales_but_stays_readable()
    {
        // MinWidth 1020 and MinHeight 700 less the 228 px sidebar, the title bar and the top banner leave roughly 790 by 610.
        var scale = PageScaling.For(790.0, 610.0);

        Assert.InRange(scale, 0.7, 0.8);
    }

    [Theory]
    [InlineData(100.0, 100.0)]
    [InlineData(1.0, 5000.0)]
    public void The_scale_never_goes_below_the_minimum(double width, double height) =>
        Assert.Equal(PageScaling.MinimumScale, PageScaling.For(width, height));

    [Theory]
    [InlineData(0.0, 700.0)]
    [InlineData(1040.0, 0.0)]
    [InlineData(-5.0, 700.0)]
    [InlineData(double.NaN, 700.0)]
    [InlineData(1040.0, double.PositiveInfinity)]
    public void An_unusable_viewport_leaves_the_pages_unscaled(double width, double height) =>
        Assert.Equal(1.0, PageScaling.For(width, height));

    [Fact]
    public void A_bigger_window_never_gets_a_smaller_scale()
    {
        var previous = 0.0;
        for (var w = 600.0; w <= 1400.0; w += 25.0)
        {
            var scale = PageScaling.For(w, w * 0.7);
            Assert.True(scale >= previous, $"the scale fell from {previous} to {scale} at width {w}");
            previous = scale;
        }
    }
}
