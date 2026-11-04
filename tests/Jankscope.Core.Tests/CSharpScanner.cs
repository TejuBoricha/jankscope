using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Jankscope.Core.Tests;

/// <summary>
/// Views of one C# source file, line structure preserved:
/// <see cref="Code"/> has comments and preprocessor lines removed and the <em>contents</em> of string and char literals blanked, so
/// identifiers can be matched without false hits from text; <see cref="CodeAndStrings"/> has comments removed but literals kept, so
/// entry-point names and concatenated names ("Open" + "Process") can be found. Interpolation holes are code in both views. Unicode
/// escapes (a backslash, u, four hex digits) are decoded in both so an escaped identifier cannot hide. <see cref="RawCode"/> and
/// <see cref="Aligned"/> are the same two views without any decoding or joining, character for character aligned with each other,
/// for code that must read the arguments that follow a token.
/// </summary>
/// <param name="EndedInCode">False when the file ended inside a comment or literal: the lexer lost track and no verdict can be trusted.</param>
/// <param name="NonAsciiCode">
/// The distinct non-ASCII characters found in code (outside comments, literals and directives, after escape decoding). C# ignores
/// format characters such as the soft hyphen inside identifiers, so <c>Library&lt;U+00AD&gt;Import</c> is the same attribute to the
/// compiler but a different word to every name-based lint (stage-0 re-verification R1). This code base uses ASCII identifiers only.
/// </param>
internal sealed record ScannedSource(string Code, string CodeAndStrings, string RawCode, string Aligned, bool EndedInCode, string NonAsciiCode);

/// <summary>
/// A small lexer, not a parser: it only needs to know where comments, directives and literals begin and end (review #9, pass 2 #2).
/// It follows the compiler's rules for line ends (LF, CR, NEL, LS, PS), preprocessor lines, char literals, regular, verbatim,
/// interpolated and raw string literals, and nested interpolation holes.
/// </summary>
internal static partial class CSharpScanner
{
    private const char NextLine = (char)0x85;
    private const char LineSeparator = (char)0x2028;
    private const char ParagraphSeparator = (char)0x2029;

    /// <summary>The characters the C# compiler treats as the end of a line (and therefore of a line comment or directive).</summary>
    public static bool IsNewLine(char c) => c is '\n' or '\r' or NextLine or LineSeparator or ParagraphSeparator;

    /// <summary>A regex character-class body holding every line-end character, for tests that split text into lines.</summary>
    public static string NewLineClass { get; } = $"\n\r{NextLine}{LineSeparator}{ParagraphSeparator}";

    public static ScannedSource Scan(string source)
    {
        var lexer = new Lexer(source);
        lexer.ScanCode(insideHole: false);
        var raw = lexer.Code.ToString();
        var aligned = lexer.All.ToString();
        var code = DecodeEscapes(raw);
        return new ScannedSource(code, DecodeEscapes(JoinConcatenatedLiterals(aligned)), raw, aligned, !lexer.Unterminated,
            new string(code.Where(c => c > 0x7F).Distinct().ToArray()));
    }

    /// <summary>Collapses <c>"a" + "b"</c> into <c>"ab"</c> so a name split across two literals is still found.</summary>
    internal static string JoinConcatenatedLiterals(string text)
    {
        string previous;
        do
        {
            previous = text;
            text = ConcatenatedLiterals().Replace(text, string.Empty);
        }
        while (previous != text);

        return text;
    }

    [GeneratedRegex("\"\\s*\\+\\s*\"")]
    private static partial Regex ConcatenatedLiterals();

    // Built from pieces so this source never contains the escape sequence it decodes.
    private static readonly Regex UnicodeEscape = new(@"\\" + "(?:u(?<h4>[0-9A-Fa-f]{4})|U(?<h8>[0-9A-Fa-f]{8})|x(?<hx>[0-9A-Fa-f]{1,4}))", RegexOptions.CultureInvariant);

    /// <summary>Replaces Unicode escape sequences by the characters they denote (invalid code points are left alone).</summary>
    internal static string DecodeEscapes(string text) => UnicodeEscape.Replace(text, m =>
    {
        var hex = m.Groups["h4"].Success ? m.Groups["h4"].Value : m.Groups["h8"].Success ? m.Groups["h8"].Value : m.Groups["hx"].Value;
        var value = int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return value is >= 0 and <= 0x10FFFF && !(value is >= 0xD800 and <= 0xDFFF) ? char.ConvertFromUtf32(value) : m.Value;
    });

    private sealed class Lexer(string s)
    {
        private int _i;
        private bool _lineStart = true;

        public StringBuilder Code { get; } = new();

        public StringBuilder All { get; } = new();

        /// <summary>Set when the end of the file was reached inside a comment, string or char literal.</summary>
        public bool Unterminated { get; private set; }

        private char At(int index) => index < s.Length ? s[index] : '\0';

        private void Track(char c)
        {
            if (IsNewLine(c))
            {
                _lineStart = true;
            }
            else if (!char.IsWhiteSpace(c))
            {
                _lineStart = false;
            }
        }

        private void Both(char c)
        {
            Code.Append(c);
            All.Append(c);
            Track(c);
        }

        /// <summary>Appends literal text: kept in the full view, blanked (line ends kept) in the code view.</summary>
        private void Literal(char c)
        {
            All.Append(c);
            Code.Append(IsNewLine(c) ? c : ' ');
            Track(c);
        }

        private void Blank(char c)
        {
            // Comments vanish from both views; line ends stay so line numbers still line up.
            if (IsNewLine(c))
            {
                Both(c);
            }
        }

