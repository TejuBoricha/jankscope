using System.Runtime;
using System.Runtime.ExceptionServices;
using System.Security.Principal;
using Jankscope.Core;

namespace Jankscope.Harness;

/// <summary>Runs a window and a device on a dedicated single-threaded-apartment thread, whatever thread the caller is on.</summary>
internal static class StaThread
{
    public static T Run<T>(Func<T> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        T result = default!;
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = work();
            }
            catch (Exception e)
            {
                failure = ExceptionDispatchInfo.Capture(e);
            }
        })
        {
            Name = "jankscope-harness-render",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
        return result;
    }
}

internal static class HarnessRunner
{
    /// <summary>Presents before the measured loop, through the same code path, so first-use costs (JIT, the first swap-chain flips) are not in the log.</summary>
    public const int WarmUpFrames = 30;

    public const int WindowWidth = 320;
    public const int WindowHeight = 180;

    public static HarnessRunResult Execute(RunOptions options, CancellationToken cancellationToken = default) =>
        Execute(options, new StopwatchClock(), cancellationToken);

    /// <exception cref="HarnessEnvironmentException">No usable Direct3D 11 adapter or window.</exception>
    /// <exception cref="OperationCanceledException">Cancelled; the window, swap chain and device are already released.</exception>
    public static HarnessRunResult Execute(RunOptions options, IClock clock, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        var run = options.Workload.Generate();
        return StaThread.Run(() => ExecuteOnThisThread(options, run, clock, cancellationToken));
    }

    private static HarnessRunResult ExecuteOnThisThread(RunOptions options, SyntheticRun run, IClock clock, CancellationToken cancellationToken)
    {
        // Disposed in reverse order: the device and swap chain go first, then the window they present to.
        using var window = new RenderWindow(options.WindowVisible, WindowWidth, WindowHeight);
        var handle = window.Open();
        using var presenter = D3D11Presenter.Create(handle, WindowWidth, WindowHeight, options.Adapter, options.SimulateNoHardware);

        var schedule = FrameSchedule.Create(run, clock.Frequency);
        var warmRun = new SyntheticRun(0, run.TargetFps, Enumerable.Repeat(1000.0 / run.TargetFps, WarmUpFrames).ToArray(), []);
        var warmSchedule = FrameSchedule.Create(warmRun, clock.Frequency);

        var warmPacer = new FramePacer(clock, FramePacer.CalibrateMarginTicks(clock));
        FrameLoop.Run(warmSchedule, presenter, warmPacer, clock, window.Pump, cancellationToken);

        // The margin the warm-up ended with already reflects any slow sleep it met.
        var pacer = new FramePacer(clock, warmPacer.MarginTicks);

        var previousLatency = GCSettings.LatencyMode;
        GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency; // no blocking gen-2 collection in the middle of the paced loop
        FrameLoopResult loop;
        CpuCost cpu;
        try
        {
            var cpuBefore = Environment.CpuUsage.TotalTime;
            var wallBefore = clock.GetTimestamp();
            loop = FrameLoop.Run(schedule, presenter, pacer, clock, window.Pump, cancellationToken);
            var wallTicks = clock.GetTimestamp() - wallBefore;
            var cpuMs = (Environment.CpuUsage.TotalTime - cpuBefore).TotalMilliseconds;
            var wallMs = QpcTime.ToMs(wallTicks, clock.Frequency);
            cpu = new CpuCost(cpuMs, wallMs, wallMs > 0 ? 100.0 * cpuMs / wallMs : 0);
        }
        finally
        {
            GCSettings.LatencyMode = previousLatency;
        }

        return new HarnessRunResult(
            options.Workload,
            run,
            schedule,
            loop.Records,
            clock.Frequency,
            presenter.Adapter,
            presenter.SwapChain,
            Fidelity.Compute(schedule, loop.Records),
            new PacerReport(QpcTime.ToMs(pacer.MarginTicks, clock.Frequency), pacer.Sleeps, pacer.SpinIterations, pacer.LateArrivals, WarmUpFrames),
            cpu,
            loop.OccludedPresents,
            options.WindowVisible,
            IsElevated());
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
