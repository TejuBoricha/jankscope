namespace Jankscope.Frames.PresentMon;

/// <summary>
/// Position of one field inside a line. For a quoted field <see cref="Start"/> and <see cref="Length"/> cover the text between the
/// quotes, still containing any doubled quotes (<see cref="HasDoubledQuote"/>).
/// </summary>
internal readonly struct CsvField(int start, int length, bool hasDoubledQuote)
{
    public int Start { get; } = start;

    public int Length { get; } = length;

    public bool HasDoubledQuote { get; } = hasDoubledQuote;

    public ReadOnlySpan<char> Raw(ReadOnlySpan<char> line) => line.Slice(Start, Length);
}

/// <summary>
/// Minimal RFC 4180 style splitter for one line: commas separate fields, a field that starts with a quote may contain commas and
/// doubled quotes. It never throws; a malformed line is reported with a reason. Fields never span lines (PresentMon writes none that do).
/// </summary>
internal static class CsvLineSplitter
{
    /// <summary>Longest line the parser will look at. A real row is about 220 characters; an application name is at most 260.</summary>
    public const int MaxLineLength = 16 * 1024;

    /// <summary>
    /// Splits <paramref name="line"/> into <paramref name="fields"/>. Only the first <c>fields.Length</c> fields are stored, but
    /// <paramref name="count"/> is always the true number of fields, so the caller can say "found 12" for a short or long row.
    /// </summary>
    public static bool TrySplit(ReadOnlySpan<char> line, Span<CsvField> fields, out int count, out string? problem)
    {
        count = 0;
        problem = null;
        var i = 0;

        while (true)
        {
            CsvField field;
            if (i < line.Length && line[i] == '"')
            {
                var start = ++i;
                var doubled = false;
                while (true)
                {
                    var quote = line[i..].IndexOf('"');
                    if (quote < 0)
                    {
                        problem = "unterminated quoted field";
                        return false;
                    }

                    i += quote;
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        doubled = true;
                        i += 2;
                        continue;
                    }

                    break;
                }

                field = new CsvField(start, i - start, doubled);
                i++; // the closing quote
                if (i < line.Length && line[i] != ',')
                {
                    problem = "unexpected text after the closing quote of a quoted field";
                    return false;
                }
            }
            else
            {
                var rest = line[i..];
                var stop = rest.IndexOfAny(',', '"');
                if (stop >= 0 && rest[stop] == '"')
                {
                    problem = "stray quote inside an unquoted field";
                    return false;
                }

                var length = stop < 0 ? rest.Length : stop;
                field = new CsvField(i, length, hasDoubledQuote: false);
                i += length;
            }

            if (count < fields.Length)
            {
                fields[count] = field;
            }

            count++;
            if (i >= line.Length)
            {
                return true;
            }

            i++; // the comma; a trailing comma makes the next field an empty one
        }
    }
}