        public void ScanCode(bool insideHole)
        {
            var depth = 0;
            while (_i < s.Length)
            {
                var c = s[_i];
                if (c == '/' && At(_i + 1) == '/')
                {
                    while (_i < s.Length && !IsNewLine(s[_i]))
                    {
                        _i++;
                    }

                    Both(' ');
                }
                else if (c == '/' && At(_i + 1) == '*')
                {
                    _i += 2;
                    while (_i < s.Length && !(s[_i] == '*' && At(_i + 1) == '/'))
                    {
                        Blank(s[_i]);
                        _i++;
                    }

                    if (_i >= s.Length)
                    {
                        Unterminated = true;
                    }

                    _i = Math.Min(_i + 2, s.Length);
                    Both(' ');
                }
                else if (c == '#' && _lineStart)
                {
                    // A preprocessor line is free text to the compiler (a stray quote in "#region x" does not start a string).
                    while (_i < s.Length && !IsNewLine(s[_i]))
                    {
                        Both(' ');
                        _i++;
                    }
                }
                else if (c == '\'')
                {
                    CharLiteral();
                }
                else if (c is '"' or '$' or '@' && TryStringStart(out var dollars, out var verbatim, out var quotes))
                {
                    ReadString(dollars, verbatim, quotes);
                }
                else if (c == '{')
                {
                    depth++;
                    Both(c);
                    _i++;
                }
                else if (c == '}')
                {
                    if (insideHole && depth == 0)
                    {
                        return; // the caller consumes the closing brace
                    }

                    depth--;
                    Both(c);
                    _i++;
                }
                else
                {
                    Both(c);
                    _i++;
                }
            }

            if (insideHole)
            {
                Unterminated = true;
            }
        }

        private bool TryStringStart(out int dollars, out bool verbatim, out int quotes)
        {
            dollars = 0;
            verbatim = false;
            quotes = 0;
            var j = _i;
            while (At(j) is '$' or '@')
            {
                if (At(j) == '$')
                {
                    dollars++;
                }
                else
                {
                    verbatim = true;
                }

                j++;
            }

            if (At(j) != '"')
            {
                return false; // an identifier such as @class, or a stray '$'
            }

            while (At(j + quotes) == '"')
            {
                quotes++;
            }

            return true;
        }

        private void CharLiteral()
        {
            Both('\'');
            _i++;
            while (_i < s.Length && s[_i] != '\'' && !IsNewLine(s[_i]))
            {
                if (s[_i] == '\\' && _i + 1 < s.Length)
                {
                    Literal(s[_i++]);
                }

                Literal(s[_i++]);
            }

            if (At(_i) == '\'')
            {
                Both('\'');
                _i++;
            }
            else if (_i >= s.Length)
            {
                Unterminated = true;
            }
        }

        private void ReadString(int dollars, bool verbatim, int quotes)
        {
            while (At(_i) is '$' or '@')
            {
                Both(s[_i++]);
            }

            if (!verbatim && quotes >= 3)
            {
                ReadRawString(dollars, quotes);
                return;
            }

            Both('"');
            _i++;
            var interpolated = dollars > 0;
            while (_i < s.Length)
            {
                var c = s[_i];
                if (interpolated && c == '{')
                {
                    if (At(_i + 1) == '{')
                    {
                        Literal(c);
                        Literal(c);
                        _i += 2;
                    }
                    else
                    {
                        Hole();
                    }
                }
                else if (interpolated && c == '}' && At(_i + 1) == '}')
                {
                    Literal(c);
                    Literal(c);
                    _i += 2;
                }
                else if (verbatim && c == '"')
                {
                    if (At(_i + 1) == '"')
                    {
                        Literal(c);
                        Literal(c);
                        _i += 2;
                    }
                    else
                    {
                        break;
                    }
                }
                else if (!verbatim && c == '\\' && _i + 1 < s.Length)
                {
                    Literal(c);
                    Literal(s[_i + 1]);
                    _i += 2;
                }
                else if (!verbatim && c == '"')
                {
                    break;
                }
                else
                {
                    Literal(c);
                    _i++;
                }
            }

            if (At(_i) == '"')
            {
                Both('"');
                _i++;
            }
            else
            {
                Unterminated = true;
            }
        }

        private void ReadRawString(int dollars, int quotes)
        {
            for (var k = 0; k < quotes; k++)
            {
                Both('"');
                _i++;
            }

            while (_i < s.Length)
            {
                var c = s[_i];
                if (c == '"')
                {
                    var run = 0;
                    while (At(_i + run) == '"')
                    {
                        run++;
                    }

                    if (run >= quotes)
                    {
                        for (var k = 0; k < quotes; k++)
                        {
                            Both('"');
                            _i++;
                        }

                        return;
                    }

                    for (var k = 0; k < run; k++)
                    {
                        Literal('"');
                        _i++;
                    }
                }
                else if (dollars > 0 && c == '{')
                {
                    var run = 0;
                    while (At(_i + run) == '{')
                    {
                        run++;
                    }

                    if (run < dollars)
                    {
                        for (var k = 0; k < run; k++)
                        {
                            Literal('{');
                            _i++;
                        }
                    }
                    else
                    {
                        for (var k = 0; k < run - dollars; k++)
                        {
                            Literal('{');
                            _i++;
                        }

                        Hole(dollars);
                    }
                }
                else
                {
                    Literal(c);
                    _i++;
                }
            }

            Unterminated = true;
        }

        /// <summary>Scans an interpolation hole as code; <paramref name="braces"/> is the number of opening and closing braces that delimit it.</summary>
        private void Hole(int braces = 1)
        {
            for (var k = 0; k < braces; k++)
            {
                Both('{');
                _i++;
            }

            ScanCode(insideHole: true);
            for (var k = 0; k < braces && At(_i) == '}'; k++)
            {
                Both('}');
                _i++;
            }
        }
    }
}
