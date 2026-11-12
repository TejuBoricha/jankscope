using System.Windows;
using System.Windows.Media;
using Jankscope.App.Services;
using Jankscope.Platform.Hotkeys;

namespace Jankscope.App.Views;

/// <summary>The first-run screen (spec section 15): purpose, what is and is not read, the anti-cheat statement, and whether the hotkeys work. Skippable and reachable again from Settings.</summary>
public partial class WelcomeWindow : Window
{
    public WelcomeWindow(IReadOnlyList<HotkeyStatus> hotkeys)
    {
        InitializeComponent();
        TitleBarTheme.Follow(this);
        Icon = Services.IconFactory.WindowIcon();
        var problems = hotkeys.Where(h => !h.Active).ToList();
        if (problems.Count == 0)
        {
            HotkeyState.Text = "Your mark hotkeys are active: " + string.Join(" and ", hotkeys.Select(h => h.Chord.ToString())) + ". You can change them in Settings.";
            HotkeyState.Foreground = (Brush)FindResource("OkBrush");
        }
        else
        {
            HotkeyState.Text = string.Join(" ", problems.Select(p => p.Reason));
            HotkeyState.Foreground = (Brush)FindResource("WarningBrush");
        }
    }

    private void OnStartClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
