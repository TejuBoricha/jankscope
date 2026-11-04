using System.Globalization;
using System.Security.Principal;
using System.Text.Json;
using Jankscope.Core;
using Xunit.Abstractions;

namespace Jankscope.Harness.Tests;

/// <summary>Runs the paced loop for a few seconds on one adapter, once, and shares the result with the tests that only read it.</summary>
public sealed class D3dRunFixture
{
    private readonly Lazy<HarnessRunResult> _warp = new(() => HarnessRunner.Execute(Options(AdapterRequest.Warp)));
    private readonly Lazy<HarnessRunResult> _hardware = new(() => HarnessRunner.Execute(Options(AdapterRequest.Hardware)));

    internal HarnessRunResult Warp => _warp.Value;

    internal HarnessRunResult Hardware => _hardware.Value;

    /// <summary>240 fps (a 4.2 ms frame, the demanding end), 2 s, three 25 ms hitches, 0.2 ms jitter.</summary>
    internal static RunOptions Options(AdapterRequest adapter, int seed = 21, double fps = 240, int frames = 480, int hitches = 3, double extra = 25, double jitter = 0.2, bool visible = false) =>
        new(new WorkloadOptions(seed, fps, frames, hitches, extra, jitter), "unused.csv", adapter, visible);
}

/// <summary>
/// Integration tests of the real renderer (test plan row 7). They need a Direct3D 11 device and are skipped with the reason where there is none.
/// All timing assertions use robust statistics (median, 99th percentile) with a generous bound for the single worst frame, because a
/// loaded machine can deschedule a thread for a few milliseconds at any time; the bounds are justified next to each constant and the
/// measured values are printed to the test log.
/// </summary>
[Trait("requires", "d3d")]
[Collection("d3d")]
public class D3dHarnessTests(D3dRunFixture fixture, ITestOutputHelper log) : IClassFixture<D3dRunFixture>
{
    // Measured on the reference laptop (hardware and WARP, 240 fps): median |error| 0.08-0.11 ms, p99 0.26-0.34 ms, worst frame 0.85-2.7 ms.
    // The bounds below leave 5x / 3x headroom on median / p99, so an unloaded run never trips them, and still fail a pacer that does not pace:
    // a plain Sleep(1) loop on a 15.6 ms timer tick misses its target by whole milliseconds.
    private const double MedianErrorLimitMs = 0.5;
    private const double P99ErrorLimitMs = 1.0;

    // There is deliberately NO bound on the single worst frame. Over 48 000 measured frames the worst were 7 ms (hardware and WARP, 144 fps) and
    // 25 ms (WARP, 240 fps, a stall inside Present()): a thread can be descheduled, or WARP's worker threads starved, for longer than a hitch at
    // any time. A hard cap would make this suite fail about once in fifty runs for reasons that are not the harness's. What is asserted instead is
    // that stalls are rare (the disturbed-frame budget below), that p99 is tight, and that nothing drifts. The summary lists every disturbed frame.
    private const double DisturbedBudget = 0.02; // at most 2 % of frames may be off by more than 2 ms

    // A 25 ms hitch must be read back as 25 ms to within this much: the p99 interval error is about a quarter of a millisecond; 3 ms also covers one stall
    // landing exactly on a hitch frame, and is far below the detector's 4 ms-above-median rule.
    private const double HitchExtraToleranceMs = 3.0;

