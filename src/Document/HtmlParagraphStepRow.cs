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
    private const double psAckTableRightMargin = 33.6;
    private const double psLandscapeColumnW = 729 * 0.75;

    /// <summary>One procedure-step row, verbatim: the body of LayoutProcedureStepRows' row loop with
    /// the measuring helpers only it uses.</summary>
    private void LayoutStepRow(int prowIdx, List<Converters.HtmlToPdfConverter.StepRow> psRows, HtmlFragment html, FlowLayout flow, Page page, double marginLeft, double marginRight, double marginTop, double marginBottom, double psWrapRight)
    {
        // Where the baseline sits in a line box: the box's leading is
        // split above and below the face's own content area, so a line
        // set tighter than the face rides higher in its box. Arial's
        // ascent and descent are 1854 and 434 per 2048 em.
        // The line box a run of this size sits in when nothing declares
        // one: normal leading on an integer number of css pixels.
        var sr = new StepRowState();
        sr.prow = psRows[prowIdx];
        sr.psContentX = marginLeft + 55.5 + sr.prow.IndentPt;
        sr.psBulletX = marginLeft + 1.5 + sr.prow.IndentPt;
        sr.psRowRight = sr.prow.ContentWidthPt > 0
            ? sr.psContentX + sr.prow.ContentWidthPt
            : sr.prow.AckTable
                ? sr.prow.Landscape ? sr.psContentX + psLandscapeColumnW
                    : page.Width - psAckTableRightMargin
                : psWrapRight;
        sr.psLimit = sr.psRowRight - sr.psContentX;

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
        // With separate borders the cells stand apart: the spacing
        // runs down the table's own edges as well as between them.
        sr.psSheetEmpty = flow.CurrentY >= flow.ContentTop - 0.5;
        flow.AdvanceY(11.25);             // step-row 15 css px margin
        // How tall a step will be: the boxes each of its lines
        // really takes at its own size, its tables, its gaps, and the
        // block the renderer reserves for the acknowledgement.
        {
            var rowNeed = PsRowNeed(sr, page, sr.prow);
            var pageBudget = flow.ContentTop - flow.BottomMargin;

            // A step keeps the sub-steps indented under it: a number
            // left at the foot of a sheet with the steps it heads
            // overleaf is not how the form reads. Only a deeper indent
            // counts - a step numbered like a child but set at the
            // parent's own indent stands on its own.
            var groupNeed = rowNeed;
            for (var k = prowIdx + 1;
                 k < psRows.Count && psRows[k].IndentPt > sr.prow.IndentPt;
                 k++)
                groupNeed += 11.25 + PsRowNeed(sr, page, psRows[k]);
            // a group too tall for any sheet cannot be kept together,
            // and forcing it over would only empty the sheet it left
            if (groupNeed > pageBudget) groupNeed = rowNeed;

            // A step travels whole: its number and the content that
            // belongs to it are not parted across a sheet. It starts a
            // fresh sheet even when taller than one - it still splits,
            // but from a clean top; only once something is already on
            // this sheet, or it would break for ever.
            var breakForSelf = flow.CurrentY - groupNeed < flow.BottomMargin
                && (groupNeed <= pageBudget || !sr.psSheetEmpty);

            if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_STEPS") == "1")
                Console.WriteLine($"    step '{sr.prow.Bullet}' need={rowNeed:0.00} "
                    + $"y={flow.CurrentY:0.00} self={breakForSelf}");

            if (rowNeed > 0 && breakForSelf)
            {
                flow.ForceNewPage();
                // the step's own margin belongs above it wherever it
                // lands, so it opens a fresh sheet the same distance
                // down as it would have opened this one. The col-full
                // generation opens one paragraph line lower still —
                // its sheet's own rhythm.
                flow.AdvanceY(11.25 + (sr.prow.ColFull ? 12.0 : 0.0));
            }
        }

        sr.psBoxTop = 0.0;
        sr.psBoxBorder = 0.0;
        sr.psBoxDouble = false;
        sr.psLineInset = 0.0;
        sr.clogTop = flow.CurrentY;
        sr.psRowTop = flow.CurrentY;
        sr.psRowStartPage = flow.CurrentPage;
        sr.psBulletPending = sr.prow.Bullet is not null;

        sr.warnFirstSeg = true;
        // ---- pure layout ----

        // CSS min-content: the widest run the cell cannot break. A
        // blank or a glyph is atomic and an inline margin belongs to
        // the run it opens; a space - inside a text run or standing
        // as a segment of its own - is where a run may end.
        // A grid that declares its own width fits the columns into
        // it: a column takes the width it declares unless its own
        // content cannot be broken narrower than that, and the
        // columns that still have room give up whatever the rest
        // overruns by. A grid that declares no width keeps the
        // columns exactly as they were declared.
        // ---- drawing ----

        // ---- flow: keep a data-entry caption with its table ----

        sr.pidx = 0;
        sr.psSeenTable = false;
        while (sr.pidx < sr.prow.Items.Count)
        {
            var item = sr.prow.Items[sr.pidx];
            if (sr.prow.Warn)
            {
                // a warning box paginates one
                // block per page until its table
                if (item.Line is { } wl)
                {
                    PsRenderLine(sr, flow, page, marginRight, psWrapRight, wl, PsLayoutLine(sr, wl));
                    if (!sr.psSeenTable && sr.pidx + 1 < sr.prow.Items.Count) PsBreakPage(sr, flow, page, marginRight, psWrapRight);
                }
                else if (item.Table is { } wt)
                {
                    sr.psSeenTable = true;
                    if (item.GapBefore > 0) flow.AdvanceY(item.GapBefore);
                    PsRenderTable(flow, sr, page, marginRight, psWrapRight, wt, PsLayoutTable(page, sr, wt));
                }
                sr.pidx++;
                continue;
            }
            if (item.BoxBorderPt > 0)
            {
                if (item.GapBefore > 0) flow.AdvanceY(item.GapBefore);
                sr.psBoxTop = flow.CurrentY;
                sr.psBoxBorder = item.BoxBorderPt;
                sr.psBoxDouble = item.BoxDouble;
                // the side inset stays rule + 2 css px; the sheet's
                // own padding-top only deepens the first line's seat
                sr.psLineInset = sr.psBoxBorder + 1.5;
                flow.AdvanceY(sr.psBoxBorder + (item.BoxPadTopPt > 0 ? item.BoxPadTopPt : 1.5));
                sr.pidx++;
                continue;
            }
            if (item.BoxEnd)
            {
                flow.AdvanceY(sr.psBoxBorder + 1.5);
                // the frame stands at least 80 css px tall
                var boxH = sr.psBoxTop - flow.CurrentY;
                if (boxH < 60.0) { flow.AdvanceY(60.0 - boxH); boxH = 60.0; }
                var bb = new Content.ContentStreamBuilder();
                bb.SaveState();
                var yTop = sr.psBoxTop;
                var yBot = sr.psBoxTop - boxH;
                // a double rule is two thinner ones filling the band
                var runs = sr.psBoxDouble
                    ? new[] { (0.625, 1.25), (3.125, 1.25) }
                    : new[] { (sr.psBoxBorder / 2, sr.psBoxBorder) };
                foreach (var (off, w) in runs)
                {
                    bb.SetLineWidth(w)
                      .MoveTo(sr.psContentX, yTop - off)
                      .LineTo(sr.psRowRight, yTop - off).Stroke()
                      .MoveTo(sr.psContentX, yBot + off)
                      .LineTo(sr.psRowRight, yBot + off).Stroke()
                      .MoveTo(sr.psContentX + off, yTop)
                      .LineTo(sr.psContentX + off, yBot).Stroke()
                      .MoveTo(sr.psRowRight - off, yTop)
                      .LineTo(sr.psRowRight - off, yBot).Stroke();
                }
                bb.RestoreState();
                flow.InjectContentAtCursor(bb.Build());
                sr.psBoxBorder = 0;
                sr.psLineInset = 0;
                sr.pidx++;
                continue;
            }
            if (item.Line is { } gl && item.KeepWithNext)
            {
                var j = sr.pidx;
                var groupH = 0.0;
                var lineLays = new List<(Converters.HtmlToPdfConverter.StepItem it,
                    List<List<(double x, Converters.HtmlToPdfConverter.StepSeg seg, string? txt)>> lay)>();
                while (j < sr.prow.Items.Count && sr.prow.Items[j].Line is { } jl)
                {
                    var lay = PsLayoutLine(sr, jl);
                    lineLays.Add((sr.prow.Items[j], lay));
                    groupH += sr.prow.Items[j].GapBefore + lay.Count * psPitch;
                    j++;
                    if (!sr.prow.Items[j - 1].KeepWithNext) break;
                }
                (List<List<string>>, double,
                 List<(double, List<(List<double>, List<(int, double, Converters.HtmlToPdfConverter.StepSeg?, string?)>)>)>,
                 double, double[])? tabLay = null;
                Converters.HtmlToPdfConverter.StepTable? tabItem = null;
                var tabGap = 0.0;
                if (j < sr.prow.Items.Count && sr.prow.Items[j].Table is { } jt)
                {
                    var tl = PsLayoutTable(page, sr, jt);
                    tabLay = tl;
                    tabItem = jt;
                    tabGap = sr.prow.Items[j].GapBefore;
                    groupH += tabGap + tl.totalH;
                    j++;
                }
                if (flow.CurrentY - groupH < flow.BottomMargin
                    && groupH <= flow.ContentTop - flow.BottomMargin)
                    PsBreakPage(sr, flow, page, marginRight, psWrapRight);
                foreach (var (it, lay) in lineLays)
                {
                    if (it.GapBefore > 0) flow.AdvanceY(it.GapBefore);
                    PsRenderLine(sr, flow, page, marginRight, psWrapRight, it.Line!, lay);
                }
                if (tabItem is not null && tabLay is { } tv)
                {
                    if (tabGap > 0) flow.AdvanceY(tabGap);
                    PsRenderTable(flow, sr, page, marginRight, psWrapRight, tabItem, tv);
                }
                sr.pidx = j;
                continue;
            }
            if (item.GapBefore > 0) flow.AdvanceY(item.GapBefore);
            if (item.Line is { } pline)
            {
                PsRenderLine(sr, flow, page, marginRight, psWrapRight, pline, PsLayoutLine(sr, pline));
            }
            else if (item.Table is { } pt)
            {
                PsRenderTable(flow, sr, page, marginRight, psWrapRight, pt, PsLayoutTable(page, sr, pt));
            }
            sr.pidx++;
        }
        if (sr.prow.HasAck && sr.prow.AckTable)
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
            var relBoxTop = sr.prow.AckHair ? 8.25 : 5.25;
            var gapAbove = Converters.HtmlToPdfConverter.StepParaMargin ?? 11.25;
            for (var qi = sr.prow.Items.Count - 1; qi >= 0; qi--)
            {
                if (sr.prow.Items[qi].BoxEnd)
                {
                    var dbl = false;
                    for (var bi = qi - 1; bi >= 0; bi--)
                        if (sr.prow.Items[bi].BoxBorderPt > 0)
                        { dbl = sr.prow.Items[bi].BoxDouble; break; }
                    gapAbove = dbl ? 0.0 : 7.5;
                    break;
                }
                if (sr.prow.Items[qi].Line is { } lastLn)
                {
                    // a paragraph spends its bottom margin above the
                    // table; a choice list (its lines pace on the
                    // option pitch) seats the blanks nearly flush
                    if (Math.Abs(lastLn.LinePt
                        - Converters.HtmlToPdfConverter.SwmOptionPitch) < 0.1)
                        gapAbove = 1.0 - relBoxTop;
                    break;
                }
                if (sr.prow.Items[qi].Table is not null) break;
            }
            var relRuleY = relBoxTop + 6.38;
            var relBlankBottom = relBoxTop + 13.5;
            // the shared second label row keys off the lowest
            // first-row label any widget put down
            var tr1Base = 0.0;
            var anyTr2 = false; var tr2Stack = 0;
            foreach (var w in sr.prow.Acks)
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
                PsBreakPage(sr, flow, page, marginRight, psWrapRight);
            else flow.AdvanceY(gapAbove);
            var topY = flow.CurrentY;
            var tb2 = new Content.ContentStreamBuilder();
            tb2.SaveState();
            var lres2 = Table.RegisterFont(flow.CurrentPage, "Helvetica-Bold");
            var tableRight = page.Width - 21.98;
            var cellX = tableRight - 22.87 - 112.5 * sr.prow.Acks.Count;
            // a filled-in check: two strokes shaped on the 10.5 pt
            // glyph's box, seated on the blank it marks
            void PsCheck(double gx, double gBase)
                => tb2.SetLineWidth(1.05)
                      .MoveTo(gx + 0.2, gBase + 3.3)
                      .LineTo(gx + 2.6, gBase + 0.4)
                      .LineTo(gx + 7.6, gBase + 10.3)
                      .Stroke();
            var anyCheckbox = false;
            foreach (var w in sr.prow.Acks)
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
            if (sr.prow.AckInitials is { } initials)
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
        else if (sr.prow.HasAck)
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
            var (maxStack, clusterH) = PsAckClusterGeom(sr.prow);
            var ackN = sr.prow.Acks.Count;
            if (flow.CurrentY - clusterH < flow.BottomMargin) PsBreakPage(sr, flow, page, marginRight, psWrapRight);
            var ab = new Content.ContentStreamBuilder();
            ab.SaveState();
            var lres = Table.RegisterFont(flow.CurrentPage, "Helvetica-Bold");
            var ruleY = flow.CurrentY - maxStack;
            for (var wi = 0; wi < ackN; wi++)
            {
                var w = sr.prow.Acks[wi];
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
        if (sr.prow.Warn)
        {
            var wb = new Content.ContentStreamBuilder();
            wb.SaveState();
            wb.SetLineWidth(3.75)
              .MoveTo(sr.psContentX - 0.9, flow.CurrentY)
              .LineTo(sr.psRowRight + 2.9, flow.CurrentY).Stroke();
            wb.RestoreState();
            flow.InjectContentAtCursor(wb.Build());
            flow.AdvanceY(3.75);
        }
        // a step that carries nothing still puts its number down
        if (sr.psBulletPending)
        {
            var nb = new Content.ContentStreamBuilder();
            nb.SaveState();
            PsDrawBullet(sr, flow, nb, flow.CurrentY - PsAscentFor(psPitch, psFs));
            nb.RestoreState();
            flow.InjectContentAtCursor(nb.Build());
        }
        // the acknowledge column stands beside the content, so a row
        // whose content is the shorter of the two is still as tall as
        // the column - even where none of it falls on the sheet.
        // NOT for the col-full generation: its widgets bank UNDER the
        // content and the drawn cluster has already spent their height
        if (!sr.prow.ColFull
            && ReferenceEquals(sr.psRowStartPage, flow.CurrentPage)
            && sr.prow.AckHeightPt > sr.psRowTop - flow.CurrentY)
            flow.AdvanceY(sr.prow.AckHeightPt - (sr.psRowTop - flow.CurrentY));
        PsDrawClog(sr, flow, page, marginRight, psWrapRight);
        // the row's bottom margin collapses with the next row's top
        // one rather than adding to it, and is dropped altogether
        // where the sheet ends - so only the top margin is spent
    }
}
