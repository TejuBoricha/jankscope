using System.Text;

namespace Jankscope.Frames.PresentMon;

/// <summary>
/// Reads UTF-8 text lines from a child's pipe with a hard limit on the memory one line may take, so a child that never ends a line
/// cannot exhaust memory. A line longer than the limit is dropped (the rest of it is skipped up to the next line break) and reported
/// as truncated. Line breaks are LF or CRLF; a lone CR is data. Invalid UTF-8 becomes U+FFFD and never throws.
/// </summary>
public sealed class BoundedLineReader
{
    public const int DefaultMaxLineChars = 16 * 1024;

    private readonly Stream _stream;
    private readonly int _maxLineChars;
    private readonly byte[] _bytes;
    private readonly char[] _chars;
    private readonly Decoder _decoder = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetDecoder();
    private readonly StringBuilder _line = new();
    private int _charPos;
    private int _charLen;
    private bool _overflow;
    private bool _pendingCarriageReturn;
    private bool _endOfStream;

    public BoundedLineReader(Stream stream, int maxLineChars = DefaultMaxLineChars, int bufferSize = 8192)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLineChars, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(bufferSize, 1);
        _stream = stream;
        _maxLineChars = maxLineChars;
        _bytes = new byte[bufferSize];
        _chars = new char[Encoding.UTF8.GetMaxCharCount(bufferSize)];
    }

    /// <summary>Next line, or null at the end of the stream. Not safe for concurrent calls.</summary>
    public async ValueTask<ChildLine?> ReadLineAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            while (_charPos < _charLen)
            {
                var c = _chars[_charPos++];
                if (c == '\n')
                {
                    return Emit(dropCarriageReturn: true);
                }

                if (_pendingCarriageReturn)
                {
                    _pendingCarriageReturn = false;
                    Append('\r');
                }

                if (c == '\r')
                {
                    _pendingCarriageReturn = true;
                }
                else
                {
                    Append(c);
                }
            }

            if (_endOfStream)
            {
                if (_line.Length > 0 || _overflow || _pendingCarriageReturn)
                {
                    return Emit(dropCarriageReturn: false);
                }

                return null;
            }

            var read = await _stream.ReadAsync(_bytes.AsMemory(), cancellationToken).ConfigureAwait(false);
            _charPos = 0;
            if (read == 0)
            {
                _endOfStream = true;
                _charLen = _decoder.GetChars(_bytes, 0, 0, _chars, 0, flush: true);
            }
            else
            {
                _charLen = _decoder.GetChars(_bytes, 0, read, _chars, 0, flush: false);
            }
        }
    }

    private void Append(char c)
    {
        if (_overflow)
        {
            return;
        }

        if (_line.Length >= _maxLineChars)
        {
            _overflow = true;
            _line.Clear();
            return;
        }

        _line.Append(c);
    }

    private ChildLine Emit(bool dropCarriageReturn)
    {
        if (!dropCarriageReturn && _pendingCarriageReturn)
        {
            Append('\r');
        }

        _pendingCarriageReturn = false;
        // Emit(dropCarriageReturn: true) is the line-break path; the end-of-stream path (false) delivers whatever was read, cut off or not.
        var unterminated = !dropCarriageReturn;
        var result = _overflow
            ? new ChildLine(string.Empty, WasTruncated: true, Unterminated: unterminated)
            : new ChildLine(_line.ToString(), WasTruncated: false, Unterminated: unterminated);
        _line.Clear();
        _overflow = false;
        return result;
    }
}
