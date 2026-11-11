using System.Globalization;
using Jankscope.Core;
using Jankscope.Frames.PresentMon;

namespace Jankscope.Frames.Tests.PresentMon;

/// <summary>
/// The argument vector is the one place where untrusted text (a process name typed by a user or read from a game list) meets a
/// third-party executable. It must stay an ordered list of separate elements, never a joined string, and it must never ask
/// PresentMon to elevate itself (HC-3, HC-7).
/// </summary>
public class RunnerArgumentsTests
{
    private const string Session = "Jankscope-4242-1";

    [Fact]
    public void Minimal_capture_has_the_documented_fixed_prefix_and_order()
    {
        var argv = PresentMonArguments.Build(Session);
        Assert.Equal(["--output_stdout", "--no_console_stats", "--session_name", Session], argv);
    }

    [Fact]
    public void Full_capture_keeps_options_in_the_documented_order()
    {
        var argv = PresentMonArguments.Build(Session, ["game.exe", "other.exe"], terminateOnProcessExit: true, timedSeconds: 30);
        Assert.Equal(
        [
            "--output_stdout", "--no_console_stats", "--session_name", Session,
            "--process_name", "game.exe", "--process_name", "other.exe",
            "--terminate_on_proc_exit",
            "--timed", "30", "--terminate_after_timed",
        ], argv);
    }

    [Theory]
    [InlineData("My Game & Co; Edition.exe")]
    [InlineData("game(1).exe")]
    [InlineData("a&b;c^d.exe")]
    [InlineData("%PATH%.exe")]
    [InlineData("$(calc).exe")]
    [InlineData("it's.exe")]
    [InlineData("caf\u00e9 \u30b2\u30fc\u30e0.exe")]
    [InlineData("evil --restart_as_admin.exe")]
    [InlineData("x --process_name y.exe")]
    public void Process_names_with_shell_metacharacters_or_option_look_alikes_stay_exactly_one_element(string name)
    {
        var argv = PresentMonArguments.Build(Session, [name]);

        var index = argv.ToList().IndexOf("--process_name");
        Assert.True(index >= 0);
        Assert.Equal(name, argv[index + 1]);
        Assert.Equal(6, argv.Count);
        Assert.Single(argv, a => a == name);
        PresentMonArguments.EnsureSafe(argv);
    }

