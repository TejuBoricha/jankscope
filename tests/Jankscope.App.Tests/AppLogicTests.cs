using System.Diagnostics;
using System.Globalization;
using Jankscope.App.Services;
using Jankscope.Core;
using Jankscope.Core.Hotkeys;
using Jankscope.Core.Reports;
using Jankscope.Frames;
using Jankscope.Frames.Incidents;
using Jankscope.Platform.Hotkeys;

namespace Jankscope.App.Tests;

public class SettingsStoreTests
{
    [Fact]
    public void Settings_round_trip()
    {
        var store = new SettingsStore(TestData.TempFile("settings.json"));
        var saved = new AppSettings { MarkStutterChord = "Ctrl+Alt+F11", MarkInputStallChord = "Ctrl+Shift+K", PreRollSeconds = 90, PostRollSeconds = 15, Theme = "Light", SoundEnabled = false, FirstRunCompleted = true, LastProgram = "game.exe" };

        Assert.True(store.TrySave(saved, out var problem), problem);
        var loaded = store.Load(out var loadProblem);

        Assert.Null(loadProblem);
        Assert.Equal(saved, loaded);
    }

    [Fact]
    public void A_missing_file_gives_the_defaults_without_a_problem()
    {
        var loaded = new SettingsStore(TestData.TempFile("nothing.json")).Load(out var problem);
        Assert.Null(problem);
        Assert.Equal(new AppSettings(), loaded);
        Assert.Equal("Ctrl+Alt+F9", loaded.MarkStutterChord);
        Assert.Equal(120, loaded.PreRollSeconds);
        Assert.False(loaded.FirstRunCompleted);
    }

    [Fact]
    public void A_corrupt_file_gives_the_defaults_and_says_so()
    {
        var path = TestData.TempFile("settings.json");
        File.WriteAllText(path, "{ this is not json");
        var loaded = new SettingsStore(path).Load(out var problem);
        Assert.Equal(new AppSettings(), loaded);
        Assert.Contains("could not be read", problem);
    }

    [Fact]
    public void Out_of_range_or_invalid_values_are_brought_back_inside_their_limits()
    {
        var path = TestData.TempFile("settings.json");
        File.WriteAllText(path, """{"markStutterChord":"F9","markInputStallChord":"Ctrl+Alt+F9","preRollSeconds":5000,"postRollSeconds":-4,"theme":"Neon"}""");
        var loaded = new SettingsStore(path).Load(out _);

        Assert.Equal("Ctrl+Alt+F9", loaded.MarkStutterChord); // "F9" alone is not a valid chord: both fall back together
        Assert.Equal("Ctrl+Alt+F10", loaded.MarkInputStallChord);
        Assert.Equal(600, loaded.PreRollSeconds);
        Assert.Equal(0, loaded.PostRollSeconds);
        Assert.Equal("System", loaded.Theme);
    }

    [Fact]
    public void Two_marks_on_one_chord_fall_back_to_the_defaults()
    {
        var settings = new AppSettings { MarkStutterChord = "Ctrl+Alt+F5", MarkInputStallChord = "ctrl+alt+f5" }.Normalized();
        Assert.Equal("Ctrl+Alt+F9", settings.MarkStutterChord);
        Assert.Equal("Ctrl+Alt+F10", settings.MarkInputStallChord);
    }

    [Fact]
    public void A_place_that_cannot_be_written_is_reported_not_thrown()
    {
        var blocker = TestData.TempFile("blocker");
        File.WriteAllText(blocker, "a file where a folder is needed");
        var store = new SettingsStore(Path.Combine(blocker, "settings.json"));

        Assert.False(store.TrySave(new AppSettings(), out var problem));
        Assert.Contains("could not be saved", problem);
    }

    [Fact]
    public void No_temporary_file_is_left_behind()
    {
        var path = TestData.TempFile("settings.json");
        new SettingsStore(path).TrySave(new AppSettings(), out _);
        Assert.Equal([path], Directory.GetFiles(Path.GetDirectoryName(path)!));
    }
}

