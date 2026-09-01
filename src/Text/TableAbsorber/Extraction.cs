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
        var lexer = new PdfLexer(stream); var operands = new List<PdfObject>();
        Dictionary<int, string>? toUnicode = null; PdfDictionary? fontDict = null;
        FontMetrics? curMetrics = null;
        double fontSize = 12, tx = 0, ty = 0, txLine = 0, tyLine = 0;
        double tmA = 1, tmB = 0, tmC = 0, tmD = 1, leading = 0;
        double curX = 0, curY = 0, moveX = 0, moveY = 0;
        var ctmStack = new Stack<(double a, double b, double c, double d, double e, double f)>();

        // Buffer path segments until a paint operator finalizes them
        var pendingLines = new List<PendingLine>();
        var pendingRects = new List<PendingRect>();

        while (true)
        {
            var token = lexer.NextToken(); if (token.Kind == TokenKind.Eof) break;
            switch (token.Kind)
            {
                case TokenKind.Integer: operands.Add(new PdfInteger(token.IntValue)); break;
                case TokenKind.Real: operands.Add(new PdfReal(token.RealValue)); break;
                case TokenKind.LiteralString: operands.Add(new PdfString(token.BytesValue!)); break;
                case TokenKind.HexString: operands.Add(new PdfString(token.BytesValue!, isHex: true)); break;
                case TokenKind.Name: operands.Add(new PdfName(token.StringValue!)); break;
                case TokenKind.ArrayStart: operands.Add(ParseArray(lexer)); break;
                case TokenKind.Keyword:
                {
                    var op = token.StringValue!;
                    switch (op)
                    {
                        case "q": ctmStack.Push((ctmA,ctmB,ctmC,ctmD,ctmE,ctmF)); break;
                        case "Q": if (ctmStack.Count > 0) (ctmA,ctmB,ctmC,ctmD,ctmE,ctmF) = ctmStack.Pop(); break;
                        case "cm":
                            if (operands.Count >= 6)
                            { var a=Num(operands[0]);var b=Num(operands[1]);var c=Num(operands[2]);var d=Num(operands[3]);var e=Num(operands[4]);var f=Num(operands[5]);
                              var nA=a*ctmA+b*ctmC;var nB=a*ctmB+b*ctmD;var nC=c*ctmA+d*ctmC;var nD=c*ctmB+d*ctmD;var nE=e*ctmA+f*ctmC+ctmE;var nF=e*ctmB+f*ctmD+ctmF;
                              ctmA=nA;ctmB=nB;ctmC=nC;ctmD=nD;ctmE=nE;ctmF=nF; }
                            break;
                        case "BT": tx=txLine=0;ty=tyLine=0;tmA=1;tmB=0;tmC=0;tmD=1;leading=0; break;
                        case "TL": if (operands.Count>=1) leading=Num(operands[0]); break;
                        case "Tf":
                            if (operands.Count>=1&&operands[0] is PdfName fn&&fonts.TryGetValue(fn.Value,out var fd)){fontDict=fd;toUnicode=TextAbsorber.ParseToUnicodeFromDict(fd,reader);curMetrics=null;try{curMetrics=FontMetrics.FromFontDict(fd,reader);}catch{}}
                            if (operands.Count>=2) fontSize=Math.Abs(Num(operands[1])); break;
                        case "Td": if (operands.Count>=2){var tdX=Num(operands[0]);var tdY=Num(operands[1]);txLine=tmA*tdX+tmC*tdY+txLine;tyLine=tmB*tdX+tmD*tdY+tyLine;tx=txLine;ty=tyLine;} break;
                        case "TD": if (operands.Count>=2){var tdX=Num(operands[0]);var tdY=Num(operands[1]);leading=-tdY;txLine=tmA*tdX+tmC*tdY+txLine;tyLine=tmB*tdX+tmD*tdY+tyLine;tx=txLine;ty=tyLine;} break;
                        case "T*": txLine=tmC*(-leading)+txLine;tyLine=tmD*(-leading)+tyLine;tx=txLine;ty=tyLine; break;
                        case "Tm": if (operands.Count>=6){tmA=Num(operands[0]);tmB=Num(operands[1]);tmC=Num(operands[2]);tmD=Num(operands[3]);tx=txLine=Num(operands[4]);ty=tyLine=Num(operands[5]);} break;
                        case "Tj":
                            // Estimated advance must carry the Tm and CTM scales — a
                            // `117 Tf` with a 0.12-scale Tm is ~14pt text, and an
                            // unscaled estimate would balloon the run width (and push
                            // its centre outside every cell).
                            if (operands.Count>=1&&operands[0] is PdfString s){var text=Decode(s.Value,toUnicode,fontDict);var(px,py)=ApplyMatrix(tx,ty,ctmA,ctmB,ctmC,ctmD,ctmE,ctmF);var hsX=tmA*ctmA+tmB*ctmC;var hsY=tmA*ctmB+tmB*ctmD;var hs=Math.Sqrt(hsX*hsX+hsY*hsY);var vsX=tmC*ctmA+tmD*ctmC;var vsY=tmC*ctmB+tmD*ctmD;var vs=Math.Sqrt(vsX*vsX+vsY*vsY);textRuns.Add(new TextRun(text,px,py,text.Length*fontSize*0.5*hs,fontSize*vs));} break;
                        case "TJ":
                            if (operands.Count>=1&&operands[0] is PdfArray arr)
                            {
                                // Walk the array element-by-element accumulating a text-space pen
                                // offset. A large NEGATIVE adjustment (a rightward jump ≫ kerning,
                                // e.g. the multi-em gaps a single TJ uses to lay out columns) is a
                                // column boundary: flush the run so far and start a new run at the
                                // jumped-to X. Without this, a header/row drawn as one TJ across
                                // several columns collapses into the first column.
                                var sb=new StringBuilder();
                                double pen=0;          // text-space advance from (tx,ty)
                                double runStartPen=0;  // pen at the current sub-run's first glyph
                                // Gap threshold: 1.5 em. Normal inter-glyph kerning is <0.05 em;
                                // an inter-word space char is a real glyph (not an adjustment).
                                double gapTU=fontSize*1.5;
                                void FlushSub()
                                {
                                    if (sb.Length==0) return;
                                    var txx=tx+tmA*runStartPen; var tyy=ty+tmB*runStartPen;
                                    var(px2,py2)=ApplyMatrix(txx,tyy,ctmA,ctmB,ctmC,ctmD,ctmE,ctmF);
                                    var hsX=tmA*ctmA+tmB*ctmC;var hsY=tmA*ctmB+tmB*ctmD;var hs=Math.Sqrt(hsX*hsX+hsY*hsY);
                                    var vsX=tmC*ctmA+tmD*ctmC;var vsY=tmC*ctmB+tmD*ctmD;var vs=Math.Sqrt(vsX*vsX+vsY*vsY);
                                    textRuns.Add(new TextRun(sb.ToString(),px2,py2,sb.Length*fontSize*0.5*hs,fontSize*vs));
                                    sb.Clear();
                                }
                                foreach(var item in arr)
                                {
                                    if (item is PdfString ps)
                                    {
                                        var t=Decode(ps.Value,toUnicode,fontDict);
                                        sb.Append(t);
                                        // Advance the pen by the true glyph run width when the font
                                        // metrics are available (falling back to a crude 0.5-em/char
                                        // estimate). Accurate widths keep each post-gap sub-run's X
                                        // inside its real column — a 0.5-em guess undershoots caps
                                        // headers and slides text into the wrong cell.
                                        pen+=curMetrics is not null ? curMetrics.MeasureString(t,fontSize) : t.Length*fontSize*0.5;
                                    }
                                    else if (item is PdfInteger or PdfReal)
                                    {
                                        var adv=-Num(item)/1000.0*fontSize; // +ve = rightward
                                        if (adv>gapTU) { FlushSub(); pen+=adv; runStartPen=pen; }
                                        else pen+=adv;
                                    }
                                }
                                FlushSub();
                            }
                            break;
                        case "m":
                            if (operands.Count>=2){var(px,py)=ApplyMatrix(Num(operands[0]),Num(operands[1]),ctmA,ctmB,ctmC,ctmD,ctmE,ctmF);curX=moveX=px;curY=moveY=py;} break;
                        case "l":
                            if (operands.Count>=2)
                            {
                                var(lx,ly)=ApplyMatrix(Num(operands[0]),Num(operands[1]),ctmA,ctmB,ctmC,ctmD,ctmE,ctmF);
                                pendingLines.Add(new PendingLine(curX, curY, lx, ly));
                                curX=lx;curY=ly;
                            }
                            break;
                        case "h":
                            // Close subpath: add a line from current point back to the move-to point
                            if (Math.Abs(curX - moveX) > 0.01 || Math.Abs(curY - moveY) > 0.01)
                                pendingLines.Add(new PendingLine(curX, curY, moveX, moveY));
                            curX = moveX; curY = moveY;
                            break;
                        case "re":
                            if (operands.Count>=4)
                            {
                                var rx=Num(operands[0]);var ry=Num(operands[1]);var rw=Num(operands[2]);var rh=Num(operands[3]);
                                var(p0x,p0y)=ApplyMatrix(rx,ry,ctmA,ctmB,ctmC,ctmD,ctmE,ctmF);
                                var(p2x,p2y)=ApplyMatrix(rx+rw,ry+rh,ctmA,ctmB,ctmC,ctmD,ctmE,ctmF);
                                var x=Math.Min(p0x,p2x); var y=Math.Min(p0y,p2y);
                                var w=Math.Abs(p2x-p0x); var h=Math.Abs(p2y-p0y);
                                pendingRects.Add(new PendingRect(x, y, w, h));
                            }
                            break;
                        // Paint operators: finalize buffered path segments as edges
                        case "S" or "s" or "f" or "F" or "f*" or "B" or "B*" or "b" or "b*":
                            FlushPendingEdges(pendingLines, pendingRects, hEdges, vEdges,
                                stroked: op is "S" or "s" or "B" or "B*" or "b" or "b*");
                            pendingLines.Clear(); pendingRects.Clear(); break;
                        case "n":
                            // No-paint: discard pending paths (clip-only)
                            pendingLines.Clear(); pendingRects.Clear(); break;
                        case "W" or "W*": break; // Clip modifiers don't finalize path
                        case "BI": SkipInlineImage(lexer); operands.Clear(); continue;
                        case "Do":
                            // Recurse into Form XObjects so table grids drawn inside them are
                            // extracted (nested tables are emitted as forms).
                            if (xobjects is not null && depth < 12 && operands.Count >= 1 && operands[0] is PdfName xn)
                            {
                                var form = reader.ResolveStream(xobjects.Get(xn.Value));
                                if (form is not null && form.Dict.GetName("Subtype") == "Form")
                                {
                                    byte[]? formBytes = null;
                                    try { formBytes = reader.DecodeStream(form); } catch { }
                                    if (formBytes is not null)
                                    {
                                        double fA = ctmA, fB = ctmB, fC = ctmC, fD = ctmD, fE = ctmE, fF = ctmF;
                                        if (reader.Resolve(form.Dict.Get("Matrix")) is PdfArray ma && ma.Count >= 6)
                                        {
                                            double m0=Num(ma[0]),m1=Num(ma[1]),m2=Num(ma[2]),m3=Num(ma[3]),m4=Num(ma[4]),m5=Num(ma[5]);
                                            fA=m0*ctmA+m1*ctmC; fB=m0*ctmB+m1*ctmD; fC=m2*ctmA+m3*ctmC; fD=m2*ctmB+m3*ctmD;
                                            fE=m4*ctmA+m5*ctmC+ctmE; fF=m4*ctmB+m5*ctmD+ctmF;
                                        }
                                        var formFonts = TextAbsorber.ResolveFonts(form.Dict, reader);
                                        foreach (var kv in fonts) formFonts.TryAdd(kv.Key, kv.Value);
                                        var formXObjects = TextAbsorber.ResolveXObjects(form.Dict, reader) ?? xobjects;
                                        ExtractTextAndLines(formBytes, formFonts, reader, textRuns, hEdges, vEdges,
                                            fA, fB, fC, fD, fE, fF, formXObjects, depth + 1);
                                    }
                                }
                            }
                            break;
                    }
                    operands.Clear(); break;
                }
                default: operands.Clear(); break;
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
        return Encoding.Latin1.GetString(bytes);
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
