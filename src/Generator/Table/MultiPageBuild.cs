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
        // The MEDIA frame's height (see Page.LayoutFrameHeight): a /Rotate page's
        // table seats against the media edges and paints upright in them.
        var pageHeight = page.LayoutFrameHeight;
        var marginLeft = Margin?.Left ?? 0;
        var marginTop = Margin?.Top ?? 0;
        // A declared Left PINS the table at that page x — it is an absolute
        // coordinate, not an inset from the content margin — and the pin beats the
        // table's own Alignment (a centred table pinned at 440 lands at 440).
        // With no pin the table hangs off the
        // flow's left content offset as before.
        var pinned = Left > 0;
        var tableX = (pinned ? Left : FlowLeftOffset) + marginLeft;
        // Honour the table's own horizontal Alignment within the page content band:
        // a Center/Right table's column block is offset so it sits centred (or right-
        // aligned) in the usable width instead of always hugging the left content
        // margin. Left (the default) keeps the margin-anchored x above.
        if (!pinned && Alignment is HorizontalAlignment.Center or HorizontalAlignment.Right)
        {
            double tableWidth = 0;
            for (var i = 0; i < colWidths.Length; i++) tableWidth += colWidths[i];
            var usable = GetTableUsableWidth(page);
            if (usable > tableWidth + 0.01)
            {
                var slack = usable - tableWidth;
                tableX = FlowLeftOffset + (Alignment == HorizontalAlignment.Center ? slack / 2 : slack);
            }
        }
        // The table's own box border draws OUTSIDE the column block, the same way an
        // explicitly-assigned cell border does: the stroke's outer edge sits on the
        // table's footprint edge and the columns start one border-width in. A 3 pt
        // box border therefore occupies 3 pt of page space on each side and the first
        // cell's text moves 3 pt right of the table origin.
        var tableBorderWidth = OuterBorderWidth();
        tableX += tableBorderWidth;
        // A declared Top PINS the table's own top at that page y — an absolute
        // coordinate measured from the page's TOP edge, not an inset from the content
        // margin — and the pin beats the flow cursor exactly as Left beats the content
        // offset (a pin at 50 on a 759 pt page draws its band
        // from 709 down).
        var tableTopY = (Top > 0 ? pageHeight - Top - marginTop
            : startY > 0 ? startY
            : pageHeight - marginTop) - tableBorderWidth;
        // Overflow pages restart the table below the page's top margin (the flow's body
        // band), not at the bare page top — matches the generator's spill layout. The
        // table's own Margin.Top still applies when no page margin is supplied.
        // …and a table PINNED by Top resumes on a spill page at the HIGHER of its pin
        // and that margin: a pin ABOVE the margin carries over (a table pinned at 10
        // and its page 2 opens its repeated header at the same 831.5 page 1 started
        // from), while a pin BELOW it does not (a table pinned at 400: its pages 2 and 3
        // both start at the ordinary 770).
        var bandTop = topMargin > 0 ? topMargin : marginTop;
        var fullPageTopY = pageHeight - (Top > 0 ? Math.Min(Top + marginTop, bandTop) : bandTop);
        var pageBottom = bottomMargin;
        // A page whose DECLARED bottom margin is tighter than the flow's default
        // 36 pt overflow inset fills its rows down to that margin (a 0.375 pt-margin
        // report sheet packs 82 rows a page); pages with ordinary margins keep the
        // legacy inset fill.
        var tightMarginFill = false;
        if (Math.Abs(bottomMargin - 36) < 0.5
            && page.PageInfo?.Margin is { BottomTouched: true } tightPm && tightPm.Bottom < pageBottom)
        {
            pageBottom = tightPm.Bottom;
            tightMarginFill = true;
        }
        // The page bounds of THIS build, kept for the nested-grid draw hook: an inner
        // grid is built against the same bottom margin and fresh-page top as its host,
        // so it breaks where the page really ends and its continuation slices land at
        // the same top the host's own continuation resumes at. A build with no bottom
        // margin (measure passes, header/footer boxes) hands the inner 0 too — it then
        // never splits, which is exactly the pre-slice-pass behaviour.
        _curPageBottom = pageBottom;
        _curFreshTopMargin = pageHeight - fullPageTopY;
        // A FOOT-STARTED main-flow table (pushed into the last row-slot above the
        // bottom content margin by its own top margin) keeps its rows above that
        // margin — its rows break to the next page at that bound.
        // An ordinarily-flowing table keeps the overflow inset, filling rows into
        // the margin band like the legacy layout. Header/footer and conversion
        // builds keep the caller's bound. The flag is resolved after the row plans
        // exist (the first row's height defines the foot band); see below.
        // A main-flow build is always a candidate — the flow passes the page's real
        // bottom margin, and the foot band is measured against that margin (a table
        // whose top sits exactly one row above it places that row on the page).
        var footStartCandidate = _contentFlow;
        var contentBottomMargin = 0.0;
        if (footStartCandidate)
        {
            contentBottomMargin = page.PageInfo?.Margin?.Bottom ?? 0;
            if (contentBottomMargin <= 0) contentBottomMargin = 72;
            if (bottomMargin > contentBottomMargin) contentBottomMargin = bottomMargin;
        }

        // RowSpan grid placement — only for the plain (identity-mapped) layout; the
        // column-chunk slicing path keeps the legacy cell-index mapping.
        var identityMap = true;
        for (var i = 0; i < cellMap.Length; i++)
            if (cellMap[i] != i) { identityMap = false; break; }
        var grid = identityMap ? ComputeGrid() : null;
        List<SpanBlock>? spanBlocks = null;
        if (grid is { } g)
        {
            // The grid may need more columns than the cell-count-derived widths
            // provide (cells shifted right past active spans); extend with the
            // last width so every grid column has one.
            if (g.gridCols > colWidths.Length)
            {
                var extended = new double[g.gridCols];
                Array.Copy(colWidths, extended, colWidths.Length);
                for (var i = colWidths.Length; i < g.gridCols; i++)
                    extended[i] = colWidths[colWidths.Length - 1];
                colWidths = extended;
            }
            spanBlocks = g.blocks;
            foreach (var b in spanBlocks) BuildSpanBlockLines(b, colWidths);

            // The generator paginates row-spanning tables against the page's
            // real bottom margin (72pt by default), not the flow's tighter 36pt
            // overflow inset.
            // Only the flow's default inset is raised: header/footer builds pass a
            // negative margin and HTML conversion passes the author's real margins,
            // and both must keep their own boundary.
            if (Math.Abs(bottomMargin - 36) < 0.5)
            {
                var pb = page.PageInfo?.Margin?.Bottom ?? 0;
                if (pb <= 0) pb = 72;
                if (pb > pageBottom) pageBottom = pb;
            }
        }

        // Bundle map for keep-together pagination: rows chained by any rowspan form a
        // bundle. link[r] == true → rows r and r+1 belong to the same bundle.
        bool[]? bundleLink = null;
        if (spanBlocks is { Count: > 0 })
        {
            bundleLink = new bool[Rows.Count];
            foreach (var b in spanBlocks)
                for (var r = Math.Max(0, b.StartRow); r < Math.Min(b.EndRow, Rows.Count) - 1; r++)
                    bundleLink[r] = true;
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

        // Pre-compute per-row content plans. Each plan carries the cells' wrapped
        // lines, uniform line height, vertical padding and the min (one-line) chunk
        // height — the paginator uses these to chop a row across pages when it
        // cannot fit in the remaining vertical space.
        var rowPlans = new List<RowPlan>(Rows.Count);
        // Space a sizeless vector image may fill: from the table top down to the
        // page's bottom CONTENT margin (72pt default), not the flow's tighter
        // overflow inset — same boundary the row-span paginator uses. Header/footer
        // builds (negative margin) and HTML conversions keep their own bound.
        var svgFillBottom = pageBottom;
        if (Math.Abs(bottomMargin - 36) < 0.5)
        {
            var pbm = page.PageInfo?.Margin?.Bottom ?? 0;
            if (pbm <= 0) pbm = 72;
            if (pbm > svgFillBottom) svgFillBottom = pbm;
        }
        var svgFillHeight = tableTopY - svgFillBottom;
        // Row.IsInNewPage is BOTH an input and an output: a caller sets it to demand
        // that the row opens a page, and the layout then overwrites it to report where
        // the row actually landed. Snapshot the demand before the report clobbers it.
        var rowOpensPage = new bool[Rows.Count];
        for (var i = 0; i < Rows.Count; i++) rowOpensPage[i] = Rows.At(i).IsInNewPageAuthored;
        for (var i = 0; i < Rows.Count; i++)
        {
            Rows.At(i).ReportInNewPage(false);
            rowPlans.Add(grid is { } gg
                ? BuildRowPlan(Rows.At(i), colWidths, cellMap, gg.gridToCell[i], gg.effRowSpan[i], svgFillHeight)
                : BuildRowPlan(Rows.At(i), colWidths, cellMap, svgFillHeight: svgFillHeight));
        }

        // A row taller than a whole page (a report cell whose text spans pages)
        // SPLITS against the page's real bottom content margin, not the flow's
        // tighter 36 pt overflow inset — the same boundary the row-span
        // paginator uses (the two-page comment row breaks its lines at the
        // 72 pt margin and resumes below the top margin).
        if (Math.Abs(bottomMargin - 36) < 0.5 && !tightMarginFill)
        {
            var pbTall = page.PageInfo?.Margin?.Bottom ?? 0;
            if (pbTall <= 0) pbTall = 72;
            if (pbTall > pageBottom)
                foreach (var rpTall in rowPlans)
                {
                    var rhTall = rpTall.LineCount == 0 ? rpTall.MinBlankHeight
                        : (rpTall.LineCount - 1) * rpTall.LineHeight + rpTall.TightLine
                          + rpTall.VertPadding;
                    if (rhTall > fullPageTopY - pageBottom + 1e-3)
                    {
                        pageBottom = pbTall;
                        break;
                    }
                }
        }

        // The tight-margin fill stops one row short of the margin line (probed:
        // the 0.375 pt-margin report sheet places 82 ten-point rows and breaks
        // while a whole further row still fits above the margin) — reserve one
        // row pitch above the declared bottom margin.
        if (tightMarginFill)
            foreach (var trp in rowPlans)
                if (trp.LineCount > 0 && trp.LineHeight > 0)
                {
                    pageBottom += trp.LineHeight;
                    break;
                }

        // A row-spanning cell distributes its height demand EVENLY across its
        // spanned rows: share = H / n where H = the cell's effective top+bottom
        // padding plus every wrapped line's font size plus each fragment's own
        // top margin. Each spanned row then independently takes
        // max(naturalHeight, share) — non-iterative, so a row whose natural
        // height exceeds the share does NOT shrink the share of the others.
        double[]? shareFloor = null;
        if (spanBlocks is { Count: > 0 })
        {
            shareFloor = new double[rowPlans.Count];
            foreach (var b in spanBlocks)
            {
                var bPad = EffectivePad(b.Cell, b.Row);
                var demand = (bPad?.Top ?? 0) + (bPad?.Bottom ?? 0);
                foreach (var l in b.Lines) demand += l.FontSize + l.TopGap;
                var nRows = Math.Max(1, Math.Min(b.EndRow, rowPlans.Count) - b.StartRow);
                var share = demand / nRows;
                for (var r = b.StartRow; r < b.EndRow && r < shareFloor.Length; r++)
                    if (share > shareFloor[r]) shareFloor[r] = share;
            }
        }

        // Foot-start resolution: the table is foot-started when its top sits at or
        // below one first-row height above the bottom content margin.
        var footStart = false;
        if (footStartCandidate && rowPlans.Count > 0)
        {
            var p0 = rowPlans[0];
            var h0 = p0.LineCount == 0
                ? p0.MinBlankHeight
                : (p0.CssContentH > 0 ? CssRowContentH(p0) : p0.TightLine) + p0.VertPadding;
            if (p0.Row.MinRowHeight > h0) h0 = p0.Row.MinRowHeight;
            // Foot-start means the table's OWN top margin drove it there (the
            // caller asked for a block anchored at the page foot) — a table that
            // merely ARRIVES low in the normal flow keeps the legacy fill.
            var ownTop = Margin?.Top ?? 0;
            footStart = tableTopY <= contentBottomMargin + h0 + 1e-3
                && ownTop >= fullPageTopY - contentBottomMargin - h0 - 1e-3;
            if (footStart && contentBottomMargin > pageBottom) pageBottom = contentBottomMargin;
        }

        // Walk rows, emit slices, spill to new pages as needed.
        var result = new List<byte[]>();
        var slices = new List<RowSlice>();
        // Cellspacing rides OUTSIDE each row's box: the first row starts one gap below
        // the table top and every following row one gap below the previous box, so the
        // cell content and its chrome keep the box the row plan measured.
        var rowGap = RowSpacingPt;
        var currentY = tableTopY - rowGap;
        LastPageConsumedH.Clear();
        var pageStartY = tableTopY;
        // Cell hyperlinks are emitted as link annotations on the first page only
        // (overflow pages aren't materialised here). firstPageDone flips once the
        // first page's content is built.
        var firstPageDone = false;
        // Repeating-rows: build slices for the first N rows once, then re-emit
        // them at the top of every overflow page (Y rebased per page).
        var repeatCount = Math.Max(0, Math.Min(RepeatingRowsCount, rowPlans.Count));
        // IsBroken = false: the table is never carried onto a second page. What does not
        // fit above the bottom content margin is DROPPED — 71
        // one-line rows produce a single page carrying rows 1..68 and a table border that
        // closes on the last one. An explicit <see cref="Broken"/> mode supersedes the
        // legacy flag: a grid declares BOTH `IsBroken = false` and
        // `TableBroken.IsInNextPage`, and its 43 rows still run over two pages.
        var truncatedUnbroken = false;
        for (var i = 0; i < rowPlans.Count && !truncatedUnbroken; i++)
        {
            var plan = rowPlans[i];
            var lineIdx = 0;

            // RowSpan keep-together: rows chained by rowspans form a
            // bundle. When the bundle doesn't fit the space left on this page, a small
            // bundle (≤ 8 rows) moves to the next page whole; a larger one may split but
            // only if at least 4 of its rows stay on this page — else it moves too.
            var forceBreak = false;
            // A page was broken for this row and NOTHING has been placed since. The
            // loop must never break twice without progress: a fresh page whose
            // repeating header leaves no room for even one line would otherwise break
            // again, and again, appending a page's content each time.
            var brokeWithoutProgress = false;
            // A row the caller marked IsInNewPage opens a page of its own -- unless the
            // cursor is already at one's top, where breaking would only buy a blank page.
            if (i < rowOpensPage.Length && rowOpensPage[i] && currentY < fullPageTopY - 1e-3)
                forceBreak = true;

            // A table does not START inside the bottom-margin band: when its FIRST row
            // cannot fit above the page's bottom CONTENT margin (a table pushed to the
            // page foot by its own top margin), the whole table moves to the next page.
            // Mid-table continuation keeps the tighter overflow inset, so ordinary row
            // splitting is unaffected. Main-flow builds only — a footer's table
            // legitimately sits at the page foot.
            if (i == 0 && footStart && currentY < fullPageTopY - 1e-3)
            {
                var h0 = plan.LineCount == 0
                    ? plan.MinBlankHeight
                    : (plan.CssContentH > 0 ? CssRowContentH(plan) : plan.TightLine) + plan.VertPadding;
                if (plan.Row.MinRowHeight > h0) h0 = plan.Row.MinRowHeight;
                if (currentY - h0 < contentBottomMargin - 1e-3)
                    forceBreak = true;
            }
            // A REPEATING header is never left alone at the foot of a page: the table
            // starts where its repeating rows and the first row under them all fit.
            // When 44 text lines leave exactly enough room for the 20 pt
            // header, the whole table still opens on the next page.
            if (i == 0 && _contentFlow && repeatCount > 0 && rowPlans.Count > repeatCount
                && currentY < fullPageTopY - 1e-3)
            {
                double headNeed = 0;
                for (var r = 0; r <= repeatCount; r++) headNeed += RowPlanHeight(rowPlans[r]);
                double headOnly = 0;
                for (var r = 0; r < repeatCount; r++) headOnly += RowPlanHeight(rowPlans[r]);
                // …and only when the header WOULD have been left there: a table whose
                // header does not fit either is already carried whole by the ordinary
                // page break, and forcing a second one costs it a page (a table opening at
                // 64.5 with 36.2 of header over a 36.4 pt margin).
                if (currentY - headOnly >= pageBottom - 1e-3
                    && currentY - headNeed < pageBottom - 1e-3
                    && headNeed <= fullPageTopY - pageBottom + 1e-3)
                    forceBreak = true;
            }
            if (bundleLink is not null && i < bundleLink.Length && bundleLink[i]
                && (i == 0 || !bundleLink[i - 1])
                && currentY < fullPageTopY - 1e-3)
            {
                var bEnd = i + 1;
                while (bEnd < rowPlans.Count && bundleLink[bEnd - 1]) bEnd++;
                var bundleRows = bEnd - i;
                var avail = currentY - pageBottom;
                double need = 0;
                var fit = 0;
                for (var r = i; r < bEnd; r++)
                {
                    var hp = rowPlans[r];
                    var contentH = hp.LineCount == 0
                        ? hp.MinBlankHeight
                        : hp.CssContentH > 0
                            ? CssRowContentH(hp)
                            : (hp.LineCount - 1) * hp.LineHeight + hp.TightLine;
                    var hRow = hp.LineCount == 0 || hp.IsBlankRow ? contentH : contentH + hp.VertPadding;
                    if (hp.Row.MinRowHeight > hRow) hRow = hp.Row.MinRowHeight;
                    need += hRow;
                    if (need <= avail + 1e-3) fit++;
                }
                // A bundle whose every row is explicitly IsRowBroken has opted OUT
                // of keeping together: the caller asked for those rows to split where
                // they stand, so such a bundle splits at the page foot
                // rather than shipping it whole to the next page.
                var bundleBreakable = true;
                for (var r = i; r < bEnd && bundleBreakable; r++)
                    if (!rowPlans[r].Row.IsRowBroken) bundleBreakable = false;
                if (!bundleBreakable && fit < bundleRows && (bundleRows <= 8 || fit < 4))
                    forceBreak = true;
            }

            // True when this row already forced a page break without emitting anything:
            // the next iteration must make progress (split the row) even when a repeated
            // header sits below the page top — otherwise an image-bearing row taller than
            // the space under the header would force page breaks forever.
            var brokePageForRow = false;
            while (lineIdx < plan.LineCount || (plan.LineCount == 0 && lineIdx == 0))
            {
                var usable = currentY - pageBottom - plan.VertPadding;
                var linesFit = plan.LineCount == 0
                    ? 1
                    : (plan.LineHeight > 0 ? (int)Math.Floor(usable / plan.LineHeight) : plan.LineCount);
                // A slice that CONTINUES overleaf may not spend the LEADING of the line
                // it cuts at: the leading rides above its glyphs, so the cut has to fall
                // below them and that much of the budget is not available to fill.
                // Re-measured once the first pass says the row will not fit whole, since
                // the reserve depends on that answer.
                if (GeneratorCellModel && plan.LineHeight > 0 && plan.Leading > 0
                    && linesFit > 0 && lineIdx + linesFit < plan.LineCount)
                    linesFit = (int)Math.Floor((usable - plan.Leading) / plan.LineHeight);
                // CSS run boxes: the uniform grid prices EVERY line at the row's tallest
                // box, so a row mixing 24 pt and 9.75 pt lines reads as far taller than it
                // is and splits a page early. Its real stack is what has to fit.
                if (CssRunBoxes && plan.CssContentH > 0 && plan.LineCount > 0
                    && CssRowContentH(plan) <= usable + 1e-6)
                    linesFit = plan.LineCount;
                // Main-flow bound: the first line needs only its TIGHT height (the
                // line grid applies from the second line on), so a row fits exactly
                // when TightLine + (n-1)·LineHeight fits — the boundary row at the
                // page foot places rather than deferring by the grid rounding.
                if (footStart && plan.LineCount > 0 && plan.LineHeight > 0)
                    linesFit = usable < plan.TightLine - 1e-3
                        ? 0
                        : 1 + (int)Math.Floor((usable - plan.TightLine) / plan.LineHeight + 1e-6);
                // A nested-reserve row prices its lines at their OWN heights (each
                // reserve line carries its share of the grid's height as its
                // FontSize), so how many fit is their cumulative sum against the
                // space — the uniform LineHeight arithmetic would split a row that
                // actually fits, or overfill one that does not.
                if (NestedTableRender && plan.CellTables is not null && plan.LineCount > 0)
                {
                    double accFit = 0;
                    var fitN = 0;
                    for (var li = lineIdx; li < plan.LineCount; li++)
                    {
                        var lh = NestedRowLineH(plan, li);
                        if (accFit + lh > usable + 1e-3) break;
                        accFit += lh;
                        fitN++;
                    }
                    linesFit = fitN;
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
                if (GeneratorCellModel && plan.LineCount == 0
                    && plan.MinBlankHeight > currentY - pageBottom + 1e-3)
                    linesFit = 0;
                var atFreshPage = currentY >= fullPageTopY - 1e-3;
                if (linesFit <= 0 && atFreshPage) linesFit = Math.Max(1, plan.LineCount - lineIdx);
                // Keep-together rules below may only DEFER a row that could actually land
                // somewhere else. A row taller than an entire empty page has nowhere to go:
                // deferring it buys a blank page and asks the same question again on the
                // next one, so such a row must split wherever it is.
                var rowFullH = plan.LineCount == 0
                    ? plan.MinBlankHeight
                    : (plan.ExactTotalH > 0 && plan.CellTables is not null
                        ? plan.ExactTotalH
                        : plan.CssContentH > 0
                        ? CssRowContentH(plan)
                        : (plan.LineCount - 1) * plan.LineHeight + plan.TightLine)
                      + plan.VertPadding;
                var fitsAnEmptyPage = rowFullH <= fullPageTopY - pageBottom + 1e-3;
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
                if (plan.CellImages is not null && plan.CellTables is null
                    && !plan.Row.IsRowBroken
                    && !atFreshPage && linesFit < plan.LineCount
                    && !brokePageForRow && fitsAnEmptyPage)
                    linesFit = 0;
                // Generator rows split across pages only when the row allows it
                // (Row.IsRowBroken): an unbroken row that does not fit the space left
                // moves whole to the next page (probed 2026-08-23: 17-line rows on
                // A4 — default rows leave the page bottom empty, IsRowBroken rows
                // fill it and continue overleaf).
                if (GeneratorCellModel && !plan.Row.IsRowBroken && plan.CellTables is null
                    && !atFreshPage && lineIdx == 0 && linesFit < plan.LineCount
                    && !brokePageForRow && fitsAnEmptyPage)
                    linesFit = 0;
                // A FixedRowHeight row is HARD-sized and never splits (lines past its
                // box clip), so what has to fit is the fixed BOX, not its line stack:
                // when the space left above the bottom margin cannot hold it, the row
                // moves whole to the next page — the same defer a content-less fixed
                // row already gets through MinBlankHeight above.
                if (GeneratorCellModel && plan.Row.FixedRowHeight > 0 && plan.LineCount > 0
                    && plan.CellTables is null
                    && !atFreshPage && lineIdx == 0 && !brokePageForRow
                    && currentY - pageBottom < plan.Row.FixedRowHeight - 1e-3
                    && plan.Row.FixedRowHeight <= fullPageTopY - pageBottom + 1e-3)
                    linesFit = 0;
                // An inline-face grid's rows move whole too: a
                // two-line subscriber row ships to the next page rather than leaving its
                // first line at the page bottom.
                if (InlineFaceGridRatio > 0 && plan.CellTables is null
                    && !atFreshPage && lineIdx == 0 && linesFit < plan.LineCount
                    && !brokePageForRow && fitsAnEmptyPage)
                    linesFit = 0;
                // An exact-stack control row (text over a checkbox) never splits:
                // when its full height doesn't fit above the bottom margin the
                // whole row moves to the next page. A NESTED-RESERVE row is the
                // exception — it splits at its reserve-line boundaries (the browser
                // breaks inside a section rather than shipping it whole to the next
                // page), so it is exempt from the whole-row defer.
                if (plan.ExactTotalH > 0 && plan.CellTables is null
                    && !plan.Row.IsRowBroken
                    && !atFreshPage && !brokePageForRow
                    && currentY - pageBottom < plan.ExactTotalH - 1e-3
                    && plan.ExactTotalH <= fullPageTopY - pageBottom + 1e-3)
                    linesFit = 0;
                if (forceBreak) { linesFit = 0; forceBreak = false; }
                // A row with nowhere to go takes what is left of it here rather than
                // buying a blank page and asking the same question again. That applies
                // on a FRESH page -- and on a page this row just broke onto WITHOUT
                // placing anything, which is the same dead end wearing a repeating
                // header. Merely having broken a page earlier is NOT: that page has
                // since filled up, and dumping the tail there drew it straight past the
                // bottom margin and off the page. The empty-linesFit path below opens
                // the next page for that case.
                if (linesFit <= 0 && (atFreshPage || brokeWithoutProgress))
                    linesFit = Math.Max(1, plan.LineCount - lineIdx);
                if (linesFit <= 0 && !IsBroken && Broken == TableBroken.None)
                {
                    // An unbroken table has no next page to move to: stop here and let
                    // the rows below it go undrawn.
                    truncatedUnbroken = true;
                    break;
                }
                if (linesFit <= 0)
                {
                    brokePageForRow = true;
                    brokeWithoutProgress = true;
                    // No room on current page — close it and open a new one.
                    LastPageConsumedH.Add(pageStartY - currentY);
                    result.Add(BuildSlicesContent(slices, colWidths, tableX, fontName, cellMap,
                        firstPageDone && !SpillPagesShareFontDict ? null : page, spanBlocks));
                    firstPageDone = true;
                    slices.Clear();
                    if (ContinuationBottomOverride > 0)
                        pageBottom = ContinuationBottomOverride;
                    if (SpillPageMargins is { } spillMargins)
                    {
                        var (spillTop, spillBottom) = spillMargins(result.Count);
                        fullPageTopY = pageHeight - spillTop;
                        pageBottom = spillBottom;
                    }
                    // Seat the first row of the fresh page so its content (text/image,
                    // drawn padTop below the slice top) lands on the margin line rather
                    // than padTop below it. Only when an explicit overflow inset is in
                    // effect and no repeating header precedes the body row.
                    currentY = fullPageTopY +
                        (topMargin > 0 && !GeneratorCellModel
                         && (repeatCount == 0 || i < repeatCount) ? plan.TopPad : 0);
                    pageStartY = currentY;
                    // Re-emit the first N rows as the repeating header on the
                    // new page — only when the row about to start is past the
                    // header band (otherwise we'd duplicate the header that
                    // hasn't even been emitted yet).
                    if (repeatCount > 0 && i >= repeatCount)
                    {
                        for (var h = 0; h < repeatCount; h++)
                        {
                            var hp = rowPlans[h];
                            var hContentH = hp.LineCount == 0
                                ? hp.MinBlankHeight
                                : hp.CssContentH > 0
                                    ? CssRowContentH(hp)
                                    : (hp.LineCount - 1) * hp.LineHeight + hp.TightLine;
                            var hSliceH = hp.LineCount == 0 || hp.IsBlankRow ? hContentH : hContentH + hp.VertPadding;
                            // Apply the row's MinRowHeight floor to the repeated header too, so a
                            // continuation page's header matches the body row pitch (otherwise the
                            // header is its natural height and every data row below shifts up).
                            if (hp.Row.MinRowHeight > hSliceH)
                                hSliceH = hp.Row.MinRowHeight;
                            slices.Add(new RowSlice
                            {
                                Plan = hp,
                                LineStart = 0,
                                LineCount = hp.LineCount,
                                TopY = currentY,
                                Height = hSliceH,
                                RowIndex = h,
                            });
                            currentY -= hSliceH;
                        }
                    }
                    if (lineIdx == 0) Rows.At(i).ReportInNewPage(true);
                    continue;
                }
                var remaining = Math.Max(0, plan.LineCount - lineIdx);
                var take = plan.LineCount == 0 ? 0 : Math.Min(remaining, linesFit);
                // A FixedRowHeight row is HARD-sized: it occupies exactly its fixed
                // height and CLIPS the wrapped lines that don't fit inside it (a
                // multi-line wrapped header key shows only the lines its fixed
                // height can hold), never splitting across pages.
                var fixedH = plan.Row.FixedRowHeight;
                if (fixedH > 0 && plan.LineCount > 0)
                {
                    var contentAvail = fixedH - plan.VertPadding;
                    take = 1 + (int)Math.Floor((contentAvail - plan.TightLine)
                        / Math.Max(1e-6, plan.LineHeight) + 1e-6);
                    take = Math.Min(plan.LineCount, Math.Max(1, take));
                }
                // INNER-DRIVEN PAGE BREAK: when a nested grid crosses this row's split
                // boundary, the grid decides where the page really ends. It is built
                // NOW (once — the draw hook consumes the cached slices) against the
                // real page bounds, and this slice sizes to the height the grid
                // consumed on this page. The line-quanta allotment would otherwise run
                // to the page bottom and paint over the band strip that must stay
                // bare below the break.
                double nestedDrivenH = -1;
                // A RESUMED reserve (its grid already built and split) sizes this slice
                // to the height the grid consumed on ITS matching page — the quanta
                // arithmetic left the continuation shorter than the picture inside it.
                if (NestedTableRender && plan.CellTables is not null && !_measureOnly
                    && take > 0)
                    foreach (var ctKvR in plan.CellTables)
                        foreach (var ctR in ctKvR.Value)
                        {
                            if (ctR.Slices is null || ctR.PlacedPages == 0) continue;
                            if (ctR.LineOffset >= lineIdx
                                || ctR.LineOffset + ctR.LineCount <= lineIdx) continue;
                            var consumed = ctR.Table.LastPageConsumedH;
                            if (ctR.PlacedPages < consumed.Count)
                            {
                                var hR = consumed[ctR.PlacedPages];
                                if (hR > nestedDrivenH) nestedDrivenH = hR;
                            }
                            ctR.PlacedPages++;
                        }
                if (NestedTableRender && plan.CellTables is not null && !_measureOnly
                    && _buildPage is not null && take > 0 && take < remaining)
                    foreach (var ctKv in plan.CellTables)
                        foreach (var ct in ctKv.Value)
                        {
                            if (ct.Slices is not null) continue;
                            if (ct.LineOffset < lineIdx
                                || ct.LineOffset >= lineIdx + take
                                || ct.LineOffset + ct.LineCount <= lineIdx + take) continue;
                            double offsetSum = 0;
                            for (var li = lineIdx; li < ct.LineOffset; li++)
                                offsetSum += NestedRowLineH(plan, li);
                            var (ctCellX, ctPadLeft, ctPadTop) =
                                NestedCellGeom(plan, colWidths, tableX, cellMap, ctKv.Key);
                            var innerT = ct.Table;
                            innerT.FlowLeftOffset = ctCellX + ctPadLeft + innerT.HtmlCapsuleOutsetHPt
                                + innerT.HtmlListIndentPt;
                            var ctTop = currentY - ctPadTop - offsetSum - innerT.HtmlCapsuleOutsetVPt
                                - innerT.HtmlMarginTopPt;
                            try
                            {
                                ct.Slices = innerT.BuildMultiPage(_buildPage, ctTop,
                                    pageBottom, pageHeight - fullPageTopY);
                                ct.Consumed = 0;
                                if (innerT.LastPageConsumedH.Count > 0)
                                {
                                    var h1 = offsetSum + ctPadTop + innerT.LastPageConsumedH[0]
                                        + innerT.HtmlCapsuleOutsetVPt + innerT.HtmlMarginTopPt;
                                    if (h1 > nestedDrivenH) nestedDrivenH = h1;
                                }
                                ct.PlacedPages = 1;
                            }
                            catch { ct.Slices = null; }
                        }
                var sliceContentH = (plan.LineCount == 0)
                    ? plan.MinBlankHeight
                    : plan.CssContentH > 0 && lineIdx == 0 && take == plan.LineCount
                        ? CssRowContentH(plan)
                        : (take - 1) * plan.LineHeight + plan.TightLine;
                // A generator row carrying a picture splits with the picture ATOMIC: the
                // slice takes the text lines that fit, defers any image whose full height
                // does not, and is only as tall as what it actually placed: a split row
                // leaves its two 10 pt captions at the foot of page 1 (a 20 pt band) and
                // opens page 2 with the 100 pt map alone.
                var imageDeferred = false;
                if (GeneratorCellModel && plan.CellImages is not null && plan.CellTables is null
                    && !(lineIdx == 0 && take == plan.LineCount))
                    sliceContentH = GeneratorImageSliceH(plan, lineIdx, take,
                        currentY - pageBottom - plan.VertPadding, out imageDeferred);
                // A nested-reserve row's partial slice is the exact sum of the taken
                // lines' own heights (the whole-row case takes ExactTotalH below);
                // the uniform grid would price a 14 pt reserve share at the row pitch.
                if (NestedTableRender && plan.CellTables is not null && take > 0
                    && !(lineIdx == 0 && take == plan.LineCount))
                {
                    double accSlice = 0;
                    for (var li = lineIdx; li < lineIdx + take; li++)
                        accSlice += NestedRowLineH(plan, li);
                    sliceContentH = accSlice;
                }
                // …and when the grid inside it drove the break, the slice is exactly
                // the height the grid consumed — the strip below stays bare.
                if (nestedDrivenH >= 0) sliceContentH = nestedDrivenH;
                // Spacer rows (content-less or whitespace-only) reserve just their line with
                // no cell padding, matching the generator; content rows keep padding.
                // Form-grid cells are CSS boxes: an &nbsp;-only row still carries its
                // borders and cellpadding (a 36pt spacer row is its 58px
                // line box PLUS the 3pt border+padding band).
                var sliceH = plan.LineCount == 0 || (plan.IsBlankRow && !FormGridCells)
                    ? sliceContentH
                    : sliceContentH + plan.VertPadding;
                // Honour a row's minimum height as a floor for content rows too (not just
                // empty ones), but only when the whole row fits in this slice.
                var wholeRowInSlice = lineIdx == 0 && take == plan.LineCount;
                if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_ROWBOX") is not null)
                    Console.WriteLine(
                        $"[rowbox] lines={plan.LineCount} take={take} lineH={plan.LineHeight:0.###} "
                        + $"tight={plan.TightLine:0.###} cssContentH={plan.CssContentH:0.###} "
                        + $"cssRowH={(plan.CssContentH > 0 ? CssRowContentH(plan) : 0):0.###} "
                        + $"vpad={plan.VertPadding:0.###} minRow={plan.Row.MinRowHeight:0.###} "
                        + $"isContent={plan.Row.MinRowHeightIsContent} exact={plan.ExactTotalH:0.###} "
                        + $"blank={plan.IsBlankRow} -> sliceContentH={sliceContentH:0.###} sliceH={sliceH:0.###}");
                // Exact-stack control rows (text stacked over a checkbox) size to the
                // true stacked height instead of the uniform line grid.
                if (plan.ExactTotalH > 0 && wholeRowInSlice)
                    sliceH = plan.ExactTotalH;
                // Under UA cell boxes the minimum is a CONTENT floor (the CSS box a
                // fixed-height child claims), so the cell's own padding still rides on
                // top of it; the legacy floor is the whole row height.
                var minFloor = UaCellBoxes || plan.Row.MinRowHeightIsContent
                    ? plan.Row.MinRowHeight + plan.VertPadding : plan.Row.MinRowHeight;
                if (wholeRowInSlice && minFloor > sliceH && sliceH <= usable)
                    sliceH = Math.Min(minFloor, currentY - pageBottom);
                // Row-span share floor: every row spanned by a RowSpan cell is at
                // least the cell's per-row share (applies to blank spacer rows too).
                if (shareFloor is not null && wholeRowInSlice && shareFloor[i] > sliceH)
                    sliceH = shareFloor[i];
                if (fixedH > 0 && plan.LineCount > 0) sliceH = fixedH;
                slices.Add(new RowSlice
                {
                    Plan = plan,
                    LineStart = lineIdx,
                    LineCount = take,
                    TopY = currentY,
                    Height = sliceH,
                    RowIndex = i,
                });
                currentY -= sliceH + rowGap;
                brokeWithoutProgress = false;   // a slice landed: the break made progress
                lineIdx += (plan.LineCount == 0 ? 1 : take);
                // After an inner-driven break nothing else lands in the leftover
                // strip — the row's remaining lines resume on the next page.
                if (nestedDrivenH >= 0 && lineIdx < plan.LineCount) forceBreak = true;
                // A picture this slice could not take ENDS the page: the strip below it
                // is what the picture needed, and nothing else of the row goes there.
                if (imageDeferred && lineIdx < plan.LineCount) forceBreak = true;
                if (fixedH > 0) lineIdx = Math.Max(lineIdx, plan.LineCount);
                if (plan.LineCount == 0) break;
            }
        }
        if (slices.Count > 0)
        {
            LastPageConsumedH.Add(pageStartY - currentY);
            result.Add(BuildSlicesContent(slices, colWidths, tableX, fontName, cellMap,
                firstPageDone && !SpillPagesShareFontDict ? null : page, spanBlocks));
        }
        if (result.Count == 0) result.Add(Array.Empty<byte>());
        // Height consumed on the (first/only) page — meaningful for the single-page
        // case the flow dispatcher uses to advance a shared cursor; multi-page tables
        // fall back to a page break regardless.
        // The footprint includes the box border on both edges — the columns were inset
        // by one border width at the top and the border draws below the last row.
        LastRenderedHeight = tableTopY - currentY + 2 * tableBorderWidth + EdgeBorderHeight();
        LastPageEndY = currentY - tableBorderWidth;
        // The grey rounded capsule behind the whole table (a border-radius div
        // wrapping a lifted grid): painted FIRST on the table's first page, padded
        // out from the table's extent.
        if (HtmlCapsuleFill is { } capFill && result.Count > 0 && !_measureOnly)
        {
            double capW = 0;
            foreach (var w in colWidths) capW += w;
            // …inside its own q/Q: the capsule's grey fill colour leaked into every
            // later run on the page otherwise (the footnote drew in #eee on white).
            var capBuilder = new ContentStreamBuilder();
            capBuilder.SaveState();
            capBuilder.SetFillColor(capFill.R / 255.0, capFill.G / 255.0, capFill.B / 255.0);
            // Half the declared border-spacing rides each cell; the grid's outer edge
            // owes a full band, so the capsule makes up the other half.
            var capPadH = HtmlCapsulePadHPt + HtmlCellSpacingBandPt / 2;
            var capPadV = HtmlCapsulePadVPt + HtmlCellSpacingBandPt / 2;
            var capH = tableTopY - currentY + 2 * capPadV;
            FillRoundedRect(capBuilder,
                tableX - capPadH, currentY - capPadV,
                capW + 2 * capPadH, capH,
                Math.Min(HtmlCapsuleRadiusPt, capH / 2));
            capBuilder.RestoreState();
            var capBytes = capBuilder.Build();
            var merged = new byte[capBytes.Length + result[0].Length];
            Array.Copy(capBytes, merged, capBytes.Length);
            Array.Copy(result[0], 0, merged, capBytes.Length, result[0].Length);
            result[0] = merged;
        }
        return result;
    }
}
