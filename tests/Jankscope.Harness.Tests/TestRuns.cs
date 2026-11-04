using Jankscope.Core;

namespace Jankscope.Harness.Tests;

/// <summary>Builds complete run results from the virtual clock, so output formats can be tested on any machine.</summary>
internal static class TestRuns
{
    public static HarnessRunResult Fake(
        AdapterKind kind = AdapterKind.Warp,
        int seed = 5,
        double fps = 144,
        int frames = 240,
        int hitches = 2,
        double extra = 25,
        double jitter = 0.2,
        bool allowsTearing = true)
    {
        var clock = new FakeClock { Now = 123_456 };
        var run = SyntheticWorkload.Generate(seed, fps, frames, hitches, extra, jitter);
        var schedule = FrameSchedule.Create(run, clock.Frequency);
        var pacer = new FramePacer(clock, 20_000);
        var loop = FrameLoop.Run(schedule, new FakePresenter(clock), pacer, clock);
        var adapter = kind == AdapterKind.Warp
            ? new AdapterInfo(AdapterKind.Warp, "Software Rasterizer (test)", "Level_11_1", AdapterRequest.Warp, null)
            : new AdapterInfo(AdapterKind.Hardware, "Test GPU 0", "Level_11_1", AdapterRequest.Auto, null);
        return new HarnessRunResult(
            new WorkloadOptions(seed, fps, frames, hitches, extra, jitter),
            run,
            schedule,
            loop.Records,
            clock.Frequency,
            adapter,
            new SwapChainInfo("FlipDiscard", 3, 320, 180, 0, allowsTearing ? 512 : 0, allowsTearing, "0xABC123"),
            Fidelity.Compute(schedule, loop.Records),
            new PacerReport(MarginMs: 2.0, Sleeps: pacer.Sleeps, SpinIterations: pacer.SpinIterations, LateArrivals: pacer.LateArrivals, WarmUpFrames: 30),
            new CpuCost(CpuMs: 100, WallMs: 200, PercentOfOneCore: 50),
            OccludedPresents: 0,
            WindowVisible: false,
            Elevated: false);
    }
}
