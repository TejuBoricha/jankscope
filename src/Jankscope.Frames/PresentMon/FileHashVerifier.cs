using System.Globalization;
using System.Security.Cryptography;

namespace Jankscope.Frames.PresentMon;

/// <summary>
/// The one executable we are allowed to start: where it is, how big it is and its SHA-256, taken from the owner-approved lock file
/// (<c>third_party/PresentMon/presentmon.lock.json</c>). The path is checked when the file is opened, so a bad path is reported as
/// a refusal with a reason rather than thrown at construction.
/// </summary>
public sealed class PinnedExecutable
{
    public PinnedExecutable(string path, long sizeBytes, string sha256)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(sha256);
        if (sizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeBytes), "The pinned size must be positive.");
        }

        if (sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("The pinned SHA-256 must be 64 hexadecimal characters.", nameof(sha256));
        }

        Path = path;
        SizeBytes = sizeBytes;
        Sha256 = sha256.ToLowerInvariant();
    }

    public string Path { get; }

    public long SizeBytes { get; }

    /// <summary>Lower-case hexadecimal.</summary>
    public string Sha256 { get; }
}

public enum ExecutableCheck
{
    Verified,
    PathNotAbsolute,
    NotFound,
    NotARegularFile,

    /// <summary>A directory above the file is a junction or symbolic link, so the path could be pointed at a different file after the check.</summary>
    PathRedirected,
    CannotOpen,
    SizeMismatch,
    HashMismatch,
}

/// <summary>Outcome of <see cref="FileHashVerifier.Open"/>. When <see cref="IsVerified"/>, the caller owns <see cref="Executable"/> and must dispose it after the launch.</summary>
public sealed record ExecutableVerification(ExecutableCheck Outcome, string Reason, VerifiedExecutable? Executable) : IDisposable
{
    public bool IsVerified => Outcome == ExecutableCheck.Verified;

    /// <summary>
    /// True for the outcomes that describe the file itself (wrong content, wrong place, not a file): repeating the attempt cannot
    /// help, so a supervisor must stop instead of retrying. <see cref="ExecutableCheck.CannotOpen"/> may be a passing sharing conflict.
    /// </summary>
    public bool IsPermanent => Outcome is not (ExecutableCheck.Verified or ExecutableCheck.CannotOpen);

    public void Dispose() => Executable?.Dispose();
}

/// <summary>
/// A file whose size and SHA-256 matched the pin, kept open with <c>FileShare.Read</c>. While this object lives, Windows refuses to
/// open the file for writing and refuses to delete or rename the file or any REAL directory above it, and no directory above it was a
/// junction or symbolic link when it was checked (a link is its own directory entry and could be deleted and recreated while the handle
/// is held, so <see cref="FileHashVerifier"/> refuses paths that go through one). The bytes that were hashed are therefore the bytes that
/// the operating system then maps when the process is created from <see cref="Path"/>.
/// <para>The constructor is internal: only <see cref="FileHashVerifier.Open(PinnedExecutable)"/> can make one, so a
/// <see cref="ProcessLaunchRequest"/> proves that the check happened.</para>
/// </summary>
public sealed class VerifiedExecutable : IDisposable
{
    private FileStream? _stream;

    internal VerifiedExecutable(FileStream stream, string path, long sizeBytes, string sha256)
    {
        _stream = stream;
        Path = path;
        SizeBytes = sizeBytes;
        Sha256 = sha256;
    }

    public string Path { get; }

    public long SizeBytes { get; }

    public string Sha256 { get; }

    public void Dispose()
    {
        _stream?.Dispose();
        _stream = null;
    }
}

/// <summary>
/// Verifies the executable before EVERY launch (HC-7). The hash is computed over the very handle that stays open across the launch,
/// not over a second open of the same path.
/// <para>
/// Residual risk, stated plainly: the process is created from the path, not from our handle, so a different file could be started
/// only if the path were redirected between our open and the launch. With the handle held, the file and its real parent directories
/// cannot be renamed, deleted or rewritten through normal file APIs (verified on Windows 11 in the tests), and the path is refused
/// when any directory above the file, or the file itself, is a junction or symbolic link. The links are checked before the open AND
/// again while the handle is held; what is left is a link swapped in and back out between those two checks, which needs write access
/// to a directory on the path. A same-user attacker is inside the trust boundary for user-level actions (spec section 6.1), and the
/// child runs unelevated, so this gains no privilege today. Install the product under a directory that standard users cannot write
/// to (docs/LIMITATIONS.md); that rule becomes a hard requirement before any elevated helper starts this code.
/// </para>
/// </summary>
public static class FileHashVerifier
{
    public static ExecutableVerification Open(PinnedExecutable pin) => Open(pin, File.GetAttributes);

