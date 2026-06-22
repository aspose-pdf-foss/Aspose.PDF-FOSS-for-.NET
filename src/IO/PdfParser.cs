using Aspose.Pdf.Core;

namespace Aspose.Pdf.IO;

internal sealed class PdfParser
{
    private readonly PdfLexer _lexer;
    private readonly byte[] _data;

    /// <summary>
    /// Optional callback to resolve an indirect object number to a long integer.
    /// Set by PdfReader so that stream /Length indirect refs can be resolved at parse time.
    /// </summary>
    public Func<int, long>? LengthResolver { get; set; }

    public PdfParser(byte[] data)
    {
        _data = data;
        _lexer = new PdfLexer(data);
    }

    public PdfParser(PdfLexer lexer, byte[] data)
    {
        _lexer = lexer;
        _data = data;
    }

    public PdfLexer Lexer => _lexer;

    /// <summary>
    /// Parse a direct object, with indirect-ref lookahead for "N G R" sequences.
    /// </summary>
    public PdfObject ParseObject()
    {
        var token = _lexer.NextToken();
        return ParseObjectFromToken(token);
    }

    private PdfObject ParseObjectFromToken(Token token)
    {
        switch (token.Kind)
        {
            case TokenKind.Integer:
            {
                // Lookahead for indirect reference: int int R
                var saved = _lexer.Position;
                var next = _lexer.NextToken();
                if (next.Kind == TokenKind.Integer)
                {
                    var next2 = _lexer.NextToken();
                    if (next2.Kind == TokenKind.Keyword && next2.StringValue == "R")
                    {
                        return new PdfIndirectRef((int)token.IntValue, (int)next.IntValue);
                    }
                }
                _lexer.Position = saved;
                return new PdfInteger(token.IntValue);
            }

            case TokenKind.Real:
                return new PdfReal(token.RealValue);

            case TokenKind.LiteralString:
                return new PdfString(token.BytesValue!, isHex: false);

            case TokenKind.HexString:
                return new PdfString(token.BytesValue!, isHex: true);

            case TokenKind.Name:
                return new PdfName(token.StringValue!);

            case TokenKind.Boolean:
                return token.BoolValue ? PdfBoolean.True : PdfBoolean.False;

            case TokenKind.Null:
                return PdfNull.Instance;

            case TokenKind.ArrayStart:
                return ParseArray();

            case TokenKind.DictStart:
                return ParseDictionaryOrStream();

            case TokenKind.Keyword when token.StringValue == "null":
                return PdfNull.Instance;

            default:
                throw new InvalidOperationException(
                    $"Unexpected token {token.Kind} at position {token.Position}");
        }
    }

    private PdfArray ParseArray()
    {
        var array = new PdfArray();
        while (true)
        {
            var token = _lexer.NextToken();
            if (token.Kind == TokenKind.ArrayEnd || token.Kind == TokenKind.Eof)
                break;
            array.Add(ParseObjectFromToken(token));
        }
        return array;
    }

    private PdfObject ParseDictionaryOrStream()
    {
        var dict = new PdfDictionary();

        while (true)
        {
            var keyToken = _lexer.NextToken();
            if (keyToken.Kind == TokenKind.DictEnd || keyToken.Kind == TokenKind.Eof)
                break;

            if (keyToken.Kind != TokenKind.Name)
                throw new InvalidOperationException(
                    $"Expected name key in dictionary, got {keyToken.Kind} at {keyToken.Position}");

            var value = ParseObject();
            dict.Set(keyToken.StringValue!, value);
        }

        // Check if followed by stream keyword
        var savedPos = _lexer.Position;
        var nextToken = _lexer.NextToken();
        if (nextToken.Kind == TokenKind.Keyword && nextToken.StringValue == "stream")
        {
            var streamData = ReadStreamBody(dict);
            return new PdfStream(dict, streamData);
        }

        _lexer.Position = savedPos;
        return dict;
    }

