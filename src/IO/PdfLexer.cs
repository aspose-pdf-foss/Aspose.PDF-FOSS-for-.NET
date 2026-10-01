using System.Globalization;
using System.Text;

namespace Aspose.Pdf.IO;

internal sealed class PdfLexer
{
    private byte[] _data;
    private long _pos;

    /// <summary>True when the most recent Keyword token began directly after a
    /// regular character (it was split off a fused lexeme like "24.5m" —
    /// damaged-stream salvage, not an authored operator).</summary>
    public bool LastKeywordFused { get; private set; }

    /// <summary>
    /// Whether a lexeme with a number fused onto an operator is broken apart.
    ///
    /// The format says an operator runs to the next white space or delimiter, so
    /// <c>m30</c> is ONE token and an unknown one. Real files carry the fused
    /// form anyway, where a writer dropped a separator, and breaking it apart
    /// recovers the page instead of losing the rest of the stream — which is why
    /// this is on unless a caller says otherwise.
    ///
    /// ⚠ A caller reading strictly — validating a stream, or reproducing what
    /// another reader answers — wants it off. The two readings disagree only on
    /// input the format does not allow, and that is exactly the input a strict
    /// reader is being asked about.
    /// </summary>
    public bool Salvaging { get; set; } = true;

    public PdfLexer(byte[] data)
    {
        _data = data;
        _pos = 0;
    }

    /// <summary>Drops the source buffer so it can be collected once the owning document is disposed.</summary>
    internal void ReleaseBuffer() => _data = Array.Empty<byte>();

    public long Position
    {
        get => _pos;
        set => _pos = value;
    }

    public int Length => _data.Length;

    public byte ByteAt(long index) => _data[index];

    public Token NextToken()
    {
        SkipWhitespaceAndComments();

        if (_pos >= _data.Length)
            return Token.EofToken(_pos);

        var startPos = _pos;
        var b = _data[_pos];

        switch (b)
        {
            case (byte)'[':
                _pos++;
                return Token.Delimiter(TokenKind.ArrayStart, startPos);

            case (byte)']':
                _pos++;
                return Token.Delimiter(TokenKind.ArrayEnd, startPos);

            case (byte)'<':
                if (_pos + 1 < _data.Length && _data[_pos + 1] == '<')
                {
                    _pos += 2;
                    return Token.Delimiter(TokenKind.DictStart, startPos);
                }
                return ReadHexString();

            case (byte)'>':
                if (_pos + 1 < _data.Length && _data[_pos + 1] == '>')
                {
                    _pos += 2;
                    return Token.Delimiter(TokenKind.DictEnd, startPos);
                }
                _pos++;
                return Token.Delimiter(TokenKind.DictEnd, startPos); // stray >

            case (byte)'(':
                return ReadLiteralString();

            case (byte)'/':
                return ReadName();

            case (byte)'+' or (byte)'-':
                return ReadNumber();

            case >= (byte)'0' and <= (byte)'9':
                return ReadNumber();

            case (byte)'.':
                return ReadNumber();

            default:
                return ReadKeywordOrBool();
        }
    }

    public Token PeekToken()
    {
        var savedPos = _pos;
        var token = NextToken();
        _pos = savedPos;
        return token;
    }

    private void SkipWhitespaceAndComments()
    {
        while (_pos < _data.Length)
        {
            var b = _data[_pos];
            if (b == ' ' || b == '\t' || b == '\r' || b == '\n' || b == '\0' || b == '\f')
            {
                _pos++;
                continue;
            }

            if (b == '%')
            {
                // Skip comment to end of line
                while (_pos < _data.Length && _data[_pos] != '\r' && _data[_pos] != '\n')
                    _pos++;
                continue;
            }

            break;
        }
    }

