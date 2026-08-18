using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Text;
namespace Aspose.Pdf;

public partial class Table : BaseParagraph
{
    /// <summary>
    /// Build the table across multiple pages. Returns content bytes per page.
    /// Sets <see cref="Row.IsInNewPage"/> on rows that overflow to subsequent pages.
    /// The first entry is for the given page; additional entries require new pages.
    /// Rows whose wrapped content exceeds the available page height are split at
    /// line boundaries — each chunk becomes a partial-row slice on its own page
    /// with the row's borders and background drawn around the chunk's extent.
    /// </summary>
    public List<byte[]> BuildMultiPage(Page page, double startY = 0, double bottomMargin = 36,
        double topMargin = 0, bool measureOnly = false, bool contentFlow = false)
    {
        _contentFlow = contentFlow;
        _measureOnly = measureOnly;
        _buildPage = page;
        _pageImages.Clear();
        _pageGraphs.Clear();
        _pageFootnotes.Clear();
        var fontName = RegisterFont(page);
        var colWidths = ParseColumnWidths(GetTableUsableWidth(page));

        // Column-pagination: when RepeatingColumnsCount > 0 and the table is wider
        // than the page can fit, split it horizontally. Each "column slice" renders
        // the first N (repeating) cells alongside one contiguous chunk of the
        // remaining cells, with the chunk packed greedily to fit page width.
        // TableBroken.VerticalInSamePage: a table WIDER than the page's usable
        // width wraps its overflow columns into bands stacked vertically on the
        // SAME page. Each band is a slice of the column
        // range rendered as its own sub-table; a ColSpan cell crossing a band
        // boundary contributes its remaining span to the next band as an EMPTY
        // cell that keeps the background/border (the text renders once, in the
        // band where the cell starts).
        if (Broken == TableBroken.VerticalInSamePage && RepeatingColumnsCount <= 0
            && colWidths.Length > 1)
        {
            var marginLeftVb = Margin?.Left ?? 0;
            var tableXVb = FlowLeftOffset + Left + marginLeftVb;
            var pageRightMarginVb = page.PageInfo?.Margin?.Right ?? 0;
            if (pageRightMarginVb <= 0) pageRightMarginVb = 36;
            var usableVb = page.Width - tableXVb - pageRightMarginVb;
            double totalWVb = 0;
            foreach (var w in colWidths) totalWVb += w;
            if (totalWVb > usableVb + 1e-3)
            {
                var bands = new List<(int start, int end)>();
                var bStart = 0;
                while (bStart < colWidths.Length)
                {
                    var bEnd = bStart;
                    double bw = 0;
                    while (bEnd < colWidths.Length
                           && (bEnd == bStart || bw + colWidths[bEnd] <= usableVb + 1e-3))
                    {
                        bw += colWidths[bEnd];
                        bEnd++;
                    }
                    bands.Add((bStart, bEnd));
                    bStart = bEnd;
                }
                if (bands.Count <= 1)
                {
                    // Single over-wide column: nothing to wrap — fall back to the
                    // proportional shrink the clamp in ParseColumnWidths skipped.
                    var scaleVb = usableVb / totalWVb;
                    for (var i = 0; i < colWidths.Length; i++) colWidths[i] *= scaleVb;
                }
                else
                {
                    var mergedPages = new List<byte[]>();
                    var curStartY = startY;
                    double heightSum = 0;
                    foreach (var (bs, be) in bands)
                    {
                        var bandTable = BuildColumnBandTable(colWidths, bs, be);
                        var bandPages = bandTable.BuildMultiPage(page, curStartY, bottomMargin, topMargin);
                        for (var pi = 0; pi < bandPages.Count; pi++)
                        {
                            if (pi == 0 && mergedPages.Count > 0)
                            {
                                // Band content joins the SAME page: concatenate streams.
                                var first = mergedPages[0];
                                var joined = new byte[first.Length + 1 + bandPages[0].Length];
                                Array.Copy(first, joined, first.Length);
                                joined[first.Length] = (byte)'\n';
                                Array.Copy(bandPages[0], 0, joined, first.Length + 1, bandPages[0].Length);
                                mergedPages[0] = joined;
                            }
                            else
                                mergedPages.Add(bandPages[pi]);
                        }
                        // Image/graph blits ride along per page slot.
                        for (var pi = 0; pi < bandTable._pageImages.Count; pi++)
                        {
                            while (_pageImages.Count <= pi) _pageImages.Add(new List<(byte[], Rectangle)>());
                            _pageImages[pi].AddRange(bandTable._pageImages[pi]);
                        }
                        for (var pi = 0; pi < bandTable._pageGraphs.Count; pi++)
                        {
                            while (_pageGraphs.Count <= pi) _pageGraphs.Add(new List<byte[]>());
                            _pageGraphs[pi].AddRange(bandTable._pageGraphs[pi]);
                        }
                        curStartY = bandTable.LastPageEndY;
                        heightSum += bandTable.LastRenderedHeight;
                        LastPageEndY = bandTable.LastPageEndY;
                    }
                    LastRenderedHeight = heightSum;
                    return mergedPages;
                }
            }
        }

        var repeat = Math.Max(0, Math.Min(RepeatingColumnsCount, colWidths.Length));
        if (repeat > 0)
        {
            var marginLeftCs = Margin?.Left ?? 0;
            var tableXCs = Left + marginLeftCs;
            var pageRightMargin = page.PageInfo?.Margin?.Right ?? 0;
            if (pageRightMargin <= 0) pageRightMargin = 36;
            var pageUsableWidth = page.Width - tableXCs - pageRightMargin;
            double totalW = 0;
            for (var i = 0; i < colWidths.Length; i++) totalW += colWidths[i];
            if (totalW > pageUsableWidth + 1e-3 && colWidths.Length > repeat)
            {
                double repeatW = 0;
                for (var i = 0; i < repeat; i++) repeatW += colWidths[i];
                var chunkBudget = Math.Max(1, pageUsableWidth - repeatW);
                var allPages = new List<byte[]>();
                var chunkStart = repeat;
                var firstSlice = true;
                while (chunkStart < colWidths.Length)
                {
                    var chunkEnd = chunkStart;
                    double w = 0;
                    while (chunkEnd < colWidths.Length &&
                           (chunkEnd == chunkStart || w + colWidths[chunkEnd] <= chunkBudget))
                    {
                        w += colWidths[chunkEnd];
                        chunkEnd++;
                    }
                    if (chunkEnd == chunkStart) chunkEnd = chunkStart + 1;

                    var sliceLen = repeat + (chunkEnd - chunkStart);
                    var sliceWidths = new double[sliceLen];
                    var sliceCellMap = new int[sliceLen];
                    for (var i = 0; i < repeat; i++) { sliceWidths[i] = colWidths[i]; sliceCellMap[i] = i; }
                    for (var i = 0; i < chunkEnd - chunkStart; i++)
                    {
                        sliceWidths[repeat + i] = colWidths[chunkStart + i];
                        sliceCellMap[repeat + i] = chunkStart + i;
                    }

                    var slicePages = BuildMultiPageInternal(
                        page, firstSlice ? startY : 0, bottomMargin,
                        sliceWidths, sliceCellMap, fontName, topMargin);
                    allPages.AddRange(slicePages);
                    firstSlice = false;
                    chunkStart = chunkEnd;
                }
                return allPages;
            }
        }

        var identity = new int[colWidths.Length];
        for (var i = 0; i < colWidths.Length; i++) identity[i] = i;
        return BuildMultiPageInternal(page, startY, bottomMargin, colWidths, identity, fontName, topMargin);
    }

