using System.Text;
using Jankscope.Frames.PresentMon;

namespace Jankscope.Frames.Tests.PresentMon;

/// <summary>The reader that sits between a child's pipe and the parser: it must bound memory whatever the child writes.</summary>
public class RunnerLineReaderTests
{
    private static async Task<List<ChildLine>> ReadAll(byte[] bytes, int maxLine = 64, int buffer = 8192)
    {
        var reader = new BoundedLineReader(new MemoryStream(bytes), maxLine, buffer);
        var lines = new List<ChildLine>();
        while (await reader.ReadLineAsync(CancellationToken.None) is { } line)
        {
            lines.Add(line);
        }

        return lines;
    }

    private static byte[] Utf8(string text) => new UTF8Encoding(false).GetBytes(text);

    [Fact]
    public async Task Lines_are_split_on_LF_and_CRLF_and_the_terminator_is_not_part_of_the_text()
    {
        var lines = await ReadAll(Utf8("a,b\r\nc,d\ne,f\r\n"));
        Assert.Equal(["a,b", "c,d", "e,f"], lines.Select(l => l.Text));
        Assert.All(lines, l => Assert.False(l.WasTruncated));
    }

    [Fact]
    public async Task A_last_line_without_a_terminator_is_still_delivered_and_then_the_stream_ends()
    {
        var lines = await ReadAll(Utf8("one\ntwo"));
        Assert.Equal(["one", "two"], lines.Select(l => l.Text));

        // Review 1a, finding 12: the cut-off final line says so, so a field-by-field parser can refuse it.
        Assert.Equal([false, true], lines.Select(l => l.Unterminated));
    }

    [Fact]
    public async Task Every_line_that_ends_with_a_line_break_is_marked_terminated_whatever_the_break_is()
    {
        var lines = await ReadAll(Utf8("a\r\nb\nc\r\n\n"));
        Assert.Equal(4, lines.Count);
        Assert.All(lines, l => Assert.False(l.Unterminated));
    }

    [Fact]
    public async Task A_long_line_cut_off_by_the_end_of_the_stream_is_both_truncated_and_unterminated()
    {
        var lines = await ReadAll(Utf8(new string('x', 500)), maxLine: 64);
        var only = Assert.Single(lines);
        Assert.True(only.WasTruncated);
        Assert.True(only.Unterminated);
    }

    [Fact]
    public async Task Empty_lines_are_delivered_as_empty_text_and_an_empty_stream_yields_nothing()
    {
        Assert.Equal(["", "x", ""], (await ReadAll(Utf8("\nx\n\n"))).Select(l => l.Text));
        Assert.Empty(await ReadAll([]));
    }

    [Fact]
    public async Task A_lone_carriage_return_is_data_not_a_line_break()
    {
        Assert.Equal(["a\rb"], (await ReadAll(Utf8("a\rb\n"))).Select(l => l.Text));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    public async Task Splitting_the_bytes_at_any_place_gives_the_same_lines_including_inside_a_multibyte_character(int bufferSize)
    {
        var text = "TestApp.exe,café ゲーム,1.5\r\nsecond,\U0001F600,2\nthird\n";
        var lines = await ReadAll(Utf8(text), maxLine: 200, buffer: bufferSize);
        Assert.Equal(["TestApp.exe,café ゲーム,1.5", "second,\U0001F600,2", "third"], lines.Select(l => l.Text));
    }

    [Fact]
    public async Task A_line_longer_than_the_limit_is_flagged_and_cut_but_the_next_line_is_intact()
    {
        var lines = await ReadAll(Utf8(new string('x', 500) + "\nshort\n"), maxLine: 64);
        Assert.Equal(2, lines.Count);
        Assert.True(lines[0].WasTruncated);
        Assert.Equal(string.Empty, lines[0].Text);
        Assert.Equal("short", lines[1].Text);
    }

    [Fact]
    public async Task A_line_of_exactly_the_limit_is_kept_and_one_more_character_flags_it()
    {
        var exact = await ReadAll(Utf8(new string('y', 64) + "\n"), maxLine: 64);
        Assert.False(exact[0].WasTruncated);
        Assert.Equal(64, exact[0].Text.Length);

        var over = await ReadAll(Utf8(new string('y', 65) + "\n"), maxLine: 64);
        Assert.True(over[0].WasTruncated);
    }

    [Fact]
    public async Task A_stream_that_never_ends_a_line_uses_bounded_memory_and_reports_one_truncated_line()
    {
        var reader = new BoundedLineReader(new EndlessStream((byte)'z', limit: 5_000_000), maxLineChars: 1024, bufferSize: 4096);
        var first = await reader.ReadLineAsync(CancellationToken.None);
        Assert.NotNull(first);
        Assert.True(first!.Value.WasTruncated);
        Assert.Null(await reader.ReadLineAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Invalid_utf8_bytes_become_replacement_characters_and_never_throw()
    {
        var lines = await ReadAll([0x61, 0xFF, 0xFE, 0x62, 0x0A]);
        Assert.Single(lines);
        Assert.StartsWith("a", lines[0].Text, StringComparison.Ordinal);
        Assert.EndsWith("b", lines[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_cancelled_token_stops_a_pending_read()
    {
        using var cts = new CancellationTokenSource();
        var reader = new BoundedLineReader(new BlockingStream(), 64);
        var pending = reader.ReadLineAsync(cts.Token).AsTask();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Silly_limits_are_rejected(int limit)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedLineReader(new MemoryStream(), limit));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedLineReader(new MemoryStream(), 10, limit));
    }

    private sealed class EndlessStream(byte value, long limit) : Stream
    {
        private long _served;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var n = (int)Math.Min(buffer.Length, limit - _served);
            buffer[..n].Fill(value);
            _served += n;
            return n;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromResult(Read(buffer.Span));

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class BlockingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
