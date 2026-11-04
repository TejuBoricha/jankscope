namespace Jankscope.Frames.Capture;

/// <summary>A program seen presenting frames during a short look, offered to the person as "track this one?".</summary>
public sealed record PresentingProgram(string Application, long Frames, double FramesPerSecond, double MedianFrameTimeMs);

/// <summary>
/// Game detection by consent (spec section 7.2): nothing is guessed from launcher libraries or user files. A short capture of every program that presents
/// frames lists them with their rates; the person picks the one to track. The list is only ever shown, never saved.
/// </summary>
public static class PresentingProgramScanner
{
    /// <summary>Groups frames by program, busiest first. Programs with no name ("&lt;unknown&gt;" or blank) are listed under that label so nothing hides.</summary>
    public static IReadOnlyList<PresentingProgram> Summarize(IEnumerable<FrameSample> frames, double durationSeconds)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(durationSeconds), durationSeconds, "must be finite and positive");
        }

        return frames
            .Where(f => f.PresentMode != "Other" && double.IsFinite(f.FrameTimeMs) && f.FrameTimeMs > 0)
            .GroupBy(f => string.IsNullOrWhiteSpace(f.Application) ? "<unknown>" : f.Application!, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var times = g.Select(f => f.FrameTimeMs).Order().ToArray();
                var median = times.Length % 2 == 1 ? times[times.Length / 2] : (times[(times.Length / 2) - 1] + times[times.Length / 2]) / 2.0;
                return new PresentingProgram(g.Key, times.Length, Math.Round(times.Length / durationSeconds, 1), Math.Round(median, 2));
            })
            .OrderByDescending(p => p.Frames)
            .ThenBy(p => p.Application, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Reads a timed capture of every program to its end and summarizes it.</summary>
    public static async Task<IReadOnlyList<PresentingProgram>> ScanAsync(IFrameSource source, double durationSeconds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var frames = new List<FrameSample>();
        source.Start();
        try
        {
            await foreach (var captured in source.Samples.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                frames.Add(captured.Sample);
            }
        }
        finally
        {
            await source.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }

        return Summarize(frames, durationSeconds);
    }
}