    /// <summary>
    /// Clone this table restricted to the grid-column range [colStart, colEnd) for
    /// VerticalInSamePage band wrapping. Cells are walked by GRID position (ColSpan
    /// advances the cursor); a cell that starts inside the band keeps its content
    /// with its span clipped to the band, a cell whose span merely REACHES INTO the
    /// band contributes an empty continuation cell that keeps background and border.
    /// </summary>
    private Table BuildColumnBandTable(double[] colWidths, int colStart, int colEnd)
    {
        var band = new Table
        {
            Border = Border,
            DefaultCellBorder = DefaultCellBorder,
            DefaultCellPadding = DefaultCellPadding,
            DefaultCellTextState = DefaultCellTextState,
            BackgroundColor = BackgroundColor,
            Left = Left,
            Margin = Margin,
            ColumnAdjustment = ColumnAdjustment.Customized,
            Broken = TableBroken.None,
            RepeatingRowsCount = RepeatingRowsCount,
            IsBroken = IsBroken,
            FlowLeftOffset = FlowLeftOffset,
        };
        var widthTokens = new System.Text.StringBuilder();
        for (var c = colStart; c < colEnd; c++)
        {
            if (widthTokens.Length > 0) widthTokens.Append(' ');
            widthTokens.Append(colWidths[c].ToString("0.###", CultureInfo.InvariantCulture));
        }
        band.ColumnWidths = widthTokens.ToString();

        for (var r = 0; r < Rows.Count; r++)
        {
            var row = Rows.At(r);
            var bandRow = band.Rows.Add();
            bandRow.Border = row.Border;
            bandRow.DefaultCellBorder = row.DefaultCellBorder;
            bandRow.DefaultCellPadding = row.DefaultCellPadding;
            bandRow.DefaultCellTextState = row.DefaultCellTextState;
            bandRow.BackgroundColor = row.BackgroundColor;
            bandRow.FixedRowHeight = row.FixedRowHeight;
            bandRow.MinRowHeight = row.MinRowHeight;
            bandRow.VerticalAlignment = row.VerticalAlignment;

            var gridPos = 0;
            for (var ci = 0; ci < row.Cells.Count && gridPos < colEnd; ci++)
            {
                var cell = row.Cells.At(ci);
                var span = Math.Max(1, cell.ColSpan);
                var cellStart = gridPos;
                var cellEnd = gridPos + span;
                gridPos = cellEnd;
                var isStart = cellStart >= colStart && cellStart < colEnd;
                var overlap = Math.Min(cellEnd, colEnd) - Math.Max(cellStart, colStart);
                if (overlap <= 0) continue;
                var bandCell = new Cell
                {
                    ColSpan = overlap,
                    RowSpan = cell.RowSpan,
                    Border = cell.Border,
                    BackgroundColor = cell.BackgroundColor,
                    Margin = cell.Margin,
                    IsNoBorder = cell.IsNoBorder,
                    DefaultCellTextState = cell.DefaultCellTextState,
                    IsWordWrapped = cell.IsWordWrapped,
                    VerticalAlignment = cell.VerticalAlignment,
                    Alignment = cell.Alignment,
                    BackgroundImage = cell.BackgroundImage,
                };
                if (isStart)
                    bandCell.Paragraphs = cell.Paragraphs;
                bandRow.Cells.Add(bandCell);
            }
        }
        return band;
    }

