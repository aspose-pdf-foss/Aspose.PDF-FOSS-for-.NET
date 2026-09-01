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
        var lexer = new PdfLexer(stream);
        var result = new MemoryStream();
        var removals = new List<(int start, int end)>();
        var operands = new List<PdfObject>();

        // CTM state — initialized to the page rotation matrix
        var ctmStack = new Stack<(double a, double b, double c, double d, double e, double f)>();
        double ctmA = rA, ctmB = rB, ctmC = rC, ctmD = rD, ctmE = rE, ctmF = rF;

        // Path construction state — track byte offset of first path operator and all points
        var pathStart = -1;
        var pathPoints = new List<(double x, double y)>();

        // Text block state — track BT offset, text positions, and text matrix components
        var btStart = -1;
        var textPoints = new List<(double x, double y)>();
        double tx = 0, ty = 0, txLine = 0, tyLine = 0;
        double tmA = 1, tmB = 0, tmC = 0, tmD = 1, leading = 0;

        while (true)
        {
            var tokenStart = (int)lexer.Position;
            var token = lexer.NextToken();
            if (token.Kind == TokenKind.Eof) break;

            switch (token.Kind)
            {
                case TokenKind.Integer:
                    operands.Add(new PdfInteger(token.IntValue));
                    break;
                case TokenKind.Real:
                    operands.Add(new PdfReal(token.RealValue));
                    break;
                case TokenKind.LiteralString:
                    operands.Add(new PdfString(token.BytesValue!));
                    break;
                case TokenKind.HexString:
                    operands.Add(new PdfString(token.BytesValue!, isHex: true));
                    break;
                case TokenKind.Name:
                    operands.Add(new PdfName(token.StringValue!));
                    break;
                case TokenKind.ArrayStart:
                    operands.Add(ParseArray(lexer));
                    break;
                case TokenKind.Keyword:
                    HandleFilterOperator(token.StringValue!, operands, tokenStart, (int)lexer.Position,
                        ref ctmA, ref ctmB, ref ctmC, ref ctmD, ref ctmE, ref ctmF, ctmStack,
                        ref pathStart, pathPoints, ref btStart, textPoints,
                        ref tx, ref ty, ref txLine, ref tyLine,
                        ref tmA, ref tmB, ref tmC, ref tmD, ref leading,
                        tableRect, removals, lexer, wholeBlocksOnly, textOnly, decorationOnly);
                    operands.Clear();
                    break;
                default:
                    operands.Clear();
                    break;
            }
        }

        if (removals.Count == 0) return stream;
        return ApplyRemovals(stream, removals, result);
    }

    /// <summary>
    /// Dispatches a single PDF operator during content stream filtering.
    /// Updates CTM/text matrix state and records byte ranges to remove.
    /// </summary>
    private static void HandleFilterOperator(
        string op, List<PdfObject> operands, int tokenStart, int tokenEnd,
        ref double ctmA, ref double ctmB, ref double ctmC, ref double ctmD, ref double ctmE, ref double ctmF,
        Stack<(double a, double b, double c, double d, double e, double f)> ctmStack,
        ref int pathStart, List<(double x, double y)> pathPoints,
        ref int btStart, List<(double x, double y)> textPoints,
        ref double tx, ref double ty, ref double txLine, ref double tyLine,
        ref double tmA, ref double tmB, ref double tmC, ref double tmD, ref double leading,
        Rectangle tableRect, List<(int start, int end)> removals, PdfLexer lexer,
        bool wholeBlocksOnly, bool textOnly, bool decorationOnly)
    {
        switch (op)
        {
            // ── Graphics state ──
            case "q":
                ctmStack.Push((ctmA, ctmB, ctmC, ctmD, ctmE, ctmF));
                break;
            case "Q":
                if (ctmStack.Count > 0)
                    (ctmA, ctmB, ctmC, ctmD, ctmE, ctmF) = ctmStack.Pop();
                break;
            case "cm":
                // Concatenate matrix: CTM' = operand × CTM (PDF 32000 §8.3.4)
                if (operands.Count >= 6)
                    ConcatenateCtm(operands, ref ctmA, ref ctmB, ref ctmC, ref ctmD, ref ctmE, ref ctmF);
                break;

            // ── Path construction (PDF 32000 §8.5.2) ──
            case "m" or "l":
                if (pathStart < 0) pathStart = tokenStart;
                if (operands.Count >= 2)
                    pathPoints.Add(TransformPoint(operands[0], operands[1], ctmA, ctmB, ctmC, ctmD, ctmE, ctmF));
                break;
            case "re":
                // Rectangle: add opposite corners to capture the full extent
                if (pathStart < 0) pathStart = tokenStart;
                if (operands.Count >= 4)
                {
                    var rx = Num(operands[0]); var ry = Num(operands[1]);
                    var rw = Num(operands[2]); var rh = Num(operands[3]);
                    pathPoints.Add(ApplyMatrix(rx, ry, ctmA, ctmB, ctmC, ctmD, ctmE, ctmF));
                    pathPoints.Add(ApplyMatrix(rx + rw, ry + rh, ctmA, ctmB, ctmC, ctmD, ctmE, ctmF));
                }
                break;
            case "c" or "v" or "y":
                // Curve operators — only the endpoint matters for hit testing
                if (pathStart < 0) pathStart = tokenStart;
                if (operands.Count >= 2)
                    pathPoints.Add(TransformPoint(operands[^2], operands[^1], ctmA, ctmB, ctmC, ctmD, ctmE, ctmF));
                break;
            case "h":
                if (pathStart < 0) pathStart = tokenStart;
                break;

            // ── Path painting — finalize and check if path falls inside table ──
            case "S" or "s" or "f" or "F" or "f*" or "B" or "B*" or "b" or "b*" or "n":
                // textOnly: the caller is sweeping up TEXT a delete could not match, so
                // the drawing around it stays. A path is dropped for merely GRAZING the
                // rectangle (any one point inside), which makes a rule running under a
                // line of text — its top edge a fraction of a point inside the band —
                // collateral damage of replacing that line.
                // decorationOnly: the caller is removing a rule that BELONGS to the text
                // it just replaced, so the path must lie WHOLLY inside that text's band.
                // A rule belonging to a line cannot be wider than the line; a page rule
                // running under it overruns the band and stays.
                if (!textOnly && pathStart >= 0 && pathPoints.Count > 0
                    && (decorationOnly
                        ? AllPointsInRect(pathPoints, tableRect)
                        : AnyPointInRect(pathPoints, tableRect)))
                    removals.Add((pathStart, tokenEnd));
                pathStart = -1;
                pathPoints.Clear();
                break;

            case "W" or "W*":
                // Clipping — preserve as-is
                break;

            // ── Text block (PDF 32000 §9.4) ──
            case "BT":
                btStart = tokenStart;
                textPoints.Clear();
                tx = txLine = ty = tyLine = 0;
                tmA = 1; tmB = 0; tmC = 0; tmD = 1;
                leading = 0;
                break;
            case "ET":
                if (!decorationOnly && btStart >= 0 && textPoints.Count > 0
                    && (wholeBlocksOnly
                        ? AllPointsInRect(textPoints, tableRect)
                        : AnyPointInRect(textPoints, tableRect)))
                    removals.Add((btStart, tokenEnd));
                btStart = -1;
                textPoints.Clear();
                break;

            // ── Text positioning operators ──
            case "TL":
                if (operands.Count >= 1) leading = Num(operands[0]);
                break;
            case "Td":
                if (operands.Count >= 2)
                    UpdateTextPosition(operands, ref tx, ref ty, ref txLine, ref tyLine, tmA, tmB, tmC, tmD);
                break;
            case "TD":
                // TD sets leading and moves — equivalent to: -ty2 TL tx ty Td
                if (operands.Count >= 2)
                {
                    leading = -Num(operands[1]);
                    UpdateTextPosition(operands, ref tx, ref ty, ref txLine, ref tyLine, tmA, tmB, tmC, tmD);
                }
                break;
            case "T*":
                // Move to start of next line using current leading
                txLine = tmC * (-leading) + txLine;
                tyLine = tmD * (-leading) + tyLine;
                tx = txLine; ty = tyLine;
                break;
            case "Tm":
                if (operands.Count >= 6)
                {
                    tmA = Num(operands[0]); tmB = Num(operands[1]);
                    tmC = Num(operands[2]); tmD = Num(operands[3]);
                    tx = txLine = Num(operands[4]);
                    ty = tyLine = Num(operands[5]);
                }
                break;

            // ── Text showing — record the current text position ──
            case "Tj" or "TJ" or "'" or "\"":
                if (btStart >= 0)
                    textPoints.Add(ApplyMatrix(tx, ty, ctmA, ctmB, ctmC, ctmD, ctmE, ctmF));
                break;

            case "BI":
                SkipInlineImage(lexer);
                break;
        }
    }

    /// <summary>
    /// Concatenates a 6-element matrix from operands into the current CTM.
    /// Formula: CTM' = M × CTM (PDF 32000 §8.3.4).
    /// </summary>
    private static void ConcatenateCtm(List<PdfObject> operands,
        ref double ctmA, ref double ctmB, ref double ctmC, ref double ctmD, ref double ctmE, ref double ctmF)
    {
        var a = Num(operands[0]); var b = Num(operands[1]);
        var c = Num(operands[2]); var d = Num(operands[3]);
        var e = Num(operands[4]); var f = Num(operands[5]);
        var nA = a * ctmA + b * ctmC;
        var nB = a * ctmB + b * ctmD;
        var nC = c * ctmA + d * ctmC;
        var nD = c * ctmB + d * ctmD;
        var nE = e * ctmA + f * ctmC + ctmE;
        var nF = e * ctmB + f * ctmD + ctmF;
        ctmA = nA; ctmB = nB; ctmC = nC; ctmD = nD; ctmE = nE; ctmF = nF;
    }

    /// <summary>Applies Td/TD text position update using the text matrix.</summary>
    private static void UpdateTextPosition(List<PdfObject> operands,
        ref double tx, ref double ty, ref double txLine, ref double tyLine,
        double tmA, double tmB, double tmC, double tmD)
    {
        var dx = Num(operands[0]); var dy = Num(operands[1]);
        txLine = tmA * dx + tmC * dy + txLine;
        tyLine = tmB * dx + tmD * dy + tyLine;
        tx = txLine; ty = tyLine;
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
