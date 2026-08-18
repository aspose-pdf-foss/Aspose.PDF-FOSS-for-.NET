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

public sealed partial class Document : IDisposable
{
    private void LayoutProcedureStepRows(List<Converters.HtmlToPdfConverter.StepRow> psRows, HtmlFragment html, FlowLayout flow, Page page, double marginLeft, double marginRight, double marginTop, double marginBottom)
    {
        // Procedure-step form rows: each bullet column numbers a
        // content column of widget lines — text runs, underlined
        // fill-in blanks at their CSS widths, stroked radio and
        // checkbox glyphs, data-entry tables — on the form's
        // 13.5 pt rhythm. Tables lay out fixed at their declared
        // width and clip at the wrap box's right edge; a data-
        // entry caption travels with its table across pages; the
        // acknowledge widgets stack blanks and small labels at
        // the sheet's right end, and a clog row draws its full-
        // height right border.
        const double psFs = 12.0, psPitch = 13.5, psAscent = 10.85;
        var psWrapRight = page.Width - 34.8;

        // Where the baseline sits in a line box: the box's leading is
        // split above and below the face's own content area, so a line
        // set tighter than the face rides higher in its box. Arial's
        // ascent and descent are 1854 and 434 per 2048 em.
        // The line box a run of this size sits in when nothing declares
        // one: normal leading on an integer number of css pixels.
        static double PsCssLineBox(double fs)
            => 0.75 * Math.Round(fs / 0.75 * 1.1499, MidpointRounding.AwayFromZero);

        static double PsAscentFor(double linePt, double fs)
            => (linePt - (1854 + 434) / 2048.0 * fs) / 2 + 1854 / 2048.0 * fs;

        double PsMeasure(string txt, bool bold, double fs)
        {
            var f = bold ? "Helvetica-Bold" : "Helvetica";
            try
            {
                return Text.FontRepository.FindFont(f)?.MeasureString(txt, fs)
                       ?? txt.Length * fs * 0.5;
            }
            catch { return txt.Length * fs * 0.5; }
        }

        // word-first wrap; an overlong word char-breaks (break-all)
        List<string> PsWrap(string txt, double fs, bool bold, double maxW)
        {
            var res = new List<string>();
            var cur = "";
            foreach (var word in txt.Split(' '))
            {
                var cand = cur.Length == 0 ? word : cur + " " + word;
                if (PsMeasure(cand, bold, fs) <= maxW || cur.Length == 0 && word.Length == 0)
                {
                    cur = cand;
                    continue;
                }
                if (cur.Length > 0) { res.Add(cur); cur = ""; }
                var piece = "";
                foreach (var ch in word)
                {
                    if (piece.Length > 0 && PsMeasure(piece + ch, bold, fs) > maxW)
                    {
                        res.Add(piece);
                        piece = "";
                    }
                    piece += ch;
                }
                cur = piece;
            }
            if (cur.Length > 0) res.Add(cur);
            if (res.Count == 0) res.Add("");
            return res;
        }

        // chars of s that fit the first display line at maxW:
        // whole words first, char-break only an overlong word
        int PsFitPrefix(string s, double fs, bool bold, double maxW)
        {
            if (PsMeasure(s.TrimEnd(), bold, fs) <= maxW) return s.Length;
            var lastGood = 0;
            for (var k = 1; k < s.Length; k++)
            {
                if (s[k] != ' ') continue;
                if (PsMeasure(s[..k].TrimEnd(), bold, fs) <= maxW) lastGood = k + 1;
                else break;
            }
            if (lastGood > 0) return lastGood;
            var n = 1;
            while (n < s.Length && s[n] != ' '
                   && PsMeasure(s[..(n + 1)], bold, fs) <= maxW) n++;
            return n;
        }

        // the first word of s (leading spaces included in its span)
        int PsFirstWordEnd(string s)
        {
            var k = 0;
            while (k < s.Length && s[k] == ' ') k++;
            while (k < s.Length && s[k] != ' ') k++;
            return k;
        }

        double PsGlyph(Content.ContentStreamBuilder pb, bool checkbox, double gx, double gBase)
        {
            if (checkbox)
            {
                pb.SetLineWidth(0.9).Rectangle(gx + 0.4, gBase, 7.7, 7.7).Stroke();
                return gx + 8.5 + 3.0;
            }
            const double rr = 4.1, rk = 0.5523;
            var ccx = gx + rr + 0.4;
            var ccy = gBase + rr;
            pb.SetLineWidth(0.9)
              .MoveTo(ccx + rr, ccy)
              .CurveTo(ccx + rr, ccy + rk * rr, ccx + rk * rr, ccy + rr, ccx, ccy + rr)
              .CurveTo(ccx - rk * rr, ccy + rr, ccx - rr, ccy + rk * rr, ccx - rr, ccy)
              .CurveTo(ccx - rr, ccy - rk * rr, ccx - rk * rr, ccy - rr, ccx, ccy - rr)
              .CurveTo(ccx + rk * rr, ccy - rr, ccx + rr, ccy - rk * rr, ccx + rr, ccy)
              .Stroke();
            return gx + 2 * rr + 0.8 + 3.75;
        }

        for (var prowIdx = 0; prowIdx < psRows.Count; prowIdx++)
        {
            var prow = psRows[prowIdx];
            var psContentX = marginLeft + 55.5 + prow.IndentPt;
            var psBulletX = marginLeft + 1.5 + prow.IndentPt;
            // the column the form declares for this row, which may
            // reach past the sheet and be clipped there
            // the acknowledge-table generation squares its column off
            // 1.2 pt further right than the flex one (its box rules
            // stroke at sheet − 33.6), and its
            // LANDSCAPE col-full rows keep the 729 css px landscape column
            const double psAckTableRightMargin = 33.6;
            const double psLandscapeColumnW = 729 * 0.75;
            var psRowRight = prow.ContentWidthPt > 0
                ? psContentX + prow.ContentWidthPt
                : prow.AckTable
                    ? prow.Landscape ? psContentX + psLandscapeColumnW
                        : page.Width - psAckTableRightMargin
                    : psWrapRight;
            var psLimit = psRowRight - psContentX;

            // the wrap seats its grid against the left edge of the
            // content column, its centre, or its right edge - which for
            // a wide column can put the grid's tail past the sheet
            // A wrap's text-align is inherited by everything in the grid, so
            // a right-aligned wrap sets each cell line flush right - 2 css px
            // of cell padding and half the rule in from the column's edge.
            // How far in from the cell's own box its text starts. A
            // widget grid collapses its borders and pads 5 css px; the
            // page's own rule pads an author's cell 2 css px inside a
            // rule the cell keeps to itself.
            static double PsCellPad(Converters.HtmlToPdfConverter.StepTable pt)
                => pt.FormRhythm ? 2.25 : 3.75;

            // With separate borders the cells stand apart: the spacing
            // runs down the table's own edges as well as between them.
            static double PsCellGap(Converters.HtmlToPdfConverter.StepTable pt)
                => pt.FormRhythm ? pt.CellSpacingPt : 0.0;

            static double PsCellInset(Converters.HtmlToPdfConverter.StepTable pt,
                double colW, double lineW) => pt.Align switch
            {
                1 => Math.Max(PsCellPad(pt), (colW - lineW) / 2),
                2 => Math.Max(PsCellPad(pt),
                    colW - (pt.FormRhythm ? PsCellPad(pt) : 1.875) - lineW),
                _ => PsCellPad(pt),
            };

            double PsTableX(Converters.HtmlToPdfConverter.StepTable pt) => pt.Align switch
            {
                1 => psContentX + Math.Max(0, (psLimit - pt.WidthPt) / 2),
                2 => psContentX + Math.Max(0, psLimit - pt.WidthPt),
                _ => psContentX,
            };

            // whether this sheet already carries a step, read BEFORE the
            // row's own margin is spent - otherwise a step that has just
            // opened a fresh sheet looks like it is following something
            // and breaks again, for ever
            var psSheetEmpty = flow.CurrentY >= flow.ContentTop - 0.5;
            flow.AdvanceY(11.25);             // step-row 15 css px margin
            // How tall a step will be: the boxes each of its lines
            // really takes at its own size, its tables, its gaps, and the
            // block the renderer reserves for the acknowledgement.
            double PsRowNeed(Converters.HtmlToPdfConverter.StepRow r)
            {
                var n = 0.0;
                foreach (var mi in r.Items)
                {
                    n += mi.GapBefore;
                    if (mi.BoxBorderPt > 0) n += mi.BoxBorderPt + 1.5;
                    else if (mi.BoxEnd) n += 1.5;
                    if (mi.Table is not null) n += PsLayoutTable(mi.Table).totalH;
                    else if (mi.Line is { } ml)
                    {
                        var mfs = ml.FontPt > 0 ? ml.FontPt : psFs;
                        var mPitch = ml.LinePt > 0 ? ml.LinePt : psPitch * mfs / psFs;
                        n += ml.EmptyPara
                            ? mPitch + (ml.BlockMargined ? 0 : 2 * mfs)
                            : Math.Max(1, PsLayoutLine(ml).Count) * mPitch;
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

            {
                var rowNeed = PsRowNeed(prow);
                var pageBudget = flow.ContentTop - flow.BottomMargin;

                // A step keeps the sub-steps indented under it: a number
                // left at the foot of a sheet with the steps it heads
                // overleaf is not how the form reads. Only a deeper indent
                // counts - a step numbered like a child but set at the
                // parent's own indent stands on its own.
                var groupNeed = rowNeed;
                for (var k = prowIdx + 1;
                     k < psRows.Count && psRows[k].IndentPt > prow.IndentPt;
                     k++)
                    groupNeed += 11.25 + PsRowNeed(psRows[k]);
                // a group too tall for any sheet cannot be kept together,
                // and forcing it over would only empty the sheet it left
                if (groupNeed > pageBudget) groupNeed = rowNeed;

                // A step travels whole: its number and the content that
                // belongs to it are not parted across a sheet. It starts a
                // fresh sheet even when taller than one - it still splits,
                // but from a clean top; only once something is already on
                // this sheet, or it would break for ever.
                var breakForSelf = flow.CurrentY - groupNeed < flow.BottomMargin
                    && (groupNeed <= pageBudget || !psSheetEmpty);

                if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_STEPS") == "1")
                    Console.WriteLine($"    step '{prow.Bullet}' need={rowNeed:0.00} "
                        + $"y={flow.CurrentY:0.00} self={breakForSelf}");

                if (rowNeed > 0 && breakForSelf)
                {
                    flow.ForceNewPage();
                    // the step's own margin belongs above it wherever it
                    // lands, so it opens a fresh sheet the same distance
                    // down as it would have opened this one. The col-full
                    // generation opens one paragraph line lower still —
                    // its sheet's own rhythm.
                    flow.AdvanceY(11.25 + (prow.ColFull ? 12.0 : 0.0));
                }
            }

            // the framed note box currently open: where it started, the
            // rule it is drawn in, and the inset its content takes
            var psBoxTop = 0.0;
            var psBoxBorder = 0.0;
            var psBoxDouble = false;
            var psLineInset = 0.0;
            var clogTop = flow.CurrentY;
            var psRowTop = flow.CurrentY;
            var psRowStartPage = flow.CurrentPage;
            var psBulletPending = prow.Bullet is not null;

            var warnFirstSeg = true;
            void PsDrawClog()
            {
                if ((!prow.Clog && !prow.Warn) || clogTop - flow.CurrentY < 2) return;
                var cb = new Content.ContentStreamBuilder();
                cb.SaveState();
                if (prow.Clog)
                    cb.SetLineWidth(0.75)
                      .MoveTo(page.Width - marginRight - 0.5, clogTop)
                      .LineTo(page.Width - marginRight - 0.5, flow.CurrentY)
                      .Stroke();
                if (prow.Warn)
                {
                    // step-warning box: 5 css px black side bars,
                    // top rule on the first page segment
                    cb.SetLineWidth(3.75)
                      .MoveTo(psContentX + 1.0, clogTop)
                      .LineTo(psContentX + 1.0, flow.CurrentY).Stroke()
                      .MoveTo(psWrapRight + 1.0, clogTop)
                      .LineTo(psWrapRight + 1.0, flow.CurrentY).Stroke();
                    if (warnFirstSeg)
                        cb.MoveTo(psContentX - 0.9, clogTop)
                          .LineTo(psWrapRight + 2.9, clogTop).Stroke();
                    warnFirstSeg = false;
                }
                cb.RestoreState();
                flow.InjectContentAtCursor(cb.Build());
            }
            void PsBreakPage(
                [System.Runtime.CompilerServices.CallerLineNumber] int callerLine = 0)
            {
                if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_PSBRK") is not null)
                    Console.WriteLine($"[psbrk] from line {callerLine} y={flow.CurrentY:0.##}");
                PsDrawClog();
                flow.ForceNewPage();
                clogTop = flow.CurrentY;
            }
            void PsDrawBullet(Content.ContentStreamBuilder pb, double pBase)
            {
                var bres = Table.RegisterFont(flow.CurrentPage, "Helvetica");
                var bx2 = psBulletX;
                if (prow.BulletSlashed)
                {
                    // a struck-through step: grey fill behind the number
                    // and a slash across it, the number inset and a
                    // shade lower
                    var fw = prow.BulletSlashWidthPt;
                    var fillTop = pBase + PsAscentFor(psPitch, psFs);
                    pb.SetFillGray(0.8)
                      .Rectangle(psBulletX, fillTop - 15.0, fw, 15.0).Fill();
                    pb.SetStrokeGray(2.0 / 3.0).SetLineWidth(2.25)
                      .MoveTo(psBulletX + fw - 0.75 - 22.5, fillTop - 15.0 - 2.31)
                      .LineTo(psBulletX + fw - 0.75, fillTop + 1.56)
                      .Stroke()
                      .SetStrokeGray(0.0).SetFillGray(0.0);
                    bx2 += 0.75;
                    pBase -= 0.75;
                }
                pb.BeginText().SetFont(bres, psFs)
                  .MoveTextPosition(bx2, pBase)
                  .ShowText(prow.Bullet!).EndText();
                psBulletPending = false;
            }

            // ---- pure layout ----

            List<List<(double x, Converters.HtmlToPdfConverter.StepSeg seg, string? txt)>>
                PsLayoutLine(Converters.HtmlToPdfConverter.StepLine pline)
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
                        if (cur.Count > 0 && cx + seg.PadLeftPt + seg.BlankPt > psLimit + 0.5) PsNl();
                        else cx += seg.PadLeftPt;
                        cur.Add((cx, seg, null));
                        cx += seg.BlankPt;
                    }
                    else if (seg.Radio || seg.Checkbox)
                    {
                        if (cur.Count > 0 && cx + 12.5 > psLimit + 0.5) PsNl();
                        cur.Add((cx, seg, null));
                        cx += 12.5;
                    }
                    else if (seg.Text is { } st)
                    {
                        cx += seg.PadLeftPt;
                        var rem = st;
                        while (rem.Length > 0)
                        {
                            var avail = psLimit - cx;
                            var w1 = PsFirstWordEnd(rem);
                            if (cur.Count > 0
                                && PsMeasure(rem[..w1].TrimEnd(), seg.Bold, lfs) > avail + 0.5
                                && PsMeasure(rem[..w1].Trim(), seg.Bold, lfs) <= psLimit)
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

            // CSS min-content: the widest run the cell cannot break. A
            // blank or a glyph is atomic and an inline margin belongs to
            // the run it opens; a space - inside a text run or standing
            // as a segment of its own - is where a run may end.
            double PsCellMinContent(List<Converters.HtmlToPdfConverter.StepLine> cell,
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

            // A grid that declares its own width fits the columns into
            // it: a column takes the width it declares unless its own
            // content cannot be broken narrower than that, and the
            // columns that still have room give up whatever the rest
            // overruns by. A grid that declares no width keeps the
            // columns exactly as they were declared.
            double[] PsColumnWidths(Converters.HtmlToPdfConverter.StepTable pt,
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

            (List<List<string>> headLines, double headH,
             List<(double h, List<(List<double> lhs, List<(int li, double x, Converters.HtmlToPdfConverter.StepSeg? seg, string? txt)> pieces)>)> laidRows,
             double totalH, double[] declared)
                PsLayoutTable(Converters.HtmlToPdfConverter.StepTable pt)
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
                var psTblX = PsTableX(pt);
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

            // ---- drawing ----

            void PsRenderLine(Converters.HtmlToPdfConverter.StepLine pline,
                List<List<(double x, Converters.HtmlToPdfConverter.StepSeg seg, string? txt)>> dLines)
            {
                var rfs = pline.FontPt > 0 ? pline.FontPt : psFs;
                var rPitch = pline.LinePt > 0 ? pline.LinePt : psPitch * rfs / psFs;
                var rAsc = PsAscentFor(
                    pline.AscentLinePt > 0 ? pline.AscentLinePt : rPitch, rfs);
                foreach (var dl in dLines)
                {
                    if (flow.CurrentY - rPitch < flow.BottomMargin) PsBreakPage();
                    var pb = new Content.ContentStreamBuilder();
                    pb.SaveState();
                    // a caption sits at the left edge of its own box,
                    // which is centred in the content column
                    var rInset = pline.CenterBoxPt > 0
                        ? Math.Max(0, (psLimit - pline.CenterBoxPt) / 2)
                        : psLineInset;
                    if (pline.Align > 0)
                    {
                        var runW = 0.0;
                        foreach (var (sx2, sg2, tx2) in dl)
                            runW = Math.Max(runW, sx2 + (sg2.BlankPt > 0
                                ? sg2.BlankPt
                                : sg2.Radio || sg2.Checkbox ? 11.2
                                : tx2 is null ? 0 : PsMeasure(tx2, sg2.Bold, rfs)));
                        rInset += pline.Align == 1
                            ? Math.Max(0, (psLimit - psLineInset * 2 - runW) / 2)
                            : Math.Max(0, psLimit - psLineInset * 2 - runW);
                    }
                    var pBase = flow.CurrentY - rAsc;
                    // the bullet keeps its own 18 css px line box whatever
                    // box the content beside it sets on
                    if (psBulletPending) PsDrawBullet(pb, flow.CurrentY - PsAscentFor(psPitch, psFs));
                    var psRuleDrop = pline.Segs.Count == 1 ? 0.0 : 2.4;
                    foreach (var (sx, seg, txt) in dl)
                    {
                        var lx = psContentX + rInset + sx;
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

            void PsRenderTable(Converters.HtmlToPdfConverter.StepTable pt,
                (List<List<string>> headLines, double headH,
                 List<(double h, List<(List<double> lhs, List<(int li, double x, Converters.HtmlToPdfConverter.StepSeg? seg, string? txt)> pieces)>)> laidRows,
                 double totalH, double[] declared) lay)
            {
                var (headLines, headH, laidRows, totalH, declared) = lay;
                if (flow.CurrentY - totalH < flow.BottomMargin
                    && totalH <= flow.ContentTop - flow.BottomMargin)
                    PsBreakPage();
                else if (laidRows.Count > 0
                         && flow.CurrentY - (headH + laidRows[0].h) < flow.BottomMargin)
                    PsBreakPage();

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
                    PsRenderTableSegment(pt, headLines, segHead, segRows, declared, psRowIdx);
                    psRowIdx += segRows.Count;
                    if (psRowIdx < laidRows.Count) PsBreakPage();
                }
            }

            void PsRenderTableSegment(Converters.HtmlToPdfConverter.StepTable pt,
                List<List<string>> headLines, double headH,
                List<(double h, List<(List<double> lhs, List<(int li, double x, Converters.HtmlToPdfConverter.StepSeg? seg, string? txt)> pieces)>)> laidRows,
                double[] declared, int firstRow = 0)
            {
                var cfs = pt.CellFontPt > 0 ? pt.CellFontPt : psFs;
                var kfs = cfs / psFs;
                var gap = PsCellGap(pt);
                var totalH = headH + gap;
                foreach (var lr in laidRows) totalH += lr.h;
                var tx0 = PsTableX(pt);
                var txR = Math.Min(tx0 + pt.WidthPt, psRowRight);
                var topY = flow.CurrentY;
                var tb2 = new Content.ContentStreamBuilder();
                tb2.SaveState();
                if (psBulletPending) PsDrawBullet(tb2, topY - psAscent);

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

            // ---- flow: keep a data-entry caption with its table ----

            var pidx = 0;
            var psSeenTable = false;
            while (pidx < prow.Items.Count)
            {
                var item = prow.Items[pidx];
                if (prow.Warn)
                {
                    // a warning box paginates one
                    // block per page until its table
                    if (item.Line is { } wl)
                    {
                        PsRenderLine(wl, PsLayoutLine(wl));
                        if (!psSeenTable && pidx + 1 < prow.Items.Count) PsBreakPage();
                    }
                    else if (item.Table is { } wt)
                    {
                        psSeenTable = true;
                        if (item.GapBefore > 0) flow.AdvanceY(item.GapBefore);
                        PsRenderTable(wt, PsLayoutTable(wt));
                    }
                    pidx++;
                    continue;
                }
                if (item.BoxBorderPt > 0)
                {
                    if (item.GapBefore > 0) flow.AdvanceY(item.GapBefore);
                    psBoxTop = flow.CurrentY;
                    psBoxBorder = item.BoxBorderPt;
                    psBoxDouble = item.BoxDouble;
                    // the side inset stays rule + 2 css px; the sheet's
                    // own padding-top only deepens the first line's seat
                    psLineInset = psBoxBorder + 1.5;
                    flow.AdvanceY(psBoxBorder + (item.BoxPadTopPt > 0 ? item.BoxPadTopPt : 1.5));
                    pidx++;
                    continue;
                }
                if (item.BoxEnd)
                {
                    flow.AdvanceY(psBoxBorder + 1.5);
                    // the frame stands at least 80 css px tall
                    var boxH = psBoxTop - flow.CurrentY;
                    if (boxH < 60.0) { flow.AdvanceY(60.0 - boxH); boxH = 60.0; }
                    var bb = new Content.ContentStreamBuilder();
                    bb.SaveState();
                    var yTop = psBoxTop;
                    var yBot = psBoxTop - boxH;
                    // a double rule is two thinner ones filling the band
                    var runs = psBoxDouble
                        ? new[] { (0.625, 1.25), (3.125, 1.25) }
                        : new[] { (psBoxBorder / 2, psBoxBorder) };
                    foreach (var (off, w) in runs)
                    {
                        bb.SetLineWidth(w)
                          .MoveTo(psContentX, yTop - off)
                          .LineTo(psRowRight, yTop - off).Stroke()
                          .MoveTo(psContentX, yBot + off)
                          .LineTo(psRowRight, yBot + off).Stroke()
                          .MoveTo(psContentX + off, yTop)
                          .LineTo(psContentX + off, yBot).Stroke()
                          .MoveTo(psRowRight - off, yTop)
                          .LineTo(psRowRight - off, yBot).Stroke();
                    }
                    bb.RestoreState();
                    flow.InjectContentAtCursor(bb.Build());
                    psBoxBorder = 0;
                    psLineInset = 0;
                    pidx++;
                    continue;
                }
                if (item.Line is { } gl && item.KeepWithNext)
                {
                    var j = pidx;
                    var groupH = 0.0;
                    var lineLays = new List<(Converters.HtmlToPdfConverter.StepItem it,
                        List<List<(double x, Converters.HtmlToPdfConverter.StepSeg seg, string? txt)>> lay)>();
                    while (j < prow.Items.Count && prow.Items[j].Line is { } jl)
                    {
                        var lay = PsLayoutLine(jl);
                        lineLays.Add((prow.Items[j], lay));
                        groupH += prow.Items[j].GapBefore + lay.Count * psPitch;
                        j++;
                        if (!prow.Items[j - 1].KeepWithNext) break;
                    }
                    (List<List<string>>, double,
                     List<(double, List<(List<double>, List<(int, double, Converters.HtmlToPdfConverter.StepSeg?, string?)>)>)>,
                     double, double[])? tabLay = null;
                    Converters.HtmlToPdfConverter.StepTable? tabItem = null;
                    var tabGap = 0.0;
                    if (j < prow.Items.Count && prow.Items[j].Table is { } jt)
                    {
                        var tl = PsLayoutTable(jt);
                        tabLay = tl;
                        tabItem = jt;
                        tabGap = prow.Items[j].GapBefore;
                        groupH += tabGap + tl.totalH;
                        j++;
                    }
                    if (flow.CurrentY - groupH < flow.BottomMargin
                        && groupH <= flow.ContentTop - flow.BottomMargin)
                        PsBreakPage();
                    foreach (var (it, lay) in lineLays)
                    {
                        if (it.GapBefore > 0) flow.AdvanceY(it.GapBefore);
                        PsRenderLine(it.Line!, lay);
                    }
                    if (tabItem is not null && tabLay is { } tv)
                    {
                        if (tabGap > 0) flow.AdvanceY(tabGap);
                        PsRenderTable(tabItem, tv);
                    }
                    pidx = j;
                    continue;
                }
                if (item.GapBefore > 0) flow.AdvanceY(item.GapBefore);
                if (item.Line is { } pline)
                {
                    PsRenderLine(pline, PsLayoutLine(pline));
                }
                else if (item.Table is { } pt)
                {
                    PsRenderTable(pt, PsLayoutTable(pt));
                }
                pidx++;
            }
            if (prow.HasAck && prow.AckTable)
            {
                // The col-full generation's acknowledge table stands
                // UNDER the content: widget cells right-anchored on a
                // 112.5 pt grid against the sheet's right edge, each
                // blank over the labels its own cell carries, and the
                // second label row on a baseline all widgets share.
                // Above it: a paragraph's own bottom margin, or the
                // table's 7.5 after a framed box (nothing after the
                // double-ruled one). All constants are empirical,
                // exact to 0.01.
                var relBoxTop = prow.AckHair ? 8.25 : 5.25;
                var gapAbove = Converters.HtmlToPdfConverter.StepParaMargin ?? 11.25;
                for (var qi = prow.Items.Count - 1; qi >= 0; qi--)
                {
                    if (prow.Items[qi].BoxEnd)
                    {
                        var dbl = false;
                        for (var bi = qi - 1; bi >= 0; bi--)
                            if (prow.Items[bi].BoxBorderPt > 0)
                            { dbl = prow.Items[bi].BoxDouble; break; }
                        gapAbove = dbl ? 0.0 : 7.5;
                        break;
                    }
                    if (prow.Items[qi].Line is { } lastLn)
                    {
                        // a paragraph spends its bottom margin above the
                        // table; a choice list (its lines pace on the
                        // option pitch) seats the blanks nearly flush
                        if (Math.Abs(lastLn.LinePt
                            - Converters.HtmlToPdfConverter.SwmOptionPitch) < 0.1)
                            gapAbove = 1.0 - relBoxTop;
                        break;
                    }
                    if (prow.Items[qi].Table is not null) break;
                }
                var relRuleY = relBoxTop + 6.38;
                var relBlankBottom = relBoxTop + 13.5;
                // the shared second label row keys off the lowest
                // first-row label any widget put down
                var tr1Base = 0.0;
                var anyTr2 = false; var tr2Stack = 0;
                foreach (var w in prow.Acks)
                {
                    // a checkbox cell reserves its label slot even when
                    // the label is empty — the shared row keys off it
                    var b = w.Kind == "boolean" && w.Blanks.Count > 0
                        ? relBlankBottom + 9.96
                        : w.Kind == "checkbox" || w.TopLabels.Count > 0
                            ? relRuleY + 8.08 : 0.0;
                    if (b > tr1Base) tr1Base = b;
                    if (w.Labels.Count > 0)
                    {
                        anyTr2 = true;
                        if (w.Labels.Count - 1 > tr2Stack) tr2Stack = w.Labels.Count - 1;
                    }
                }
                var tr2Base = (tr1Base > 0 ? tr1Base : relRuleY + 8.08) + 10.5;
                var relBottom = 2.04 + (anyTr2 ? tr2Base + 9.0 * tr2Stack
                    : tr1Base > 0 ? tr1Base : relBlankBottom);
                if (flow.CurrentY - (gapAbove + relBottom) < flow.BottomMargin)
                    PsBreakPage();
                else flow.AdvanceY(gapAbove);
                var topY = flow.CurrentY;
                var tb2 = new Content.ContentStreamBuilder();
                tb2.SaveState();
                var lres2 = Table.RegisterFont(flow.CurrentPage, "Helvetica-Bold");
                var tableRight = page.Width - 21.98;
                var cellX = tableRight - 22.87 - 112.5 * prow.Acks.Count;
                // a filled-in check: two strokes shaped on the 10.5 pt
                // glyph's box, seated on the blank it marks
                void PsCheck(double gx, double gBase)
                    => tb2.SetLineWidth(1.05)
                          .MoveTo(gx + 0.2, gBase + 3.3)
                          .LineTo(gx + 2.6, gBase + 0.4)
                          .LineTo(gx + 7.6, gBase + 10.3)
                          .Stroke();
                var anyCheckbox = false;
                foreach (var w in prow.Acks)
                {
                    if (w.Kind == "boolean")
                    {
                        var ox = cellX;
                        foreach (var (bw, thick, optLabel, check) in w.Blanks)
                        {
                            // a plain option blank inks only its bottom
                            // rule — the other three sides are
                            // white strokes; the `box` variant is a
                            // full rectangle at triple weight
                            if (thick)
                                tb2.SetLineWidth(2.25).Rectangle(
                                    ox + 1.125, topY - relBlankBottom + 1.125,
                                    bw - 2.25, 13.5 - 2.25).Stroke();
                            else
                                tb2.SetLineWidth(0.75)
                                   .MoveTo(ox, topY - relBlankBottom + 0.375)
                                   .LineTo(ox + bw, topY - relBlankBottom + 0.375).Stroke();
                            if (check)
                                PsCheck(ox + bw / 2 - 3.93, topY - relBlankBottom + 2.4);
                            if (optLabel is not null)
                                tb2.BeginText().SetFont(lres2, 6.0)
                                   .MoveTextPosition(ox, topY - (relBlankBottom + 9.96))
                                   .ShowText(optLabel).EndText();
                            ox += 59.12;
                        }
                    }
                    else
                    {
                        if (w.Kind == "checkbox") anyCheckbox = true;
                        tb2.SetLineWidth(0.75)
                           .MoveTo(cellX, topY - relRuleY)
                           .LineTo(cellX + 104.25, topY - relRuleY).Stroke();
                        if (w.Blanks.Count > 0 && w.Blanks[0].Check)
                            PsCheck(cellX + 104.25 / 2 - 3.93, topY - relRuleY - 2.12);
                        for (var li = 0; li < w.TopLabels.Count; li++)
                            tb2.BeginText().SetFont(lres2, 6.0)
                               .MoveTextPosition(cellX, topY - (relRuleY + 8.08 + 9.0 * li))
                               .ShowText(w.TopLabels[li]).EndText();
                    }
                    for (var li = 0; li < w.Labels.Count; li++)
                        tb2.BeginText().SetFont(lres2, 6.0)
                           .MoveTextPosition(cellX, topY - (tr2Base + 9.0 * li))
                           .ShowText(w.Labels[li]).EndText();
                    cellX += 112.5;
                }
                if (prow.AckInitials is { } initials)
                {
                    // the initials cell: baseline mid-cluster beside a
                    // boolean row, on the shared label row's seat beside
                    // a checkbox — both empirically calibrated
                    var iBase = anyCheckbox
                        ? topY - relBottom + 2.04
                        : topY - (relBoxTop - 0.75 + relBottom) / 2;
                    var ires = Table.RegisterFont(flow.CurrentPage, "Helvetica");
                    tb2.BeginText().SetFont(ires, psFs)
                       .MoveTextPosition(tableRight - 13.62, iBase)
                       .ShowText(initials).EndText();
                }
                // full-height rules also run down the
                // table's right edge — in WHITE; they leave no ink, so
                // none are drawn here
                tb2.RestoreState();
                flow.InjectContentAtCursor(tb2.Build());
                flow.AdvanceY(relBottom);
            }
            else if (prow.HasAck)
            {
                // Widget cluster (the sr-ack DIV generation), all of
                // its geometry exact to 0.01 pt. The widgets stand
                // in document order in fixed-pitch cells against the
                // sheet's right edge, and every blank's bottom rule sits
                // on ONE shared baseline: the deepest widget's own stack
                // — a boolean's bordered box, a checkbox's hair line
                // above its blank, a signature's taller blank — decides
                // how far under the cluster top that is. A boolean's
                // option captions hang below the rule, and each label
                // DIV then takes a fixed slot; an EMPTY label KEEPS its
                // slot, which is what makes every row of a form the
                // same height regardless of which labels it fills in.
                var (maxStack, clusterH) = PsAckClusterGeom(prow);
                var ackN = prow.Acks.Count;
                if (flow.CurrentY - clusterH < flow.BottomMargin) PsBreakPage();
                var ab = new Content.ContentStreamBuilder();
                ab.SaveState();
                var lres = Table.RegisterFont(flow.CurrentPage, "Helvetica-Bold");
                var ruleY = flow.CurrentY - maxStack;
                for (var wi = 0; wi < ackN; wi++)
                {
                    var w = prow.Acks[wi];
                    var wl = page.Width - ackRightInset - ackCellPitch * (ackN - 1 - wi);
                    if (w.Kind == "boolean")
                    {
                        // the picked option (.box) draws its full frame;
                        // an unpicked one only its bottom rule
                        for (var b = 0; b < Math.Min(2, w.Blanks.Count); b++)
                        {
                            var bx = wl + (b == 0 ? 0 : ackBoolBox2);
                            if (w.Blanks[b].Box)
                                // the picked option's frame stands ON the
                                // shared rule: its bottom stroke rides just
                                // above it, the caption stays clear below
                                ab.SetLineWidth(ackBoxStroke).Rectangle(
                                    bx + ackBoxStroke / 2,
                                    ruleY - 0.375 + ackBoxStroke / 2,
                                    ackBoolBoxW - ackBoxStroke,
                                    ackBoolBoxH - ackBoxStroke).Stroke();
                            else
                                ab.SetLineWidth(0.75).MoveTo(bx, ruleY)
                                  .LineTo(bx + ackBoolBoxW, ruleY).Stroke();
                            if (w.Blanks[b].OptLabel is { Length: > 0 } cap)
                                ab.BeginText().SetFont(lres, 6.0)
                                  .MoveTextPosition(wl + (b == 0 ? 0 : ackBoolCap2),
                                      ruleY - ackLabelDrop)
                                  .ShowText(cap).EndText();
                        }
                    }
                    else
                        ab.SetLineWidth(0.75).MoveTo(wl, ruleY)
                          .LineTo(wl + ackBlankW, ruleY).Stroke();
                    // labels below the rule; a boolean's caption line
                    // occupies the first slot
                    var slot0 = w.Kind == "boolean" ? 1 : 0;
                    for (var li = 0; li < w.Labels.Count; li++)
                        if (w.Labels[li].Length > 0)
                            ab.BeginText().SetFont(lres, 6.0)
                              .MoveTextPosition(wl,
                                  ruleY - ackLabelDrop - ackLabelPitch * (li + slot0))
                              .ShowText(w.Labels[li]).EndText();
                }
                ab.RestoreState();
                flow.InjectContentAtCursor(ab.Build());
                flow.AdvanceY(clusterH);
            }
            if (prow.Warn)
            {
                var wb = new Content.ContentStreamBuilder();
                wb.SaveState();
                wb.SetLineWidth(3.75)
                  .MoveTo(psContentX - 0.9, flow.CurrentY)
                  .LineTo(psRowRight + 2.9, flow.CurrentY).Stroke();
                wb.RestoreState();
                flow.InjectContentAtCursor(wb.Build());
                flow.AdvanceY(3.75);
            }
            // a step that carries nothing still puts its number down
            if (psBulletPending)
            {
                var nb = new Content.ContentStreamBuilder();
                nb.SaveState();
                PsDrawBullet(nb, flow.CurrentY - PsAscentFor(psPitch, psFs));
                nb.RestoreState();
                flow.InjectContentAtCursor(nb.Build());
            }
            // the acknowledge column stands beside the content, so a row
            // whose content is the shorter of the two is still as tall as
            // the column - even where none of it falls on the sheet.
            // NOT for the col-full generation: its widgets bank UNDER the
            // content and the drawn cluster has already spent their height
            if (!prow.ColFull
                && ReferenceEquals(psRowStartPage, flow.CurrentPage)
                && prow.AckHeightPt > psRowTop - flow.CurrentY)
                flow.AdvanceY(prow.AckHeightPt - (psRowTop - flow.CurrentY));
            PsDrawClog();
            // the row's bottom margin collapses with the next row's top
            // one rather than adding to it, and is dropped altogether
            // where the sheet ends - so only the top margin is spent
        }
    }

    // height consumed instead of moving the flow cursor, so a nested
    // layout table reports upward and every row advances exactly once.
    private static IEnumerable<Table> LeafTables(Table t)
    {
        foreach (var r in t.Rows)
            foreach (var c in r.Cells)
                foreach (var p in c.Paragraphs)
                    if (p is Table inner)
                    {
                        if (HasNestedTables(inner))
                            foreach (var deeper in LeafTables(inner)) yield return deeper;
                        else yield return inner;
                    }
    }

    private static bool HasNestedTables(Table t)
    {
        foreach (var r in t.Rows)
            foreach (var c in r.Cells)
                foreach (var p in c.Paragraphs)
                    if (p is Table) return true;
        return false;
    }

    private double RenderLayoutTable(Table lt, double originX, double boxW, double startY,
        FlowLayout flow, double marginLeft, HashSet<Table> renderedTables, bool measureOnly = false)
    {
        var originLeft = originX >= 0 ? originX : marginLeft;
        // The layout table's own cell padding sits above and below every
        // block it places — the markup's `cellpadding` nests, so each level
        // of table inset adds its own.
        var lpadTop = lt.DefaultCellPadding?.Top ?? 0;
        var lpadBottom = lt.DefaultCellPadding?.Bottom ?? 0;
        var y = startY;
        foreach (var lrow in lt.Rows)
        {
            var cellCount = lrow.Cells.Count;
            if (cellCount == 0) continue;
            var widths = new double[cellCount];
            var declared = 0.0;
            var undeclared = 0;
            for (var c = 0; c < cellCount; c++)
            {
                var w = lrow.Cells.At(c).Width;
                widths[c] = w > 0 ? w : 0;
                if (w > 0) declared += w; else undeclared++;
            }
            if (undeclared > 0)
            {
                var share = Math.Max(0, boxW - declared) / undeclared;
                for (var c = 0; c < cellCount; c++)
                    if (widths[c] <= 0) widths[c] = share;
            }

            // how tall is this row? measure before placing, so a row
            // that no longer fits can move to a fresh page whole
            if (originX < 0 && !measureOnly)
            {
                var need = 0.0;
                for (var c = 0; c < cellCount; c++)
                {
                    var mcell = lrow.Cells.At(c);
                    var mh = 0.0;
                    foreach (var mp in mcell.Paragraphs)
                    {
                        if (mp is Table mt)
                        {
                            // a cell holding a table of tables is as tall as
                            // placing it would make it — measure it the same way
                            if (HasNestedTables(mt))
                            {
                                mh += RenderLayoutTable(mt, originLeft, widths[c], y, flow, marginLeft, renderedTables, measureOnly: true);
                                continue;
                            }
                            mt.FlowLeftOffset = originLeft;
                            mt.BuildMultiPage(flow.CurrentPage, y, flow.BottomMargin, measureOnly: true);
                            mh += mt.LastRenderedHeight;
                        }
                        else if (mp is Text.TextFragment mtf)
                            mh += Converters.HtmlToPdfConverter.FaceLineHeight("Helvetica",
                                mtf.TextState.FontSize > 0 ? mtf.TextState.FontSize : 12);
                    }
                    need = Math.Max(need, mh);
                }
                if (need > 0) need += lpadTop + lpadBottom;
                if (need > 0 && y - need < flow.BottomMargin
                    && need <= flow.ContentTop - flow.BottomMargin)
                {
                    flow.ForceNewPage();
                    y = flow.CurrentY;
                }
            }

            var rowTop = y - lpadTop;
            var rowAdvance = 0.0;
            var cx = originLeft;
            for (var c = 0; c < cellCount; c++)
            {
                var lcell = lrow.Cells.At(c);
                var cy = rowTop;
                foreach (var lp in lcell.Paragraphs)
                {
                    if (lp is Table lin)
                    {
                        if (!measureOnly) renderedTables.Add(lin);
                        lin.HtmlEngineMetrics = true;
                        lin.HtmlLayoutWrap = true;
                        // a table that itself only places other tables keeps placing them
                        if (HasNestedTables(lin))
                        {
                            cy -= RenderLayoutTable(lin, cx, widths[c], cy, flow, marginLeft, renderedTables, measureOnly);   // nested: no paging
                            continue;
                        }
                        // a floated cell resolves its percentage a second
                        // time: the region is half the cell, hung on its
                        // right edge, and the table may overflow past it
                        var region = lcell.Alignment == HorizontalAlignment.Right
                            ? widths[c] / 2 : widths[c];
                        Converters.HtmlToPdfConverter.ApplyAutoWidths(lin, region, fill: !lin.HtmlAutoWidth);
                        Converters.HtmlToPdfConverter.ApplyAutoRowHeights(lin);
                        var lx = lcell.Alignment == HorizontalAlignment.Right
                            ? cx + widths[c] - region : cx;
                        lin.FlowLeftOffset = lx;
                        var lcontents = lin.BuildMultiPage(flow.CurrentPage, cy, flow.BottomMargin,
                            measureOnly: measureOnly);
                        if (!measureOnly)
                        {
                            if (lcontents.Count > 0) flow.InjectContentAtCursor(lcontents[0]);
                            if (lin.LastGraphDraws.Count > 0)
                                foreach (var gc in lin.LastGraphDraws[0])
                                    flow.InjectContentAtCursor(gc);
                            if (!flow.HasOverflowed && lin.LastImageDraws.Count > 0)
                                foreach (var (data, rect) in lin.LastImageDraws[0])
                                    flow.CurrentPage.AddImage(data, rect);
                        }
                        cy -= lin.LastRenderedHeight;
                    }
                    else if (lp is Text.TextFragment ltf
                             && !string.IsNullOrWhiteSpace(ltf.Text))
                    {
                        var lfs = ltf.TextState.FontSize > 0 ? ltf.TextState.FontSize : 12;
                        var lface = ltf.TextState.IsBold ? "Helvetica-Bold" : "Helvetica";
                        if (!measureOnly)
                        {
                            var lres = Table.RegisterFont(flow.CurrentPage, lface);
                            var lb = new Content.ContentStreamBuilder();
                            lb.SaveState();
                            lb.BeginText().SetFont(lres, lfs)
                              .MoveTextPosition(cx, cy - lfs)
                              .ShowText(ltf.Text!).EndText();
                            lb.RestoreState();
                            flow.InjectContentAtCursor(lb.Build());
                        }
                        cy -= Converters.HtmlToPdfConverter.FaceLineHeight(lface, lfs);
                    }
                    else if (lp is Text.TextFragment lws)
                        // A blank (or &nbsp;-only) cell is still a line box:
                        // it takes its own font's line height, not a nominal one.
                        cy -= Converters.HtmlToPdfConverter.FaceLineHeight("Helvetica",
                            lws.TextState.FontSize > 0 ? lws.TextState.FontSize : 12);
                }
                rowAdvance = Math.Max(rowAdvance, rowTop - cy);
                cx += widths[c];
            }
            y -= lpadTop + rowAdvance + lpadBottom;
        }
        return startY - y;
    }

    // Render a real HTML <table> as a generator Table at the flow cursor,
    // paginating like a page-level Table paragraph (same logic as the
    // `para is Table` branch below).
    private void RenderHtmlTable(Table t, FlowLayout flow, Page page, double marginLeft, double marginTop,
        List<(byte[] content, double width, double height)> overflowPages,
        Dictionary<int, List<(byte[] data, Rectangle rect)>> overflowImages)
    {
        var tablePage = flow.CurrentPage;
        t.FlowLeftOffset = marginLeft;
        var spillTopMargin = PageInfo?.Margin is { TopTouched: true } dm ? dm.Top : marginTop;

        // Page-break-before: if the whole table doesn't fit in the space left
        // on the current page but would fit on a fresh one, move it to the next
        // page (keeps a table together — the common HTML expectation). Measure
        // its single-page height from the content top first.
        t.BuildMultiPage(tablePage, flow.ContentTop, flow.BottomMargin, measureOnly: true);
        var tableH = t.LastRenderedHeight;
        var avail = flow.CurrentY - flow.BottomMargin;
        var pageBudget = flow.ContentTop - flow.BottomMargin;
        // …but the form-grid dialect SPLITS a section table
        // instead (the band row stays on the page foot,
        // the header/data rows continue overleaf).
        if (tableH > avail + 0.5 && tableH <= pageBudget + 0.5
            && flow.CurrentY < flow.ContentTop - 0.5
            && !t.HonorCellTtfFaces)
            flow.ForceNewPage();

        var pageContents = t.BuildMultiPage(tablePage, flow.CurrentY, flow.BottomMargin, spillTopMargin,
            contentFlow: true);
        var tableImages = t.LastImageDraws;
        var tableGraphs = t.LastGraphDraws;
        // Inject the first slice at the flow's CURRENT page position (the start
        // page, or the current overflow buffer once the flow has page-broken) —
        // NOT directly on the start page, which is where the cursor no longer is.
        flow.InjectContentAtCursor(pageContents[0]);
        if (tableGraphs.Count > 0)
            foreach (var gc in tableGraphs[0])
                flow.InjectContentAtCursor(gc);
        // Cell images: drawn on the live start page (only correct before the flow
        // overflows — overflowed cell images are rare and out of scope here).
        if (!flow.HasOverflowed && tableImages.Count > 0)
            foreach (var (data, rect) in tableImages[0])
                tablePage.AddImage(data, rect);
        if (pageContents.Count == 1)
        {
            flow.AdvanceY(t.LastRenderedHeight);
        }
        else
        {
            for (var pi = 1; pi < pageContents.Count - 1; pi++)
            {
                if (pi < tableImages.Count && tableImages[pi].Count > 0)
                    overflowImages[overflowPages.Count] = tableImages[pi];
                overflowPages.Add((pageContents[pi], tablePage.Width, tablePage.Height));
            }
            var lastIdx = pageContents.Count - 1;
            var lastSlot = flow.ContinueOnPrebuiltSpill(pageContents[lastIdx], t.LastPageEndY);
            if (lastIdx < tableImages.Count && tableImages[lastIdx].Count > 0)
                overflowImages[lastSlot] = tableImages[lastIdx];
        }
    }

    // Render a single-font inline-emphasis fragment (one <font face size>
    // wrapper holding only b/u/i runs) as embedded styled runs with
    // stroked underlines, on the half-leading line model: for a run of
    // s px, box top/bottom = hhea ascent/descent plus half of
    // (round(lineHeight px) - ascent - descent), maxed against the
    // 16px serif strut; the first baseline sits `above` under the
    // cursor and lines advance by (above + below).
    private bool RenderInlineEmphasisRuns(string iface, double ipt,
        List<(string text, bool bold, bool underline, bool italic)> iruns,
        FlowLayout flow, Page page, double marginLeft, double marginRight)
    {
        var regTtf = Text.FontRepository.GetTtfData(iface);
        if (regTtf is null) return false;
        var boldTtf = Text.FontRepository.GetTtfData(iface + " Bold") ?? regTtf;
        var regData = new Text.FontData(iface, Text.FontType.TrueType);
        regData.SetTtfData(regTtf);
        var boldData = new Text.FontData(iface + " Bold", Text.FontType.TrueType);
        boldData.SetTtfData(boldTtf);
        var mReg = Text.TextPaginator.CreateMeasurer(iface, ipt, regData);
        var mBold = Text.TextPaginator.CreateMeasurer(iface, ipt, boldData);

        // Line-box metrics in px (pt = px * 0.75), em = 2048.
        var sPx = ipt / 0.75;
        const double em = 2048.0;
        const double faceAscent = 1854, faceDescent = 434, faceLineGap = 67;
        var ascPx = faceAscent * sPx / em;
        var descPx = faceDescent * sPx / em;
        var lPx = Math.Round(sPx * (faceAscent + faceDescent + faceLineGap) / em,
            MidpointRounding.AwayFromZero);
        var halfLead = (lPx - ascPx - descPx) / 2;
        const double strutTop = 14.3984375, strutBottom = 3.6015625;
        var above = Math.Max(ascPx + halfLead, strutTop);
        var below = Math.Max(descPx + halfLead, strutBottom);
        var firstBaselinePt = above * 0.75;
        var linePitchPt = (above + below) * 0.75;

        // Tokenise the styled runs into word/space atoms for a greedy
        // wrap; a line break drops the space it breaks on.
        var atoms = new List<(string text, bool bold, bool underline, bool space)>();
        foreach (var run in iruns)
        {
            var t = run.text;
            var i0 = 0;
            while (i0 < t.Length)
            {
                var isSpace = t[i0] == ' ';
                var i1 = i0;
                while (i1 < t.Length && (t[i1] == ' ') == isSpace) i1++;
                atoms.Add((t[i0..i1], run.bold, run.underline, isSpace));
                i0 = i1;
            }
        }

        var contentW = page.Width - marginLeft - marginRight;
        var lines2 = new List<List<(string text, bool bold, bool underline, double x, double w)>>();
        var cur = new List<(string, bool, bool, double, double)>();
        double curW = 0;
        foreach (var at in atoms)
        {
            var w = at.bold ? mBold(at.text) : mReg(at.text);
            if (!at.space && curW + w > contentW && cur.Count > 0)
            {
                // Drop the trailing space atom the wrap breaks on.
                while (cur.Count > 0 && cur[^1].Item1.Trim().Length == 0)
                    cur.RemoveAt(cur.Count - 1);
                lines2.Add(cur);
                cur = new List<(string, bool, bool, double, double)>();
                curW = 0;
            }
            cur.Add((at.text, at.bold, at.underline, curW, w));
            curW += w;
        }
        if (cur.Count > 0) lines2.Add(cur);
        if (lines2.Count == 0) return false;

        var frameTop = flow.CurrentY;
        var fontDict2 = Table.ResolvePageFontDict(flow.CurrentPage);
        var b2 = new Content.ContentStreamBuilder();
        for (var li = 0; li < lines2.Count; li++)
        {
            var baseline = frameTop - firstBaselinePt - li * linePitchPt;
            // Merge adjacent same-style atoms into one show per run.
            var line = lines2[li];
            var ri = 0;
            while (ri < line.Count)
            {
                var rj = ri;
                while (rj + 1 < line.Count
                       && line[rj + 1].Item2 == line[ri].Item2
                       && line[rj + 1].Item3 == line[ri].Item3) rj++;
                var textRun = string.Concat(line.GetRange(ri, rj - ri + 1)
                    .ConvertAll(a => a.Item1));
                var xOff = line[ri].Item4;
                var runW = line[rj].Item4 + line[rj].Item5 - xOff;
                var bold2 = line[ri].Item2;
                var (res2, hex2) = Text.Type0FontEmbedder.Embed(fontDict2,
                    bold2 ? boldTtf : regTtf,
                    bold2 ? iface + " Bold" : iface,
                    textRun, stripSpacesInBaseFont: true);
                b2.BeginText();
                b2.SetFont(res2, ipt);
                b2.SetTextMatrix(1, 0, 0, 1, marginLeft + xOff, baseline);
                b2.ShowTextHex(hex2);
                b2.EndText();
                if (line[ri].Item3)
                {
                    // Stroked underline: a 0.1em-thick band whose top
                    // edge sits 0.1em below the baseline, spanning the
                    // run's advances.
                    b2.SaveState();
                    b2.SetStrokeGray(0);
                    b2.SetLineWidth(0.1 * ipt);
                    var uy = baseline - 0.15 * ipt;
                    b2.MoveTo(marginLeft + xOff, uy)
                      .LineTo(marginLeft + xOff + runW, uy)
                      .Stroke();
                    b2.RestoreState();
                }
                ri = rj + 1;
            }
        }
        flow.InjectContentAtCursor(b2.Build());
        // The block consumes its content extent without the outer
        // half-leadings: ascent + (n-1) pitches + descent.
        var blockH = (ascPx + (lines2.Count - 1) * (above + below) + descPx) * 0.75;
        flow.AdvanceY(blockH);
        return true;
    }

    private void RenderUaSerifChunk(string chunk, double uaWrapPt, HtmlFragment html,
        FlowLayout flow, double marginLeft)
    {
        var uaBody = System.Text.RegularExpressions.Regex.Replace(chunk,
            @"(?s)<head\b.*?</head>|<!--.*?-->", "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        // only p/h elements and inline span/emphasis tags carry text;
        // structural tags (html, body, div, ...) leave the scan so their
        // names never surface as content
        uaBody = System.Text.RegularExpressions.Regex.Replace(uaBody,
            @"</(?!p\b|h[1-6]\b|span\b|strong\b|b\b|em\b|i\b|br\b)[^>]*>" +
            @"|<(?!/|p\b|h[1-6]\b|span\b|strong\b|b\b|em\b|i\b|br\b)[^>]*>", "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var uaB = new Content.ContentStreamBuilder();
        uaB.SaveState();
        var uaTimes = Table.RegisterFont(flow.CurrentPage, "Times-Roman");
        var uaTimesB = Table.RegisterFont(flow.CurrentPage, "Times-Bold");
        double UaMeasure(string t, bool bold, double fsM)
        {
            try
            {
                return Text.FontRepository.FindFont(bold ? "Times-Bold" : "Times-Roman")
                    ?.MeasureString(t, fsM) ?? t.Length * fsM * 0.5;
            }
            catch { return t.Length * fsM * 0.5; }
        }
        var uaAfterHead = false;
        // at a line-box edge (chunk start, or after a blank <br> line)
        // the next baseline seats at the ascent drop, not a full pitch
        var uaAtBoxEdge = true;
        foreach (System.Text.RegularExpressions.Match em in
            System.Text.RegularExpressions.Regex.Matches(uaBody,
                @"(?s)<(?<tag>p|h[1-6])\b[^>]*>(?<in>.*?)</\k<tag>>|<br\b[^>]*>|(?<bare>[^<]+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            var isHead = false;
            string inner;
            if (em.Groups["tag"].Success)
            {
                isHead = em.Groups["tag"].Value.StartsWith("h",
                    StringComparison.OrdinalIgnoreCase);
                inner = em.Groups["in"].Value;
            }
            else
            {
                inner = em.Groups["bare"].Value;
                if (inner.Trim().Length == 0) continue;
            }
            // inline runs: span colours and strong/b bold, inherited
            var uaRuns = new List<(string T, Color? C, bool Bold, bool Styled, bool Lead, bool Trail)>();
            var uaStack = new Stack<(Color?, bool)>();
            Color? uaC = null;
            var uaStyled = false;
            var uaBold = isHead ? 1 : 0;
            var rp = 0;
            // edge whitespace decides word seams between adjacent runs;
            // StripHtmlTags trims it, so read it off the raw slice
            var uaForceLead = false;
            void EmitRun(string raw)
            {
                if (raw.Length == 0) return;
                var lead = uaForceLead || char.IsWhiteSpace(raw[0]);
                var t = System.Text.RegularExpressions.Regex.Replace(
                    HtmlFragment.StripHtmlTags(raw), @"\s+", " ").Trim();
                if (t.Length == 0) { uaForceLead = true; return; }
                uaRuns.Add((t, uaC, uaBold > 0, uaStyled, lead,
                    char.IsWhiteSpace(raw[^1])));
                uaForceLead = false;
            }
            foreach (System.Text.RegularExpressions.Match tg in
                System.Text.RegularExpressions.Regex.Matches(inner, @"<[^>]*>"))
            {
                EmitRun(inner[rp..tg.Index]);
                rp = tg.Index + tg.Length;
                var tag2 = tg.Value;
                if (System.Text.RegularExpressions.Regex.IsMatch(tag2, @"^<\s*/\s*span",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                { if (uaStack.Count > 0) (uaC, uaStyled) = uaStack.Pop(); }
                else if (System.Text.RegularExpressions.Regex.IsMatch(tag2, @"^<\s*span",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    uaStack.Push((uaC, uaStyled));
                    var st = System.Text.RegularExpressions.Regex.Match(tag2,
                        @"style\s*=\s*(['""])(?<s>[^'""]*)\1",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (st.Success)
                    {
                        var cm2 = System.Text.RegularExpressions.Regex.Match(
                            st.Groups["s"].Value, @"(?<![-\w])color\s*:\s*([^;]+)",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (cm2.Success && Converters.HtmlToPdfConverter
                                .ParseCssColor(cm2.Groups[1].Value.Trim()) is { } cc2)
                        { uaC = cc2; uaStyled = true; }
                    }
                }
                else if (System.Text.RegularExpressions.Regex.IsMatch(tag2,
                    @"^<\s*(strong|b)[\s>]",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase)) uaBold++;
                else if (System.Text.RegularExpressions.Regex.IsMatch(tag2,
                    @"^<\s*/\s*(strong|b)[\s>]",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase)) uaBold--;
            }
            EmitRun(inner[rp..]);
            if (uaRuns.Count == 0)
            {
                // a <p> holding only <br> keeps one blank line box;
                // a truly empty <p> takes nothing
                if (System.Text.RegularExpressions.Regex.IsMatch(inner, @"<br\b",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    flow.AdvanceY(UaSerifPitchPt);
                    uaAtBoxEdge = true;
                    uaAfterHead = false;
                }
                continue;
            }
            var uaFs = isHead ? UaSerifH2Pt : UaSerifPt;
            // wrap the run stream at the UA text width
            var uaLines = new List<List<(double X, string T, Color? C, bool Bold)>>();
            var uaCur = new List<(double, string, Color?, bool)>();
            var uaLineStyled = false;
            double uaX = 0;
            var uaPrevOpen = false;   // previous run ended mid-word
            for (var ri = 0; ri < uaRuns.Count; ri++)
            {
                var (rt, rc, rb, rstyled, rlead, rtrail) = uaRuns[ri];
                var runWords = rt.Split(' ');
                for (var wi = 0; wi < runWords.Length; wi++)
                {
                    var word = runWords[wi];
                    if (word.Length == 0) continue;
                    var w2 = UaMeasure(word, rb, uaFs);
                    // a run starting without whitespace continues the
                    // previous run's word ("opmaak" + "." = "opmaak.")
                    var glue = wi == 0 && uaPrevOpen && !rlead;
                    if (glue && uaCur.Count > 0)
                        uaX -= UaMeasure(" ", rb, uaFs);
                    if (!glue && uaCur.Count > 0 && uaX + w2 > uaWrapPt)
                    {
                        uaLines.Add(uaCur);
                        uaCur = new List<(double, string, Color?, bool)>();
                        uaX = 0;
                    }
                    uaCur.Add((uaX, word, rc, rb));
                    uaX += w2 + UaMeasure(" ", rb, uaFs);
                    if (rstyled) uaLineStyled = true;
                }
                uaPrevOpen = !rtrail;
            }
            if (uaCur.Count > 0) uaLines.Add(uaCur);
            var firstOfElement = true;
            foreach (var line2 in uaLines)
            {
                var drop = uaAtBoxEdge ? UaSerifSeatPt
                    : isHead && firstOfElement ? UaSerifH2BeforePt
                    : uaAfterHead ? UaSerifH2AfterPt
                    : firstOfElement && uaLineStyled ? UaSerifMixedPitchPt
                    : UaSerifPitchPt;
                flow.AdvanceY(drop);
                var baseY = flow.CurrentY;
                foreach (var (lx, lt, lc, lb) in line2)
                {
                    if (lc is { } lcc)
                        uaB.SetFillColor(lcc.R / 255.0, lcc.G / 255.0, lcc.B / 255.0);
                    uaB.BeginText().SetFont(lb ? uaTimesB : uaTimes, uaFs)
                       .MoveTextPosition(marginLeft + lx, baseY)
                       .ShowText(lt).EndText();
                    if (lc is not null) uaB.SetFillColor(0, 0, 0);
                }
                uaAfterHead = isHead;
                uaAtBoxEdge = false;
                firstOfElement = false;
            }
        }
        uaB.RestoreState();
        flow.InjectContentAtCursor(uaB.Build());
        // close the last line box so following content (a table)
        // starts at the box bottom, not the baseline
        if (!uaAtBoxEdge) flow.AdvanceY(UaSerifPitchPt - UaSerifSeatPt);
    }

    private void RenderUaSerifTable(string chunk, double uaBoxPt, FlowLayout flow, Page page,
        double marginLeft)
    {
        const System.Text.RegularExpressions.RegexOptions UaRx =
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
            | System.Text.RegularExpressions.RegexOptions.Singleline;
        var twm = System.Text.RegularExpressions.Regex.Match(chunk,
            @"<table\b[^>]*\bwidth\s*=\s*[""']?(\d+)(?![\d%])", UaRx);
        var tableW = twm.Success
            ? double.Parse(twm.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture) * 0.75
            : uaBoxPt;
        var zeroPad = System.Text.RegularExpressions.Regex.IsMatch(chunk,
            @"<table\b[^>]*\bcellpadding\s*=\s*[""']?0[""']?", UaRx);
        var pad = zeroPad ? 0.0 : UaTdPadPt;

        // columns: declared pt widths from the colgroup, else
        // percentage widths off the first row's cells
        var colWs = new List<double>();
        foreach (System.Text.RegularExpressions.Match cm in
            System.Text.RegularExpressions.Regex.Matches(chunk,
                @"<col\b[^>]*width\s*:\s*([\d.]+)pt", UaRx))
            colWs.Add(double.Parse(cm.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture));
        if (colWs.Count == 0)
            foreach (System.Text.RegularExpressions.Match cm in
                System.Text.RegularExpressions.Regex.Matches(chunk,
                    @"<td\b[^>]*width\s*:\s*([\d.]+)%", UaRx))
                colWs.Add(tableW * double.Parse(cm.Groups[1].Value,
                    System.Globalization.CultureInfo.InvariantCulture) / 100.0);
        if (colWs.Count == 0) return;

        // faces: css-named family with real metrics, the
        // fallback face for glyphs the primary lacks, and the
        // UA serif default drawn as the base-14 Times
        var faces = new Dictionary<string,
            (byte[] Ttf, string Name, Text.GlyphOutlineParser Gp, Text.TrueTypeParser Tp)>();
        (byte[], string, Text.GlyphOutlineParser, Text.TrueTypeParser)? Face(string family)
        {
            var key = family.ToLowerInvariant();
            if (faces.TryGetValue(key, out var have)) return have;
            var name = System.Globalization.CultureInfo.InvariantCulture
                .TextInfo.ToTitleCase(key);
            // the repository's ttf data drops the legacy kern
            // table wrap measurement relies on — read the system
            // file itself when the face is a known one
            var file = key switch
            {
                "calibri" => "calibri.ttf",
                "times new roman" => "times.ttf",
                "microsoft sans serif" => "micross.ttf",
                _ => null,
            };
            byte[]? ttf = null;
            if (file is not null)
                try
                {
                    ttf = System.IO.File.ReadAllBytes(System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.Fonts), file));
                }
                catch { ttf = null; }
            if (ttf is null)
                try { ttf = Text.FontRepository.GetTtfData(name); }
                catch { return null; }
            if (ttf is null) return null;
            try
            {
                var tp2 = new Text.TrueTypeParser(ttf);
                tp2.Parse();
                var got = (ttf, name, new Text.GlyphOutlineParser(ttf), tp2);
                faces[key] = got;
                return got;
            }
            catch { return null; }
        }
        // css line box: whole-css-px rounded hhea line height
        double CssBox(Text.TrueTypeParser t, double s) =>
            0.75 * Math.Floor((t.Ascent + Math.Abs(t.Descent) + t.LineGap)
                * (s * 96.0 / 72.0) / t.UnitsPerEm + 0.5);
        double SeatIn(Text.TrueTypeParser t, double s, double box) =>
            t.UsWinAscent * s / t.UnitsPerEm
            + (box - (t.UsWinAscent + t.UsWinDescent) * s / t.UnitsPerEm) / 2;
        // the paste's U+FFFD stays as-is; it draws
        // through the system fallback face, which carries the
        // replacement-character glyph
        const char UaFallbackChar = '�';
        const string UaFallbackFamily = "Microsoft Sans Serif";

        double GlyphAdv(Text.GlyphOutlineParser gp, int gid, double s) =>
            gp.GetAdvanceWidth(gid) * s / (gp.UnitsPerEm > 0 ? gp.UnitsPerEm : 1000.0);
        double StyledWidth(string t, Text.GlyphOutlineParser gp,
            Text.GlyphOutlineParser? gpFb, double s)
        {
            double w = 0;
            var prev = -1;
            foreach (var c in t)
            {
                if (c == UaFallbackChar && gpFb is not null)
                {
                    var gf = gpFb.CMap.TryGetValue(c, out var g2) ? g2 : 0;
                    w += GlyphAdv(gpFb, gf, s);
                    prev = -1;
                    continue;
                }
                var gid = gp.CMap.TryGetValue(c, out var g) ? g : 0;
                if (prev >= 0)
                    w += gp.GetKernAdjustment(prev, gid) * s
                         / (gp.UnitsPerEm > 0 ? gp.UnitsPerEm : 1000.0);
                w += GlyphAdv(gp, gid, s);
                prev = gid;
            }
            return w;
        }

        var fontDict = Table.ResolvePageFontDict(flow.CurrentPage);
        var uaTimes2 = Table.RegisterFont(flow.CurrentPage, "Times-Roman");
        double TimesWidth(string t)
        {
            try
            {
                return Text.FontRepository.FindFont("Times-Roman")
                    ?.MeasureString(t, UaSerifPt) ?? t.Length * UaSerifPt * 0.5;
            }
            catch { return t.Length * UaSerifPt * 0.5; }
        }

        var tb = new Content.ContentStreamBuilder();
        tb.SaveState();
        var topD = page.Height - flow.CurrentY;   // top-down cursor
        var totalH = 0.0;

        foreach (System.Text.RegularExpressions.Match rm in
            System.Text.RegularExpressions.Regex.Matches(chunk,
                @"<tr(?<a>[^>]*)>(?<in>.*?)</tr>", UaRx))
        {
            var declH = 0.0;
            var dhm = System.Text.RegularExpressions.Regex.Match(
                rm.Groups["a"].Value, @"height\s*:\s*([\d.]+)pt", UaRx);
            if (dhm.Success)
                declH = double.Parse(dhm.Groups[1].Value,
                    System.Globalization.CultureInfo.InvariantCulture);

            var cells = new List<(string Text, double Fs, string? Family,
                Color? Bg, Color? EdgeColor, bool[] Solid,
                List<string> Lines, List<double> Boxes)>();
            foreach (System.Text.RegularExpressions.Match dm in
                System.Text.RegularExpressions.Regex.Matches(rm.Groups["in"].Value,
                    @"<td(?<a>[^>]*)>(?<in>.*?)</td>", UaRx))
            {
                var attrs = dm.Groups["a"].Value;
                var text = System.Text.RegularExpressions.Regex.Replace(
                    HtmlFragment.StripHtmlTags(dm.Groups["in"].Value),
                    @"\s+", " ").Trim();
                var fs = UaSerifPt;
                var fsm = System.Text.RegularExpressions.Regex.Match(attrs,
                    @"font-size\s*:\s*([\d.]+)pt", UaRx);
                if (fsm.Success)
                    fs = double.Parse(fsm.Groups[1].Value,
                        System.Globalization.CultureInfo.InvariantCulture);
                string? family = null;
                var ffm = System.Text.RegularExpressions.Regex.Match(attrs,
                    @"font-family\s*:\s*([^;""']+)", UaRx);
                if (ffm.Success) family = ffm.Groups[1].Value.Trim();
                Color? bg = null;
                var bgm = System.Text.RegularExpressions.Regex.Match(attrs,
                    @"background[^;]*?(#[0-9a-f]{6})", UaRx);
                if (bgm.Success)
                    bg = Converters.HtmlToPdfConverter.ParseCssColor(bgm.Groups[1].Value);
                // border-style lists top right bottom left
                var solid = new bool[4];
                var bsm = System.Text.RegularExpressions.Regex.Match(attrs,
                    @"border-style\s*:\s*([^;]+)", UaRx);
                if (bsm.Success)
                {
                    var toks = bsm.Groups[1].Value.Trim().Split(' ',
                        StringSplitOptions.RemoveEmptyEntries);
                    for (var e = 0; e < 4; e++)
                        solid[e] = string.Equals(
                            toks[Math.Min(e, toks.Length - 1)], "solid",
                            StringComparison.OrdinalIgnoreCase);
                    if (toks.Length == 2) { solid[2] = solid[0]; solid[3] = solid[1]; }
                }
                Color? edgeColor = null;
                var bcm = System.Text.RegularExpressions.Regex.Match(attrs,
                    @"border-color\s*:\s*(#[0-9a-f]{6})", UaRx);
                if (bcm.Success)
                    edgeColor = Converters.HtmlToPdfConverter.ParseCssColor(bcm.Groups[1].Value);
                cells.Add((text, fs, family, bg, edgeColor, solid,
                    new List<string>(), new List<double>()));
            }
            if (cells.Count == 0) continue;

            // wrap each cell and size its line boxes
            var rowContentH = 0.0;
            for (var ci = 0; ci < cells.Count; ci++)
            {
                var cell = cells[ci];
                var colW = colWs[Math.Min(ci, colWs.Count - 1)];
                var leftInset = cell.Solid[3] ? UaTableEdgePt : 0.0;
                var availW = colW - leftInset - 2 * pad;
                var styled = cell.Family is not null && Face(cell.Family) is not null;
                var fb = Face(UaFallbackFamily);
                // lines break by the real face's
                // metrics even where we draw the base-14 serif —
                // measure with the system TTF when it resolves
                var measureFace = styled
                    ? Face(cell.Family!)
                    : Face("Times New Roman");
                double WordW(string w)
                {
                    if (measureFace is { } mf)
                        return StyledWidth(w, mf.Item3, fb?.Item3, cell.Fs);
                    double mw = 0;
                    foreach (System.Text.RegularExpressions.Match sm in
                        System.Text.RegularExpressions.Regex.Matches(w,
                            $@"[^{UaFallbackChar}]+|{UaFallbackChar}+"))
                        mw += sm.Value[0] == UaFallbackChar && fb is not null
                            ? StyledWidth(sm.Value, fb.Value.Item3, null, cell.Fs)
                            : TimesWidth(sm.Value);
                    return mw;
                }
                var cur = "";
                foreach (var w in cell.Text.Split(' ',
                    StringSplitOptions.RemoveEmptyEntries))
                {
                    var probe = cur.Length == 0 ? w : cur + " " + w;
                    if (cur.Length > 0 && WordW(probe) > availW)
                    { cell.Lines.Add(cur); cur = w; }
                    else cur = probe;
                }
                if (cur.Length > 0) cell.Lines.Add(cur);
                foreach (var ln in cell.Lines)
                {
                    double box;
                    if (styled)
                    {
                        var f = Face(cell.Family!)!.Value;
                        box = CssBox(f.Item4, cell.Fs);
                        if (ln.IndexOf(UaFallbackChar) >= 0 && fb is not null)
                            box = Math.Max(box, CssBox(fb.Value.Item4, cell.Fs));
                    }
                    else box = UaSerifPitchPt;
                    cell.Boxes.Add(box);
                }
                var sum = 0.0;
                foreach (var b in cell.Boxes) sum += b;
                rowContentH = Math.Max(rowContentH, sum);
                cells[ci] = cell;
            }

            var edged = false;
            foreach (var c in cells) if (c.Solid[0] || c.Solid[2]) edged = true;
            var rowH = Math.Max(
                declH > 0 ? declH + (edged ? 2 * UaTableEdgePt : 0) : 0,
                rowContentH + 2 * pad);

            // paint: fills, then edges, then text
            var cellL = marginLeft;
            for (var ci = 0; ci < cells.Count; ci++)
            {
                var cell = cells[ci];
                var colW = colWs[Math.Min(ci, colWs.Count - 1)];
                var cellR = cellL + colW;
                if (cell.Bg is { } bgc)
                    tb.SetFillColor(bgc.R / 255.0, bgc.G / 255.0, bgc.B / 255.0)
                      .Rectangle(cellL, page.Height - (topD + rowH), colW, rowH)
                      .Fill();
                if (cell.Solid[0])
                {
                    var ec = cell.EdgeColor ?? Color.Black;
                    tb.SetStrokeColor(ec.R / 255.0, ec.G / 255.0, ec.B / 255.0)
                      .SetLineWidth(UaTableEdgePt)
                      .MoveTo(cellL, page.Height - (topD + UaTableEdgePt / 2))
                      .LineTo(cellR, page.Height - (topD + UaTableEdgePt / 2))
                      .Stroke();
                }
                if (cell.Solid[2])
                    tb.SetStrokeColor(0, 0, 0).SetLineWidth(UaTableEdgePt)
                      .MoveTo(cellL, page.Height - (topD + rowH - UaTableEdgePt / 2))
                      .LineTo(cellR, page.Height - (topD + rowH - UaTableEdgePt / 2))
                      .Stroke();
                if (cell.Solid[3])
                    tb.SetStrokeColor(0, 0, 0).SetLineWidth(UaTableEdgePt)
                      .MoveTo(cellL + UaTableEdgePt / 2, page.Height - topD)
                      .LineTo(cellL + UaTableEdgePt / 2, page.Height - (topD + rowH))
                      .Stroke();

                if (cell.Lines.Count > 0)
                {
                    var styled = cell.Family is not null && Face(cell.Family) is not null;
                    var fb = Face(UaFallbackFamily);
                    // the fill colour above still governs — text is black
                    tb.SetFillColor(0, 0, 0);
                    var sum = 0.0;
                    foreach (var b in cell.Boxes) sum += b;
                    var contentTop = topD + (rowH - sum) / 2;
                    double pitch, seat1;
                    if (styled)
                    {
                        var f = Face(cell.Family!)!.Value;
                        pitch = CssBox(f.Item4, cell.Fs);
                        seat1 = SeatIn(f.Item4, cell.Fs, cell.Boxes[0]);
                    }
                    else
                    {
                        pitch = UaSerifPitchPt;
                        seat1 = UaSerifSeatPt + (cell.Boxes[0] - UaSerifPitchPt) / 2;
                    }
                    var textX = cellL + (cell.Solid[3] ? UaTableEdgePt : 0) + pad;
                    for (var li = 0; li < cell.Lines.Count; li++)
                    {
                        var baseD = contentTop + seat1 + li * pitch;
                        var py = page.Height - baseD;
                        var ln = cell.Lines[li];
                        if (styled)
                        {
                            var f = Face(cell.Family!)!.Value;
                            var x = textX;
                            // split at fallback-glyph boundaries
                            foreach (System.Text.RegularExpressions.Match sm in
                                System.Text.RegularExpressions.Regex.Matches(ln,
                                    $@"[^{UaFallbackChar}]+|{UaFallbackChar}+"))
                            {
                                var seg = sm.Value;
                                var isFb = seg[0] == UaFallbackChar && fb is not null;
                                var (ttf, name, gp, _) = isFb ? fb!.Value : f;
                                var (res, hex) = Text.Type0FontEmbedder.Embed(
                                    fontDict, ttf, name, seg,
                                    stripSpacesInBaseFont: true);
                                tb.BeginText().SetFont(res, cell.Fs)
                                  .MoveTextPosition(x, py);
                                if (StepKernAdjustments(seg, gp) is { } adj)
                                    tb.ShowTextHexKerned(hex, adj);
                                else tb.ShowTextHex(hex);
                                tb.EndText();
                                x += StyledWidth(seg, gp,
                                    isFb ? null : fb?.Item3, cell.Fs);
                            }
                        }
                        else
                        {
                            // base-14 Times for the serif default; the
                            // fallback glyph alone goes through its
                            // embedded face
                            var x = textX;
                            foreach (System.Text.RegularExpressions.Match sm in
                                System.Text.RegularExpressions.Regex.Matches(ln,
                                    $@"[^{UaFallbackChar}]+|{UaFallbackChar}+"))
                            {
                                var seg = sm.Value;
                                if (seg[0] == UaFallbackChar && fb is not null)
                                {
                                    var (ttf, name, gp, _) = fb.Value;
                                    var (res, hex) = Text.Type0FontEmbedder.Embed(
                                        fontDict, ttf, name, seg,
                                        stripSpacesInBaseFont: true);
                                    tb.BeginText().SetFont(res, cell.Fs)
                                      .MoveTextPosition(x, py)
                                      .ShowTextHex(hex).EndText();
                                    x += StyledWidth(seg, gp, null, cell.Fs);
                                }
                                else
                                {
                                    tb.BeginText().SetFont(uaTimes2, cell.Fs)
                                      .MoveTextPosition(x, py)
                                      .ShowText(seg).EndText();
                                    x += TimesWidth(seg);
                                }
                            }
                        }
                    }
                }
                cellL = cellR;
            }
            topD += rowH;
            totalH += rowH;
        }
        tb.RestoreState();
        flow.InjectContentAtCursor(tb.Build());
        flow.AdvanceY(totalH);
    }

    private void RenderHtmlBlocks(string chunk, HtmlFragment html, FlowLayout flow, Page page,
        Text.TextBuilder tb, Color? htmlColor, List<byte[]> inlineSvgs,
        ref bool htmlFragmentLinkEmitted, double htmlFrameIndent,
        double marginLeft, double marginRight, double marginTop)
    {
        // A FontSize the caller set on the fragment is the HTML body size:
        // it seeds the parser's root style, so unsized blocks inherit it
        // while explicit heading/inline sizes still win.
        var bodyFs = html.TextState is { FontSizeTouched: true } bts && bts.FontSize > 0
            ? (double)bts.FontSize : 0;
        // …and when the caller set none, the document's OWN `body { }` rule
        // is the base type — a fragment that ships a stylesheet sets in the
        // size and face it declares, not the 11 pt Standard-14 default.
        // The caller's TextState still wins; this only fills the gap.
        var bodyCss = Converters.HtmlToPdfConverter.BodyCssFont(chunk);
        if (bodyFs <= 0 && bodyCss.SizePt > 0) bodyFs = bodyCss.SizePt;
        var bodyCssFace = html.TextState?.Font is null
            && string.IsNullOrEmpty(html.TextState?.FontName)
            && bodyCss.Face is { Length: > 0 } bcf
            ? SafeFindFont(bcf) : null;
        if (bodyCssFace?.SourceFontData?.TtfData is not { Length: > 0 }) bodyCssFace = null;
        // Inline <strong>/<u> runs are tracked as RANGES here: a
        // paragraph that emphasises only some of its words draws
        // those words bold/underlined instead of promoting the
        // whole block's face.
        var blocks = Converters.HtmlToPdfConverter.ParseHtmlBlocks(
            chunk, bodyFs, inlineEmphasisRuns: true);
        // Legacy-font dialect (summernote / Word-paste HTML): every text run
        // is wrapped in <font face size> with a resolvable embedded face and
        // an explicit colour. It renders faithfully — embedded
        // face at the <font size> point size, CSS colour, on a 1.25×em line
        // grid — instead of the Standard-14 legacy flow. Gated tightly so no
        // other page-level HtmlFragment changes.
        Text.Font? legacyFace = null;
        var legacyDialect = false;
        foreach (var b in blocks)
            if (b.LegacyFontSized && b.FontFamily is { Length: > 0 } fam0)
            {
                var f0 = SafeFindFont(fam0);
                if (f0?.SourceFontData?.TtfData is { Length: > 0 })
                { legacyFace = f0; legacyDialect = true; break; }
            }
        foreach (var b in blocks)
        {
            // Page-level emphasis title (e.g. <p style="font-family:X"><b><i>):
            // the named face draws in its bold-italic variant at
            // the browser <p> default size, on the font's natural line height.
            // Gated on combined bold+italic + a resolvable styled face so ordinary
            // page HTML keeps the Standard-14 flow.
            Text.Font? styledFace = null;
            if (!legacyDialect && b.EmBold && b.EmItalic && b.FontFamily is { Length: > 0 } sf)
            {
                var stl = Text.FontStyles.Bold | Text.FontStyles.Italic;
                var cand = SafeFindFontStyled(sf, stl);
                if (cand?.SourceFontData?.TtfData is { Length: > 0 }) styledFace = cand;
            }
            // Emphasis title uses the browser <p> default 12pt (the body
            // default is 11; the styled path uses 12).
            var fontSize = legacyDialect && b.LegacyFontPt > 0 ? b.LegacyFontPt
                : styledFace is not null ? (b.FontSize > 11.0 ? b.FontSize : 12.0)
                : b.FontSize > 0 ? b.FontSize : 11.0;
            // Faithful line grid for the dialect: pitch = 1.25×em.
            var legacyLead = legacyDialect ? fontSize * 0.25 : 0.0;
            // List items carry a top margin (the common
            // `li { margin: .5em 0 }` rule) so the vertical rhythm
            // tracks a browser/CSS layout rather than packing tight.
            var topMargin = b.MarginTop + (b.IsListItem ? fontSize * 0.5 : 0);
            if (topMargin > 0) flow.AdvanceY(topMargin);
            if (b.IsImage
                && !(b.ImageSrc?.StartsWith("inline-svg:", StringComparison.Ordinal) ?? false)
                && (b.ImageSrc is null || LoadHtmlImageBytes(b.ImageSrc) is null))
            {
                // A broken/missing <img> still occupies the CSS default
                // replaced-element box (300x150 px, width capped by the
                // stylesheet), so following content flows below it — reserve
                // that box inline at the image's document position.
                var imgH = (b.ImageHeight > 0 ? b.ImageHeight : 150.0) * 0.75;
                if (flow.CurrentY - imgH < flow.BottomMargin) flow.ForceNewPage();
                flow.AdvanceY(imgH);
                continue;
            }
            if (b.IsCheckbox)
            {
                // <input type="checkbox"> inside an in-page HtmlFragment:
                // reserve a small AcroForm CheckboxField at the flow cursor,
                // queued with the current overflow slot so it binds to the page
                // it actually flows onto (registered on Form by FinaliseFormFields).
                flow.QueueCheckbox(10.0, b.LeftIndent, b.Checked);
                continue;
            }
            if (b.IsInputField)
            {
                // <input>/<textarea> inside an in-page HtmlFragment: place an
                // interactive AcroForm TextBoxField at the flow cursor, named
                // from the HTML name/id so callers can find it by FullName.
                var ifPage = flow.CurrentPage;
                var ifLlx = marginLeft + b.LeftIndent;
                var ifContentW = ifPage.Width - marginLeft - marginRight - b.LeftIndent;
                var ifW = b.InputWidth > 0 ? System.Math.Min(b.InputWidth, ifContentW) : ifContentW;
                var ifH = b.InputHeight > 0 ? b.InputHeight : fontSize * 1.3;
                var ifTop = flow.CurrentY;
                var ifField = new Aspose.Pdf.Forms.TextBoxField(ifPage,
                    new Aspose.Pdf.Rectangle(ifLlx, ifTop - ifH, ifLlx + ifW, ifTop))
                {
                    Multiline = b.InputMultiline,
                    ReadOnly = b.InputReadOnly,
                };
                if (!string.IsNullOrEmpty(b.InputName)) ifField.PartialName = b.InputName;
                if (!string.IsNullOrEmpty(b.InputValue)) ifField.Value = b.InputValue;
                Form.Add(ifField, ifPage.Number);
                flow.AdvanceY(ifH + b.MarginBottom);
                continue;
            }
            if (b.IsHorizontalRule)
            {
                // Draw the <hr> as a thin filled bar across the
                // content width in its CSS border colour.
                var hrPage = flow.CurrentPage;
                var lineW = hrPage.Width - marginLeft - marginRight;
                var th = b.RuleWidth > 0 ? b.RuleWidth : 1.0;
                var hrY = flow.CurrentY;
                var csb = new Content.ContentStreamBuilder();
                csb.SaveState();
                csb.SetFillColor(b.RuleColor ?? Color.FromRgb(128, 128, 128));
                csb.Rectangle(marginLeft, hrY - th, lineW, th);
                csb.Fill();
                csb.RestoreState();
                hrPage.AddContentStream(csb.Build());
                flow.AdvanceY(th + 2);
                continue;
            }
            if (string.IsNullOrEmpty(b.Text))
            {
                // Dialect blank line (<p><br></p>) occupies a full 1.25×em grid
                // row; a caller-set line-height steps blank rows on the same
                // pitch as text rows.
                var blankLs = (double)(html.TextState?.LineSpacing ?? 0f);
                flow.AdvanceY(b.ExplicitHeight > 0 ? b.ExplicitHeight
                    : blankLs is > 0 and <= 4 ? fontSize * blankLs
                    : legacyDialect ? fontSize + legacyLead : fontSize);
                continue;
            }
            var bf = new Text.TextFragment(b.Text);
            bf.TextState.FontSize = (float)fontSize;
            // HTML renders text on roughly a 1.2x line pitch; the legacy-font
            // dialect uses a 1.25×em grid. A LineSpacing the CALLER set on the
            // fragment overrides that pitch as the CSS line-height: a small
            // value (≤ 4) is the multiplier form (1.5 at 12 pt steps 18 pt per
            // line); larger values keep the TextFragment points-of-extra-leading
            // convention.
            var htmlCallerLs = (double)(html.TextState?.LineSpacing ?? 0f);
            var htmlBlockLead = htmlCallerLs > 0
                ? (htmlCallerLs <= 4 ? fontSize * (htmlCallerLs - 1) : htmlCallerLs)
                : legacyDialect ? legacyLead : fontSize * 0.2;
            bf.TextState.LineSpacing = (float)htmlBlockLead;
            bf.TextState.IsBold = b.FontRes == "F2";
            bf.TextState.IsItalic = b.FontRes == "F3";
            // Emphasis title: draw with the embedded bold-italic face on the
            // CSS "normal" line height (pixel-quantized win-metric
            // leading), overriding the Standard-14 bold/italic flags.
            if (styledFace is not null)
            {
                bf.TextState.Font = styledFace;
                bf.TextState.IsBold = false;
                bf.TextState.IsItalic = false;
                var pitch = HtmlNormalLineHeightPt(styledFace.SourceFontData?.TtfData, fontSize);
                bf.TextState.LineSpacing = (float)(pitch > 0 ? pitch - fontSize : fontSize * 0.2);
            }
            // The document's own body face draws the block, on the CSS
            // `line-height: normal` box that face's own metrics define
            // (pixel-quantized, so the pitch steps in 0.75 pt).
            if (bodyCssFace is not null && styledFace is null && !legacyDialect)
            {
                bf.TextState.Font = bodyCssFace;
                var bodyPitch = HtmlNormalLineHeightPt(
                    bodyCssFace.SourceFontData?.TtfData, fontSize);
                if (bodyPitch > 0)
                    bf.TextState.LineSpacing = (float)(bodyPitch - fontSize);
            }
            // Dialect: draw with the embedded face and the run's CSS colour.
            if (legacyDialect)
            {
                var face = b.FontFamily is { Length: > 0 } fam1 ? SafeFindFont(fam1) : null;
                if (face?.SourceFontData?.TtfData is { Length: > 0 }) bf.TextState.Font = face;
                else if (legacyFace is not null) bf.TextState.Font = legacyFace;
                if (b.ForeColor is { } fc) bf.TextState.ForegroundColor = fc;
            }
            if (htmlColor is not null) bf.TextState.ForegroundColor = htmlColor;
            // Split the block into segments so inline <a href> ranges carry a
            // WebHyperlink — the layout engine turns hyperlinked segments into
            // Link annotations over their rendered run.
            if (b.Anchors is { Count: > 0 })
                ApplyHtmlAnchorSegments(bf, b.Text, b.Anchors);
            // A Hyperlink set on the HtmlFragment ITSELF covers the fragment:
            // ONE Link annotation goes over the rendered
            // block (the first when the HTML splits into several).
            if (html.Hyperlink is not null && !htmlFragmentLinkEmitted)
            {
                bf.Hyperlink = html.Hyperlink;
                htmlFragmentLinkEmitted = true;
            }
            // The block pitch above is layout-synthesised, not a caller
            // request — keep the legacy first-line drop.
            bf.TextState.LineSpacingSynthetic = true;
            flow.LeftIndent = b.LeftIndent + htmlFrameIndent;
            // A block whose <strong>/<u> runs cover only part of it
            // sets those runs in their own style; the base face of
            // the line stays regular. A block emphasised throughout
            // keeps the whole-block promotion above.
            var emphRuns = HtmlEmphasisRuns(b);
            bool wrote;
            if (emphRuns is not null)
            {
                bf.TextState.IsBold = false;
                wrote = flow.WriteEmphasisRuns(bf, emphRuns);
            }
            else wrote = flow.WriteTextFragment(bf);
            flow.LeftIndent = 0;
            if (!wrote)
            {
                bf.Position = new Text.Position(marginLeft + b.LeftIndent,
                    page.Height - marginTop - bf.TextState.FontSize);
                tb.AppendText(bf);
            }
            if (b.MarginBottom > 0) flow.AdvanceY(b.MarginBottom);
        }
        // Draw this chunk's <img> elements in-flow (per segment), so a
        // logo lands at its position rather than after all content.
        RenderHtmlImages(chunk, flow, marginLeft, marginRight, inlineSvgs);
    }

    // ── UA-serif wide-box fragment ─────────────────────────
    // A full <HTML> document fragment with no font of its own and
    // tables declaring absolute pixel widths: the text sets in the
    // UA serif at the browser's 680 css px wrap, the tables in the
    // widest declared box, and everything clips at the sheet edge.
    // All distances below are empirical constants.
    private const double UaSerifPt = 12.0;          // UA body em

    private const double UaSerifPitchPt = 13.5;     // 12 pt on the 1.125 line

    private const double UaSerifSeatPt = 10.8;      // cursor -> first baseline

    private const double UaSerifH2Pt = 18.0;        // h2: 1.5 em bold

    private const double UaSerifH2BeforePt = 33.47; // prev baseline -> h2 baseline

    private const double UaSerifH2AfterPt = 28.73;  // h2 baseline -> next baseline

    private const double UaSerifMixedPitchPt = 14.3; // a line carrying a styled span

    // ── UA-serif tables: the paste's own spreadsheet grids ──
    // A cell styles itself (face, size, banded fill, 0.5 pt
    // edges) or falls to UA defaults (the serif at 12 pt with
    // the 1 css px padding). Line geometry is the css line-box
    // model computed from the face's own metrics: box = hhea
    // line height rounded to whole css px, baseline seats at
    // winAscent plus half the surplus leading. A cell centers
    // its stack of line boxes in the row (vertical-align:
    // middle); baselines step by the primary face's box while
    // a taller fallback box adds its extra half-leading to the
    // first seat. Declared pt row heights grow by the two
    // 0.5 pt edges. The row-top edge strokes in
    // the declared border colour and the bottom/left edges in
    // black.
    private const double UaTdPadPt = 0.75;      // UA default 1 css px td padding

    private const double UaTableEdgePt = 0.5;   // declared border width
}
