using System.Diagnostics;
using System.Globalization;
using Jankscope.Core;
using Jankscope.Frames.Detection;
using Jankscope.Frames.PresentMon;

namespace Jankscope.Frames.Tests.PresentMon;

/// <summary>
/// Decides, once per test run, whether the integration tests against the pinned PresentMon can run on this machine, and says why not when they cannot:
/// not Windows, binary not present (it is git-ignored and fetched with the owner's approval), or this account cannot open a real-time ETW session
/// (needs Performance Log Users or administrator rights) or nothing presents frames. A binary that is present but does NOT match the lock file is not
/// a reason to skip: the tests then run and fail, because the runner refuses to start it.
/// </summary>
internal static class PresentMonAvailability
{
    private static readonly Lazy<string?> Reason = new(Probe);

    public static string? SkipReason => Reason.Value;

    private static string? Probe()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "PresentMon is a Windows tool";
        }

        PinnedExecutable pin;
        try
        {
            pin = PresentMonLockFile.Read(RunnerTestPaths.PinnedLockFile);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return "third_party/PresentMon/presentmon.lock.json could not be read (" + e.GetType().Name + ")";
        }

        if (!File.Exists(pin.Path))
        {
            return "the pinned PresentMon executable is not present (git-ignored; restore it with tools/fetch-third-party.ps1)";
        }

        using (var check = FileHashVerifier.Open(pin))
        {
            if (!check.IsVerified)
            {
                return null; // present but wrong: do not run it (not even to probe), and do not hide it either; the tests fail loudly
            }
        }

        return ProbeEtwAccess(pin);
    }

    /// <summary>One second of the verified executable with the product's session prefix, started the plain way (not through the launcher under test).</summary>
    private static string? ProbeEtwAccess(PinnedExecutable pin)
    {
        var session = "Jankscope-" + Environment.ProcessId + "-probe";
        var startInfo = new ProcessStartInfo
        {
            FileName = pin.Path,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in PresentMonArguments.Build(session, timedSeconds: 1))
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return "the pinned PresentMon could not be started";
        }

        var rows = 0;
        var error = string.Empty;
        var stdout = Task.Run(() =>
        {
            while (process.StandardOutput.ReadLine() is not null)
            {
                rows++;
            }
        });
        var stderr = Task.Run(() => process.StandardError.ReadToEnd());
        if (!process.WaitForExit(15_000))
        {
            process.Kill(entireProcessTree: true);
            return "the PresentMon probe did not finish within 15 s";
        }

        stdout.Wait(5_000);
        if (stderr.Wait(5_000))
        {
            error = stderr.Result;
        }

        if (process.ExitCode != 0)
        {
            var line = error.Split('\n').FirstOrDefault(l => l.StartsWith("error:", StringComparison.OrdinalIgnoreCase))?.Trim();
            return "this account cannot capture frames (cannot open a real-time ETW session; needs Performance Log Users or administrator): " + (line ?? "exit code " + process.ExitCode);
        }

        return rows > 1 ? null : "no frames were presented on this machine during the probe (no display session?)";
    }
}

/// <summary>A Fact that skips itself, with the reason, when <see cref="PresentMonAvailability"/> says the pinned binary cannot be exercised here.</summary>
internal sealed class RequiresPresentMonFactAttribute : FactAttribute
{
    public RequiresPresentMonFactAttribute()
    {
        if (PresentMonAvailability.SkipReason is { } reason)
        {
            Skip = reason;
        }
    }
}

/// <summary>
/// Makes the skips of the real-PresentMon tests safe (review 1a, finding 7): with <c>JANKSCOPE_REQUIRE_PRESENTMON=1</c> (set by
/// <c>build.ps1 -RequireHardwareTests</c> on the reference bench) a machine that would skip them fails this test instead, so a bench run
/// cannot go green with the real-binary tests silently not run.
/// </summary>
public class RealPresentMonCanaryTests
{
    [Fact]
    public void When_hardware_tests_are_required_the_real_PresentMon_tests_must_be_able_to_run_here()
    {
        if (Environment.GetEnvironmentVariable("JANKSCOPE_REQUIRE_PRESENTMON") != "1")
        {
            return; // an ordinary run: where the binary cannot be exercised the tests skip, with the reason on screen
        }

        Assert.Null(PresentMonAvailability.SkipReason);
    }
}

