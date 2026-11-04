namespace Jankscope.Frames;

/// <summary>Which generation of PresentMon metrics a log uses. 2.x metrics are referenced to CPU frame start and are not comparable with 1.x logs.</summary>
public enum FrameLogFormat
{
    Unknown,
    PresentMon2,
    PresentMon1,
}

/// <summary>Result of reading a log header. When unsupported, <see cref="UnsupportedReason"/> says exactly why.</summary>
public sealed record LogHeaderResult(
    bool Supported,
    FrameLogFormat Format,
    string? UnsupportedReason,
    IReadOnlyList<string> MissingOptionalColumns,
    IReadOnlyList<string> IgnoredColumns);

/// <summary>
/// Line-oriented, tolerant parser contract. A runner feeds it stdout lines as they arrive; a file reader feeds it a whole file.
/// Implementations must never throw on malformed input and must parse numbers with the invariant culture.
/// </summary>
public interface IFrameLogParser
{
    /// <summary>Reads the header line. Must be called once before <see cref="TryParseRow"/>.</summary>
    LogHeaderResult ReadHeader(string headerLine);

    /// <summary>Parses one data line. Returns false with a human-readable <paramref name="problem"/> for a malformed line.</summary>
    bool TryParseRow(string line, out FrameSample sample, out string? problem);
}
