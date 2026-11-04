using Jankscope.Frames.PresentMon;

namespace Jankscope.Frames.Tests.PresentMon;

/// <summary>The real launcher against the fake executable: what is actually passed, in what environment, and that nothing is left behind.</summary>
public class RunnerLauncherTests
{
    private const string Session = "Jankscope-4242-1";

    private static async Task<(List<string> Output, List<string> Errors, int ExitCode)> RunToEnd(IChildProcess child)
    {
        var exit = child.WaitForExitAsync(CancellationToken.None);
        var output = new List<string>();
        var errors = new List<string>();
        var readOut = Task.Run(async () =>
        {
            while (await child.ReadOutputLineAsync(CancellationToken.None) is { } line)
            {
                output.Add(line.Text);
            }
        });
        var readErr = Task.Run(async () =>
        {
            while (await child.ReadErrorLineAsync(CancellationToken.None) is { } line)
            {
                errors.Add(line.Text);
            }
        });
        var code = await exit.WaitAsync(TimeSpan.FromSeconds(30));
        await Task.WhenAll(readOut, readErr).WaitAsync(TimeSpan.FromSeconds(30));
        return (output, errors, code);
    }

    [Fact]
    public async Task The_child_runs_and_its_output_error_stream_and_exit_code_come_back()
    {
        using var fake = new FakePresentMonInstall("rows=3", "holdOpen=false");
        using var child = fake.Launch(PresentMonArguments.Build(Session));

        var (output, errors, code) = await RunToEnd(child);

        Assert.Equal(0, code);
        Assert.Equal(4, output.Count);
        Assert.StartsWith("Application,ProcessID", output[0], StringComparison.Ordinal);
        Assert.StartsWith("TestAppA.exe,1000", output[1], StringComparison.Ordinal);
        Assert.Contains(errors, e => e.StartsWith("warning: PresentMon requires elevated privilege", StringComparison.Ordinal));
        Assert.True(child.ProcessId > 0);
    }

    [Fact]
    public async Task Every_argument_arrives_as_exactly_the_element_that_was_built_even_for_hostile_looking_names()
    {
        using var fake = new FakePresentMonInstall("rows=1", "holdOpen=false");
        string[] names = ["My Game & Co; Edition (x64).exe", "%PATH% $(calc) 'q'.exe", "café ゲーム.exe", "evil --restart_as_admin --output_file x.exe", "has space before ext .exe"];
        var argv = PresentMonArguments.Build(Session, names, terminateOnProcessExit: true, timedSeconds: 7);

        using var child = fake.Launch(argv);
        await RunToEnd(child);

        var launch = Assert.Single(fake.Launches);
        Assert.Equal(argv, launch.Args); // same elements, same order, nothing split, merged, expanded or re-quoted
        Assert.Equal(1, launch.Ordinal);
    }

    [Fact]
    public async Task The_child_gets_a_minimal_environment_a_private_temporary_directory_and_an_empty_input()
    {
        using var fake = new FakePresentMonInstall("rows=1", "holdOpen=false");
        string? workDirectory;
        using (var child = fake.Launch(PresentMonArguments.Build(Session)))
        {
            await RunToEnd(child);
            workDirectory = Assert.Single(fake.Launches).Cwd;
            Assert.True(Directory.Exists(workDirectory));
        }

        var launch = Assert.Single(fake.Launches);
        Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), workDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(Path.GetDirectoryName(fake.ExePath), workDirectory, StringComparer.OrdinalIgnoreCase);
        Assert.StartsWith("Jankscope-pm-", Path.GetFileName(workDirectory), StringComparison.Ordinal);
        Assert.False(Directory.Exists(workDirectory), "the working directory is removed when the child is released");

        string[] allowed = ["SystemRoot", "windir", "SystemDrive", "PATH", "TEMP", "TMP"];
        Assert.All(launch.EnvNames, name => Assert.Contains(name, allowed, StringComparer.OrdinalIgnoreCase));
        Assert.Contains("SystemRoot", launch.EnvNames, StringComparer.OrdinalIgnoreCase);
        foreach (var leaked in new[] { "USERNAME", "USERPROFILE", "COMPUTERNAME", "USERDOMAIN", "APPDATA", "LOCALAPPDATA", "HOMEPATH", "OneDrive", "DOTNET_ROOT" })
        {
            Assert.DoesNotContain(leaked, launch.EnvNames, StringComparer.OrdinalIgnoreCase);
        }

