using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

/// <summary>
/// Modifies text state properties (font size, etc.) in PDF content streams.
/// Finds the Tf operator associated with a given text string and updates its size parameter.
/// </summary>
internal sealed partial class TextStateModifier
{
    /// <summary>The x/y of the nearest simple `1 0 0 1 x y Tm` seat preceding
    /// <paramref name="showStart"/>, or null when the run is positioned any other
    /// way (Td chains, rotated matrices — those lines are never re-laid).</summary>
    private static (double x, double y)? FindSeatBefore(byte[] content, int showStart)
    {
        var s = Encoding.Latin1.GetString(content, 0, System.Math.Min(showStart, content.Length));
        System.Text.RegularExpressions.Match? last = null;
        foreach (System.Text.RegularExpressions.Match m in SimpleTm.Matches(s))
            last = m;
        if (last is null) return null;
        return (double.Parse(last.Groups[1].Value, CultureInfo.InvariantCulture),
                double.Parse(last.Groups[2].Value, CultureInfo.InvariantCulture));
    }

    /// <summary>Font-name spans of the shortest run of CONSECUTIVE show operators whose
    /// concatenated text contains <paramref name="text"/> — the case where one absorbed
    /// fragment is drawn by several operators (a word split as <c>(c) Tj … (reated) Tj</c>,
    /// each with its own Tf). Restyling such a fragment has to repoint every one of those
    /// Tf operators, or the leftovers keep the original font alive. Null when no window
    /// matches; spans already naming <paramref name="alreadyReplacedRes"/> are dropped, so
    /// re-running over a converted region changes nothing.</summary>
    private List<(int start, int end)>? FindSpannedTfSpans(byte[] streamBytes, string text,
        PdfDictionary pageDict, PdfReader reader, string? alreadyReplacedRes)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var ops = CollectShowOps(streamBytes, pageDict, reader);
        if (ops.Count < 2) return null;