    [Theory]
    [InlineData("..\\game.exe")]
    [InlineData("dir\\game.exe")]
    [InlineData("dir/game.exe")]
    [InlineData("../game.exe")]
    [InlineData("/game.exe")]
    [InlineData("\\\\server\\share\\game.exe")]
    [InlineData("C:\\game.exe")]
    [InlineData("C:game.exe")]
    [InlineData("game.exe:stream")]
    [InlineData("\"game.exe\"")]
    [InlineData("ga\"me.exe")]
    [InlineData("game\n.exe")]
    [InlineData("game\r.exe")]
    [InlineData("game\t.exe")]
    [InlineData("game\0.exe")]
    [InlineData("game\u0085.exe")]
    [InlineData("-game.exe")]
    [InlineData("--restart_as_admin")]
    [InlineData("--restart_as_admin.exe")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" game.exe")]
    [InlineData("game.exe ")]
    [InlineData(".exe")]
    [InlineData("game")]
    [InlineData("game.exe.txt")]
    [InlineData("ga*me.exe")]
    [InlineData("ga?me.exe")]
    [InlineData("ga<me>.exe")]
    [InlineData("ga|me.exe")]
    [InlineData("game\u202e.exe")] // right-to-left override: shows a name that is not the name
    public void Process_names_that_are_not_plain_exe_names_are_rejected(string name)
    {
        Assert.Throws<ArgumentException>(() => PresentMonArguments.Build(Session, [name]));
    }

    [Fact]
    public void A_lone_surrogate_and_an_overlong_name_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => PresentMonArguments.Build(Session, ["game\ud800.exe"]));
        Assert.Throws<ArgumentException>(() => PresentMonArguments.Build(Session, [new string('a', 252) + ".exe"]));
        PresentMonArguments.Build(Session, [new string('a', 251) + ".exe"]);
    }

    [Fact]
    public void Null_names_and_a_null_list_entry_are_rejected_not_ignored()
    {
        Assert.Throws<ArgumentException>(() => PresentMonArguments.Build(Session, [null!]));
    }

    [Fact]
    public void Process_names_are_deduplicated_ignoring_case_and_the_count_is_bounded()
    {
        var argv = PresentMonArguments.Build(Session, ["Game.exe", "game.EXE", "other.exe"]);
        Assert.Equal(2, argv.Count(a => a == "--process_name"));

        var tooMany = Enumerable.Range(0, PresentMonArguments.MaxProcessNames + 1).Select(i => $"g{i}.exe").ToList();
        Assert.Throws<ArgumentException>(() => PresentMonArguments.Build(Session, tooMany));
    }

    [Theory]
    [InlineData("PresentMon")]
    [InlineData("Other-1")]
    [InlineData("jankscope-1")] // the prefix is matched exactly: a different case is not "ours"
    [InlineData("JANKSCOPE-1")]
    [InlineData("Jankscope")]
    [InlineData("Jankscope-")]
    [InlineData("")]
    [InlineData(" Jankscope-1")]
    [InlineData("Jankscope-1 ")]
    [InlineData("Jankscope-1 --restart_as_admin")]
    [InlineData("Jankscope-1\"")]
    [InlineData("Jankscope-1\\x")]
    [InlineData("Jankscope-1\n")]
    [InlineData("Jankscope-\u00e91")]
    public void Session_names_must_carry_our_prefix_and_a_plain_suffix(string name)
    {
        Assert.Throws<ArgumentException>(() => PresentMonArguments.Build(name));
        Assert.Throws<ArgumentException>(() => PresentMonArguments.BuildTerminateExistingSession(name));
    }

    [Fact]
    public void Session_name_prefix_comes_from_branding_and_the_default_name_is_valid()
    {
        Assert.StartsWith(Branding.EtwSessionPrefix, PresentMonArguments.NewSessionName(1234), StringComparison.Ordinal);
        Assert.Equal("Jankscope-1234", PresentMonArguments.NewSessionName(1234));
        Assert.Equal("Jankscope-1234-capture", PresentMonArguments.NewSessionName(1234, "capture"));
        Assert.Throws<ArgumentException>(() => PresentMonArguments.NewSessionName(1234, "bad label"));
        Assert.Throws<ArgumentOutOfRangeException>(() => PresentMonArguments.NewSessionName(-1));
        PresentMonArguments.Build(PresentMonArguments.NewSessionName(Environment.ProcessId));
    }

    [Fact]
    public void Restart_as_admin_is_never_emitted_for_any_combination_of_options()
    {
        foreach (var names in new IReadOnlyList<string>?[] { null, [], ["game.exe"], ["a.exe", "b b.exe"] })
        {
            foreach (var terminate in new[] { false, true })
            {
                foreach (var timed in new int?[] { null, 1, 3600 })
                {
                    foreach (var stop in new[] { false, true })
                    {
                        if (terminate && (names is null || names.Count == 0))
                        {
                            continue;
                        }

                        var argv = PresentMonArguments.Build(Session, names, terminate, timed, stop);
                        Assert.DoesNotContain(argv, a => a.Contains("restart_as_admin", StringComparison.OrdinalIgnoreCase) && !a.Contains(' '));
                        PresentMonArguments.EnsureSafe(argv);
                    }
                }
            }
        }

        Assert.DoesNotContain(PresentMonArguments.BuildTerminateExistingSession(Session), a => a.Contains("restart_as_admin", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Existing_session_options_are_emitted_only_when_asked_for()
    {
        Assert.DoesNotContain("--stop_existing_session", PresentMonArguments.Build(Session));
        Assert.DoesNotContain("--terminate_existing_session", PresentMonArguments.Build(Session, ["g.exe"], true, 5, false));

        var stopping = PresentMonArguments.Build(Session, stopExistingSession: true);
        Assert.Contains("--stop_existing_session", stopping);
        Assert.DoesNotContain("--terminate_existing_session", stopping);

        Assert.Equal(["--terminate_existing_session", "--session_name", Session], PresentMonArguments.BuildTerminateExistingSession(Session));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(86401)]
    public void Timed_capture_needs_a_sane_number_of_seconds(int seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PresentMonArguments.Build(Session, timedSeconds: seconds));
    }

    [Fact]
    public void Terminate_on_process_exit_needs_at_least_one_named_process()
    {
        Assert.Throws<ArgumentException>(() => PresentMonArguments.Build(Session, terminateOnProcessExit: true));
        Assert.Throws<ArgumentException>(() => PresentMonArguments.Build(Session, [], terminateOnProcessExit: true));
        Assert.Contains("--terminate_on_proc_exit", PresentMonArguments.Build(Session, ["g.exe"], terminateOnProcessExit: true));
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("ar-SA")]
    [InlineData("fa-IR")]
    [InlineData("de-DE")]
    public void Numbers_are_formatted_with_the_invariant_culture(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var argv = PresentMonArguments.Build(Session, timedSeconds: 1234);
            Assert.Contains("1234", argv);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Build_returns_a_list_of_separate_elements_never_a_joined_command_line()
    {
        var argv = PresentMonArguments.Build(Session, ["a b.exe"]);
        Assert.IsAssignableFrom<IReadOnlyList<string>>(argv);
        Assert.DoesNotContain(argv, a => a.StartsWith("--output_stdout ", StringComparison.Ordinal));
        Assert.Contains("a b.exe", argv);
    }

    // ---- EnsureSafe: the grammar the launcher enforces on whatever it is handed, independent of Build ----

    [Theory]
    [InlineData("--restart_as_admin")]
    [InlineData("--RESTART_AS_ADMIN")]
    [InlineData("--restart_as_admin=true")]
    [InlineData("--output_file")]
    [InlineData("--etl_file")]
    [InlineData("--hotkey")]
    [InlineData("--v1_metrics")]
    [InlineData("--bogus")]
    [InlineData("-x")]
    [InlineData("/restart_as_admin")]
    public void Unknown_or_dangerous_options_are_rejected_wherever_they_appear(string option)
    {
        var good = PresentMonArguments.Build(Session);
        Assert.Throws<ArgumentException>(() => PresentMonArguments.EnsureSafe([.. good, option]));
        Assert.Throws<ArgumentException>(() => PresentMonArguments.EnsureSafe([option, .. good]));
        Assert.Throws<ArgumentException>(() => PresentMonArguments.EnsureSafe([option]));
    }

    [Fact]
    public void A_value_slot_cannot_smuggle_an_option_and_a_missing_value_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => PresentMonArguments.EnsureSafe(["--output_stdout", "--session_name", "--restart_as_admin"]));
        Assert.Throws<ArgumentException>(() => PresentMonArguments.EnsureSafe(["--session_name", Session, "--process_name", "--restart_as_admin"]));
        Assert.Throws<ArgumentException>(() => PresentMonArguments.EnsureSafe(["--session_name", Session, "--process_name"]));
        Assert.Throws<ArgumentException>(() => PresentMonArguments.EnsureSafe(["--session_name"]));
        Assert.Throws<ArgumentException>(() => PresentMonArguments.EnsureSafe(["--session_name", Session, "--timed", "abc"]));
        Assert.Throws<ArgumentException>(() => PresentMonArguments.EnsureSafe(["--session_name", Session, "--timed", "0"]));
        Assert.Throws<ArgumentException>(() => PresentMonArguments.EnsureSafe(["--session_name", Session, "stray.exe"]));
    }

    [Fact]
    public void A_single_joined_string_is_not_a_valid_argument_vector()
    {
        Assert.Throws<ArgumentException>(() => PresentMonArguments.EnsureSafe(["--output_stdout --no_console_stats --session_name " + Session]));
    }

    [Fact]
    public void A_foreign_or_missing_session_name_is_rejected_by_the_guard_too()
    {
        Assert.Throws<ArgumentException>(() => PresentMonArguments.EnsureSafe(["--output_stdout"]));
        Assert.Throws<ArgumentException>(() => PresentMonArguments.EnsureSafe(["--session_name", "PresentMon"]));
        Assert.Throws<ArgumentException>(() => PresentMonArguments.EnsureSafe(["--terminate_existing_session", "--session_name", "SomeoneElsesSession"]));
        Assert.Throws<ArgumentException>(() => PresentMonArguments.EnsureSafe(["--terminate_existing_session"]));
        Assert.Throws<ArgumentException>(() => PresentMonArguments.EnsureSafe(["--stop_existing_session", "--session_name", "Other"]));
    }

    [Fact]
    public void An_empty_vector_a_null_vector_and_a_duplicate_session_name_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => PresentMonArguments.EnsureSafe([]));
        Assert.Throws<ArgumentNullException>(() => PresentMonArguments.EnsureSafe(null!));
        Assert.Throws<ArgumentException>(() => PresentMonArguments.EnsureSafe(["--session_name", Session, "--session_name", Session]));
    }

    [Fact]
    public void The_terminate_form_accepts_only_its_own_two_options()
    {
        PresentMonArguments.EnsureSafe(PresentMonArguments.BuildTerminateExistingSession(Session));
        Assert.Throws<ArgumentException>(() => PresentMonArguments.EnsureSafe(["--terminate_existing_session", "--session_name", Session, "--output_stdout"]));
        Assert.Throws<ArgumentException>(() => PresentMonArguments.EnsureSafe(["--terminate_existing_session", "--session_name", Session, "--process_name", "g.exe"]));
    }

    [Fact]
    public void Everything_Build_can_produce_is_accepted_by_the_guard()
    {
        PresentMonArguments.EnsureSafe(PresentMonArguments.Build(Session, ["a.exe", "b c.exe"], true, 10, true));
        PresentMonArguments.EnsureSafe(PresentMonArguments.Build(Session));
    }
}
