using System.Text.Json;
using Jankscope.Core;

namespace Jankscope.Harness.Tests;

public class RunSummaryTests
{
    private static JsonElement Summary(HarnessRunResult result) => JsonDocument.Parse(RunSummary.ToJson(result)).RootElement;

    [Fact]
    public void The_summary_is_tagged_Simulated_and_says_it_cannot_be_exported_as_a_measurement()
    {
        var root = Summary(TestRuns.Fake());

        Assert.Equal("Simulated", root.GetProperty("provenance").GetString());
        Assert.False(root.GetProperty("exportableAsMeasurement").GetBoolean());
        Assert.Contains("Simulated", root.GetProperty("notice").GetString());
        Assert.Contains("not a measurement", root.GetProperty("notice").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_result_type_carries_Simulated_and_the_provenance_guard_refuses_it()
    {
        var result = TestRuns.Fake();

        Assert.Equal(DataProvenance.Simulated, result.Provenance);
        Assert.False(result.Provenance.MayBeExportedAsMeasurement());
        Assert.Throws<InvalidOperationException>(() => result.Provenance.RequireExportableAsMeasurement());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void No_text_in_the_summary_names_a_measured_or_inferred_provenance(bool warp)
    {
        var json = RunSummary.ToJson(TestRuns.Fake(warp ? AdapterKind.Warp : AdapterKind.Hardware));

        Assert.DoesNotContain("\"Measured\"", json);
        Assert.DoesNotContain("\"Inferred\"", json);
        Assert.DoesNotContain("\"exportableAsMeasurement\": true", json);
    }

    [Fact]
    public void A_WARP_run_states_that_it_proves_the_pipeline_only()
    {
        var root = Summary(TestRuns.Fake(AdapterKind.Warp));
        var adapter = root.GetProperty("adapter");

        Assert.Equal("warp", adapter.GetProperty("kind").GetString());
        Assert.True(adapter.GetProperty("provesPipelineOnly").GetBoolean());
        Assert.Contains("pipeline only", adapter.GetProperty("note").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not GPU timing", adapter.GetProperty("note").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WARP", root.GetProperty("notice").GetString());
    }

    [Fact]
    public void A_hardware_run_is_still_simulated_workload_and_does_not_claim_to_prove_the_pipeline_only()
    {
        var root = Summary(TestRuns.Fake(AdapterKind.Hardware));
        var adapter = root.GetProperty("adapter");

        Assert.Equal("hardware", adapter.GetProperty("kind").GetString());
        Assert.False(adapter.GetProperty("provesPipelineOnly").GetBoolean());
        Assert.Equal("Simulated", root.GetProperty("provenance").GetString());
        Assert.DoesNotContain("WARP", root.GetProperty("notice").GetString());
    }

    [Fact]
    public void The_summary_lists_the_run_parameters_and_the_injected_hitches_as_ground_truth()
    {
        var result = TestRuns.Fake(seed: 9, fps: 120, frames: 300, hitches: 3, extra: 30, jitter: 0.25);
        var root = Summary(result);

        var run = root.GetProperty("run");
        Assert.Equal(9, run.GetProperty("seed").GetInt32());
        Assert.Equal(120, run.GetProperty("targetFps").GetDouble());
        Assert.Equal(300, run.GetProperty("frames").GetInt32());
        Assert.Equal(3, run.GetProperty("hitches").GetInt32());
        Assert.Equal(30, run.GetProperty("extraMs").GetDouble());
        Assert.Equal(0.25, run.GetProperty("jitterMs").GetDouble());

        var truth = root.GetProperty("groundTruth").GetProperty("hitches").EnumerateArray()
            .Select(h => (h.GetProperty("frameIndex").GetInt32(), h.GetProperty("extraMs").GetDouble())).ToList();
        Assert.Equal(result.Run.Hitches.Select(h => (h.FrameIndex, h.ExtraMs)).ToList(), truth);
    }

    [Fact]
    public void Timing_fields_name_the_clock_and_state_that_no_timer_resolution_change_was_made()
    {
        var result = TestRuns.Fake();
        var timing = Summary(result).GetProperty("timing");

        Assert.Equal(result.Frequency, timing.GetProperty("qpcFrequencyHz").GetInt64());
        Assert.Contains("QueryPerformanceCounter", timing.GetProperty("clock").GetString());
        Assert.Equal("none", timing.GetProperty("timerResolutionChange").GetString());
        Assert.Equal("Present() return", timing.GetProperty("timestampPoint").GetString());
        Assert.Equal(2.0, timing.GetProperty("sleepMarginMs").GetDouble());
    }

    [Fact]
    public void Fidelity_cpu_and_environment_blocks_are_present_and_numeric()
    {
        var root = Summary(TestRuns.Fake());
        var fidelity = root.GetProperty("fidelity");

        foreach (var block in new[] { "presentTimeErrorMs", "frameTimeErrorMs" })
        {
            Assert.True(fidelity.GetProperty(block).GetProperty("max").GetDouble() >= 0);
            Assert.True(fidelity.GetProperty(block).GetProperty("median").GetDouble() >= 0);
            Assert.True(fidelity.GetProperty(block).GetProperty("p99").GetDouble() >= 0);
        }

        Assert.Equal(2, fidelity.GetProperty("hitches").GetArrayLength());
        Assert.Equal(2.0, fidelity.GetProperty("disturbedThresholdMs").GetDouble());
        Assert.Equal(JsonValueKind.Array, fidelity.GetProperty("disturbedFrames").ValueKind);
        Assert.Equal(50, root.GetProperty("cpu").GetProperty("percentOfOneCore").GetDouble());
        Assert.False(root.GetProperty("environment").GetProperty("elevated").GetBoolean());
        Assert.Equal(3, root.GetProperty("swapChain").GetProperty("bufferCount").GetInt32());
        Assert.Equal("FlipDiscard", root.GetProperty("swapChain").GetProperty("swapEffect").GetString());
    }

    [Fact]
    public void The_summary_does_not_contain_the_machine_or_user_name()
    {
        var json = RunSummary.ToJson(TestRuns.Fake());

        Assert.DoesNotContain(Environment.MachineName, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.UserName, json, StringComparison.OrdinalIgnoreCase);
    }
}
