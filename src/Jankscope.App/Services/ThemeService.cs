using System.Windows;

namespace Jankscope.App.Services;

/// <summary>Light and dark colour sets (spec section 15). "System" follows the Windows app-mode setting, read only.</summary>
public static class ThemeService
{
    private const string PersonalizeKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>True when Windows is set to dark app mode. Light when the value cannot be read.</summary>
    public static bool SystemPrefersDark()
    {
        try
        {
            return Microsoft.Win32.Registry.GetValue(PersonalizeKey, "AppsUseLightTheme", 1) is int value && value == 0;
        }
        catch (Exception e) when (e is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Swaps the colour dictionary (the first merged dictionary), restyles the title bars of the open windows (<see cref="TitleBarTheme"/>) and returns whether
    /// the dark one is now in use. Call it on the interface thread: it reads the application's window list.
    /// </summary>
    public static bool Apply(Application application, string theme)
    {
        var dark = theme == "Dark" || (theme != "Light" && SystemPrefersDark());
        var dictionary = new ResourceDictionary { Source = new Uri(dark ? "/jankscope-app;component/Themes/Dark.xaml" : "/jankscope-app;component/Themes/Light.xaml", UriKind.Relative) };
        var merged = application.Resources.MergedDictionaries;
        if (merged.Count > 0)
        {
            merged[0] = dictionary;
        }
        else
        {
            merged.Insert(0, dictionary);
        }

        TitleBarTheme.ThemeChanged(application, dark);
        return dark;
    }
}

/// <summary>What the windows need from the application object: the hotkeys, the theme, the welcome screen and the mark flow.</summary>
public interface IShell
{
    IReadOnlyList<Jankscope.Platform.Hotkeys.HotkeyStatus> HotkeyStatuses { get; }

    IReadOnlyList<Jankscope.Platform.Hotkeys.HotkeyStatus> ApplyHotkeys(Jankscope.Core.Hotkeys.HotkeyBindings bindings);

    void ApplyTheme(string theme);

    void ShowWelcome();

    void RequestMark(Jankscope.Frames.Incidents.MarkKind kind);

    void ChangeWindowVisibility(bool visible);
}
