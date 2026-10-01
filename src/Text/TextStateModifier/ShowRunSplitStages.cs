using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

internal sealed partial class TextStateModifier
{
    /// <summary>Lex the TJ operand array into its items: every literal string and every
    /// kern number in array order, each tagged with the character span it shows, with the
    /// character count they show between them. The list is null when the array holds
    /// anything this rewrite cannot address byte-for-byte.</summary>
    private static (List<(bool isString, int start, int end, int charStart, int charLen)>? items, int chars) ScanTjItems(
        byte[] original, int arrStart, int arrEnd, Dictionary<int, string>? toUnicode)
    {
        var chars = 0;
        var items = new List<(bool isString, int start, int end, int charStart, int charLen)>();
        var lexer = new PdfLexer(original) { Position = arrStart };
        if (lexer.NextToken().Kind != TokenKind.ArrayStart) return (null, 0);
        while (true)
        {
            var itemStart = (int)lexer.Position;
            var t = lexer.NextToken();
            if (t.Kind == TokenKind.ArrayEnd) break;
            var itemEnd = (int)lexer.Position;
            if (itemEnd > arrEnd) return (null, 0);
            switch (t.Kind)
            {
                case TokenKind.LiteralString:
                {
                    var bytes = t.BytesValue;
                    if (bytes is null) return (null, 0);
                    // The literal must be a plain, escape-free (…) run: the char offsets
                    // below index straight into its bytes.
                    if (original[itemEnd - 1] != (byte)')') return (null, 0);
                    var innerStart = itemStart;
                    while (innerStart < itemEnd && original[innerStart] != (byte)'(') innerStart++;
                    if (innerStart >= itemEnd) return (null, 0);
                    if (itemEnd - 1 - (innerStart + 1) != bytes.Length) return (null, 0);
                    for (int i = innerStart + 1; i < itemEnd - 1; i++)
                        if (original[i] is 0x5C or 0x28 or 0x29) return (null, 0);   // backslash, ( , )
                    var decodedItem = DecodeTextString(bytes, toUnicode);
                    if (decodedItem.Length != bytes.Length) return (null, 0);
                    items.Add((true, innerStart + 1, itemEnd - 1, chars, bytes.Length));
                    chars += bytes.Length;
                    break;
                }
                case TokenKind.Integer:
                case TokenKind.Real:
                    items.Add((false, itemStart, itemEnd, chars, 0));
                    break;
                case TokenKind.Eof:
                    return (null, 0);
                default:
                    return (null, 0); // hex string or anything else — not addressable here
            }
        }
        if (chars == 0) return (null, 0);
        return (items, chars);
    }

    /// <summary>Emit the items whose characters fall in [from, to) as one array. A kern number
    /// belongs to the group its FOLLOWING glyph is in, which is where the cursor sits.</summary>
    private static void EmitGroup(StringBuilder outSb, List<(bool isString, int start, int end, int charStart, int charLen)> items, byte[] original,
        int from, int to)
    {
        outSb.Append('[');
        foreach (var it in items)
        {
            if (!it.isString)
            {
                if (it.charStart >= from && it.charStart < to) AppendRaw(outSb, original, it.start, it.end);
                continue;
            }
            int s = Math.Max(it.charStart, from);
            int e = Math.Min(it.charStart + it.charLen, to);
            if (e <= s) continue;
            outSb.Append('(');
            AppendRaw(outSb, original, it.start + (s - it.charStart), it.start + (e - it.charStart));
            outSb.Append(')');
        }
        outSb.Append(']');
    }

    /// <summary>Copy a byte range of the original operand into the output verbatim.</summary>
    private static void AppendRaw(StringBuilder outSb, byte[] src, int from, int to)
    {
        for (int i = from; i < to; i++) outSb.Append((char)src[i]);
    }
}