    private Token ReadLiteralString()
    {
        var startPos = _pos;
        _pos++; // skip (

        var result = new List<byte>();
        var depth = 1;

        while (_pos < _data.Length && depth > 0)
        {
            var b = _data[_pos];

            if (b == '(')
            {
                depth++;
                result.Add(b);
                _pos++;
            }
            else if (b == ')')
            {
                depth--;
                if (depth > 0)
                {
                    result.Add(b);
                }
                _pos++;
            }
            else if (b == '\\')
            {
                _pos++;
                if (_pos >= _data.Length) break;
                var escaped = _data[_pos];
                switch (escaped)
                {
                    case (byte)'n': result.Add((byte)'\n'); _pos++; break;
                    case (byte)'r': result.Add((byte)'\r'); _pos++; break;
                    case (byte)'t': result.Add((byte)'\t'); _pos++; break;
                    case (byte)'b': result.Add((byte)'\b'); _pos++; break;
                    case (byte)'f': result.Add((byte)'\f'); _pos++; break;
                    case (byte)'(': result.Add((byte)'('); _pos++; break;
                    case (byte)')': result.Add((byte)')'); _pos++; break;
                    case (byte)'\\': result.Add((byte)'\\'); _pos++; break;
                    case (byte)'\r':
                        _pos++;
                        if (_pos < _data.Length && _data[_pos] == '\n') _pos++;
                        break;
                    case (byte)'\n':
                        _pos++;
                        break;
                    case >= (byte)'0' and <= (byte)'7':
                        // Octal escape (1-3 digits)
                        int octal = escaped - '0';
                        _pos++;
                        if (_pos < _data.Length && _data[_pos] >= '0' && _data[_pos] <= '7')
                        {
                            octal = octal * 8 + (_data[_pos] - '0');
                            _pos++;
                            if (_pos < _data.Length && _data[_pos] >= '0' && _data[_pos] <= '7')
                            {
                                octal = octal * 8 + (_data[_pos] - '0');
                                _pos++;
                            }
                        }
                        result.Add((byte)(octal & 0xFF));
                        break;
                    default:
                        // Unknown escape — ignore the backslash per spec
                        result.Add(escaped);
                        _pos++;
                        break;
                }
            }
            else
            {
                // Normalize \r and \r\n to \n per PDF spec
                if (b == '\r')
                {
                    result.Add((byte)'\n');
                    _pos++;
                    if (_pos < _data.Length && _data[_pos] == '\n') _pos++;
                }
                else
                {
                    result.Add(b);
                    _pos++;
                }
            }
        }

        return Token.LiteralStringToken(result.ToArray(), startPos);
    }

    private Token ReadHexString()
    {
        var startPos = _pos;
        _pos++; // skip <

        var result = new List<byte>();
        var nibbleCount = 0;
        var currentByte = 0;

        while (_pos < _data.Length)
        {
            var b = _data[_pos];
            if (b == '>')
            {
                _pos++;
                break;
            }

            // Skip whitespace inside hex strings
            if (b == ' ' || b == '\t' || b == '\r' || b == '\n' || b == '\f')
            {
                _pos++;
                continue;
            }

            var nibble = HexValue(b);
            if (nibble < 0)
            {
                _pos++;
                continue; // skip invalid hex chars
            }

            if (nibbleCount % 2 == 0)
            {
                currentByte = nibble << 4;
            }
            else
            {
                currentByte |= nibble;
                result.Add((byte)currentByte);
            }
            nibbleCount++;
            _pos++;
        }

        // Odd number of nibbles — pad with 0
        if (nibbleCount % 2 == 1)
            result.Add((byte)currentByte);

        return Token.HexStringToken(result.ToArray(), startPos);
    }

    private static int HexValue(byte b) => b switch
    {
        >= (byte)'0' and <= (byte)'9' => b - '0',
        >= (byte)'a' and <= (byte)'f' => b - 'a' + 10,
        >= (byte)'A' and <= (byte)'F' => b - 'A' + 10,
        _ => -1,
    };

