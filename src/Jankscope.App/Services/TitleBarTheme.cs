using System.Windows;
using System.Windows.Interop;
using Jankscope.Platform.Windowing;

namespace Jankscope.App.Services;

/// <summary>
/// Keeps the title bar of every window of this program in step with the colour theme (GUI-run report, defect 9: the title bar stayed white on the
/// near-black dark theme). A window asks to follow the theme once, in its constructor; <see cref="ThemeService.Apply"/> restyles the windows that are
/// already open when the theme changes. The call only changes how this program's own windows are drawn; if Windows refuses it the title bar stays as it was.
/// </summary>
public static class TitleBarTheme
{
    private static bool _dark;

    /// <summary>The call that draws a title bar dark or light; replaced in tests.</summary>
    internal static Func<nint, bool, bool> SetDark { get; set; } = TitleBar.TrySetDark;

    /// <summary>True while the dark colour set is in use.</summary>
    public static bool IsDark => _dark;

    /// <summary>Draws the title bar of <paramref name="window"/> to match the theme as soon as its window handle exists, and records nothing else about it.</summary>
    public static void Follow(Window window)
    {
        window.SourceInitialized += (_, _) => Style(window);
    }

    /// <summary>Records the theme and restyles the windows that are open.</summary>
    internal static void ThemeChanged(Application application, bool dark)
    {
        _dark = dark;
        foreach (Window window in application.Windows)
        {
            Style(window);
        }
    }

    private static void Style(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != 0)
        {
            SetDark(handle, _dark);
        }
    }
}
