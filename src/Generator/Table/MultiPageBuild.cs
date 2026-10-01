using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public partial class Table
{
    private List<byte[]> BuildMultiPageInternal(Page page, double startY, double bottomMargin,
        double[] colWidths, int[] cellMap, string fontName, double topMargin = 0)
    {
        var mp = new MultiPageBuildState();
        mp.page = page;
        mp.startY = startY;
        mp.bottomMargin = bottomMargin;
        mp.colWidths = colWidths;
        mp.cellMap = cellMap;
        mp.fontName = fontName;
        mp.topMargin = topMargin;
        PlaceMultiPageTable(mp);

        PlanMultiPageRows(mp);
        for (var i = 0; i < Rows.Count; i++) mp.rowOpensPage[i] = Rows.At(i).IsInNewPageAuthored;
        for (var i = 0; i < Rows.Count; i++)
        {
            Rows.At(i).ReportInNewPage(false);
            mp.rowPlans.Add(mp.grid is { } gg
                ? BuildRowPlan(Rows.At(i), mp.colWidths, mp.cellMap, gg.gridToCell[i], gg.effRowSpan[i], mp.svgFillHeight)
                : BuildRowPlan(Rows.At(i), mp.colWidths, mp.cellMap, svgFillHeight: mp.svgFillHeight));
        }

        // A row taller than a whole page (a report cell whose text spans pages)
        // SPLITS against the page's real bottom content margin, not the flow's
        // tighter 36 pt overflow inset — the same boundary the row-span
        // paginator uses (the two-page comment row breaks its lines at the
        // 72 pt margin and resumes below the top margin).
        PlanMultiPageFooters(mp);

        mp.result = new List<byte[]>();
        mp.slices = new List<RowSlice>();
        // (the declared cell spacing joins the HTML row gap: a gap before the first
        // row, between every pair and after the last)
        mp.rowGap = RowSpacingPt + CellSpacingV;
        mp.currentY = mp.tableTopY - mp.rowGap
            // (a resolved collapsed grid's first line stands half its first row's
            // widest top rule under the table's top)
            - CollapsedTopShift(RepeatingRowsSkipFirstPage ? Math.Max(0, Math.Min(RepeatingRowsCount, Rows.Count)) : 0,
                mp.colWidths.Length);
        LastPageConsumedH.Clear();
        PageEndYs.Clear();
        mp.pageStartY = mp.tableTopY;
        mp.firstPageDone = false;
        mp.repeatCount = Math.Max(0, Math.Min(RepeatingRowsCount, mp.rowPlans.Count));
        PlanFooterBand(mp);
        mp.truncatedUnbroken = false;
        // A continued-from band is not laid on the page the table OPENS on: the
        // body starts under the page top and the band appears from page two.
        for (var i = RepeatingRowsSkipFirstPage ? mp.repeatCount : 0;
             i < mp.bodyRowCount && !mp.truncatedUnbroken; i++)
        {
            if (!BuildMultiPageRow(mp, i)) break;
        }
        EmitFooterBand(mp, tableEnds: true);
        if (mp.slices.Count > 0)
        {
            LastPageConsumedH.Add(mp.pageStartY - mp.currentY);
            mp.result.Add(BuildSlicesContent(mp.slices, mp.colWidths, mp.tableX, mp.fontName, mp.cellMap,
                mp.firstPageDone && !SpillPagesShareFontDict ? null : mp.page, mp.spanBlocks));
        }
        if (mp.result.Count == 0) mp.result.Add(Array.Empty<byte>());
        // Height consumed on the (first/only) page — meaningful for the single-page
        // case the flow dispatcher uses to advance a shared cursor; multi-page tables
        // fall back to a page break regardless.
        // The footprint includes the box border on both edges — the columns were inset
        // by one border width at the top and the border draws below the last row.
        LastRenderedHeight = mp.tableTopY - mp.currentY + 2 * mp.tableBorderWidth
            + EdgeBorderHeight() + CollapsedGridEdgeHeight();
        // A collapsed grid's rows bill only the rule each opens on; the stroke the
        // grid owes beyond them (see CollapsedGridEdgeHeight) is part of its
        // footprint on the spill page too, or the flow resumes half a rule up.
        LastPageEndY = mp.currentY - mp.tableBorderWidth - CollapsedGridEdgeHeight();
        if (PageEndYs.Count < mp.result.Count) PageEndYs.Add(LastPageEndY);
        // The grey rounded capsule behind the whole table (a border-radius div
        // wrapping a lifted grid): painted FIRST on the table's first page, padded
        // out from the table's extent.
        FillHtmlCapsule(mp);
        return mp.result;
    }

    /// <summary>Lays one planned row onto the current page slice, breaking to a new slice when the row does not fit; false when the table stops early.</summary>
    private bool BuildMultiPageRow(MultiPageBuildState mp, int i)
    {
        mp.plan = mp.rowPlans[i];
        mp.lineIdx = 0;

        mp.forceBreak = false;
        mp.brokeWithoutProgress = false;
        // A row the caller marked IsInNewPage opens a page of its own -- unless the
        // cursor is already at one's top, where breaking would only buy a blank page.
        if (i < mp.rowOpensPage.Length && mp.rowOpensPage[i] && mp.currentY < mp.fullPageTopY - 1e-3)
            mp.forceBreak = true;

        // A table does not START inside the bottom-margin band: when its FIRST row
        // cannot fit above the page's bottom CONTENT margin (a table pushed to the
        // page foot by its own top margin), the whole table moves to the next page.
        // Mid-table continuation keeps the tighter overflow inset, so ordinary row
        // splitting is unaffected. Main-flow builds only — a footer's table
        // legitimately sits at the page foot.
        if (i == 0 && mp.footStart && mp.currentY < mp.fullPageTopY - 1e-3)
        {
            var h0 = mp.plan.LineCount == 0
                ? mp.plan.MinBlankHeight
                : (mp.plan.CssContentH > 0 ? CssRowContentH(mp.plan) : mp.plan.TightLine) + mp.plan.VertPadding;
            if (MinRowFloor(mp.plan) > h0) h0 = MinRowFloor(mp.plan);
            if (mp.currentY - h0 < mp.contentBottomMargin - 1e-3)
                mp.forceBreak = true;
        }
        // A REPEATING header is never left alone at the foot of a page: the table
        // starts where its repeating rows and the first row under them all fit.
        // When 44 text lines leave exactly enough room for the 20 pt
        // header, the whole table still opens on the next page.
        if (i == 0 && _contentFlow && mp.repeatCount > 0 && mp.rowPlans.Count > mp.repeatCount
            && mp.currentY < mp.fullPageTopY - 1e-3)
        {
            double headNeed = 0;
            for (var r = 0; r <= mp.repeatCount; r++) headNeed += RowPlanHeight(mp.rowPlans[r]);
            double headOnly = 0;
            for (var r = 0; r < mp.repeatCount; r++) headOnly += RowPlanHeight(mp.rowPlans[r]);
            // …and only when the header WOULD have been left there: a table whose
            // header does not fit either is already carried whole by the ordinary
            // page break, and forcing a second one costs it a page (a table opening at
            // 64.5 with 36.2 of header over a 36.4 pt margin).
            if (mp.currentY - headOnly >= mp.pageBottom - 1e-3
                && mp.currentY - headNeed < mp.pageBottom - 1e-3
                && headNeed <= mp.fullPageTopY - mp.pageBottom + 1e-3)
                mp.forceBreak = true;
        }
        if (mp.bundleLink is not null && i < mp.bundleLink.Length && mp.bundleLink[i]
            && (i == 0 || !mp.bundleLink[i - 1])
            && mp.currentY < mp.fullPageTopY - 1e-3)
        {
            var bEnd = i + 1;
            while (bEnd < mp.rowPlans.Count && mp.bundleLink[bEnd - 1]) bEnd++;
            var bundleRows = bEnd - i;
            var avail = mp.currentY - mp.pageBottom;
            double need = 0;
            var fit = 0;
            for (var r = i; r < bEnd; r++)
            {
                var hp = mp.rowPlans[r];
                var contentH = hp.LineCount == 0
                    ? hp.MinBlankHeight
                    : hp.CssContentH > 0
                        ? CssRowContentH(hp)
                        : (hp.LineCount - 1) * hp.LineHeight + hp.TightLine;
                var hRow = hp.LineCount == 0 || hp.IsBlankRow ? contentH : contentH + hp.VertPadding;
                if (MinRowFloor(hp) > hRow) hRow = MinRowFloor(hp);
                need += hRow;
                if (need <= avail + 1e-3) fit++;
            }
            // A bundle whose every row is explicitly IsRowBroken has opted OUT
            // of keeping together: the caller asked for those rows to split where
            // they stand, and such a bundle splits at the page foot
            // rather than shipping it whole to the next page.
            var bundleBreakable = true;
            for (var r = i; r < bEnd && bundleBreakable; r++)
                if (!mp.rowPlans[r].Row.IsRowBroken) bundleBreakable = false;
            // …and a grid whose blocks are CUT by the break has opted out of
            // keeping the bundle together altogether: the block stays where it
            // is and the page break runs through it.
            if (SpanBlockSplitsAtPageBreak) bundleBreakable = true;
            if (!bundleBreakable && fit < bundleRows && (bundleRows <= 8 || fit < 4))
                mp.forceBreak = true;
        }

        mp.brokePageForRow = false;
        while (mp.lineIdx < mp.plan.LineCount || (mp.plan.LineCount == 0 && mp.lineIdx == 0))
        {
            if (!LayoutMultiPageRowLine(mp, i)) break;
        }
        return true;
    }

    /// <summary>Lays the next line group of the planned row onto the current slice, opening a new slice when the page is full; false to stop the row.</summary>
    private bool LayoutMultiPageRowLine(MultiPageBuildState mp, int i)
    {
        MeasureRowLineHeights(mp);
        FitRowLinesToSlice(mp);
        FitRowUnderItsGlyphs(mp);
        if (mp.forceBreak) { mp.linesFit = 0; mp.forceBreak = false; }
        // A row with nowhere to go takes what is left of it here rather than
        // buying a blank page and asking the same question again. That applies
        // on a FRESH page -- and on a page this row just broke onto WITHOUT
        // placing anything, which is the same dead end wearing a repeating
        // header. Merely having broken a page earlier is NOT: that page has
        // since filled up, and dumping the tail there drew it straight past the
        // bottom margin and off the page. The empty-linesFit path below opens
        // the next page for that case.
        if (mp.linesFit <= 0 && (mp.atFreshPage || mp.brokeWithoutProgress))
            mp.linesFit = Math.Max(1, mp.plan.LineCount - mp.lineIdx);
        if (mp.linesFit <= 0 && !IsBroken && Broken == TableBroken.None)
        {
            // An unbroken table has no next page to move to: stop here and let
            // the rows below it go undrawn.
            mp.truncatedUnbroken = true;
            return false;
        }
        if (BreakRowAcrossSlices(mp, i)) return true;
        mp.remaining = Math.Max(0, mp.plan.LineCount - mp.lineIdx);
        mp.take = mp.plan.LineCount == 0 ? 0 : Math.Min(mp.remaining, mp.linesFit);
        mp.fixedH = mp.plan.Row.FixedRowHeight;
        if (mp.fixedH > 0 && mp.plan.LineCount > 0)
        {
            var contentAvail = mp.fixedH - mp.plan.VertPadding;
            mp.take = 1 + (int)Math.Floor((contentAvail - mp.plan.TightLine)
                / Math.Max(1e-6, mp.plan.LineHeight) + 1e-6);
            mp.take = Math.Min(mp.plan.LineCount, Math.Max(1, mp.take));
        }
        mp.nestedDrivenH = -1;
        // A RESUMED reserve (its grid already built and split) sizes this slice
        // to the height the grid consumed on ITS matching page — the quanta
        // arithmetic left the continuation shorter than the picture inside it.
        PlaceNestedRowTables(mp);
        mp.imageDeferred = false;
        if (GeneratorCellModel && mp.plan.CellImages is not null && mp.plan.CellTables is null
            && !(mp.lineIdx == 0 && mp.take == mp.plan.LineCount))
            (mp.sliceContentH, mp.imageDeferred) = GeneratorImageSliceH(mp.plan, mp.lineIdx, mp.take,
                mp.currentY - mp.pageBottom - mp.plan.VertPadding);
        // A nested-reserve row's partial slice is the exact sum of the taken
        // lines' own heights (the whole-row case takes ExactTotalH below);
        // the uniform grid would price a 14 pt reserve share at the row pitch.
        CommitRowSlice(mp, i);
        mp.currentY -= mp.sliceH + mp.rowGap;
        mp.brokeWithoutProgress = false;   // a slice landed: the break made progress
        mp.lineIdx += (mp.plan.LineCount == 0 ? 1 : mp.take);
        // After an inner-driven break nothing else lands in the leftover
        // strip — the row's remaining lines resume on the next page.
        if (mp.nestedDrivenH >= 0 && mp.lineIdx < mp.plan.LineCount) mp.forceBreak = true;
        // A picture this slice could not take ENDS the page: the strip below it
        // is what the picture needed, and nothing else of the row goes there.
        if (mp.imageDeferred && mp.lineIdx < mp.plan.LineCount) mp.forceBreak = true;
        if (mp.fixedH > 0) mp.lineIdx = Math.Max(mp.lineIdx, mp.plan.LineCount);
        if (mp.plan.LineCount == 0) return false;
        return true;
    }

    /// <summary>An HTML capsule fill paints its band behind the finished table on every slice.</summary>
    private void FillHtmlCapsule(MultiPageBuildState mp)
    {
        if (HtmlCapsuleFill is { } capFill && mp.result.Count > 0 && !_measureOnly)
        {
            double capW = 0;
            foreach (var w in mp.colWidths) capW += w;
            // …inside its own q/Q: the capsule's grey fill colour leaked into every
            // later run on the page otherwise (the footnote drew in #eee on white).
            var capBuilder = new ContentStreamBuilder();
            capBuilder.SaveState();
            capBuilder.SetFillColor(capFill.R / 255.0, capFill.G / 255.0, capFill.B / 255.0);
            // Half the declared border-spacing rides each cell; the grid's outer edge
            // owes a full band, so the capsule makes up the other half.
            var capPadH = HtmlCapsulePadHPt + HtmlCellSpacingBandPt / 2;
            var capPadV = HtmlCapsulePadVPt + HtmlCellSpacingBandPt / 2;
            var capH = mp.tableTopY - mp.currentY + 2 * capPadV;
            FillRoundedRect(capBuilder,
                mp.tableX - capPadH, mp.currentY - capPadV,
                capW + 2 * capPadH, capH,
                Math.Min(HtmlCapsuleRadiusPt, capH / 2));
            capBuilder.RestoreState();
            var capBytes = capBuilder.Build();
            var merged = new byte[capBytes.Length + mp.result[0].Length];
            Array.Copy(capBytes, merged, capBytes.Length);
            Array.Copy(mp.result[0], 0, merged, capBytes.Length, mp.result[0].Length);
            mp.result[0] = merged;
        }
    }

    /// <summary>The tight-margin fill, the span-block share floor and the footer-start candidate of the table's pages.</summary>
    private void PlanMultiPageFooters(MultiPageBuildState mp)
    {
        if (Math.Abs(mp.bottomMargin - 36) < 0.5 && !mp.tightMarginFill)
        {
            var pbTall = mp.page.PageInfo?.Margin?.Bottom ?? 0;
            if (pbTall <= 0) pbTall = 72;
            if (pbTall > mp.pageBottom)
                foreach (var rpTall in mp.rowPlans)
                {
                    var rhTall = rpTall.LineCount == 0 ? rpTall.MinBlankHeight
                        : (rpTall.LineCount - 1) * rpTall.LineHeight + rpTall.TightLine
                          + rpTall.VertPadding;
                    if (rhTall > mp.fullPageTopY - mp.pageBottom + 1e-3)
                    {
                        mp.pageBottom = pbTall;
                        break;
                    }
                }
        }

        // The tight-margin fill stops one row short of the margin line (probed:
        // the 0.375 pt-margin report sheet places 82 ten-point rows and breaks
        // while a whole further row still fits above the margin) — reserve one
        // row pitch above the declared bottom margin.
        if (mp.tightMarginFill)
            foreach (var trp in mp.rowPlans)
                if (trp.LineCount > 0 && trp.LineHeight > 0)
                {
                    mp.pageBottom += trp.LineHeight;
                    break;
                }

        mp.shareFloor = null;
        if (mp.spanBlocks is { Count: > 0 })
        {
            mp.shareFloor = new double[mp.rowPlans.Count];
            foreach (var b in mp.spanBlocks)
                if (SpanHeightFallsOnLastRow) FloorSpanLastRow(mp, b);
                else ShareSpanHeightEvenly(mp, b);
        }

        mp.footStart = false;
        if (mp.footStartCandidate && mp.rowPlans.Count > 0)
        {
            var p0 = mp.rowPlans[0];
            var h0 = p0.LineCount == 0
                ? p0.MinBlankHeight
                : (p0.CssContentH > 0 ? CssRowContentH(p0) : p0.TightLine) + p0.VertPadding;
            if (MinRowFloor(p0) > h0) h0 = MinRowFloor(p0);
            // Foot-start means the table's OWN top margin drove it there (the
            // caller asked for a block anchored at the page foot) — a table that
            // merely ARRIVES low in the normal flow keeps the legacy fill.
            var ownTop = Margin?.Top ?? 0;
            mp.footStart = mp.tableTopY <= mp.contentBottomMargin + h0 + 1e-3
                && ownTop >= mp.fullPageTopY - mp.contentBottomMargin - h0 - 1e-3;
            if (mp.footStart && mp.contentBottomMargin > mp.pageBottom) mp.pageBottom = mp.contentBottomMargin;
        }
    }

    /// <summary>The cell grid, span blocks, bundle links and per-row plans the page build lays out.</summary>
    private void PlanMultiPageRows(MultiPageBuildState mp)
    {
        mp.identityMap = true;
        for (var i = 0; i < mp.cellMap.Length; i++)
            if (mp.cellMap[i] != i) { mp.identityMap = false; break; }
        mp.grid = mp.identityMap ? ComputeGrid() : null;
        mp.spanBlocks = null;
        if (mp.grid is { } g)
        {
            // The grid may need more columns than the cell-count-derived widths
            // provide (cells shifted right past active spans); extend with the
            // last width so every grid column has one.
            if (g.gridCols > mp.colWidths.Length)
            {
                var extended = new double[g.gridCols];
                Array.Copy(mp.colWidths, extended, mp.colWidths.Length);
                for (var i = mp.colWidths.Length; i < g.gridCols; i++)
                    extended[i] = mp.colWidths[mp.colWidths.Length - 1];
                mp.colWidths = extended;
            }
            mp.spanBlocks = g.blocks;
            foreach (var b in mp.spanBlocks) BuildSpanBlockLines(b, mp.colWidths);

            // The generator paginates row-spanning tables against the page's
            // real bottom margin (72pt by default), not the flow's tighter 36pt
            // overflow inset.
            // Only the flow's default inset is raised: header/footer builds pass a
            // negative margin and HTML conversion passes the author's real margins,
            // and both must keep their own boundary.
            if (Math.Abs(mp.bottomMargin - 36) < 0.5)
            {
                var pb = mp.page.PageInfo?.Margin?.Bottom ?? 0;
                if (pb <= 0) pb = 72;
                if (pb > mp.pageBottom) mp.pageBottom = pb;
            }
        }

        mp.bundleLink = null;
        if (mp.spanBlocks is { Count: > 0 })
        {
            mp.bundleLink = new bool[Rows.Count];
            foreach (var b in mp.spanBlocks)
                for (var r = Math.Max(0, b.StartRow); r < Math.Min(b.EndRow, Rows.Count) - 1; r++)
                    mp.bundleLink[r] = true;
        }

        // Laying the table out writes the effective cell text state back onto the
        // cells' DOM fragments — a fragment (or segment) without its own colour
        // reports the column/row/cell default after save. Cells marked
        // IsOverrideByFragment keep their fragment states untouched.
        for (var wbR = 0; wbR < Rows.Count; wbR++)
        {
            var wbRow = Rows.At(wbR);
            for (var wbC = 0; wbC < wbRow.Cells.Count; wbC++)
            {
                var wbCell = wbRow.Cells.At(wbC);
                if (wbCell.IsOverrideByFragment) continue;
                // Per-property fallback: auto-created cell/row states are empty,
                // so a colour set only at an outer level must still reach the cell.
                var effFg = wbCell.DefaultCellTextState?.ForegroundColor
                    ?? wbRow.DefaultCellTextState?.ForegroundColor
                    ?? DefaultCellTextState?.ForegroundColor;
                if (effFg is null) continue;
                foreach (var wbPara in wbCell.Paragraphs)
                {
                    if (wbPara is not TextFragment wbTf) continue;
                    wbTf.TextState.ForegroundColor ??= effFg;
                    for (var wbS = 1; wbS <= wbTf.Segments.Count; wbS++)
                    {
                        var wbSt = wbTf.Segments[wbS].TextState;
                        if (wbSt is not null) wbSt.ForegroundColor ??= effFg;
                    }
                }
            }
        }

        mp.rowPlans = new List<RowPlan>(Rows.Count);
        mp.svgFillBottom = mp.pageBottom;
        if (Math.Abs(mp.bottomMargin - 36) < 0.5)
        {
            var pbm = mp.page.PageInfo?.Margin?.Bottom ?? 0;
            if (pbm <= 0) pbm = 72;
            if (pbm > mp.svgFillBottom) mp.svgFillBottom = pbm;
        }
        mp.svgFillHeight = mp.tableTopY - mp.svgFillBottom;
        mp.rowOpensPage = new bool[Rows.Count];
    }

    /// <summary>The table's x, top, page bottom and band top on the page it starts on.</summary>
    private void PlaceMultiPageTable(MultiPageBuildState mp)
    {
        mp.pageHeight = mp.page.LayoutFrameHeight;
        // Every build resolves a collapsed grid's boundaries afresh: its rows or
        // their rules may have changed since the last.
        _collapsedRules = null;
        mp.marginLeft = Margin?.Left ?? 0;
        mp.marginTop = Margin?.Top ?? 0;
        mp.pinned = Left > 0;
        mp.tableX = (mp.pinned ? Left : FlowLeftOffset) + mp.marginLeft;
        // Honour the table's own horizontal Alignment within the page content band:
        // a Center/Right table's column block is offset so it sits centred (or right-
        // aligned) in the usable width instead of always hugging the left content
        // margin. Left (the default) keeps the margin-anchored x above.
        // An over-wide centred nested grid centres its DECLARED width in the flat
        // band, from twice its own left margin (see NestedCentreBandPt).
        if (NestedOverwideTarget > 0)
            mp.tableX = 2 * mp.marginLeft + (NestedCentreBandPt - NestedDeclaredSum) / 2;
        else if (!mp.pinned && Alignment is HorizontalAlignment.Center or HorizontalAlignment.Right)
        {
            // (a resolved collapsed grid's box also holds half its widest outer rules)
            var tableWidth = CollapsedOuterWidth(mp.colWidths.Length) + GridBoxWidth(mp.colWidths);
            var usable = GetTableUsableWidth(mp.page);
            if (usable > tableWidth + 0.01)
            {
                var slack = usable - tableWidth;
                mp.tableX = FlowLeftOffset + (Alignment == HorizontalAlignment.Center ? slack / 2 : slack);
            }
        }
        mp.tableBorderWidth = OuterBorderWidth();
        // A resolved collapsed grid's first line stands half its widest left-edge
        // rule inside the box, not half the skeleton rule.
        mp.tableX += mp.tableBorderWidth + CollapsedLeftShift(mp.colWidths.Length);
        mp.tableTopY = (Top > 0 ? mp.pageHeight - Top - mp.marginTop
            : mp.startY > 0 ? mp.startY
            : mp.pageHeight - mp.marginTop) - mp.tableBorderWidth;
        mp.bandTop = mp.topMargin > 0 ? mp.topMargin : mp.marginTop;
        mp.fullPageTopY = mp.pageHeight - (Top > 0 ? Math.Min(Top + mp.marginTop, mp.bandTop) : mp.bandTop);
        mp.pageBottom = mp.bottomMargin;
        mp.tightMarginFill = false;
        if (Math.Abs(mp.bottomMargin - 36) < 0.5
            && mp.page.PageInfo?.Margin is { BottomTouched: true } tightPm && tightPm.Bottom < mp.pageBottom)
        {
            mp.pageBottom = tightPm.Bottom;
            mp.tightMarginFill = true;
        }
        // The page bounds of THIS build, kept for the nested-grid draw hook: an inner
        // grid is built against the same bottom margin and fresh-page top as its host,
        // so it breaks where the page really ends and its continuation slices land at
        // the same top the host's own continuation resumes at. A build with no bottom
        // margin (measure passes, header/footer boxes) hands the inner 0 too — it then
        // never splits, which is exactly the pre-slice-pass behaviour.
        _curPageBottom = mp.pageBottom;
        _curFreshTopMargin = mp.pageHeight - mp.fullPageTopY;
        mp.footStartCandidate = _contentFlow;
        mp.contentBottomMargin = 0.0;
        if (mp.footStartCandidate)
        {
            mp.contentBottomMargin = mp.page.PageInfo?.Margin?.Bottom ?? 0;
            if (mp.contentBottomMargin <= 0) mp.contentBottomMargin = 72;
            if (mp.bottomMargin > mp.contentBottomMargin) mp.contentBottomMargin = mp.bottomMargin;
        }
    }

    /// <summary>The taken lines become a row slice on the current page and the cursor moves below them.</summary>
    private void CommitRowSlice(MultiPageBuildState mp, int i)
    {
        if (NestedTableRender && mp.plan.CellTables is not null && mp.take > 0
            && !(mp.lineIdx == 0 && mp.take == mp.plan.LineCount))
        {
            double accSlice = 0;
            for (var li = mp.lineIdx; li < mp.lineIdx + mp.take; li++)
                accSlice += NestedRowLineH(mp.plan, li);
            mp.sliceContentH = accSlice;
        }
        // …and when the grid inside it drove the break, the slice is exactly
        // the height the grid consumed — the strip below stays bare.
        if (mp.nestedDrivenH >= 0) mp.sliceContentH = mp.nestedDrivenH;
        // (a UA-boxed blank row keeps its cell padding: the mailing's padded nbsp underline cells stand 19.5)
        mp.sliceH = mp.plan.LineCount == 0 || (mp.plan.IsBlankRow && !FormGridCells && !UaCellBoxes)
            ? mp.sliceContentH
            : mp.sliceContentH + mp.plan.VertPadding;
        // (…and a UA-boxed row's continuation slice carries no top padding: its box was padded on its first page)
        if (UaCellBoxes && mp.lineIdx > 0 && mp.plan.LineCount > 0) mp.sliceH -= mp.plan.TopPad;
        if (UaCellBoxes && mp.take < mp.remaining && mp.plan.LineCount > 0) mp.sliceH -= Math.Max(0, mp.plan.VertPadding - mp.plan.TopPad);
        mp.wholeRowInSlice = mp.lineIdx == 0 && mp.take == mp.plan.LineCount;
        if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_ROWBOX") is not null)
            Console.WriteLine(
                $"[rowbox] lines={mp.plan.LineCount} take={mp.take} lineH={mp.plan.LineHeight:0.###} "
                + $"tight={mp.plan.TightLine:0.###} cssContentH={mp.plan.CssContentH:0.###} "
                + $"cssRowH={(mp.plan.CssContentH > 0 ? CssRowContentH(mp.plan) : 0):0.###} "
                + $"vpad={mp.plan.VertPadding:0.###} minRow={mp.plan.Row.MinRowHeight:0.###} "
                + $"isContent={mp.plan.Row.MinRowHeightIsContent} exact={mp.plan.ExactTotalH:0.###} "
                + $"blank={mp.plan.IsBlankRow} -> sliceContentH={mp.sliceContentH:0.###} sliceH={mp.sliceH:0.###}");
        // Exact-stack control rows (text stacked over a checkbox) size to the
        // true stacked height instead of the uniform line grid.
        if (mp.plan.ExactTotalH > 0 && mp.wholeRowInSlice)
            mp.sliceH = mp.plan.ExactTotalH;
        mp.minFloor = UaCellBoxes || mp.plan.Row.MinRowHeightIsContent
            ? mp.plan.Row.MinRowHeight + mp.plan.VertPadding : MinRowFloor(mp.plan);
        if (mp.wholeRowInSlice && mp.minFloor > mp.sliceH && mp.sliceH <= mp.usable)
            mp.sliceH = Math.Min(mp.minFloor, mp.currentY - mp.pageBottom);
        // Row-span share floor: every row spanned by a RowSpan cell is at
        // least the cell's per-row share (applies to blank spacer rows too).
        if (mp.shareFloor is not null && mp.wholeRowInSlice && mp.shareFloor[i] > mp.sliceH)
            mp.sliceH = mp.shareFloor[i];
        if (mp.fixedH > 0 && mp.plan.LineCount > 0) mp.sliceH = mp.fixedH;
        // A row kept under its glyphs closes at the page's bottom, its rule on the margin; a row
        // cut there closes under its last line's glyphs.
        if (mp.closesAtBottom)
            mp.sliceH = Math.Min(mp.sliceH, mp.currentY - mp.pageBottom - CollapsedGridEdgeHeight());
        else if (mp.closesUnderGlyphs && mp.take < mp.remaining)
            mp.sliceH -= LeadingUnderLastGlyphs(mp.plan);
        mp.slices.Add(new RowSlice
        {
            Plan = mp.plan,
            LineStart = mp.lineIdx,
            LineCount = mp.take,
            TopY = mp.currentY,
            Height = mp.sliceH,
            RowIndex = i,
        });
    }

    /// <summary>Nested cell tables are laid into the slice and the slice's content height is settled.</summary>
    private void PlaceNestedRowTables(MultiPageBuildState mp)
    {
        if (NestedTableRender && mp.plan.CellTables is not null && !_measureOnly
            && mp.take > 0)
            foreach (var ctKvR in mp.plan.CellTables)
                foreach (var ctR in ctKvR.Value)
                {
                    if (ctR.Slices is null || ctR.PlacedPages == 0) continue;
                    if (ctR.LineOffset >= mp.lineIdx
                        || ctR.LineOffset + ctR.LineCount <= mp.lineIdx) continue;
                    var consumed = ctR.Table.LastPageConsumedH;
                    if (ctR.PlacedPages < consumed.Count)
                    {
                        var hR = consumed[ctR.PlacedPages];
                        if (hR > mp.nestedDrivenH) mp.nestedDrivenH = hR;
                    }
                    ctR.PlacedPages++;
                }
        if (NestedTableRender && mp.plan.CellTables is not null && !_measureOnly
            && _buildPage is not null && mp.take > 0 && mp.take < mp.remaining)
            foreach (var ctKv in mp.plan.CellTables)
                foreach (var ct in ctKv.Value)
                {
                    if (ct.Slices is not null) continue;
                    if (ct.LineOffset < mp.lineIdx
                        || ct.LineOffset >= mp.lineIdx + mp.take
                        || ct.LineOffset + ct.LineCount <= mp.lineIdx + mp.take) continue;
                    double offsetSum = 0;
                    for (var li = mp.lineIdx; li < ct.LineOffset; li++)
                        offsetSum += NestedRowLineH(mp.plan, li);
                    var (ctCellX, ctPadLeft, ctPadTop) =
                        NestedCellGeom(mp.plan, mp.colWidths, mp.tableX, mp.cellMap, ctKv.Key);
                    var innerT = ct.Table;
                    innerT.FlowLeftOffset = ctCellX + ctPadLeft + innerT.HtmlCapsuleOutsetHPt
                        + innerT.HtmlListIndentPt;
                    var ctTop = mp.currentY - ctPadTop - offsetSum - innerT.HtmlCapsuleOutsetVPt
                        - innerT.HtmlMarginTopPt;
                    try
                    {
                        ct.Slices = innerT.BuildMultiPage(_buildPage, ctTop,
                            mp.pageBottom, mp.pageHeight - mp.fullPageTopY);
                        ct.Consumed = 0;
                        if (innerT.LastPageConsumedH.Count > 0)
                        {
                            var h1 = offsetSum + ctPadTop + innerT.LastPageConsumedH[0]
                                + innerT.HtmlCapsuleOutsetVPt + innerT.HtmlMarginTopPt;
                            if (h1 > mp.nestedDrivenH) mp.nestedDrivenH = h1;
                        }
                        ct.PlacedPages = 1;
                    }
                    catch { ct.Slices = null; }
                }
        mp.sliceContentH = (mp.plan.LineCount == 0)
            // (a UA-boxed row holding no line at all has no height - the browser lays no line box in an empty cell)
            ? (UaCellBoxes ? 0 : mp.plan.MinBlankHeight)
            : mp.plan.CssContentH > 0 && mp.lineIdx == 0 && mp.take == mp.plan.LineCount
                ? CssRowContentH(mp.plan)
                : GeneratorCellModel && EngineStackCell(mp.plan) is { } stack
                    ? EngineStackH(stack, mp.lineIdx, mp.take)
                    : (mp.take - 1) * mp.plan.LineHeight + mp.plan.TightLine;
    }

    /// <summary>The tallest HTML-engine cell stack of the row (every line an engine line with
    /// its own CSS box); null when the row has none, and the uniform grid prices it.</summary>
    private static List<CellLine>? EngineStackCell(RowPlan p)
    {
        // Only a row the stack itself sizes (no picture or nested table beside it, the
        // stack's lines being the row's line count): a picture cell's reserve lines
        // price the row otherwise, and a 3-line text cell beside a 300 pt picture must
        // not split the row after its third line.
        if (p.CssCells is null || p.CellImages is not null || p.CellTables is not null) return null;
        List<CellLine>? best = null;
        var bestH = 0.0;
        foreach (var col in p.CssCells)
        {
            if (col >= p.CellLines.Count) continue;
            var lines = p.CellLines[col];
            if (lines.Count == 0 || lines.Exists(l => !l.HtmlEngine || l.BoxH <= 0)) continue;
            var h = lines.Sum(l => l.BoxH);
            if (h > bestH) { bestH = h; best = lines; }
        }
        return best is not null && best.Count == p.LineCount ? best : null;
    }

    /// <summary>The height of <paramref name="count"/> engine lines of the stack from <paramref name="from"/>: their boxes summed.</summary>
    private static double EngineStackH(List<CellLine> stack, int from, int count)
    {
        var h = 0.0;
        for (var li = from; li < System.Math.Min(stack.Count, from + count); li++) h += stack[li].BoxH;
        return h;
    }

    /// <summary>A row that does not fit closes the slice and opens the next page for it; true when it did, so the line loop measures the row on the fresh page again.</summary>
    private bool BreakRowAcrossSlices(MultiPageBuildState mp, int i)
    {
        if (mp.linesFit <= 0)
        {
            // …unless the only thing in the row's way was the footer band's
            // reserve and this page is where the table ends: the band is then
            // never drawn and the room it held is the row's.
            if (ReleaseCarriedForwardBand(mp, i)) return true;
            // The blocks this page holds only part of are cut here, while its
            // own slices still say how tall their portion is.
            SplitSpanBlocksAtBreak(mp, i);
            mp.brokePageForRow = true;
            mp.brokeWithoutProgress = true;
            // No room on current page — close it and open a new one. The footer
            // band closes it first: it stands under the last row the page took,
            // in the room held back from this page's budget for it.
            EmitFooterBand(mp, tableEnds: false);
            LastPageConsumedH.Add(mp.pageStartY - mp.currentY);
            PageEndYs.Add(mp.currentY - mp.tableBorderWidth - CollapsedGridEdgeHeight());
            mp.result.Add(BuildSlicesContent(mp.slices, mp.colWidths, mp.tableX, mp.fontName, mp.cellMap,
                mp.firstPageDone && !SpillPagesShareFontDict ? null : mp.page, mp.spanBlocks));
            mp.firstPageDone = true;
            mp.slices.Clear();
            // A bound the caller overrides for continuation pages still owes the
            // footer band its room: the reserve rides on every page's bottom.
            if (ContinuationBottomOverride > 0)
                mp.pageBottom = ContinuationBottomOverride + mp.footerReserve;
            if (SpillPageMargins is { } spillMargins)
            {
                var (spillTop, spillBottom) = spillMargins(mp.result.Count);
                mp.fullPageTopY = mp.pageHeight - spillTop;
                mp.pageBottom = spillBottom + mp.footerReserve;
            }
            // Seat the first row of the fresh page so its content (text/image,
            // drawn padTop below the slice top) lands on the margin line rather
            // than padTop below it. Only when an explicit overflow inset is in
            // effect and no repeating header precedes the body row.
            // (a UA-boxed grid keeps its padding real space: a fresh page's first slice draws under the margin)
            mp.currentY = mp.fullPageTopY +
                (mp.topMargin > 0 && !GeneratorCellModel && !UaCellBoxes
                 && (mp.repeatCount == 0 || i < mp.repeatCount) ? mp.plan.TopPad : 0)
                - CollapsedTopShift(mp.repeatCount > 0 && i >= mp.repeatCount ? 0 : i, mp.colWidths.Length)
                // (a spaced grid opens every page with its gap, as it opened its first)
                - CellSpacingV;
            mp.pageStartY = mp.currentY;
            // Re-emit the first N rows as the repeating header on the
            // new page — only when the row about to start is past the
            // header band (otherwise we'd duplicate the header that
            // hasn't even been emitted yet).
            if (mp.repeatCount > 0 && i >= mp.repeatCount)
            {
                for (var h = 0; h < mp.repeatCount; h++)
                {
                    var hp = mp.rowPlans[h];
                    var hSliceH = BandSliceHeight(hp);
                    mp.slices.Add(new RowSlice
                    {
                        Plan = hp,
                        LineStart = 0,
                        LineCount = hp.LineCount,
                        TopY = mp.currentY,
                        Height = hSliceH,
                        RowIndex = h,
                    });
                    // (…and its gap follows the repeated band as it follows every row;
                    // the HTML row gap is left as it was here)
                    mp.currentY -= hSliceH + CellSpacingV;
                }
            }
            if (mp.lineIdx == 0) Rows.At(i).ReportInNewPage(true);
            return true;
        }
        return false;
    }

    /// <summary>Rows that must not split, fixed-height rows and exact-height rows decide how many lines the slice takes.</summary>
    private void FitRowLinesToSlice(MultiPageBuildState mp)
    {
        mp.fitsAnEmptyPage = mp.rowFullH <= mp.fullPageTopY - mp.pageBottom + 1e-3;
        // An image-bearing row is not split across a page boundary: the image is
        // blitted once at the row's top, so a partial first slice would orphan it.
        // Force the whole row onto the next page when it can't fit here (unless
        // we're already on a fresh page, where it must be placed regardless).
        // …but a NESTED-RESERVE row splits even so: its cell images are the
        // layout spacers riding beside the grid (a 15×1 gif must not veto the
        // page break); the grid's REAL pictures live in deeper rows of their
        // own, which defer whole right here when their turn comes.
        // …and a row the caller marked IsRowBroken has asked for the split
        // explicitly: the marked row leaves its caption lines at the foot of
        // page 1 and carries the image overleaf.
        if (mp.plan.CellImages is not null && mp.plan.CellTables is null
            && !mp.plan.Row.IsRowBroken
            && !mp.atFreshPage && mp.linesFit < mp.plan.LineCount
            && !mp.brokePageForRow && mp.fitsAnEmptyPage)
            mp.linesFit = 0;
        // Generator rows split across pages only when the row allows it
        // (Row.IsRowBroken): an unbroken row that does not fit the space left
        // moves whole to the next page (probed 2026-08-23: 17-line rows on
        // A4 — default rows leave the page bottom empty, IsRowBroken rows
        // fill it and continue overleaf). A row holding a nested table moves
        // whole the same way (a 165 pt entity block that does not fit under two
        // others opens the next page rather than leaving its first rows behind) --
        // unless the row is hard-sized, where the fixed box is what has to fit.
        if (GeneratorCellModel && !mp.plan.Row.IsRowBroken
            && (mp.plan.CellTables is null || mp.plan.Row.FixedRowHeight <= 0)
            && !mp.atFreshPage && mp.lineIdx == 0 && mp.linesFit < mp.plan.LineCount
            && !mp.brokePageForRow && mp.fitsAnEmptyPage)
            mp.linesFit = 0;
        // A FixedRowHeight row is HARD-sized and never splits (lines past its
        // box clip), so what has to fit is the fixed BOX, not its line stack:
        // when the space left above the bottom margin cannot hold it, the row
        // moves whole to the next page — the same defer a content-less fixed
        // row already gets through MinBlankHeight above.
        if (GeneratorCellModel && mp.plan.Row.FixedRowHeight > 0 && mp.plan.LineCount > 0
            && mp.plan.CellTables is null
            && !mp.atFreshPage && mp.lineIdx == 0 && !mp.brokePageForRow
            && mp.currentY - mp.pageBottom < mp.plan.Row.FixedRowHeight - 1e-3
            && mp.plan.Row.FixedRowHeight <= mp.fullPageTopY - mp.pageBottom + 1e-3)
            mp.linesFit = 0;
        // An inline-face grid's rows move whole too: the layout ships a
        // two-line subscriber row to the next page rather than leaving its
        // first line at the page bottom.
        if (InlineFaceGridRatio > 0 && mp.plan.CellTables is null
            && !mp.atFreshPage && mp.lineIdx == 0 && mp.linesFit < mp.plan.LineCount
            && !mp.brokePageForRow && mp.fitsAnEmptyPage)
            mp.linesFit = 0;
        // An exact-stack control row (text over a checkbox) never splits:
        // when its full height doesn't fit above the bottom margin the
        // whole row moves to the next page. A NESTED-RESERVE row is the
        // exception — it splits at its reserve-line boundaries (the browser
        // breaks inside a section rather than shipping it whole to the next
        // page), so it is exempt from the whole-row defer.
        if (mp.plan.ExactTotalH > 0 && mp.plan.CellTables is null
            && !mp.plan.Row.IsRowBroken
            && !mp.atFreshPage && !mp.brokePageForRow
            && mp.currentY - mp.pageBottom < mp.plan.ExactTotalH - 1e-3
            && mp.plan.ExactTotalH <= mp.fullPageTopY - mp.pageBottom + 1e-3)
            mp.linesFit = 0;
    }

    /// <summary>How many of the row's lines fit the slice, their heights and the row's full height.</summary>
    private void MeasureRowLineHeights(MultiPageBuildState mp)
    {
        mp.usable = mp.currentY - mp.pageBottom - mp.plan.VertPadding;
        // (a UA-boxed row's slice reserves only the padding it draws: the top pad on its first slice, none on a
        //  continuation - the bottom pad follows the last line wherever it lands; measured on the mailing, whose
        //  page ends on the SWIFT line with the column's 15 px pad below it on the next page)
        if (UaCellBoxes && mp.plan.LineCount > 0)
            mp.usable = mp.currentY - mp.pageBottom - (mp.lineIdx == 0 ? mp.plan.TopPad : 0);
        mp.linesFit = mp.plan.LineCount == 0
            ? 1
            : (mp.plan.LineHeight > 0 ? (int)Math.Floor(mp.usable / mp.plan.LineHeight) : mp.plan.LineCount);
        // A slice that CONTINUES overleaf may not spend the LEADING of the line
        // it cuts at: the leading rides above its glyphs, so the cut has to fall
        // below them and that much of the budget is not available to fill.
        // Re-measured once the first pass says the row will not fit whole, since
        // the reserve depends on that answer.
        if (GeneratorCellModel && mp.plan.LineHeight > 0 && mp.plan.Leading > 0
            && mp.linesFit > 0 && mp.lineIdx + mp.linesFit < mp.plan.LineCount)
            mp.linesFit = (int)Math.Floor((mp.usable - mp.plan.Leading) / mp.plan.LineHeight);
        // CSS run boxes: the uniform grid prices EVERY line at the row's tallest
        // box, so a row mixing 24 pt and 9.75 pt lines reads as far taller than it
        // is and splits a page early. Its real stack is what has to fit.
        if (CssRunBoxes && mp.plan.CssContentH > 0 && mp.plan.LineCount > 0
            && CssRowContentH(mp.plan) <= mp.usable + 1e-6)
            mp.linesFit = mp.plan.LineCount;
        // Main-flow bound: the first line needs only its TIGHT height (the
        // line grid applies from the second line on), so a row fits exactly
        // when TightLine + (n-1)·LineHeight fits — the boundary row at the
        // page foot places rather than deferring by the grid rounding.
        if (mp.footStart && mp.plan.LineCount > 0 && mp.plan.LineHeight > 0)
            mp.linesFit = mp.usable < mp.plan.TightLine - 1e-3
                ? 0
                : 1 + (int)Math.Floor((mp.usable - mp.plan.TightLine) / mp.plan.LineHeight + 1e-6);
        // A nested-reserve row prices its lines at their OWN heights (each
        // reserve line carries its share of the grid's height as its
        // FontSize), so how many fit is their cumulative sum against the
        // space — the uniform LineHeight arithmetic would split a row that
        // actually fits, or overfill one that does not.
        if ((NestedTableRender && mp.plan.CellTables is not null || UaCellBoxes && mp.plan.CssContentH > 0) && mp.plan.LineCount > 0)
        {
            double accFit = 0;
            var fitN = 0;
            for (var li = mp.lineIdx; li < mp.plan.LineCount; li++)
            {
                var lh = NestedRowLineH(mp.plan, li);
                if (accFit + lh > mp.usable + 1e-3) break;
                accFit += lh;
                fitN++;
            }
            mp.linesFit = fitN;
        }
        // An HTML-engine cell stack prices its lines at their OWN CSS boxes too: the
        // uniform pitch is the FIRST line's box (a heading plus its block margin), so it
        // budgeted a 74-line terms cell at 20 lines a page and seated them centred in
        // the slack (probed: the reference fills the first page with 41 lines at 13.5
        // and continues overleaf from the top).
        if (GeneratorCellModel && EngineStackCell(mp.plan) is { } stack && mp.plan.LineCount > 0)
        {
            double accFit = 0;
            var fitN = 0;
            for (var li = mp.lineIdx; li < stack.Count; li++)
            {
                if (accFit + stack[li].BoxH > mp.usable + 1e-3) break;
                accFit += stack[li].BoxH;
                fitN++;
            }
            mp.linesFit = fitN;
        }
        // At the top of a fresh overflow page, guarantee at least one line
        // of progress so we never infinitely loop on a row that cannot fit
        // its padding + one line into the full page height.
        // currentY sits at (or just above, by the first row's TopPad) the page
        // top whenever we've just opened a fresh page; >= keeps the loop-progress
        // guard working after the TopPad seating nudges currentY above fullPageTopY.
        // Generator dialect: a content-less row whose box does not fit the space
        // left moves on like any other (probed: a nested grid's empty 14 pt
        // row never draws across its fixed host's inner bottom).
        if (GeneratorCellModel && mp.plan.LineCount == 0
            && mp.plan.MinBlankHeight > mp.currentY - mp.pageBottom + 1e-3)
            mp.linesFit = 0;
        mp.atFreshPage = mp.currentY >= mp.fullPageTopY - 1e-3;
        if (mp.linesFit <= 0 && mp.atFreshPage) mp.linesFit = Math.Max(1, mp.plan.LineCount - mp.lineIdx);
        mp.rowFullH = mp.plan.LineCount == 0
            ? mp.plan.MinBlankHeight
            : (mp.plan.ExactTotalH > 0 && mp.plan.CellTables is not null
                ? mp.plan.ExactTotalH
                : mp.plan.CssContentH > 0
                ? CssRowContentH(mp.plan)
                : (mp.plan.LineCount - 1) * mp.plan.LineHeight + mp.plan.TightLine)
              + mp.plan.VertPadding;
    }
}
