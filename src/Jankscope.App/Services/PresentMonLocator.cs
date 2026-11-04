using Jankscope.Core;
using Jankscope.Frames.PresentMon;

namespace Jankscope.App.Services;

/// <summary>Finds the pinned PresentMon console executable the way the command-line tool finds its catalogs: next to the program, or by walking up to the repository.</summary>
public static class PresentMonLocator
{
    /// <summary>The lock file (the owner-approved size and SHA-256 of the one executable the product may start), or null.</summary>
    public static string? FindLockFile(string startDirectory)
    {
        for (var directory = new DirectoryInfo(startDirectory); directory is not null; directory = directory.Parent)
        {
            foreach (var relative in new[] { Path.Combine("PresentMon", "presentmon.lock.json"), Path.Combine("third_party", "PresentMon", "presentmon.lock.json") })
            {
                var candidate = Path.Combine(directory.FullName, relative);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    /// <summary>The pinned executable, or null with a reason in <paramref name="problem"/> (no lock file, an invalid lock file, or the file itself missing).</summary>
    public static PinnedExecutable? Find(string startDirectory, out string? problem)
    {
        problem = null;
        var lockFile = FindLockFile(startDirectory);
        if (lockFile is null)
        {
            problem = "The PresentMon lock file was not found. Capturing needs the pinned frame-capture tool that ships with the program.";
            return null;
        }

        try
        {
            var pin = PresentMonLockFile.Read(lockFile);
            if (!File.Exists(pin.Path))
            {
                problem = "The pinned frame-capture tool is missing: run tools/fetch-third-party.ps1 (it verifies the approved file).";
                return null;
            }

            return pin;
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            problem = "The PresentMon lock file could not be used (" + e.GetType().Name + ").";
            return null;
        }
    }

    /// <summary>The catalogs, failing closed: when they cannot be read, anti-cheat knowledge is unavailable and the program says so.</summary>
    public static CatalogSet LoadCatalogs(string startDirectory, out string? problem)
    {
        problem = null;
        var directory = CatalogSet.FindDefaultDirectory(startDirectory);
        if (directory is null)
        {
            problem = "The catalogs folder was not found, so anti-cheat knowledge is unavailable.";
            return CatalogSet.Empty;
        }

        try
        {
            return CatalogSet.Load(directory);
        }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            problem = "A catalog is invalid (" + e.GetType().Name + "), so anti-cheat knowledge is unavailable.";
            return CatalogSet.Empty;
        }
    }
}
