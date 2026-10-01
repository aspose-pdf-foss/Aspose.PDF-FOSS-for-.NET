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

}
