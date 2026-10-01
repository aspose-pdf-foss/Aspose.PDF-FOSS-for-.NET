using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TableAbsorber
{
    internal static void RemoveTableContent(Page page, Rectangle tableRect, bool wholeBlocksOnly = false, bool textOnly = false, bool decorationOnly = false)
    {
        var reader = page.Reader;
        var contentStreams = GetContentStreams(page.Dict, reader);
        if (contentStreams.Count == 0) return;
        using var combined = new MemoryStream();
        foreach (var cs in contentStreams)
        {
            if (combined.Length > 0) combined.WriteByte((byte)'\n');
            combined.Write(cs);
        }
        var allBytes = combined.ToArray();
        var filtered = FilterContentStream(allBytes, tableRect, page, wholeBlocksOnly, textOnly, decorationOnly);
        page.SetContentStream(filtered);
    }

    /// <summary>
    /// Removes graphics and text from the content stream that fall inside <paramref name="tableRect"/>.
    /// Walks the stream token-by-token, tracking the current transformation matrix (CTM) and
    /// text matrix so that path/text coordinates can be mapped to page space. Any path or BT…ET
    /// block whose transformed points lie inside the table rectangle is stripped out.
    /// </summary>
    private static byte[] FilterContentStream(byte[] stream, Rectangle tableRect, Page page, bool wholeBlocksOnly, bool textOnly, bool decorationOnly)
    {
        var (rA, rB, rC, rD, rE, rF) = PageRotationCtm(page);
        var cf = new ContentFilterState();
        cf.lexer = new PdfLexer(stream);
        var result = new MemoryStream();
        cf.removals = new List<(int start, int end)>();
        cf.operands = new List<PdfObject>();
        cf.ctmStack = new Stack<(double a, double b, double c, double d, double e, double f)>();
        cf.ctmA = rA; cf.ctmB = rB; cf.ctmC = rC; cf.ctmD = rD; cf.ctmE = rE; cf.ctmF = rF;
        cf.pathStart = -1;
        cf.pathPoints = new List<(double x, double y)>();
        cf.btStart = -1;
        cf.textPoints = new List<(double x, double y)>();
        cf.tx = 0; cf.ty = 0; cf.txLine = 0; cf.tyLine = 0;
        cf.tmA = 1; cf.tmB = 0; cf.tmC = 0; cf.tmD = 1; cf.leading = 0;
        cf.tableRect = tableRect;
        cf.wholeBlocksOnly = wholeBlocksOnly;
        cf.textOnly = textOnly;
        cf.decorationOnly = decorationOnly;

        while (true)
        {
            var tokenStart = (int)cf.lexer.Position;
            var token = cf.lexer.NextToken();
            if (token.Kind == TokenKind.Eof) break;

            switch (token.Kind)
            {
                case TokenKind.Integer:
                    cf.operands.Add(new PdfInteger(token.IntValue));
                    break;
                case TokenKind.Real:
                    cf.operands.Add(new PdfReal(token.RealValue));
                    break;
                case TokenKind.LiteralString:
                    cf.operands.Add(new PdfString(token.BytesValue!));
                    break;
                case TokenKind.HexString:
                    cf.operands.Add(new PdfString(token.BytesValue!, isHex: true));
                    break;
                case TokenKind.Name:
                    cf.operands.Add(new PdfName(token.StringValue!));
                    break;
                case TokenKind.ArrayStart:
                    cf.operands.Add(ParseArray(cf.lexer));
                    break;
                case TokenKind.Keyword:
                    HandleFilterOperator(cf, token.StringValue!, tokenStart, (int)cf.lexer.Position);
                    cf.operands.Clear();
                    break;
                default:
                    cf.operands.Clear();
                    break;
            }
        }

        if (cf.removals.Count == 0) return stream;
        return ApplyRemovals(stream, cf.removals, result);
    }

    /// <summary>
    /// Dispatches a single PDF operator during content stream filtering.
    /// Updates CTM/text matrix state and records byte ranges to remove.
    /// </summary>
    private static void HandleFilterOperator(ContentFilterState cf, string op, int tokenStart, int tokenEnd)
    {
        switch (op)
        {
            case "q": case "Q": case "cm":
                FilterStateOperator(cf, op);
                break;
            case "m": case "l": case "re": case "c": case "v": case "y": case "h": case "S": case "s": case "f": case "F": case "f*": case "B": case "B*": case "b": case "b*": case "n": case "W": case "W*":
                FilterPathOperator(cf, tokenStart, tokenEnd, op);
                break;
            case "BT": case "ET": case "TL": case "Td": case "TD": case "T*": case "Tm":
                FilterTextOperator(cf, tokenStart, tokenEnd, op);
                break;
            // ── Text showing — record the current text position ──
            case "Tj" or "TJ" or "'" or "\"":
                if (cf.btStart >= 0)
                    cf.textPoints.Add(ApplyMatrix(cf.tx, cf.ty, cf.ctmA, cf.ctmB, cf.ctmC, cf.ctmD, cf.ctmE, cf.ctmF));
                break;

            case "BI":
                SkipInlineImage(cf.lexer);
                break;
        }
    }

    /// <summary>
    /// Concatenates a 6-element matrix from operands into the current CTM.
    /// Formula: CTM' = M × CTM (PDF 32000 §8.3.4).
    /// </summary>
    private static void ConcatenateCtm(ContentFilterState cf)
    {
        var a = Num(cf.operands[0]); var b = Num(cf.operands[1]);
        var c = Num(cf.operands[2]); var d = Num(cf.operands[3]);
        var e = Num(cf.operands[4]); var f = Num(cf.operands[5]);
        var nA = a * cf.ctmA + b * cf.ctmC;
        var nB = a * cf.ctmB + b * cf.ctmD;
        var nC = c * cf.ctmA + d * cf.ctmC;
        var nD = c * cf.ctmB + d * cf.ctmD;
        var nE = e * cf.ctmA + f * cf.ctmC + cf.ctmE;
        var nF = e * cf.ctmB + f * cf.ctmD + cf.ctmF;
        cf.ctmA = nA; cf.ctmB = nB; cf.ctmC = nC; cf.ctmD = nD; cf.ctmE = nE; cf.ctmF = nF;
    }

    /// <summary>Applies Td/TD text position update using the text matrix.</summary>
    private static void UpdateTextPosition(ContentFilterState cf)
    {
        var dx = Num(cf.operands[0]); var dy = Num(cf.operands[1]);
        cf.txLine = cf.tmA * dx + cf.tmC * dy + cf.txLine;
        cf.tyLine = cf.tmB * dx + cf.tmD * dy + cf.tyLine;
        cf.tx = cf.txLine; cf.ty = cf.tyLine;
    }

    /// <summary>
    /// Removes byte ranges from <paramref name="stream"/> by merging overlapping removals
    /// and writing only the surviving segments.
    /// </summary>
    private static byte[] ApplyRemovals(byte[] stream, List<(int start, int end)> removals, MemoryStream result)
    {
        removals.Sort((a, b) => a.start.CompareTo(b.start));

        // Merge overlapping/adjacent removal ranges
        var merged = new List<(int start, int end)> { removals[0] };
        for (var i = 1; i < removals.Count; i++)
        {
            var last = merged[^1];
            if (removals[i].start <= last.end)
                merged[^1] = (last.start, Math.Max(last.end, removals[i].end));
            else
                merged.Add(removals[i]);
        }

        // Write only the bytes outside merged removal ranges
        var pos = 0;
        foreach (var (start, end) in merged)
        {
            if (start > pos)
                result.Write(stream, pos, start - pos);
            pos = end;
        }
        if (pos < stream.Length)
            result.Write(stream, pos, stream.Length - pos);

        return result.ToArray();
    }

    /// <summary>Returns true if any point falls within the rectangle (with tolerance margin).</summary>
    /// <summary>True when EVERY point lies inside the rectangle — the test for a
    /// sweep that must not take neighbouring text drawn in the same BT…ET block
    /// with it.</summary>
    private static bool AllPointsInRect(List<(double x, double y)> points, Rectangle rect)
    {
        foreach (var (x, y) in points)
            if (x < rect.LLX || x > rect.URX || y < rect.LLY || y > rect.URY) return false;
        return points.Count > 0;
    }

    private static bool AnyPointInRect(List<(double x, double y)> points, Rectangle rect)
    {
        const double margin = 10.0;
        foreach (var (x, y) in points)
        {
            if (x >= rect.LLX - margin && x <= rect.URX + margin &&
                y >= rect.LLY - margin && y <= rect.URY + margin)
                return true;
        }
        return false;
    }
}
