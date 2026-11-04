using System.Diagnostics;
using System.Text;
using Jankscope.Frames.PresentMon;
using Xunit.Abstractions;

namespace Jankscope.Frames.Tests.PresentMon;

/// <summary>ReadAll and the streaming reader: encodings, line endings, problem reporting, laziness and the 1 M-row memory/time bound.</summary>
public class CsvParserStreamTests(ITestOutputHelper output)
{
    private const string Header = "TimeInMs,MsBetweenPresents,Application";

    private static string Log(string lineBreak, params string[] lines) => string.Join(lineBreak, lines) + lineBreak;

    // ---- ReadAll ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Crlf_and_lf_give_identical_results()
    {
        var lines = new[] { Header, "10.0,5.0,A.exe", "15.0,5.0,A.exe", "20.0,5.5,B.exe" };

        var lf = CsvTestData.ReadAllText(Log("\n", lines), out var lfHeader, out var lfProblems);
        var crlf = CsvTestData.ReadAllText(Log("\r\n", lines), out var crlfHeader, out var crlfProblems);

        Assert.Equal(3, lf.Count);
        Assert.Equal(lf, crlf);
        Assert.Equal(lfHeader, crlfHeader, new LogHeaderComparer());
        Assert.Empty(lfProblems);
        Assert.Empty(crlfProblems);
    }

    [Fact]
    public void A_final_line_without_a_line_break_is_still_read()
    {
        var samples = CsvTestData.ReadAllText(Header + "\n10.0,5.0,A.exe\n15.0,5.0,A.exe", out _, out var problems);

        Assert.Equal(2, samples.Count);
        Assert.Empty(problems);
    }

    [Fact]
    public void The_real_fixture_reads_the_same_with_crlf_line_endings()
    {
        var lf = CsvTestData.ReadFixture("v2.6.0-real-sanitized.csv");
        var crlf = lf.Replace("\n", "\r\n", StringComparison.Ordinal);

        var a = CsvTestData.ReadAllText(lf, out _, out _);
        var b = CsvTestData.ReadAllText(crlf, out _, out var problems);

        Assert.Equal(857, b.Count);
        Assert.Equal(a, b);
        Assert.Empty(problems);
    }

    [Fact]
    public void A_utf8_byte_order_mark_is_handled_both_when_the_reader_strips_it_and_when_it_does_not()
    {
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(Log("\r\n", "Application,TimeInMs,MsBetweenPresents", "A.exe,10.0,5.0")))
            .ToArray();
        Assert.Equal(0xEF, bytes[0]);

        // Reader that detects and removes the BOM.
        using var detecting = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var a = PresentMonCsv.ReadAll(detecting, out var headerA, out var problemsA);

        // Reader that leaves it in the text (U+FEFF as the first character of the header line).
        using var keeping = new StreamReader(new MemoryStream(bytes), new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false);
        var b = PresentMonCsv.ReadAll(keeping, out var headerB, out var problemsB);

        Assert.True(headerA.Supported, headerA.UnsupportedReason);
        Assert.True(headerB.Supported, headerB.UnsupportedReason);
        Assert.DoesNotContain("Application", headerB.MissingOptionalColumns);
        Assert.Equal(headerA, headerB, new LogHeaderComparer());
        Assert.Empty(problemsA);
        Assert.Empty(problemsB);
        Assert.Single(a);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Blank_lines_are_skipped_silently_and_do_not_consume_an_index()
    {
        var text = "\n\n" + Header + "\n\n10.0,5.0,A.exe\n   \n\n15.0,5.0,A.exe\n\n";

        var samples = CsvTestData.ReadAllText(text, out var header, out var problems);

        Assert.True(header.Supported);
        Assert.Empty(problems);
        Assert.Equal([0L, 1L], samples.Select(s => s.Index));
        Assert.Equal([10.0, 15.0], samples.Select(s => s.TimestampMs));
    }

    [Fact]
    public void Problems_carry_the_physical_line_number_counting_blank_lines()
    {
        var text = Header + "\n\n10.0,5.0,A.exe\n15.0,-5.0,A.exe\n20.0,5.0\n25.0,5.0,A.exe\n";

        var samples = CsvTestData.ReadAllText(text, out _, out var problems);

        Assert.Equal([10.0, 25.0], samples.Select(s => s.TimestampMs));
        Assert.Equal(2, problems.Count);
        Assert.StartsWith("line 4:", problems[0]);
        Assert.Contains("negative", problems[0]);
        Assert.StartsWith("line 5:", problems[1]);
        Assert.Contains("expected 3 fields", problems[1]);
    }

    [Fact]
    public void Rejected_rows_leave_gaps_in_the_index()
    {
        var text = Header + "\n10.0,5.0,A\n15.0,-5.0,A\n20.0,5.0,A\nbroken\n30.0,5.0,A\n";

        var samples = CsvTestData.ReadAllText(text, out _, out _);

        Assert.Equal([0L, 2L, 4L], samples.Select(s => s.Index));
    }

