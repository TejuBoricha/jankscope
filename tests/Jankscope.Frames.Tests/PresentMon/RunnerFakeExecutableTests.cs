using System.Diagnostics;
using Jankscope.Frames.PresentMon;

namespace Jankscope.Frames.Tests.PresentMon;

/// <summary>
/// The whole stack on real processes: PresentMonRunner + PresentMonProcessLauncher + the fake executable. Time here is real, so the timeouts are
/// short; the assertions are about what is left behind (processes, sessions) and what was actually passed to the executable.
/// </summary>
public class RunnerFakeExecutableTests
{
    private static PresentMonRunnerOptions Options(FakePresentMonInstall fake, Func<PresentMonRunnerOptions, PresentMonRunnerOptions>? change = null)
    {
        var options = new PresentMonRunnerOptions
        {
            Executable = fake.Pin,
            SessionName = "Jankscope-" + Environment.ProcessId + "-" + Guid.NewGuid().ToString("N")[..6],
            WatchdogNoOutputTimeout = TimeSpan.FromSeconds(30),
            StopGracePeriod = TimeSpan.FromSeconds(10),
            CleanupTimeout = TimeSpan.FromSeconds(20),
            RestartBackoffInitial = TimeSpan.FromMilliseconds(50),
            RestartBackoffMax = TimeSpan.FromMilliseconds(200),
            FrameQueueCapacity = 1024,
        };
        return change is null ? options : change(options);
    }

    private static async Task<List<CapturedFrame>> Drain(PresentMonRunner runner)
    {
        var frames = new List<CapturedFrame>();
        await foreach (var frame in runner.Samples.ReadAllAsync())
        {
            frames.Add(frame);
        }

        return frames;
    }

    private static PresentMonRunner NewRunner(PresentMonRunnerOptions options) =>
        new(options, new PresentMonProcessLauncher(), () => new TestFrameParser());

    [Fact]
    public async Task A_normal_capture_streams_rows_from_a_real_child_and_finishes_cleanly()
    {
        using var fake = new FakePresentMonInstall("rows=6");
        var options = Options(fake, o => o with { TimedSeconds = 1, ProcessNames = ["My Game & Co; Edition.exe"] });
        await using var runner = NewRunner(options);

        runner.Start();
        await runner.Completion.WaitAsync(TimeSpan.FromSeconds(60));

        var health = runner.Health;
        Assert.Equal(RunnerState.Completed, health.State);
        Assert.Equal(6, health.RowsParsed);
        Assert.Empty(health.Problems);
        Assert.Contains(health.Notes, n => n.Contains("requires elevated privilege", StringComparison.Ordinal));
        Assert.Equal(Enumerable.Range(0, 6).Select(i => (long)i), (await Drain(runner)).Select(f => f.Sample.Index));

        var launch = Assert.Single(fake.Launches);
        Assert.Equal(
        [
            "--output_stdout", "--no_console_stats", "--session_name", health.SessionName!,
            "--process_name", "My Game & Co; Edition.exe", "--timed", "1", "--terminate_after_timed",
        ], launch.Args);
        Assert.DoesNotContain("--restart_as_admin", launch.Args);
        Assert.Empty(fake.LiveSessions);
        await fake.AssertAllLaunchedProcessesGone();
    }

    [Fact]
    public async Task A_changed_executable_is_never_started()
    {
        using var fake = new FakePresentMonInstall();
        fake.TamperWithExecutable();
        await using var runner = NewRunner(Options(fake));

        runner.Start();
        await runner.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(RunnerState.Failed, runner.Health.State);
        Assert.Contains("SHA-256 mismatch", runner.Health.LastError);
        Assert.True(fake.NeverLaunched, "the fake would have written its argv log if it had been started");
    }

    [Fact]
    public async Task A_hung_child_is_killed_by_the_watchdog_its_leaked_session_is_removed_and_the_restart_is_bounded()
    {
        using var fake = new FakePresentMonInstall("mode=hang", "headerFirst=true");
        var options = Options(fake, o => o with { WatchdogNoOutputTimeout = TimeSpan.FromSeconds(1.5), MaxRestarts = 1 });
        await using var runner = NewRunner(options);

        runner.Start();
        await runner.Completion.WaitAsync(TimeSpan.FromSeconds(60));

        var health = runner.Health;
        Assert.Equal(RunnerState.Failed, health.State);
        Assert.Equal(2, health.Stalls);
        Assert.Equal(1, health.Restarts);
        Assert.Contains("stalled", health.LastError);
        Assert.Equal(2, fake.Captures.Count);
        Assert.Equal(2, fake.Launches.Count(l => l.Args.Contains("--terminate_existing_session"))); // one clean-up per killed capture
        Assert.Empty(fake.LiveSessions);
        Assert.Null(health.UncleanedSession);
        await fake.AssertAllLaunchedProcessesGone();
    }