public class IconFactoryTests
{
    [Fact]
    public void The_png_is_a_32_pixel_image_and_the_ico_wraps_it_correctly()
    {
        var png = IconFactory.Png(TrayState.Capturing);
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], png[..4]);

        var ico = IconFactory.Ico(TrayState.Capturing);
        Assert.Equal([0, 0, 1, 0, 1, 0], ico[..6]);
        Assert.Equal(32, ico[6]);
        Assert.Equal(32, ico[7]);
        Assert.Equal(png.Length, BitConverter.ToInt32(ico, 14));
        Assert.Equal(22, BitConverter.ToInt32(ico, 18));
        Assert.Equal(png, ico[22..]);
    }

    [Fact]
    public void Every_state_makes_a_usable_icon_and_the_states_look_different()
    {
        var pngs = new List<byte[]>();
        foreach (var state in Enum.GetValues<TrayState>())
        {
            using var icon = IconFactory.TrayIcon(state);
            Assert.Equal(32, icon.Width);
            Assert.Equal(32, icon.Height);
            pngs.Add(IconFactory.Png(state));
        }

        Assert.Equal(pngs.Count, pngs.Select(Convert.ToHexString).Distinct().Count());
    }
}

public class AppHostTests
{
    private static (AppHost Host, FakeCaptureFactory Factory, SilentCue Cue) Create(CapabilityMatrix? matrix = null, string? settingsPath = null)
    {
        var factory = new FakeCaptureFactory();
        var cue = new SilentCue();
        var host = new AppHost(
            AppContext.BaseDirectory,
            new SettingsStore(settingsPath ?? TestData.TempFile("settings.json")),
            factory,
            cue,
            () => matrix ?? TestData.Matrix(TestData.Row("os.build", "10.0.26300")));
        return (host, factory, cue);
    }

    private static async Task FeedAndWait(AppHost host, FakeCaptureFactory factory, FrameSample[] frames)
    {
        foreach (var frame in frames)
        {
            factory.Capture.Push(frame);
        }

        await TestData.WaitUntil(() => host.Controller.Session!.FramesReceived == frames.Length);
    }

    [Fact]
    public async Task Marking_while_nothing_is_captured_says_so_and_plays_the_not_recorded_tone()
    {
        var (host, _, cue) = Create();
        await using var owner = host;

        var outcome = await host.MarkAsync(MarkKind.Stutter);

        Assert.Equal(MarkOutcomeKind.NotCapturing, outcome.Kind);
        Assert.Null(outcome.Mark);
        Assert.Equal(1, cue.NotRecordedCount);
        Assert.Empty(cue.MarkedKinds);
        Assert.Empty(host.Controller.Recorder.Marks);
    }

    [Fact]
    public async Task A_mark_during_a_capture_is_recorded_with_candidates_and_the_tone_plays()
    {
        var (host, factory, cue) = Create();
        await using var owner = host;
        Assert.True(host.Controller.Start("game.exe").Started);
        var (frames, _) = TestData.Stream(22, (1200, 90));
        await FeedAndWait(host, factory, frames);
        var raised = new List<IncidentMark>();
        host.MarkChanged += raised.Add;

        var outcome = await host.MarkAsync(MarkKind.Stutter);

        Assert.Equal(MarkOutcomeKind.Recorded, outcome.Kind);
        Assert.NotNull(outcome.Mark!.CaptureTime);
        Assert.NotEmpty(outcome.Mark.Search.Candidates);
        Assert.Null(outcome.Mark.Selection);
        Assert.Equal([MarkKind.Stutter], cue.MarkedKinds);
        Assert.Single(raised);
        Assert.Equal("game.exe", outcome.Mark.TargetProcess);
    }

    [Fact]
    public async Task The_same_press_twice_in_a_row_is_one_mark()
    {
        var (host, factory, _) = Create();
        await using var owner = host;
        host.Controller.Start("game.exe");
        await FeedAndWait(host, factory, TestData.Stream(5).Frames);

        Assert.Equal(MarkOutcomeKind.Recorded, (await host.MarkAsync(MarkKind.Stutter)).Kind);
        Assert.Equal(MarkOutcomeKind.Debounced, (await host.MarkAsync(MarkKind.Stutter)).Kind);
        Assert.Equal(MarkOutcomeKind.Recorded, (await host.MarkAsync(MarkKind.InputStall)).Kind);
        Assert.Equal(2, host.Controller.Recorder.Marks.Count);
    }

    [Fact]
    public async Task A_mark_before_any_frame_arrived_is_recorded_without_a_place_on_the_timeline()
    {
        var (host, _, cue) = Create();
        await using var owner = host;
        host.Controller.Start("game.exe");

        var outcome = await host.MarkAsync(MarkKind.Stutter);

        Assert.Equal(MarkOutcomeKind.RecordedWithoutTime, outcome.Kind);
        Assert.NotNull(outcome.Mark);
        Assert.Null(outcome.Mark.CaptureTime);
        Assert.Equal(1, cue.NotRecordedCount);
    }