    /// <summary>The attribute reader is a seam so tests can present a reparse point without needing the privilege to create one.</summary>
    internal static ExecutableVerification Open(PinnedExecutable pin, Func<string, FileAttributes> getAttributes)
    {
        ArgumentNullException.ThrowIfNull(pin);
        ArgumentNullException.ThrowIfNull(getAttributes);

        var path = pin.Path;
        if (!IsCanonicalLocalAbsolutePath(path))
        {
            return Refuse(ExecutableCheck.PathNotAbsolute,
                "The executable path must be an absolute, normalized local path (no PATH lookup, no relative path, no network or device path, no '..').");
        }

        FileStream? stream = null;
        try
        {
            FileAttributes attributes;
            try
            {
                attributes = getAttributes(path);
            }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
            {
                return Refuse(ExecutableCheck.NotFound, "The pinned executable does not exist.");
            }

            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
            {
                return Refuse(ExecutableCheck.NotARegularFile, "The pinned executable must be a regular file, not a directory, device, symbolic link or other reparse point.");
            }

            if (FirstRedirectedAncestor(path, getAttributes) is { } redirected)
            {
                return redirected;
            }

            stream = new FileStream(path, new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                Options = FileOptions.SequentialScan,
            });

            // Again, now that the handle pins the real directories above the file: a link that appeared between the first check and the open shows here.
            if (FirstRedirectedAncestor(path, getAttributes) is { } redirectedLater)
            {
                return redirectedLater;
            }

            if (!stream.CanSeek)
            {
                return Refuse(ExecutableCheck.NotARegularFile, "The pinned executable is not a seekable disk file.");
            }

            if (stream.Length != pin.SizeBytes)
            {
                return Refuse(ExecutableCheck.SizeMismatch,
                    $"The executable is {stream.Length.ToString(CultureInfo.InvariantCulture)} bytes but the pin says {pin.SizeBytes.ToString(CultureInfo.InvariantCulture)} bytes; refusing to launch.");
            }

            var actual = Convert.ToHexStringLower(SHA256.HashData(stream));
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actual), Convert.FromHexString(pin.Sha256)))
            {
                return Refuse(ExecutableCheck.HashMismatch, $"SHA-256 mismatch: the pin says {pin.Sha256} but the file hashes to {actual}; refusing to launch.");
            }

            var verified = new VerifiedExecutable(stream, path, pin.SizeBytes, actual);
            stream = null;
            return new ExecutableVerification(ExecutableCheck.Verified, "Size and SHA-256 match the pin.", verified);
        }
        catch (FileNotFoundException)
        {
            return Refuse(ExecutableCheck.NotFound, "The pinned executable does not exist.");
        }
        catch (DirectoryNotFoundException)
        {
            return Refuse(ExecutableCheck.NotFound, "The pinned executable does not exist.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Refuse(ExecutableCheck.CannotOpen,
                "The pinned executable could not be opened read-only with write protection (" + e.GetType().Name + "); another program may have it open for writing.");
        }
        finally
        {
            stream?.Dispose();
        }
    }

    private static ExecutableVerification Refuse(ExecutableCheck outcome, string reason) => new(outcome, reason, null);

    /// <summary>The refusal for the first directory above <paramref name="path"/> that is a junction or symbolic link, or null when every directory is a real one.</summary>
    private static ExecutableVerification? FirstRedirectedAncestor(string path, Func<string, FileAttributes> getAttributes)
    {
        for (var directory = Path.GetDirectoryName(path); !string.IsNullOrEmpty(directory); directory = Path.GetDirectoryName(directory))
        {
            FileAttributes attributes;
            try
            {
                attributes = getAttributes(directory);
            }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
            {
                return Refuse(ExecutableCheck.NotFound, "A directory above the pinned executable does not exist.");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return Refuse(ExecutableCheck.CannotOpen, "A directory above the pinned executable could not be inspected (" + e.GetType().Name + "); refusing to launch.");
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                return Refuse(ExecutableCheck.PathRedirected,
                    "A directory above the pinned executable is a junction or symbolic link, so the path could be pointed at a different file after the check; refusing to launch. Install the executable in a normal directory.");
            }
        }

        return null;
    }

    /// <summary>
    /// A drive-letter path in its normalized form. Rejected: relative and drive-relative paths, UNC and device paths (<c>\\server</c>,
    /// <c>\\?\</c>, <c>\\.\</c>), alternate data streams, and anything <see cref="Path.GetFullPath(string)"/> would rewrite
    /// (<c>..</c> segments, forward slashes, trailing dots or blanks).
    /// </summary>
    internal static bool IsCanonicalLocalAbsolutePath(string path)
    {
        if (path.Length < 4 || path.Contains('\0') || !Path.IsPathFullyQualified(path))
        {
            return false;
        }

        if (!(char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\'))
        {
            return false;
        }

        if (path.IndexOf(':', 2) >= 0)
        {
            return false;
        }

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        return string.Equals(full, path, StringComparison.OrdinalIgnoreCase);
    }
}
