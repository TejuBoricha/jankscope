namespace Jankscope.App.Views;

/// <summary>
/// How the pages fit a window smaller than the size they are laid out for. A page is arranged for <see cref="DesignWidth"/> by <see cref="DesignHeight"/>;
/// in a smaller window the whole page is scaled down together instead of being squeezed, so labels never run into each other or get cut (the real-app run
/// of 2026-11-12, defects 1, 4 and 6, found them at the smallest window) and nothing needs scrolling. In a larger window the page simply fills the space.
/// </summary>
internal static class PageScaling
{
    /// <summary>The width a page is laid out for, in device-independent pixels. At this width and above every page was checked on screen (the real-app run, 1280 by 800 window).</summary>
    public const double DesignWidth = 1040.0;

    /// <summary>The height a page is laid out for, in device-independent pixels.</summary>
    public const double DesignHeight = 700.0;

    /// <summary>The smallest scale ever used. The smallest window the program allows scales to about 0.76; this only guards against a smaller viewport than that.</summary>
    public const double MinimumScale = 0.7;

    /// <summary>The scale for a viewport of the given size: 1 when it is at least the design size, otherwise what makes the design size fit, never below <see cref="MinimumScale"/>.</summary>
    public static double For(double viewportWidth, double viewportHeight)
    {
        if (!double.IsFinite(viewportWidth) || !double.IsFinite(viewportHeight) || viewportWidth <= 0 || viewportHeight <= 0)
        {
            return 1.0;
        }

        return Math.Clamp(Math.Min(viewportWidth / DesignWidth, viewportHeight / DesignHeight), MinimumScale, 1.0);
    }
}
