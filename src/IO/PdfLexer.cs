using System.Globalization;
using System.Text;

namespace Aspose.Pdf.IO;

internal sealed class PdfLexer
{
    private readonly byte[] _data;
    private long _pos;

    public PdfLexer(byte[] data)
    {
        _data = data;
        _pos = 0;
    }

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

        var sb = new StringBuilder();
        while (_pos < _data.Length)
        {
            var b = _data[_pos];
            if (IsWhitespace(b) || IsDelimiter(b))
                break;

            if (b == '#' && _pos + 2 < _data.Length)
            {
                var hi = HexValue(_data[_pos + 1]);
                var lo = HexValue(_data[_pos + 2]);
                if (hi >= 0 && lo >= 0)
                {
                    sb.Append((char)((hi << 4) | lo));
                    _pos += 3;
                    continue;
                }
            }

            sb.Append((char)b);
            _pos++;
        }

        return Token.NameToken(sb.ToString(), startPos);
    }

    private Token ReadNumber()
    {
        var startPos = _pos;
        var sb = new StringBuilder();
        var hasDecimal = false;

        // Sign
        if (_pos < _data.Length && (_data[_pos] == '+' || _data[_pos] == '-'))
        {
            sb.Append((char)_data[_pos]);
            _pos++;
        }

        while (_pos < _data.Length)
        {
            var b = _data[_pos];
            if (b >= '0' && b <= '9')
            {
                sb.Append((char)b);
                _pos++;
            }
            else if (b == '.' && !hasDecimal)
            {
                hasDecimal = true;
                sb.Append('.');
                _pos++;
            }
            else
            {
                break;
            }
        }

        var text = sb.ToString();

        // Bare sign ("-" or "+") with no digits/decimal — treat as 0
        if (text is "-" or "+")
            return Token.IntegerToken(0, startPos);

        if (hasDecimal)
        {
            // "." alone (or "-." / "+.") is a bare decimal point — treat as 0.0
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                value = 0.0;
            return Token.RealToken(value, startPos);
        }
        else
        {
            if (long.TryParse(text, CultureInfo.InvariantCulture, out var value))
                return Token.IntegerToken(value, startPos);
            // Overflow or malformed — treat as 0
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var dvalue))
                dvalue = 0.0;
            return Token.RealToken(dvalue, startPos);
        }
    }

    private Token ReadKeywordOrBool()
    {
        var startPos = _pos;
        var sb = new StringBuilder();

        while (_pos < _data.Length)
        {
            var b = _data[_pos];
            if (IsWhitespace(b) || IsDelimiter(b))
                break;
            sb.Append((char)b);
            _pos++;
        }

        var word = sb.ToString();

        // Safety: if no characters were consumed but we're not at EOF, the current byte is
        // a delimiter not handled by the outer switch (e.g. '}', ')' outside a string).
        // Advance past it to prevent an infinite loop.
        if (word.Length == 0 && _pos < _data.Length)
            _pos++;

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

    private static bool IsWhitespace(byte b) =>
        b == ' ' || b == '\t' || b == '\r' || b == '\n' || b == '\0' || b == '\f';

    private static bool IsDelimiter(byte b) =>
        b == '(' || b == ')' || b == '<' || b == '>' ||
        b == '[' || b == ']' || b == '{' || b == '}' ||
        b == '/' || b == '%';
}
