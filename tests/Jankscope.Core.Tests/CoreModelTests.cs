using System.Text.Json;

namespace Jankscope.Core.Tests;

public class CapabilityEntryTests
{
    [Fact]
    public void Unavailable_entry_must_state_a_reason()
    {
        Assert.Throws<ArgumentException>(() =>
            new CapabilityEntry("x", "X", CapabilityTier.A, Availability.NotAvailable, "src", DataProvenance.None, null, " "));
    }

    [Fact]
    public void Entry_must_name_its_source()
    {
        Assert.Throws<ArgumentException>(() =>
            CapabilityEntry.Available("x", "X", CapabilityTier.A, "", "value"));
    }

    [Fact]
    public void Planned_entry_is_marked_not_implemented_with_a_reason_and_no_value()
    {
        var e = CapabilityEntry.Planned("gpu.adapters", "GPU adapters", CapabilityTier.A, "Stage 1a");
        Assert.Equal(Availability.NotImplemented, e.Availability);
        Assert.Contains("Stage 1a", e.Reason);
        Assert.Null(e.Value);
        Assert.Equal(DataProvenance.None, e.Provenance);
    }

    [Fact]
    public void Matrix_serializes_enums_as_strings_and_omits_nulls()
    {
        var matrix = new CapabilityMatrix(DateTimeOffset.UnixEpoch, "1.0",
            [CapabilityEntry.Unavailable("a.b", "AB", CapabilityTier.C, Availability.NeedsElevation, "src", "needs the helper")],
            [new ProbeHealth("a", true, 1.5, null)]);
        var json = matrix.ToJson();
        using var doc = JsonDocument.Parse(json);
        var entry = doc.RootElement.GetProperty("entries")[0];
        Assert.Equal("NeedsElevation", entry.GetProperty("availability").GetString());
        Assert.Equal("C", entry.GetProperty("tier").GetString());
        Assert.False(entry.TryGetProperty("value", out _));
    }
}

public class ProvenanceTests
{
    [Theory]
    [InlineData(DataProvenance.Measured, true)]
    [InlineData(DataProvenance.Inferred, true)]
    [InlineData(DataProvenance.Assumed, false)]
    [InlineData(DataProvenance.Simulated, false)]
    [InlineData(DataProvenance.Fixture, false)]
    [InlineData(DataProvenance.None, false)]
    public void Only_measured_or_inferred_data_may_be_exported_as_measurement(DataProvenance p, bool allowed)
    {
        Assert.Equal(allowed, p.MayBeExportedAsMeasurement());
        if (!allowed)
        {
            Assert.Throws<InvalidOperationException>(() => p.RequireExportableAsMeasurement());
        }
    }
}

public class SyntheticWorkloadTests
{
    [Fact]
    public void Same_seed_gives_identical_runs()
    {
        var a = SyntheticWorkload.Generate(7, 144, 2000, 6, 25, 0.3);
        var b = SyntheticWorkload.Generate(7, 144, 2000, 6, 25, 0.3);
        Assert.Equal(a.FrameTimesMs, b.FrameTimesMs);
        Assert.Equal(a.Hitches, b.Hitches);
    }

    [Fact]
    public void Hitches_land_after_warm_up_are_spaced_and_carry_the_extra_time()
    {
        var run = SyntheticWorkload.Generate(3, 144, 3000, 10, 40);
        var baseMs = 1000.0 / 144;
        Assert.Equal(10, run.Hitches.Count);
        Assert.All(run.Hitches, h =>
        {
            Assert.True(h.FrameIndex >= SyntheticWorkload.WarmUpFrames);
            Assert.Equal(baseMs + 40, run.FrameTimesMs[h.FrameIndex], precision: 9);
        });
        var indexes = run.Hitches.Select(h => h.FrameIndex).Order().ToArray();
        for (var i = 1; i < indexes.Length; i++)
        {
            Assert.True(indexes[i] - indexes[i - 1] >= SyntheticWorkload.MinHitchSpacingFrames);
        }
    }

    [Fact]
    public void Non_hitch_frames_stay_at_the_base_time_without_jitter()
    {
        var run = SyntheticWorkload.Generate(3, 100, 500, 3, 30);
        var hitchIndexes = run.Hitches.Select(h => h.FrameIndex).ToHashSet();
        for (var i = 0; i < run.FrameTimesMs.Count; i++)
        {
            if (!hitchIndexes.Contains(i))
            {
                Assert.Equal(10.0, run.FrameTimesMs[i], precision: 9);
            }
        }
    }

    [Fact]
    public void Synthetic_data_is_tagged_simulated_and_refused_for_export()
    {
        var run = SyntheticWorkload.Generate(1, 60, 600, 2, 20);
        Assert.Equal(DataProvenance.Simulated, run.Provenance);
        Assert.Contains("Simulated", run.ToJson());
        Assert.Throws<InvalidOperationException>(() => run.Provenance.RequireExportableAsMeasurement());
    }

    [Fact]
    public void Too_many_hitches_for_the_run_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => SyntheticWorkload.Generate(1, 60, 200, 50, 20));
    }
}

public class EtwSessionClassifierTests
{
    [Fact]
    public void Only_sessions_with_our_prefix_are_classified_as_ours()
    {
        var names = new[] { "Jankscope-probe-1", "pm2_other", "Jankscope-capture-9", "NT Kernel Logger", "jankscope-lowercase" };
        var s = EtwSessionClassifier.Summarize(names, Branding.EtwSessionPrefix);
        Assert.Equal(5, s.Total);
        Assert.Equal(64, s.Limit);
        Assert.Equal(["Jankscope-capture-9", "Jankscope-probe-1"], s.OwnPrefixSessions);
        Assert.Equal(3, s.ForeignSessions);
        Assert.Equal(59, s.Headroom);
    }
}