        Assert.True(launch.StdinAtEnd, "the child's input is closed, not shared with ours");
    }

    [Theory]
    [InlineData("--restart_as_admin")]
    [InlineData("--output_file")]
    [InlineData("--etl_file")]
    [InlineData("--bogus")]
    public async Task A_vector_off_the_allowlist_is_refused_before_anything_starts(string option)
    {
        using var fake = new FakePresentMonInstall();
        var launcher = new PresentMonProcessLauncher();
        using var verified = fake.Verify();

        foreach (var argv in new IReadOnlyList<string>[]
        {
            [.. PresentMonArguments.Build(Session), option],
            [option, .. PresentMonArguments.Build(Session)],
            [option],
            ["--output_stdout --session_name " + Session],
        })
        {
            var e = Assert.Throws<ProcessLaunchException>(() => launcher.Start(new ProcessLaunchRequest(verified, argv)));
            Assert.Contains("allowlist", e.Message);
        }

        await Task.Delay(100);
        Assert.True(fake.NeverLaunched);
    }

    [Fact]
    public void The_launcher_cannot_be_given_a_bare_path_only_a_verified_executable_and_it_is_not_public()
    {
        // Review 1a, finding 2: "pinned and hash-verified" used to be a convention held by the caller. Now the type system holds it.
        Assert.All(typeof(ProcessLaunchRequest).GetConstructors(), c => Assert.Equal(typeof(VerifiedExecutable), c.GetParameters()[0].ParameterType));
        Assert.Empty(typeof(VerifiedExecutable).GetConstructors()); // only FileHashVerifier.Open can make one (the constructor is internal)
        Assert.False(typeof(PresentMonProcessLauncher).IsPublic);
        Assert.DoesNotContain(typeof(ProcessLaunchRequest).GetProperties(), p => p.PropertyType == typeof(string) && p.CanWrite);
    }

    [Theory]
    [InlineData("PresentMon.exe")]
    [InlineData("..\\PresentMon.exe")]
    [InlineData("\\\\server\\share\\PresentMon.exe")]
    [InlineData("C:PresentMon.exe")]
    [InlineData("cmd.exe")]
    public void A_path_that_is_not_an_absolute_local_path_is_refused_even_on_a_verified_executable_so_nothing_is_ever_found_through_PATH(string path)
    {
        // Defence in depth: FileHashVerifier would never produce such a path, so the test builds the object through the internal constructor.
        using var dir = new TempDir();
        using var stream = new FileStream(dir.Write("x.bin", [1]), FileMode.Open, FileAccess.Read, FileShare.Read);
        using var forged = new VerifiedExecutable(stream, path, 1, new string('0', 64));

        var e = Assert.Throws<ProcessLaunchException>(() => new PresentMonProcessLauncher().Start(new ProcessLaunchRequest(forged, PresentMonArguments.Build(Session))));
        Assert.Contains("absolute", e.Message);
    }

    [Fact]
    public void A_missing_file_is_a_launch_failure_without_a_path_in_the_message_and_leaves_no_directory_behind()
    {
        using var dir = new TempDir();
        var missing = dir.File("not-here.exe");
        var before = Directory.GetDirectories(Path.GetTempPath(), "Jankscope-pm-*").Length;
        using var stream = new FileStream(dir.Write("x.bin", [1]), FileMode.Open, FileAccess.Read, FileShare.Read);
        using var forged = new VerifiedExecutable(stream, missing, 1, new string('0', 64)); // the file vanished after verification (not possible while the handle is held; this checks the launcher's own failure path)

        var e = Assert.Throws<ProcessLaunchException>(() => new PresentMonProcessLauncher().Start(new ProcessLaunchRequest(forged, PresentMonArguments.Build(Session))));

        Assert.DoesNotContain(dir.Path, e.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Windows error", e.Message);
        Assert.True(Directory.GetDirectories(Path.GetTempPath(), "Jankscope-pm-*").Length <= before);
    }

    [Fact]
    public async Task Killing_the_child_ends_a_hanging_process_and_waiting_returns()
    {
        using var fake = new FakePresentMonInstall("mode=hang", "headerFirst=true");
        using var child = fake.Launch(PresentMonArguments.Build(Session));
        var first = await child.ReadOutputLineAsync(CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.StartsWith("Application,", first!.Value.Text, StringComparison.Ordinal);
        Assert.True(RunnerProcesses.IsAlive(child.ProcessId));

        Assert.True(child.TryKill(out var failure));
        Assert.Null(failure);
        await child.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));

        await fake.AssertAllLaunchedProcessesGone();
        Assert.True(child.TryKill(out _)); // repeating it after the exit is harmless
        Assert.Equal(["jankscope-4242-1.session"], fake.LiveSessions); // a killed capture leaves its session behind, as the real tool does
    }

    [Fact]
    public async Task Releasing_a_child_that_is_still_running_ends_it_so_nothing_outlives_its_owner()
    {
        using var fake = new FakePresentMonInstall("mode=hang", "headerFirst=true");
        var child = fake.Launch(PresentMonArguments.Build(Session));
        await child.ReadOutputLineAsync(CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(30));
        var pid = child.ProcessId;
        Assert.True(RunnerProcesses.IsAlive(pid));

        child.Dispose();
        child.Dispose(); // twice is fine

        await fake.AssertAllLaunchedProcessesGone();
        Assert.False(RunnerProcesses.IsAlive(pid));
        Assert.False(Directory.Exists(Assert.Single(fake.Launches).Cwd));
    }
}
