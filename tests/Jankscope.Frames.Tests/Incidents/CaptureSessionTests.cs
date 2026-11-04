using System.Threading.Channels;
using Jankscope.Core;
using Jankscope.Frames.Capture;
using Jankscope.Frames.Incidents;
using Jankscope.Frames.PresentMon;
using Jankscope.Frames.Ring;
using Jankscope.Frames.Tests.PresentMon;

namespace Jankscope.Frames.Tests.Incidents;

/// <summary>A frame source driven by the test: it holds whatever frames the test pushes and ends when told.</summary>
internal sealed class ScriptedFrameSource : IFrameSource
{
    private readonly Channel<CapturedFrame> _channel = Channel.CreateUnbounded<CapturedFrame>();
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public RunnerState State { get; set; } = RunnerState.NotStarted;

    public bool Started { get; private set; }

    public int StopCalls { get; private set; }

    public Exception? StartThrows { get; init; }

    public ChannelReader<CapturedFrame> Samples => _channel.Reader;

    public Task Completion => _completion.Task;

    public RunnerHealth Health => new(State, 0, null, 0, 0, [], 0, 0, [], null, 0, null);

    public void Push(int run, FrameSample sample) => _channel.Writer.TryWrite(new CapturedFrame(run, sample));

    public void End()
    {
        _channel.Writer.TryComplete();
        _completion.TrySetResult();
    }