    [Fact]
    public void An_unsupported_header_gives_no_samples_and_a_reason()
    {
        var samples = CsvTestData.ReadAllText("Application,ProcessID\nA.exe,12\nB.exe,13\n", out var header, out var problems);

        Assert.False(header.Supported);
        Assert.Contains("MsBetweenPresents", header.UnsupportedReason);
        Assert.Empty(samples);
        Assert.Empty(problems);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("  \r\n\r\n")]
    public void Empty_input_is_unsupported_and_says_so(string text)
    {
        var samples = CsvTestData.ReadAllText(text, out var header, out var problems);

        Assert.False(header.Supported);
        Assert.Equal(FrameLogFormat.Unknown, header.Format);
        Assert.Contains("empty", header.UnsupportedReason);
        Assert.Empty(samples);
        Assert.Empty(problems);
    }

    [Fact]
    public void A_header_without_rows_is_supported_and_empty()
    {
        var samples = CsvTestData.ReadAllText(Header + "\n", out var header, out var problems);

        Assert.True(header.Supported);
        Assert.Empty(samples);
        Assert.Empty(problems);
    }

    [Fact]
    public void A_huge_number_of_bad_rows_is_listed_up_to_a_cap_and_the_rest_is_counted()
    {
        var sb = new StringBuilder(Header).Append('\n').Append("10.0,5.0,A\n");
        for (var i = 0; i < 3_000; i++)
        {
            sb.Append("bad row\n");
        }

        var samples = CsvTestData.ReadAllText(sb.ToString(), out _, out var problems);

        Assert.Single(samples);
        Assert.Equal(PresentMonCsv.MaxListedProblems + 1, problems.Count);
        Assert.StartsWith("line 3:", problems[0]);
        Assert.Contains($"{3_000 - PresentMonCsv.MaxListedProblems} more", problems[^1]);
    }

    [Fact]
    public void Read_all_rejects_a_null_reader_as_a_programming_error()
    {
        Assert.Throws<ArgumentNullException>(() => PresentMonCsv.ReadAll(null!, out _, out _));
    }

    // ---- streaming ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void Streaming_gives_the_same_samples_problems_and_header_as_read_all()
    {
        var text = CsvTestData.ReadFixture("v2.6.0-real-sanitized.csv") + "garbage line\n10,20\n";
        var all = CsvTestData.ReadAllText(text, out var allHeader, out var allProblems);

        LogHeaderResult? streamHeader = null;
        var streamProblems = new List<string>();
        using var reader = new StringReader(text);
        var streamed = PresentMonCsv.ReadStreaming(reader, h => streamHeader = h, streamProblems.Add).ToList();

        Assert.Equal(857, all.Count);
        Assert.Equal(all, streamed);
        Assert.Equal(allHeader, streamHeader, new LogHeaderComparer());
        Assert.Equal(allProblems, streamProblems);
        Assert.Equal(2, streamProblems.Count);
    }

    [Fact]
    public void Streaming_reports_the_header_before_the_first_sample()
    {
        var order = new List<string>();
        using var reader = new StringReader(Header + "\n10.0,5.0,A\n");

        foreach (var sample in PresentMonCsv.ReadStreaming(reader, header => order.Add("header"), problem => order.Add("problem")))
        {
            Assert.Equal(10.0, sample.TimestampMs);
            order.Add("sample");
        }

        Assert.Equal(["header", "sample"], order);
    }

    [Fact]
    public void Streaming_an_unsupported_log_reports_the_header_and_yields_nothing()
    {
        LogHeaderResult? header = null;
        using var reader = new StringReader("Alpha,Beta\n1,2\n");

        var samples = PresentMonCsv.ReadStreaming(reader, h => header = h).ToList();

        Assert.Empty(samples);
        Assert.NotNull(header);
        Assert.False(header.Supported);
    }

    [Fact]
    public void Streaming_does_not_run_until_it_is_enumerated_and_does_not_read_ahead()
    {
        var lines = new[] { Header }.Concat(Enumerable.Range(1, 100).Select(i => $"{i}.0,5.0,A")).ToArray();
        var counting = new CountingLineReader(lines);

        var sequence = PresentMonCsv.ReadStreaming(counting);
        Assert.Equal(0, counting.LinesRead);

        var firstFive = sequence.Take(5).ToList();

        Assert.Equal(5, firstFive.Count);
        Assert.True(counting.LinesRead <= 6, $"read {counting.LinesRead} lines to produce 5 samples (1 header + 5 rows expected)");
    }