    private void Report(string label, HarnessRunResult r)
    {
        var f = r.Fidelity;
        var inv = CultureInfo.InvariantCulture;
        log.WriteLine(string.Create(inv, $"[{label}] adapter {r.Adapter.Kind} '{r.Adapter.Description}', {r.Records.Count} frames at {r.Workload.Fps} fps, QPC {r.Frequency} Hz"));
        log.WriteLine(string.Create(inv, $"[{label}] Present() return vs schedule |error|: max {f.PresentTimeError.MaxMs:F3} ms, median {f.PresentTimeError.MedianMs:F3} ms, p99 {f.PresentTimeError.P99Ms:F3} ms, mean signed {f.MeanSignedPresentErrorMs:F3} ms"));
        log.WriteLine(string.Create(inv, $"[{label}] frame time vs ground truth |error|: max {f.FrameTimeError.MaxMs:F3} ms, median {f.FrameTimeError.MedianMs:F3} ms, p99 {f.FrameTimeError.P99Ms:F3} ms"));
        log.WriteLine(string.Create(inv, $"[{label}] harness CPU {r.Cpu.CpuMs:F0} ms over {r.Cpu.WallMs:F0} ms = {r.Cpu.PercentOfOneCore:F0} % of one core; sleep margin {r.Pacer.MarginMs:F2} ms, {r.Pacer.Sleeps} sleeps, {r.Pacer.LateArrivals} late"));
        foreach (var h in f.Hitches)
        {
            log.WriteLine(string.Create(inv, $"[{label}] hitch at frame {h.FrameIndex}: injected {h.InjectedExtraMs:F1} ms, interval {h.AchievedFrameTimeMs:F3} ms, carried extra {h.CarriedExtraMs:F3} ms"));
        }
    }

    private static void AssertWithinTolerance(HarnessRunResult r)
    {
        var f = r.Fidelity;
        Assert.True(f.PresentTimeError.MedianMs <= MedianErrorLimitMs, $"median Present() error {f.PresentTimeError.MedianMs:F3} ms > {MedianErrorLimitMs} ms");
        Assert.True(f.PresentTimeError.P99Ms <= P99ErrorLimitMs, $"p99 Present() error {f.PresentTimeError.P99Ms:F3} ms > {P99ErrorLimitMs} ms");
        Assert.True(f.FrameTimeError.MedianMs <= MedianErrorLimitMs, $"median frame-time error {f.FrameTimeError.MedianMs:F3} ms");
        Assert.True(f.FrameTimeError.P99Ms <= P99ErrorLimitMs * 1.5, $"p99 frame-time error {f.FrameTimeError.P99Ms:F3} ms"); // the difference of two errors
        Assert.True(f.DisturbedFrames.Count <= DisturbedBudget * f.Frames, $"{f.DisturbedFrames.Count} of {f.Frames} frames were disturbed by more than {Fidelity.DisturbedThresholdMs} ms");
    }

    [D3dFact]
    public void A_WARP_run_follows_the_schedule_within_tolerance()
    {
        var r = fixture.Warp;
        Report("warp", r);

        Assert.Equal(AdapterKind.Warp, r.Adapter.Kind);
        Assert.Equal(480, r.Records.Count);
        AssertWithinTolerance(r);
    }

    [D3dFact(requireHardware: true)]
    public void A_hardware_run_follows_the_schedule_within_tolerance()
    {
        var r = fixture.Hardware;
        Report("hardware", r);

        Assert.Equal(AdapterKind.Hardware, r.Adapter.Kind);
        Assert.Equal(AdapterRequest.Hardware, r.Adapter.Requested);
        AssertWithinTolerance(r);
    }

    [D3dFact]
    public void Absolute_pacing_does_not_drift_over_the_run()
    {
        // Targets are absolute, so the error in the second half of the run must look like the error in the first half. A pacer that waited a
        // frame time after each present would add its per-frame overhead every frame, and the error would grow along the run.
        var r = fixture.Warp;
        double MedianSignedErrorMs(IEnumerable<FrameRecord> records) =>
            records.Select(x => QpcTime.ToMs(x.PresentTicks - x.ScheduledTicks, r.Frequency)).Order().ElementAt(records.Count() / 2);

        var first = MedianSignedErrorMs(r.Records.Take(r.Records.Count / 2));
        var second = MedianSignedErrorMs(r.Records.Skip(r.Records.Count / 2));

        log.WriteLine(string.Create(CultureInfo.InvariantCulture, $"median signed Present() error: first half {first:F3} ms, second half {second:F3} ms"));
        Assert.True(Math.Abs(second - first) <= 0.3, $"drift {second - first:F3} ms between the halves");
    }

