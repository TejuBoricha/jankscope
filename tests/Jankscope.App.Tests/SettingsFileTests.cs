using System.Text.Json;
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

        // Read as JSON, not as text: the file escapes '+' (as +), so the chord is not in it letter for letter.
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var names = document.RootElement.EnumerateObject().Select(p => p.Name).ToArray();

        Assert.Equal("Ctrl+Alt+F11", document.RootElement.GetProperty("markStutterChord").GetString());
        Assert.DoesNotContain(names, n => n.Equals("bindings", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(
            ["firstRunCompleted", "lastProgram", "markInputStallChord", "markStutterChord", "postRollSeconds", "preRollSeconds", "soundEnabled", "theme"],
            names.OrderBy(n => n, StringComparer.Ordinal).ToArray());
    }
}
