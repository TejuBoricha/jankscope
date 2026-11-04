using Jankscope.Frames.Incidents;

namespace Jankscope.App.Services;

/// <summary>The audible confirmation of a mark (spec section 7.3: feedback without an overlay). Distinct tones, so a person in a game can tell them apart.</summary>
public interface IAudioCue
{
    void Marked(MarkKind kind);

    /// <summary>The key was pressed but nothing was recorded (no capture is running, or no frames have arrived yet).</summary>
    void NotRecorded();
}

public sealed class AudioCue(Func<bool> enabled) : IAudioCue
{
    public void Marked(MarkKind kind) => Play(kind == MarkKind.Stutter ? [(880, 90), (1175, 120)] : [(660, 90), (440, 140)]);

    public void NotRecorded() => Play([(220, 260)]);

    private void Play((int Hertz, int Milliseconds)[] tones)
    {
        if (!enabled())
        {
            return;
        }

        // A tone blocks for its length, so it plays on a worker; a machine without a sound device simply stays silent.
        _ = Task.Run(() =>
        {
            try
            {
                foreach (var (hertz, milliseconds) in tones)
                {
                    Console.Beep(hertz, milliseconds);
                }
            }
            catch (Exception e) when (e is PlatformNotSupportedException or IOException or InvalidOperationException)
            {
                try
                {
                    System.Media.SystemSounds.Asterisk.Play();
                }
                catch (Exception inner) when (inner is InvalidOperationException or PlatformNotSupportedException)
                {
                    // no sound device: nothing to do
                }
            }
        });
    }
}

/// <summary>A cue that stays silent and remembers what it was asked to play (tests, and a person who switched sound off).</summary>
public sealed class SilentCue : IAudioCue
{
    public List<MarkKind> MarkedKinds { get; } = [];

    public int NotRecordedCount { get; private set; }

    public void Marked(MarkKind kind) => MarkedKinds.Add(kind);

    public void NotRecorded() => NotRecordedCount++;
}