    [D3dFact]
    public void Hitch_frames_carry_the_injected_extra_within_tolerance()
    {
        var r = fixture.Warp;
        Report("warp", r);

        Assert.Equal(3, r.Fidelity.Hitches.Count);
        Assert.All(r.Fidelity.Hitches, h =>
        {
            Assert.Equal(25.0, h.InjectedExtraMs);
            Assert.InRange(h.CarriedExtraMs, 25.0 - HitchExtraToleranceMs, 25.0 + HitchExtraToleranceMs);
        });

        // ...and every other frame is ordinary: the median achieved interval of the non-hitch frames is the base frame time, not base + extra.
        var hitchFrames = r.Fidelity.Hitches.Select(h => h.FrameIndex).ToHashSet();
        var ordinary = Enumerable.Range(1, r.Records.Count - 1)
            .Where(i => !hitchFrames.Contains(i))
            .Select(i => QpcTime.ToMs(r.Records[i].PresentTicks - r.Records[i - 1].PresentTicks, r.Frequency))
            .Order().ToList();
        Assert.InRange(ordinary[ordinary.Count / 2], 1000.0 / 240 - 0.3, 1000.0 / 240 + 0.3);
    }

    [D3dFact]
    public void The_schedule_is_deterministic_run_to_run_and_changes_with_the_seed()
    {
        var a1 = HarnessRunner.Execute(D3dRunFixture.Options(AdapterRequest.Warp, seed: 31, frames: 150, hitches: 1));
        var a2 = HarnessRunner.Execute(D3dRunFixture.Options(AdapterRequest.Warp, seed: 31, frames: 150, hitches: 1));
        var b = HarnessRunner.Execute(D3dRunFixture.Options(AdapterRequest.Warp, seed: 32, frames: 150, hitches: 1));

        Assert.Equal(a1.Schedule.TargetTicks, a2.Schedule.TargetTicks);
        Assert.Equal(a1.Run.Hitches, a2.Run.Hitches);
        Assert.NotEqual(a1.Schedule.TargetTicks, b.Schedule.TargetTicks);

        // What was scheduled is also what the log says was scheduled, to the 4 decimals of the CSV.
        Assert.Equal(a1.Records.Select(x => x.ScheduledTicks), a1.Schedule.TargetTicks);
        Assert.Equal(a1.Records.Select(x => x.InjectedExtraMs), a2.Records.Select(x => x.InjectedExtraMs));
    }

    [D3dFact]
    public void Forcing_the_software_adapter_uses_WARP_and_the_output_says_it_proves_the_pipeline_only()
    {
        var r = fixture.Warp;

        Assert.Equal(AdapterKind.Warp, r.Adapter.Kind);
        Assert.Equal(AdapterRequest.Warp, r.Adapter.Requested);
        Assert.Null(r.Adapter.FallbackReason); // it was asked for, not a fallback
        Assert.Contains("WARP", r.Adapter.Description);

        var summary = JsonDocument.Parse(RunSummary.ToJson(r)).RootElement;
        Assert.True(summary.GetProperty("adapter").GetProperty("provesPipelineOnly").GetBoolean());
        Assert.Contains("proves the pipeline only", summary.GetProperty("notice").GetString());
    }

    [D3dFact(requireHardware: true)]
    public void A_hardware_run_does_not_claim_to_prove_the_pipeline_only()
    {
        var summary = JsonDocument.Parse(RunSummary.ToJson(fixture.Hardware)).RootElement;

        Assert.Equal("hardware", summary.GetProperty("adapter").GetProperty("kind").GetString());
        Assert.False(summary.GetProperty("adapter").GetProperty("provesPipelineOnly").GetBoolean());
        Assert.Equal("Simulated", summary.GetProperty("provenance").GetString()); // still a simulated workload
    }

    [D3dFact]
    public void Auto_falls_back_to_WARP_and_records_why_when_no_hardware_adapter_is_usable()
    {
        var options = new RunOptions(new WorkloadOptions(1, 144, 90, 0, 25, 0), "unused.csv", AdapterRequest.Auto, false) { SimulateNoHardware = true };

        var r = HarnessRunner.Execute(options);

        Assert.Equal(AdapterKind.Warp, r.Adapter.Kind);
        Assert.Equal(AdapterRequest.Auto, r.Adapter.Requested);
        Assert.False(string.IsNullOrWhiteSpace(r.Adapter.FallbackReason));
        var summary = JsonDocument.Parse(RunSummary.ToJson(r)).RootElement;
        Assert.Equal("warp", summary.GetProperty("adapter").GetProperty("kind").GetString());
        Assert.Equal(r.Adapter.FallbackReason, summary.GetProperty("adapter").GetProperty("fallbackReason").GetString());
    }

