using System.Text.Json;
using Jankscope.Core;

namespace Jankscope.Harness.Tests;

public class HarnessCliTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "jankscope-harness-cli-" + Guid.NewGuid().ToString("N"));

    public HarnessCliTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static (int Exit, string Out, string Err) Invoke(string[] args, Func<RunOptions, CancellationToken, HarnessRunResult>? runner = null)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exit = HarnessCli.Run(args, output, error, runner);
        return (exit, output.ToString(), error.ToString());
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("help")]
    public void Help_documents_every_option_the_simulation_label_and_the_exit_codes(string arg)
    {
        var (exit, output, error) = Invoke([arg]);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, error);
        foreach (var token in new[]
        {
            "plan", "run", "--seed", "--fps", "--frames", "--hitches", "--extra-ms", "--jitter-ms", "--out", "--adapter", "auto|hardware|warp",
            "--window-visible", "Simulated", "WARP", "pipeline only", "not a measurement", "unelevated", "Exit codes",
        })
        {
            Assert.Contains(token, output);
        }
    }

    [Fact]
    public void No_arguments_prints_usage_to_stderr_and_exits_with_2()
    {
        var (exit, output, error) = Invoke([]);

        Assert.Equal(2, exit);
        Assert.Equal(string.Empty, output);
        Assert.Contains("Usage", error);
    }

    [Fact]
    public void A_bad_option_exits_with_2_names_the_problem_and_runs_nothing()
    {
        var ran = false;
        var (exit, _, error) = Invoke(["run", "--fps", "fast", "--out", Path.Combine(_dir, "x.csv")], (_, _) => { ran = true; return TestRuns.Fake(); });

        Assert.Equal(2, exit);
        Assert.Contains("--fps", error);
        Assert.False(ran);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public void An_impossible_workload_is_reported_as_a_usage_error()
    {
        var (exit, _, error) = Invoke(["run", "--frames", "100", "--hitches", "50", "--out", Path.Combine(_dir, "x.csv")], (_, _) => TestRuns.Fake());

        Assert.Equal(2, exit);
        Assert.Contains("hitch", error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public void The_summary_path_may_not_collide_with_the_csv_path()
    {
        var (exit, _, error) = Invoke(["run", "--out", Path.Combine(_dir, "x.summary.json")], (_, _) => TestRuns.Fake());

        Assert.Equal(2, exit);
        Assert.Contains("summary", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Plan_prints_the_ground_truth_json_unchanged()
    {
        var (exit, output, _) = Invoke(["plan", "--seed", "5", "--fps", "120", "--frames", "400", "--hitches", "3", "--extra-ms", "30", "--jitter-ms", "0.1"]);

        Assert.Equal(0, exit);
        Assert.Equal(SyntheticWorkload.Generate(5, 120, 400, 3, 30, 0.1).ToJson(), output.TrimEnd());
        Assert.Equal("Simulated", JsonDocument.Parse(output).RootElement.GetProperty("provenance").GetString());
    }

    [Fact]
    public void Plan_with_out_writes_the_file_and_reports_it_as_simulated()
    {
        var path = Path.Combine(_dir, "plan.json");

        var (exit, output, _) = Invoke(["plan", "--seed", "5", "--out", path]);

        Assert.Equal(0, exit);
        Assert.Equal(SyntheticWorkload.Generate(5, 144, 3000, 8, 25, 0.2).ToJson(), File.ReadAllText(path));
        Assert.Contains("Simulated", output);
    }

    [Fact]
    public void Run_writes_the_csv_and_the_summary_and_prints_a_simulated_label()
    {
        var csv = Path.Combine(_dir, "sub", "run.csv");
        var seen = (RunOptions?)null;

        var (exit, output, error) = Invoke(["run", "--seed", "8", "--adapter", "warp", "--out", csv], (o, _) => { seen = o; return TestRuns.Fake(AdapterKind.Warp, seed: 8); });

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, error);
        Assert.Equal(AdapterRequest.Warp, seen!.Adapter);
        Assert.True(File.Exists(csv));
        var summaryPath = Path.Combine(_dir, "sub", "run.summary.json");
        Assert.True(File.Exists(summaryPath));
        Assert.Equal("Simulated", JsonDocument.Parse(File.ReadAllText(summaryPath)).RootElement.GetProperty("provenance").GetString());
        Assert.StartsWith("Application,ProcessID,", File.ReadAllText(csv));
        Assert.Contains("SIMULATED", output);
        Assert.Contains("pipeline only", output); // the WARP run says so on the console too
        Assert.Contains("run.csv", output);
        Assert.Contains("run.summary.json", output);
    }

    [Fact]
    public void A_hardware_run_prints_the_simulation_label_without_the_WARP_caveat()
    {
        var csv = Path.Combine(_dir, "hw.csv");

        var (exit, output, _) = Invoke(["run", "--out", csv], (_, _) => TestRuns.Fake(AdapterKind.Hardware));

        Assert.Equal(0, exit);
        Assert.Contains("SIMULATED", output);
        Assert.DoesNotContain("pipeline only", output);
    }

    [Fact]
    public void Missing_graphics_support_exits_with_3_writes_nothing_and_says_why()
    {
        var csv = Path.Combine(_dir, "none.csv");

        var (exit, _, error) = Invoke(["run", "--adapter", "hardware", "--out", csv], (_, _) => throw new HarnessEnvironmentException("no hardware Direct3D 11 adapter could be created"));

        Assert.Equal(3, exit);
        Assert.Contains("no hardware Direct3D 11 adapter", error);
        Assert.False(File.Exists(csv));
        Assert.False(File.Exists(Path.Combine(_dir, "none.summary.json")));
    }

    [Fact]
    public void Ctrl_C_exits_with_130_passes_the_token_to_the_run_and_writes_nothing()
    {
        var csv = Path.Combine(_dir, "stopped.csv");
        using var cancellation = new CancellationTokenSource();
        var seen = default(CancellationToken);

        using var output = new StringWriter();
        using var error = new StringWriter();
        var exit = HarnessCli.Run(["run", "--out", csv], output, error, (_, token) =>
        {
            seen = token;
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return TestRuns.Fake();
        }, cancellation.Token);

        Assert.Equal(130, exit);
        Assert.Equal(cancellation.Token, seen);
        Assert.Contains("cancelled", error.ToString());
        Assert.False(File.Exists(csv));
        Assert.False(File.Exists(Path.Combine(_dir, "stopped.summary.json")));
    }

    [Fact]
    public void An_unexpected_failure_exits_with_1_and_does_not_leave_partial_files()
    {
        var csv = Path.Combine(_dir, "boom.csv");

        var (exit, _, error) = Invoke(["run", "--out", csv], (_, _) => throw new InvalidOperationException("boom"));

        Assert.Equal(1, exit);
        Assert.Contains("boom", error);
        Assert.False(File.Exists(csv));
    }
}
