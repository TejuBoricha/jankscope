using System.Globalization;
using Jankscope.Core;

namespace Jankscope.Harness;

/// <summary>Command-line front end. Exit codes: 0 success, 1 unexpected failure, 2 usage error, 3 the environment cannot run the harness, 130 cancelled with Ctrl+C.</summary>
internal static class HarnessCli
{
    public const int ExitOk = 0;
    public const int ExitFailure = 1;
    public const int ExitUsage = 2;
    public const int ExitEnvironment = 3;
    public const int ExitCancelled = 130; // 128 + SIGINT, the usual shell convention for Ctrl+C

    public const string HelpText = """
        jankscope-harness: a synthetic Direct3D 11 workload with known, injected hitches.
        It is the independent ground truth for stutter detection: a small renderer presents frames on a schedule and records its own
        QPC timestamps. Everything it writes is Simulated data: it is not a measurement of any game or of this PC, and the Jankscope
        product can never export it as one.

        Usage:
          jankscope-harness plan [workload options] [--out FILE]
          jankscope-harness run  [workload options] --out FILE [--adapter auto|hardware|warp] [--window-visible]
          jankscope-harness --help

        Workload options (defaults in brackets):
          --seed N         random seed; the same seed gives the same schedule and hitch positions [1]
          --fps F          target frame rate [144]
          --frames N       number of frames [3000]
          --hitches N      number of injected hitches (at least 30 frames apart, after a 60 frame warm-up) [8]
          --extra-ms M     extra milliseconds added to each hitch frame [25]
          --jitter-ms J    uniform frame-time jitter, plus or minus J ms [0.2]

        Modes:
          plan   prints the ground-truth schedule as JSON (or writes it with --out); renders nothing.
          run    opens a small window, creates a Direct3D 11 device and a flip-model swap chain (DXGI_SWAP_EFFECT_FLIP_DISCARD) and calls
                 Present(0) for every frame at its scheduled time. Each Present() return is timestamped with the harness's own QPC.
                 Frame i is due at the cumulative sum of the first i + 1 ground-truth frame times; the waits are absolute, so errors do
                 not accumulate.

        Options of run:
          --out FILE       the CSV: PresentMon 2.6.0 column names, NA for what the harness cannot truthfully provide, plus five
                           Jankscope columns (provenance Simulated, frame index, scheduled time, injected extra, raw QPC ticks).
                           The JSON summary is written next to it as <name>.summary.json.
          --adapter A      auto (default): the first hardware adapter, else the WARP software adapter; hardware: fail if there is none;
                           warp: always use the software adapter. A WARP run proves the pipeline only, not GPU timing, and says so.
          --window-visible show the small window (without taking focus). By default the window exists but is never shown, and a frame-capture
                           tool such as PresentMon sees none of its presents: use --window-visible whenever another program captures the run.

        Notes:
          The harness runs unelevated, starts no ETW session and touches no other program. It does not change the Windows timer
          resolution: it measures how long Sleep(1) really takes on this PC, sleeps while the target is farther away than that, and
          spins for the rest of each wait. On a coarse timer it therefore spins more (more CPU, same accuracy); the CPU cost is in the summary.

        Exit codes:
          0 success   1 unexpected failure   2 usage error   3 no usable Direct3D 11 adapter or window on this machine
          130 cancelled with Ctrl+C (the device, swap chain and window are released; no files are written)
        """;

    public static int Run(
        string[] args,
        TextWriter output,
        TextWriter error,
        Func<RunOptions, CancellationToken, HarnessRunResult>? runner = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        var parsed = HarnessArguments.Parse(args);
        switch (parsed.Kind)
        {
            case HarnessCommandKind.Help:
                output.WriteLine(HelpText);
                return ExitOk;
            case HarnessCommandKind.Plan:
                return Plan(parsed.Plan!, output, error);
            case HarnessCommandKind.Run:
                return Render(parsed.Run!, output, error, runner ?? HarnessRunner.Execute, cancellationToken);
            default:
                error.WriteLine("error: " + parsed.Error);
                error.WriteLine("Usage: jankscope-harness plan|run [options]; use --help for the options.");
                return ExitUsage;
        }
    }