    [D3dFact]
    public void Forcing_hardware_when_there_is_none_fails_with_an_environment_error_and_releases_everything()
    {
        var options = new RunOptions(new WorkloadOptions(1, 144, 90, 0, 25, 0), "unused.csv", AdapterRequest.Hardware, false) { SimulateNoHardware = true };
        var windowsBefore = RenderWindow.LiveInstances;
        var presentersBefore = D3D11Presenter.LiveInstances;

        var e = Assert.Throws<HarnessEnvironmentException>(() => HarnessRunner.Execute(options));

        Assert.Contains("hardware", e.Message);
        Assert.Equal(windowsBefore, RenderWindow.LiveInstances);
        Assert.Equal(presentersBefore, D3D11Presenter.LiveInstances);
    }

    [D3dFact]
    public void A_finished_run_leaves_no_window_device_or_swap_chain_behind()
    {
        var windowsBefore = RenderWindow.LiveInstances;
        var presentersBefore = D3D11Presenter.LiveInstances;

        HarnessRunner.Execute(D3dRunFixture.Options(AdapterRequest.Warp, frames: 90, hitches: 0));
        HarnessRunner.Execute(D3dRunFixture.Options(AdapterRequest.Warp, frames: 90, hitches: 0));

        Assert.Equal(windowsBefore, RenderWindow.LiveInstances);
        Assert.Equal(presentersBefore, D3D11Presenter.LiveInstances);
    }