        for (var start = 0; start < ops.Count; start++)
        {
            var head = ops[start].decoded;
            if (head.Length == 0 || head.Contains(text, StringComparison.Ordinal)) continue;

            // The fragment starts inside this operator: its text ends with some prefix of
            // the fragment. Take the longest such overlap, then walk forward consuming the
            // remainder operator by operator — the last one may overshoot (it carries the
            // text that follows the fragment).
            var overlap = 0;
            for (var k = Math.Min(head.Length, text.Length); k >= 1; k--)
                if (head.EndsWith(text[..k], StringComparison.Ordinal)) { overlap = k; break; }
            if (overlap == 0) continue;

            var remaining = text[overlap..];
            var last = start;
            var matched = remaining.Length == 0;
            for (var end = start + 1; end < ops.Count && !matched; end++)
            {
                var piece = ops[end].decoded;
                if (piece.Length == 0) { last = end; continue; }
                if (remaining.StartsWith(piece, StringComparison.Ordinal))
                {
                    remaining = remaining[piece.Length..];
                    last = end;
                    matched = remaining.Length == 0;
                }
                else if (piece.StartsWith(remaining, StringComparison.Ordinal))
                {
                    last = end;
                    matched = true;
                }
                else break;
            }
            if (!matched || last == start) continue;

            var spans = new List<(int start, int end)>();
            for (var i = start; i <= last; i++)
            {
                if (ops[i].nameStart < 0) continue;
                if (string.Equals(ops[i].res, alreadyReplacedRes, StringComparison.Ordinal)) continue;
                var span = (ops[i].nameStart, ops[i].nameEnd);
                if (!spans.Contains(span)) spans.Add(span);
            }
            if (spans.Count > 0) return spans;
        }
        return null;
    }

    /// <summary>Every text-showing operator in the stream, in order: the byte span of the
    /// Tf name operand governing it, that name, and the text it decodes to. Composite
    /// (Type0) runs decode through their ToUnicode like the simple ones but cannot be
    /// restyled by a Tf repoint, so they are reported with no name span.</summary>
    private List<(int nameStart, int nameEnd, string? res, string decoded)> CollectShowOps(
        byte[] streamBytes, PdfDictionary pageDict, PdfReader reader)
    {
        var result = new List<(int, int, string?, string)>();
        var fonts = TextAbsorber.ResolveFonts(pageDict, reader);
        var lexer = new PdfLexer(streamBytes);
        var operands = new List<(TokenKind kind, PdfObject obj, int startPos, int endPos)>();
        int tfNameStart = -1, tfNameEnd = -1;
        string? tfRes = null;
        Dictionary<int, string>? toUnicode = null;
        var simple = false;

        while (true)
        {
            var startPos = (int)lexer.Position;
            var token = lexer.NextToken();
            if (token.Kind == TokenKind.Eof) break;
            var endPos = (int)lexer.Position;
            switch (token.Kind)
            {
                case TokenKind.Integer:
                    operands.Add((token.Kind, new PdfInteger(token.IntValue), startPos, endPos));
                    break;
                case TokenKind.Real:
                    operands.Add((token.Kind, new PdfReal(token.RealValue), startPos, endPos));
                    break;
                case TokenKind.LiteralString:
                    operands.Add((token.Kind, new PdfString(token.BytesValue!), startPos, endPos));
                    break;
                case TokenKind.HexString:
                    operands.Add((token.Kind, new PdfString(token.BytesValue!, isHex: true), startPos, endPos));
                    break;
                case TokenKind.Name:
                    operands.Add((token.Kind, new PdfName(token.StringValue!), startPos, endPos));
                    break;
                case TokenKind.ArrayStart:
                {
                    var arr = new StringBuilder();
                    while (true)
                    {
                        var t = lexer.NextToken();
                        if (t.Kind == TokenKind.Eof) return result;
                        if (t.Kind == TokenKind.ArrayEnd) break;
                        if ((t.Kind == TokenKind.LiteralString || t.Kind == TokenKind.HexString)
                            && t.BytesValue is not null)
                            arr.Append(DecodeTextString(t.BytesValue, toUnicode));
                    }
                    operands.Add((TokenKind.ArrayStart, new PdfString(Cp1252.GetBytes(arr.ToString())),
                        startPos, (int)lexer.Position));
                    break;
                }
                case TokenKind.Keyword:
                {
                    var op = token.StringValue!;
                    if (op == "Tf" && operands.Count >= 2 && operands[0].obj is PdfName fn)
                    {
                        tfRes = fn.Value;
                        tfNameStart = operands[0].startPos;
                        tfNameEnd = operands[0].endPos;
                        if (fonts.TryGetValue(fn.Value, out var fontDict))
                        {
                            toUnicode = TextAbsorber.ParseToUnicodeFromDict(fontDict, reader);
                            simple = fontDict.GetName("Subtype") != "Type0";
                        }
                        else { toUnicode = null; simple = false; }
                    }
                    else if (op is "Tj" or "TJ" or "'" or "\"" && operands.Count >= 1
                             && operands[^1].obj is PdfString show)
                    {
                        var decoded = DecodeTextString(show.Value, toUnicode);
                        result.Add(simple ? (tfNameStart, tfNameEnd, tfRes, decoded)
                                          : (-1, -1, tfRes, decoded));
                    }
                    operands.Clear();
                    break;
                }
                default:
                    operands.Clear();
                    break;
            }
        }
        return result;
    }

    private static string DecodeTextString(byte[] bytes, Dictionary<int, string>? toUnicode)
    {
        if (toUnicode is not null && toUnicode.Count > 0)
        {
            var sb = new StringBuilder();
            // Try 2-byte codes first (CID fonts)
            if (bytes.Length >= 2 && bytes.Length % 2 == 0)
            {
                bool allMapped = true;
                for (int i = 0; i < bytes.Length; i += 2)
                {
                    int code = (bytes[i] << 8) | bytes[i + 1];
                    if (toUnicode.TryGetValue(code, out var ch))
                        sb.Append(ch);
                    else
                    {
                        allMapped = false;
                        break;
                    }
                }
                if (allMapped) return sb.ToString();
                sb.Clear();
            }
            // 1-byte codes
            foreach (var b in bytes)
            {
                if (toUnicode.TryGetValue(b, out var ch))
                    sb.Append(ch);
                else
                    sb.Append((char)b);
            }
            return sb.ToString();
        }
        // Default: WinAnsiEncoding / Latin1
        return Cp1252.GetString(bytes);
    }

    private static List<byte[]> GetContentStreams(Page page, PdfReader reader)
    {
        var result = new List<byte[]>();
        var contentsObj = reader.Resolve(page.Dict.Get("Contents"));
        if (contentsObj is PdfStream stream)
            result.Add(reader.DecodeStream(stream));
        else if (contentsObj is PdfArray arr)
        {
            foreach (var item in arr)
            {
                var s = reader.ResolveStream(item);
                if (s is not null) result.Add(reader.DecodeStream(s));
            }
        }
        return result;
    }

    private static byte[] CombineStreams(List<byte[]> streams)
    {
        if (streams.Count == 1) return streams[0];
        var total = 0;
        foreach (var s in streams) total += s.Length + 1;
        var result = new byte[total];
        var pos = 0;
        foreach (var s in streams)
        {
            Array.Copy(s, 0, result, pos, s.Length);
            pos += s.Length;
            result[pos++] = (byte)'\n';
        }
        return result;
    }
}