    [Fact]
    public async Task A_crash_then_a_good_run_restarts_once_and_each_run_has_its_own_session()
    {
        using var fake = new FakePresentMonInstall("mode.1=crash", "crashAfterRows.1=2", "exitCode.1=3", "mode.2=normal", "rows.2=4");
        var options = Options(fake, o => o with { TimedSeconds = 1 });
        await using var runner = NewRunner(options);

        runner.Start();
        await runner.Completion.WaitAsync(TimeSpan.FromSeconds(60));

        var health = runner.Health;
        Assert.Equal(RunnerState.Completed, health.State);
        Assert.Equal(1, health.Restarts);
        Assert.Contains(health.Problems, p => p.Contains("exited with code 3", StringComparison.Ordinal));
        Assert.Equal(2 + 4, health.RowsParsed);

        var frames = await Drain(runner);
        Assert.Equal([1, 1, 2, 2, 2, 2], frames.Select(f => f.Run));
        var sessions = fake.Captures.Select(c => c.Args[c.Args.ToList().IndexOf("--session_name") + 1]).ToList();
        Assert.Equal(2, sessions.Distinct().Count());
        Assert.All(sessions, s => Assert.StartsWith("Jankscope-", s, StringComparison.Ordinal));
        Assert.Empty(fake.LiveSessions); // the crashed run's session was removed by name
        await fake.AssertAllLaunchedProcessesGone();
    }

    [Fact]
    public async Task Stop_ends_a_running_capture_gracefully_through_its_own_session_and_leaves_nothing()
    {
        using var fake = new FakePresentMonInstall("rows=20", "rowDelayMs=10", "holdOpen=true");
        await using var runner = NewRunner(Options(fake));

        runner.Start();
        await RunnerRig.Eventually(() => runner.Health.RowsParsed >= 5, "some rows", 30_000);
        var stopwatch = Stopwatch.StartNew();
        await runner.StopAsync().WaitAsync(TimeSpan.FromSeconds(60));

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(8), "a graceful stop is quick: " + stopwatch.Elapsed);
        var health = runner.Health;
        Assert.Equal(RunnerState.Stopped, health.State);
        Assert.DoesNotContain(health.Problems, p => p.Contains("grace period", StringComparison.Ordinal)); // nobody had to be killed
        Assert.Empty(fake.LiveSessions);
        await fake.AssertAllLaunchedProcessesGone();
        Assert.Single(fake.Launches, l => l.Args.Contains("--terminate_existing_session"));
    }

    [Fact]
    public async Task Stop_kills_a_child_that_ignores_the_graceful_request_after_the_grace_period()
    {
        using var fake = new FakePresentMonInstall("rows=3", "holdOpen=true", "ignoreTerminate=true");
        var options = Options(fake, o => o with { StopGracePeriod = TimeSpan.FromMilliseconds(600) });
        await using var runner = NewRunner(options);

        runner.Start();
        await RunnerRig.Eventually(() => runner.Health.RowsParsed >= 3, "the rows", 30_000);
        await runner.StopAsync().WaitAsync(TimeSpan.FromSeconds(60));

        var health = runner.Health;
        Assert.Equal(RunnerState.Stopped, health.State);
        Assert.Contains(health.Problems, p => p.Contains("did not leave within the grace period", StringComparison.Ordinal));
        Assert.Empty(fake.LiveSessions);
        await fake.AssertAllLaunchedProcessesGone();
    }

    [Fact]
    public async Task Slow_but_steady_output_does_not_trip_the_watchdog()
    {
        // 30 rows, one every 250 ms: 7.5 seconds in total, longer than the 5 s watchdog, but never 5 s of silence. The margins are wide on purpose: the
        // child is a .NET program that starts cold on a loaded two-processor CI runner, so its first row can come well over a second after the launch, and
        // a 2 s watchdog tripped there once (CI run 41830266693: Stalls was 1 on a push to main, while the same code passed in the pull request run).
        // The property under test (steady output is not a stall) does not need a tight margin.
        using var fake = new FakePresentMonInstall("rows=30", "rowDelayMs=250", "holdOpen=true");
        var options = Options(fake, o => o with { WatchdogNoOutputTimeout = TimeSpan.FromSeconds(5) });
        await using var runner = NewRunner(options);

        runner.Start();
        await RunnerRig.Eventually(() => runner.Health.RowsParsed == 30, "all rows", 60_000);

        Assert.Equal(0, runner.Health.Stalls);
        Assert.Equal(RunnerState.Running, runner.Health.State);
        await runner.StopAsync().WaitAsync(TimeSpan.FromSeconds(60));
        Assert.Empty(fake.LiveSessions);
        await fake.AssertAllLaunchedProcessesGone();
    }

    [Fact]
    public async Task Disposing_the_runner_while_a_real_child_runs_leaves_no_process_and_no_session()
    {
        using var fake = new FakePresentMonInstall("rows=5", "holdOpen=true");
        var runner = NewRunner(Options(fake));
        runner.Start();
        await RunnerRig.Eventually(() => runner.Health.RowsParsed >= 5, "the rows", 30_000);

        await runner.DisposeAsync();

        Assert.Equal(RunnerState.Stopped, runner.Health.State);
        Assert.Empty(fake.LiveSessions);
        await fake.AssertAllLaunchedProcessesGone();
    }
}
