using System.Text;

namespace Jankscope.Core.Tests;

internal static class Repo
{
    // third_party is scanned too: it holds pinned binaries (not matched by the source patterns), but a source file or project hidden there must not escape the lints.
    private static readonly string[] SkippedTopLevel = [".git", ".vs", "node_modules"];

    public static string Root { get; } = FindRoot();

    public static string Catalogs => Path.Combine(Root, "catalogs");

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Jankscope.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Repository root (Jankscope.slnx) not found above " + AppContext.BaseDirectory);
    }

    /// <summary>Repository-relative path with forward slashes.</summary>
    public static string Rel(string path) => Path.GetRelativePath(Root, path).Replace('\\', '/');

    /// <summary>
    /// Every file matching <paramref name="pattern"/> anywhere in the repository except VCS data and tool caches, and except
    /// build output. Build output is excluded exactly where the SDK excludes it: a <c>bin</c> or <c>obj</c> directory directly inside
    /// a project directory. A <c>bin</c> folder deeper in a project is compiled and therefore scanned (review pass 2 #3).
    /// </summary>
    public static IEnumerable<string> AllFiles(string pattern) => Enumerate(Root, pattern, isRoot: true);

    /// <summary>Same rules as <see cref="AllFiles"/> for an arbitrary directory (used to test the scope rules themselves).</summary>
    public static IEnumerable<string> FilesIn(string absoluteDirectory, string pattern) =>
        Enumerate(absoluteDirectory, pattern, isRoot: false);

    public static IEnumerable<string> Files(string subDirectory, string pattern) =>
        Enumerate(Path.Combine(Root, subDirectory), pattern, isRoot: false);

    private static IEnumerable<string> Enumerate(string directory, string pattern, bool isRoot)
    {
        var isProject = Directory.EnumerateFiles(directory, "*.csproj").Any();
        foreach (var file in Directory.EnumerateFiles(directory, pattern))
        {
            yield return file;
        }

        foreach (var sub in Directory.EnumerateDirectories(directory))
        {
            var name = Path.GetFileName(sub);
            if ((isRoot && SkippedTopLevel.Contains(name)) || (isProject && name is "bin" or "obj"))
            {
                continue;
            }

            foreach (var file in Enumerate(sub, pattern, isRoot: false))
            {
                yield return file;
            }
        }
    }
}

/// <summary>
/// Finds UTF-8 text that was decoded as a single-byte code page and saved again, by round trip rather than by a list of known artifacts.
/// A run of non-ASCII characters is cut at every character Windows-1252 cannot hold; a segment that encodes to bytes which are themselves
/// valid UTF-8 was almost certainly mis-decoded. C1 control characters (U+0080-U+009F) never occur in sound text, they are what bytes
/// 0x80-0x9F turn into under Latin-1 style corruption, so any of them is reported.
/// </summary>
internal static class Mojibake
{
    private static readonly Encoding Windows1252 = CreateWindows1252();
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly System.Text.RegularExpressions.Regex NonAsciiRun = new(@"[^\x00-\x7F]+");

    private static Encoding CreateWindows1252()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

    /// <summary>Returns the first suspicious run, or null when the text looks sound.</summary>
    public static string? Find(string text)
    {
        foreach (System.Text.RegularExpressions.Match run in NonAsciiRun.Matches(text))
        {
            if (run.Value.Any(c => c is >= (char)0x80 and <= (char)0x9F))
            {
                return run.Value;
            }

            var segment = new List<byte>();
            foreach (var c in run.Value)
            {
                if (TryEncode(c, out var b))
                {
                    segment.Add(b);
                    continue;
                }

                if (IsValidUtf8(segment))
                {
                    return run.Value;
                }

                segment.Clear(); // a character Windows-1252 cannot hold ends the segment; it does not hide its neighbours
            }

            if (IsValidUtf8(segment))
            {
                return run.Value;
            }
        }

        return null;
    }

    private static bool TryEncode(char c, out byte b)
    {
        try
        {
            var bytes = Windows1252.GetBytes(c.ToString());
            b = bytes[0];
            return bytes.Length == 1;
        }
        catch (EncoderFallbackException)
        {
            b = 0;
            return false;
        }
    }

    private static bool IsValidUtf8(List<byte> bytes)
    {
        if (bytes.Count == 0)
        {
            return false;
        }

        try
        {
            _ = StrictUtf8.GetString(bytes.ToArray());
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false; // ordinary Latin-1 text such as an accented letter or a section sign
        }
    }

    /// <summary>The corruption itself, for tests: UTF-8 bytes read as Windows-1252.</summary>
    public static string Corrupt(string text) => Windows1252.GetString(Encoding.UTF8.GetBytes(text));

    /// <summary>Latin-1 style corruption: UTF-8 bytes read as ISO-8859-1 (bytes 0x80-0x9F become C1 controls).</summary>
    public static string CorruptAsLatin1(string text) => Encoding.Latin1.GetString(Encoding.UTF8.GetBytes(text));
}
