using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Jankscope.App.Services;

namespace Jankscope.App.Tests;

/// <summary>GUI-run report, defect 14: a disabled primary button ("Mark stutter" before a capture starts) must stay readable and must still look switched off.</summary>
[Collection("WPF")]
public class ButtonStateTests
{
    /// <summary>WCAG 2.x relative luminance of an sRGB colour.</summary>
    private static double Luminance(Color color)
    {
        static double Channel(byte value)
        {
            var c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));
    }

    private static double Contrast(Color a, Color b)
    {
        var (high, low) = (Luminance(a), Luminance(b));
        if (high < low)
        {
            (high, low) = (low, high);
        }

        return (high + 0.05) / (low + 0.05);
    }

    private static Color Solid(Brush brush) => ((SolidColorBrush)brush).Color;

    /// <summary>The colours a primary button really shows in each state, with the theme in use, taken from the effective property values.</summary>
    private static (double Opacity, Color Fill, Color Label, Color Border) Look(string theme, bool enabled)
    {
        ThemeService.Apply(Application.Current, theme);
        try
        {
            var button = new Button { Content = "Mark stutter", IsEnabled = enabled, Style = (Style)Application.Current.FindResource("PrimaryButton") };
            button.ApplyTemplate();
            return (button.Opacity, Solid(button.Background), Solid(button.Foreground), Solid(button.BorderBrush));
        }
        finally
        {
            ThemeService.Apply(Application.Current, "Dark"); // what the test host started with
        }
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public async Task A_disabled_primary_button_keeps_a_label_that_can_be_read(string theme)
    {
        var (opacity, fill, label, _) = await WpfHost.RunAsync(() => Look(theme, enabled: false));

        // The plain-button rule fades the whole button to 38%; that is what made this label hard to read, so the primary button must not be faded.
        Assert.Equal(1.0, opacity);
        var contrast = Contrast(fill, label);
        Assert.True(contrast >= 4.5, $"{theme}: the disabled label is {contrast:0.0}:1 on its fill; 4.5:1 is the bar for normal-size text");
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public async Task A_disabled_primary_button_still_looks_switched_off(string theme)
    {
        var (on, off) = await WpfHost.RunAsync(() => (Look(theme, enabled: true), Look(theme, enabled: false)));

        // Not the bright accent fill any more, and not the enabled label colour.
        Assert.NotEqual(on.Fill, off.Fill);
        Assert.NotEqual(on.Label, off.Label);
        Assert.NotEqual(on.Border, off.Border);
        Assert.Equal(1.0, on.Opacity);
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public async Task An_enabled_primary_button_is_unchanged_and_readable(string theme)
    {
        var (_, fill, label, _) = await WpfHost.RunAsync(() => Look(theme, enabled: true));

        Assert.True(Contrast(fill, label) >= 4.5, $"{theme}: the enabled primary label is {Contrast(fill, label):0.0}:1 on its fill");
    }

    [Fact]
    public void The_contrast_helper_matches_known_values()
    {
        Assert.Equal(21.0, Contrast(Colors.Black, Colors.White), 3);
        Assert.Equal(1.0, Contrast(Colors.Gray, Colors.Gray), 6);
        Assert.Equal(4.48, Contrast(Color.FromRgb(0x77, 0x77, 0x77), Colors.White), 2); // #777 on white is the textbook "just under 4.5"
    }
}
