using Jankscope.Core;
using Jankscope.Frames.PresentMon;

namespace Jankscope.Frames.Tests.PresentMon;

/// <summary>
/// The supervisor against an in-memory launcher and a manual clock: hash check before every launch, watchdog, restart with back-off,
/// back-pressure, graceful and forced stop, and the guarantee that nothing (child or ETW session) is left behind.
/// </summary>
public class RunnerSupervisionTests
{
    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Every wait on the runner is bounded: a regression that stops the runner from completing then FAILS the test with a timeout instead of hanging the run
    /// (review 1a, finding 21d; the one-off Frames hang was never traced). The runner's own clock is manual, so this bound is real time and generous.
    /// </summary>
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    private static async Task<List<CapturedFrame>> Drain(PresentMonRunner runner)
    {
        var frames = new List<CapturedFrame>();
        using var bound = new CancellationTokenSource(Bound);
        await foreach (var frame in runner.Samples.ReadAllAsync(bound.Token))
        {
            frames.Add(frame);
        }

        return frames;
    }

    private static Task BackingOffFor(RunnerRig rig, TimeSpan delay) =>
        RunnerRig.Eventually(() => rig.Health.State == RunnerState.BackingOff && rig.Clock.NextDueIn == delay, $"back-off of {delay}");

    // ---- the happy path and what the runner hands out ----

    [Fact]
    public async Task A_timed_capture_delivers_its_frames_and_completes_without_a_restart()
    {
        await using var rig = new RunnerRig(o => o with { TimedSeconds = 5, ProcessNames = ["game.exe"] });
        rig.Launcher.EnqueueCapture(c =>
        {
            c.WriteHeaderAndRows(3);
            c.Exit(0);
        });

        rig.Runner.Start();
        await rig.WaitForCompletion();

        var health = rig.Health;
        Assert.Equal(RunnerState.Completed, health.State);
        Assert.Equal(0, health.Restarts);
        Assert.Null(health.LastError);
        Assert.Equal(4, health.LinesRead);
        Assert.Equal(3, health.RowsParsed);
        Assert.Empty(health.Problems);
        Assert.Equal(0, health.DroppedFrames);
        Assert.Equal("Jankscope-4242-1", health.SessionName);

        var frames = await Drain(rig.Runner);
        Assert.Equal([0L, 1L, 2L], frames.Select(f => f.Sample.Index));
        Assert.All(frames, f => Assert.Equal(1, f.Run));
        Assert.Equal([7.0, 14.0, 21.0], frames.Select(f => f.Sample.TimestampMs));
        Assert.All(frames, f => Assert.Equal(6.9, f.Sample.FrameTimeMs));

        var launch = Assert.Single(rig.Launcher.Children);
        Assert.Equal(
        [
            "--output_stdout", "--no_console_stats", "--session_name", "Jankscope-4242-1",
            "--process_name", "game.exe", "--timed", "5", "--terminate_after_timed",
        ], launch.Request.Arguments);
        Assert.Equal(rig.ExecutablePath, launch.Request.ExecutablePath);
        rig.AssertNothingLeftBehind();
    }

    [Fact]
    public async Task A_restart_starts_a_new_run_whose_frames_say_so_and_whose_parser_is_fresh()
    {
        await using var rig = new RunnerRig(o => o with { TimedSeconds = 5 });
        rig.Launcher.EnqueueCapture(c =>
        {
            c.WriteHeaderAndRows(2);
            c.Exit(3);
        });
        rig.Launcher.EnqueueCapture(c =>
        {
            c.WriteHeaderAndRows(2);
            c.Exit(0);
        });

        rig.Runner.Start();
        await BackingOffFor(rig, Second);
        rig.Clock.Advance(Second);
        await rig.WaitForCompletion();

        var frames = await Drain(rig.Runner);
        Assert.Equal([1, 1, 2, 2], frames.Select(f => f.Run));
        Assert.Equal([0L, 1L, 0L, 1L], frames.Select(f => f.Sample.Index));
        Assert.Equal(1, rig.Health.Restarts);
        Assert.Equal(RunnerState.Completed, rig.Health.State);
        Assert.Equal(["Jankscope-4242-1", "Jankscope-4242-2"], rig.Launcher.Captures.Select(c => c.Session));
        rig.AssertNothingLeftBehind();
    }

