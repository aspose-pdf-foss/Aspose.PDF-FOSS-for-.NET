using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>Consumes one content token: whitespace, delimiters, comments, strings, dictionaries and names advance the operand bookkeeping; an operator ends it, a Tf switches the encoding, a show operator whose glyphs are all notdef is queued for deletion. False when an inline image makes the stream unsafe to rewrite.</summary>
    private bool ScanNotdefToken(NotdefRewriteState nd)
    {
        var c = nd.text[nd.pos];
        if (char.IsWhiteSpace(c)) { nd.pos++; return true; }
        if (c is '[' or ']' or '{' or '}') { BeginOperand(nd, nd.pos); nd.pos++; return true; }
        if (c == '%') // comment to end-of-line
        {
            while (nd.pos < nd.text.Length && nd.text[nd.pos] != '\n' && nd.text[nd.pos] != '\r') nd.pos++;
            return true;
        }
        if (SkipNotdefLiteralString(nd, c)) return true;
        if (c == '<')
        {
            if (nd.pos + 1 < nd.text.Length && nd.text[nd.pos + 1] == '<') // dict
            { BeginOperand(nd, nd.pos); nd.pos += 2; return true; }
            BeginOperand(nd, nd.pos);
            var end = nd.text.IndexOf('>', nd.pos + 1);
            if (end < 0) end = nd.text.Length - 1;
            nd.strings.Add(DecodeHexStringBytes(nd.text, nd.pos + 1, end));
            nd.pos = end + 1; return true;
        }
        if (SkipNotdefDictEnd(nd, c)) return true;

        // Regular token (number or operator).
        var tokEnd = nd.pos;
        while (tokEnd < nd.text.Length && !char.IsWhiteSpace(nd.text[tokEnd])
               && nd.text[tokEnd] is not ('/' or '(' or ')' or '<' or '>' or '[' or ']' or '{' or '}' or '%'))
            tokEnd++;
        // A stray delimiter byte (an unbalanced ')' or a lone '>') yields an
        // empty token with tokEnd == pos — skip the byte or the scan never advances.
        if (tokEnd == nd.pos) { nd.pos++; return true; }
        var isNumber = Compat.IsAsciiDigit(c) || c is '+' or '-' or '.';
        if (isNumber) { BeginOperand(nd, nd.pos); nd.pos = tokEnd; return true; }
        // Only four operators matter here, read in place: a stream is mostly other tokens.
        if (TokenIs(nd, tokEnd, "BI")) { nd.inlineImage = true; return false; } // inline image: bail out, keep original bytes
        if (TokenIs(nd, tokEnd, "Tf")) nd.currentFont = nd.lastName;
        else if ((TokenIs(nd, tokEnd, "Tj") || TokenIs(nd, tokEnd, "TJ")) && nd.strings.Count > 0 && nd.currentFont is not null
            && EncodingFor(nd, nd.currentFont) is { } names)
        {
            var sawCode = false;
            var allNotdef = true;
            foreach (var s in nd.strings)
                foreach (var b in s)
                {
                    sawCode = true;
                    if (b >= 0x20 || names[b] is not (null or ".notdef"))
                    { allNotdef = false; break; }
                }
            if (sawCode && allNotdef)
            {
                nd.options.ConversionLog.Add(new PdfAViolation
                {
                    Rule = "NotdefGlyph",
                    Description = $"Page {nd.pageNumber} text show operator references only the .notdef glyph"
                        + (nd.strip ? " — operator removed." : "."),
                    PageNumber = nd.pageNumber,
                });
                if (nd.strip && nd.operandStart >= 0)
                    nd.deletions.Add((nd.operandStart, tokEnd, null));
            }
        }
        // A composite font reported for showing its .notdef glyph: the log already names it,
        // and a show of nothing else gives way to the advance it made.
        else if (_notdefCidFonts is not null && nd.strip && nd.operandStart >= 0 && nd.currentFont is not null
            && (TokenIs(nd, tokEnd, "Tj") || TokenIs(nd, tokEnd, "TJ"))
            && NotdefCidAdvance(nd, nd.currentFont) is { } advance)
            nd.deletions.Add((nd.operandStart, tokEnd, advance));
        EndOperator(nd);
        nd.pos = tokEnd;
        return true;
    }

    /// <summary>Whether the token ending at <paramref name="tokEnd"/> is the operator <paramref name="op"/>.</summary>
    private static bool TokenIs(NotdefRewriteState nd, int tokEnd, string op)
        => tokEnd - nd.pos == op.Length && string.CompareOrdinal(nd.text, nd.pos, op, 0, op.Length) == 0;

    /// <summary>A '>>' dictionary end or a '&lt;' token: dictionary opens and ends are bookkept as operands, a hex string is consumed whole as a string operand. True when the token was consumed.</summary>
    private static bool SkipNotdefDictEnd(NotdefRewriteState nd, char c)
    {
        if (c == '>' && nd.pos + 1 < nd.text.Length && nd.text[nd.pos + 1] == '>')
        { BeginOperand(nd, nd.pos); nd.pos += 2; return true; }
        if (c == '/') // name token
        {
            BeginOperand(nd, nd.pos);
            var end = nd.pos + 1;
            while (end < nd.text.Length && !char.IsWhiteSpace(nd.text[end])
                   && nd.text[end] is not ('/' or '(' or ')' or '<' or '>' or '[' or ']' or '{' or '}' or '%'))
                end++;
            nd.lastName = nd.text[(nd.pos + 1)..end];
            nd.pos = end; return true;
        }
        return false;
    }

    /// <summary>A literal string operand: consumed with its escapes and balanced parentheses and gathered for the show that follows. True when the token was consumed.</summary>
    private static bool SkipNotdefLiteralString(NotdefRewriteState nd, char c)
    {
        if (c == '(') // literal string, with escapes and balanced parens
        {
            BeginOperand(nd, nd.pos);
            var end = nd.pos + 1;
            var depth = 1;
            while (end < nd.text.Length && depth > 0)
            {
                var sc = nd.text[end];
                if (sc == '\\') end++;
                else if (sc == '(') depth++;
                else if (sc == ')') depth--;
                end++;
            }
            nd.strings.Add(DecodeLiteralStringBytes(nd.text, nd.pos + 1, end - 1));
            nd.pos = end; return true;
        }
        return false;
    }
}
