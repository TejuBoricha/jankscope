using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jankscope.Core;
using Jankscope.Core.Hotkeys;
using Jankscope.Frames.Incidents;

namespace Jankscope.App.Services;

/// <summary>
/// The few things the person can set. Stored as a small JSON file in their own profile (no registry, nothing sent anywhere). A value that is missing,
/// unreadable or out of range falls back to its default instead of failing: settings must never keep the program from starting.
/// </summary>
public sealed record AppSettings
{
    public string MarkStutterChord { get; init; } = HotkeyBindings.Default.MarkStutter.ToString();

    public string MarkInputStallChord { get; init; } = HotkeyBindings.Default.MarkInputStall.ToString();

    public double PreRollSeconds { get; init; } = IncidentRecorderOptions.Default.PreRollSeconds;

    public double PostRollSeconds { get; init; } = IncidentRecorderOptions.Default.PostRollSeconds;

    /// <summary>"System", "Light" or "Dark".</summary>
    public string Theme { get; init; } = "System";

    public bool SoundEnabled { get; init; } = true;

    public bool FirstRunCompleted { get; init; }

    public string? LastProgram { get; init; }

    /// <summary>Worked out from the two chords above, so it is not written to the settings file (it was, as noise that was ignored on load).</summary>
    [JsonIgnore]
    public HotkeyBindings Bindings =>
        HotkeyBindings.TryCreate(MarkStutterChord, MarkInputStallChord, out var bindings, out _) ? bindings : HotkeyBindings.Default;

    /// <summary>The same settings with every value brought inside its allowed range or back to its default.</summary>
    public AppSettings Normalized()
    {
        var bindings = Bindings;
        return this with
        {
            MarkStutterChord = bindings.MarkStutter.ToString(),
            MarkInputStallChord = bindings.MarkInputStall.ToString(),
            PreRollSeconds = double.IsFinite(PreRollSeconds) ? Math.Clamp(PreRollSeconds, 30, 600) : IncidentRecorderOptions.Default.PreRollSeconds,
            PostRollSeconds = double.IsFinite(PostRollSeconds) ? Math.Clamp(PostRollSeconds, 0, 300) : IncidentRecorderOptions.Default.PostRollSeconds,
            Theme = Theme is "Light" or "Dark" ? Theme : "System",
            LastProgram = string.IsNullOrWhiteSpace(LastProgram) ? null : LastProgram.Trim(),
        };
    }
}

public sealed class SettingsStore(string filePath)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Branding.DataFolderName, "settings.json");

    public string FilePath { get; } = filePath;

    /// <summary>The saved settings, or the defaults when there is no file or it cannot be read. <paramref name="problem"/> says which.</summary>
    public AppSettings Load(out string? problem)
    {
        problem = null;
        if (!File.Exists(FilePath))
        {
            return new AppSettings();
        }

        try
        {
            using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return (JsonSerializer.Deserialize<AppSettings>(stream, Options) ?? new AppSettings()).Normalized();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            problem = "The settings file could not be read (" + e.GetType().Name + "), so the defaults are in use.";
            return new AppSettings();
        }
    }

    /// <summary>Writes through a temporary file so a crash never leaves half a file. Returns false (and says why) rather than throwing: a read-only profile must not stop the program.</summary>
    public bool TrySave(AppSettings settings, out string? problem)
    {
        problem = null;
        var temporary = FilePath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath)!);
            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(JsonSerializer.Serialize(settings.Normalized(), Options));
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, FilePath, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            problem = "The settings could not be saved (" + e.GetType().Name + ").";
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // a stray temporary file is harmless
            }
        }
    }
}