    public void Start()
    {
        if (StartThrows is not null)
        {
            throw StartThrows;
        }

        Started = true;
        State = RunnerState.Running;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        StopCalls++;
        State = RunnerState.Stopped;
        End();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public class CaptureSessionTests
{
    private static (CaptureSession Session, ScriptedFrameSource Source, ManualTimeProvider Time) Create(int capacity = 4096)
    {
        var time = new ManualTimeProvider();
        var source = new ScriptedFrameSource();
        var session = new CaptureSession(source, new RingBuffer<FrameSample>(capacity), new CaptureClockMap(time.TimestampFrequency), time);
        return (session, source, time);
    }

    private static FrameSample Frame(long index, double timestampMs, string? mode = "Hardware: Independent Flip") =>
        new(index, timestampMs, 16.667, PresentMode: mode, Application: "game.exe", Provenance: DataProvenance.Measured);

    [Fact]
    public async Task Frames_reach_the_history_and_the_clock_map_and_are_counted()
    {
        var (session, source, time) = Create();
        session.Start();
        for (var i = 0; i < 100; i++)
        {
            source.Push(1, Frame(i, (i + 1) * 16.667));
        }

        await session.StopAsync();

        Assert.Equal(100, session.FramesReceived);
        Assert.Equal(100, session.History.Count);
        Assert.Equal(100, session.Clock.Observations);
        Assert.Equal(1, session.Run);
        Assert.Equal(0, session.WriteFailures);
        Assert.NotNull(session.SinceLastFrame);
        Assert.Equal("game.exe", session.History.Snapshot()[0].Application);
        _ = time;
    }

    [Fact]
    public async Task Frames_queued_when_stop_is_called_are_all_written_before_it_returns()
    {
        var (session, source, _) = Create(capacity: 8192);
        session.Start();
        for (var i = 0; i < 5000; i++)
        {
            source.Push(1, Frame(i, (i + 1) * 2.0));
        }

        await session.StopAsync();
        Assert.Equal(5000, session.FramesReceived);
        await session.StopAsync(); // safe to repeat
        Assert.Equal(2, source.StopCalls);
    }

    [Fact]
    public async Task Frames_with_the_present_mode_other_are_ignored_and_counted_not_lost_silently()
    {
        var (session, source, _) = Create();
        session.Start();
        source.Push(1, Frame(0, 10, "Other"));
        source.Push(1, Frame(1, 20, "Hardware: Independent Flip"));
        source.Push(1, Frame(2, 30, "Other"));
        source.Push(1, Frame(3, 40, null));
        await session.StopAsync();

        Assert.Equal(2, session.FramesReceived);
        Assert.Equal(2, session.IgnoredFrames);
        Assert.Equal([1L, 3L], session.History.Snapshot().Select(f => f.Index).ToArray());
    }

    [Fact]
    public async Task A_restarted_capture_is_a_new_run_and_a_new_clock_epoch()
    {
        var (session, source, time) = Create();
        session.Start();
        for (var i = 0; i < 50; i++)
        {
            source.Push(1, Frame(i, 100 + i * 100));
        }

        source.Push(2, Frame(0, 20)); // PresentMon was restarted: its clock starts again
        await session.StopAsync();

        Assert.Equal(2, session.Run);
        var estimate = session.Clock.Estimate(time.GetTimestamp());
        Assert.Equal(1, estimate!.Value.Epoch);
        Assert.Equal(1, estimate.Value.Observations);
    }

    [Fact]
    public async Task Waiting_for_frames_is_reported_while_running_but_quiet()
    {
        var (session, source, time) = Create();
        var quiet = TimeSpan.FromSeconds(2);
        Assert.False(session.WaitingForFrames(quiet)); // not started: nothing to wait for

        session.Start();
        Assert.True(session.WaitingForFrames(quiet));  // running, no frame yet

        source.Push(1, Frame(0, 10));
        await WaitUntil(() => session.FramesReceived == 1);
        Assert.False(session.WaitingForFrames(quiet));

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.False(session.WaitingForFrames(quiet));
        time.Advance(TimeSpan.FromSeconds(5));
        Assert.True(session.WaitingForFrames(quiet));

        await session.StopAsync();
        Assert.False(session.WaitingForFrames(quiet));
    }

    [Fact]
    public async Task A_source_that_refuses_to_start_starts_nothing_and_the_error_reaches_the_caller()
    {
        var time = new ManualTimeProvider();
        var source = new ScriptedFrameSource { StartThrows = new InvalidOperationException("Capture suppressed by anti-cheat safety: test") };
        await using var session = new CaptureSession(source, new RingBuffer<FrameSample>(64), new CaptureClockMap(time.TimestampFrequency), time);

        var error = Assert.Throws<InvalidOperationException>(session.Start);
        Assert.Contains("anti-cheat", error.Message);
        Assert.False(source.Started);
        Assert.Equal(0, session.FramesReceived);
    }

    [Fact]
    public void A_session_cannot_be_started_twice()
    {
        var (session, _, _) = Create();
        session.Start();
        Assert.Throws<InvalidOperationException>(session.Start);
    }

    [Fact]
    public async Task Disposing_stops_the_source()
    {
        var (session, source, _) = Create();
        session.Start();
        await session.DisposeAsync();
        Assert.Equal(RunnerState.Stopped, source.State);
    }

    [Fact]
    public async Task The_real_runner_and_the_fake_executable_feed_the_history_and_leave_nothing_behind()
    {
        using var fake = new FakePresentMonInstall("rows=40");
        var options = new PresentMonRunnerOptions
        {
            Executable = fake.Pin,
            SessionName = "Jankscope-" + Environment.ProcessId + "-" + Guid.NewGuid().ToString("N")[..6],
            ProcessNames = ["game.exe"],
            TimedSeconds = 1,
            WatchdogNoOutputTimeout = Timeout.InfiniteTimeSpan,
            StopGracePeriod = TimeSpan.FromSeconds(10),
            CleanupTimeout = TimeSpan.FromSeconds(20),
        };
        var runner = new PresentMonRunner(options, new PresentMonProcessLauncher(), () => new TestFrameParser());
        await using var session = new CaptureSession(
            new RunnerFrameSource(runner), new RingBuffer<FrameSample>(1024), new CaptureClockMap(TimeProvider.System.TimestampFrequency));

        session.Start();
        await runner.Completion.WaitAsync(TimeSpan.FromSeconds(60));
        await session.StopAsync();

        Assert.Equal(40, session.FramesReceived);
        Assert.Equal(40, session.History.Count);
        Assert.Equal(RunnerState.Completed, session.Health.State);
        Assert.Equal(1, session.Run);
        Assert.Empty(fake.LiveSessions);
        await fake.AssertAllLaunchedProcessesGone();
    }

    [Fact]
    public async Task Anti_cheat_suppression_on_the_real_runner_starts_no_process()
    {
        using var fake = new FakePresentMonInstall();
        var options = new PresentMonRunnerOptions
        {
            Executable = fake.Pin,
            SessionName = "Jankscope-" + Environment.ProcessId + "-" + Guid.NewGuid().ToString("N")[..6],
            AntiCheatSuppressionCheck = () => new SuppressionDecision(true, AntiCheatSafety.SuppressedReason),
        };
        var runner = new PresentMonRunner(options, new PresentMonProcessLauncher(), () => new TestFrameParser());
        await using var session = new CaptureSession(
            new RunnerFrameSource(runner), new RingBuffer<FrameSample>(64), new CaptureClockMap(TimeProvider.System.TimestampFrequency));

        Assert.Throws<InvalidOperationException>(session.Start);
        Assert.True(fake.NeverLaunched);
    }

    private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 10_000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                Assert.Fail("the condition was not met in time");
            }

            await Task.Delay(5);
        }
    }
}