    [D3dFact]
    public void Cancelling_a_run_midway_releases_the_window_device_and_swap_chain()
    {
        var windowsBefore = RenderWindow.LiveInstances;
        var presentersBefore = D3D11Presenter.LiveInstances;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        Assert.ThrowsAny<OperationCanceledException>(() => HarnessRunner.Execute(D3dRunFixture.Options(AdapterRequest.Warp, frames: 2400, hitches: 4), cancellation.Token));

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(8), "a 10 second run must stop shortly after the cancel, not run to the end");
        Assert.Equal(windowsBefore, RenderWindow.LiveInstances);
        Assert.Equal(presentersBefore, D3D11Presenter.LiveInstances);
    }

    [D3dFact]
    public void A_visible_window_run_completes_pumps_messages_and_still_cleans_up()
    {
        var windowsBefore = RenderWindow.LiveInstances;

        var r = HarnessRunner.Execute(D3dRunFixture.Options(AdapterRequest.Warp, frames: 120, hitches: 1, visible: true));

        Assert.True(r.WindowVisible);
        Assert.Equal(120, r.Records.Count);
        Assert.True(r.Fidelity.PresentTimeError.MedianMs <= MedianErrorLimitMs, $"median {r.Fidelity.PresentTimeError.MedianMs:F3} ms");
        Assert.Equal(windowsBefore, RenderWindow.LiveInstances);
    }

    [D3dFact]
    public void The_csv_of_a_real_run_has_the_PresentMon_schema_one_row_per_frame_and_only_NA_for_what_it_cannot_know()
    {
        var r = fixture.Warp;
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        PresentLogCsv.Write(writer, PresentLogContext.For(r, "jankscope-harness.exe", Environment.ProcessId), r.Records);
        var lines = writer.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        var fixtureHeader = File.ReadLines(Path.Combine(AppContext.BaseDirectory, "fixtures", "presentmon", "v2.6.0-real-sanitized.csv")).First().Split(',');
        var header = lines[0].Split(',');
        Assert.Equal(fixtureHeader, header.Take(28).ToArray());
        Assert.Equal(1 + r.Records.Count, lines.Length);

        var column = header.Select((n, i) => (n, i)).ToDictionary(x => x.n, x => x.i);
        var rows = lines.Skip(1).Select(l => l.Split(',')).ToList();
        Assert.All(rows, row =>
        {
            Assert.Equal(header.Length, row.Length);
            Assert.Equal("NA", row[column["MsBetweenDisplayChange"]]);
            Assert.Equal("NA", row[column["MsGPUTime"]]);
            Assert.Equal("NA", row[column["PresentMode"]]);
            Assert.Equal("Simulated", row[column["JankscopeProvenance"]]);
        });

        // The log's own frame times, read back from the CSV the way a parser would, still show the three hitches.
        var between = rows.Select(row => double.Parse(row[column["MsBetweenPresents"]], CultureInfo.InvariantCulture)).ToList();
        Assert.Equal(3, between.Count(ms => ms > 20.0));
    }

    [D3dFact]
    public void Everything_the_harness_writes_is_tagged_Simulated_and_nothing_claims_to_be_a_measurement()
    {
        var dir = Path.Combine(Path.GetTempPath(), "jankscope-harness-e2e-" + Guid.NewGuid().ToString("N"));
        try
        {
            var csv = Path.Combine(dir, "run.csv");
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exit = HarnessCli.Run(["run", "--seed", "4", "--fps", "240", "--frames", "150", "--hitches", "1", "--adapter", "warp", "--out", csv], output, error);

            Assert.Equal(0, exit);
            Assert.Equal(string.Empty, error.ToString());
            var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "run.summary.json"))).RootElement;
            Assert.Equal("Simulated", summary.GetProperty("provenance").GetString());
            Assert.False(summary.GetProperty("exportableAsMeasurement").GetBoolean());

            var all = File.ReadAllText(csv) + File.ReadAllText(Path.Combine(dir, "run.summary.json")) + output;
            Assert.DoesNotContain("\"Measured\"", all);
            Assert.DoesNotContain("\"Inferred\"", all);
            Assert.All(File.ReadLines(csv).Skip(1), line => Assert.Equal("Simulated", line.Split(',')[28])); // the first harness column, after the 28 PresentMon ones
            Assert.Contains("SIMULATED", output.ToString());
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [D3dFact]
    public void The_run_reports_whether_it_was_elevated_and_needs_no_elevation()
    {
        // The test host is the harness's host: if this passes, the harness ran in whatever (normally standard-user) token the host has.
        using var identity = WindowsIdentity.GetCurrent();
        var elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);

        Assert.Equal(elevated, fixture.Warp.Elevated);
        log.WriteLine("test host elevated: " + elevated);
    }

    [D3dFact]
    public void The_harness_reports_its_own_cpu_cost_against_the_wall_clock()
    {
        var r = fixture.Warp;
        Report("warp", r);

        Assert.True(r.Cpu.CpuMs > 0);
        Assert.True(r.Cpu.WallMs >= r.Schedule.TargetMs[^1] - 1.0, "the loop cannot finish before its last scheduled frame");
        Assert.True(r.Cpu.WallMs <= r.Schedule.TargetMs[^1] + 250, "the loop must end shortly after its last scheduled frame");
        Assert.InRange(r.Cpu.PercentOfOneCore, 0.1, 100.0 * Environment.ProcessorCount);
    }

    [D3dFact]
    public void The_summary_of_a_real_run_names_the_clock_the_swap_chain_and_the_timestamp_point()
    {
        var summary = JsonDocument.Parse(RunSummary.ToJson(fixture.Warp)).RootElement;

        Assert.Equal(System.Diagnostics.Stopwatch.Frequency, summary.GetProperty("timing").GetProperty("qpcFrequencyHz").GetInt64());
        Assert.Equal("FlipDiscard", summary.GetProperty("swapChain").GetProperty("swapEffect").GetString());
        Assert.Equal(0, summary.GetProperty("swapChain").GetProperty("syncInterval").GetInt32());
        Assert.True(summary.GetProperty("swapChain").GetProperty("bufferCount").GetInt32() >= 2); // flip model needs at least two buffers
        Assert.Equal("Present() return", summary.GetProperty("timing").GetProperty("timestampPoint").GetString());
    }
}