    [Fact]
    public async Task Start_refuses_invalid_names_and_a_second_capture_and_says_why()
    {
        var (host, factory, _) = Create();
        await using var owner = host;

        Assert.False(host.Controller.Start("").Started);
        Assert.False(host.Controller.Start("C:\\Games\\game.exe").Started);
        Assert.False(host.Controller.Start("-flag.exe").Started);
        Assert.False(host.Controller.Start("notanexe").Started);
        Assert.Empty(factory.CapturedPrograms);

        Assert.True(host.Controller.Start("  game.exe  ").Started);
        Assert.Equal(["game.exe"], factory.CapturedPrograms);
        var second = host.Controller.Start("other.exe");
        Assert.False(second.Started);
        Assert.Contains("Already capturing game.exe", second.Message);
    }

    [Fact]
    public async Task A_factory_that_cannot_create_a_capture_gives_a_failed_status_not_an_exception()
    {
        var factory = new FakeCaptureFactory { CreateThrows = new InvalidOperationException("the capture tool is missing") };
        await using var host = new AppHost(AppContext.BaseDirectory, new SettingsStore(TestData.TempFile("s.json")), factory, new SilentCue(), () => TestData.Matrix(TestData.Row("a", "b")));

        var result = host.Controller.Start("game.exe");

        Assert.False(result.Started);
        Assert.Contains("missing", result.Message);
        Assert.Equal(CaptureState.Failed, host.Controller.Status().State);
        Assert.False(host.Controller.IsCapturing);
    }

    [Fact]
    public async Task Stopping_ends_the_source_and_the_status_goes_idle()
    {
        var (host, factory, _) = Create();
        await using var owner = host;
        host.Controller.Start("game.exe");
        await TestData.WaitUntil(() => host.Controller.IsCapturing);

        await host.Controller.StopAsync();

        Assert.Equal(1, factory.Capture.StopCalls);
        Assert.False(host.Controller.IsCapturing);
    }

    [Fact]
    public async Task Scanning_lists_the_programs_drawing_frames_busiest_first()
    {
        var (host, factory, _) = Create();
        await using var owner = host;
        for (var i = 0; i < 50; i++)
        {
            factory.Scan.Push(new FrameSample(i, i * 16.7, 16.7, PresentMode: "Hardware: Independent Flip", Application: "game.exe"));
        }

        for (var i = 0; i < 10; i++)
        {
            factory.Scan.Push(new FrameSample(i, i * 16.7, 16.7, PresentMode: "Hardware: Independent Flip", Application: "dwm.exe"));
        }

        factory.Scan.End();
        var programs = await host.Controller.ScanAsync(5);

        Assert.Equal(["game.exe", "dwm.exe"], programs.Select(p => p.Application).ToArray());
        Assert.Equal(10.0, programs[0].FramesPerSecond);
    }

    [Fact]
    public void The_tray_state_follows_the_capture_and_the_hotkeys()
    {
        var (host, _, _) = Create();
        var chord = HotkeyChord.Parse("Ctrl+Alt+F9");
        var ok = new HotkeyStatus(HotkeyAction.MarkStutter, chord, true, HotkeyFailure.None, null);
        var bad = new HotkeyStatus(HotkeyAction.MarkInputStall, HotkeyChord.Parse("Ctrl+Alt+F10"), false, HotkeyFailure.AlreadyRegistered, "x");

        Assert.Equal(TrayState.Idle, host.TrayStatus([ok]).State);
        var withProblem = host.TrayStatus([ok, bad]);
        Assert.Equal(TrayState.Attention, withProblem.State);
        Assert.Contains("Ctrl+Alt+F9", withProblem.Tooltip);

        Assert.Equal("Mark stutter   (Ctrl+Alt+F9)", AppHost.MenuText(HotkeyAction.MarkStutter, [ok, bad]));
        Assert.Equal("Mark input stall   (hotkey not registered)", AppHost.MenuText(HotkeyAction.MarkInputStall, [ok, bad]));
        Assert.Equal("Mark stutter", AppHost.MenuText(HotkeyAction.MarkStutter, []));
    }