    private List<byte[]> BuildMultiPageInternal(Page page, double startY, double bottomMargin,
        double[] colWidths, int[] cellMap, string fontName, double topMargin = 0)
    {
        var pageHeight = page.Height;
        var marginLeft = Margin?.Left ?? 0;
        var marginTop = Margin?.Top ?? 0;
        var tableX = FlowLeftOffset + Left + marginLeft;
        // Honour the table's own horizontal Alignment within the page content band:
        // a Center/Right table's column block is offset so it sits centred (or right-
        // aligned) in the usable width instead of always hugging the left content
        // margin. Left (the default) keeps the margin-anchored x above.
        if (Alignment is HorizontalAlignment.Center or HorizontalAlignment.Right)
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
        // Explicitly-assigned per-side cell borders draw OUTSIDE the declared
        // column widths: each column's footprint is borderL + width + borderR
        // (a "30 30 …" table with 5 pt GraphInfo borders lays out
        // on a 40 pt column pitch with abutting double borders between cells).
        if (DefaultCellBorder is { } dcb0
            && (dcb0.LeftAssigned || dcb0.RightAssigned || dcb0.TopAssigned || dcb0.BottomAssigned))
        {
            var bl0 = dcb0.Side.HasFlag(BorderSide.Left) || dcb0.LeftAssigned
                ? (dcb0.RawLeft?.LineWidth > 0 ? dcb0.RawLeft.LineWidth : dcb0.Width) : 0;
            var br0 = dcb0.Side.HasFlag(BorderSide.Right) || dcb0.RightAssigned
                ? (dcb0.RawRight?.LineWidth > 0 ? dcb0.RawRight.LineWidth : dcb0.Width) : 0;
            if (bl0 + br0 > 0)
            {
                colWidths = (double[])colWidths.Clone();
                for (var i = 0; i < colWidths.Length; i++) colWidths[i] += bl0 + br0;
            }
        }
        // The table's own box border draws OUTSIDE the column block, the same way an
        // explicitly-assigned cell border does: the stroke's outer edge sits on the
        // table's footprint edge and the columns start one border-width in. A 3 pt
        // box border therefore occupies 3 pt of page space on each side and the first
        // cell's text moves 3 pt right of the table origin.
        var tableBorderWidth = OuterBorderWidth();
        tableX += tableBorderWidth;
        var tableTopY = (startY > 0 ? startY : pageHeight - Top - marginTop) - tableBorderWidth;
        // Overflow pages restart the table below the page's top margin (the flow's body
        // band), not at the bare page top — matches the generator's spill layout. The
        // table's own Margin.Top still applies when no page margin is supplied.
        var fullPageTopY = pageHeight - (topMargin > 0 ? topMargin : marginTop);
        var pageBottom = bottomMargin;
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
        var footStartCandidate = _contentFlow && Math.Abs(bottomMargin - 36) < 0.5;
        var contentBottomMargin = 0.0;
        if (footStartCandidate)
        {
            contentBottomMargin = page.PageInfo?.Margin?.Bottom ?? 0;
            if (contentBottomMargin <= 0) contentBottomMargin = 72;
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
        for (var i = 0; i < Rows.Count; i++)
        {
            Rows.At(i).IsInNewPage = false;
            rowPlans.Add(grid is { } gg
                ? BuildRowPlan(Rows.At(i), colWidths, cellMap, gg.gridToCell[i], gg.effRowSpan[i], svgFillHeight)
                : BuildRowPlan(Rows.At(i), colWidths, cellMap, svgFillHeight: svgFillHeight));
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
        for (var i = 0; i < rowPlans.Count; i++)
        {
            var plan = rowPlans[i];
            var lineIdx = 0;

            // RowSpan keep-together: rows chained by rowspans form a
            // bundle. When the bundle doesn't fit the space left on this page, a small
            // bundle (≤ 8 rows) moves to the next page whole; a larger one may split but
            // only if at least 4 of its rows stay on this page — else it moves too.
            var forceBreak = false;

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
                if (fit < bundleRows && (bundleRows <= 8 || fit < 4))
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
                if (plan.CellImages is not null && plan.CellTables is null
                    && !atFreshPage && linesFit < plan.LineCount
                    && !brokePageForRow && fitsAnEmptyPage)
                    linesFit = 0;
                // An exact-stack control row (text over a checkbox) never splits:
                // when its full height doesn't fit above the bottom margin the
                // whole row moves to the next page. A NESTED-RESERVE row is the
                // exception — it splits at its reserve-line boundaries (the browser
                // breaks inside a section rather than shipping it whole to the next
                // page), so it is exempt from the whole-row defer.
                if (plan.ExactTotalH > 0 && plan.CellTables is null
                    && !atFreshPage && !brokePageForRow
                    && currentY - pageBottom < plan.ExactTotalH - 1e-3
                    && plan.ExactTotalH <= fullPageTopY - pageBottom + 1e-3)
                    linesFit = 0;
                if (forceBreak) { linesFit = 0; forceBreak = false; }
                if (linesFit <= 0 && (atFreshPage || brokePageForRow))
                    linesFit = Math.Max(1, plan.LineCount - lineIdx);
                if (linesFit <= 0)
                {
                    brokePageForRow = true;
                    // No room on current page — close it and open a new one.
                    LastPageConsumedH.Add(pageStartY - currentY);
                    result.Add(BuildSlicesContent(slices, colWidths, tableX, fontName, cellMap,
                        firstPageDone ? null : page, spanBlocks));
                    firstPageDone = true;
                    slices.Clear();
                    // Seat the first row of the fresh page so its content (text/image,
                    // drawn padTop below the slice top) lands on the margin line rather
                    // than padTop below it. Only when an explicit overflow inset is in
                    // effect and no repeating header precedes the body row.
                    currentY = fullPageTopY +
                        (topMargin > 0 && (repeatCount == 0 || i < repeatCount) ? plan.TopPad : 0);
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
                    Rows.At(i).IsInNewPage = lineIdx == 0 ? true : Rows.At(i).IsInNewPage;
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
                lineIdx += (plan.LineCount == 0 ? 1 : take);
                // After an inner-driven break nothing else lands in the leftover
                // strip — the row's remaining lines resume on the next page.
                if (nestedDrivenH >= 0 && lineIdx < plan.LineCount) forceBreak = true;
                if (fixedH > 0) lineIdx = Math.Max(lineIdx, plan.LineCount);
                if (plan.LineCount == 0) break;
            }
        }
        if (slices.Count > 0)
        {
            LastPageConsumedH.Add(pageStartY - currentY);
            result.Add(BuildSlicesContent(slices, colWidths, tableX, fontName, cellMap,
                firstPageDone ? null : page, spanBlocks));
        }
        if (result.Count == 0) result.Add(Array.Empty<byte>());
        // Height consumed on the (first/only) page — meaningful for the single-page
        // case the flow dispatcher uses to advance a shared cursor; multi-page tables
        // fall back to a page break regardless.
        // The footprint includes the box border on both edges — the columns were inset
        // by one border width at the top and the border draws below the last row.
        LastRenderedHeight = tableTopY - currentY + 2 * tableBorderWidth;
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

    /// <summary>Content height of a row containing CSS line-box cells: the tallest css
    /// cell's summed box heights, floored by the uniform grid the row's remaining
    /// (non-css) cells still need.</summary>
    /// <summary>Record one image a cell draws, keeping the ones already recorded for that
    /// column — a cell may hold several, each on its own line.</summary>
    private static void AddCellImage(RowPlan plan, int col, CellImage img)
    {
        plan.CellImages ??= new Dictionary<int, List<CellImage>>();
        if (!plan.CellImages.TryGetValue(col, out var list))
            plan.CellImages[col] = list = new List<CellImage>();
        list.Add(img);
    }

    /// <summary>A nested table a cell renders in place (the real slice pass): the
    /// built inner table, its height measured at the cell's content width, and the
    /// reserved-line offset that seats it below any text above it in the cell.</summary>
    private sealed class CellNestedTable
    {
        public Table Table = null!;
        public double HeightPt;
        public int LineOffset;
        /// <summary>Reserve lines the grid occupies in its host cell — the host row's
        /// page-break points. 1 = the pre-slice-pass unsplittable reserve.</summary>
        public int LineCount = 1;
        /// <summary>The grid's page slices, built ONCE (against the real page bounds)
        /// at the first host slice covering the reserve; each further covering host
        /// slice consumes the next one.</summary>
        public List<byte[]>? Slices;
        public int Consumed;
        /// <summary>Host slices already SIZED for this reserve during pagination —
        /// indexes <see cref="Table.LastPageConsumedH"/> so a continuation slice takes
        /// the height the grid really consumed on that page.</summary>
        public int PlacedPages;
    }

    /// <summary>Grid-column geometry for a nested reserve at SLICING time — the same
    /// cell walk the slice renderer does, stopped at the reserve's column, so an
    /// inner grid built during pagination lands at exactly the X and pads the draw
    /// pass would have given it.</summary>
    private (double x, double padLeft, double padTop) NestedCellGeom(
        RowPlan plan, double[] colWidths, double tableX, int[] cellMap, int targetCol)
    {
        var row = plan.Row;
        var cellX = tableX;
        var gridToCell = plan.GridToCell;
        for (var col = 0; col < colWidths.Length; col++)
        {
            int origIdx;
            if (gridToCell is not null)
            {
                origIdx = col < gridToCell.Length ? gridToCell[col] : -1;
                if (origIdx == -2) continue;
                if (origIdx < 0 || origIdx >= row.Cells.Count) { cellX += colWidths[col]; continue; }
            }
            else if (plan.ColToCell is { } colToCell)
            {
                origIdx = col < colToCell.Length ? colToCell[col] : -1;
                if (origIdx == -2) continue;
                if (origIdx < 0) { cellX += colWidths[col]; continue; }
            }
            else
            {
                origIdx = cellMap[col];
                if (origIdx >= row.Cells.Count) { cellX += colWidths[col]; continue; }
            }
            var cell = row.Cells.At(origIdx);
            var span = Math.Max(1, Math.Min(cell.ColSpan, colWidths.Length - col));
            var cellWidth = GetCellWidth(colWidths, col, span);
            if (col == targetCol)
            {
                var padding = EffectivePad(cell, row);
                var dp = DefaultPad(cell, row);
                return (cellX, padding?.Left ?? dp, padding?.Top ?? 0);
            }
            cellX += cellWidth;
        }
        return (tableX, 0, 0);
    }

    /// <summary>Height of row line <paramref name="li"/> when a nested-reserve row
    /// SPLITS across pages — each line priced exactly as the exact-stack measure
    /// priced it (a reserve line carries its share of the grid's height as its
    /// FontSize, a boxed line its box, a text line its CSS line box), so the split
    /// arithmetic sums to the same total the whole row takes.</summary>
    private static double NestedRowLineH(RowPlan p, int li)
    {
        double h = 0;
        foreach (var cellLines in p.CellLines)
        {
            if (li >= cellLines.Count) continue;
            var cl = cellLines[li];
            var lh = cl.BoxH > 0 ? cl.BoxH
                : cl.ImgReserve ? cl.FontSize
                : cl.Text.Length > 0 || cl.Boxes is { Count: > 0 } || cl.HtmlEngine
                    ? CssLineBoxPt(cl.FontSize > 0 ? cl.FontSize : DefaultCellFontPt)
                    : 0;
            if (lh > h) h = lh;
        }
        return h;
    }

    private static double CssRowContentH(RowPlan p)
    {
        var legacy = p.NonCssLineCount == 0
            ? 0
            : (p.NonCssLineCount - 1) * p.LineHeight + p.TightLine;
        // CssContentTight, when set, is the same stack measured to the last baseline.
        return Math.Max(p.CssContentTight > 0 ? p.CssContentTight : p.CssContentH, legacy);
    }

    /// <summary>A row's layout plan: per-cell wrapped lines and vertical metrics.</summary>
    private sealed class RowPlan
    {
        public Row Row = null!;
        public List<List<CellLine>> CellLines = new();  // [cell][line]
        public double LineHeight;        // uniform per-row line height (max across cells)
        public double TightLine;         // tight (no-leading) height of the tallest line; used so a
                                         // block of n lines is (n-1)·LineHeight + TightLine tall
        public double CssContentTight;   // CssContentH trimmed to the last baseline (CSS run boxes)
        public double VertPadding;       // max (padTop + padBottom) across cells
        public int LineCount;            // max line count across cells; 0 = empty row
        public double MinBlankHeight;    // height for an empty row (FixedRowHeight/MinRowHeight)
        public double ExactTotalH;       // exact row height when a control cell stacks text + box (0 = uniform grid)
        public double TopPad;            // max cell top padding — used to seat the first row
                                         // on an overflow page so its content reaches the margin
        // col -> the images that cell draws, in line order. A LIST, not one entry: a cell
        // holding several pictures kept only the last when this was keyed by column alone.
        public Dictionary<int, List<CellImage>>? CellImages;
        // col -> the NESTED tables that cell renders (the real slice pass, replacing
        // the flatten): each with its measured height and the reserved-line offset.
        public Dictionary<int, List<CellNestedTable>>? CellTables;
        // col -> inline layout (rows of left-to-right text/graph runs) for cells that
        // mix Graph paragraphs with inline text; null when the cell has no graph.
        public Dictionary<int, List<List<InlineItem>>>? CellInline;
        public bool IsBlankRow;          // content is whitespace only — rendered tight (no padding)
        // Cells laid out as CSS line boxes (mixed per-line font sizes from styled HTML):
        // their lines position by cumulative BoxH with BaseOff baselines instead of the
        // uniform LineHeight grid. CssContentH = the tallest such cell's summed box height.
        public HashSet<int>? CssCells;
        public double CssContentH;
        // Max line count among NON-css cells — the uniform-grid height still applies to them.
        public int NonCssLineCount;
        // RowSpan grid mode (null otherwise): grid column → cell index starting there
        // (-1 vacant/foreign-span, -2 covered by this row's own ColSpan), and each cell's
        // effective (clamped) row span. Cells with EffRowSpan > 1 are drawn by the
        // span-block pass, not the per-row slice render.
        public int[]? GridToCell;
        public int[]? EffRowSpan;

        // Non-grid rows: grid column → cell index honouring ColSpan (-1 vacant past the
        // last cell, -2 covered by an earlier cell's span). Null when the caller passed a
        // non-identity cellMap (column-band chunking keeps its own mapping).
        public int[]? ColToCell;
    }

    /// <summary>One item on an inline cell line: either a text run or a Graph, with its
    /// x-offset (from the cell content-left) and size resolved during layout.</summary>
    private sealed class InlineItem
    {
        public Aspose.Pdf.Drawing.Graph? Graph;     // non-null → draw this graph
        public string? Text;                        // non-null → show this text run
        public double FontSize;
        public Color? Color;
        public double X;                            // offset from the cell content-left
        public double Width;
        public double Height;
        public double BaseFontSize;                 // line baseline reference size (pre sub/super shrink)
        public double BaselineShift;                // baseline Y shift (+super / -sub); 0 = normal
        public bool Empty;                          // emit an empty show-text run at this position
        public byte[]? Ttf;                         // per-run embedded TrueType (Type0) — null = table default font
        public string? FontName;                    // base font name for the embedded run
        public byte[]? ImageData;                   // non-null → blit this image at the item box
    }

    /// <summary>An <see cref="Image"/> paragraph placed inside a table cell. Carries the
    /// already-read bytes and the resolved display size/alignment so the render pass can
    /// blit it at the cell's top via <see cref="Page.AddImage(byte[], Rectangle)"/>.</summary>
    private sealed class CellImage
    {
        public byte[] Data = null!;
        public double Width;
        public double Height;
        public HorizontalAlignment Align;

        /// <summary>Extra x inset from the cell content-left — used when an
        /// aspect-fitted image is centred inside its declared Fix box.</summary>
        public double XOffset;

        /// <summary>Number of text/content lines preceding this image in the cell, so the
        /// render pass seats the image on its own line below them instead of at the cell top
        /// (e.g. a title line above a centred logo).</summary>
        public int LineOffset;
    }

    /// <summary>True when the text contains a CJK / Han / Kana / Hangul character — the only
    /// case where a fragment's embedded font must override the table's Standard-14 render path
    /// (a plain embedded Latin font keeps the existing path to avoid changing green layouts).</summary>
    private static bool IsCjk(int o) =>
        (o >= 0x3400 && o <= 0x9FFF) || (o >= 0x3000 && o <= 0x30FF)
        || (o >= 0xF900 && o <= 0xFAFF) || (o >= 0xAC00 && o <= 0xD7AF);

    /// <summary>True when the text contains any CJK ideograph, kana, or Hangul character.</summary>
    private static bool ContainsCjk(string s)
    {
        foreach (var c in s) if (IsCjk(c)) return true;
        return false;
    }

    /// <summary>True when the text has CJK content AND the embedded font actually covers every
    /// CJK glyph. Only then does routing through Type0/CID help — a font without the glyphs
    /// (e.g. Arial on Japanese) must keep the existing path so its layout is unchanged.</summary>
    private static bool CjkCoveredBy(string s, byte[] ttf)
    {
        var gp = GetInlineGlyphParser(ttf);
        if (gp is null) return false;
        var any = false;
        foreach (var ch in s)
        {
            if (!IsCjk(ch)) continue;
            any = true;
            if (!gp.CMap.TryGetValue(ch, out var g) || g == 0) return false;
        }
        return any;
    }

    /// <summary>A cell line: already-wrapped text with per-line font/color.</summary>
    private sealed class CellLine
    {
        public string Text = "";
        public double FontSize;
        /// <summary>The line came from the HTML engine's own parse, so a blank one is a
        /// real line box the markup asked for (a leading <c>&lt;br&gt;</c>) rather than a
        /// spacer the cell may collapse.</summary>
        public bool HtmlEngine;
        public Color? ForegroundColor;
        public bool Bold;                // draw with the bold face of the table font
        // The source fragment demands CSS line boxes even at a uniform size
        // (see TextFragment.CssLineBoxAlways).
        public bool CssForce;

        /// <summary>Extra left inset for this line within the cell content box —
        /// list items in an HTML cell indent by the list's margin-start, with
        /// the bullet hanging to the left of the indented text.</summary>
        public double LeftIndent;

        /// <summary>Extra space above this line from the source fragment's
        /// Margin.Top (applied to the fragment's first wrapped line) — label
        /// groups inside a row-spanning sidebar cell are separated by each
        /// fragment's own top margin.</summary>
        public double TopGap;

        /// <summary>True for the blank lines that reserve a cell image's vertical
        /// footprint — they must survive the leading-blank-spacer drop, or the
        /// text after the image rides up underneath its blit.</summary>
        public bool ImgReserve;

        /// <summary>When set, this line renders a form-control glyph (e.g. a radio
        /// button circle) ahead of <see cref="Text"/>, which holds the option caption.</summary>
        public Aspose.Pdf.Forms.RadioButtonOptionField? Option;

        /// <summary>Radio options riding INLINE in <see cref="Text"/>: one entry per
        /// <see cref="InlineRadioChar"/>/<see cref="InlineRadioCheckedChar"/> in the
        /// text, in order. The render pass draws each as a circle glyph advancing the
        /// pen (captions are ordinary line text between the markers) and places the
        /// option's widget over its glyph.</summary>
        public List<Aspose.Pdf.Forms.RadioButtonOptionField>? InlineOptions;

        /// <summary>When set, this line carries a checkbox field whose widget is placed at
        /// the laid-out cell position (its /AP appearance supplies the box and check glyph).</summary>
        public Aspose.Pdf.Forms.CheckboxField? Checkbox;

        /// <summary>Hyperlink carried by the source fragment, if any. Rendered as a
        /// link annotation over the line's text rectangle.</summary>
        public Hyperlink? Hyperlink;

        /// <summary>FootNote carried by the source fragment (attached to the
        /// fragment's last laid-out line): its superscript marker renders right
        /// after this line's text and the note body joins the page-bottom band.</summary>
        public Note? FootNote;

        /// <summary>Inline hyperlinked runs (HTML <c>&lt;a&gt;</c> inside a table cell):
        /// pre-measured x-offset/width pairs (same metrics that laid the line out),
        /// each annotated over just its own glyph run rather than the whole line.</summary>
        public List<(double XOff, double W, Hyperlink Link)>? LinkRuns;

        /// <summary>Kerned embedded-face width of <see cref="Text"/> (set with <see cref="Runs"/>);
        /// used instead of the Standard-14 estimate when centring/right-aligning the line.</summary>
        public double KernedWidth;

        /// <summary>When set, this line holds already-shaped Arabic/Unicode text (visual order)
        /// that must be drawn with the embedded TrueType program here as a Type0/CID font (the
        /// table's default Standard-14 font can't display it). The render pass embeds the font
        /// and emits the line as hex glyph IDs.</summary>
        public byte[]? Type0Ttf;
        public string? Type0FontName;
        // When set, the Type0 render emits each space-separated token as its own positioned
        // run (space-separated CJK), so the absorber surfaces per-token fragments. Not set for
        // shaped Arabic (which is one visual-order run and must not be re-split).
        public bool Type0SplitTokens;
        // Form-grid mixed-style line: the render draws these segments in order, each
        // embedded in its own face variant, advancing by the segment's kerned width.
        public List<(string Text, byte[] Ttf, string Name)>? StyleRuns;

        /// <summary>Horizontal alignment for this individual line within the cell content box.
        /// Cell text and TextFragments inherit the cell's resolved alignment (cell → row →
        /// table DefaultCellTextState); an HtmlFragment keeps its own block alignment (left
        /// unless its style requests centre/right), so a single cell can mix alignments.</summary>
        public HorizontalAlignment Align = HorizontalAlignment.Left;

        // CSS line-box metrics carried from the source fragment (HTML styled-cell path):
        // ascent/descent as fractions of em. Zero = legacy layout.
        public double CssAsc;
        public double CssDesc;
        // Resolved CSS line-box geometry (set only when the OWNING CELL is in css-box
        // mode — its lines carry mixed font sizes): the box height this line occupies
        // and the baseline offset from the box top. Zero = legacy uniform stepping.
        public double BoxH;
        public double BaseOff;

        // Emit the Type0 run as a TJ array with the font's pair-kerning adjustments
        // (HTML-engine cell path only — those runs are kerned).
        public bool KernTj;

        // HTML-engine cell line: styled serif runs drawn at per-run x-offsets on the
        // common baseline (mixed bold/size within one line). Null = single-style line.
        public List<HtmlRun>? Runs;

        /// <summary>Inline boxes drawn behind this line's text (title plates, status
        /// pills) — see <see cref="InlineBoxDecoration"/>.</summary>
        public List<InlineBoxDecoration>? Boxes;
    }

    /// <summary>One slice of a row on one page. A row with content taller than the
    /// available page height produces multiple slices across consecutive pages.</summary>
    private sealed class RowSlice
    {
        public RowPlan Plan = null!;
        public int LineStart;
        public int LineCount;
        public double TopY;
        public double Height;
        public int RowIndex;
    }

    /// <summary>A cell spanning multiple rows (RowSpan &gt; 1). Its background, border and
    /// content are drawn once per page over the union of its rows' slices; the rows below
    /// the start row leave the spanned grid columns vacant.</summary>
    private sealed class SpanBlock
    {
        public int StartRow;             // first row index (inclusive)
        public int EndRow;               // last row index (exclusive), clamped to Rows.Count
        public int GridCol;              // starting grid column
        public int ColSpan;              // grid columns covered
        public Cell Cell = null!;
        public Row Row = null!;
        public List<CellLine> Lines = new();
        public double LineHeight;
        public double TightLine;
    }

    /// <summary>Grid placement for tables using RowSpan: assigns every cell its grid column
    /// the way an HTML table does (cells flow left-to-right past columns still occupied by
    /// spans from earlier rows). Returns null when no cell spans rows — the legacy
    /// column-index-equals-cell-index layout stays in effect for those tables.</summary>
    private (int[][] gridToCell, int[][] effRowSpan, int gridCols, List<SpanBlock> blocks)? ComputeGrid()
    {
        var anySpan = false;
        for (var r = 0; r < Rows.Count && !anySpan; r++)
        {
            var row = Rows.At(r);
            for (var c = 0; c < row.Cells.Count; c++)
                if (row.Cells.At(c).RowSpan > 1) { anySpan = true; break; }
        }
        if (!anySpan) return null;

        var gridToCell = new int[Rows.Count][];
        var effRowSpan = new int[Rows.Count][];
        var blocks = new List<SpanBlock>();
        // Columns still held by rowspans from earlier rows: (colStart, colSpan, rowsLeft).
        var pending = new List<(int colStart, int colSpan, int remaining)>();
        var gridCols = 1;

        for (var r = 0; r < Rows.Count; r++)
        {
            var row = Rows.At(r);
            var occupied = new List<bool>();
            void Reserve(int col) { while (occupied.Count <= col) occupied.Add(false); occupied[col] = true; }
            foreach (var (colStart, colSpan, _) in pending)
                for (var k = 0; k < colSpan; k++) Reserve(colStart + k);

            // -1 = vacant or held by a foreign rowspan (advance x by the column width);
            // -2 = covered by this row's own ColSpan cell (x already advanced by the span).
            var mapping = new List<int>();
            void Put(int col, int val)
            {
                while (mapping.Count <= col) mapping.Add(-1);
                mapping[col] = val;
            }
            var eff = new int[row.Cells.Count];
            var cursor = 0;
            // Spans placed in THIS row start covering rows at r+1; they must not be
            // decremented at the end of row r, so collect them separately.
            var placedThisRow = new List<(int colStart, int colSpan, int remaining)>();
            for (var ci = 0; ci < row.Cells.Count; ci++)
            {
                var cell = row.Cells.At(ci);
                var colSpan = Math.Max(1, cell.ColSpan);
                bool Fits(int at)
                {
                    for (var k = 0; k < colSpan; k++)
                        if (at + k < occupied.Count && occupied[at + k]) return false;
                    return true;
                }
                while (!Fits(cursor)) cursor++;
                Put(cursor, ci);
                for (var k = 1; k < colSpan; k++) Put(cursor + k, -2);
                eff[ci] = Math.Min(Math.Max(1, cell.RowSpan), Rows.Count - r);
                if (eff[ci] > 1)
                {
                    placedThisRow.Add((cursor, colSpan, eff[ci] - 1));
                    blocks.Add(new SpanBlock
                    {
                        StartRow = r, EndRow = r + eff[ci],
                        GridCol = cursor, ColSpan = colSpan,
                        Cell = cell, Row = row,
                    });
                }
                cursor += colSpan;
            }
            if (mapping.Count > gridCols) gridCols = mapping.Count;
            if (occupied.Count > gridCols) gridCols = occupied.Count;
            gridToCell[r] = mapping.ToArray();
            effRowSpan[r] = eff;
            for (var p = pending.Count - 1; p >= 0; p--)
            {
                var (cs, csp, rem) = pending[p];
                if (rem <= 1) pending.RemoveAt(p);
                else pending[p] = (cs, csp, rem - 1);
            }
            pending.AddRange(placedThisRow);
        }

        // Pad every row's mapping to the full grid width (vacant → -1).
        for (var r = 0; r < Rows.Count; r++)
        {
            if (gridToCell[r].Length == gridCols) continue;
            var padded = new int[gridCols];
            for (var c = 0; c < gridCols; c++) padded[c] = c < gridToCell[r].Length ? gridToCell[r][c] : -1;
            gridToCell[r] = padded;
        }
        return (gridToCell, effRowSpan, gridCols, blocks);
    }

    /// <summary>Wrapped content lines for a row-spanning cell (text-only: TextFragment /
    /// HtmlFragment). Mirrors the plain-text path of <see cref="BuildRowPlan"/>.</summary>
    private void BuildSpanBlockLines(SpanBlock block, double[] colWidths)
    {
        var cell = block.Cell;
        var row = block.Row;
        var padding = EffectivePad(cell, row);
        var dp = DefaultPad(cell, row);
        var padLeft = padding?.Left ?? dp;
        var padRight = padding?.Right ?? dp;
        var width = GetCellWidth(colWidths, block.GridCol, block.ColSpan);
        var availWidth = width - padLeft - padRight;

        var textState = cell.DefaultCellTextState ?? row.DefaultCellTextState ?? DefaultCellTextState;
        var defaultFontSize = ResolveCellFontSize(cell, row);
        var cellAlign = ResolveCellAlignment(cell, row);
        double maxLine = 0, tight = 0;

        foreach (var paragraph in cell.Paragraphs)
        {
            string? text = null;
            double fragFontSize = defaultFontSize;
            Color? color = null;
            var fragBold = false;
            var fragAlign = cellAlign;
            if (paragraph is TextFragment tf)
            {
                text = tf.Text;
                fragFontSize = ResolveFragmentFontSize(tf, defaultFontSize);
                color = tf.TextState.ForegroundColor ?? textState?.ForegroundColor;
                fragBold = tf.TextState.IsBold;
                if (!fragBold)
                    foreach (var fseg in tf.Segments)
                        if (fseg.TextState.IsBold && !string.IsNullOrEmpty(fseg.Text))
                        { fragBold = true; break; }
            }
            else if (paragraph is HtmlFragment html)
            {
                text = HtmlFragment.StripHtmlTags(html.HtmlContent ?? "");
                color = textState?.ForegroundColor;
                // The fragment's own stylesheet text-align overrides the
                // spanning-cell default (a block declared left stays left in a
                // centred span cell). Its stylesheet font-size (px) sizes the
                // lines the same way it does in a grid cell.
                var hAligned = (html.HtmlContent ?? "").Replace(" ", string.Empty);
                if (hAligned.IndexOf("text-align:left", StringComparison.OrdinalIgnoreCase) >= 0)
                    fragAlign = HorizontalAlignment.Left;
                else if (hAligned.IndexOf("text-align:right", StringComparison.OrdinalIgnoreCase) >= 0)
                    fragAlign = HorizontalAlignment.Right;
                else if (hAligned.IndexOf("text-align:center", StringComparison.OrdinalIgnoreCase) >= 0)
                    fragAlign = HorizontalAlignment.Center;
                var hfs = Regex.Match(html.HtmlContent ?? "", @"font-size\s*:\s*([\d.]+)\s*px",
                    RegexOptions.IgnoreCase);
                if (hfs.Success && double.TryParse(hfs.Groups[1].Value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var hpx) && hpx > 0)
                    fragFontSize = hpx * 0.75;
            }
            // A deliberately-kept blank paragraph (a styled &nbsp; spacer <p>) reserves
            // its line box in the spanning cell as vertical space.
            if (string.IsNullOrEmpty(text) && paragraph is TextFragment kb && kb.CssKeepBlank)
            {
                var kfs = ResolveFragmentFontSize(kb, defaultFontSize);
                block.Lines.Add(new CellLine { Text = " ", FontSize = kfs, Align = fragAlign });
                if (kfs > maxLine) { maxLine = kfs; tight = kfs; }
                continue;
            }
            if (string.IsNullOrEmpty(text)) continue;
            if (fragFontSize > maxLine) { maxLine = fragFontSize; tight = fragFontSize; }
            var fragFirstLine = block.Lines.Count;
            // Opted-in band tables (HonorCellFontFaces): a spanning cell's serif
            // fragment draws in the embedded serif face with its real kerned
            // metrics, exactly like the grid-cell path — the Standard-14 Helvetica
            // fallback wraps wider than the serif-measured span width.
            if (HonorCellFontFaces && paragraph is TextFragment sbtf
                && IsSerifCssFamily(sbtf.TextState.Font?.FontName)
                && (fragBold ? BoldSerifTtf() : SerifTtf()) is { } spanSerifTtf)
            {
                if (text.IndexOf('☐') >= 0 || text.IndexOf('☒') >= 0)
                    text = text.Replace('☐', '□').Replace('☒', '□');
                foreach (var segment in text.Split('\n'))
                {
                    if (segment.Length == 0) continue;
                    // A whitespace-only paragraph (an &nbsp; spacer <p>) keeps its
                    // line box — the kerned wrap would swallow it entirely.
                    if (string.IsNullOrWhiteSpace(segment.Replace(' ', ' ')))
                    {
                        block.Lines.Add(new CellLine
                            { Text = segment, FontSize = fragFontSize, ForegroundColor = color, Align = fragAlign });
                        continue;
                    }
                    foreach (var l in WrapKernedLines(segment, fragFontSize, spanSerifTtf, availWidth))
                        block.Lines.Add(new CellLine
                        {
                            Text = l,
                            FontSize = fragFontSize,
                            ForegroundColor = color,
                            Align = fragAlign,
                            Type0Ttf = spanSerifTtf,
                            Type0FontName = fragBold ? "Times New Roman Bold" : "Times New Roman",
                            KernTj = true,
                            KernedWidth = MeasureWidthKerned(l, fragFontSize, spanSerifTtf),
                        });
                }
                if (ParagraphMargin(paragraph) is { Top: > 0 } sbm && block.Lines.Count > fragFirstLine)
                    block.Lines[fragFirstLine].TopGap = sbm.Top;
                continue;
            }
            foreach (var segment in text.Split('\n'))
            {
                if (segment.Length == 0) continue;
                if (cell.IsWordWrapped || MeasureWidth(segment, fragFontSize) > availWidth)
                {
                    foreach (var l in WrapText(segment, fragFontSize, availWidth))
                        block.Lines.Add(new CellLine { Text = l, FontSize = fragFontSize, ForegroundColor = color, Bold = fragBold, Align = fragAlign });
                }
                else
                    block.Lines.Add(new CellLine { Text = segment, FontSize = fragFontSize, ForegroundColor = color, Bold = fragBold, Align = fragAlign });
            }
            // The fragment's own top margin separates it from the previous
            // fragment in the spanning cell (applied above its first line).
            if (paragraph.Margin is { Top: > 0 } fm && block.Lines.Count > fragFirstLine)
                block.Lines[fragFirstLine].TopGap = fm.Top;
        }
        block.LineHeight = maxLine > 0 ? maxLine : defaultFontSize;
        block.TightLine = tight > 0 ? tight : block.LineHeight;
    }

    /// <summary>Resolve a cell's effective horizontal text alignment by walking
    /// cell → row → table. The default (<see cref="HorizontalAlignment.Left"/>/None) at a
    /// level means "inherit", so a table-wide <c>DefaultCellTextState.HorizontalAlignment =
    /// Center</c> reaches cells that don't override it — previously the cell's auto-created
    /// <c>DefaultCellTextState</c> (Left) shadowed the table default wholesale.</summary>
    private HorizontalAlignment ResolveCellAlignment(Cell cell, Row row)
    {
        static bool Set(HorizontalAlignment a) =>
            a != HorizontalAlignment.Left && a != HorizontalAlignment.None;

        if (Set(cell.Alignment)) return cell.Alignment;
        if (cell.DefaultCellTextState is { } cts && Set(cts.HorizontalAlignment)) return cts.HorizontalAlignment;
        if (row.DefaultCellTextState is { } rts && Set(rts.HorizontalAlignment)) return rts.HorizontalAlignment;
        if (DefaultCellTextState is { } tts && tts.HorizontalAlignment != HorizontalAlignment.None)
            return tts.HorizontalAlignment;
        return HorizontalAlignment.Left;
    }

    /// <summary>Resolve a cell's default text size by walking cell → row → table
    /// <see cref="DefaultCellTextState"/> for the first level whose FontSize was
    /// explicitly set (<see cref="Aspose.Pdf.Text.TextState.FontSizeTouched"/>).
    /// A TextState's FontSize defaults to 10 pt, so a plain <c>?? FontSize</c> chain
    /// stops at the cell's auto-created state and lets that 10 pt default shadow an
    /// explicit <c>row.DefaultCellTextState.FontSize = 8</c> — mirroring the
    /// alignment walk fixes that. Falls back to the effective default size when no
    /// level set one, preserving prior behaviour for untouched tables.</summary>
    private double ResolveCellFontSize(Cell cell, Row row)
    {
        if (cell.DefaultCellTextState is { FontSizeTouched: true } c) return c.FontSize;
        if (row.DefaultCellTextState is { FontSizeTouched: true } r) return r.FontSize;
        if (DefaultCellTextState is { FontSizeTouched: true } t) return t.FontSize;
        var ts = cell.DefaultCellTextState ?? row.DefaultCellTextState ?? DefaultCellTextState;
        return ts?.FontSize > 0 ? ts.FontSize : 12;
    }

    /// <summary>Best-effort horizontal alignment for an HtmlFragment from its inline CSS.
    /// A block-level HtmlFragment defaults to left and does NOT inherit the cell's text
    /// alignment (matching the generator, where a plain HtmlFragment stays left in a
    /// centred cell while a <c>justify-content:center</c>/<c>text-align:center</c> one centres).</summary>
    private static HorizontalAlignment ParseHtmlAlignment(string? html)
    {
        if (string.IsNullOrEmpty(html)) return HorizontalAlignment.Left;
        var h = html.Replace(" ", string.Empty);
        if (h.IndexOf("text-align:center", StringComparison.OrdinalIgnoreCase) >= 0 ||
            h.IndexOf("justify-content:center", StringComparison.OrdinalIgnoreCase) >= 0 ||
            h.IndexOf("align=\"center\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
            h.IndexOf("align='center'", StringComparison.OrdinalIgnoreCase) >= 0)
            return HorizontalAlignment.Center;
        if (h.IndexOf("text-align:right", StringComparison.OrdinalIgnoreCase) >= 0 ||
            h.IndexOf("justify-content:flex-end", StringComparison.OrdinalIgnoreCase) >= 0)
            return HorizontalAlignment.Right;
        return HorizontalAlignment.Left;
    }

    /// <summary>The <c>&lt;a href&gt;</c> runs of an HtmlFragment cell, as (anchor text, url)
    /// pairs in document order — the same shape a TextFragment carries in
    /// <c>HtmlAnchors</c>, so the cell's line layout annotates each run over its own
    /// characters. Null when the markup holds no usable anchor.</summary>
    private static List<(string Text, string Url)>? ParseCellHtmlAnchors(string? html)
    {
        if (string.IsNullOrEmpty(html)) return null;
        List<(string Text, string Url)>? anchors = null;
        foreach (Match m in Regex.Matches(html,
                     @"<a\b[^>]*\bhref\s*=\s*(?:'(?<u>[^']*)'|""(?<u>[^""]*)""|(?<u>[^\s>]+))[^>]*>(?<t>.*?)</a\s*>",
                     RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var atext = HtmlFragment.StripHtmlTags(m.Groups["t"].Value).Trim();
            var url = m.Groups["u"].Value.Trim();
            if (atext.Length == 0 || url.Length == 0) continue;
            (anchors ??= new()).Add((atext, url));
        }
        return anchors;
    }

    /// <summary>Lines for an HTML cell whose body is block text followed by a
    /// <c>&lt;ul&gt;</c>/<c>&lt;ol&gt;</c> list; null when the fragment carries no
    /// list items. The fragment's stylesheet font-size (px, browser 0.75 px→pt)
    /// sizes every line. Items indent by the list margin-start (CSS default
    /// 40px) and keep that indent on continuation lines, with the bullet
    /// hanging to the left of each item's first line.</summary>
    private List<CellLine>? BuildHtmlListCellLines(string? htmlContent, double availWidth,
        double defaultSize, Color? color, HorizontalAlignment blockAlign)
    {
        var h = htmlContent ?? "";
        if (h.IndexOf("<li", StringComparison.OrdinalIgnoreCase) < 0) return null;

        var size = defaultSize;
        var fsm = Regex.Match(h, @"font-size\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
        if (fsm.Success && double.TryParse(fsm.Groups[1].Value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var px) && px > 0)
            size = px * 0.75;

        var result = new List<CellLine>();
        var listStart = Regex.Match(h, @"<[uo]l\b", RegexOptions.IgnoreCase);
        var head = HtmlFragment.StripHtmlTags(listStart.Success ? h[..listStart.Index] : h).Trim();
        // Justified block text in a narrow list cell seats each line at the
        // column's right edge (short lines keep their natural width).
        var headAlign = (h.Replace(" ", string.Empty))
            .IndexOf("text-align:justify", StringComparison.OrdinalIgnoreCase) >= 0
            ? HorizontalAlignment.Right : blockAlign;
        foreach (var seg in head.Split('\n'))
        {
            var s = seg.Trim();
            if (s.Length == 0) continue;
            foreach (var l in WrapText(s, size, availWidth))
                result.Add(new CellLine { Text = l, FontSize = size, ForegroundColor = color, Align = headAlign, BoxH = size, BaseOff = size });
        }

        const double listIndent = 40 * 0.75; // CSS default margin-start
        var bulletPrefix = (char)0x95 + " "; // WinAnsi bullet
        var bulletW = MeasureWidthExact(bulletPrefix, size);
        foreach (Match im in Regex.Matches(h, @"<li\b[^>]*>(.*?)</li>",
                     RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var item = HtmlFragment.StripHtmlTags(im.Groups[1].Value).Trim();
            if (item.Length == 0) continue;
            var wrapped = WrapText(item, size, availWidth - listIndent + bulletW);
            for (var wi = 0; wi < wrapped.Count; wi++)
                result.Add(new CellLine
                {
                    Text = wi == 0 ? bulletPrefix + wrapped[wi] : wrapped[wi],
                    FontSize = size,
                    ForegroundColor = color,
                    Align = HorizontalAlignment.Left,
                    LeftIndent = wi == 0 ? listIndent - bulletW : listIndent,
                    BoxH = size,
                    BaseOff = size,
                });
        }
                // The list block reserves a strip below its last item (the list bottom
        // margin collapsed with the item's negative margin), keeping the row
        // height a list-bearing cell produces.
        result.Add(new CellLine { Text = "", FontSize = size, BoxH = size / 2, BaseOff = size / 2 });
        return result.Count > 0 ? result : null;
    }

    /// <summary>Default per-side cell padding when no explicit padding is set. A "bare" cell —
    /// no border and no explicit margin/cell-padding anywhere in the cell → row → table chain —
    /// gets 0, matching the generator (its borderless default cell seats text at the
    /// content edge and its row pitch is just the font size). Cells that carry a border or
    /// explicit padding keep the historical 2pt default (their templates depend on it).</summary>
    private double DefaultPad(Cell cell, Row row)
    {
        // A BorderInfo with Side=None is decorative-only — such cells behave
        // exactly like borderless ones (text at the content edge,
        // row pitch = font size), so only a border that actually DRAWS keeps
        // the historical 2 pt padding.
        static bool Draws(BorderInfo? b) => b is not null && b.Side != BorderSide.None;
        var hasBorder = Draws(cell.Border) || Draws(row.DefaultCellBorder) || Draws(row.Border)
            || Draws(DefaultCellBorder) || Draws(Border);
        var hasPad = cell.Margin is not null || row.DefaultCellPadding is not null || DefaultCellPadding is not null;
        return (!hasBorder && !hasPad) ? 0.0 : 2.0;
    }
}
