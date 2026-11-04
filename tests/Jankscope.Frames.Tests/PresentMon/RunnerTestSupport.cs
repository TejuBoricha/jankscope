using System.Security.Cryptography;
using Jankscope.Frames.PresentMon;

namespace Jankscope.Frames.Tests.PresentMon;

/// <summary>Where the repository is, found the same way the Core tests find it (the folder that holds Jankscope.slnx).</summary>
internal static class RunnerTestPaths
{
    public static string RepoRoot { get; } = FindRoot();

    public static string PinnedLockFile => Path.Combine(RepoRoot, "third_party", "PresentMon", "presentmon.lock.json");

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
}

/// <summary>A private temporary directory that is removed (best effort) when the test ends.</summary>
internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "jankscope-runner-tests-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    /// <summary>Writes a file with the given bytes and returns its path.</summary>
    public string Write(string name, byte[] bytes)
    {
        var path = File(name);
        System.IO.File.WriteAllBytes(path, bytes);
        return path;
    }

    public static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // best effort: a test that left a handle open must not fail the run a second time
        }
    }
}

/// <summary>Xunit v2 has no run-time skip, so the decision is made when the attribute is read: a test that cannot run says why.</summary>
internal sealed class SymbolicLinkFactAttribute : FactAttribute
{
    private static readonly Lazy<string?> Reason = new(Probe);

    public SymbolicLinkFactAttribute()
    {
        if (Reason.Value is { } reason)
        {
            Skip = reason;
        }
    }

    private static string? Probe()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "jankscope-symlink-probe-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var target = System.IO.Path.Combine(dir, "t.bin");
            File.WriteAllBytes(target, [1]);
            File.CreateSymbolicLink(System.IO.Path.Combine(dir, "l.bin"), target);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return "this account cannot create symbolic links (needs Developer Mode or the symbolic-link privilege): " + e.GetType().Name;
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // best effort
            }
        }
    }
}

/// <summary>One recorded start of the fake executable, as the fake wrote it to its argv log.</summary>
internal sealed record FakeLaunch(int Pid, int Ordinal, IReadOnlyList<string> Args, string Cwd, IReadOnlyList<string> EnvNames, bool StdinAtEnd);

/// <summary>
/// A private copy of the fake PresentMon executable (tools/Jankscope.FakePresentMon) with its own control file, argv log and session markers, so
/// tests run in parallel without seeing each other. The pin is computed from the copy, so the real hash check runs on the real file.
/// </summary>
internal sealed class FakePresentMonInstall : IDisposable
{
    public FakePresentMonInstall(params string[] control)
    {
        foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory, "Jankscope.FakePresentMon.*").Where(f => !f.EndsWith(".pdb", StringComparison.Ordinal)))
        {
            File.Copy(file, Dir.File(Path.GetFileName(file)));
        }

        ExePath = Dir.File("Jankscope.FakePresentMon.exe");
        var bytes = File.ReadAllBytes(ExePath);
        Pin = new PinnedExecutable(ExePath, bytes.Length, TempDir.Sha256Hex(bytes));
        SetControl(control);
    }

    public TempDir Dir { get; } = new();

    public string ExePath { get; }

    public PinnedExecutable Pin { get; }

    /// <summary>The fake, verified against its pin the way the runner does it. The caller owns the result (it holds the file open) and disposes it after the launch.</summary>
    public VerifiedExecutable Verify()
    {
        var check = FileHashVerifier.Open(Pin);
        Assert.True(check.IsVerified, check.Reason);
        return check.Executable!;
    }

    /// <summary>Starts the fake through the real launcher with the file held across the start, exactly as the runner does.</summary>
    public IChildProcess Launch(IReadOnlyList<string> arguments)
    {
        using var verified = Verify();
        return new PresentMonProcessLauncher().Start(new ProcessLaunchRequest(verified, arguments));
    }

    private string ArgvLog => Dir.File("fake-presentmon.argv.jsonl");

    public void SetControl(params string[] lines) => File.WriteAllLines(Dir.File("fake-presentmon.control"), lines);

    /// <summary>True when the fake has never been started.</summary>
    public bool NeverLaunched => !File.Exists(ArgvLog);

    public IReadOnlyList<FakeLaunch> Launches
    {
        get
        {
            if (!File.Exists(ArgvLog))
            {
                return [];
            }

            using var stream = new FileStream(ArgvLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var launches = new List<FakeLaunch>();
            while (reader.ReadLine() is { Length: > 0 } line)
            {
                System.Text.Json.JsonDocument doc;
                try
                {
                    doc = System.Text.Json.JsonDocument.Parse(line);
                }
                catch (System.Text.Json.JsonException)
                {
                    continue; // the fake was ended in the middle of writing this line (review 1a, finding 21a)
                }

                using var docScope = doc;
                var root = doc.RootElement;
                launches.Add(new FakeLaunch(
                    root.GetProperty("pid").GetInt32(),
                    root.GetProperty("ordinal").GetInt32(),
                    [.. root.GetProperty("args").EnumerateArray().Select(a => a.GetString()!)],
                    root.GetProperty("cwd").GetString()!,
                    [.. root.GetProperty("envNames").EnumerateArray().Select(a => a.GetString()!)],
                    root.GetProperty("stdinAtEnd").GetBoolean()));
            }

            return launches;
        }
    }

    public IReadOnlyList<FakeLaunch> Captures => [.. Launches.Where(l => l.Ordinal > 0)];

    /// <summary>The ETW sessions the fake still "owns" (marker files): what a real PresentMon would leave behind in the kernel.</summary>
    public IReadOnlyList<string> LiveSessions
    {
        get
        {
            var directory = Dir.File("sessions");
            return Directory.Exists(directory) ? [.. Directory.EnumerateFiles(directory, "*.session").Select(Path.GetFileName).Cast<string>()] : [];
        }
    }

    public void TamperWithExecutable()
    {
        var bytes = File.ReadAllBytes(ExePath);
        bytes[bytes.Length / 2] ^= 0xFF;
        File.WriteAllBytes(ExePath, bytes);
    }

    /// <summary>Waits until no process with any of the recorded ids is left (a recorded id is only trusted within one test run, where reuse is not a concern).</summary>
    public async Task AssertAllLaunchedProcessesGone(int timeoutMs = 10_000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (true)
        {
            var alive = Launches.Select(l => l.Pid).Where(RunnerProcesses.IsAlive).ToList();
            if (alive.Count == 0)
            {
                return;
            }

            if (Environment.TickCount64 > deadline)
            {
                Assert.Fail("These fake PresentMon processes are still running: " + string.Join(", ", alive));
            }

            await Task.Delay(20);
        }
    }

    public void Dispose()
    {
        // Never leave a stray fake behind, whatever the test did.
        foreach (var pid in Launches.Select(l => l.Pid).Where(RunnerProcesses.IsAlive))
        {
            RunnerProcesses.KillQuietly(pid);
        }

        Dir.Dispose();
    }
}

/// <summary>Test-side process checks (test code is not product code: the Process type is fine here).</summary>
internal static class RunnerProcesses
{
    public static bool IsAlive(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public static void KillQuietly(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            process.Kill(entireProcessTree: true);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // already gone
        }
    }
}
