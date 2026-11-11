using Jankscope.App.Services;

namespace Jankscope.App.Tests;

public class SettingsFileTests
{
    [Fact]
    public void The_settings_file_holds_what_the_person_set_and_not_values_worked_out_from_it()
    {
        // Review of Stage 1b, finding 14: the computed hotkey bindings (modifiers, virtual key) were written too, as noise that was ignored on load.
        var path = TestData.TempFile("settings.json");

        Assert.True(new SettingsStore(path).TrySave(new AppSettings { MarkStutterChord = "Ctrl+Alt+F11" }, out var problem), problem);
        var text = File.ReadAllText(path);

        Assert.Contains("\"markStutterChord\"", text);
        Assert.Contains("Ctrl+Alt+F11", text);
        Assert.DoesNotContain("bindings", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("virtualKey", text, StringComparison.OrdinalIgnoreCase);
    }
}
