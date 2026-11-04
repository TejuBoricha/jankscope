using System.Text;
using Jankscope.Frames.PresentMon;

namespace Jankscope.Frames.Tests.PresentMon;

/// <summary>"Never throw on malformed input": truncation at every position, seeded fuzzing, and hostile shapes.</summary>
public class CsvParserRobustnessTests
{
    private static string FirstRealRow() => CsvTestData.ReadFixture("v2.6.0-real-sanitized.csv").Split('\n')[1];

    [Fact]
    public void A_row_cut_off_anywhere_before_its_last_unread_column_is_rejected()
    {
        // PresentMon killed mid-write leaves a partial last line. Every cut that loses a column the parser reads (or any column
        // before the last one) changes the field count or the quoting and must be rejected; a half number can never be accepted.
        var row = FirstRealRow();
        var lastFieldStart = row.LastIndexOf(',') + 1; // the 28th column (MsClickToPhotonLatency) is not read, so a cut inside it is still a complete reading
        var parser = CsvTestData.ParserFor(CsvTestData.RealHeader);
        var accepted = new List<int>();

        for (var cut = 1; cut < lastFieldStart; cut++)
        {
            if (parser.TryParseRow(row[..cut], out _, out _))
            {
                accepted.Add(cut);
            }
        }

        Assert.Empty(accepted);
    }

    [Fact]
    public void A_cut_inside_the_unread_last_column_still_yields_the_complete_values_that_were_read()
    {
        // Documents the one cut that cannot be told apart from a whole row: every column the parser reads is intact.
        var row = FirstRealRow();
        var parser = CsvTestData.ParserFor(CsvTestData.RealHeader);

        var full = CsvTestData.Ok(parser, row);
        var cut = CsvTestData.Ok(parser, row[..^1]);

        Assert.Equal(full with { Index = cut.Index }, cut);
    }

    [Fact]
    public void A_row_cut_off_inside_a_quoted_field_is_rejected()
    {
        var parser = CsvTestData.ParserFor("Application,TimeInMs,MsBetweenPresents");
        var row = "\"Some, Game.exe\",10.0,5.0";
        var lastComma = row.LastIndexOf(',');

        // Up to and including the cut right after the last comma (empty last field) nothing may be accepted. A cut deeper inside the last
        // field leaves a shorter number ("5" for "5.0") that is indistinguishable from a real one: see the next test.
        for (var cut = 1; cut <= lastComma + 1; cut++)
        {
            if (parser.TryParseRow(row[..cut], out var sample, out _))
            {
                Assert.Fail($"accepted a truncated row at {cut}: {row[..cut]} -> {sample}");
            }
        }
    }

    [Fact]
    public void A_cut_inside_the_last_column_is_only_caught_when_the_column_is_not_a_number_or_is_empty()
    {
        // Known limit of a line-oriented parser, written down as a test so nobody is surprised: when the LAST column of the file is one
        // that the parser reads, a cut that leaves a shorter valid number cannot be detected. PresentMon's own column order ends with
        // columns the parser does not read, so a real capture cannot be affected; a runner must also never pass on an unterminated fragment.
        var parser = CsvTestData.ParserFor("TimeInMs,MsBetweenPresents");

        Assert.Equal(6.0, CsvTestData.Ok(parser, "10.0,6.").FrameTimeMs); // "6.4395" cut after the decimal point: accepted as 6
        CsvTestData.Bad(parser, "10.0,"); // cut right after the comma: empty
        CsvTestData.Bad(parser, "10.0,6.4e"); // cut inside an exponent: not a number
    }

    [Fact]
    public void Seeded_garbage_never_throws_and_never_yields_a_non_finite_or_negative_value()
    {
        var parser = CsvTestData.ParserFor(CsvTestData.FullHeader);
        var random = new Random(20261017);
        const string alphabet = ",,,\"\"\r\n\t abcXYZ0123456789....--++eE:<>NAnaInfty\\/";

        for (var i = 0; i < 50_000; i++)
        {
            var length = random.Next(0, 120);
            var chars = new char[length];
            for (var c = 0; c < length; c++)
            {
                chars[c] = alphabet[random.Next(alphabet.Length)];
            }

            if (parser.TryParseRow(new string(chars), out var sample, out var problem))
            {
                AssertSane(sample);
                Assert.Null(problem);
            }
            else
            {
                Assert.False(string.IsNullOrWhiteSpace(problem));
                Assert.Equal(default, sample);
            }
        }
    }

