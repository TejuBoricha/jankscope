using System.Runtime.InteropServices;

namespace Jankscope.Platform.Windowing;

/// <summary>
/// The one documented Win32 entry point (dwmapi.h) that sets an attribute of a window's own frame. The only attribute used is
/// <c>DWMWA_USE_IMMERSIVE_DARK_MODE</c>: the title bar and borders of this program's own window are drawn dark instead of white. It changes how one
/// window of this process is drawn, nothing else: no other process, no system setting, no input, no memory outside the arguments. The DLL search path
/// is restricted to System32 by the assembly attribute in AssemblyInfo.cs.
/// </summary>
internal static partial class NativeWindowing
{
    [LibraryImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
    internal static partial int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}

/// <summary>Draws the title bar of one of this program's own windows to match the colour theme (GUI-run report, defect 9).</summary>
public static class TitleBar
{
    /// <summary>
    /// DWMWA_USE_IMMERSIVE_DARK_MODE. Windows 10 20H1 (build 19041) and every Windows 11 build know this value; Windows 10 22H2 is the oldest system this
    /// program targets. On an older build Windows refuses the value and the title bar simply stays light, as it was before.
    /// </summary>
    internal const int UseImmersiveDarkMode = 20;

    /// <summary>The seam over the native call, so a refusal can be tested on a machine that would accept it.</summary>
    internal delegate int SetAttribute(nint window, int attribute, ref int value, int size);

    /// <summary>
    /// Asks Windows to draw the title bar of <paramref name="window"/> dark or light. True when Windows accepted it. False (and nothing changed) for a
    /// window with no handle yet, or when Windows refuses; a title bar that cannot follow the theme is cosmetic, so a refusal is never an error.
    /// </summary>
    public static bool TrySetDark(nint window, bool dark) => TrySetDark(window, dark, NativeWindowing.DwmSetWindowAttribute);

    internal static bool TrySetDark(nint window, bool dark, SetAttribute set)
    {
        if (window == 0)
        {
            return false;
        }

        var value = dark ? 1 : 0;
        return set(window, UseImmersiveDarkMode, ref value, sizeof(int)) == 0;
    }
}
