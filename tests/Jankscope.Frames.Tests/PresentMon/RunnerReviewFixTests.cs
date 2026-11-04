using Jankscope.Frames.PresentMon;

namespace Jankscope.Frames.Tests.PresentMon;

/// <summary>Creates directory junctions for tests (no privilege needed, unlike symbolic links). Test code is not product code: Process is fine here.</summary>
internal static class TestJunction
{
    public static void Create(string link, string target)
    {
        var info = new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[] { "/c", "mklink", "/J", link, target })
        {
            info.ArgumentList.Add(argument);
        }

        using var process = System.Diagnostics.Process.Start(info)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(30_000), "mklink did not finish");
        Assert.True(Directory.Exists(link) && new DirectoryInfo(link).Attributes.HasFlag(FileAttributes.ReparsePoint), "could not create a junction: " + output);
    }

    /// <summary>Removes only the junction, never what it points at.</summary>
    public static void Remove(string link)
    {
        if (Directory.Exists(link))
        {
            Directory.Delete(link, recursive: false);
        }
    }
}

/// <summary>
/// Regression tests for the fixes that came out of the independent Stage 1a review (docs/reviews/stage-1a.md): the junction race (3), the
/// verified file staying protected until the launch (8, mutation R8), the reparse refusal (8, mutation R4), a child that must not leak when
/// the run cannot be set up (11) and a cut-off last line (12).
/// </summary>
public class RunnerReviewFixTests
{
    // ---- finding 3: a junction above the file ----

    [Fact]
    public void A_directory_junction_directly_above_the_file_is_refused_and_the_same_file_by_its_real_path_is_accepted()
    {
        using var fake = new FakePresentMonInstall();
        using var dir = new TempDir();
        var link = dir.File("J");
        TestJunction.Create(link, fake.Dir.Path);
        try
        {
            var viaLink = new PinnedExecutable(Path.Combine(link, Path.GetFileName(fake.ExePath)), fake.Pin.SizeBytes, fake.Pin.Sha256);
            using var refused = FileHashVerifier.Open(viaLink);
            Assert.Equal(ExecutableCheck.PathRedirected, refused.Outcome);
            Assert.True(refused.IsPermanent, "a redirected path is a property of the install, not a passing condition");
            Assert.Null(refused.Executable);
            Assert.DoesNotContain(dir.Path, refused.Reason, StringComparison.OrdinalIgnoreCase);

            using var control = FileHashVerifier.Open(fake.Pin); // the same bytes by their real path verify: the refusal is about the path, not the file
            Assert.True(control.IsVerified, control.Reason);
        }
        finally
        {
            TestJunction.Remove(link);
        }
    }

    [Fact]
    public void A_junction_several_levels_above_the_file_is_refused_too()
    {
        using var fake = new FakePresentMonInstall();
        using var real = new TempDir();
        Directory.CreateDirectory(real.File("a"));
        Directory.CreateDirectory(real.File(Path.Combine("a", "b")));
        var copy = real.File(Path.Combine("a", "b", "pm.exe"));
        File.Copy(fake.ExePath, copy);
        using var dir = new TempDir();
        var link = dir.File("J");
        TestJunction.Create(link, real.Path);
        try
        {
            using var control = FileHashVerifier.Open(new PinnedExecutable(copy, fake.Pin.SizeBytes, fake.Pin.Sha256));
            Assert.True(control.IsVerified, control.Reason);

            var viaLink = new PinnedExecutable(Path.Combine(link, "a", "b", "pm.exe"), fake.Pin.SizeBytes, fake.Pin.Sha256);
            using var refused = FileHashVerifier.Open(viaLink);
            Assert.Equal(ExecutableCheck.PathRedirected, refused.Outcome);
        }
        finally
        {
            TestJunction.Remove(link);
        }
    }

    [Fact]
    public void A_link_that_appears_between_the_first_check_and_the_open_is_caught_by_the_second_check()
    {
        using var fake = new FakePresentMonInstall();
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Plants a reparse point on the file's own directory from the second look onwards: what a swap between the two checks would show.
        FileAttributes Attributes(string path)
        {
            var real = File.GetAttributes(path);
            if (!real.HasFlag(FileAttributes.Directory))
            {
                return real;
            }

            seen[path] = seen.GetValueOrDefault(path) + 1;
            return seen[path] >= 2 ? real | FileAttributes.ReparsePoint : real;
        }

        using var check = FileHashVerifier.Open(fake.Pin, Attributes);

        Assert.Equal(ExecutableCheck.PathRedirected, check.Outcome);
        Assert.Null(check.Executable);
        Assert.Equal(2, seen[Path.GetDirectoryName(fake.ExePath)!]); // the file's own directory was looked at twice: before the open, and again with the handle held
        Assert.All(seen.Values, count => Assert.InRange(count, 1, 2)); // every directory above it was looked at in the first pass
    }

    // ---- finding 8, mutation R4: the reparse refusal must be pinned even where this account cannot create a symbolic link ----