    [Fact]
    public async Task Every_session_name_carries_the_product_prefix_and_no_launch_ever_contains_restart_as_admin()
    {
        await using var rig = new RunnerRig(o => o with { ProcessNames = ["a b & c; d.exe"], TerminateOnProcessExit = true, TimedSeconds = 9 });
        rig.Launcher.DefaultCapture = c =>
        {
            c.WriteLine(SampleLog.Header);
            c.Exit(3);
        };

        rig.Runner.Start();
        for (var i = 1; i <= 3; i++)
        {
            await BackingOffFor(rig, TimeSpan.FromSeconds(Math.Pow(2, i - 1)));
            rig.Clock.Advance(TimeSpan.FromSeconds(Math.Pow(2, i - 1)));
        }

        await rig.WaitForCompletion();

        Assert.Equal(4, rig.Launcher.Captures.Count);
        Assert.All(rig.Launcher.Children, c =>
        {
            Assert.StartsWith(Branding.EtwSessionPrefix, c.Session, StringComparison.Ordinal);
            Assert.DoesNotContain(c.Request.Arguments, a => a.Equals("--restart_as_admin", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(c.Request.Arguments, a => a.Equals("--stop_existing_session", StringComparison.OrdinalIgnoreCase));
        });
        Assert.Equal(4, rig.Launcher.Captures.Select(c => c.Session).Distinct().Count());
        Assert.All(rig.Launcher.Captures, c => Assert.Contains("a b & c; d.exe", c.Request.Arguments));
        Assert.All(rig.Launcher.Terminators, t => Assert.Equal(["--terminate_existing_session", "--session_name", t.Session], t.Request.Arguments));
    }

    // ---- hash verification before every launch ----

    [Fact]
    public async Task A_file_that_does_not_match_the_pin_is_never_launched_and_the_runner_gives_up_at_once()
    {
        await using var rig = new RunnerRig();
        rig.TamperWithExecutable();

        rig.Runner.Start();
        await rig.WaitForCompletion();

        Assert.Empty(rig.Launcher.Children);
        Assert.Equal(RunnerState.Failed, rig.Health.State);
        Assert.Equal(0, rig.Health.Restarts);
        Assert.Contains("SHA-256 mismatch", rig.Health.LastError);
        Assert.Contains(rig.Health.Problems, p => p.Contains("Refused to start PresentMon", StringComparison.Ordinal));
        rig.AssertNothingLeftBehind();
    }

    [Fact]
    public async Task The_hash_is_checked_again_before_every_restart_not_only_before_the_first_launch()
    {
        await using var rig = new RunnerRig();
        rig.Launcher.EnqueueCapture(c =>
        {
            c.WriteHeaderAndRows(1);
            c.Exit(3);
        });

        rig.Runner.Start();
        await BackingOffFor(rig, Second);
        Assert.Single(rig.Launcher.Captures);

        rig.TamperWithExecutable(); // the first launch was fine; the file changes while the runner waits
        rig.Clock.Advance(Second);
        await rig.WaitForCompletion();

        Assert.Single(rig.Launcher.Captures);
        Assert.Equal(RunnerState.Failed, rig.Health.State);
        Assert.Contains("SHA-256 mismatch", rig.Health.LastError);
    }

    [Fact]
    public async Task A_helper_launch_that_ends_a_session_is_verified_too()
    {
        await using var rig = new RunnerRig();
        rig.Launcher.EnqueueCapture(c => c.WriteHeaderAndRows(1));
        rig.Runner.Start();
        await RunnerRig.Eventually(() => rig.Health.RowsParsed == 1, "first row");

        rig.TamperWithExecutable();
        await rig.Runner.StopAsync().WaitAsync(Bound);

        Assert.Empty(rig.Launcher.Terminators); // the verification failed, so no helper was started
        Assert.Contains(rig.Health.Problems, p => p.Contains("end its session", StringComparison.Ordinal));
        Assert.Equal(RunnerState.Stopped, rig.Health.State);
        Assert.True(rig.Launcher.Captures[0].Killed); // without a graceful path the tree is ended
        Assert.Equal("Jankscope-4242-1", rig.Health.UncleanedSession); // and the leak is reported, not hidden
    }

    [Fact]
    public async Task A_relative_pinned_path_is_refused_and_nothing_launches()
    {
        using var dir = new TempDir();
        var bytes = new byte[100];
        dir.Write("pm.exe", bytes);
        var previous = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = dir.Path;
            var options = new PresentMonRunnerOptions { Executable = new PinnedExecutable("pm.exe", 100, TempDir.Sha256Hex(bytes)), SessionName = "Jankscope-1" };
            var launcher = new FakeProcessLauncher();
            await using var runner = new PresentMonRunner(options, launcher, () => new TestFrameParser(), new ManualTimeProvider());
            runner.Start();
            await runner.Completion.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Empty(launcher.Children);
            Assert.Equal(RunnerState.Failed, runner.Health.State);
            Assert.Contains("absolute", runner.Health.LastError);
        }
        finally
        {
            Environment.CurrentDirectory = previous;
        }
    }

    // ---- watchdog ----

    [Fact]
    public async Task The_watchdog_ends_a_child_that_prints_nothing_for_too_long_and_a_restart_follows_after_the_back_off()
    {
        await using var rig = new RunnerRig();
        rig.Launcher.EnqueueCapture(c => c.WriteLine(SampleLog.Header)); // header, then silence
        rig.Launcher.EnqueueCapture(c => c.WriteHeaderAndRows(2));

        rig.Runner.Start();
        await RunnerRig.Eventually(() => rig.Health.State == RunnerState.Running && rig.Clock.PendingTimers >= 1, "the watchdog to be armed");

        rig.Clock.Advance(TimeSpan.FromSeconds(9.9));
        Assert.Equal(RunnerState.Running, rig.Health.State);
        Assert.False(rig.Launcher.Captures[0].Killed);

        rig.Clock.Advance(TimeSpan.FromSeconds(0.1));
        await BackingOffFor(rig, Second);

        var health = rig.Health;
        Assert.Equal(1, health.Stalls);
        Assert.Equal(1, health.Restarts);
        Assert.Contains("stalled", health.LastError);
        Assert.Contains(health.Problems, p => p.StartsWith("stalled", StringComparison.Ordinal));
        Assert.True(rig.Launcher.Captures[0].Killed);
        Assert.Empty(rig.Launcher.LiveSessions); // the killed capture left its session; the runner removed it by name
        Assert.Single(rig.Launcher.Terminators);

        rig.Clock.Advance(Second);
        await RunnerRig.Eventually(() => rig.Health.RowsParsed == 2 && rig.Health.State == RunnerState.Running, "the second run to produce rows");
        Assert.Equal(2, rig.Launcher.Captures.Count);

        await rig.Runner.StopAsync().WaitAsync(Bound);
        Assert.Equal(RunnerState.Stopped, rig.Health.State);
        rig.AssertNothingLeftBehind();
    }

    [Fact]
    public async Task Without_restarts_a_stall_ends_the_runner_in_the_failed_state_with_the_stall_as_the_reason()
    {
        await using var rig = new RunnerRig(o => o with { MaxRestarts = 0 });
        rig.Launcher.DefaultCapture = c => c.WriteLine(SampleLog.Header);

        rig.Runner.Start();
        await rig.AdvanceWhenArmed(TimeSpan.FromSeconds(10));
        await rig.WaitForCompletion();

        Assert.Equal(RunnerState.Failed, rig.Health.State);
        Assert.Equal(1, rig.Health.Stalls);
        Assert.StartsWith("stalled: no output from PresentMon for 10 s", rig.Health.LastError, StringComparison.Ordinal);
        rig.AssertNothingLeftBehind();
    }

    [Fact]
    public async Task Output_keeps_the_watchdog_quiet_however_long_the_capture_runs()
    {
        await using var rig = new RunnerRig();
        FakeChild? child = null;
        rig.Launcher.EnqueueCapture(c =>
        {
            child = c;
            c.WriteLine(SampleLog.Header);
        });

        rig.Runner.Start();
        await RunnerRig.Eventually(() => rig.Health.State == RunnerState.Running && rig.Clock.PendingTimers >= 1, "the watchdog to be armed");

        for (var i = 0; i < 6; i++) // 36 seconds in total, never 10 seconds of silence
        {
            rig.Clock.Advance(TimeSpan.FromSeconds(6));
            var before = rig.Health.LinesRead;
            child!.WriteLine(SampleLog.Row(i));
            await RunnerRig.Eventually(() => rig.Health.LinesRead > before, "the row to be read");
            await RunnerRig.Eventually(() => rig.Clock.PendingTimers >= 1, "the watchdog to re-arm");
        }

        Assert.Equal(RunnerState.Running, rig.Health.State);
        Assert.Equal(0, rig.Health.Stalls);
        Assert.False(child!.Killed);
        await rig.Runner.StopAsync().WaitAsync(Bound);
        rig.AssertNothingLeftBehind();
    }

    [Fact]
    public async Task The_watchdog_can_be_switched_off()
    {
        await using var rig = new RunnerRig(o => o with { WatchdogNoOutputTimeout = Timeout.InfiniteTimeSpan });
        rig.Launcher.EnqueueCapture(c => c.WriteLine(SampleLog.Header));

        rig.Runner.Start();
        await RunnerRig.Eventually(() => rig.Health.State == RunnerState.Running && rig.Health.LinesRead == 1, "the header");
        rig.Clock.Advance(TimeSpan.FromHours(10));
        await Task.Delay(50);

        Assert.Equal(RunnerState.Running, rig.Health.State);
        Assert.Equal(0, rig.Health.Stalls);
        await rig.Runner.StopAsync().WaitAsync(Bound);
        rig.AssertNothingLeftBehind();
    }

    [Fact]
    public async Task A_kill_that_fails_is_reported_and_the_runner_still_ends_cleanly()
    {
        await using var rig = new RunnerRig(o => o with { MaxRestarts = 0 });
        rig.Launcher.EnqueueCapture(c =>
        {
            c.WriteLine(SampleLog.Header);
            c.KillFails = true;
        });

        rig.Runner.Start();
        await rig.AdvanceWhenArmed(TimeSpan.FromSeconds(10)); // watchdog fires, the kill fails
        await RunnerRig.Eventually(() => rig.Health.Problems.Any(p => p.Contains("access denied", StringComparison.Ordinal)), "the kill failure to be reported");
        await rig.DriveClockUntilCompleted(TimeSpan.FromSeconds(5)); // the wait for the child to disappear runs out, then the final release ends it

        Assert.Contains(rig.Health.Problems, p => p.Contains("still running after it was killed", StringComparison.Ordinal));
        Assert.Equal(RunnerState.Failed, rig.Health.State);
        Assert.True(rig.Launcher.Captures[0].HasExited); // the run's final release ended it
    }

    // ---- crashes, restarts, back-off ----

    [Fact]
    public async Task Crashes_restart_with_exponential_back_off_and_the_runner_gives_up_after_the_limit()
    {
        await using var rig = new RunnerRig();
        rig.Launcher.DefaultCapture = c =>
        {
            c.WriteLine(SampleLog.Header);
            c.WriteError("error: something went wrong");
            c.Exit(3);
        };

        rig.Runner.Start();
        var expected = new[] { 1, 2, 4 };
        for (var i = 0; i < expected.Length; i++)
        {
            var delay = TimeSpan.FromSeconds(expected[i]);
            await BackingOffFor(rig, delay);
            Assert.Equal(i + 1, rig.Health.Restarts);
            Assert.Equal(i + 1, rig.Launcher.Captures.Count);

            rig.Clock.Advance(delay - TimeSpan.FromMilliseconds(1));
            await Task.Delay(30); // give a wrongly early restart the chance to happen
            Assert.Equal(i + 1, rig.Launcher.Captures.Count);

            rig.Clock.Advance(TimeSpan.FromMilliseconds(1));
        }

        await rig.WaitForCompletion();

        var health = rig.Health;
        Assert.Equal(RunnerState.Failed, health.State);
        Assert.Equal(3, health.Restarts);
        Assert.Equal(4, rig.Launcher.Captures.Count);
        Assert.Contains("gave up after 3 restart(s) in a row", health.LastError);
        Assert.Contains("exited with code 3", health.LastError);
        Assert.Contains("error: something went wrong", health.LastError);
        Assert.Equal(4, health.ProblemCount);
        Assert.Equal(4, rig.Launcher.Terminators.Count); // a crashed capture may leave its session: it is ended every time
        rig.AssertNothingLeftBehind();
    }

    [Fact]
    public async Task A_capture_that_ran_healthily_for_a_while_gets_a_fresh_restart_budget()
    {
        await using var rig = new RunnerRig(o => o with { MaxRestarts = 1 });
        FakeChild? second = null;
        rig.Launcher.EnqueueCapture(c =>
        {
            c.WriteLine(SampleLog.Header);
            c.Exit(3);
        });
        rig.Launcher.EnqueueCapture(c =>
        {
            second = c;
            c.WriteHeaderAndRows(1);
        });

        rig.Runner.Start();
        await BackingOffFor(rig, Second);
        rig.Clock.Advance(Second);
        await RunnerRig.Eventually(() => second is not null && rig.Health.RowsParsed == 1, "the second run");

        for (var i = 0; i < 5; i++) // 40 seconds of steady output: longer than StableRunDuration
        {
            await RunnerRig.Eventually(() => rig.Clock.PendingTimers >= 1, "the watchdog to be armed");
            rig.Clock.Advance(TimeSpan.FromSeconds(8));
            var before = rig.Health.LinesRead;
            second!.WriteLine(SampleLog.Row(i + 1));
            await RunnerRig.Eventually(() => rig.Health.LinesRead > before, "the row to be read");
        }

        second!.Exit(3);

        // Without the reset this would be the second failure in a row and the runner would give up (MaxRestarts = 1).
        await BackingOffFor(rig, Second);
        Assert.Equal(RunnerState.BackingOff, rig.Health.State);
        Assert.Equal(2, rig.Health.Restarts);

        await rig.Runner.StopAsync().WaitAsync(Bound);
        Assert.Equal(RunnerState.Stopped, rig.Health.State);
        rig.AssertNothingLeftBehind();
    }

    [Fact]
    public async Task A_failed_launch_is_retried_with_back_off_like_a_crash()
    {
        await using var rig = new RunnerRig(o => o with { TimedSeconds = 3 });
        rig.Launcher.LaunchFailures.Enqueue(new ProcessLaunchException("The executable could not be started (Windows error 5)."));
        rig.Launcher.LaunchFailures.Enqueue(new ProcessLaunchException("The executable could not be started (Windows error 5)."));
        rig.Launcher.EnqueueCapture(c =>
        {
            c.WriteHeaderAndRows(1);
            c.Exit(0);
        });

        rig.Runner.Start();
        await BackingOffFor(rig, Second);
        rig.Clock.Advance(Second);
        await BackingOffFor(rig, TimeSpan.FromSeconds(2));
        rig.Clock.Advance(TimeSpan.FromSeconds(2));
        await rig.WaitForCompletion();

        Assert.Equal(RunnerState.Completed, rig.Health.State);
        Assert.Equal(2, rig.Health.Restarts);
        Assert.Equal(2, rig.Health.Problems.Count(p => p.Contains("Windows error 5", StringComparison.Ordinal)));
        Assert.Single(rig.Launcher.Captures);
        rig.AssertNothingLeftBehind();
    }

    [Fact]
    public async Task A_clean_exit_that_nobody_asked_for_counts_as_a_failure_not_as_success()
    {
        await using var rig = new RunnerRig(o => o with { MaxRestarts = 0 }); // neither timed nor tied to a target
        rig.Launcher.EnqueueCapture(c =>
        {
            c.WriteHeaderAndRows(1);
            c.Exit(0);
        });

        rig.Runner.Start();
        await rig.WaitForCompletion();

        Assert.Equal(RunnerState.Failed, rig.Health.State);
        Assert.Contains("exited unexpectedly", rig.Health.LastError);
    }

    [Fact]
    public async Task A_log_the_parser_does_not_support_is_fatal_and_never_retried()
    {
        await using var rig = new RunnerRig();
        rig.Launcher.EnqueueCapture(c =>
        {
            c.WriteLine("Application,ProcessID,SomethingElse");
            c.WriteLine("a,1,2");
        });

        rig.Runner.Start();
        await rig.WaitForCompletion();

        Assert.Equal(RunnerState.Failed, rig.Health.State);
        Assert.Equal(0, rig.Health.Restarts);
        Assert.Contains("unsupported PresentMon log", rig.Health.LastError);
        Assert.Single(rig.Launcher.Captures);
        Assert.True(rig.Launcher.Captures[0].Killed);
        rig.AssertNothingLeftBehind();
    }

    // ---- back-pressure, problems, notes ----

    [Fact]
    public async Task A_full_queue_drops_the_oldest_frames_counts_them_and_never_blocks_the_pipe_reader()
    {
        await using var rig = new RunnerRig(o => o with { TimedSeconds = 5, FrameQueueCapacity = 4 });
        rig.Launcher.EnqueueCapture(c =>
        {
            c.WriteHeaderAndRows(100);
            c.Exit(0);
        });

        rig.Runner.Start();
        await rig.WaitForCompletion(); // nobody is reading Samples: a blocking policy would never get here

        var health = rig.Health;
        Assert.Equal(RunnerState.Completed, health.State);
        Assert.Equal(100, health.RowsParsed);
        Assert.Equal(96, health.DroppedFrames);

        var kept = await Drain(rig.Runner);
        Assert.Equal([96L, 97L, 98L, 99L], kept.Select(f => f.Sample.Index)); // the newest survive
    }

    [Fact]
    public async Task A_consumer_that_keeps_up_loses_nothing()
    {
        await using var rig = new RunnerRig(o => o with { TimedSeconds = 5, FrameQueueCapacity = 4 });
        var received = new System.Collections.Concurrent.ConcurrentQueue<long>();
        using var readBound = new CancellationTokenSource(Bound);
        var reader = Task.Run(async () =>
        {
            await foreach (var frame in rig.Runner.Samples.ReadAllAsync(readBound.Token))
            {
                received.Enqueue(frame.Sample.Index);
            }
        });

        rig.Launcher.EnqueueCapture(c =>
        {
            c.WriteLine(SampleLog.Header);
            c.WriteLine(SampleLog.Row(0));
        });
        rig.Runner.Start();
        for (var i = 1; i < 50; i++)
        {
            var row = i;
            await RunnerRig.Eventually(() => rig.Health.RowsParsed == row, "row " + row);
            await RunnerRig.Eventually(() => received.Count == row, "the reader to take row " + row);
            rig.Launcher.Captures[0].WriteLine(SampleLog.Row(i));
        }

        rig.Launcher.Captures[0].Exit(0);
        await rig.WaitForCompletion();
        await reader.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(0, rig.Health.DroppedFrames);
        Assert.Equal(Enumerable.Range(0, 50).Select(i => (long)i), received);
    }

    [Fact]
    public async Task The_warning_about_unknown_processes_is_a_note_and_never_an_error()
    {
        await using var rig = new RunnerRig(o => o with { TimedSeconds = 5 });
        rig.Launcher.EnqueueCapture(c =>
        {
            c.WriteError(SampleLog.ElevationWarning);
            c.WriteHeaderAndRows(2);
            c.Exit(0);
        });

        rig.Runner.Start();
        await rig.WaitForCompletion();

        var health = rig.Health;
        var note = Assert.Single(health.Notes);
        Assert.Contains("requires elevated privilege", note);
        Assert.Contains("<unknown>", note);
        Assert.Contains("--terminate_on_proc_exit", note); // the indented continuation lines were joined into one note
        Assert.Empty(health.Problems);
        Assert.Null(health.LastError);
        Assert.Equal(RunnerState.Completed, health.State);
    }

    [Fact]
    public async Task Malformed_rows_are_counted_and_the_first_few_are_kept_with_their_line_numbers()
    {
        await using var rig = new RunnerRig(o => o with { TimedSeconds = 5 });
        rig.Launcher.EnqueueCapture(c =>
        {
            c.WriteLine(SampleLog.Header);
            for (var i = 0; i < 30; i++)
            {
                c.WriteLine("garbage");
            }

            c.WriteLine(SampleLog.Row(0));
            c.Exit(0);
        });

        rig.Runner.Start();
        await rig.WaitForCompletion();

        var health = rig.Health;
        Assert.Equal(RunnerState.Completed, health.State);
        Assert.Equal(1, health.RowsParsed);
        Assert.Equal(30, health.ProblemCount);
        Assert.Equal(20, health.Problems.Count);
        Assert.Equal("line 2: not a data row", health.Problems[0]);
        Assert.Equal(32, health.LinesRead);
    }

    [Fact]
    public async Task A_line_that_was_too_long_is_a_problem_not_a_row()
    {
        await using var rig = new RunnerRig(o => o with { TimedSeconds = 5 });
        rig.Launcher.EnqueueCapture(c =>
        {
            c.WriteOverlongLine();
            c.WriteHeaderAndRows(1);
            c.Exit(0);
        });

        rig.Runner.Start();
        await rig.WaitForCompletion();

        Assert.Equal(1, rig.Health.RowsParsed);
        Assert.Contains(rig.Health.Problems, p => p.Contains("longer than", StringComparison.Ordinal));
        Assert.Equal(RunnerState.Completed, rig.Health.State);
    }

    [Fact]
    public async Task A_parser_that_throws_cannot_take_the_runner_down()
    {
        await using var rig = new RunnerRig(o => o with { TimedSeconds = 5 }, () => new ThrowingFrameParser());
        rig.Launcher.EnqueueCapture(c =>
        {
            c.WriteHeaderAndRows(3);
            c.Exit(0);
        });

        rig.Runner.Start();
        await rig.WaitForCompletion();

        Assert.Equal(RunnerState.Completed, rig.Health.State);
        Assert.Equal(0, rig.Health.RowsParsed);
        Assert.Equal(3, rig.Health.ProblemCount);
        Assert.Contains("InvalidOperationException", rig.Health.Problems[0]);
        Assert.DoesNotContain("boom", rig.Health.Problems[0]); // type names only, never message text
    }

    // ---- stop ----

    [Fact]
    public async Task Stop_asks_the_capture_to_leave_by_ending_its_own_session_and_kills_nothing_when_it_does()
    {
        await using var rig = new RunnerRig();
        rig.Launcher.EnqueueCapture(c => c.WriteHeaderAndRows(2));

        rig.Runner.Start();
        await RunnerRig.Eventually(() => rig.Health.RowsParsed == 2, "the first rows");

        await rig.Runner.StopAsync().WaitAsync(Bound);

        var capture = Assert.Single(rig.Launcher.Captures);
        Assert.False(capture.Killed);
        Assert.Equal(0, capture.ExitCode);
        var terminator = Assert.Single(rig.Launcher.Terminators);
        Assert.Equal(["--terminate_existing_session", "--session_name", "Jankscope-4242-1"], terminator.Request.Arguments);
        Assert.Equal(RunnerState.Stopped, rig.Health.State);
        Assert.True(rig.Runner.Completion.IsCompletedSuccessfully);
        Assert.Equal(2, (await Drain(rig.Runner)).Count); // the channel is closed: reading to the end terminates
        rig.AssertNothingLeftBehind();
    }

    [Fact]
    public async Task Stop_kills_the_process_tree_when_the_capture_ignores_the_graceful_request_and_the_grace_period_runs_out()
    {
        await using var rig = new RunnerRig();
        rig.Launcher.EnqueueCapture(c =>
        {
            c.IgnoresTerminate = true;
            c.WriteHeaderAndRows(1);
        });

        rig.Runner.Start();
        await RunnerRig.Eventually(() => rig.Health.RowsParsed == 1, "the first row");

        var stopping = rig.Runner.StopAsync();

        // The watchdog's timer (10 s) is armed for the whole run, so "a timer is pending" does not say the grace period has started. Wait for the timer due within it:
        // advancing the clock before the runner has armed it moves past a timer that does not exist yet, and the stop then never finishes.
        await RunnerRig.Eventually(() => rig.Health.State == RunnerState.Stopping && rig.Launcher.Terminators.Count == 1 && rig.Clock.NextDueIn <= TimeSpan.FromSeconds(5), "the grace period to be running");
        Assert.False(rig.Launcher.Captures[0].Killed);

        rig.Clock.Advance(TimeSpan.FromSeconds(4.9));
        await Task.Delay(30);
        Assert.False(rig.Launcher.Captures[0].Killed);

        rig.Clock.Advance(TimeSpan.FromSeconds(0.1));
        await stopping.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(rig.Launcher.Captures[0].Killed);
        Assert.Equal(RunnerState.Stopped, rig.Health.State);
        Assert.Equal(2, rig.Launcher.Terminators.Count); // after the kill the session is ended again, whatever the first request did
        Assert.Contains(rig.Health.Problems, p => p.Contains("did not leave within the grace period", StringComparison.Ordinal));
        rig.AssertNothingLeftBehind();
    }

    [Fact]
    public async Task Stop_when_the_helper_hangs_gives_up_after_the_cleanup_timeout_and_reports_the_session_that_may_remain()
    {
        // No watchdog: its 10 s timer would be pending for the whole run and satisfy "a timer is pending" before the helper's cleanup timer is armed,
        // and advancing the clock before then skips a timer that does not exist yet.
        await using var rig = new RunnerRig(o => o with { WatchdogNoOutputTimeout = Timeout.InfiniteTimeSpan });
        rig.Launcher.Terminator = TerminatorBehavior.Hang;
        rig.Launcher.EnqueueCapture(c => c.WriteHeaderAndRows(1));

        rig.Runner.Start();
        await RunnerRig.Eventually(() => rig.Health.RowsParsed == 1, "the first row");

        var stopping = rig.Runner.StopAsync();
        for (var i = 0; i < 2; i++) // the graceful request, then the clean-up after the kill, each waits the full timeout
        {
            await RunnerRig.Eventually(() => rig.Launcher.Terminators.Count == i + 1 && rig.Clock.PendingTimers >= 1, "helper " + (i + 1));
            rig.Clock.Advance(TimeSpan.FromSeconds(10));
        }

        await stopping.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(RunnerState.Stopped, rig.Health.State);
        Assert.Equal("Jankscope-4242-1", rig.Health.UncleanedSession);
        Assert.Contains(rig.Health.Problems, p => p.Contains("cleanup timeout", StringComparison.Ordinal));
        Assert.All(rig.Launcher.Children, c => Assert.True(c.HasExited && c.Disposed));
        Assert.True(rig.Launcher.Captures[0].Killed);
    }

    [Fact]
    public async Task Stop_while_waiting_to_restart_cancels_the_wait_and_starts_nothing_more()
    {
        await using var rig = new RunnerRig();
        rig.Launcher.DefaultCapture = c =>
        {
            c.WriteLine(SampleLog.Header);
            c.Exit(3);
        };

        rig.Runner.Start();
        await BackingOffFor(rig, Second);
        await rig.Runner.StopAsync().WaitAsync(Bound);

        Assert.Equal(RunnerState.Stopped, rig.Health.State);
        Assert.Single(rig.Launcher.Captures);
        rig.AssertNothingLeftBehind();
    }

    [Fact]
    public async Task Stop_before_start_is_harmless_and_a_stopped_runner_cannot_be_started()
    {
        await using var rig = new RunnerRig();
        await rig.Runner.StopAsync().WaitAsync(Bound);
        await rig.Runner.StopAsync().WaitAsync(Bound); // repeatable

        Assert.Equal(RunnerState.Stopped, rig.Health.State);
        Assert.True(rig.Runner.Completion.IsCompletedSuccessfully);
        Assert.False(await rig.Runner.Samples.WaitToReadAsync());
        Assert.Throws<InvalidOperationException>(rig.Runner.Start);
        Assert.Empty(rig.Launcher.Children);
    }

    [Fact]
    public async Task Start_twice_is_rejected()
    {
        await using var rig = new RunnerRig();
        rig.Launcher.DefaultCapture = c => c.WriteHeaderAndRows(1);
        rig.Runner.Start();
        Assert.Throws<InvalidOperationException>(rig.Runner.Start);
        await rig.Runner.StopAsync().WaitAsync(Bound);
        rig.AssertNothingLeftBehind();
    }

    [Fact]
    public async Task Disposing_a_running_runner_ends_the_child_and_its_session()
    {
        var rig = new RunnerRig();
        rig.Launcher.EnqueueCapture(c => c.WriteHeaderAndRows(1));
        rig.Runner.Start();
        await RunnerRig.Eventually(() => rig.Health.RowsParsed == 1, "the first row");

        await rig.DisposeAsync();

        rig.AssertNothingLeftBehind();
        Assert.Equal(RunnerState.Stopped, rig.Health.State);
        await rig.Runner.DisposeAsync(); // twice is fine
    }

    [Fact]
    public async Task Stop_requested_at_the_very_moment_of_start_never_leaves_a_child_behind()
    {
        for (var attempt = 0; attempt < 25; attempt++)
        {
            await using var rig = new RunnerRig();
            rig.Launcher.DefaultCapture = c => c.WriteHeaderAndRows(1);
            rig.Runner.Start();
            if (attempt % 2 == 0)
            {
                await Task.Yield();
            }

            await rig.Runner.StopAsync().WaitAsync(Bound);
            Assert.Equal(RunnerState.Stopped, rig.Health.State);
            rig.AssertNothingLeftBehind();
        }
    }

    // ---- construction ----

    [Theory]
    [InlineData("PresentMon")]
    [InlineData("Other-1")]
    [InlineData("jankscope-1")]
    [InlineData("Jankscope-")]
    [InlineData("Jankscope-1 --restart_as_admin")]
    public void A_foreign_or_malformed_session_name_is_rejected_when_the_runner_is_created(string name)
    {
        using var dir = new TempDir();
        var pin = new PinnedExecutable(dir.Write("pm.exe", [1, 2, 3]), 3, TempDir.Sha256Hex([1, 2, 3]));
        Assert.Throws<ArgumentException>(() => new PresentMonRunner(new PresentMonRunnerOptions { Executable = pin, SessionName = name }, new FakeProcessLauncher(), () => new TestFrameParser()));
    }

    [Fact]
    public void Bad_process_names_and_values_are_rejected_when_the_runner_is_created_not_at_the_first_launch()
    {
        using var dir = new TempDir();
        var pin = new PinnedExecutable(dir.Write("pm.exe", [1, 2, 3]), 3, TempDir.Sha256Hex([1, 2, 3]));

        PresentMonRunner Make(Func<PresentMonRunnerOptions, PresentMonRunnerOptions> change) =>
            new(change(new PresentMonRunnerOptions { Executable = pin, SessionName = "Jankscope-1" }), new FakeProcessLauncher(), () => new TestFrameParser());

        Assert.Throws<ArgumentException>(() => Make(o => o with { ProcessNames = ["..\\evil.exe"] }));
        Assert.Throws<ArgumentException>(() => Make(o => o with { ProcessNames = ["--restart_as_admin"] }));
        Assert.Throws<ArgumentException>(() => Make(o => o with { TerminateOnProcessExit = true }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Make(o => o with { TimedSeconds = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Make(o => o with { FrameQueueCapacity = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Make(o => o with { MaxRestarts = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Make(o => o with { WatchdogNoOutputTimeout = TimeSpan.Zero }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Make(o => o with { StopGracePeriod = TimeSpan.FromSeconds(-1) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Make(o => o with { RestartBackoffInitial = TimeSpan.Zero }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Make(o => o with { RestartBackoffMax = TimeSpan.FromMilliseconds(1) }));
        Assert.Equal(RunnerState.NotStarted, Make(o => o).Health.State);
    }

    // ---- back-off arithmetic ----

    [Fact]
    public void Back_off_doubles_from_the_first_delay_up_to_the_limit()
    {
        var initial = TimeSpan.FromMilliseconds(500);
        var max = TimeSpan.FromSeconds(8);
        Assert.Equal(
            [0.5, 1, 2, 4, 8, 8, 8],
            Enumerable.Range(1, 7).Select(k => RestartBackoff.DelayFor(k, initial, max).TotalSeconds));
    }

    [Fact]
    public void Back_off_cannot_overflow_however_large_the_inputs_are()
    {
        var day = TimeSpan.FromDays(1);
        var cap = TimeSpan.FromDays(300);
        Assert.Equal(cap, RestartBackoff.DelayFor(int.MaxValue, day, cap));
        Assert.Equal(cap, RestartBackoff.DelayFor(10_000, TimeSpan.FromDays(100), cap));
        Assert.Equal(TimeSpan.FromDays(100) * 1, RestartBackoff.DelayFor(1, TimeSpan.FromDays(100), TimeSpan.FromDays(300)));
    }

    [Fact]
    public void Back_off_rejects_nonsense()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RestartBackoff.DelayFor(0, Second, Second));
        Assert.Throws<ArgumentOutOfRangeException>(() => RestartBackoff.DelayFor(1, TimeSpan.Zero, Second));
        Assert.Throws<ArgumentOutOfRangeException>(() => RestartBackoff.DelayFor(1, Second, TimeSpan.FromMilliseconds(1)));
    }

    [Fact]
    public void Start_refuses_and_fails_when_suppressed_by_anti_cheat_safety()
    {
        var rig = new RunnerRig(o => o with
        {
            AntiCheatSuppressionCheck = () => new SuppressionDecision(true, AntiCheatSafety.SuppressedReason),
        });

        var ex = Assert.Throws<InvalidOperationException>(() => rig.Runner.Start());
        Assert.Contains(AntiCheatSafety.SuppressedReason, ex.Message);

        var health = rig.Health;
        Assert.Equal(RunnerState.Failed, health.State);
        Assert.Equal(AntiCheatSafety.SuppressedReason, health.LastError);
        Assert.True(rig.Runner.Completion.IsCompleted);
        Assert.Empty(rig.Launcher.Children);
    }

    [Fact]
    public async Task Start_proceeds_when_anti_cheat_safety_allows_capture()
    {
        await using var rig = new RunnerRig(o => o with
        {
            AntiCheatSuppressionCheck = () => new SuppressionDecision(false, null),
            TimedSeconds = 5,
        });

        rig.Launcher.EnqueueCapture(c =>
        {
            c.WriteHeaderAndRows(1);
            c.Exit(0);
        });

        rig.Runner.Start();
        await rig.WaitForCompletion();

        Assert.Equal(RunnerState.Completed, rig.Health.State);
    }
}