    private Token ReadName()
    {
        var startPos = _pos;
        _pos++; // skip /
        var start = _pos;
        var escaped = false;
        while (_pos < _data.Length)
        {
            var b = _data[_pos];
            if (IsWhitespace(b) || IsDelimiter(b))
                break;
            if (b == '#') escaped = true;
            _pos++;
        }
        // A name without a #xx escape is its own bytes; only an escaped one is rebuilt.
        if (!escaped)
            return Token.NameToken(Compat.Latin1.GetString(_data, (int)start, (int)(_pos - start)), startPos);

        var sb = new StringBuilder();
        for (var i = start; i < _pos;)
        {
            var b = _data[i];
            if (b == '#' && i + 2 < _data.Length)
            {
                var hi = HexValue(_data[i + 1]);
                var lo = HexValue(_data[i + 2]);
                if (hi >= 0 && lo >= 0)
                {
                    sb.Append((char)((hi << 4) | lo));
                    i += 3;
                    continue;
                }
            }
            sb.Append((char)b);
            i++;
        }
        return Token.NameToken(sb.ToString(), startPos);
    }

    /// <summary>Significant digits a number is read exactly from its bytes with: the
    /// mantissa stays below 2^53, so dividing by the (exact) power of ten rounds once,
    /// to the same nearest double a decimal parse yields. Longer runs go through the parser.</summary>
    private const int MaxExactDigits = 15;

    private static readonly double[] PowersOfTen =
    {
        1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10, 1e11,
        1e12, 1e13, 1e14, 1e15, 1e16, 1e17, 1e18, 1e19, 1e20, 1e21, 1e22,
    };