public class PresentingProgramScannerTests
{
    private static FrameSample Frame(string? app, double frameMs, string? mode = "Hardware: Independent Flip") =>
        new(0, 0, frameMs, PresentMode: mode, Application: app);

    [Fact]
    public void Programs_are_grouped_and_listed_busiest_first_with_rate_and_median()
    {
        var frames = new List<FrameSample>();
        frames.AddRange(Enumerable.Repeat(Frame("game.exe", 6.94), 1440)); // 144 fps for 10 s
        frames.AddRange(Enumerable.Repeat(Frame("dwm.exe", 16.67), 600));
        frames.AddRange(Enumerable.Repeat(Frame("Chat.exe", 33.3), 300));
        var list = PresentingProgramScanner.Summarize(frames, 10);

        Assert.Equal(["game.exe", "dwm.exe", "Chat.exe"], list.Select(p => p.Application).ToArray());
        Assert.Equal(144.0, list[0].FramesPerSecond);
        Assert.Equal(6.94, list[0].MedianFrameTimeMs);
        Assert.Equal(1440, list[0].Frames);
    }

    [Fact]
    public void Names_are_matched_without_regard_to_case_and_unnamed_frames_are_not_hidden()
    {
        var list = PresentingProgramScanner.Summarize(
            [Frame("Game.EXE", 10), Frame("game.exe", 10), Frame(null, 10), Frame("  ", 10)], 1);
        Assert.Equal(2, list.Count);
        Assert.Equal(2, list.Single(p => p.Application.Equals("game.exe", StringComparison.OrdinalIgnoreCase)).Frames);
        Assert.Equal(2, list.Single(p => p.Application == "<unknown>").Frames);
    }

    [Fact]
    public void Other_mode_and_unusable_frame_times_are_left_out()
    {
        var list = PresentingProgramScanner.Summarize(
            [Frame("a.exe", 10, "Other"), Frame("a.exe", 0), Frame("a.exe", double.NaN), Frame("a.exe", -1), Frame("a.exe", 12)], 1);
        Assert.Equal(1, Assert.Single(list).Frames);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void A_scan_needs_a_positive_duration(double seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PresentingProgramScanner.Summarize([], seconds));
    }

    [Fact]
    public async Task Scanning_reads_the_source_to_its_end_and_always_stops_it()
    {
        var source = new ScriptedFrameSource();
        source.Push(1, Frame("a.exe", 10));
        source.Push(1, Frame("b.exe", 10));
        source.Push(1, Frame("a.exe", 10));
        source.End();

        var list = await PresentingProgramScanner.ScanAsync(source, 5);

        Assert.True(source.Started);
        Assert.Equal(1, source.StopCalls);
        Assert.Equal(["a.exe", "b.exe"], list.Select(p => p.Application).ToArray());
        Assert.Equal(0.4, list[0].FramesPerSecond);
    }

    [Fact]
    public async Task A_scan_that_is_cancelled_still_stops_the_source()
    {
        var source = new ScriptedFrameSource();
        using var cts = new CancellationTokenSource();
        var scan = PresentingProgramScanner.ScanAsync(source, 5, cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scan);
        Assert.Equal(1, source.StopCalls);
    }
}