    private static int Plan(PlanOptions options, TextWriter output, TextWriter error)
    {
        SyntheticRun run;
        try
        {
            run = options.Workload.Generate();
        }
        catch (ArgumentException e)
        {
            error.WriteLine("error: invalid workload: " + e.Message);
            return ExitUsage;
        }

        var json = run.ToJson();
        if (options.OutPath is null)
        {
            output.WriteLine(json);
            return ExitOk;
        }

        try
        {
            EnsureDirectory(options.OutPath);
            File.WriteAllText(options.OutPath, json);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            error.WriteLine("error: cannot write " + options.OutPath + ": " + e.Message);
            return ExitFailure;
        }

        output.WriteLine($"Wrote {run.FrameTimesMs.Count} frames and {run.Hitches.Count} injected hitches (Simulated) to {options.OutPath}");
        return ExitOk;
    }

    private static int Render(RunOptions options, TextWriter output, TextWriter error, Func<RunOptions, CancellationToken, HarnessRunResult> runner, CancellationToken cancellationToken)
    {
        try
        {
            options.Workload.Generate(); // reject an impossible workload as a usage error before any window is opened
        }
        catch (ArgumentException e)
        {
            error.WriteLine("error: invalid workload: " + e.Message);
            return ExitUsage;
        }

        HarnessRunResult result;
        try
        {
            result = runner(options, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            error.WriteLine("cancelled: the run was stopped before it finished; nothing was written.");
            return ExitCancelled;
        }
        catch (HarnessEnvironmentException e)
        {
            error.WriteLine("error: the harness cannot run here: " + e.Message);
            return ExitEnvironment;
        }
        catch (Exception e)
        {
            error.WriteLine("error: the harness failed: " + e.Message);
            return ExitFailure;
        }

        var summaryPath = HarnessArguments.SummaryPathFor(options.OutPath);
        try
        {
            EnsureDirectory(options.OutPath);
            var context = PresentLogContext.For(result, Path.GetFileName(Environment.ProcessPath) ?? RunSummary.ToolName + ".exe", Environment.ProcessId);
            PresentLogCsv.WriteFile(options.OutPath, context, result.Records);
            File.WriteAllText(summaryPath, RunSummary.ToJson(result));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            error.WriteLine("error: cannot write the output files: " + e.Message);
            return ExitFailure;
        }

        Report(result, options.OutPath, summaryPath, output);
        return ExitOk;
    }

    private static void Report(HarnessRunResult r, string csvPath, string summaryPath, TextWriter output)
    {
        var inv = CultureInfo.InvariantCulture;
        var f = r.Fidelity;
        output.WriteLine("jankscope-harness: SIMULATED workload (ground truth injected by this tool); not a measurement.");
        output.WriteLine($"adapter: {r.Adapter.Kind.ToString().ToLowerInvariant()} ({r.Adapter.Description}, feature level {r.Adapter.FeatureLevel})");
        if (r.Adapter.Kind == AdapterKind.Warp)
        {
            output.WriteLine("  " + RunSummary.WarpNote);
        }

        if (r.Adapter.FallbackReason is not null)
        {
            output.WriteLine("  fell back to WARP: " + r.Adapter.FallbackReason);
        }

        output.WriteLine(string.Create(inv, $"frames: {r.Records.Count} at {r.Workload.Fps} fps, {r.Run.Hitches.Count} injected hitches of {r.Workload.ExtraMs} ms, seed {r.Workload.Seed}"));
        output.WriteLine(string.Create(inv, $"Present() return vs schedule (absolute error): max {f.PresentTimeError.MaxMs:F3} ms, median {f.PresentTimeError.MedianMs:F3} ms, p99 {f.PresentTimeError.P99Ms:F3} ms"));
        output.WriteLine(string.Create(inv, $"frame time vs ground truth (absolute error):   max {f.FrameTimeError.MaxMs:F3} ms, median {f.FrameTimeError.MedianMs:F3} ms, p99 {f.FrameTimeError.P99Ms:F3} ms"));
        output.WriteLine(string.Create(inv, $"disturbed frames (interval off by more than {Fidelity.DisturbedThresholdMs:F0} ms): {f.DisturbedFrames.Count}; listed in the summary"));
        output.WriteLine(string.Create(inv, $"harness CPU: {r.Cpu.PercentOfOneCore:F0} % of one core (sleep margin {r.Pacer.MarginMs:F2} ms, {r.Pacer.LateArrivals} late arrivals)"));
        output.WriteLine("wrote " + csvPath);
        output.WriteLine("wrote " + summaryPath);
    }

    private static void EnsureDirectory(string filePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