    private Token ReadNumber()
    {
        var startPos = _pos;
        var negative = false;
        var hasDecimal = false;
        var anyDigit = false;
        var significant = 0;
        var fractionDigits = 0;
        long mantissa = 0;

        // Sign
        if (_pos < _data.Length && (_data[_pos] == '+' || _data[_pos] == '-'))
        {
            negative = _data[_pos] == '-';
            _pos++;
        }

        while (_pos < _data.Length)
        {
            var b = _data[_pos];
            if (b >= '0' && b <= '9')
            {
                anyDigit = true;
                if (mantissa != 0 || b != '0') significant++;
                if (significant <= MaxExactDigits) mantissa = mantissa * 10 + (b - '0');
                if (hasDecimal) fractionDigits++;
                _pos++;
            }
            else if (b == '.' && !hasDecimal)
            {
                hasDecimal = true;
                _pos++;
            }
            else
            {
                break;
            }
        }
        var lexemeEnd = _pos;

        // A damaged stream can fuse two numbers into one lexeme ("100.1239200.456"
        // where a separator byte was overwritten, or "0.00-25614825" with an embedded
        // sign). The lexer emits exactly ONE number per numeric lexeme: the
        // longest valid prefix (up to the second '.' or an interior sign) is the value
        // and the remainder of the run is discarded — it must not become a second
        // token, or every downstream operand count / array element shifts by one.
        while (_pos < _data.Length && (_data[_pos] == '.' || _data[_pos] == '+' || _data[_pos] == '-'
               || (_data[_pos] >= '0' && _data[_pos] <= '9')))
            _pos++;

        // Bare sign ("-" or "+") with no digits/decimal — treat as 0
        if (!anyDigit && !hasDecimal)
            return Token.IntegerToken(0, startPos);
        // "." alone (or "-." / "+.") is a bare decimal point — treat as 0.0
        if (!anyDigit)
            return Token.RealToken(0.0, startPos);

        if (significant <= MaxExactDigits && fractionDigits < PowersOfTen.Length)
        {
            if (hasDecimal)
            {
                var real = mantissa / PowersOfTen[fractionDigits];
                return Token.RealToken(negative ? -real : real, startPos);
            }
            return Token.IntegerToken(negative ? -mantissa : mantissa, startPos);
        }

        // Too many digits to read exactly: parse the lexeme's text.
        var text = Compat.Latin1.GetString(_data, (int)startPos, (int)(lexemeEnd - startPos));
        if (hasDecimal)
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                value = 0.0;
            return Token.RealToken(value, startPos);
        }
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
            return Token.IntegerToken(integer, startPos);
        // Overflow or malformed — treat as 0
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var dvalue))
            dvalue = 0.0;
        return Token.RealToken(dvalue, startPos);
    }

    /// <summary>The words a lexer meets by the million - content operators and the file
    /// structure keywords - resolved to one shared string each instead of a fresh one per
    /// sighting. Keyed by the word's bytes packed little-endian with its length on top.</summary>
    private static readonly Dictionary<long, string> KnownWords = BuildKnownWords();

    private const int MaxPackedWordLength = 7;

    private static Dictionary<long, string> BuildKnownWords()
    {
        var words = new[]
        {
            "q", "Q", "cm", "w", "J", "j", "M", "d", "ri", "i", "gs",
            "m", "l", "c", "v", "y", "h", "re",
            "S", "s", "f", "F", "f*", "B", "B*", "b", "b*", "n", "W", "W*",
            "BT", "ET", "Tc", "Tw", "Tz", "TL", "Tf", "Tr", "Ts", "Td", "TD", "Tm", "T*",
            "Tj", "TJ", "'", "\"", "d0", "d1",
            "CS", "cs", "SC", "SCN", "sc", "scn", "G", "g", "RG", "rg", "K", "k",
            "sh", "BI", "ID", "EI", "Do", "MP", "DP", "BMC", "BDC", "EMC", "BX", "EX",
            "true", "false", "null", "obj", "endobj", "stream", "xref", "trailer", "R",
        };
        var table = new Dictionary<long, string>(words.Length);
        foreach (var word in words)
        {
            long key = 0;
            for (var i = 0; i < word.Length; i++) key |= (long)(byte)word[i] << (8 * i);
            table[key | ((long)word.Length << 56)] = word;
        }
        return table;
    }

    private string WordAt(long start, int length)
    {
        if (length <= MaxPackedWordLength)
        {
            long key = 0;
            for (var i = 0; i < length; i++) key |= (long)_data[start + i] << (8 * i);
            if (KnownWords.TryGetValue(key | ((long)length << 56), out var known))
                return known;
        }
        return Compat.Latin1.GetString(_data, (int)start, length);
    }

    private Token ReadKeywordOrBool()
    {
        var startPos = _pos;
        // A keyword glued straight onto the previous lexeme ("24.5m" split into
        // 24.5 + m) is salvage from a damaged stream, not an authored operator —
        // consumers that validate operator shape (operand counts) must not treat
        // it as the real thing. Whitespace/delimiter-separated keywords are authored.
        LastKeywordFused = startPos > 0 && startPos <= _data.Length
            && !IsWhitespace(_data[startPos - 1]) && !IsDelimiter(_data[startPos - 1]);

        while (_pos < _data.Length)
        {
            var b = _data[_pos];
            if (IsWhitespace(b) || IsDelimiter(b))
                break;
            // A digit (or '.') ends an operator lexeme — no PDF keyword contains one
            // EXCEPT the Type 3 glyph operators d0/d1. Damaged streams fuse numbers
            // straight onto operators ("re9 w"); splitting here keeps the following
            // number a real token instead of producing an unknown "re9" keyword.
            var length = _pos - startPos;
            if (Salvaging && length > 0 && ((b >= '0' && b <= '9') || b == '.'))
            {
                var isD01 = length == 1 && _data[startPos] == 'd' && (b == '0' || b == '1')
                    && (_pos + 1 >= _data.Length
                        || IsWhitespace(_data[_pos + 1]) || IsDelimiter(_data[_pos + 1]));
                if (!isD01)
                    break;
            }
            _pos++;
        }

        var wordLength = (int)(_pos - startPos);
        // Safety: if no characters were consumed but we're not at EOF, the current byte is
        // a delimiter not handled by the outer switch (e.g. '}', ')' outside a string).
        // Advance past it to prevent an infinite loop.
        if (wordLength == 0 && _pos < _data.Length)
            _pos++;
        var word = wordLength == 0 ? string.Empty : WordAt(startPos, wordLength);

        return word switch
        {
            "true" => Token.BooleanToken(true, startPos),
            "false" => Token.BooleanToken(false, startPos),
            "null" => Token.NullToken(startPos),
            _ => Token.KeywordToken(word, startPos),
        };
    }

    /// <summary>
    /// Read inline image data after the ID keyword. The payload length is taken
    /// exactly whenever it is known — from the geometry (unfiltered, via
    /// <paramref name="expectedLength"/>) or by walking RunLengthDecode packets to
    /// their EOD marker (<paramref name="runLength"/>). Otherwise we scan for an
    /// "EI" delimited by whitespace, a heuristic that can misfire when raw image
    /// bytes contain "EI", which is why an exact length is preferred.
    /// </summary>
    public byte[] ReadInlineImageData(int expectedLength = -1, bool runLength = false)
    {
        // After ID, skip exactly one whitespace byte
        if (_pos < _data.Length && IsWhitespace(_data[_pos]))
            _pos++;

        var start = _pos;

        // RunLengthDecode is self-terminating: walk its packets to the 0x80 EOD
        // marker for the exact encoded length. Deterministic, unlike the EI scan.
        if (runLength && expectedLength < 0)
            expectedLength = RunLengthEncodedLength(start);

        // Known unfiltered length: take exactly that many bytes, then consume
        // the trailing EI. This avoids treating an "EI" byte pair inside the
        // binary image data (whose last byte may not be whitespace) as the
        // terminator, which would desync the whole content stream.
        if (expectedLength >= 0 && start + expectedLength <= _data.Length)
        {
            var exact = new byte[expectedLength];
            Array.Copy(_data, start, exact, 0, expectedLength);
            _pos = start + expectedLength;
            while (_pos < _data.Length && IsWhitespace(_data[_pos]))
                _pos++;
            if (_pos + 1 < _data.Length && _data[_pos] == (byte)'E' && _data[_pos + 1] == (byte)'I')
                _pos += 2;
            return exact;
        }

        // Otherwise scan for an "EI" delimited by whitespace on both sides. The
        // preceding-whitespace requirement keeps us from stopping on an "EI" byte
        // pair that occurs inside the image data.
        for (var p = start; p < _data.Length - 1; p++)
        {
            if (_data[p] != (byte)'E' || _data[p + 1] != (byte)'I') continue;
            var precededByWs = p == start || IsWhitespace(_data[p - 1]);
            var followedByWsOrEof = p + 2 >= _data.Length ||
                IsWhitespace(_data[p + 2]) || IsDelimiter(_data[p + 2]);
            if (precededByWs && followedByWsOrEof)
                return TakeInlineImage(start, p);
        }

        // Fallback: return remaining data
        var fallback = new byte[_data.Length - start];
        Array.Copy(_data, start, fallback, 0, fallback.Length);
        _pos = _data.Length;
        return fallback;
    }

    /// <summary>Slice the inline-image payload [start, eiPos), trimming one
    /// trailing whitespace, and advance past the "EI" marker.</summary>
    private byte[] TakeInlineImage(long start, long eiPos)
    {
        var dataEnd = eiPos;
        if (dataEnd > start && IsWhitespace(_data[dataEnd - 1]))
            dataEnd--;
        var result = new byte[dataEnd - start];
        Array.Copy(_data, start, result, 0, result.Length);
        _pos = eiPos + 2; // skip past "EI"
        return result;
    }

    /// <summary>
    /// Length in bytes of a RunLengthDecode stream starting at <paramref name="from"/>,
    /// up to and including the 0x80 end-of-data marker (PDF 32000 §7.4.5), or -1 if it
    /// runs off the end of the buffer without an EOD.
    /// </summary>
    private int RunLengthEncodedLength(long from)
    {
        var p = from;
        while (p < _data.Length)
        {
            int len = _data[p];
            if (len == 128) return (int)(p - from + 1);   // EOD marker
            p += len < 128 ? len + 2 : 2;                 // literal run (len+1 bytes), or replicate (1 byte)
        }
        return -1;
    }

    internal static bool IsWhitespace(byte b) =>
        b == ' ' || b == '\t' || b == '\r' || b == '\n' || b == '\0' || b == '\f';

    private static bool IsDelimiter(byte b) =>
        b == '(' || b == ')' || b == '<' || b == '>' ||
        b == '[' || b == ']' || b == '{' || b == '}' ||
        b == '/' || b == '%';
}
