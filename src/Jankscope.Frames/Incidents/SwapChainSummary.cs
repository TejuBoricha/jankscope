using System.Globalization;

namespace Jankscope.Frames.Incidents;

/// <summary>How many frames of the examined window were presented on one swap chain. <paramref name="Address"/> is the program's own memory address: kept in memory to tell chains apart, never exported.</summary>
public sealed record SwapChainUse(string Address, int Frames);

/// <summary>
/// Notices when one program presents on more than one swap chain (independent Stage 1b review, finding 7; <c>LIMITATIONS</c> L26). PresentMon measures the frame time of
/// each swap chain separately, so a program with a second chain (an overlay, a video surface) writes two interleaved series, and the slow one can look like stutter that is not
/// the game's: the review's example, a 144 fps chain beside a 1 fps chain, offered five 1,000 ms frames of the slow chain as the five worst moments. This does not change any
/// measurement; it says what is in the window and which candidates sit on a secondary chain, so that the person is not left to guess.
/// </summary>
public static class SwapChainSummary
{
    /// <summary>
    /// The chains in <paramref name="frames"/>, busiest first (ties: by address, so the answer does not depend on the order of arrival). Frames that do not say which chain they
    /// were on (an older log, a fixture) are left out: with no chain named for any frame the result is empty, because nothing can be said.
    /// </summary>
    public static IReadOnlyList<SwapChainUse> Summarize(ReadOnlySpan<FrameSample> frames)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var frame in frames)
        {
            if (frame.SwapChain is { } chain)
            {
                counts[chain] = counts.GetValueOrDefault(chain) + 1;
            }
        }

        return counts
            .Select(c => new SwapChainUse(c.Key, c.Value))
            .OrderByDescending(c => c.Frames)
            .ThenBy(c => c.Address, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The address of the chain with the most frames, or null when no chain is known. With two chains of equal size the first by address is "the main one".</summary>
    public static string? MainChain(IReadOnlyList<SwapChainUse>? uses) => uses is { Count: > 0 } ? uses[0].Address : null;

    /// <summary>
    /// One sentence for the investigator and the export, or null when there is nothing to say (no chain known, or just one). It names counts only, not addresses, so it is safe to
    /// export. The counts add up: the main chain's frames plus the others' are the frames whose chain is known.
    /// </summary>
    public static string? Notice(IReadOnlyList<SwapChainUse>? uses)
    {
        if (uses is not { Count: > 1 })
        {
            return null;
        }

        var others = uses.Skip(1).Sum(u => u.Frames);
        var inv = CultureInfo.InvariantCulture;
        return $"This program presented on {uses.Count.ToString(inv)} swap chains in the seconds before the mark: {uses[0].Frames.ToString(inv)} frame(s) on the main one and {others.ToString(inv)} on " +
            (uses.Count == 2 ? "another" : $"{(uses.Count - 1).ToString(inv)} others") +
            " (an overlay or a video surface, for example). The capture tool measures each swap chain's frame times separately, so a slow secondary chain can look like stutter below that is not the game's. " +
            "A candidate on a secondary chain says so.";
    }

    /// <summary>The words added to a candidate's description when its frame is on a chain that is not the main one.</summary>
    public const string SecondaryChainTag = " This frame is on a secondary swap chain, not the program's main one.";
}
