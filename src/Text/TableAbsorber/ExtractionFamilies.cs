using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TableAbsorber
{
    /// <summary>The q/Q stack, the CTM and the form XObjects walked in their own matrix.</summary>
    private static void ExtractLinesStateOperator(TableLinesState tl, Dictionary<string, PdfDictionary> fonts, PdfReader reader, List<TextRun> textRuns, List<HEdge> hEdges, List<VEdge> vEdges, PdfDictionary? xobjects, int depth, string op)
    {
        switch (op)
        {
            case "q": tl.ctmStack.Push((tl.ctmA,tl.ctmB,tl.ctmC,tl.ctmD,tl.ctmE,tl.ctmF)); break;
            case "Q": if (tl.ctmStack.Count > 0) (tl.ctmA,tl.ctmB,tl.ctmC,tl.ctmD,tl.ctmE,tl.ctmF) = tl.ctmStack.Pop(); break;
            case "cm":
                if (tl.operands.Count >= 6)
                { var a=Num(tl.operands[0]);var b=Num(tl.operands[1]);var c=Num(tl.operands[2]);var d=Num(tl.operands[3]);var e=Num(tl.operands[4]);var f=Num(tl.operands[5]);
                  var nA=a*tl.ctmA+b*tl.ctmC;var nB=a*tl.ctmB+b*tl.ctmD;var nC=c*tl.ctmA+d*tl.ctmC;var nD=c*tl.ctmB+d*tl.ctmD;var nE=e*tl.ctmA+f*tl.ctmC+tl.ctmE;var nF=e*tl.ctmB+f*tl.ctmD+tl.ctmF;
                  tl.ctmA=nA;tl.ctmB=nB;tl.ctmC=nC;tl.ctmD=nD;tl.ctmE=nE;tl.ctmF=nF; }
                break;
            case "Do":
                // Recurse into Form XObjects so table grids drawn inside them are
                // extracted (nested tables are emitted as forms).
                if (xobjects is not null && depth < 12 && tl.operands.Count >= 1 && tl.operands[0] is PdfName xn)
                {
                    var form = reader.ResolveStream(xobjects.Get(xn.Value));
                    if (form is not null && form.Dict.GetName("Subtype") == "Form")
                    {
                        byte[]? formBytes = null;
                        try { formBytes = reader.DecodeStream(form); } catch { }
                        if (formBytes is not null)
                        {
                            double fA = tl.ctmA, fB = tl.ctmB, fC = tl.ctmC, fD = tl.ctmD, fE = tl.ctmE, fF = tl.ctmF;
                            if (reader.Resolve(form.Dict.Get("Matrix")) is PdfArray ma && ma.Count >= 6)
                            {
                                double m0=Num(ma[0]),m1=Num(ma[1]),m2=Num(ma[2]),m3=Num(ma[3]),m4=Num(ma[4]),m5=Num(ma[5]);
                                fA=m0*tl.ctmA+m1*tl.ctmC; fB=m0*tl.ctmB+m1*tl.ctmD; fC=m2*tl.ctmA+m3*tl.ctmC; fD=m2*tl.ctmB+m3*tl.ctmD;
                                fE=m4*tl.ctmA+m5*tl.ctmC+tl.ctmE; fF=m4*tl.ctmB+m5*tl.ctmD+tl.ctmF;
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
    }

    /// <summary>The text operators: font, leading, line moves, the matrix and the strings that become text runs.</summary>
    private static void ExtractLinesTextOperator(TableLinesState tl, Dictionary<string, PdfDictionary> fonts, PdfReader reader, List<TextRun> textRuns, string op)
    {
        switch (op)
        {
            case "BT": tl.tx=tl.txLine=0;tl.ty=tl.tyLine=0;tl.tmA=1;tl.tmB=0;tl.tmC=0;tl.tmD=1;tl.leading=0; break;
            case "TL": if (tl.operands.Count>=1) tl.leading=Num(tl.operands[0]); break;
            case "Tf":
                if (tl.operands.Count>=1&&tl.operands[0] is PdfName fn&&fonts.TryGetValue(fn.Value,out var fd)){tl.fontDict=fd;tl.toUnicode=TextAbsorber.ParseToUnicodeFromDict(fd,reader);tl.curMetrics=null;try{tl.curMetrics=FontMetrics.FromFontDict(fd,reader);}catch{}}
                if (tl.operands.Count>=2) tl.fontSize=Math.Abs(Num(tl.operands[1])); break;
            case "Td": if (tl.operands.Count>=2){var tdX=Num(tl.operands[0]);var tdY=Num(tl.operands[1]);tl.txLine=tl.tmA*tdX+tl.tmC*tdY+tl.txLine;tl.tyLine=tl.tmB*tdX+tl.tmD*tdY+tl.tyLine;tl.tx=tl.txLine;tl.ty=tl.tyLine;} break;
            case "TD": if (tl.operands.Count>=2){var tdX=Num(tl.operands[0]);var tdY=Num(tl.operands[1]);tl.leading=-tdY;tl.txLine=tl.tmA*tdX+tl.tmC*tdY+tl.txLine;tl.tyLine=tl.tmB*tdX+tl.tmD*tdY+tl.tyLine;tl.tx=tl.txLine;tl.ty=tl.tyLine;} break;
            case "T*": tl.txLine=tl.tmC*(-tl.leading)+tl.txLine;tl.tyLine=tl.tmD*(-tl.leading)+tl.tyLine;tl.tx=tl.txLine;tl.ty=tl.tyLine; break;
            case "Tm": if (tl.operands.Count>=6){tl.tmA=Num(tl.operands[0]);tl.tmB=Num(tl.operands[1]);tl.tmC=Num(tl.operands[2]);tl.tmD=Num(tl.operands[3]);tl.tx=tl.txLine=Num(tl.operands[4]);tl.ty=tl.tyLine=Num(tl.operands[5]);} break;
            case "Tj":
                // Estimated advance must carry the Tm and CTM scales — a
                // `117 Tf` with a 0.12-scale Tm is ~14pt text, and an
                // unscaled estimate would balloon the run width (and push
                // its centre outside every cell).
                if (tl.operands.Count>=1&&tl.operands[0] is PdfString s){var text=Decode(s.Value,tl.toUnicode,tl.fontDict);var(px,py)=ApplyMatrix(tl.tx,tl.ty,tl.ctmA,tl.ctmB,tl.ctmC,tl.ctmD,tl.ctmE,tl.ctmF);var hsX=tl.tmA*tl.ctmA+tl.tmB*tl.ctmC;var hsY=tl.tmA*tl.ctmB+tl.tmB*tl.ctmD;var hs=Math.Sqrt(hsX*hsX+hsY*hsY);var vsX=tl.tmC*tl.ctmA+tl.tmD*tl.ctmC;var vsY=tl.tmC*tl.ctmB+tl.tmD*tl.ctmD;var vs=Math.Sqrt(vsX*vsX+vsY*vsY);textRuns.Add(new TextRun(text,px,py,text.Length*tl.fontSize*0.5*hs,tl.fontSize*vs));} break;
            case "TJ":
                if (tl.operands.Count>=1&&tl.operands[0] is PdfArray arr)
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
                    double gapTU=tl.fontSize*1.5;
                    void FlushSub()
                    {
                        if (sb.Length==0) return;
                        var txx=tl.tx+tl.tmA*runStartPen; var tyy=tl.ty+tl.tmB*runStartPen;
                        var(px2,py2)=ApplyMatrix(txx,tyy,tl.ctmA,tl.ctmB,tl.ctmC,tl.ctmD,tl.ctmE,tl.ctmF);
                        var hsX=tl.tmA*tl.ctmA+tl.tmB*tl.ctmC;var hsY=tl.tmA*tl.ctmB+tl.tmB*tl.ctmD;var hs=Math.Sqrt(hsX*hsX+hsY*hsY);
                        var vsX=tl.tmC*tl.ctmA+tl.tmD*tl.ctmC;var vsY=tl.tmC*tl.ctmB+tl.tmD*tl.ctmD;var vs=Math.Sqrt(vsX*vsX+vsY*vsY);
                        textRuns.Add(new TextRun(sb.ToString(),px2,py2,sb.Length*tl.fontSize*0.5*hs,tl.fontSize*vs));
                        sb.Clear();
                    }
                    foreach(var item in arr)
                    {
                        if (item is PdfString ps)
                        {
                            var t=Decode(ps.Value,tl.toUnicode,tl.fontDict);
                            sb.Append(t);
                            // Advance the pen by the true glyph run width when the font
                            // metrics are available (falling back to a crude 0.5-em/char
                            // estimate). Accurate widths keep each post-gap sub-run's X
                            // inside its real column — a 0.5-em guess undershoots caps
                            // headers and slides text into the wrong cell.
                            pen+=tl.curMetrics is not null ? tl.curMetrics.MeasureString(t,tl.fontSize) : t.Length*tl.fontSize*0.5;
                        }
                        else if (item is PdfInteger or PdfReal)
                        {
                            var adv=-Num(item)/1000.0*tl.fontSize; // +ve = rightward
                            if (adv>gapTU) { FlushSub(); pen+=adv; runStartPen=pen; }
                            else pen+=adv;
                        }
                    }
                    FlushSub();
                }
                break;
        }
    }

    /// <summary>The path operators: segments and rectangles buffered, then kept as edges when a paint finalises them.</summary>
    private static void ExtractLinesPathOperator(TableLinesState tl, List<HEdge> hEdges, List<VEdge> vEdges, string op)
    {
        switch (op)
        {
            case "m":
                if (tl.operands.Count>=2){var(px,py)=ApplyMatrix(Num(tl.operands[0]),Num(tl.operands[1]),tl.ctmA,tl.ctmB,tl.ctmC,tl.ctmD,tl.ctmE,tl.ctmF);tl.curX=tl.moveX=px;tl.curY=tl.moveY=py;} break;
            case "l":
                if (tl.operands.Count>=2)
                {
                    var(lx,ly)=ApplyMatrix(Num(tl.operands[0]),Num(tl.operands[1]),tl.ctmA,tl.ctmB,tl.ctmC,tl.ctmD,tl.ctmE,tl.ctmF);
                    tl.pendingLines.Add(new PendingLine(tl.curX, tl.curY, lx, ly));
                    tl.curX=lx;tl.curY=ly;
                }
                break;
            case "h":
                // Close subpath: add a line from current point back to the move-to point
                if (Math.Abs(tl.curX - tl.moveX) > 0.01 || Math.Abs(tl.curY - tl.moveY) > 0.01)
                    tl.pendingLines.Add(new PendingLine(tl.curX, tl.curY, tl.moveX, tl.moveY));
                tl.curX = tl.moveX; tl.curY = tl.moveY;
                break;
            case "re":
                if (tl.operands.Count>=4)
                {
                    var rx=Num(tl.operands[0]);var ry=Num(tl.operands[1]);var rw=Num(tl.operands[2]);var rh=Num(tl.operands[3]);
                    var(p0x,p0y)=ApplyMatrix(rx,ry,tl.ctmA,tl.ctmB,tl.ctmC,tl.ctmD,tl.ctmE,tl.ctmF);
                    var(p2x,p2y)=ApplyMatrix(rx+rw,ry+rh,tl.ctmA,tl.ctmB,tl.ctmC,tl.ctmD,tl.ctmE,tl.ctmF);
                    var x=Math.Min(p0x,p2x); var y=Math.Min(p0y,p2y);
                    var w=Math.Abs(p2x-p0x); var h=Math.Abs(p2y-p0y);
                    tl.pendingRects.Add(new PendingRect(x, y, w, h));
                }
                break;
            // Paint operators: finalize buffered path segments as edges
            case "S" or "s" or "f" or "F" or "f*" or "B" or "B*" or "b" or "b*":
                FlushPendingEdges(tl.pendingLines, tl.pendingRects, hEdges, vEdges,
                    stroked: op is "S" or "s" or "B" or "B*" or "b" or "b*");
                tl.pendingLines.Clear(); tl.pendingRects.Clear(); break;
            case "n":
                // No-paint: discard pending paths (clip-only)
                tl.pendingLines.Clear(); tl.pendingRects.Clear(); break;
            case "W" or "W*": break; // Clip modifiers don't finalize path
        }
    }
}