    [Fact]
    public void Mutated_real_rows_never_throw_and_every_accepted_row_is_sane()
    {
        var baseRow = FirstRealRow().TrimEnd('\r');
        var parser = CsvTestData.ParserFor(CsvTestData.RealHeader);
        var random = new Random(424242);
        const string junk = ",\"x9.-eNA \t\0\r\n";
        var rejected = 0;
        var accepted = 0;

        for (var i = 0; i < 30_000; i++)
        {
            var sb = new StringBuilder(baseRow);
            for (var edits = random.Next(1, 4); edits > 0; edits--)
            {
                var at = random.Next(0, sb.Length + 1);
                switch (random.Next(3))
                {
                    case 0 when sb.Length > 0 && at < sb.Length:
                        sb.Remove(at, 1);
                        break;
                    case 1:
                        sb.Insert(at, junk[random.Next(junk.Length)]);
                        break;
                    default:
                        if (at < sb.Length)
                        {
                            sb[at] = junk[random.Next(junk.Length)];
                        }

                        break;
                }
            }

            if (parser.TryParseRow(sb.ToString(), out var sample, out var problem))
            {
                accepted++;
                AssertSane(sample);
            }
            else
            {
                rejected++;
                Assert.False(string.IsNullOrWhiteSpace(problem));
            }
        }

        Assert.True(rejected > 0, "the mutations should break at least some rows");
        Assert.True(accepted > 0, "some mutations land in unread columns and leave the row valid");
    }

    [Fact]
    public void Hostile_shapes_are_rejected_not_thrown_on()
    {
        var parser = CsvTestData.ParserFor(CsvTestData.FullHeader);
        string[] lines =
        [
            new string(',', 10_000),
            new string('"', 10_001),
            "\"" + new string('"', 4_000) + "\"",
            string.Concat(Enumerable.Repeat("\"\",", 5_000)),
            new string((char)0xD800, 50),                     // lone surrogates
            new string([(char)1, (char)2, (char)3]),
            new string([(char)0xFFFF, ',', (char)0xFFFE, ',', (char)0xFFFD]),
        ];

        foreach (var line in lines)
        {
            var accepted = parser.TryParseRow(line, out var sample, out var problem);
            if (accepted)
            {
                AssertSane(sample);
            }
            else
            {
                Assert.False(string.IsNullOrWhiteSpace(problem));
            }
        }
    }

    [Fact]
    public void Non_ascii_application_names_survive()
    {
        var parser = CsvTestData.ParserFor("Application,TimeInMs,MsBetweenPresents");
        var name = "Spiel-" + (char)0x00E4 + (char)0x00F6 + (char)0x00FC + "-" + (char)0x30B2 + (char)0x30FC + (char)0x30E0 + ".exe";

        var sample = CsvTestData.Ok(parser, name + ",10.0,5.0");

        Assert.Equal(name, sample.Application);
    }

    private static void AssertSane(FrameSample sample)
    {
        Assert.True(double.IsFinite(sample.TimestampMs) && sample.TimestampMs >= 0, $"timestamp {sample.TimestampMs}");
        Assert.True(double.IsFinite(sample.FrameTimeMs) && sample.FrameTimeMs > 0, $"frame time {sample.FrameTimeMs}");
        foreach (var optional in new[] { sample.DisplayedFrameTimeMs, sample.CpuBusyMs, sample.GpuBusyMs })
        {
            Assert.True(optional is null || (double.IsFinite(optional.Value) && optional.Value >= 0), $"optional metric {optional}");
        }

        Assert.True(sample.ProcessId is null or >= 0, $"process id {sample.ProcessId}");
    }
}
