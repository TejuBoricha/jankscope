using System.Windows;
using System.Windows.Interop;
using Jankscope.App.Services;
using Jankscope.Platform.Windowing;

namespace Jankscope.App.Tests;

/// <summary>Skipped, with a visible reason, where there is no desktop or the Windows build does not know the attribute (Windows 10 20H1, build 19041, is the first that does).</summary>
public sealed class DwmDarkModeFactAttribute : FactAttribute
{
    public DwmDarkModeFactAttribute()
    {
        if (!Environment.UserInteractive)
        {
            Skip = "This session is not interactive, so there is no desktop to create windows on.";
        }
        else if (Environment.OSVersion.Version.Build < 19041)
        {
            Skip = "This Windows build is older than 19041 and does not know DWMWA_USE_IMMERSIVE_DARK_MODE.";
        }
    }
}

/// <summary>GUI-run report, defect 9: the title bar follows the colour theme, for windows opened later and for windows already open.</summary>
[Collection("WPF")]
public class TitleBarThemeTests
{
    /// <summary>Runs <paramref name="body"/> on the interface thread with the title-bar call replaced by a recorder, then puts the real call and the dark theme back.</summary>
    private static Task<List<(nint Window, bool Dark)>> Recording(Action<List<(nint Window, bool Dark)>> body) =>
        WpfHost.RunAsync(() =>
        {
            var calls = new List<(nint Window, bool Dark)>();
            TitleBarTheme.SetDark = (window, dark) =>
            {
                calls.Add((window, dark));
                return true;
            };

            try
            {
                body(calls);
            }
            finally
            {
                TitleBarTheme.SetDark = TitleBar.TrySetDark;
                ThemeService.Apply(Application.Current, "Dark"); // what the test host started with
            }

            return calls;
        });

    [Fact]
    public async Task A_window_opened_while_the_theme_is_dark_gets_a_dark_title_bar_when_its_handle_exists()
    {
        nint handle = 0;
        var calls = await Recording(_ =>
        {
            ThemeService.Apply(Application.Current, "Dark");
            var window = new Window();
            TitleBarTheme.Follow(window);
            handle = new WindowInteropHelper(window).EnsureHandle();
            window.Close();
        });

        Assert.NotEqual((nint)0, handle);
        Assert.Contains((handle, true), calls);
    }

    [Fact]
    public async Task A_window_opened_while_the_theme_is_light_gets_a_light_title_bar()
    {
        nint handle = 0;
        var calls = await Recording(_ =>
        {
            ThemeService.Apply(Application.Current, "Light");
            var window = new Window();
            TitleBarTheme.Follow(window);
            handle = new WindowInteropHelper(window).EnsureHandle();
            window.Close();
        });

        Assert.Contains((handle, false), calls);
        Assert.DoesNotContain((handle, true), calls);
    }

    [Fact]
    public async Task Changing_the_theme_restyles_a_window_that_is_already_open()
    {
        nint handle = 0;
        var calls = await Recording(_ =>
        {
            ThemeService.Apply(Application.Current, "Dark");
            var window = new Window();
            TitleBarTheme.Follow(window);
            handle = new WindowInteropHelper(window).EnsureHandle();

            ThemeService.Apply(Application.Current, "Light");
            ThemeService.Apply(Application.Current, "Dark");
            window.Close();
        });

        // dark when it opened, light after the switch to Light, dark again after the switch back
        Assert.Equal([true, false, true], calls.Where(c => c.Window == handle).Select(c => c.Dark).ToArray());
    }

    [Fact]
    public async Task A_window_that_has_no_handle_yet_is_left_alone_by_a_theme_change_and_styled_once_it_has_one()
    {
        nint handle = 0;
        List<(nint Window, bool Dark)> beforeHandle = [];
        var calls = await Recording(recorded =>
        {
            var window = new Window(); // not shown yet, no handle
            TitleBarTheme.Follow(window);
            ThemeService.Apply(Application.Current, "Light");
            beforeHandle = [.. recorded];
            handle = new WindowInteropHelper(window).EnsureHandle();
            window.Close();
        });

        // Without the guard on a missing handle the theme change would send handle 0 to Windows.
        Assert.DoesNotContain(beforeHandle, c => c.Window == 0);
        Assert.NotEqual((nint)0, handle);
        Assert.Contains((handle, false), calls); // and the Light theme that was current when the handle appeared is applied then
    }

    [DwmDarkModeFact]
    public async Task Windows_really_accepts_the_dark_attribute_for_a_real_window_handle()
    {
        var (dark, light) = await WpfHost.RunAsync(() =>
        {
            var window = new Window();
            var handle = new WindowInteropHelper(window).EnsureHandle();
            var darkAccepted = TitleBar.TrySetDark(handle, true);
            var lightAccepted = TitleBar.TrySetDark(handle, false);
            window.Close();
            return (darkAccepted, lightAccepted);
        });

        Assert.True(dark);
        Assert.True(light);
    }
}