    private byte[] ReadStreamBody(PdfDictionary dict)
    {
        // Skip EOL after "stream" keyword (CR, LF, or CRLF). PDF 32000 §7.3.8.1 puts
        // the EOL directly after "stream", but some writers emit a stray space/tab
        // before it ("stream \r\n"); skip those too, otherwise the stream data starts
        // a few bytes early and a filtered stream decodes to garbage (a stray
        // " \r\n" read as the first bytes of a FlateDecode stream corrupts it and
        // renders the page blank). A well-formed "stream\r\n" hits the EOL on the
        // first byte, so this skip is a no-op there.
        var pos = _lexer.Position;
        while (pos < _data.Length && (_data[pos] == ' ' || _data[pos] == '\t'))
            pos++;
        if (pos < _data.Length && _data[pos] == '\r')
        {
            pos++;
            if (pos < _data.Length && _data[pos] == '\n')
                pos++;
        }
        else if (pos < _data.Length && _data[pos] == '\n')
        {
            pos++;
        }

        // Try to use /Length — may be an indirect ref that requires resolution
        var lengthValue = dict.GetInt("Length", -1);
        if (lengthValue < 0 && dict.Get("Length") is PdfIndirectRef lenRef && LengthResolver is not null)
            lengthValue = LengthResolver(lenRef.ObjectNumber);
        if (lengthValue > 0 && pos + lengthValue <= _data.Length)
        {
            var result = new byte[lengthValue];
            Array.Copy(_data, pos, result, 0, (int)lengthValue);

            // Advance lexer past endstream. PDF spec allows one EOL before endstream,
            // but some PDFs have extra whitespace. Skip any whitespace chars before checking.
            var checkPos = pos + lengthValue;
            while (checkPos < _data.Length && (_data[checkPos] == '\r' || _data[checkPos] == '\n' || _data[checkPos] == ' '))
                checkPos++;
            if (MatchKeyword(checkPos, "endstream"u8))
                _lexer.Position = checkPos + 9;
            else
                // endstream not adjacent to declared length — scan forward from declared end to position lexer
                _lexer.Position = ScanForEndstreamPosition(pos + lengthValue);
            return result;
        }

        // Fallback: scan for endstream (used when Length is missing or 0)
        return ScanForEndstream(pos);
    }

    /// <summary>
    /// Scan forward from startPos to find "endstream" and return the position just after it.
    /// Used to advance the lexer when the declared Length doesn't have endstream immediately after.
    /// </summary>
    private long ScanForEndstreamPosition(long startPos)
    {
        var needle = "endstream"u8;
        for (var i = startPos; i <= _data.Length - needle.Length; i++)
        {
            if (MatchKeyword(i, needle))
                return i + needle.Length;
        }
        return startPos;
    }

    private byte[] ScanForEndstream(long startPos)
    {
        var needle = "endstream"u8;
        for (var i = startPos; i <= _data.Length - needle.Length; i++)
        {
            if (MatchKeyword(i, needle))
            {
                // Trim trailing EOL
                var endData = i;
                if (endData > startPos && _data[endData - 1] == '\n') endData--;
                if (endData > startPos && _data[endData - 1] == '\r') endData--;

                var length = (int)(endData - startPos);
                var result = new byte[length];
                Array.Copy(_data, startPos, result, 0, length);
                _lexer.Position = i + needle.Length;
                return result;
            }
        }

        // No endstream found — return rest
        var remaining = (int)(_data.Length - startPos);
        var fallback = new byte[remaining];
        Array.Copy(_data, startPos, fallback, 0, remaining);
        _lexer.Position = _data.Length;
        return fallback;
    }

    private bool MatchKeyword(long offset, ReadOnlySpan<byte> keyword)
    {
        if (offset + keyword.Length > _data.Length)
            return false;
        return _data.AsSpan((int)offset, keyword.Length).SequenceEqual(keyword);
    }

    /// <summary>
    /// Parse "N G obj . endobj" — returns the indirect object.
    /// Assumes lexer is positioned at the object number.
    /// </summary>
    public IndirectObject ParseIndirectObject()
    {
        var objNumToken = _lexer.NextToken();
        var genToken = _lexer.NextToken();
        var objKeyword = _lexer.NextToken();

        if (objNumToken.Kind != TokenKind.Integer || genToken.Kind != TokenKind.Integer ||
            objKeyword.Kind != TokenKind.Keyword || objKeyword.StringValue != "obj")
        {
            throw new InvalidOperationException(
                $"Expected indirect object header at position {objNumToken.Position}");
        }

        var value = ParseObject();

        // Skip to endobj
        var endToken = _lexer.NextToken();
        // The value might have consumed endobj as part of stream handling,
        // so only verify if we got a keyword
        if (endToken.Kind == TokenKind.Keyword && endToken.StringValue != "endobj")
        {
            // Unexpected — but tolerate
        }

        return new IndirectObject((int)objNumToken.IntValue, (int)genToken.IntValue, value);
    }

    /// <summary>
    /// Parse an indirect object when the lexer is already positioned at the start.
    /// This version takes explicit obj/gen numbers (for object stream parsing).
    /// </summary>
    public PdfObject ParseObjectAt(long offset)
    {
        _lexer.Position = offset;
        return ParseObject();
    }
}
