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

            if (Path.IsPathRooted(file) || file.Contains(':', StringComparison.Ordinal))
            {
                throw new InvalidDataException("The lock file's 'file' must be a relative path.");
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

    private static string ReadString(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(element.GetString()))
        {
            throw new InvalidDataException($"The lock file has no '{name}' string.");
        }

        return element.GetString()!;
    }
}
