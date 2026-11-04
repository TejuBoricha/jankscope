namespace Jankscope.Harness.Tests;

public class HarnessArgumentsTests
{
    [Fact]
    public void Run_defaults_match_the_plan_defaults_and_the_adapter_is_auto()
    {
        var parsed = HarnessArguments.Parse(["run", "--out", "x.csv"]);

        Assert.Null(parsed.Error);
        Assert.Equal(HarnessCommandKind.Run, parsed.Kind);
        var run = parsed.Run!;
        Assert.Equal(new WorkloadOptions(1, 144, 3000, 8, 25, 0.2), run.Workload);
        Assert.Equal("x.csv", run.OutPath);
        Assert.Equal(AdapterRequest.Auto, run.Adapter);
        Assert.False(run.WindowVisible);
    }

    [Fact]
    public void Every_run_option_is_read()
    {
        var parsed = HarnessArguments.Parse(
            ["run", "--seed", "42", "--fps", "239.76", "--frames", "900", "--hitches", "4", "--extra-ms", "33.5", "--jitter-ms", "0.35", "--out", "a b\\c.csv", "--adapter", "warp", "--window-visible"]);

        Assert.Null(parsed.Error);
        var run = parsed.Run!;
        Assert.Equal(new WorkloadOptions(42, 239.76, 900, 4, 33.5, 0.35), run.Workload);
        Assert.Equal("a b\\c.csv", run.OutPath);
        Assert.Equal(AdapterRequest.Warp, run.Adapter);
        Assert.True(run.WindowVisible);
    }

    [Theory]
    [InlineData("auto", "Auto")]
    [InlineData("hardware", "Hardware")]
    [InlineData("warp", "Warp")]
    [InlineData("WARP", "Warp")]
    public void Adapter_choices(string text, string expected) =>
        Assert.Equal(expected, HarnessArguments.Parse(["run", "--out", "x", "--adapter", text]).Run!.Adapter.ToString());

    [Fact]
    public void Plan_takes_the_workload_options_and_an_optional_out_but_no_render_options()
    {
        var parsed = HarnessArguments.Parse(["plan", "--seed", "3", "--out", "plan.json"]);

        Assert.Null(parsed.Error);
        Assert.Equal(HarnessCommandKind.Plan, parsed.Kind);
        Assert.Equal(3, parsed.Plan!.Workload.Seed);
        Assert.Equal("plan.json", parsed.Plan.OutPath);
        Assert.Null(HarnessArguments.Parse(["plan"]).Plan!.OutPath);
        Assert.NotNull(HarnessArguments.Parse(["plan", "--adapter", "warp"]).Error);
        Assert.NotNull(HarnessArguments.Parse(["plan", "--window-visible"]).Error);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("help")]
    [InlineData("/?")]
    public void Help_is_recognised(string flag) =>
        Assert.Equal(HarnessCommandKind.Help, HarnessArguments.Parse([flag]).Kind);

    [Fact]
    public void No_arguments_is_a_usage_error_not_help_success() =>
        Assert.NotNull(HarnessArguments.Parse([]).Error);

    [Theory]
    [InlineData("run", "--out")]                              // missing value
    [InlineData("run", "--fps", "abc", "--out", "x")]         // not a number
    [InlineData("run", "--fps", "NaN", "--out", "x")]         // not finite
    [InlineData("run", "--seed", "1.5", "--out", "x")]        // not an integer
    [InlineData("run", "--frames", "ten", "--out", "x")]
    [InlineData("run", "--bogus", "1", "--out", "x")]         // unknown flag
    [InlineData("run", "stray", "--out", "x")]                // positional
    [InlineData("run", "--seed", "1", "--seed", "2", "--out", "x")] // duplicate
    [InlineData("run", "--adapter", "gpu", "--out", "x")]     // unknown adapter
    [InlineData("run", "--fps", "60")]                         // run needs --out
    [InlineData("explode")]                                    // unknown command
    public void Bad_input_is_reported_with_a_reason_and_never_silently_defaulted(params string[] args)
    {
        var parsed = HarnessArguments.Parse(args);

        Assert.False(string.IsNullOrWhiteSpace(parsed.Error));
        Assert.Null(parsed.Run);
    }

    [Fact]
    public void Numbers_are_parsed_with_the_invariant_culture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            var parsed = HarnessArguments.Parse(["run", "--fps", "144.5", "--jitter-ms", "0.25", "--out", "x"]);

            Assert.Null(parsed.Error);
            Assert.Equal(144.5, parsed.Run!.Workload.Fps);
            Assert.Equal(0.25, parsed.Run.Workload.JitterMs);
            Assert.NotNull(HarnessArguments.Parse(["run", "--fps", "144,5", "--out", "x"]).Error);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Negative_values_are_read_as_values_and_left_to_the_workload_validation()
    {
        var parsed = HarnessArguments.Parse(["run", "--extra-ms", "-5", "--out", "x"]);

        Assert.Null(parsed.Error);
        Assert.Equal(-5, parsed.Run!.Workload.ExtraMs);
    }
}
