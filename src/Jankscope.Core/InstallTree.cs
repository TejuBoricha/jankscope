namespace Jankscope.Core;

/// <summary>
/// Where the program looks for the files that ship with it (the catalogs, the pinned capture tool's lock file): its own folder and the folders above it,
/// so a build inside the repository finds the repository's copies.
/// <para>
/// The root of the drive is never offered. A standard user can usually create a folder directly under <c>C:\</c>, so a search that reached the root would
/// honour <c>C:\catalogs</c> or <c>C:\third_party\PresentMon</c> whenever nothing nearer existed, and with them a stranger's anti-cheat knowledge or pin
/// (review of Stage 1b, finding 15). Folders below the root belong to an install or to a person's own profile.
/// </para>
/// </summary>
public static class InstallTree
{
    /// <summary><paramref name="startDirectory"/> and every folder above it, nearest first, without the root of the drive. Nothing is read from disk.</summary>
    public static IEnumerable<string> SearchDirectories(string startDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(startDirectory);
        for (var directory = new DirectoryInfo(startDirectory); directory?.Parent is not null; directory = directory.Parent)
        {
            yield return directory.FullName;
        }
    }
}