/// <summary>Wraps the product launcher to learn what the runner really started: process ids, and the exit code of every child.</summary>
internal sealed class RecordingLauncher(IProcessLauncher inner) : IProcessLauncher
{
    private readonly object _gate = new();
    private readonly List<RecordedChild> _children = [];

    public IReadOnlyList<RecordedChild> Children
    {
        get
        {
            lock (_gate)
            {
                return [.. _children];
            }
        }
    }

    public IChildProcess Start(ProcessLaunchRequest request)
    {
        var child = new RecordedChild(inner.Start(request), request);
        lock (_gate)
        {
            _children.Add(child);
        }

        return child;
    }

    internal sealed class RecordedChild(IChildProcess inner, ProcessLaunchRequest request) : IChildProcess
    {
        private int _exitCode = int.MinValue;

        public ProcessLaunchRequest Request { get; } = request;

        public bool IsTerminator => Request.Arguments.Contains("--terminate_existing_session");

        public int? ExitCode => _exitCode == int.MinValue ? null : _exitCode;

        public int ProcessId => inner.ProcessId;

        public ValueTask<ChildLine?> ReadOutputLineAsync(CancellationToken cancellationToken) => inner.ReadOutputLineAsync(cancellationToken);

        public ValueTask<ChildLine?> ReadErrorLineAsync(CancellationToken cancellationToken) => inner.ReadErrorLineAsync(cancellationToken);

        public async Task<int> WaitForExitAsync(CancellationToken cancellationToken)
        {
            var code = await inner.WaitForExitAsync(cancellationToken);
            Volatile.Write(ref _exitCode, code);
            return code;
        }

        public bool TryKill(out string? failure) => inner.TryKill(out failure);

        public void Dispose() => inner.Dispose();
    }
}

[CollectionDefinition("RealPresentMon", DisableParallelization = true)]
public sealed class RealPresentMonCollection;

/// <summary>
/// The pinned PresentMon 2.6.0 console executable, unelevated, through the real runner and the real launcher. Each test uses a session name
/// that carries this test process's id, so the "no session left" checks cannot be confused by other tools that use the product prefix.
/// </summary>
[Collection("RealPresentMon")]
[Trait("requires", "presentmon")]
public class RunnerRealBinaryTests
{
    private static string SessionBase(string label) => PresentMonArguments.NewSessionName(Environment.ProcessId, label);

    private static PinnedExecutable Pin() => PresentMonLockFile.Read(RunnerTestPaths.PinnedLockFile);