    [Fact]
    public void A_final_component_that_reports_a_reparse_point_is_refused_as_not_a_regular_file()
    {
        using var fake = new FakePresentMonInstall();

        using var check = FileHashVerifier.Open(fake.Pin, p => string.Equals(p, fake.ExePath, StringComparison.OrdinalIgnoreCase)
            ? FileAttributes.Archive | FileAttributes.ReparsePoint
            : File.GetAttributes(p));

        Assert.Equal(ExecutableCheck.NotARegularFile, check.Outcome);
        Assert.Null(check.Executable);
    }

    [Fact]
    public void A_directory_above_that_reports_a_reparse_point_is_refused_even_without_the_privilege_to_make_a_real_link()
    {
        using var fake = new FakePresentMonInstall();
        var directory = Path.GetDirectoryName(fake.ExePath)!;

        using var check = FileHashVerifier.Open(fake.Pin, p => string.Equals(p, directory, StringComparison.OrdinalIgnoreCase)
            ? FileAttributes.Directory | FileAttributes.ReparsePoint
            : File.GetAttributes(p));

        Assert.Equal(ExecutableCheck.PathRedirected, check.Outcome);
    }

    [Fact]
    public void A_directory_that_cannot_be_inspected_fails_closed_as_a_passing_condition()
    {
        using var fake = new FakePresentMonInstall();
        var directory = Path.GetDirectoryName(fake.ExePath)!;

        using var check = FileHashVerifier.Open(fake.Pin, p => string.Equals(p, directory, StringComparison.OrdinalIgnoreCase)
            ? throw new UnauthorizedAccessException()
            : File.GetAttributes(p));

        Assert.Equal(ExecutableCheck.CannotOpen, check.Outcome);
        Assert.False(check.IsPermanent);
        Assert.Null(check.Executable);
    }

    // ---- finding 8, mutation R8: the file stays protected until the launcher has returned ----

    [Fact]
    public async Task The_verified_file_cannot_be_opened_for_writing_or_renamed_while_the_launcher_is_creating_the_process()
    {
        await using var rig = new RunnerRig(o => o with { TimedSeconds = 5 });
        var outcomes = new List<string>();
        rig.Launcher.OnStart = request =>
        {
            outcomes.Add(TryOpenForWrite(request.ExecutablePath));
            outcomes.Add(TryRename(request.ExecutablePath));
        };
        rig.Launcher.EnqueueCapture(c =>
        {
            c.WriteHeaderAndRows(1);
            c.Exit(0);
        });

        rig.Runner.Start();
        await rig.WaitForCompletion();

        Assert.Equal(["blocked", "blocked"], outcomes);
        Assert.Equal(RunnerState.Completed, rig.Health.State);
    }

    private static string TryOpenForWrite(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            return "WRITABLE";
        }
        catch (IOException)
        {
            return "blocked";
        }
        catch (UnauthorizedAccessException)
        {
            return "blocked";
        }
    }

    private static string TryRename(string path)
    {
        var moved = path + ".moved";
        try
        {
            File.Move(path, moved);
            File.Move(moved, path);
            return "RENAMED";
        }
        catch (IOException)
        {
            return "blocked";
        }
        catch (UnauthorizedAccessException)
        {
            return "blocked";
        }
    }

    // ---- finding 11: nothing leaks when the run cannot be set up ----

    [Fact]
    public async Task A_parser_factory_that_throws_after_the_launch_still_ends_releases_and_cleans_up_the_child()
    {
        await using var rig = new RunnerRig(parser: () => throw new InvalidOperationException("the factory failed"));
        rig.Launcher.EnqueueCapture(_ => { }); // a capture that would run forever

        rig.Runner.Start();
        await rig.WaitForCompletion();

        var capture = Assert.Single(rig.Launcher.Captures);
        Assert.True(capture.Killed, "the child was ended");
        Assert.True(capture.HasExited);
        Assert.True(capture.Disposed, "the child was released");
        Assert.Equal(RunnerState.Failed, rig.Health.State);
        Assert.Contains("internal error", rig.Health.LastError);
        rig.AssertNothingLeftBehind(); // including the capture's ETW session, which a killed capture leaves behind
    }

    // ---- finding 12: a last line that the pipe cut off is never parsed ----

    [Fact]
    public async Task A_cut_off_last_line_is_dropped_with_a_problem_and_never_parsed_into_a_frame()
    {
        await using var rig = new RunnerRig(o => o with { TimedSeconds = 5 });
        rig.Launcher.EnqueueCapture(c =>
        {
            c.WriteHeaderAndRows(3);
            c.WriteUnterminatedLine(SampleLog.Row(3)); // a complete-looking row, but the line break never arrived
            c.Exit(0);
        });

        rig.Runner.Start();
        await rig.WaitForCompletion();

        var health = rig.Health;
        Assert.Equal(3, health.RowsParsed);
        Assert.Equal(5, health.LinesRead);
        Assert.Contains(health.Problems, p => p.Contains("before the line break", StringComparison.Ordinal));
        var frames = new List<CapturedFrame>();
        await foreach (var frame in rig.Runner.Samples.ReadAllAsync())
        {
            frames.Add(frame);
        }

        Assert.Equal([0L, 1L, 2L], frames.Select(f => f.Sample.Index));
    }
}
