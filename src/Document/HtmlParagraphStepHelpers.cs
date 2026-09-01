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
// The procedure-step row helpers - line boxes, cell metrics, page breaks, bullets, tables and line rendering - lifted out of LayoutStepRow; each takes the row state and the inputs it reads.
    private static double PsCssLineBox(double fs)
    => 0.75 * Math.Round(fs / 0.75 * 1.1499, MidpointRounding.AwayFromZero);

    private static double PsAscentFor(double linePt, double fs)
    => (linePt - (1854 + 434) / 2048.0 * fs) / 2 + 1854 / 2048.0 * fs;

    private static double PsCellPad(Converters.HtmlToPdfConverter.StepTable pt)
        => pt.FormRhythm ? 2.25 : 3.75;

    private static double PsCellGap(Converters.HtmlToPdfConverter.StepTable pt)
        => pt.FormRhythm ? pt.CellSpacingPt : 0.0;

    private static double PsCellInset(Converters.HtmlToPdfConverter.StepTable pt,
        double colW, double lineW) => pt.Align switch
    {
        1 => Math.Max(PsCellPad(pt), (colW - lineW) / 2),
        2 => Math.Max(PsCellPad(pt),
            colW - (pt.FormRhythm ? PsCellPad(pt) : 1.875) - lineW),
        _ => PsCellPad(pt),
    };

    private static double PsTableX(StepRowState sr, Converters.HtmlToPdfConverter.StepTable pt) => pt.Align switch
    {
        1 => sr.psContentX + Math.Max(0, (sr.psLimit - pt.WidthPt) / 2),
        2 => sr.psContentX + Math.Max(0, sr.psLimit - pt.WidthPt),
        _ => sr.psContentX,
    };

    private static double PsRowNeed(StepRowState sr, Page page, Converters.HtmlToPdfConverter.StepRow r)
    {
        var n = 0.0;
        foreach (var mi in r.Items)
        {
            n += mi.GapBefore;
            if (mi.BoxBorderPt > 0) n += mi.BoxBorderPt + 1.5;
            else if (mi.BoxEnd) n += 1.5;
            if (mi.Table is not null) n += PsLayoutTable(page, sr, mi.Table).totalH;
            else if (mi.Line is { } ml)
            {
                var mfs = ml.FontPt > 0 ? ml.FontPt : psFs;
                var mPitch = ml.LinePt > 0 ? ml.LinePt : psPitch * mfs / psFs;
                n += ml.EmptyPara
                    ? mPitch + (ml.BlockMargined ? 0 : 2 * mfs)
                    : Math.Max(1, PsLayoutLine(sr, ml).Count) * mPitch;
            }
        }
        // the cluster's exact height, so keep-together prices
        // what the renderer draws (AckTable rows measure their
        // own way further down)
        if (n > 0 && r.HasAck) n += r.AckTable ? 44 : PsAckClusterGeom(r).ClusterH;
        // the row is a flex line: it cannot be shorter than the
        // acknowledge column standing beside its content
        if (n > 0 || r.Bullet is not null) n = Math.Max(n, r.AckHeightPt);
        return n;
    }

    private static void PsDrawClog(StepRowState sr, FlowLayout flow, Page page, double marginRight, double psWrapRight)
    {
        if ((!sr.prow.Clog && !sr.prow.Warn) || sr.clogTop - flow.CurrentY < 2) return;
        var cb = new Content.ContentStreamBuilder();
        cb.SaveState();
        if (sr.prow.Clog)
            cb.SetLineWidth(0.75)
              .MoveTo(page.Width - marginRight - 0.5, sr.clogTop)
              .LineTo(page.Width - marginRight - 0.5, flow.CurrentY)
              .Stroke();
        if (sr.prow.Warn)
        {
            // step-warning box: 5 css px black side bars,
            // top rule on the first page segment
            cb.SetLineWidth(3.75)
              .MoveTo(sr.psContentX + 1.0, sr.clogTop)
              .LineTo(sr.psContentX + 1.0, flow.CurrentY).Stroke()
              .MoveTo(psWrapRight + 1.0, sr.clogTop)
              .LineTo(psWrapRight + 1.0, flow.CurrentY).Stroke();
            if (sr.warnFirstSeg)
                cb.MoveTo(sr.psContentX - 0.9, sr.clogTop)
                  .LineTo(psWrapRight + 2.9, sr.clogTop).Stroke();
            sr.warnFirstSeg = false;
        }
        cb.RestoreState();
        flow.InjectContentAtCursor(cb.Build());
    }

    private static void PsBreakPage(StepRowState sr, FlowLayout flow, Page page, double marginRight, double psWrapRight, 
        [System.Runtime.CompilerServices.CallerLineNumber] int callerLine = 0)
    {
        if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_PSBRK") is not null)
            Console.WriteLine($"[psbrk] from line {callerLine} y={flow.CurrentY:0.##}");
        PsDrawClog(sr, flow, page, marginRight, psWrapRight);
        flow.ForceNewPage();
        sr.clogTop = flow.CurrentY;
    }

    private static void PsDrawBullet(StepRowState sr, FlowLayout flow, Content.ContentStreamBuilder pb, double pBase)
    {
        var bres = Table.RegisterFont(flow.CurrentPage, "Helvetica");
        var bx2 = sr.psBulletX;
        if (sr.prow.BulletSlashed)
        {
            // a struck-through step: grey fill behind the number
            // and a slash across it, the number inset and a
            // shade lower
            var fw = sr.prow.BulletSlashWidthPt;
            var fillTop = pBase + PsAscentFor(psPitch, psFs);
            pb.SetFillGray(0.8)
              .Rectangle(sr.psBulletX, fillTop - 15.0, fw, 15.0).Fill();
            pb.SetStrokeGray(2.0 / 3.0).SetLineWidth(2.25)
              .MoveTo(sr.psBulletX + fw - 0.75 - 22.5, fillTop - 15.0 - 2.31)
              .LineTo(sr.psBulletX + fw - 0.75, fillTop + 1.56)
              .Stroke()
              .SetStrokeGray(0.0).SetFillGray(0.0);
            bx2 += 0.75;
            pBase -= 0.75;
        }
        pb.BeginText().SetFont(bres, psFs)
          .MoveTextPosition(bx2, pBase)
          .ShowText(sr.prow.Bullet!).EndText();
        sr.psBulletPending = false;
    }

    private static List<List<(double x, Converters.HtmlToPdfConverter.StepSeg seg, string? txt)>> PsLayoutLine(StepRowState sr, Converters.HtmlToPdfConverter.StepLine pline)
    {
        var lfs = pline.FontPt > 0 ? pline.FontPt : psFs;
        var dLines = new List<List<(double, Converters.HtmlToPdfConverter.StepSeg, string?)>>();
        var cur = new List<(double, Converters.HtmlToPdfConverter.StepSeg, string?)>();
        var cx = 0.0;
        void PsNl()
        {
            dLines.Add(cur);
            cur = new List<(double, Converters.HtmlToPdfConverter.StepSeg, string?)>();
            cx = 0;
        }
        foreach (var seg in pline.Segs)
        {
            if (seg.BlankPt > 0)
            {
                if (cur.Count > 0 && cx + seg.PadLeftPt + seg.BlankPt > sr.psLimit + 0.5) PsNl();
                else cx += seg.PadLeftPt;
                cur.Add((cx, seg, null));
                cx += seg.BlankPt;
            }
            else if (seg.Radio || seg.Checkbox)
            {
                if (cur.Count > 0 && cx + 12.5 > sr.psLimit + 0.5) PsNl();
                cur.Add((cx, seg, null));
                cx += 12.5;
            }
            else if (seg.Text is { } st)
            {
                cx += seg.PadLeftPt;
                var rem = st;
                while (rem.Length > 0)
                {
                    var avail = sr.psLimit - cx;
                    var w1 = PsFirstWordEnd(rem);
                    if (cur.Count > 0
                        && PsMeasure(rem[..w1].TrimEnd(), seg.Bold, lfs) > avail + 0.5
                        && PsMeasure(rem[..w1].Trim(), seg.Bold, lfs) <= sr.psLimit)
                    {
                        PsNl();
                        rem = rem.TrimStart();
                        continue;
                    }
                    var fit = PsFitPrefix(rem, lfs, seg.Bold, Math.Max(avail, 4));
                    cur.Add((cx, seg, rem[..fit]));
                    cx += PsMeasure(rem[..fit], seg.Bold, lfs);
                    rem = rem[fit..];
                    if (rem.Length > 0) { PsNl(); rem = rem.TrimStart(); }
                }
            }
        }
        if (cur.Count > 0) PsNl();
        // a line that carries nothing is still a line box - a
        // break after a block closes one with nothing on it
        if (dLines.Count == 0) PsNl();
        return dLines;
    }

    private static double PsCellMinContent(List<Converters.HtmlToPdfConverter.StepLine> cell,
        double cfs, double kfs)
    {
        var min = 0.0;
        foreach (var cl in cell)
        {
            if (cl.EmptyPara) continue;
            var run = 0.0;
            foreach (var seg in cl.Segs)
            {
                if (seg.BlankPt > 0) run += seg.PadLeftPt + seg.BlankPt;
                else if (seg.Radio || seg.Checkbox) run += 11.2 * kfs;
                else if (seg.Text is { } st)
                {
                    if (st.Trim().Length == 0) { run = 0; continue; }
                    var words = st.Split(' ');
                    for (var k = 0; k < words.Length; k++)
                    {
                        if (k > 0 || words[k].Length == 0) run = 0;
                        if (words[k].Length == 0) continue;
                        run += (k == 0 ? seg.PadLeftPt : 0)
                             + PsMeasure(words[k], seg.Bold, cfs);
                        min = Math.Max(min, run);
                    }
                    if (st.EndsWith(' ')) run = 0;
                    continue;
                }
                min = Math.Max(min, run);
            }
            min = Math.Max(min, run + cl.TrailPadPt);
        }
        return min;
    }

    private static double[] PsColumnWidths(Converters.HtmlToPdfConverter.StepTable pt,
        double cfs, double kfs)
    {
        var n = pt.ColPts.Count;
        var w = new double[n];
        for (var c = 0; c < n; c++) w[c] = pt.ColPts[c];
        if (!pt.FormRhythm || !pt.WidthDeclared || n == 0) return w;
        var avail = pt.WidthPt - (n + 1) * PsCellGap(pt);
        if (avail <= 0) return w;
        var floors = new double[n];
        for (var c = 0; c < n; c++)
        {
            var m = 0.0;
            foreach (var row in pt.Rows)
                if (c < row.Count)
                    m = Math.Max(m, PsCellMinContent(row[c], cfs, kfs));
            floors[c] = m + 2 * PsCellPad(pt);
            w[c] = Math.Max(w[c], floors[c]);
        }
        var sum = 0.0;
        foreach (var v in w) sum += v;
        if (sum <= 0) return w;
        if (sum < avail)
        {
            for (var c = 0; c < n; c++) w[c] *= avail / sum;
            return w;
        }
        var slack = 0.0;
        for (var c = 0; c < n; c++) slack += w[c] - floors[c];
        if (slack <= 0.01) return w;   // nothing to give: it overflows
        var over = Math.Min(sum - avail, slack);
        for (var c = 0; c < n; c++) w[c] -= over * (w[c] - floors[c]) / slack;
        return w;
    }

    private static (List<List<string>> headLines, double headH, List<(double h, List<(List<double> lhs, List<(int li, double x, Converters.HtmlToPdfConverter.StepSeg? seg, string? txt)> pieces)>)> laidRows, double totalH, double[] declared) PsLayoutTable(Page page, StepRowState sr, Converters.HtmlToPdfConverter.StepTable pt)
    {
        // the table's own text size, and the factor its line
        // metrics scale by against the form's 12 pt
        var cfs = pt.CellFontPt > 0 ? pt.CellFontPt : psFs;
        var kfs = cfs / psFs;
        // an author's table stacks its cell lines on the form's
        // own pitch; a widget grid keeps the tighter one it was
        // built to
        // an author's grid stacks its cell lines on the box the
        // cell's own size asks for; a widget grid keeps the tighter
        // one it was built to
        var baseLine = pt.FormRhythm ? PsCssLineBox(cfs) : 11.4 * kfs;
        var declared = PsColumnWidths(pt, cfs, kfs);
        var visible = new double[declared.Length];
        var used = 0.0;
        for (var c = 0; c < declared.Length; c++)
        {
            visible[c] = Math.Max(0, Math.Min(declared[c], pt.WidthPt - used));
            used += declared[c];
        }
        var headLines = new List<List<string>>();
        var headH = 0.0;
        for (var c = 0; c < pt.Header.Count && c < visible.Length; c++)
        {
            var iw = Math.Max(visible[c] - 4.5, 1.0);
            var ls = PsWrap(pt.Header[c], 10.5, true, iw);
            headLines.Add(ls);
            headH = Math.Max(headH, Math.Max(12.75, ls.Count * 12.24));
        }

        var laidRows =
            new List<(double h, List<(List<double> lhs, List<(int li, double x, Converters.HtmlToPdfConverter.StepSeg? seg, string? txt)> pieces)>)>();
        var psTblX = PsTableX(sr, pt);
        foreach (var row in pt.Rows)
        {
            var rowH = 0.0;
            var colX = psTblX + PsCellGap(pt);
            var cellsOut = new List<(List<double>, List<(int, double, Converters.HtmlToPdfConverter.StepSeg?, string?)>)>();
            for (var c = 0; c < row.Count; c++)
            {
                // an author's cell wraps inside its own box, a
                // widget grid's against the column it declared
                var colWc = c < declared.Length ? declared[c] : 48.75;
                var iw = Math.Max(pt.FormRhythm
                    ? colWc - 2 * PsCellPad(pt) : colWc + 1.0, 8.0);
                var lhs = new List<double>();
                var pieces = new List<(int, double, Converters.HtmlToPdfConverter.StepSeg?, string?)>();
                var ccx = 0.0;
                var lineH = baseLine;
                var lineDirty = false;
                var blockBlankW = 0.0;
                // the block's own margin above, spent on the
                // first line box it opens and no other
                var clMargin = 0.0;
                void NewLine()
                {
                    lhs.Add(lineH + clMargin);
                    clMargin = 0;
                    ccx = 0;
                    lineH = baseLine;
                    lineDirty = false;
                }
                foreach (var cl in row[c])
                {
                    // the margin stands ABOVE the block: it grows
                    // the box the line before it closed, not its own
                    clMargin = pt.FormRhythm ? cl.MarginTopPt : 0.0;
                    if (clMargin > 0 && lhs.Count > 0)
                    {
                        lhs[^1] += clMargin;
                        clMargin = 0;
                    }
                    // an empty paragraph takes a line box AND
                    // the paragraph's own margins, above and below
                    if (cl.EmptyPara)
                    {
                        var pBox = pt.FormRhythm && cl.LinePt > 0 ? cl.LinePt : baseLine;
                        var pMar = pt.FormRhythm && cl.ParaMarginPt > 0
                            ? cl.ParaMarginPt : cfs;
                        lhs.Add(pBox + 2 * pMar);
                        clMargin = 0;
                        ccx = 0; lineH = baseLine; lineDirty = false;
                        continue;
                    }
                    foreach (var seg in cl.Segs)
                    {
                        if (seg.BlankPt > 0)
                        {
                            if (ccx > 0 && ccx + seg.PadLeftPt + seg.BlankPt > iw + 0.5) NewLine();
                            else ccx += seg.PadLeftPt;
                            pieces.Add((lhs.Count, ccx, seg, null));
                            ccx += seg.BlankPt;
                            lineH = pt.FormRhythm ? baseLine : 14.4 * kfs;
                            lineDirty = true;
                            blockBlankW = seg.PadLeftPt < 0.01 && cl.Segs.Count == 1
                                ? seg.BlankPt : 0.0;
                        }
                        else if (seg.Radio || seg.Checkbox)
                        {
                            // a choice symbol is a 13 px box with
                            // 5 px of margin after it
                            var gw = pt.FormRhythm && seg.Radio ? 13.5 * kfs : 11.2 * kfs;
                            if (ccx > 0 && ccx + gw > iw + 0.5) NewLine();
                            pieces.Add((lhs.Count, ccx, seg, null));
                            ccx += gw;
                            lineDirty = true;
                        }
                        else if (seg.Text is { } st)
                        {
                            // a label under a block-level blank wraps at
                            // the blank's width, not the cell's
                            var tw = blockBlankW > 0 && !lineDirty
                                ? Math.Min(iw, blockBlankW + 4.5) : iw;
                            if (lineDirty) ccx += seg.PadLeftPt;
                            var rem = st;
                            while (rem.Length > 0)
                            {
                                var avail = tw - ccx;
                                var w1 = PsFirstWordEnd(rem);
                                if (lineDirty
                                    && PsMeasure(rem[..w1].TrimEnd(), seg.Bold, cfs) > avail + 0.5
                                    && PsMeasure(rem[..w1].Trim(), seg.Bold, cfs) <= tw)
                                {
                                    NewLine();
                                    rem = rem.TrimStart();
                                    continue;
                                }
                                var fit = PsFitPrefix(rem, cfs, seg.Bold, Math.Max(avail, 4));
                                if (rem[..fit].Trim().Length > 0 || lineDirty)
                                {
                                    pieces.Add((lhs.Count, ccx, seg, rem[..fit]));
                                    ccx += PsMeasure(rem[..fit], seg.Bold, cfs);
                                    lineDirty = true;
                                }
                                rem = rem[fit..];
                                if (rem.Length > 0) { NewLine(); rem = rem.TrimStart(); }
                            }
                        }
                    }
                    if (lineDirty || ccx > 0) NewLine();
                    if (cl.Segs.Count != 1 || cl.Segs[0].BlankPt <= 0) blockBlankW = 0.0;
                }
                // with separate borders the cell stands its own
                // box, one rule above its lines and one below
                var cellH = pt.FormRhythm ? 1.5 : 4.08 * kfs;
                foreach (var lh in lhs) cellH += lh;
                cellsOut.Add((lhs, pieces));
                // a column the sheet cuts off before it starts is
                // never measured - the row is only as tall as what
                // can actually be drawn on the page
                if (colX < page.Width - 0.5) rowH = Math.Max(rowH, cellH);
                colX += colWc + PsCellGap(pt);
            }
            var rowFloor = laidRows.Count < pt.RowMinPt.Count
                ? pt.RowMinPt[laidRows.Count] : 0.0;
            laidRows.Add((Math.Max(rowH, rowFloor) + pt.CellSpacingPt,
                cellsOut));
        }

        // the spacing runs down the table's own edges too, so a
        // grid of separate cells is one gap taller than its rows
        var totalH = headH + PsCellGap(pt);
        foreach (var lr in laidRows) totalH += lr.h;
        return (headLines, headH, laidRows, totalH, declared);
    }

    private static void PsRenderLine(StepRowState sr, FlowLayout flow, Page page, double marginRight, double psWrapRight, Converters.HtmlToPdfConverter.StepLine pline,
        List<List<(double x, Converters.HtmlToPdfConverter.StepSeg seg, string? txt)>> dLines)
    {
        var rfs = pline.FontPt > 0 ? pline.FontPt : psFs;
        var rPitch = pline.LinePt > 0 ? pline.LinePt : psPitch * rfs / psFs;
        var rAsc = PsAscentFor(
            pline.AscentLinePt > 0 ? pline.AscentLinePt : rPitch, rfs);
        foreach (var dl in dLines)
        {
            if (flow.CurrentY - rPitch < flow.BottomMargin) PsBreakPage(sr, flow, page, marginRight, psWrapRight);
            var pb = new Content.ContentStreamBuilder();
            pb.SaveState();
            // a caption sits at the left edge of its own box,
            // which is centred in the content column
            var rInset = pline.CenterBoxPt > 0
                ? Math.Max(0, (sr.psLimit - pline.CenterBoxPt) / 2)
                : sr.psLineInset;
            if (pline.Align > 0)
            {
                var runW = 0.0;
                foreach (var (sx2, sg2, tx2) in dl)
                    runW = Math.Max(runW, sx2 + (sg2.BlankPt > 0
                        ? sg2.BlankPt
                        : sg2.Radio || sg2.Checkbox ? 11.2
                        : tx2 is null ? 0 : PsMeasure(tx2, sg2.Bold, rfs)));
                rInset += pline.Align == 1
                    ? Math.Max(0, (sr.psLimit - sr.psLineInset * 2 - runW) / 2)
                    : Math.Max(0, sr.psLimit - sr.psLineInset * 2 - runW);
            }
            var pBase = flow.CurrentY - rAsc;
            // the bullet keeps its own 18 css px line box whatever
            // box the content beside it sets on
            if (sr.psBulletPending) PsDrawBullet(sr, flow, pb, flow.CurrentY - PsAscentFor(psPitch, psFs));
            var psRuleDrop = pline.Segs.Count == 1 ? 0.0 : 2.4;
            foreach (var (sx, seg, txt) in dl)
            {
                var lx = sr.psContentX + rInset + sx;
                if (seg.BlankPt > 0)
                {
                    pb.SetLineWidth(0.75)
                      .MoveTo(lx, pBase - psRuleDrop)
                      .LineTo(lx + seg.BlankPt, pBase - psRuleDrop)
                      .Stroke();
                }
                else if (seg.Radio || seg.Checkbox)
                {
                    PsGlyph(pb, seg.Checkbox, lx, pBase);
                }
                else if (txt is not null)
                {
                    var pf = seg.Bold ? "Helvetica-Bold" : "Helvetica";
                    var pres = Table.RegisterFont(flow.CurrentPage, pf);
                    pb.BeginText().SetFont(pres, psFs)
                      .MoveTextPosition(lx, pBase)
                      .ShowText(txt).EndText();
                }
            }
            pb.RestoreState();
            flow.InjectContentAtCursor(pb.Build());
            // an empty paragraph takes a line box AND the margins
            // the paragraph carries above and below it - the same
            // rule the table rows decode on
            flow.AdvanceY(pline.EmptyPara && !pline.BlockMargined
                ? rPitch + 2 * rfs
                : rPitch);
        }
    }

    private static void PsRenderTable(FlowLayout flow, StepRowState sr, Page page, double marginRight, double psWrapRight, Converters.HtmlToPdfConverter.StepTable pt,
        (List<List<string>> headLines, double headH,
         List<(double h, List<(List<double> lhs, List<(int li, double x, Converters.HtmlToPdfConverter.StepSeg? seg, string? txt)> pieces)>)> laidRows,
         double totalH, double[] declared) lay)
    {
        var (headLines, headH, laidRows, totalH, declared) = lay;
        if (flow.CurrentY - totalH < flow.BottomMargin
            && totalH <= flow.ContentTop - flow.BottomMargin)
            PsBreakPage(sr, flow, page, marginRight, psWrapRight);
        else if (laidRows.Count > 0
                 && flow.CurrentY - (headH + laidRows[0].h) < flow.BottomMargin)
            PsBreakPage(sr, flow, page, marginRight, psWrapRight);

        var psRowIdx = 0;
        while (psRowIdx < laidRows.Count)
        {
            var segHead = psRowIdx == 0 ? headH : 0.0;
            var segRows = new List<(double h, List<(List<double> lhs, List<(int li, double x, Converters.HtmlToPdfConverter.StepSeg? seg, string? txt)> pieces)>)>();
            var segH = segHead;
            while (psRowIdx + segRows.Count < laidRows.Count)
            {
                var rh = laidRows[psRowIdx + segRows.Count].h;
                if (segRows.Count > 0 && flow.CurrentY - segH - rh < flow.BottomMargin) break;
                segRows.Add(laidRows[psRowIdx + segRows.Count]);
                segH += rh;
            }
            PsRenderTableSegment(sr, flow, pt, headLines, segHead, segRows, declared, psRowIdx);
            psRowIdx += segRows.Count;
            if (psRowIdx < laidRows.Count) PsBreakPage(sr, flow, page, marginRight, psWrapRight);
        }
    }

    private static void PsRenderTableSegment(StepRowState sr, FlowLayout flow, Converters.HtmlToPdfConverter.StepTable pt,
        List<List<string>> headLines, double headH,
        List<(double h, List<(List<double> lhs, List<(int li, double x, Converters.HtmlToPdfConverter.StepSeg? seg, string? txt)> pieces)>)> laidRows,
        double[] declared, int firstRow = 0)
    {
        var cfs = pt.CellFontPt > 0 ? pt.CellFontPt : psFs;
        var kfs = cfs / psFs;
        var gap = PsCellGap(pt);
        var totalH = headH + gap;
        foreach (var lr in laidRows) totalH += lr.h;
        var tx0 = PsTableX(sr, pt);
        var txR = Math.Min(tx0 + pt.WidthPt, sr.psRowRight);
        var topY = flow.CurrentY;
        var tb2 = new Content.ContentStreamBuilder();
        tb2.SaveState();
        if (sr.psBulletPending) PsDrawBullet(sr, flow, tb2, topY - psAscent);

        // cell fills go down BEFORE the grid and the text, so the
        // rules stay visible and the text sits on top of its fill
        var bgY = topY - headH - gap;
        for (var r = 0; r < laidRows.Count; r++)
        {
            var bgRow = firstRow + r < pt.RowBg.Count ? pt.RowBg[firstRow + r] : null;
            var boxH = Math.Max(0, laidRows[r].h - gap);
            var bgX = tx0 + gap;
            for (var c = 0; bgRow is not null && c < bgRow.Count; c++)
            {
                var cw = c < declared.Length ? declared[c] : 48.75;
                if (bgRow[c] is { } fill && bgX < txR - 1)
                {
                    tb2.SetFillColor(fill);
                    tb2.Rectangle(bgX, bgY - boxH,
                        Math.Min(cw, txR - bgX), boxH);
                    tb2.Fill();
                }
                bgX += cw + gap;
            }
            bgY -= laidRows[r].h;
        }
        tb2.SetFillColor(Color.Black);

        // grid
        tb2.SetStrokeGray(0.5).SetLineWidth(0.75);
        if (pt.FormRhythm)
        {
            // separate borders: every cell keeps its own rule,
            // drawn inside its box, and the spacing shows as a
            // gap between one cell's box and the next
            var cy = topY - headH - gap;
            foreach (var lr in laidRows)
            {
                var boxH = Math.Max(0, lr.h - gap);
                var cx = tx0 + gap;
                for (var c = 0; c < declared.Length; c++)
                {
                    var cw = declared[c];
                    if (cx + cw > txR + 0.5) break;
                    tb2.MoveTo(cx, cy - 0.375).LineTo(cx + cw, cy - 0.375).Stroke();
                    tb2.MoveTo(cx, cy - boxH + 0.375).LineTo(cx + cw, cy - boxH + 0.375).Stroke();
                    tb2.MoveTo(cx + 0.375, cy).LineTo(cx + 0.375, cy - boxH).Stroke();
                    tb2.MoveTo(cx + cw - 0.375, cy).LineTo(cx + cw - 0.375, cy - boxH).Stroke();
                    cx += cw + gap;
                }
                cy -= lr.h;
            }
        }
        else
        {
            var gy = topY;
            tb2.MoveTo(tx0, gy).LineTo(txR, gy).Stroke();
            gy -= headH;
            tb2.MoveTo(tx0, gy).LineTo(txR, gy).Stroke();
            foreach (var lr in laidRows)
            {
                gy -= lr.h;
                tb2.MoveTo(tx0, gy).LineTo(txR, gy).Stroke();
            }
            var gx2 = tx0;
            tb2.MoveTo(gx2, topY).LineTo(gx2, gy).Stroke();
            for (var c = 0; c < declared.Length; c++)
            {
                gx2 += declared[c];
                if (gx2 > txR - 1) break;
                tb2.MoveTo(gx2, topY).LineTo(gx2, gy).Stroke();
            }
            tb2.MoveTo(txR, topY).LineTo(txR, gy).Stroke();
        }
        tb2.SetStrokeGray(0);

        // header text, middle-aligned per column
        var thRes = Table.RegisterFont(flow.CurrentPage, "Helvetica-Bold");
        var hx = tx0 + gap;
        for (var c = 0; headH > 0 && c < headLines.Count; c++)
        {
            if (hx < txR - 2)
            {
                var blockTop = topY - (headH - headLines[c].Count * 12.24) / 2;
                var colW = c < declared.Length ? declared[c] : 48.75;
                for (var li = 0; li < headLines[c].Count; li++)
                {
                    // a heading cell centres each of its lines in
                    // the column, the way a th is set
                    var lw = PsMeasure(headLines[c][li], true, 10.5);
                    tb2.BeginText().SetFont(thRes, 10.5)
                       .MoveTextPosition(hx + PsCellInset(pt, colW, lw),
                           blockTop - 9.5 - li * 12.24)
                       .ShowText(headLines[c][li]).EndText();
                }
            }
            hx += (c < declared.Length ? declared[c] : 48.75) + gap;
        }

        // cells
        var rowTop = topY - headH - gap;
        foreach (var (rh, cellsOut) in laidRows)
        {
            var cellX = tx0 + gap;
            for (var c = 0; c < cellsOut.Count; c++)
            {
                if (cellX >= txR - 2)
                {
                    cellX += (c < declared.Length ? declared[c] : 48.75) + gap;
                    continue;
                }
                var (lhs, pieces) = cellsOut[c];
                var tops = new double[lhs.Count + 1];
                // a cell's content is centred in the box it was
                // given: the tallest cell fills the row, the rest
                // ride in the middle of it
                var lineSum = 0.0;
                foreach (var lh in lhs) lineSum += lh;
                var boxIn = pt.FormRhythm ? 0.75 : 0.0;
                var vMid = pt.FormRhythm
                    ? Math.Max(0, (rh - gap - 2 * boxIn - lineSum) / 2) : 0.0;
                tops[0] = rowTop - boxIn - vMid;
                for (var li = 0; li < lhs.Count; li++) tops[li + 1] = tops[li] - lhs[li];
                // how far each of the cell's lines runs, so the
                // wrap's alignment can seat it in the column
                var cellColW = c < declared.Length ? declared[c] : 48.75;
                var lineEnd = new double[lhs.Count + 1];
                foreach (var (li, sx, seg, txt) in pieces)
                {
                    var w = seg is { BlankPt: > 0 } ? seg.BlankPt
                        : seg is { Radio: true } ? (pt.FormRhythm ? 13.5 : 11.2) * kfs
                        : seg is { Checkbox: true } ? 11.2 * kfs
                        : txt is not null && seg is not null
                            ? PsMeasure(txt, seg.Bold, cfs) : 0.0;
                    var i2 = Math.Min(li, lhs.Count);
                    lineEnd[i2] = Math.Max(lineEnd[i2], sx + w);
                }
                foreach (var (li, sx, seg, txt) in pieces)
                {
                    var lineTop = tops[Math.Min(li, lhs.Count)];
                    var lx = cellX + sx
                        + PsCellInset(pt, cellColW, lineEnd[Math.Min(li, lhs.Count)]);
                    // an author's cell seats its text on the box
                    // its own size asks for, under the rule it
                    // draws above it
                    var lBase = lineTop - (pt.FormRhythm
                        ? PsAscentFor(PsCssLineBox(cfs), cfs)
                        : 12.5 * kfs);
                    if (seg is { BlankPt: > 0 })
                    {
                        // the blank is seated on the bottom of its
                        // line box, 3 css px of margin under its rule
                        var ruleY = lineTop - (pt.FormRhythm
                            ? PsCssLineBox(cfs) - 2.625 * kfs : 12.0 * kfs);
                        tb2.SetLineWidth(0.75)
                           .MoveTo(lx, ruleY)
                           .LineTo(lx + seg.BlankPt, ruleY)
                           .Stroke();
                    }
                    else if (seg is { Radio: true } or { Checkbox: true })
                    {
                        PsGlyph(tb2, seg!.Checkbox, lx, lBase);
                    }
                    else if (txt is not null && seg is not null)
                    {
                        var cres = Table.RegisterFont(flow.CurrentPage,
                            seg.Bold ? "Helvetica-Bold" : "Helvetica");
                        tb2.BeginText().SetFont(cres, cfs)
                           .MoveTextPosition(lx, lBase)
                           .ShowText(txt).EndText();
                    }
                }
                cellX += cellColW + gap;
            }
            rowTop -= rh;
        }
        tb2.RestoreState();
        flow.InjectContentAtCursor(tb2.Build());
        flow.AdvanceY(totalH);
    }
}