    [Fact]
    public void Streaming_leaves_the_reader_open_for_its_owner()
    {
        var streamed = new TrackingReader(Header + "\n10.0,5.0,A\n");
        var all = new TrackingReader(Header + "\n10.0,5.0,A\n");

        Assert.Single(PresentMonCsv.ReadStreaming(streamed).ToList());
        Assert.Single(PresentMonCsv.ReadAll(all, out _, out _));

        Assert.False(streamed.Disposed);
        Assert.False(all.Disposed);
    }

    [Fact]
    public void One_million_real_style_rows_stream_in_bounded_memory_and_linear_time()
    {
        // The pool is the 857 real data lines, cycled; the expected totals are computed from the same text without the parser.
        const int rows = 1_000_000;
        var raw = CsvTestData.ReadRawFixture("v2.6.0-real-sanitized.csv");
        var pool = CsvTestData.ReadFixture("v2.6.0-real-sanitized.csv").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var poolFrameTimes = raw.Rows.Select(r => raw.Number(r, "MsBetweenPresents")).ToArray();
        var expectedSum = 0.0;
        for (var i = 0; i < rows; i++)
        {
            expectedSum += poolFrameTimes[i % poolFrameTimes.Length];
        }

        var source = new GeneratedLogReader(pool, rows);
        var baseline = GC.GetTotalMemory(forceFullCollection: true);
        long worstLive = 0;
        double sum = 0;
        long count = 0;
        long lastIndex = -1;
        var clock = Stopwatch.StartNew();

        foreach (var sample in PresentMonCsv.ReadStreaming(source))
        {
            Assert.Equal(lastIndex + 1, sample.Index);
            lastIndex = sample.Index;
            sum += sample.FrameTimeMs;
            count++;
            if (count % 100_000 == 0)
            {
                // A forced collection measures what is still reachable. Streaming keeps nothing; materialising 1 M samples would keep
                // about 1 M x 96 bytes = ~96 MB reachable (and the 1 M strings of a naive reader far more).
                worstLive = Math.Max(worstLive, GC.GetTotalMemory(forceFullCollection: true) - baseline);
            }
        }

        clock.Stop();
        output.WriteLine($"{count} rows in {clock.Elapsed.TotalSeconds:F2} s; worst reachable growth {worstLive / 1024.0 / 1024.0:F2} MB");

        Assert.Equal(rows, count);
        Assert.Equal(expectedSum, sum, expectedSum * 1e-9);
        Assert.True(worstLive < 16L * 1024 * 1024, $"{worstLive / 1024.0 / 1024.0:F1} MB stayed reachable while streaming");
        // Linear-time guard, not a performance target. Measured on the bench in Release: about 1 s for the million rows, including the ten
        // forced collections (roughly 1 M rows/s), with 0.00 MB reachable growth. A live capture produces a few hundred rows per second,
        // so 30 s leaves ~30x headroom for a slow CI VM or a Debug build, while quadratic behaviour or per-row I/O would blow far past it.
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), $"1 M rows took {clock.Elapsed.TotalSeconds:F1} s");
    }

    // ---- readers ---------------------------------------------------------------------------------------------------------------

    private sealed class LogHeaderComparer : IEqualityComparer<LogHeaderResult?>
    {
        public bool Equals(LogHeaderResult? x, LogHeaderResult? y) =>
            x is not null && y is not null
            && x.Supported == y.Supported && x.Format == y.Format && x.UnsupportedReason == y.UnsupportedReason
            && x.MissingOptionalColumns.SequenceEqual(y.MissingOptionalColumns) && x.IgnoredColumns.SequenceEqual(y.IgnoredColumns);

        public int GetHashCode(LogHeaderResult? obj) => HashCode.Combine(obj?.Supported, obj?.Format);
    }

    /// <summary>Hands out the given lines one at a time and counts how many were requested.</summary>
    private sealed class CountingLineReader(string[] lines) : TextReader
    {
        private int _next;

        public int LinesRead => _next;

        public override string? ReadLine() => _next < lines.Length ? lines[_next++] : null;

        public override int Peek() => _next < lines.Length ? 0 : -1;

        public override int Read() => throw new NotSupportedException("the parser must read line by line");
    }

    /// <summary>The real header followed by <c>rows</c> data lines drawn cyclically from a pool, produced on demand (never held in memory).</summary>
    private sealed class GeneratedLogReader(string[] pool, int rows) : TextReader
    {
        private int _emitted = -1; // -1: header not yet handed out

        public override string? ReadLine()
        {
            if (_emitted < 0)
            {
                _emitted = 0;
                return pool[0].TrimEnd('\r');
            }

            if (_emitted >= rows)
            {
                return null;
            }

            // pool[0] is the header line; data lines are pool[1..].
            var line = pool[1 + (_emitted % (pool.Length - 1))];
            _emitted++;
            return line;
        }

        public override int Peek() => _emitted < rows ? 0 : -1;

        public override int Read() => throw new NotSupportedException("the parser must read line by line");
    }

    private sealed class TrackingReader(string text) : StringReader(text)
    {
        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