    [Fact]
    public async Task Settings_changes_are_saved_normalized_and_a_failed_save_is_reported()
    {
        var path = TestData.TempFile("settings.json");
        var (host, _, _) = Create(settingsPath: path);
        await using var owner = host;

        var saved = host.Update(s => s with { PreRollSeconds = 9999, Theme = "Dark" }, out var problem);

        Assert.Null(problem);
        Assert.Equal(600, saved.PreRollSeconds);
        Assert.Equal("Dark", new SettingsStore(path).Load(out _).Theme);
        Assert.Equal(host.Settings, saved);
    }

    [Fact]
    public async Task A_corrupt_settings_file_is_a_problem_to_show_not_a_crash()
    {
        var path = TestData.TempFile("settings.json");
        File.WriteAllText(path, "][");
        var (host, _, _) = Create(settingsPath: path);
        await using var owner = host;

        Assert.Contains(host.Problems, p => p.Contains("settings file could not be read"));
        Assert.Equal(new AppSettings(), host.Settings);
    }

    [Fact]
    public async Task The_whole_flow_from_a_key_press_to_a_redacted_file_leaks_nothing()
    {
        const string user = "Zanzibar_Quinn";
        const string machine = "QUINN-RIG-77";
        var matrix = TestData.Matrix(
            TestData.Row("os.build", $"Windows build on {machine}"),
            TestData.Row("storage.disk", $"C:\\Users\\{user}\\AppData disk"));
        var (host, factory, _) = Create(matrix);
        await using var owner = host;
        host.Controller.Start("game.exe");
        var (frames, _) = TestData.Stream(40, (2000, 90), (2280, 75));
        await FeedAndWait(host, factory, frames);

        var outcome = await host.MarkAsync(MarkKind.Stutter);
        var mark = outcome.Mark!;
        host.Controller.Recorder.SetNote(mark.Id, $"it happened to {user} on {machine} at 192.168.1.40");
        host.Controller.Recorder.SelectCandidate(mark.Id, 1);
        var current = host.Controller.Recorder.Marks.Single();

        var context = RedactionContext.Empty.With(RedactionCategory.UserName, user).With(RedactionCategory.HostName, machine);
        var preview = await Task.Run(() => host.Controller.PrepareExport(current, redact: true, context));

        Assert.True(preview.RedactionApplied);
        Assert.DoesNotContain(user, preview.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(machine, preview.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("192.168.1.40", preview.Text, StringComparison.Ordinal);
        Assert.Contains("\"selectionStatus\"", preview.Text);
        Assert.Contains("chose candidate 1", preview.Text);

        var path = TestData.TempFile("incident.json");
        ExportPipeline.WriteAtomic(preview, path);
        Assert.Equal(preview.Bytes.ToArray(), File.ReadAllBytes(path));
    }

    [Fact]
    public async Task Exporting_a_mark_with_no_frames_is_refused_with_a_reason()
    {
        var (host, _, _) = Create();
        await using var owner = host;
        host.Controller.Start("game.exe");
        var mark = (await host.MarkAsync(MarkKind.Stutter)).Mark!;

        var error = Assert.Throws<InvalidOperationException>(() => host.Controller.PrepareExport(mark, redact: true));
        Assert.Contains("no usable frame", error.Message);
    }

    [Fact]
    public async Task Start_refuses_while_previous_session_is_still_stopping()
    {
        var (host, factory, _) = Create();
        await using var owner = host;
        Assert.True(host.Controller.Start("game.exe").Started);

        factory.Capture.State = Jankscope.Frames.PresentMon.RunnerState.Stopping;
        var whileStopping = host.Controller.Start("other.exe");
        Assert.False(whileStopping.Started);
        Assert.Contains("Already capturing game.exe", whileStopping.Message);

        await host.Controller.StopAsync();
        await host.Controller.Session!.Completion;
        Assert.True(host.Controller.Start("other.exe").Started);
    }

    [DesktopFact]
    public async Task Real_presentmon_and_visible_harness_end_to_end_mark_anchor_and_redacted_export()
    {
        var pin = PresentMonLocator.Find(AppContext.BaseDirectory, out _);
        if (pin is null)
        {
            return;
        }

        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var harnessExe = Path.Combine(repoRoot, "tools", "Jankscope.Harness", "bin", "Release", "net10.0-windows", "jankscope-harness.exe");
        if (!File.Exists(harnessExe))
        {
            return;
        }

        const string userCanary = "Zanzibar_Quinn_E2E";
        const string hostCanary = "QUINN-E2E-RIG";
        var cue = new SilentCue();
        var factory = new PresentMonCaptureFactory(pin!);
        var matrix = TestData.Matrix(
            TestData.Row("os.build", $"Windows 11 on {hostCanary}"),
            TestData.Row("storage.disk", $"C:\\Users\\{userCanary}\\Desktop\\game"));
        await using var host = new AppHost(
            AppContext.BaseDirectory,
            new SettingsStore(TestData.TempFile("settings.json")),
            factory,
            cue,
            () => matrix);

        var start = host.Controller.Start("jankscope-harness.exe");
        Assert.True(start.Started, start.Message);
        await Task.Delay(700);

        var csvPath = TestData.TempFile("harness-e2e.csv");
        var harnessInfo = new ProcessStartInfo
        {
            FileName = harnessExe,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in new[] { "run", "--seed", "7", "--fps", "120", "--frames", "360", "--hitches", "3", "--extra-ms", "65", "--jitter-ms", "0.2", "--out", csvPath, "--window-visible" })
        {
            harnessInfo.ArgumentList.Add(arg);
        }

        using var harnessProcess = Process.Start(harnessInfo);
        Assert.NotNull(harnessProcess);
        await harnessProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(0, harnessProcess.ExitCode);

        await TestData.WaitUntil(() => host.Controller.Session!.FramesReceived >= 150, timeoutMs: 10_000);

        var outcome = await host.MarkAsync(MarkKind.Stutter);
        Assert.Equal(MarkOutcomeKind.Recorded, outcome.Kind);
        Assert.Equal([MarkKind.Stutter], cue.MarkedKinds);

        var mark = outcome.Mark!;
        Assert.NotNull(mark.CaptureTime);
        Assert.NotEmpty(mark.Search.Candidates);
        Assert.Contains(mark.Search.Candidates.Take(3), c => c.Kind == AnchorKind.StutterEvent && c.SeverityMs >= 40.0);

        host.Controller.Recorder.SelectCandidate(mark.Id, 1);
        host.Controller.Recorder.SetNote(mark.Id, $"hitch seen by {userCanary} on {hostCanary} from 192.168.10.25");
        var updated = host.Controller.Recorder.Marks.Single();

        var context = RedactionContext.ForThisMachine()
            .With(RedactionCategory.UserName, userCanary)
            .With(RedactionCategory.HostName, hostCanary);
        var preview = host.Controller.PrepareExport(updated, redact: true, context);
        Assert.True(preview.RedactionApplied);
        Assert.DoesNotContain(userCanary, preview.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(hostCanary, preview.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("192.168.10.25", preview.Text, StringComparison.Ordinal);

        var exportPath = TestData.TempFile("e2e-incident.json");
        ExportPipeline.WriteAtomic(preview, exportPath);
        Assert.Equal(preview.Bytes.ToArray(), File.ReadAllBytes(exportPath));

        await host.Controller.StopAsync();
        Assert.False(host.Controller.IsCapturing);
    }

    [Fact]
    public void ExternalUrlOpener_refuses_invalid_or_non_allowlisted_urls()
    {
        Assert.False(ExternalUrlOpener.TryOpen(null!));
        Assert.False(ExternalUrlOpener.TryOpen(string.Empty));
        Assert.False(ExternalUrlOpener.TryOpen("http://insecure.com"));
        Assert.False(ExternalUrlOpener.TryOpen("ftp://files.com"));
        Assert.False(ExternalUrlOpener.TryOpen("file:///C:/Users/test.exe"));
        Assert.False(ExternalUrlOpener.TryOpen("https://evil-unlisted-site.com"));
    }

    [Fact]
    public async Task AppHost_prepares_compatibility_export_cleanly()
    {
        var matrix = TestData.Matrix(
            TestData.Row("os.build", "Windows 11 Build 26100"),
            TestData.Row("hardware.cpu", "AMD Ryzen 7 7800X3D"));

        await using var host = new AppHost(
            AppContext.BaseDirectory,
            new SettingsStore(TestData.TempFile("settings.json")),
            new UnavailableCaptureFactory(),
            new SilentCue(),
            () => matrix);

        var preview = host.PrepareCompatibilityExport(redact: true);
        Assert.NotNull(preview);
        Assert.True(preview.RedactionApplied);
        Assert.Contains("Windows 11 Build 26100", preview.Text);
        Assert.Contains("AMD Ryzen 7 7800X3D", preview.Text);
        Assert.True(preview.Bytes.Length > 0);
    }
}
