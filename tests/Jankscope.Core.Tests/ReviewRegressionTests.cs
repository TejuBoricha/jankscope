namespace Jankscope.Core.Tests;

/// <summary>Regressions from the independent Stage 0 code review (docs/reviews/stage-0.md). Each test fails on the pre-fix code.</summary>
public class FrameStatsReviewTests
{
    [Fact]
    public void Rank_is_exact_for_the_99_9_percentile_at_every_multiple_of_a_thousand()
    {
        // In double arithmetic 99.9 / 100 * 10000 = 9990.000000000002, whose ceiling is 9991 (wrong).
        for (var n = 1000; n <= 300_000; n += 1000)
        {
            Assert.Equal(n / 1000 * 999, FrameStats.NearestRank(n, 99.9));
        }
    }

    [Fact]
    public void Rank_matches_integer_arithmetic_for_the_99_and_99_9_percentiles_at_all_sizes()
    {
        for (var n = 1; n <= 20_000; n++)
        {
            Assert.Equal((99L * n + 99) / 100, FrameStats.NearestRank(n, 99.0));
            Assert.Equal((int)((999L * n + 999) / 1000), FrameStats.NearestRank(n, 99.9));
        }
    }

    [Fact]
    public void Exactly_ten_slow_frames_in_ten_thousand_do_not_set_the_point_one_percent_low()
    {
        var times = Enumerable.Repeat(10.0, 10_000).ToArray();
        for (var i = 0; i < 10; i++)
        {
            times[i * 997] = 50.0;
        }

        Assert.Equal(100.0, FrameStats.PointOnePercentLowFps(times));

        times[9_999] = 50.0; // an 11th slow frame: now rank 9990 is slow
        Assert.Equal(20.0, FrameStats.PointOnePercentLowFps(times));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0.0)]
    [InlineData(-3.0)]
    public void Invalid_frame_times_make_every_statistic_not_a_number(double bad)
    {
        double[] times = [10.0, bad, 12.0];
        Assert.True(double.IsNaN(FrameStats.AverageFps(times)));
        Assert.True(double.IsNaN(FrameStats.PercentileFrameTimeMs(times, 50)));
    }

    [Fact]
    public void A_nan_percentile_is_rejected_not_treated_as_the_minimum()
    {
        Assert.True(double.IsNaN(FrameStats.PercentileFrameTimeMs([1.0, 2.0, 3.0], double.NaN)));
    }
}

public class SyntheticWorkloadReviewTests
{
    [Fact]
    public void Generator_output_is_pinned_so_a_runtime_upgrade_cannot_change_every_seed()
    {
        // Golden values from the SplitMix64 implementation (System.Random's sequence is not guaranteed across .NET versions).
        var run = SyntheticWorkload.Generate(7, 144, 2000, 6, 25, 0.3);
        Assert.Equal([275, 536, 850, 1261, 1526, 1726], run.Hitches.Select(h => h.FrameIndex));
        Assert.Equal(6.878342293479207, run.FrameTimesMs[0], precision: 12);
        Assert.Equal(6.654517421161338, run.FrameTimesMs[1], precision: 12);
        Assert.Equal(6.697570755605442, run.FrameTimesMs[1999], precision: 12);

        Assert.Equal([125, 529], SyntheticWorkload.Generate(1, 60, 600, 2, 20).Hitches.Select(h => h.FrameIndex));
    }

    [Fact]
    public void Different_seeds_give_different_series()
    {
        var a = SyntheticWorkload.Generate(1, 144, 2000, 6, 25, 0.3);
        var b = SyntheticWorkload.Generate(2, 144, 2000, 6, 25, 0.3);
        Assert.NotEqual(a.FrameTimesMs, b.FrameTimesMs);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Non_positive_frame_counts_are_rejected(int frames)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SyntheticWorkload.Generate(1, 60, frames, 0, 10));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-1.0)]
    [InlineData(double.PositiveInfinity)]
    public void Hitch_extra_must_be_finite_and_not_negative(double extra)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SyntheticWorkload.Generate(1, 60, 600, 2, extra));
    }

    [Fact]
    public void Jitter_must_stay_below_the_base_frame_time_so_no_frame_is_zero_or_negative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SyntheticWorkload.Generate(1, 500, 600, 0, 0, jitterMs: 5.0));
        var ok = SyntheticWorkload.Generate(1, 500, 600, 0, 0, jitterMs: 1.9);
        Assert.All(ok.FrameTimesMs, t => Assert.True(t > 0));
    }
}

public class CapabilityReviewTests
{
    [Fact]
    public void An_available_entry_must_carry_a_value()
    {
        Assert.Throws<ArgumentException>(() =>
            new CapabilityEntry("x", "X", CapabilityTier.A, Availability.Available, "src", DataProvenance.Measured, null, null));
    }
}

