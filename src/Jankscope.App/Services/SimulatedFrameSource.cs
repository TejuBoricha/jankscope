using System.Threading.Channels;
using Jankscope.Core;
using Jankscope.Frames;
using Jankscope.Frames.Capture;
using Jankscope.Frames.PresentMon;

namespace Jankscope.App.Services;

/// <summary>
/// Built-in interactive stutter simulator (<see cref="DataProvenance.Simulated"/>, HC-9).
/// Streams a deterministic 120 FPS frame sequence with realistic shader-compile and asset-streaming hitches
/// into the standard <see cref="CaptureSession"/> pipeline so users can experience Jankscope's live oscilloscope,
/// mark workflow, candidate anchor finder, and evidence-graded Investigator with one click before launching a game.
/// </summary>
public sealed class SimulatedFrameSource : IFrameSource
{
    private readonly Channel<CapturedFrame> _channel = Channel.CreateUnbounded<CapturedFrame>();
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _cts = new();
    private readonly string _applicationName;
    private readonly bool _fastFill;
    private volatile RunnerState _state = RunnerState.NotStarted;
    private long _rowsParsed;

    public SimulatedFrameSource(string applicationName = "simulated-workbench.exe", bool fastFill = true)
    {
        _applicationName = applicationName;
        _fastFill = fastFill;
    }

    public ChannelReader<CapturedFrame> Samples => _channel.Reader;

    public Task Completion => _completion.Task;

    public RunnerHealth Health => new(
        _state,
        Restarts: 0,
        LastError: null,
        LinesRead: Interlocked.Read(ref _rowsParsed),
        RowsParsed: Interlocked.Read(ref _rowsParsed),
        Problems: [],
        ProblemCount: 0,
        DroppedFrames: 0,
        Notes: ["SIMULATED workload (HC-9): tagged as DataProvenance.Simulated so it is never confused with a real game measurement."],
        SessionName: "Jankscope-Simulated-Lab",
        Stalls: 0,
        UncleanedSession: null);

    public void Start()
    {
        _state = RunnerState.Running;
        _ = Task.Run(RunSimulationAsync);
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _state = RunnerState.Stopped;
        _cts.Cancel();
        _channel.Writer.TryComplete();
        _completion.TrySetResult();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _cts.Dispose();
        _channel.Writer.TryComplete();
        _completion.TrySetResult();
        return ValueTask.CompletedTask;
    }

    /// <summary>Generates a realistic 25-second 120 FPS frame stream with 3 distinct stutter signatures (isolated severe spike, double-frame hitch, and asset-streaming burst).</summary>
    public static FrameSample[] BuildDemoSequence(string appName = "simulated-workbench.exe", int totalFrames = 3000)
    {
        var samples = new FrameSample[totalFrames];
        var clockMs = 0.0;
        const double baselineMs = 1000.0 / 120.0; // 8.333 ms (120 FPS)

        for (var i = 0; i < totalFrames; i++)
        {
            // Small natural frame-time breathing (+/- 0.35 ms)
            var wave = Math.Sin(i * 0.17) * 0.28 + Math.Cos(i * 0.05) * 0.12;
            var extraMs = 0.0;

            // Signature 1 (~5.5 s before end): Isolated 68 ms shader-compile stall
            if (i == totalFrames - 660)
            {
                extraMs = 60.0;
            }
            // Signature 2 (~2.8 s before end): 3-frame traversal / asset-streaming burst
            else if (i == totalFrames - 340)
            {
                extraMs = 38.0;
            }
            else if (i == totalFrames - 339)
            {
                extraMs = 46.0;
            }
            else if (i == totalFrames - 338)
            {
                extraMs = 29.0;
            }
            // Signature 3 (~0.35 s before mark, right inside the 150-400 ms human reaction-time cone): 82 ms major frame freeze
            else if (i == totalFrames - 42)
            {
                extraMs = 74.0;
            }

            var frameMs = Math.Max(2.0, baselineMs + wave + extraMs);
            clockMs += frameMs;
            samples[i] = new FrameSample(
                Index: i,
                TimestampMs: clockMs,
                FrameTimeMs: frameMs,
                DisplayedFrameTimeMs: frameMs,
                CpuBusyMs: frameMs * 0.72,
                GpuBusyMs: frameMs * 0.85,
                PresentMode: "Hardware: Independent Flip",
                Application: appName,
                ProcessId: 4242,
                Provenance: DataProvenance.Simulated);
        }

        return samples;
    }

    private async Task RunSimulationAsync()
    {
        try
        {
            var prefill = BuildDemoSequence(_applicationName, 2400);
            foreach (var sample in prefill)
            {
                if (_cts.IsCancellationRequested)
                {
                    return;
                }

                _channel.Writer.TryWrite(new CapturedFrame(1, sample));
                Interlocked.Increment(ref _rowsParsed);
            }

            if (_fastFill)
            {
                var nextIndex = (long)prefill.Length;
                var clockMs = prefill[^1].TimestampMs;
                while (!_cts.IsCancellationRequested)
                {
                    await Task.Delay(16, _cts.Token).ConfigureAwait(false);
                    for (var k = 0; k < 2; k++)
                    {
                        var spike = (nextIndex % 260 == 0) ? 48.0 : 0.0;
                        var frameMs = 8.33 + (Math.Sin(nextIndex * 0.2) * 0.25) + spike;
                        clockMs += frameMs;
                        var sample = new FrameSample(
                            Index: nextIndex++,
                            TimestampMs: clockMs,
                            FrameTimeMs: frameMs,
                            DisplayedFrameTimeMs: frameMs,
                            CpuBusyMs: frameMs * 0.7,
                            GpuBusyMs: frameMs * 0.85,
                            PresentMode: "Hardware: Independent Flip",
                            Application: _applicationName,
                            ProcessId: 4242,
                            Provenance: DataProvenance.Simulated);
                        _channel.Writer.TryWrite(new CapturedFrame(1, sample));
                        Interlocked.Increment(ref _rowsParsed);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // normal stop
        }
        finally
        {
            _channel.Writer.TryComplete();
            _completion.TrySetResult();
        }
    }
}
