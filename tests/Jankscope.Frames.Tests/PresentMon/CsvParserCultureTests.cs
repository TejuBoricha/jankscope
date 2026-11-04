using Jankscope.Frames.PresentMon;

namespace Jankscope.Frames.Tests.PresentMon;

/// <summary>
/// PresentMon always writes "." as the decimal separator, whatever the Windows region says. The parser must read it the same way
/// on every machine (spec 6.4: version-tolerant parsers, no culture surprises).
/// </summary>
public class CsvParserCultureTests
{
    // de-DE and fr-FR use "," as the decimal separator, tr-TR changes the case of "i", sv-SE writes a Unicode minus sign, and
    // ar-SA and hi-IN have their own digits and grouping.
    public static TheoryData<string> Cultures => new() { "de-DE", "tr-TR", "fr-FR", "sv-SE", "ar-SA", "hi-IN", "ru-RU", "en-US" };

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Numbers_use_the_invariant_culture(string culture)
    {
        CsvTestData.WithCulture(culture, () =>
        {
            var parser = CsvTestData.ParserFor(CsvTestData.FullHeader);

            var sample = CsvTestData.Ok(parser, "1234.5678,6.4395,9.5,1.25,3.5,Mode,App.exe,4242");

            Assert.Equal(1234.5678, sample.TimestampMs);
            Assert.Equal(6.4395, sample.FrameTimeMs);
            Assert.Equal(9.5, sample.DisplayedFrameTimeMs);
            Assert.Equal(4242, sample.ProcessId);
        });
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void A_comma_decimal_is_not_silently_read_as_a_point_decimal(string culture)
    {
        // "6,43" can only reach the parser inside quotes (otherwise it is two fields). A culture-sensitive parse under de-DE
        // would turn it into 6.43 or 643; the invariant parse rejects it.
        CsvTestData.WithCulture(culture, () =>
        {
            var parser = CsvTestData.ParserFor("TimeInMs,MsBetweenPresents");

            var problem = CsvTestData.Bad(parser, "10.0,\"6,43\"");

            Assert.Contains("not a number", problem);
        });
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Column_names_match_whatever_the_culture_is(string culture)
    {
        CsvTestData.WithCulture(culture, () =>
        {
            var result = new PresentMonCsvParser().ReadHeader(CsvTestData.RealHeader.ToUpperInvariant());

            Assert.True(result.Supported, result.UnsupportedReason);
            Assert.Empty(result.MissingOptionalColumns);
        });
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Problem_text_does_not_depend_on_the_culture(string culture)
    {
        CsvTestData.WithCulture(culture, () =>
        {
            var parser = CsvTestData.ParserFor("TimeInMs,MsBetweenPresents");

            var problem = CsvTestData.Bad(parser, "1,2,3");

            Assert.Equal("expected 2 fields, found 3 (truncated or corrupt line?)", problem);
        });
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void The_real_fixture_parses_identically_under_every_culture(string culture)
    {
        var text = CsvTestData.ReadFixture("v2.6.0-real-sanitized.csv");
        IReadOnlyList<FrameSample>? baseline = null;
        CsvTestData.WithCulture("en-US", () => baseline = CsvTestData.ReadAllText(text, out _, out _));

        CsvTestData.WithCulture(culture, () =>
        {
            var samples = CsvTestData.ReadAllText(text, out var header, out var problems);

            Assert.True(header.Supported);
            Assert.Empty(problems);
            Assert.Equal(baseline, samples);
        });
    }
}