public class CatalogReviewTests
{
    private const string Good = """
        { "schemaVersion": 1, "catalog": "anticheat",
          "entries": [ { "id": "demo", "displayName": "Demo", "serviceNames": ["DemoSvc"], "verification": "community",
                         "source": "https://example.invalid/", "lastReviewed": "2026-10-16" } ] }
        """;

    [Theory]
    [InlineData("1.0")]
    [InlineData("1.5")]
    [InlineData("1e0")]
    [InlineData("99999999999")]
    public void Non_integer_schema_versions_are_issues_not_exceptions(string version)
    {
        var bad = Good.Replace("\"schemaVersion\": 1", $"\"schemaVersion\": {version}");
        var issues = CatalogValidator.Validate(bad);
        Assert.Contains(issues, i => i.Message.Contains("schemaVersion"));
    }

    [Fact]
    public void An_unpaired_surrogate_is_an_issue_not_an_exception()
    {
        Assert.NotEmpty(CatalogValidator.Validate("{ \"x\": \"\\ud800\" }".Replace("\\ud800", "\ud800")));
    }

    [Theory]
    [InlineData("\"id\": \"demo\"", "\"id\": \"demo\\n\"")]
    [InlineData("\"DemoSvc\"", "\"DemoSvc\\n\"")]
    public void A_trailing_newline_does_not_satisfy_the_id_or_name_patterns(string find, string replace)
    {
        Assert.NotEmpty(CatalogValidator.Validate(Good.Replace(find, replace)));
    }

    [Fact]
    public void Empty_entries_are_rejected_because_they_would_silently_report_none_found()
    {
        var empty = """{ "schemaVersion": 1, "catalog": "anticheat", "entries": [] }""";
        Assert.Contains(CatalogValidator.Validate(empty), i => i.Message.Contains("must not be empty"));
    }

    [Fact]
    public void A_file_must_declare_the_kind_it_is_loaded_as()
    {
        var wrongKind = """
            { "schemaVersion": 1, "catalog": "vendor_terms",
              "entries": [ { "id": "v", "displayName": "V", "terms": ["abc"], "verification": "documented", "source": "s", "lastReviewed": "2026-10-16" } ] }
            """;
        Assert.Empty(CatalogValidator.Validate(wrongKind));
        Assert.Contains(CatalogValidator.Validate(wrongKind, expectedKind: "anticheat"), i => i.Message.Contains("must declare the 'anticheat'"));
    }

    [Theory]
    [InlineData("\"displayName\": \"Demo\"", "\"displayName\": \"Demo\\u001b[31m\"")]
    [InlineData("\"source\": \"https://example.invalid/\"", "\"source\": \"x\\u0007\"")]
    public void Control_characters_in_free_text_are_rejected(string find, string replace)
    {
        Assert.Contains(CatalogValidator.Validate(Good.Replace(find, replace)), i => i.Message.Contains("control characters"));
    }

    [Fact]
    public void Free_text_is_length_limited_and_notes_must_be_strings()
    {
        var longName = Good.Replace("\"displayName\": \"Demo\"", $"\"displayName\": \"{new string('a', 200)}\"");
        Assert.Contains(CatalogValidator.Validate(longName), i => i.Message.Contains("longer than"));

        var objectNotes = Good.Replace("\"verification\"", "\"notes\": {\"a\": {\"b\": 1}}, \"verification\"");
        Assert.Contains(CatalogValidator.Validate(objectNotes), i => i.Message.Contains("'notes' must be a string"));
    }

    [Fact]
    public void Loader_turns_every_failure_into_invalid_data_exception()
    {
        var dir = Directory.CreateTempSubdirectory("jankscope-catalog-review");
        try
        {
            // schemaVersion 1.5 used to escape as FormatException; a wrong-kind file used to load with zero names.
            File.WriteAllText(Path.Combine(dir.FullName, "anticheat.json"), Good.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 1.5"));
            File.WriteAllText(Path.Combine(dir.FullName, "protected_titles.json"), "{}");
            Assert.Throws<InvalidDataException>(() => CatalogSet.Load(dir.FullName));

            File.WriteAllText(Path.Combine(dir.FullName, "anticheat.json"), File.ReadAllText(Path.Combine(Repo.Catalogs, "vendor_terms.json")));
            File.WriteAllText(Path.Combine(dir.FullName, "protected_titles.json"), File.ReadAllText(Path.Combine(Repo.Catalogs, "protected_titles.json")));
            Assert.Throws<InvalidDataException>(() => CatalogSet.Load(dir.FullName));

            File.Delete(Path.Combine(dir.FullName, "anticheat.json"));
            Assert.Throws<InvalidDataException>(() => CatalogSet.Load(dir.FullName));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
