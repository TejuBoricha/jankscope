using System.Text.Json;

namespace Jankscope.Frames.PresentMon;

/// <summary>
/// Reads the owner-approved pin from <c>third_party/PresentMon/presentmon.lock.json</c>: the file name relative to the lock
/// file, its size and its SHA-256. The lock file is the single record of what the product may launch (ADR 0003, HC-7).
/// </summary>
public static class PresentMonLockFile
{
    public static PinnedExecutable Read(string lockFilePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(lockFilePath);
        var lockFull = Path.GetFullPath(lockFilePath);

        JsonDocument document;
        try
        {
            using var input = new FileStream(lockFull, FileMode.Open, FileAccess.Read, FileShare.Read);
            document = JsonDocument.Parse(input);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException("The lock file is not valid JSON (" + e.GetType().Name + ").");
        }

        using (document)
        {
            var root = document.RootElement;
            var file = ReadString(root, "file");
            var sha256 = ReadString(root, "sha256");
            if (!root.TryGetProperty("sizeBytes", out var sizeElement) || sizeElement.ValueKind != JsonValueKind.Number || !sizeElement.TryGetInt64(out var size) || size <= 0)
            {
                throw new InvalidDataException("The lock file has no positive 'sizeBytes'.");
            }

            if (Path.IsPathRooted(file) || file.Contains(':', StringComparison.Ordinal) || file[0] is '/' or '\\')
            {
                throw new InvalidDataException("The lock file's 'file' must be a relative path.");
            }

            if (EscapesItsDirectory(file))
            {
                throw new InvalidDataException("The lock file's 'file' points outside the lock file's directory.");
            }

            var directory = Path.GetDirectoryName(lockFull)!;
            var target = Path.GetFullPath(Path.Combine(directory, file));
            var inside = Path.GetRelativePath(directory, target);
            if (inside == ".." || inside.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(inside))
            {
                throw new InvalidDataException("The lock file's 'file' points outside the lock file's directory.");
            }

            try
            {
                return new PinnedExecutable(target, size, sha256);
            }
            catch (ArgumentException e)
            {
                throw new InvalidDataException("The lock file's 'sha256' is not a SHA-256 (" + e.GetType().Name + ").");
            }
        }
    }

    /// <summary>
    /// True when walking the path's segments ever steps above the directory it starts in. Both <c>/</c> and <c>\</c> separate segments, whatever the
    /// machine: <see cref="Path"/> only treats <c>\</c> as a separator on Windows, so <c>..\evil.exe</c> would pass as a plain file name anywhere else.
    /// </summary>
    private static bool EscapesItsDirectory(string file)
    {
        var depth = 0;
        foreach (var segment in file.Split('/', '\\'))
        {
            if (segment.Length == 0 || segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (--depth < 0)
                {
                    return true;
                }
            }
            else
            {
                depth++;
            }
        }

        return false;
    }

    private static string ReadString(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(element.GetString()))
        {
            throw new InvalidDataException($"The lock file has no '{name}' string.");
        }

        return element.GetString()!;
    }
}
