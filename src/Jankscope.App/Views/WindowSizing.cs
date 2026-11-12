namespace Jankscope.App.Views;

/// <summary>
/// The size the main window opens at. The window is designed for 1440 by 900, but a laptop screen at 125 percent scaling has only about 1536 by 824
/// device-independent pixels of usable space (the real-app run of 2026-11-12, defect 17), so the window opens no larger than the usable area.
/// </summary>
internal static class WindowSizing
{
    /// <summary>Space left around the window, so it does not touch the screen edges or run under the taskbar.</summary>
    public const double Margin = 16.0;

    /// <summary>
    /// <paramref name="width"/> by <paramref name="height"/> reduced to fit the usable area, but never below the window's own minimum size
    /// (a screen smaller than the minimum gets the minimum: the window is still usable, just partly off screen).
    /// </summary>
    public static (double Width, double Height) Fit(double width, double height, double workAreaWidth, double workAreaHeight, double minWidth, double minHeight)
    {
        if (!double.IsFinite(workAreaWidth) || !double.IsFinite(workAreaHeight) || workAreaWidth <= 0 || workAreaHeight <= 0)
        {
            return (width, height);
        }

        return (
            Math.Max(minWidth, Math.Min(width, workAreaWidth - Margin)),
            Math.Max(minHeight, Math.Min(height, workAreaHeight - Margin)));
    }
}
