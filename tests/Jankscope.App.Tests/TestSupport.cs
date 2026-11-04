using System.Threading.Channels;
using System.Windows;
using System.Windows.Threading;
using Jankscope.App.Services;
using Jankscope.Core;
using Jankscope.Frames;
using Jankscope.Frames.Capture;
using Jankscope.Frames.PresentMon;

namespace Jankscope.App.Tests;

/// <summary>One shared STA thread with one WPF application (a process can have only one), so interface tests never fight over it.</summary>
internal static class WpfHost
{
    private static readonly Lazy<Dispatcher> Instance = new(Start, LazyThreadSafetyMode.ExecutionAndPublication);

    public static Dispatcher Dispatcher => Instance.Value;

    public static Task<T> RunAsync<T>(Func<T> work) => Dispatcher.InvokeAsync(work).Task;

    public static Task RunAsync(Action work) => Dispatcher.InvokeAsync(work).Task;

    private static Dispatcher Start()
    {
        Dispatcher? dispatcher = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            ThemeService.Apply(application, "Dark");
            application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/jankscope-app;component/Themes/Styles.xaml", UriKind.Relative) });
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "WPF test host",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return dispatcher!;
    }
}

[CollectionDefinition("WPF", DisableParallelization = true)]
public sealed class WpfCollection;

/// <summary>A frame source the test drives: it holds the frames the test pushes and ends when told.</summary>
internal sealed class ScriptedSource : IFrameSource
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

    public void Push(FrameSample sample) => _channel.Writer.TryWrite(new CapturedFrame(1, sample));

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

internal sealed class FakeCaptureFactory : ICaptureFactory
{
    public ScriptedSource Capture { get; } = new();

    public ScriptedSource Scan { get; } = new();

    public List<string> CapturedPrograms { get; } = [];

    public Exception? CreateThrows { get; init; }

    public IFrameSource CreateCapture(string programName)
    {
        if (CreateThrows is not null)
        {
            throw CreateThrows;
        }

        CapturedPrograms.Add(programName);
        return Capture;
    }

    public IFrameSource CreateScan(int seconds) => Scan;
}

internal static class TestData
{
    public const double Frame = 1000.0 / 60.0;

    public static CapabilityMatrix Matrix(params CapabilityEntry[] entries) =>
        new(DateTimeOffset.Parse("2026-11-04T00:00:00Z"), "0.0.0-test", entries, []);

    public static CapabilityEntry Row(string id, string value) =>
        CapabilityEntry.Available(id, "Title of " + id, CapabilityTier.A, "test", value, DataProvenance.Measured);

    /// <summary>Frames at 60 fps with the given frames lengthened, ending at the returned time, as a PresentMon log would give them.</summary>
    public static (FrameSample[] Frames, double EndMs) Stream(double seconds, params (int Index, double ExtraMs)[] spikes)
    {
        var count = (int)(seconds * 60);
        var extra = spikes.ToDictionary(s => s.Index, s => s.ExtraMs);
        var frames = new FrameSample[count];
        var clock = 0.0;
        for (var i = 0; i < count; i++)
        {
            var length = Frame + (extra.TryGetValue(i, out var e) ? e : 0.0);
            clock += length;
            frames[i] = new FrameSample(i, clock, length, PresentMode: "Hardware: Independent Flip", Application: "game.exe", Provenance: DataProvenance.Fixture);
        }

        return (frames, clock);
    }

    public static async Task WaitUntil(Func<bool> condition, int timeoutMs = 10_000)
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

    public static string TempFile(string name)
    {
        var directory = Directory.CreateTempSubdirectory("jankscope-app-tests-");
        return Path.Combine(directory.FullName, name);
    }
}
