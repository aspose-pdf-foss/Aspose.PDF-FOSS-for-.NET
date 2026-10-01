using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextReplacer
{
    private static (PdfArray result, int endPos) ParseContentArrayWithPositions(PdfLexer lexer)
    {
        int endPos = default;
        var array = new PdfArray();
        while (true)
        {
            var t = lexer.NextToken();
            if (t.Kind == TokenKind.ArrayEnd || t.Kind == TokenKind.Eof)
            {
                endPos = (int)lexer.Position;
                return (array, endPos);
            }
            switch (t.Kind)
            {
                case TokenKind.Integer: array.Add(new PdfInteger(t.IntValue)); break;
                case TokenKind.Real: array.Add(new PdfReal(t.RealValue)); break;
                case TokenKind.LiteralString: array.Add(new PdfString(t.BytesValue!)); break;
                case TokenKind.HexString: array.Add(new PdfString(t.BytesValue!, isHex: true)); break;
                case TokenKind.Name: array.Add(new PdfName(t.StringValue!)); break;
            }
        }
    }

    private static byte[] CombineStreams(List<byte[]> streams)
    {
        if (streams.Count == 1) return streams[0];
        var total = 0;
        foreach (var s in streams) total += s.Length + 1; // +1 for separator newline
        var result = new byte[total];
        var offset = 0;
        foreach (var s in streams)
        {
            s.CopyTo(result, offset);
            offset += s.Length;
            result[offset++] = (byte)'\n';
        }
        return result;
    }

    /// <summary>Whether <paramref name="content"/> can show text: a token scan for the
    /// text-showing operators (Tj, TJ, ', ") and for Do, whose form may show text in turn.
    /// A stream carrying none of them has nothing for a replace to find, so its walk -
    /// fonts resolved, every operand boxed - is skipped. The scan is conservative: a
    /// keyword inside a string literal or inline-image data counts as a sighting, which
    /// only costs the walk it would have had anyway.</summary>
    internal static bool MayShowText(byte[] content)
    {
        for (var i = 0; i < content.Length; i++)
        {
            var b = content[i];
            var next = i + 1 < content.Length ? content[i + 1] : (byte)0;
            var length = b switch
            {
                (byte)'T' when next is (byte)'j' or (byte)'J' => 2,
                (byte)'D' when next == (byte)'o' => 2,
                (byte)'\'' or (byte)'"' => 1,
                _ => 0,
            };
            if (length == 0) continue;
            var opensToken = i == 0 || IsTokenBoundary(content[i - 1]);
            var closesToken = i + length >= content.Length || IsTokenBoundary(content[i + length]);
            if (opensToken && closesToken) return true;
        }
        return false;
    }

    private static bool IsTokenBoundary(byte b) => b is (byte)' ' or (byte)'\n' or (byte)'\r'
        or (byte)'\t' or (byte)'\f' or 0 or (byte)'(' or (byte)')' or (byte)'<' or (byte)'>'
        or (byte)'[' or (byte)']' or (byte)'/' or (byte)'%' or (byte)'{' or (byte)'}';

    /// <summary>Decode a form XObject's content unless it is already known to show no
    /// text. The verdict is kept on the stream, so every later replace on the page skips
    /// the form without decoding it: a drawing page can invoke tens of thousands of
    /// text-free forms, and walking them once per edited fragment was the whole cost.</summary>
    private static bool FormMayShowText(PdfStream form, PdfReader reader, out byte[] content)
    {
        if (form.ShowsNoText == true)
        {
            content = [];
            return false;
        }
        content = reader.DecodeStream(form);
        form.ShowsNoText ??= !MayShowText(content);
        return form.ShowsNoText != true;
    }

    private static List<byte[]> GetContentStreams(Page page, PdfReader reader)
    {
        var result = new List<byte[]>();
        var contentsObj = reader.Resolve(page.Dict.Get("Contents"));

        if (contentsObj is PdfStream stream)
        {
            result.Add(reader.DecodeStream(stream));
        }
        else if (contentsObj is PdfArray arr)
        {
            foreach (var item in arr)
            {
                var s = reader.ResolveStream(item);
                if (s is not null)
                    result.Add(reader.DecodeStream(s));
            }
        }

        return result;
    }

    private static void SkipInlineImage(PdfLexer lexer)
    {
        while (true)
        {
            var t = lexer.NextToken();
            if (t.Kind == TokenKind.Eof) return;
            if (t.Kind == TokenKind.Keyword && t.StringValue == "ID") break;
        }

        var pos = lexer.Position + 1;
        var len = lexer.Length;

        static bool IsWs(byte b) => b is 0x00 or 0x09 or 0x0A or 0x0C or 0x0D or 0x20;

        // First choice: whitespace-EI-whitespace. Fallback: EI-whitespace whose
        // following bytes read as ordinary operator text - a Flate inline-image
        // payload can END FLUSH against the EI with no separator before it.
        long fallback = -1;
        while (pos < len - 1)
        {
            if (lexer.ByteAt(pos) == (byte)'E' && lexer.ByteAt(pos + 1) == (byte)'I')
            {
                var after = pos + 2;
                var afterWs = after >= len || IsWs(lexer.ByteAt(after));
                if (afterWs)
                {
                    if (pos > 0 && IsWs(lexer.ByteAt(pos - 1)))
                    {
                        lexer.Position = after;
                        return;
                    }
                    if (fallback < 0)
                    {
                        // Plausibility: the next 16 bytes are printable/whitespace.
                        var ok = true;
                        for (var k = after; k < Math.Min(after + 16, len); k++)
                        {
                            var nb = lexer.ByteAt(k);
                            if (!IsWs(nb) && (nb < 0x20 || nb > 0x7E)) { ok = false; break; }
                        }
                        if (ok) fallback = after;
                    }
                }
            }
            pos++;
        }
        lexer.Position = fallback >= 0 ? fallback : len;
    }
}
