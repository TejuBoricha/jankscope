using System.Runtime.InteropServices;
using System.Text.Json;
using Jankscope.Core;

namespace Jankscope.Harness;

/// <summary>The JSON summary written next to the CSV. It is tagged Simulated and says in words that it is not a measurement.</summary>
internal static class RunSummary
{
    public const string SimulatedNotice =
        "Simulated ground-truth workload from the Jankscope synthetic harness. It is not a measurement of any game or of the graphics performance of this PC, " +
        "and it can never be exported as one (HC-9).";

    public const string WarpNote = "WARP software adapter: proves the pipeline only, not GPU timing.";

    public const string ToolName = "jankscope-harness";

    public static string ToJson(HarnessRunResult result) => JsonSerializer.Serialize(Create(result), JankscopeJson.Options);

    public static object Create(HarnessRunResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var warp = result.Adapter.Kind == AdapterKind.Warp;
        var f = result.Fidelity;

        return new
        {
            provenance = result.Provenance,
            exportableAsMeasurement = result.Provenance.MayBeExportedAsMeasurement(),
            notice = warp ? SimulatedNotice + " " + WarpNote : SimulatedNotice,
            tool = ToolName,
            run = new
            {
                seed = result.Workload.Seed,
                targetFps = result.Workload.Fps,
                frames = result.Workload.Frames,
                hitches = result.Workload.Hitches,
                extraMs = result.Workload.ExtraMs,
                jitterMs = result.Workload.JitterMs,
            },
            groundTruth = new { hitches = result.Run.Hitches.Select(h => new { frameIndex = h.FrameIndex, extraMs = h.ExtraMs }) },
            adapter = new
            {
                kind = result.Adapter.Kind.ToString().ToLowerInvariant(),
                requested = result.Adapter.Requested.ToString().ToLowerInvariant(),
                description = result.Adapter.Description,
                featureLevel = result.Adapter.FeatureLevel,
                provesPipelineOnly = warp,
                note = warp ? WarpNote : "Hardware adapter; the workload is still simulated.",
                fallbackReason = result.Adapter.FallbackReason,
            },
            swapChain = new
            {
                swapEffect = result.SwapChain.SwapEffect,
                bufferCount = result.SwapChain.BufferCount,
                width = result.SwapChain.Width,
                height = result.SwapChain.Height,
                syncInterval = result.SwapChain.SyncInterval,
                presentFlags = result.SwapChain.PresentFlags,
                allowsTearing = result.SwapChain.AllowsTearing,
                windowVisible = result.WindowVisible,
            },
            timing = new
            {
                qpcFrequencyHz = result.Frequency,
                clock = "Stopwatch (QueryPerformanceCounter)",
                timestampPoint = "Present() return",
                timerResolutionChange = "none",
                sleepMarginMs = result.Pacer.MarginMs,
                sleeps = result.Pacer.Sleeps,
                spinIterations = result.Pacer.SpinIterations,
                lateArrivals = result.Pacer.LateArrivals,
                warmUpFrames = result.Pacer.WarmUpFrames,
                occludedPresents = result.OccludedPresents,
            },
            fidelity = new
            {
                frames = f.Frames,
                presentTimeErrorMs = new { max = f.PresentTimeError.MaxMs, median = f.PresentTimeError.MedianMs, p99 = f.PresentTimeError.P99Ms },
                frameTimeErrorMs = new { max = f.FrameTimeError.MaxMs, median = f.FrameTimeError.MedianMs, p99 = f.FrameTimeError.P99Ms },
                meanSignedPresentErrorMs = f.MeanSignedPresentErrorMs,
                disturbedThresholdMs = Fidelity.DisturbedThresholdMs,
                disturbedFrames = f.DisturbedFrames.Select(d => new { frameIndex = d.FrameIndex, intervalErrorMs = d.IntervalErrorMs }),
                hitches = f.Hitches.Select(h => new
                {
                    frameIndex = h.FrameIndex,
                    injectedExtraMs = h.InjectedExtraMs,
                    achievedFrameTimeMs = h.AchievedFrameTimeMs,
                    carriedExtraMs = h.CarriedExtraMs,
                }),
            },
            cpu = new { cpuMs = result.Cpu.CpuMs, wallMs = result.Cpu.WallMs, percentOfOneCore = result.Cpu.PercentOfOneCore },
            environment = new
            {
                elevated = result.Elevated,
                os = RuntimeInformation.OSDescription,
                architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                runtime = RuntimeInformation.FrameworkDescription,
            },
        };
    }
}
