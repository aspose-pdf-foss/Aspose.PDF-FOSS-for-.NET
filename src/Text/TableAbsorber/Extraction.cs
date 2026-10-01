using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TableAbsorber
{
    /// <summary>Buffered line segment within a path being constructed.</summary>
    private readonly record struct PendingLine(double X1, double Y1, double X2, double Y2);

    /// <summary>Buffered rect within a path being constructed.</summary>
    private readonly record struct PendingRect(double X, double Y, double W, double H);

    private static void ExtractTextAndLines(byte[] stream, Dictionary<string, PdfDictionary> fonts,
        PdfReader reader, List<TextRun> textRuns, List<HEdge> hEdges, List<VEdge> vEdges,
        double ctmA = 1, double ctmB = 0, double ctmC = 0, double ctmD = 1, double ctmE = 0, double ctmF = 0,
        PdfDictionary? xobjects = null, int depth = 0)
    {
        var tl = new TableLinesState();
        tl.lexer = new PdfLexer(stream);
        tl.operands = new List<PdfObject>();
        tl.toUnicode = null;
        tl.fontDict = null;
        tl.curMetrics = null;
        tl.fontSize = 12;
        tl.tx = 0;
        tl.ty = 0;
        tl.txLine = 0;
        tl.tyLine = 0;
        tl.tmA = 1;
        tl.tmB = 0;
        tl.tmC = 0;
        tl.tmD = 1;
        tl.leading = 0;
        tl.curX = 0;
        tl.curY = 0;
        tl.moveX = 0;
        tl.moveY = 0;
        tl.ctmStack = new Stack<(double a, double b, double c, double d, double e, double f)>();
        // The CTM this stream is drawn under: the identity for a page, the form's
        // own matrix for an XObject the walk recursed into.
        tl.ctmA = ctmA;
        tl.ctmB = ctmB;
        tl.ctmC = ctmC;
        tl.ctmD = ctmD;
        tl.ctmE = ctmE;
        tl.ctmF = ctmF;

        tl.pendingLines = new List<PendingLine>();
        tl.pendingRects = new List<PendingRect>();

        while (true)
        {
            var token = tl.lexer.NextToken(); if (token.Kind == TokenKind.Eof) break;
            switch (token.Kind)
            {
                case TokenKind.Integer: tl.operands.Add(new PdfInteger(token.IntValue)); break;
                case TokenKind.Real: tl.operands.Add(new PdfReal(token.RealValue)); break;
                case TokenKind.LiteralString: tl.operands.Add(new PdfString(token.BytesValue!)); break;
                case TokenKind.HexString: tl.operands.Add(new PdfString(token.BytesValue!, isHex: true)); break;
                case TokenKind.Name: tl.operands.Add(new PdfName(token.StringValue!)); break;
                case TokenKind.ArrayStart: tl.operands.Add(ParseArray(tl.lexer)); break;
                case TokenKind.Keyword:
                {
                    var op = token.StringValue!;
                    switch (op)
                    {
                        case "q": case "Q": case "cm": case "Do":
                            ExtractLinesStateOperator(tl, fonts, reader, textRuns, hEdges, vEdges, xobjects, depth, op);
                            break;
                        case "BT": case "TL": case "Tf": case "Td": case "TD": case "T*": case "Tm": case "Tj": case "TJ":
                            ExtractLinesTextOperator(tl, fonts, reader, textRuns, op);
                            break;
                        case "m": case "l": case "h": case "re": case "S": case "s": case "f": case "F": case "f*": case "B": case "B*": case "b": case "b*": case "n": case "W": case "W*":
                            ExtractLinesPathOperator(tl, hEdges, vEdges, op);
                            break;
                        case "BI": SkipInlineImage(tl.lexer); tl.operands.Clear(); continue;
                    }
                    tl.operands.Clear(); break;
                }
                default: tl.operands.Clear(); break;
            }
        }
    }

    /// <summary>Flush buffered path segments into H/V edge lists, applying the TS collectEdges logic.</summary>
    private static void FlushPendingEdges(List<PendingLine> lines, List<PendingRect> rects,
        List<HEdge> hEdges, List<VEdge> vEdges, bool stroked)
    {
        // Process line segments
        foreach (var line in lines)
        {
            var dy = Math.Abs(line.Y2 - line.Y1);
            var dx = Math.Abs(line.X2 - line.X1);
            if (dy <= LineRectThreshold && dx > LineRectThreshold)
                hEdges.Add(new HEdge((line.Y1 + line.Y2) / 2, Math.Min(line.X1, line.X2), Math.Max(line.X1, line.X2)));
            else if (dx <= LineRectThreshold && dy > LineRectThreshold)
                vEdges.Add(new VEdge((line.X1 + line.X2) / 2, Math.Min(line.Y1, line.Y2), Math.Max(line.Y1, line.Y2)));
        }

        // Process rects (matching TS collectEdges logic)
        foreach (var rect in rects)
        {
            var (x, y, w, h) = (rect.X, rect.Y, rect.W, rect.H);

            // Thin filled rects → treat as line borders. The edge coordinate is the
            // rect's PATH ORIGIN (x, y), not its centre — table
            // rectangles are anchored on the bar origins (a 0.48pt border bar at y=723.22
            // puts the table top at 723.22, not 723.46).
            if (h <= LineRectThreshold && w >= MinCellW)
            {
                hEdges.Add(new HEdge(y, x, x + w));
                continue;
            }
            if (w <= LineRectThreshold && h >= MinCellH)
            {
                vEdges.Add(new VEdge(x, y, y + h));
                continue;
            }

            // A STROKED rectangle is a drawn box: all four of its edges are rules. A
            // FILLED one of cell size is a background - a shaded cell, the tinted
            // band behind a heading paragraph - and contributes no rule: a Word
            // table whose header cells carry per-paragraph shading reads as the
            // outer 4 x 2 grid, not the shading boxes' sub-grid.
            if (stroked && w >= MinCellW && h >= MinCellH)
            {
                hEdges.Add(new HEdge(y + h, x, x + w)); // top
                hEdges.Add(new HEdge(y, x, x + w));     // bottom
                vEdges.Add(new VEdge(x, y, y + h));      // left
                vEdges.Add(new VEdge(x + w, y, y + h));  // right
            }
        }
    }

    private static string Decode(byte[] bytes, Dictionary<int, string>? toUnicode, PdfDictionary? fontDict)
    {
        if (toUnicode is not null)
        {
            var isCid = fontDict?.GetName("Subtype") == "Type0"; var sb = new StringBuilder();
            if (isCid && bytes.Length >= 2) for (var i = 0; i + 1 < bytes.Length; i += 2) { var code = (bytes[i] << 8) | bytes[i + 1]; sb.Append(toUnicode.TryGetValue(code, out var m) ? m : "\uFFFD"); }
            else foreach (var b in bytes) sb.Append(toUnicode.TryGetValue(b, out var m) ? m : ((char)b).ToString());
            return sb.ToString();
        }
        return Compat.Latin1.GetString(bytes);
    }

    private static Dictionary<string, PdfDictionary> ResolveFonts(PdfDictionary pageDict, PdfReader reader)
        => TextAbsorber.ResolveFonts(pageDict, reader);

    private static byte[] ConcatenateStreams(List<byte[]> streams)
    {
        if (streams.Count == 1) return streams[0];
        using var ms = new MemoryStream();
        foreach (var s in streams) { if (ms.Length > 0) ms.WriteByte((byte)'\n'); ms.Write(s); }
        return ms.ToArray();
    }

    private static List<byte[]> GetContentStreams(PdfDictionary pageDict, PdfReader reader)
    {
        var result = new List<byte[]>();
        var obj = reader.Resolve(pageDict.Get("Contents"));
        if (obj is PdfStream stream) result.Add(reader.DecodeStream(stream));
        else if (obj is PdfArray arr) foreach (var item in arr) { var s = reader.ResolveStream(item); if (s is not null) result.Add(reader.DecodeStream(s)); }
        return result;
    }

    // Delegate to the shared implementation, which sizes Flate-compressed inline images by
    // inflate-probing the "EI" candidates instead of a fragile byte scan that desyncs the
    // lexer on binary data.
    private static void SkipInlineImage(PdfLexer lexer) => TextAbsorber.SkipInlineImage(lexer);

    private static PdfArray ParseArray(PdfLexer lexer)
    {
        var arr = new PdfArray();
        while (true) { var t = lexer.NextToken(); if (t.Kind == TokenKind.ArrayEnd || t.Kind == TokenKind.Eof) break;
            switch (t.Kind) { case TokenKind.Integer: arr.Add(new PdfInteger(t.IntValue)); break; case TokenKind.Real: arr.Add(new PdfReal(t.RealValue)); break;
                case TokenKind.LiteralString: arr.Add(new PdfString(t.BytesValue!)); break; case TokenKind.HexString: arr.Add(new PdfString(t.BytesValue!, isHex: true)); break;
                case TokenKind.Name: arr.Add(new PdfName(t.StringValue!)); break; } }
        return arr;
    }

    private static double Num(PdfObject obj) => obj switch { PdfInteger i => i.Value, PdfReal r => r.Value, _ => 0 };
}