    /// <summary>Names of the real-time ETW sessions that start with <paramref name="prefix"/>, from the system's own tool.</summary>
    private static async Task<List<string>> EtwSessionsStartingWith(string prefix)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "logman.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        };
        startInfo.ArgumentList.Add("query");
        startInfo.ArgumentList.Add("-ets");
        using var process = Process.Start(startInfo)!;
        var text = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return [.. text.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))];
    }

    private static async Task AssertNoneRunning(IEnumerable<int> processIds)
    {
        var deadline = Environment.TickCount64 + 10_000;
        while (true)
        {
            var alive = processIds.Where(RunnerProcesses.IsAlive).ToList();
            if (alive.Count == 0)
            {
                return;
            }

            if (Environment.TickCount64 > deadline)
            {
                Assert.Fail("PresentMon processes are still running: " + string.Join(", ", alive));
            }

            await Task.Delay(50);
        }
    }

    [RequiresPresentMonFact]
    public async Task A_three_second_unelevated_capture_delivers_rows_and_leaves_no_process_and_no_session()
    {
        var sessionBase = SessionBase("timed");
        var launcher = new RecordingLauncher(new PresentMonProcessLauncher());
        await using var runner = new PresentMonRunner(
            new PresentMonRunnerOptions { Executable = Pin(), SessionName = sessionBase, TimedSeconds = 3, WatchdogNoOutputTimeout = TimeSpan.FromSeconds(20) },
            launcher,
            () => new TestFrameParser());

        runner.Start();
        await runner.Completion.WaitAsync(TimeSpan.FromSeconds(60));

        var health = runner.Health;
        Assert.Equal(RunnerState.Completed, health.State);
        Assert.True(health.RowsParsed > 0, $"no frame rows arrived (lines read: {health.LinesRead}, problems: {string.Join("; ", health.Problems)})");
        Assert.Equal(0, health.Restarts);
        Assert.Equal(0, health.DroppedFrames);

        var capture = Assert.Single(launcher.Children);
        Assert.Equal(0, capture.ExitCode);
        Assert.Contains("--terminate_after_timed", capture.Request.Arguments);
        Assert.DoesNotContain("--restart_as_admin", capture.Request.Arguments);
        Assert.Equal(sessionBase + "-1", capture.Request.Arguments[capture.Request.Arguments.ToList().IndexOf("--session_name") + 1]);

        await AssertNoneRunning(launcher.Children.Select(c => c.ProcessId));
        Assert.Empty(await EtwSessionsStartingWith(sessionBase));
    }

    [RequiresPresentMonFact]
    public async Task Stop_in_the_middle_of_a_capture_is_graceful_and_leaves_no_process_and_no_session()
    {
        var sessionBase = SessionBase("stop");
        var launcher = new RecordingLauncher(new PresentMonProcessLauncher());
        await using var runner = new PresentMonRunner(
            new PresentMonRunnerOptions { Executable = Pin(), SessionName = sessionBase, WatchdogNoOutputTimeout = TimeSpan.FromSeconds(20) },
            launcher,
            () => new TestFrameParser());

        runner.Start();
        await RunnerRig.Eventually(() => runner.Health.RowsParsed >= 20, "frame rows from the real executable", 30_000);
        Assert.NotEmpty(await EtwSessionsStartingWith(sessionBase)); // the session exists while capturing

        await runner.StopAsync().WaitAsync(TimeSpan.FromSeconds(60));

        var health = runner.Health;
        Assert.Equal(RunnerState.Stopped, health.State);
        Assert.DoesNotContain(health.Problems, p => p.Contains("grace period", StringComparison.Ordinal)); // PresentMon left by itself, nobody had to be killed
        Assert.Null(health.UncleanedSession);
        Assert.Equal(2, launcher.Children.Count);
        Assert.True(launcher.Children[1].IsTerminator);
        Assert.Equal(0, launcher.Children[1].ExitCode);
        await AssertNoneRunning(launcher.Children.Select(c => c.ProcessId));
        Assert.Empty(await EtwSessionsStartingWith(sessionBase));
    }

    [RequiresPresentMonFact]
    public async Task A_capture_that_the_watchdog_kills_leaves_its_session_behind_and_the_runner_removes_it()
    {
        // No process of this name exists, so PresentMon prints nothing: the watchdog fires while the real executable is alive and holds its session.
        var sessionBase = SessionBase("stall");
        var launcher = new RecordingLauncher(new PresentMonProcessLauncher());
        await using var runner = new PresentMonRunner(
            new PresentMonRunnerOptions
            {
                Executable = Pin(),
                SessionName = sessionBase,
                ProcessNames = ["jankscope-no-such-game.exe"],
                WatchdogNoOutputTimeout = TimeSpan.FromSeconds(2),
                MaxRestarts = 0,
            },
            launcher,
            () => new TestFrameParser());

        runner.Start();
        await runner.Completion.WaitAsync(TimeSpan.FromSeconds(60));

        var health = runner.Health;
        Assert.Equal(RunnerState.Failed, health.State);
        Assert.Equal(1, health.Stalls);
        Assert.Null(health.UncleanedSession);
        Assert.Equal(2, launcher.Children.Count);
        Assert.True(launcher.Children[1].IsTerminator);
        // Exit code 0 means the session existed and was ended: the killed capture did leave it behind (7 would mean "no existing sessions found").
        Assert.Equal(0, launcher.Children[1].ExitCode);
        await AssertNoneRunning(launcher.Children.Select(c => c.ProcessId));
        Assert.Empty(await EtwSessionsStartingWith(sessionBase));
    }

    [RequiresPresentMonFact]
    public async Task A_live_capture_against_the_visible_harness_detects_injected_hitches()
    {
        var harnessExe = Path.Combine(RunnerTestPaths.RepoRoot, "tools", "Jankscope.Harness", "bin", "Release", "net10.0-windows", "jankscope-harness.exe");
        if (!File.Exists(harnessExe))
        {
            return;
        }

        using var tempDir = new TempDir();
        var csvPath = tempDir.File("harness.csv");
        const int seed = 7;
        const double fps = 144.0;
        const int frames = 1_200;
        const int hitches = 4;
        const double extraMs = 45.0;
        const double jitterMs = 0.2;

        var groundTruth = SyntheticWorkload.Generate(seed, fps, frames, hitches, extraMs, jitterMs);

        var harnessInfo = new ProcessStartInfo
        {
            FileName = harnessExe,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in new[] { "run", "--seed", seed.ToString(CultureInfo.InvariantCulture), "--fps", fps.ToString(CultureInfo.InvariantCulture), "--frames", frames.ToString(CultureInfo.InvariantCulture), "--hitches", hitches.ToString(CultureInfo.InvariantCulture), "--extra-ms", extraMs.ToString(CultureInfo.InvariantCulture), "--jitter-ms", jitterMs.ToString(CultureInfo.InvariantCulture), "--out", csvPath, "--window-visible" })
        {
            harnessInfo.ArgumentList.Add(arg);
        }

        var sessionBase = SessionBase("e2e-harness");
        var launcher = new RecordingLauncher(new PresentMonProcessLauncher());
        await using var runner = new PresentMonRunner(
            new PresentMonRunnerOptions
            {
                Executable = Pin(),
                SessionName = sessionBase,
                ProcessNames = ["jankscope-harness.exe"],
                WatchdogNoOutputTimeout = TimeSpan.FromSeconds(20),
            },
            launcher,
            () => new PresentMonCsvParser(DataProvenance.Measured));

        runner.Start();

        var watch = Stopwatch.StartNew();
        while (runner.Health.State != RunnerState.Running && watch.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(50);
        }
        await Task.Delay(600);

        using var harnessProcess = Process.Start(harnessInfo);
        Assert.NotNull(harnessProcess);

        await harnessProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(0, harnessProcess.ExitCode);

        await Task.Delay(1000);
        await runner.StopAsync().WaitAsync(TimeSpan.FromSeconds(30));

        var health = runner.Health;
        Assert.Equal(RunnerState.Stopped, health.State);
        Assert.True(health.RowsParsed > 100, $"expected rows, got {health.RowsParsed}");

        var captured = new List<FrameSample>();
        await foreach (var frame in runner.Samples.ReadAllAsync())
        {
            captured.Add(frame.Sample);
        }

        Assert.NotEmpty(captured);

        // Filter out non-swapchain GDI presents (PresentMode = "Other") that Windows Forms message pumping emits alongside DXGI flip presents.
        var flipFrames = captured.Where(c => c.PresentMode != "Other").ToArray();
        var detectorResult = StutterDetector.Analyze(flipFrames);
        Assert.True(detectorResult.Events.Count >= hitches, $"expected at least {hitches} events, got {detectorResult.Events.Count}");

        await AssertNoneRunning(launcher.Children.Select(c => c.ProcessId));
        Assert.Empty(await EtwSessionsStartingWith(sessionBase));
    }
}
