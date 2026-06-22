using System.Collections;
using System.Globalization;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

/// <summary>
/// How a <see cref="Table"/> sizes its columns (API parity).
/// </summary>
public enum ColumnAdjustment
{
    /// <summary>Use <c>ColumnWidths</c> as specified.</summary>
    Customized,
    /// <summary>Distribute available width equally across columns.</summary>
    AutoFitToWindow,
    /// <summary>Size columns to fit the widest cell content.</summary>
    AutoFitToContent,
}

/// <summary>Corner-rounding style for a <see cref="Table"/>'s border box.</summary>
public enum BorderCornerStyle
{
    /// <summary>Sharp corners (default).</summary>
    None,

    /// <summary>Rounded corners (using <c>BorderInfo.RoundedBorderRadius</c>).</summary>
    Round,
}

public enum TableBroken
{
    None,
    Vertical,
    VerticalInSamePage,
    IsInNextPage,
}

/// <summary>
/// Represents a table that can be added to a PDF page.
/// </summary>
public class Table : BaseParagraph
{
    /// <summary>
    /// Space-separated column widths (e.g. "100 200 150").
    /// If not set, columns are distributed equally.
    /// </summary>
    public string? ColumnWidths { get; set; }

    /// <summary>Table border.</summary>
    public BorderInfo? Border { get; set; }

    /// <summary>Default cell border applied to all cells unless overridden.</summary>
    public BorderInfo? DefaultCellBorder { get; set; }

    /// <summary>Default cell padding.</summary>
    public MarginInfo? DefaultCellPadding { get; set; }

    /// <summary>Default text state for cells. Auto-initialized so callers can do
    /// <c>table.DefaultCellTextState.HorizontalAlignment = ...</c> without null-checking.</summary>
    public TextState? DefaultCellTextState { get; set; } = new TextState();

    /// <summary>Table background color.</summary>
    public Color? BackgroundColor { get; set; }

    /// <summary>Left position of the table on the page.</summary>
    public float Left { get; set; }

    /// <summary>Top position of the table on the page (distance from page top).</summary>
    public float Top { get; set; }

    /// <summary>Table margin.</summary>
    public new MarginInfo Margin { get; set; } = new();

    /// <summary>Number of rows to repeat at the top of each page when the table spans pages.</summary>
    public int RepeatingRowsCount { get; set; }

    /// <summary>Maximum columns count for the table. Stored only;
    /// currently not honoured by the paginator, exposed for source-level
    /// API compatibility.</summary>
    public int RepeatingColumnsCount { get; set; }

    /// <summary>How the table breaks across pages.</summary>
    public TableBroken Broken { get; set; }

    /// <summary>
    /// Whether the table is allowed to break across pages.
    /// Setting false is a hint to the renderer — rows are still split when
    /// a single row doesn't fit, but the flag is honoured for best-effort packing.
    /// </summary>
    public bool IsBroken { get; set; } = true;

    /// <summary>
    /// How columns are sized when the table is rendered (API parity).
    /// </summary>
    public ColumnAdjustment ColumnAdjustment { get; set; } = ColumnAdjustment.Customized;

    /// <summary>The collection of rows in this table.</summary>
    public Rows Rows { get; }

    /// <summary>Default column-width used when <see cref="ColumnWidths"/>
    /// is empty. Stored as a string (e.g. <c>"100"</c>) to mirror Aspose.PDF for .NET.</summary>
    public string? DefaultColumnWidth { get; set; }

    /// <summary>Corner-rounding style applied to the table's border box.</summary>
    public BorderCornerStyle CornerStyle { get; set; } = BorderCornerStyle.None;

    /// <summary>When true, cell-border widths count against cell padding and
    /// row-height calculations. Stored only — the FOSS paginator always
    /// treats borders as exterior to the cell content.</summary>
    public bool IsBordersIncluded { get; set; }

    /// <summary>Optional indicator drawn at the page break when a row is
    /// split across pages. Stored only.</summary>
    public TextFragment? BreakText { get; set; }

    /// <summary>Default text state for the rows repeated on continuation
    /// pages (see <see cref="RepeatingRowsCount"/>). Stored only.</summary>
    public TextState? RepeatingRowsStyle { get; set; }

    public HorizontalAlignment Alignment { get; set; } = HorizontalAlignment.Left;

    public Table()
    {
        Rows = new Rows(this);
    }

    // ── Layout helpers ─────────────────────────────────────────────────────

    /// <summary>Sum of column widths from <see cref="ColumnWidths"/>.
    /// Zero when no widths are configured.</summary>
    public double GetWidth()
    {
        if (string.IsNullOrEmpty(ColumnWidths)) return 0;
        double total = 0;
        foreach (var tok in ColumnWidths.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            if (double.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out var w))
                total += w;
        return total;
    }

    /// <summary>Approximate rendered height of the table on
    /// <paramref name="parentPage"/>. Sums each row's fixed or estimated
    /// height; the result is used by the paginator to decide whether the
    /// whole table fits on the page.</summary>
    public double GetHeight(Page parentPage)
    {
        _ = parentPage;
        double total = 0;
        foreach (var row in Rows)
        {
            var padding = row.DefaultCellPadding ?? DefaultCellPadding;
            var padV = (padding?.Top ?? 2) + (padding?.Bottom ?? 2);
            var ts = row.DefaultCellTextState ?? DefaultCellTextState;
            var fontSize = ts?.FontSize ?? 12;
            total += row.FixedRowHeight > 0
                ? row.FixedRowHeight
                : Math.Max(row.MinRowHeight, fontSize + padV);
        }
        return total;
    }

    /// <summary>Apply <paramref name="textState"/> to every cell in the
    /// given (1-based) column number. Cells whose own TextState was set
    /// independently are not overwritten.</summary>
    public void SetColumnTextState(int colNumber, Aspose.Pdf.Text.TextState textState)
    {
        if (textState is null) return;
        foreach (var row in Rows)
        {
            var idx = colNumber - 1;
            if (idx < 0 || idx >= row.Cells.Count) continue;
            var cell = row.Cells[idx];
            cell.DefaultCellTextState ??= textState;
        }
    }

    /// <summary>Shallow clone of the table. The Rows collection is reused
    /// (the contents are not deep-copied) which matches the Aspose.PDF for .NET
    /// <c>object Clone()</c> contract.</summary>
    public override object Clone()
    {
        var t = (Table)MemberwiseClone();
        return t;
    }

    // ── Data import helpers (System.Data.DataTable / DataView) ──────────────
    //
    // Each ImportDataTable / ImportDataView overload converts the source's
    // string-rendered cells into Row/Cell instances inserted starting at the
    // 1-based (firstFilledRow, firstFilledColumn) offset. The overloads
    // returning void match the Aspose.PDF for .NET reflection signature exactly.

    /// <summary>Import a 2-D object array into the table.</summary>
    public void ImportArray(object?[] importedArray, int firstFilledRow, int firstFilledColumn, bool isLeftColumnsFilled)
    {
        if (importedArray is null) return;
        _ = isLeftColumnsFilled;
        EnsureRowsAndColumns(firstFilledRow, firstFilledColumn + importedArray.Length);
        for (int i = 0; i < importedArray.Length; i++)
        {
            var row = Rows[firstFilledRow - 1];
            var cellIdx = firstFilledColumn - 1 + i;
            EnsureCellCount(row, cellIdx + 1);
            row.Cells[cellIdx].Text = importedArray[i]?.ToString() ?? string.Empty;
        }
    }

    /// <summary>Import all rows of a <see cref="System.Data.DataTable"/>.</summary>
    public void ImportDataTable(System.Data.DataTable importedDataTable, bool isColumnNamesImported,
        int firstFilledRow, int firstFilledColumn)
    {
        if (importedDataTable is null) return;
        var startRow = firstFilledRow;
        if (isColumnNamesImported)
        {
            var header = importedDataTable.Columns.Cast<System.Data.DataColumn>()
                .Select(c => (object)c.ColumnName).ToArray();
            ImportArray(header, startRow, firstFilledColumn, isLeftColumnsFilled: false);
            startRow++;
        }
        for (int r = 0; r < importedDataTable.Rows.Count; r++)
        {
            var values = importedDataTable.Rows[r].ItemArray;
            // Coerce DBNull to empty string so .ToString() doesn't surface "System.DBNull".
            for (int i = 0; i < values.Length; i++)
                if (values[i] is null || values[i] is System.DBNull) values[i] = string.Empty;
            ImportArray(values, startRow + r, firstFilledColumn, isLeftColumnsFilled: false);
        }
    }

    /// <summary>Import with explicit max-rows / max-columns and HTML support flag.</summary>
    public void ImportDataTable(System.Data.DataTable importedDataTable, bool isColumnNamesShown,
        int firstFilledRow, byte firstFilledColumn, int maxRows, int maxColumns, bool isHtmlSupported)
    {
        if (importedDataTable is null) return;
        _ = maxColumns; _ = isHtmlSupported;
        var startRow = firstFilledRow;
        if (isColumnNamesShown)
        {
            var header = importedDataTable.Columns.Cast<System.Data.DataColumn>()
                .Take(maxColumns > 0 ? maxColumns : int.MaxValue)
                .Select(c => (object)c.ColumnName).ToArray();
            ImportArray(header, startRow, firstFilledColumn, isLeftColumnsFilled: false);
            startRow++;
        }
        var rowCap = maxRows > 0 ? Math.Min(maxRows, importedDataTable.Rows.Count) : importedDataTable.Rows.Count;
        for (int r = 0; r < rowCap; r++)
        {
            var values = importedDataTable.Rows[r].ItemArray;
            for (int i = 0; i < values.Length; i++)
                if (values[i] is null || values[i] is System.DBNull) values[i] = string.Empty;
            ImportArray(values, startRow + r, firstFilledColumn, isLeftColumnsFilled: false);
        }
    }

    /// <summary>Import a subset of rows / columns selected by index lists.</summary>
    public void ImportDataTable(System.Data.DataTable importedDataTable,
        int[] sourceRowList, int[] sourceColumnList,
        int firstFilledRow, int firstFilledColumn,
        bool showColumnNamesAsFirstRow, bool isHtmlSupported)
    {
        if (importedDataTable is null || sourceRowList is null || sourceColumnList is null) return;
        _ = isHtmlSupported;
        var startRow = firstFilledRow;
        if (showColumnNamesAsFirstRow)
        {
            var header = sourceColumnList
                .Where(c => c >= 0 && c < importedDataTable.Columns.Count)
                .Select(c => (object)importedDataTable.Columns[c].ColumnName)
                .ToArray();
            ImportArray(header, startRow, firstFilledColumn, isLeftColumnsFilled: false);
            startRow++;
        }
        for (int r = 0; r < sourceRowList.Length; r++)
        {
            var rowIx = sourceRowList[r];
            if (rowIx < 0 || rowIx >= importedDataTable.Rows.Count) continue;
            var row = importedDataTable.Rows[rowIx];
            var values = sourceColumnList
                .Where(c => c >= 0 && c < importedDataTable.Columns.Count)
                .Select(c => (object)(row[c] is System.DBNull ? string.Empty : row[c]?.ToString() ?? string.Empty))
                .ToArray();
            ImportArray(values, startRow + r, firstFilledColumn, isLeftColumnsFilled: false);
        }
    }

    /// <summary>Import a <see cref="System.Data.DataView"/>.</summary>
    public void ImportDataView(System.Data.DataView sourceDataView, bool isColumnNamesImported,
        int firstFilledRow, int firstFilledColumn, int maxRows, int maxColumns)
    {
        if (sourceDataView is null) return;
        var startRow = firstFilledRow;
        var cols = sourceDataView.Table?.Columns.Cast<System.Data.DataColumn>().ToList()
                   ?? new System.Collections.Generic.List<System.Data.DataColumn>();
        if (maxColumns > 0 && maxColumns < cols.Count) cols = cols.GetRange(0, maxColumns);
        if (isColumnNamesImported)
        {
            var header = cols.Select(c => (object)c.ColumnName).ToArray();
            ImportArray(header, startRow, firstFilledColumn, isLeftColumnsFilled: false);
            startRow++;
        }
        var rowCap = maxRows > 0 ? Math.Min(maxRows, sourceDataView.Count) : sourceDataView.Count;
        for (int r = 0; r < rowCap; r++)
        {
            var values = cols.Select(c =>
            {
                var v = sourceDataView[r][c.ColumnName];
                return (object?)(v is System.DBNull ? string.Empty : v?.ToString() ?? string.Empty);
            }).ToArray();
            ImportArray(values, startRow + r, firstFilledColumn, isLeftColumnsFilled: false);
        }
    }

    private void EnsureRowsAndColumns(int rowCount, int colCount)
    {
        while (Rows.Count < rowCount) Rows.Add();
        _ = colCount;
    }

    private static void EnsureCellCount(Row row, int count)
    {
        while (row.Cells.Count < count) row.Cells.Add(new Cell());
    }

    /// <summary>When the table is flowed inside a page's paragraph stream this carries
    /// the page's left content margin so the table aligns with surrounding text instead
    /// of the page edge. Zero for absolutely-positioned tables.</summary>
    internal double FlowLeftOffset { get; set; }

    /// <summary>Height (points) consumed by the table's first page after the most recent
    /// <see cref="BuildMultiPage"/>; lets the caller advance a shared flow cursor.</summary>
    internal double LastRenderedHeight { get; private set; }

    /// <summary>Y cursor (page-space) just below the table on its last rendered page.
    /// Lets the flow dispatcher resume trailing paragraphs on a multi-page table's final
    /// spill page instead of opening a fresh one.</summary>
    internal double LastPageEndY { get; private set; }

    /// <summary>Per-page image blits collected during the most recent <see cref="BuildMultiPage"/>:
    /// entry <c>i</c> holds the page-space (data, rect) pairs for the i-th returned content blob.
    /// The caller applies these via <see cref="Page.AddImage(byte[], Rectangle)"/> once each page
    /// (including overflow pages) has been materialised.</summary>
    internal IReadOnlyList<List<(byte[] data, Rectangle rect)>> LastImageDraws => _pageImages;
    private readonly List<List<(byte[] data, Rectangle rect)>> _pageImages = new();

    /// <summary>Per-page content streams for inline cell graphs (legend swatches, bar
    /// graphs) drawn during the most recent build; the caller appends each via
    /// <see cref="Page.AddContentStream"/> once the page exists.</summary>
    internal IReadOnlyList<List<byte[]>> LastGraphDraws => _pageGraphs;
    private readonly List<List<byte[]>> _pageGraphs = new();

    /// <summary>
    /// Build the table across multiple pages. Returns content bytes per page.
    /// Sets <see cref="Row.IsInNewPage"/> on rows that overflow to subsequent pages.
    /// The first entry is for the given page; additional entries require new pages.
    /// Rows whose wrapped content exceeds the available page height are split at
    /// line boundaries — each chunk becomes a partial-row slice on its own page
    /// with the row's borders and background drawn around the chunk's extent.
    /// </summary>
    public List<byte[]> BuildMultiPage(Page page, double startY = 0, double bottomMargin = 36,
        double topMargin = 0)
    {
        _pageImages.Clear();
        _pageGraphs.Clear();
        var fontName = RegisterFont(page);
        var colWidths = ParseColumnWidths(GetTableUsableWidth(page));

        // Column-pagination: when RepeatingColumnsCount > 0 and the table is wider
        // than the page can fit, split it horizontally. Each "column slice" renders
        // the first N (repeating) cells alongside one contiguous chunk of the
        // remaining cells, with the chunk packed greedily to fit page width.
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

    private List<byte[]> BuildMultiPageInternal(Page page, double startY, double bottomMargin,
        double[] colWidths, int[] cellMap, string fontName, double topMargin = 0)
    {
        var pageHeight = page.Height;
        var marginLeft = Margin?.Left ?? 0;
        var marginTop = Margin?.Top ?? 0;
        var tableX = FlowLeftOffset + Left + marginLeft;
        var tableTopY = startY > 0 ? startY : pageHeight - Top - marginTop;
        // Overflow pages restart the table below the page's top margin (the flow's body
        // band), not at the bare page top — matches the generator's spill layout. The
        // table's own Margin.Top still applies when no page margin is supplied.
        var fullPageTopY = pageHeight - (topMargin > 0 ? topMargin : marginTop);
        var pageBottom = bottomMargin;

        // Pre-compute per-row content plans. Each plan carries the cells' wrapped
        // lines, uniform line height, vertical padding and the min (one-line) chunk
        // height — the paginator uses these to chop a row across pages when it
        // cannot fit in the remaining vertical space.
        var rowPlans = new List<RowPlan>(Rows.Count);
        for (var i = 0; i < Rows.Count; i++)
        {
            Rows.At(i).IsInNewPage = false;
            rowPlans.Add(BuildRowPlan(Rows.At(i), colWidths, cellMap));
        }

        // Walk rows, emit slices, spill to new pages as needed.
        var result = new List<byte[]>();
        var slices = new List<RowSlice>();
        var currentY = tableTopY;
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
            while (lineIdx < plan.LineCount || (plan.LineCount == 0 && lineIdx == 0))
            {
                var usable = currentY - pageBottom - plan.VertPadding;
                var linesFit = plan.LineCount == 0
                    ? 1
                    : (plan.LineHeight > 0 ? (int)Math.Floor(usable / plan.LineHeight) : plan.LineCount);
                // At the top of a fresh overflow page, guarantee at least one line
                // of progress so we never infinitely loop on a row that cannot fit
                // its padding + one line into the full page height.
                // currentY sits at (or just above, by the first row's TopPad) the page
                // top whenever we've just opened a fresh page; >= keeps the loop-progress
                // guard working after the TopPad seating nudges currentY above fullPageTopY.
                var atFreshPage = currentY >= fullPageTopY - 1e-3;
                if (linesFit <= 0 && atFreshPage) linesFit = Math.Max(1, plan.LineCount - lineIdx);
                // An image-bearing row is not split across a page boundary: the image is
                // blitted once at the row's top, so a partial first slice would orphan it.
                // Force the whole row onto the next page when it can't fit here (unless
                // we're already on a fresh page, where it must be placed regardless).
                if (plan.CellImages is not null && !atFreshPage && linesFit < plan.LineCount)
                    linesFit = 0;
                if (linesFit <= 0)
                {
                    // No room on current page — close it and open a new one.
                    result.Add(BuildSlicesContent(slices, colWidths, tableX, fontName, cellMap,
                        firstPageDone ? null : page));
                    firstPageDone = true;
                    slices.Clear();
                    // Seat the first row of the fresh page so its content (text/image,
                    // drawn padTop below the slice top) lands on the margin line rather
                    // than padTop below it. Only when an explicit overflow inset is in
                    // effect and no repeating header precedes the body row.
                    currentY = fullPageTopY +
                        (topMargin > 0 && (repeatCount == 0 || i < repeatCount) ? plan.TopPad : 0);
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
                                : hp.LineCount * hp.LineHeight;
                            var hSliceH = hp.LineCount == 0 || hp.IsBlankRow ? hContentH : hContentH + hp.VertPadding;
                            slices.Add(new RowSlice
                            {
                                Plan = hp,
                                LineStart = 0,
                                LineCount = hp.LineCount,
                                TopY = currentY,
                                Height = hSliceH,
                            });
                            currentY -= hSliceH;
                        }
                    }
                    Rows.At(i).IsInNewPage = lineIdx == 0 ? true : Rows.At(i).IsInNewPage;
                    continue;
                }
                var remaining = Math.Max(0, plan.LineCount - lineIdx);
                var take = plan.LineCount == 0 ? 0 : Math.Min(remaining, linesFit);
                var sliceContentH = (plan.LineCount == 0)
                    ? plan.MinBlankHeight
                    : take * plan.LineHeight;
                // Spacer rows (content-less or whitespace-only) reserve just their line with
                // no cell padding, matching the reference engine; content rows keep padding.
                var sliceH = plan.LineCount == 0 || plan.IsBlankRow
                    ? sliceContentH
                    : sliceContentH + plan.VertPadding;
                slices.Add(new RowSlice
                {
                    Plan = plan,
                    LineStart = lineIdx,
                    LineCount = take,
                    TopY = currentY,
                    Height = sliceH,
                });
                currentY -= sliceH;
                lineIdx += (plan.LineCount == 0 ? 1 : take);
                if (plan.LineCount == 0) break;
            }
        }
        if (slices.Count > 0)
            result.Add(BuildSlicesContent(slices, colWidths, tableX, fontName, cellMap,
                firstPageDone ? null : page));
        if (result.Count == 0) result.Add(Array.Empty<byte>());
        // Height consumed on the (first/only) page — meaningful for the single-page
        // case the flow dispatcher uses to advance a shared cursor; multi-page tables
        // fall back to a page break regardless.
        LastRenderedHeight = tableTopY - currentY;
        LastPageEndY = currentY;
        return result;
    }

    /// <summary>A row's layout plan: per-cell wrapped lines and vertical metrics.</summary>
    private sealed class RowPlan
    {
        public Row Row = null!;
        public List<List<CellLine>> CellLines = new();  // [cell][line]
        public double LineHeight;        // uniform per-row line height (max across cells)
        public double VertPadding;       // max (padTop + padBottom) across cells
        public int LineCount;            // max line count across cells; 0 = empty row
        public double MinBlankHeight;    // height for an empty row (FixedRowHeight/MinRowHeight)
        public double TopPad;            // max cell top padding — used to seat the first row
                                         // on an overflow page so its content reaches the margin
        public Dictionary<int, CellImage>? CellImages;  // col -> image drawn at the cell top (null when none)
        // col -> inline layout (rows of left-to-right text/graph runs) for cells that
        // mix Graph paragraphs with inline text; null when the cell has no graph.
        public Dictionary<int, List<List<InlineItem>>>? CellInline;
        public bool IsBlankRow;          // content is whitespace only — rendered tight (no padding)
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
    }

    /// <summary>A cell line: already-wrapped text with per-line font/color.</summary>
    private sealed class CellLine
    {
        public string Text = "";
        public double FontSize;
        public Color? ForegroundColor;

        /// <summary>When set, this line renders a form-control glyph (e.g. a radio
        /// button circle) ahead of <see cref="Text"/>, which holds the option caption.</summary>
        public Aspose.Pdf.Forms.RadioButtonOptionField? Option;

        /// <summary>When set, this line carries a checkbox field whose widget is placed at
        /// the laid-out cell position (its /AP appearance supplies the box and check glyph).</summary>
        public Aspose.Pdf.Forms.CheckboxField? Checkbox;

        /// <summary>Hyperlink carried by the source fragment, if any. Rendered as a
        /// link annotation over the line's text rectangle.</summary>
        public Hyperlink? Hyperlink;
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
    }

    private RowPlan BuildRowPlan(Row row, double[] colWidths, int[] cellMap)
    {
        var plan = new RowPlan { Row = row };
        var defaultPad = row.DefaultCellPadding ?? DefaultCellPadding;
        double maxLineHeight = 0;
        double maxVertPad = 0;
        double maxTopPad = 0;

        for (var col = 0; col < colWidths.Length; col++)
        {
            var origIdx = cellMap[col];
            if (origIdx >= row.Cells.Count) { plan.CellLines.Add(new List<CellLine>()); continue; }
            var cell = row.Cells.At(origIdx);
            var padding = cell.Margin ?? defaultPad;
            var padV = (padding?.Top ?? 2) + (padding?.Bottom ?? 2);
            if (padV > maxVertPad) maxVertPad = padV;
            if ((padding?.Top ?? 2) > maxTopPad) maxTopPad = padding?.Top ?? 2;

            var padLeft = padding?.Left ?? 2;
            var padRight = padding?.Right ?? 2;
            var span = Math.Max(1, Math.Min(cell.ColSpan, colWidths.Length - col));
            var cellWidth = GetCellWidth(colWidths, col, span);
            var availWidth = cellWidth - padLeft - padRight;

            var textState = cell.DefaultCellTextState ?? row.DefaultCellTextState ?? DefaultCellTextState;
            var defaultFontSize = textState?.FontSize > 0 ? textState.FontSize : 12;
            var lines = new List<CellLine>();

            var cellNeedsInline = false;
            foreach (var gp in cell.Paragraphs)
                if (gp is Aspose.Pdf.Drawing.Graph || IsInlineParagraph(gp)) { cellNeedsInline = true; break; }
            if (cellNeedsInline)
            {
                // Cells mixing Graph paragraphs with inline text (e.g. a colour-swatch
                // legend or a horizontal bar graph) get a left-to-right inline layout;
                // reserve one blank text line per inline row for height accounting.
                var inlineRows = BuildInlineCellLayout(cell, availWidth, defaultFontSize, textState, out var inlineH);
                (plan.CellInline ??= new())[col] = inlineRows;
                foreach (var _ in inlineRows) lines.Add(new CellLine { Text = "", FontSize = defaultFontSize });
                if (inlineH > maxLineHeight) maxLineHeight = inlineH;
            }
            else
            foreach (var paragraph in cell.Paragraphs)
            {
                // Nested table: flatten each inner row into one line per row so
                // height accounting and pagination see the inner content. Cell
                // text from each inner cell is joined with " | " as a visual
                // separator; proper nested-table rendering would need its own
                // slice pass, but this keeps pagination honest.
                if (paragraph is Table inner)
                {
                    // Flatten each inner row into lines, preserving block
                    // boundaries from HtmlFragment text (via \n after StripHtmlTags)
                    // so the outer cell's height budget reflects the inner table's
                    // true visual extent. Each inner row contributes at least one
                    // line per non-empty segment.
                    var innerRows = inner.Rows;
                    if (defaultFontSize * 1.2 > maxLineHeight) maxLineHeight = defaultFontSize * 1.2;
                    for (int ri = 0; ri < innerRows.Count; ri++)
                    {
                        var irow = innerRows.At(ri);
                        var segments = new List<string>();
                        for (int ici = 0; ici < irow.Cells.Count; ici++)
                        {
                            var icell = irow.Cells.At(ici);
                            foreach (var ip in icell.Paragraphs)
                            {
                                string? rawText = null;
                                if (ip is TextFragment itf) rawText = itf.Text;
                                else if (ip is HtmlFragment ihtml) rawText = HtmlFragment.StripHtmlTags(ihtml.HtmlContent ?? "");
                                if (string.IsNullOrEmpty(rawText)) continue;
                                foreach (var part in rawText.Split('\n'))
                                {
                                    var trimmed = part.Trim();
                                    if (trimmed.Length > 0) segments.Add(trimmed);
                                }
                            }
                        }
                        // Ensure each inner row renders a minimum of one blank
                        // line when it carries non-text content; otherwise the
                        // height collapses and pagination under-counts.
                        if (segments.Count == 0) segments.Add(" ");
                        foreach (var seg in segments)
                        {
                            foreach (var l in WrapText(seg, defaultFontSize, availWidth))
                                lines.Add(new CellLine { Text = l, FontSize = defaultFontSize, ForegroundColor = textState?.ForegroundColor });
                        }
                    }
                    continue;
                }

                // An Image paragraph in a cell is a variable-height block. Resolve its
                // display size (explicit Fix* or natural, fit to the cell width), reserve
                // matching vertical space as blank lines so the row's height budget and
                // pagination cover it, and stash the bytes for the render pass to blit.
                if (paragraph is Image cellImg)
                {
                    var imgBytes = ReadImageBytes(cellImg);
                    if (imgBytes is null) continue;
                    double dispW, dispH;
                    if (cellImg.FixWidth > 0 && cellImg.FixHeight > 0)
                    {
                        dispW = cellImg.FixWidth;
                        dispH = cellImg.FixHeight;
                    }
                    else if (TryGetCellImageSizePt(imgBytes, out var natW, out var natH) && natW > 0 && natH > 0)
                    {
                        if (cellImg.IsApplyResolution)
                        {
                            // Resolution-aware: fit to the cell's content width preserving the
                            // aspect ratio (IsApplyResolution behaviour — a wide
                            // image is scaled down to the column, height shrinks proportionally).
                            if (availWidth > 0 && natW > availWidth)
                            {
                                dispH = natH * (availWidth / natW);
                                dispW = availWidth;
                            }
                            else
                            {
                                dispW = natW;
                                dispH = natH;
                            }
                        }
                        else
                        {
                            // Default (no resolution applied): the width is clamped to the cell's
                            // content width while the height stays at the image's natural
                            // point-height (aspect is not preserved — a wide image is squeezed to
                            // the column and rendered at full height). Explicit Fix* sizing above
                            // is the documented way to avoid this stretch.
                            dispW = availWidth > 0 && natW > availWidth ? availWidth : natW;
                            dispH = natH;
                        }
                    }
                    else
                    {
                        dispW = availWidth > 0 ? availWidth : 100;
                        dispH = dispW;
                    }
                    (plan.CellImages ??= new Dictionary<int, CellImage>())[col] = new CellImage
                    {
                        Data = imgBytes, Width = dispW, Height = dispH, Align = cellImg.HorizontalAlignment,
                    };
                    var imgLineH = defaultFontSize * 1.2;
                    if (imgLineH > maxLineHeight) maxLineHeight = imgLineH;
                    var imgLines = Math.Max(1, (int)Math.Ceiling(dispH / imgLineH));
                    for (var k = 0; k < imgLines; k++)
                        lines.Add(new CellLine { Text = "", FontSize = defaultFontSize });
                    continue;
                }

                // A radio-button option in a cell renders as a glyph (circle) followed
                // by its caption. Emit one line carrying the option so the row's height
                // budget covers the glyph and the render pass can draw it.
                if (paragraph is Aspose.Pdf.Forms.RadioButtonOptionField opt)
                {
                    var capSize = opt.Caption?.TextState.FontSize > 0
                        ? opt.Caption!.TextState.FontSize
                        : defaultFontSize;
                    var glyphH = opt.Height > 0 ? opt.Height : capSize;
                    // A control row is sized to its glyph/caption without the extra
                    // text leading — the glyph is a fixed box, not a line of type.
                    var lh = Math.Max(glyphH, capSize);
                    if (lh > maxLineHeight) maxLineHeight = lh;
                    lines.Add(new CellLine
                    {
                        Text = opt.Caption?.Text ?? "",
                        FontSize = capSize,
                        ForegroundColor = opt.Caption?.TextState.ForegroundColor ?? textState?.ForegroundColor,
                        Option = opt,
                    });
                    continue;
                }

                // A checkbox in a cell occupies a fixed glyph box; record a control line so
                // the row height covers it and the render pass repositions its widget.
                if (paragraph is Aspose.Pdf.Forms.CheckboxField cbf)
                {
                    var boxH = cbf.Height > 0 ? cbf.Height : defaultFontSize;
                    if (boxH > maxLineHeight) maxLineHeight = boxH;
                    lines.Add(new CellLine { Text = "", FontSize = defaultFontSize, Checkbox = cbf });
                    continue;
                }

                string? text = null;
                double fragFontSize = defaultFontSize;
                Color? color = null;
                Hyperlink? fragLink = null;
                if (paragraph is TextFragment tf)
                {
                    text = tf.Text;
                    fragFontSize = ResolveFragmentFontSize(tf, defaultFontSize);
                    color = tf.TextState.ForegroundColor ?? textState?.ForegroundColor;
                    fragLink = tf.HyperlinkValue;
                }
                else if (paragraph is HtmlFragment html)
                {
                    text = HtmlFragment.StripHtmlTags(html.HtmlContent ?? "");
                    color = textState?.ForegroundColor;
                }
                if (text is null) continue;
                var thisLineHeight = fragFontSize * 1.2;
                if (thisLineHeight > maxLineHeight) maxLineHeight = thisLineHeight;

                // An empty TextFragment is a deliberate spacer in many cell
                // layouts (e.g. TextFragment with LineSpacing set and no
                // text). Emit it as one blank line so the row's height
                // budget includes the spacer — dropping it here would
                // collapse vertical padding that tests rely on.
                if (text.Length == 0)
                {
                    lines.Add(new CellLine { Text = "", FontSize = fragFontSize, ForegroundColor = color });
                    continue;
                }

                // Always wrap when text would overflow the column; IsWordWrapped=false
                // only suppresses mid-word breaks, not inter-word wrapping — otherwise
                // a cell with long text would clip horizontally. Also split on embedded
                // newlines (from HtmlFragment block-element boundaries) so each HTML
                // block starts on its own line.
                foreach (var segment in text.Split('\n'))
                {
                    if (segment.Length == 0) continue;
                    var estWidth = MeasureWidth(segment, fragFontSize);
                    if (cell.IsWordWrapped || estWidth > availWidth)
                    {
                        foreach (var l in WrapText(segment, fragFontSize, availWidth))
                            lines.Add(new CellLine { Text = l, FontSize = fragFontSize, ForegroundColor = color, Hyperlink = fragLink });
                    }
                    else
                    {
                        lines.Add(new CellLine { Text = segment, FontSize = fragFontSize, ForegroundColor = color, Hyperlink = fragLink });
                    }
                }
            }
            plan.CellLines.Add(lines);
            if (lines.Count > plan.LineCount) plan.LineCount = lines.Count;
        }
        plan.LineHeight = maxLineHeight > 0 ? maxLineHeight : 14.4;
        plan.VertPadding = maxVertPad;
        plan.TopPad = maxTopPad;
        plan.MinBlankHeight = Math.Max(row.FixedRowHeight, row.MinRowHeight);
        // A content-less row reserves a single line (no padding, see the slice loop) —
        // matching the reference engine's tight spacer rows rather than a full row.
        if (plan.MinBlankHeight <= 0) plan.MinBlankHeight = plan.LineCount == 0 ? plan.LineHeight : 20;
        // A whitespace-only row (e.g. a " " spacer) is likewise a tight spacer drawn
        // without cell padding so it reserves just its line.
        plan.IsBlankRow = plan.CellInline is null && plan.LineCount > 0
            && row.FixedRowHeight <= 0
            && System.Linq.Enumerable.All(plan.CellLines,
                cl => System.Linq.Enumerable.All(cl, l => string.IsNullOrWhiteSpace(l.Text)));
        return plan;
    }

    /// <summary>Effective font size for a cell text fragment: the fragment's own size when
    /// set, else the first segment that carries one (callers commonly set size on the
    /// TextSegment rather than the TextFragment), else the cell default.</summary>
    private static double ResolveFragmentFontSize(Aspose.Pdf.Text.TextFragment tf, double fallback)
    {
        // A TextFragment built via the parameterless ctor + Segments.Add carries a
        // default empty leading segment, so prefer the size of a segment that actually
        // has text (where callers set an explicit per-segment size) over the fragment's
        // own default state.
        if (tf.Segments is { Count: > 0 })
            foreach (var s in tf.Segments)
                if (s.TextState.FontSize > 0 && !string.IsNullOrEmpty(s.Text))
                    return s.TextState.FontSize;
        if (tf.TextState.FontSize > 0) return tf.TextState.FontSize;
        return fallback;
    }

    /// <summary>Read the (type-shadowed) IsInLineParagraph flag — TextFragment redeclares
    /// it with <c>new</c>, so a BaseParagraph-typed read would miss the value callers set.</summary>
    private static bool IsInlineParagraph(BaseParagraph p) => p switch
    {
        Aspose.Pdf.Text.TextFragment tf => tf.IsInLineParagraph,
        _ => p.IsInLineParagraph,
    };

    /// <summary>Lay a graph-bearing cell out into left-to-right inline rows, wrapping at
    /// the cell's content width. Each row is positioned <see cref="InlineItem"/>s (a text
    /// run or a Graph) with x-offsets from the cell content-left; the render pass draws the
    /// text and blits each graph's content stream at the resolved position.</summary>
    private List<List<InlineItem>> BuildInlineCellLayout(
        Cell cell, double availWidth, double defaultFontSize,
        Aspose.Pdf.Text.TextState? cellTextState, out double lineHeight)
    {
        var rows = new List<List<InlineItem>>();
        var current = new List<InlineItem>();
        double x = 0;
        var maxH = defaultFontSize * 1.2;
        var contentW = availWidth > 0 ? availWidth : double.MaxValue;

        void Flush()
        {
            if (current.Count > 0) { rows.Add(current); current = new List<InlineItem>(); }
            x = 0;
        }

        foreach (var para in cell.Paragraphs)
        {
            if (para is Aspose.Pdf.Drawing.Graph g)
            {
                if (!g.IsInLineParagraph) Flush();
                var marginL = g.Margin?.Left ?? 0;
                if (current.Count > 0 && x + marginL + g.Width > contentW) Flush();
                x += marginL;
                current.Add(new InlineItem { Graph = g, X = x, Width = g.Width, Height = g.Height });
                x += g.Width;
                if (g.Height > maxH) maxH = g.Height;
                if (!g.IsInLineParagraph) Flush();
            }
            else if (para is Aspose.Pdf.Text.TextFragment tf)
            {
                var text = tf.Text ?? string.Empty;
                var fs = ResolveFragmentFontSize(tf, defaultFontSize);
                var color = tf.TextState.ForegroundColor ?? cellTextState?.ForegroundColor;
                var marginL = tf.Margin?.Left ?? 0;
                if (!tf.IsInLineParagraph) Flush();
                var w = MeasureWidthExact(text, fs);
                if (current.Count > 0 && x + marginL + w > contentW) Flush();
                x += marginL;
                current.Add(new InlineItem { Text = text, FontSize = fs, Color = color, X = x, Width = w, Height = fs * 1.2 });
                x += w;
                if (fs * 1.2 > maxH) maxH = fs * 1.2;
                if (!tf.IsInLineParagraph) Flush();
            }
            // Other paragraph kinds inside a graph cell are not laid out inline.
        }
        Flush();
        if (rows.Count == 0) rows.Add(new List<InlineItem>());
        lineHeight = maxH;
        return rows;
    }

    /// <summary>Resolve an image's natural size in points for in-cell layout. On Windows
    /// the platform decoder is used so images without explicit density (JFIF units=0)
    /// resolve at the 96-DPI default the generator assumes; elsewhere it falls back to the
    /// header parser (which defaults such images to 72 DPI).</summary>
    private static bool TryGetCellImageSizePt(byte[] data, out double widthPt, out double heightPt)
    {
        widthPt = 0; heightPt = 0;
        if (OperatingSystem.IsWindows())
        {
            try
            {
#pragma warning disable CA1416
                using var ms = new MemoryStream(data);
                using var img = System.Drawing.Image.FromStream(ms, false, false);
                var dpiX = img.HorizontalResolution > 0 ? img.HorizontalResolution : 96;
                var dpiY = img.VerticalResolution > 0 ? img.VerticalResolution : 96;
                widthPt = img.Width * 72.0 / dpiX;
                heightPt = img.Height * 72.0 / dpiY;
                if (widthPt > 0 && heightPt > 0) return true;
#pragma warning restore CA1416
            }
            catch { /* fall through to the header parser */ }
        }
        return Document.TryGetImageNaturalSizePt(data, out widthPt, out heightPt);
    }

    /// <summary>Read an <see cref="Image"/> paragraph's bytes from its stream or file,
    /// rewinding a seekable stream so a second build pass still sees the data.</summary>
    private static byte[]? ReadImageBytes(Image img)
    {
        if (img.ImageStream is not null)
        {
            var stream = img.ImageStream;
            var pos = stream.CanSeek ? stream.Position : -1L;
            try
            {
                if (stream.CanSeek) stream.Position = 0;
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                return ms.ToArray();
            }
            finally
            {
                if (pos >= 0) stream.Position = pos;
            }
        }
        if (!string.IsNullOrEmpty(img.File) && System.IO.File.Exists(img.File))
            return System.IO.File.ReadAllBytes(img.File);
        return null;
    }

    /// <summary>Emit content for the slices that landed on the current page.</summary>
    private byte[] BuildSlicesContent(List<RowSlice> slices, double[] colWidths,
        double tableX, string fontName, int[] cellMap, Page? linkPage = null)
    {
        var builder = new ContentStreamBuilder();
        var links = linkPage is not null ? new List<(Rectangle rect, Hyperlink link)>() : null;
        var optionSink = linkPage is not null
            ? new List<(Aspose.Pdf.Forms.RadioButtonOptionField opt, Rectangle rect)>() : null;
        var checkboxSink = linkPage is not null
            ? new List<(Aspose.Pdf.Forms.CheckboxField cbf, Rectangle rect)>() : null;
        var pageImages = new List<(byte[] data, Rectangle rect)>();
        var pageGraphs = new List<byte[]>();
        builder.SaveState();
        foreach (var slice in slices)
            RenderRowSlice(builder, slice, colWidths, tableX, fontName, cellMap, links, pageImages, optionSink, pageGraphs, checkboxSink);
        _pageImages.Add(pageImages);
        _pageGraphs.Add(pageGraphs);

        // Outer table.Border wraps the slices that landed on this page.
        // Drawn after slices so it sits on top of cell backgrounds/borders.
        if (Border is not null && slices.Count > 0)
        {
            var totalWidth = 0.0;
            foreach (var w in colWidths) totalWidth += w;
            var topY = slices[0].TopY;
            var bottomY = slices[^1].TopY - slices[^1].Height;
            DrawBorder(builder, Border, tableX, bottomY, totalWidth, topY - bottomY);
        }
        builder.RestoreState();

        if (linkPage is not null && links is { Count: > 0 })
        {
            foreach (var (rect, link) in links)
            {
                if (link is WebHyperlink wh && !string.IsNullOrEmpty(wh.Url))
                    linkPage.Annotations.AddLinkAnnotation(rect, wh.Url);
                else if (link is LocalHyperlink lh && lh.TargetPageNumber > 0)
                    linkPage.Annotations.AddLinkAnnotation(rect,
                        new Aspose.Pdf.Annotations.GoToAction(
                            new Aspose.Pdf.Annotations.XYZExplicitDestination(lh.TargetPageNumber, 0, 0, 0)));
            }
        }

        // Radio-option widgets laid out in cells: place each option's widget at its
        // glyph rectangle and add it to the page /Annots so it round-trips as an
        // interactive control alongside the drawn glyph.
        if (linkPage is not null && optionSink is { Count: > 0 })
            foreach (var (opt, rect) in optionSink)
                opt.OwnerRadio?.PlaceOptionWidget(opt, linkPage, rect);

        // Checkbox widgets laid out in cells: move each widget to its glyph rectangle
        // (its /AP appearance draws the box and check at that position).
        if (linkPage is not null && checkboxSink is { Count: > 0 })
            foreach (var (cbf, rect) in checkboxSink)
                cbf.PlaceWidget(linkPage, rect);

        return builder.Build();
    }

    private void RenderRowSlice(ContentStreamBuilder builder, RowSlice slice,
        double[] colWidths, double tableX, string fontName, int[] cellMap,
        List<(Rectangle rect, Hyperlink link)>? links = null,
        List<(byte[] data, Rectangle rect)>? imageSink = null,
        List<(Aspose.Pdf.Forms.RadioButtonOptionField opt, Rectangle rect)>? optionSink = null,
        List<byte[]>? graphSink = null,
        List<(Aspose.Pdf.Forms.CheckboxField cbf, Rectangle rect)>? checkboxSink = null)
    {
        var row = slice.Plan.Row;
        var defaultPad = row.DefaultCellPadding ?? DefaultCellPadding;
        var cellX = tableX;

        for (var col = 0; col < colWidths.Length; col++)
        {
            var origIdx = cellMap[col];
            if (origIdx >= row.Cells.Count) { cellX += colWidths[col]; continue; }
            var cell = row.Cells.At(origIdx);
            // Clamp ColSpan to the slice's remaining columns so chunked rendering
            // doesn't read past the end of the slice's colWidths.
            var span = Math.Max(1, Math.Min(cell.ColSpan, colWidths.Length - col));
            var cellWidth = GetCellWidth(colWidths, col, span);
            var padding = cell.Margin ?? defaultPad;
            var padLeft = padding?.Left ?? 2;
            var padTop = padding?.Top ?? 2;

            // Record the cell's laid-out rectangle (page space) for callers that
            // query Cell.Rect/Width after save. Union across slices when a row is
            // split across pages.
            cell.Width = cellWidth;
            var sliceRect = new Rectangle(cellX, slice.TopY - slice.Height, cellX + cellWidth, slice.TopY);
            cell.Rect = cell.Rect is null
                ? sliceRect
                : new Rectangle(
                    Math.Min(cell.Rect.LLX, sliceRect.LLX), Math.Min(cell.Rect.LLY, sliceRect.LLY),
                    Math.Max(cell.Rect.URX, sliceRect.URX), Math.Max(cell.Rect.URY, sliceRect.URY));

            // Background
            var bgColor = cell.BackgroundColor ?? row.BackgroundColor;
            if (bgColor is not null)
            {
                builder.SetFillColor(bgColor);
                builder.Rectangle(cellX, slice.TopY - slice.Height, cellWidth, slice.Height);
                builder.Fill();
            }

            // Border
            if (!cell.IsNoBorder)
            {
                var cellBorder = cell.Border ?? row.DefaultCellBorder ?? row.Border ?? DefaultCellBorder;
                if (cellBorder is not null)
                    DrawBorder(builder, cellBorder, cellX, slice.TopY - slice.Height, cellWidth, slice.Height);
            }

            // Text content — render the slice's line window for this cell.
            var cellLines = col < slice.Plan.CellLines.Count ? slice.Plan.CellLines[col] : null;
            if (cellLines is { Count: > 0 } && slice.LineCount > 0)
            {
                var firstLine = slice.LineStart;
                var lastLine = Math.Min(firstLine + slice.LineCount, cellLines.Count);
                if (firstLine < lastLine)
                {
                    var hasOption = false;
                    for (var li = firstLine; li < lastLine; li++)
                        if (cellLines[li].Option is not null || cellLines[li].Checkbox is not null) { hasOption = true; break; }

                    if (hasOption)
                    {
                        // Form-control lines need path drawing (the glyph) interleaved with
                        // text, which a single text object can't hold — render line by line.
                        RenderControlLines(builder, cellLines, firstLine, lastLine,
                            cellX + padLeft, slice.TopY - padTop, slice.Plan.LineHeight, fontName, optionSink, checkboxSink);
                    }
                    else if (cell.Alignment is HorizontalAlignment.Center or HorizontalAlignment.Right)
                    {
                        // Centre / right-align each line within the cell content box.
                        // Lines can differ in width, so each is positioned absolutely.
                        var padRight = padding?.Right ?? 2;
                        for (var li = firstLine; li < lastLine; li++)
                        {
                            var line = cellLines[li];
                            if (line.Text.Length == 0) continue;
                            var w = MeasureWidth(line.Text, line.FontSize);
                            var lineX = cell.Alignment == HorizontalAlignment.Center
                                ? cellX + Math.Max(padLeft, (cellWidth - w) / 2)
                                : Math.Max(cellX + padLeft, cellX + cellWidth - padRight - w);
                            var lineTop = slice.TopY - padTop - (li - firstLine) * slice.Plan.LineHeight;
                            builder.BeginText();
                            builder.SetFont(fontName, line.FontSize);
                            ApplyColor(builder, line.ForegroundColor);
                            builder.MoveTextPosition(lineX, lineTop - line.FontSize);
                            builder.ShowText(line.Text);
                            builder.EndText();
                        }
                    }
                    else
                    {
                        var first = cellLines[firstLine];
                        var textX = cellX + padLeft;
                        var textY = slice.TopY - padTop - first.FontSize;

                        builder.BeginText();
                        builder.SetFont(fontName, first.FontSize);
                        ApplyColor(builder, first.ForegroundColor);
                        builder.MoveTextPosition(textX, textY);
                        builder.ShowText(first.Text);

                        var lastFontSize = first.FontSize;
                        for (var li = firstLine + 1; li < lastLine; li++)
                        {
                            var line = cellLines[li];
                            if (line.FontSize != lastFontSize)
                            {
                                builder.SetFont(fontName, line.FontSize);
                                lastFontSize = line.FontSize;
                            }
                            ApplyColor(builder, line.ForegroundColor);
                            builder.MoveTextPosition(0, -slice.Plan.LineHeight);
                            builder.ShowText(line.Text);
                        }
                        builder.EndText();

                        // Collect link annotations over hyperlinked lines (page-space rects).
                        if (links is not null)
                        {
                            for (var li = firstLine; li < lastLine; li++)
                            {
                                var line = cellLines[li];
                                if (line.Hyperlink is null || line.Text.Length == 0) continue;
                                var lineTop = slice.TopY - padTop - (li - firstLine) * slice.Plan.LineHeight;
                                var lineBottom = lineTop - line.FontSize;
                                var w = MeasureWidth(line.Text, line.FontSize);
                                links.Add((new Rectangle(textX, lineBottom, textX + w, lineTop), line.Hyperlink));
                            }
                        }
                    }
                }
            }

            // Image content — recorded once, at the row's top slice, for the caller to blit
            // onto the materialised page (overflow pages don't exist yet during the build).
            // The image is collected as a page-space rect; the cell border drawn above into
            // builder frames it once both content streams land on the page.
            if (imageSink is not null && slice.LineStart == 0 &&
                slice.Plan.CellImages is { } imgs && imgs.TryGetValue(col, out var ci))
            {
                var padRight = padding?.Right ?? 2;
                var imgX = cellX + padLeft;
                if (ci.Align == HorizontalAlignment.Center)
                    imgX = cellX + Math.Max(0, (cellWidth - ci.Width) / 2);
                else if (ci.Align == HorizontalAlignment.Right)
                    imgX = cellX + Math.Max(0, cellWidth - padRight - ci.Width);
                var imgTopY = slice.TopY - padTop;
                imageSink.Add((ci.Data, new Rectangle(imgX, imgTopY - ci.Height, imgX + ci.Width, imgTopY)));
            }

            // Inline graph/text content (legend swatches, bar graphs): drawn once at the
            // cell top on the first slice. Text is shown in the table stream; each graph
            // is emitted as its own page-space content stream via graphSink.
            if (slice.LineStart == 0 &&
                slice.Plan.CellInline is { } inlineMap && inlineMap.TryGetValue(col, out var inlineRows))
            {
                for (var ri = 0; ri < inlineRows.Count; ri++)
                {
                    var lineTop = slice.TopY - padTop - ri * slice.Plan.LineHeight;
                    foreach (var item in inlineRows[ri])
                    {
                        var ix = cellX + padLeft + item.X;
                        if (item.Graph is { } g)
                            graphSink?.Add(g.Build(null, ix, lineTop - slice.Plan.LineHeight));
                        else if (item.Text is { Length: > 0 } t)
                        {
                            builder.BeginText();
                            builder.SetFont(fontName, item.FontSize);
                            ApplyColor(builder, item.Color);
                            builder.MoveTextPosition(ix, lineTop - item.FontSize);
                            builder.ShowText(t);
                            builder.EndText();
                        }
                    }
                }
            }
            cellX += cellWidth;
        }
    }

    /// <summary>Render a cell's visible lines one at a time, drawing a form-control
    /// glyph (currently the radio-button circle) ahead of any option line's caption.</summary>
    private void RenderControlLines(ContentStreamBuilder builder, List<CellLine> cellLines,
        int firstLine, int lastLine, double leftX, double topY, double lineHeight, string fontName,
        List<(Aspose.Pdf.Forms.RadioButtonOptionField opt, Rectangle rect)>? optionSink = null,
        List<(Aspose.Pdf.Forms.CheckboxField cbf, Rectangle rect)>? checkboxSink = null)
    {
        for (var li = firstLine; li < lastLine; li++)
        {
            var line = cellLines[li];
            var lineTop = topY - (li - firstLine) * lineHeight;
            var textX = leftX;

            if (line.Checkbox is { } cbf)
            {
                var bw = cbf.Width > 0 ? cbf.Width : line.FontSize;
                var bh = cbf.Height > 0 ? cbf.Height : line.FontSize;
                // The widget's /AP draws the box + check glyph; just record its rectangle.
                checkboxSink?.Add((cbf, new Rectangle(leftX, lineTop - bh, leftX + bw, lineTop)));
                textX = leftX + bw + 4;
            }

            if (line.Option is { } opt)
            {
                var glyphW = opt.Width > 0 ? opt.Width : line.FontSize;
                var glyphH = opt.Height > 0 ? opt.Height : line.FontSize;
                // Centre the glyph on the line; nudge it down from the cell top.
                var cx = leftX + glyphW / 2;
                var cy = lineTop - glyphH / 2;
                var c = opt.Characteristics.Border;
                DrawEllipse(builder, cx, cy, glyphW / 2, glyphH / 2,
                    c.R / 255.0, c.G / 255.0, c.B / 255.0);
                textX = leftX + glyphW + 4;
                // The option's widget annotation is placed over the glyph so it
                // round-trips as an interactive form control at the laid-out cell
                // position (the sink owner adds it to the page /Annots).
                optionSink?.Add((opt, new Rectangle(leftX, lineTop - glyphH, leftX + glyphW, lineTop)));
            }

            if (!string.IsNullOrEmpty(line.Text))
            {
                builder.BeginText();
                builder.SetFont(fontName, line.FontSize);
                ApplyColor(builder, line.ForegroundColor);
                builder.MoveTextPosition(textX, lineTop - line.FontSize);
                builder.ShowText(line.Text);
                builder.EndText();
            }
        }
    }

    /// <summary>Stroke an axis-aligned ellipse centred at (cx, cy), approximated with
    /// four cubic Béziers.</summary>
    private static void DrawEllipse(ContentStreamBuilder builder, double cx, double cy,
        double rx, double ry, double r, double g, double b)
    {
        if (rx <= 0 || ry <= 0) return;
        const double k = 0.5522847498;
        builder.SetLineWidth(1);
        builder.SetStrokeColor(r, g, b);
        builder.MoveTo(cx + rx, cy);
        builder.CurveTo(cx + rx, cy + ry * k, cx + rx * k, cy + ry, cx, cy + ry);
        builder.CurveTo(cx - rx * k, cy + ry, cx - rx, cy + ry * k, cx - rx, cy);
        builder.CurveTo(cx - rx, cy - ry * k, cx - rx * k, cy - ry, cx, cy - ry);
        builder.CurveTo(cx + rx * k, cy - ry, cx + rx, cy - ry * k, cx + rx, cy);
        builder.ClosePath();
        builder.Stroke();
    }

    private static void ApplyColor(ContentStreamBuilder builder, Color? color)
    {
        if (color is { } c)
            builder.SetFillColor(c.R / 255.0, c.G / 255.0, c.B / 255.0);
        else
            builder.SetFillColor(0, 0, 0);
    }

    /// <summary>
    /// Build the table content stream bytes for the given page.
    /// Registers a Helvetica font in the page resources.
    /// </summary>
    public byte[] Build(Page page)
    {
        var fontName = RegisterFont(page);
        var colWidths = ParseColumnWidths(GetTableUsableWidth(page));
        var builder = new ContentStreamBuilder();
        var pageHeight = page.Height;

        // Table origin in PDF coordinates (bottom-left origin)
        var marginLeft = Margin?.Left ?? 0;
        var marginTop = Margin?.Top ?? 0;
        var tableX = Left + marginLeft;
        var tableTopY = pageHeight - Top - marginTop;

        builder.SaveState();

        // Draw table background if specified
        if (BackgroundColor is not null)
        {
            var totalWidth = 0.0;
            foreach (var w in colWidths) totalWidth += w;
            var totalHeight = CalculateTotalHeight(colWidths);

            builder.SetFillColor(BackgroundColor);
            builder.Rectangle(tableX, tableTopY - totalHeight, totalWidth, totalHeight);
            builder.Fill();
        }

        // Render rows
        var currentY = tableTopY;
        for (var rowIdx = 0; rowIdx < Rows.Count; rowIdx++)
        {
            var row = Rows.At(rowIdx);
            var rowHeight = CalculateRowHeight(row, colWidths);
            var cellX = tableX;

            for (var colIdx = 0; colIdx < row.Cells.Count && colIdx < colWidths.Length; colIdx++)
            {
                var cell = row.Cells.At(colIdx);
                var cellWidth = GetCellWidth(colWidths, colIdx, cell.ColSpan);
                var cellTopY = currentY;

                // Effective padding
                var padding = cell.Margin ?? row.DefaultCellPadding ?? DefaultCellPadding;
                var padLeft = padding?.Left ?? 2;
                var padRight = padding?.Right ?? 2;
                var padTop = padding?.Top ?? 2;
                var padBottom = padding?.Bottom ?? 2;

                // Draw cell background
                var bgColor = cell.BackgroundColor ?? row.BackgroundColor;
                if (bgColor is not null)
                {
                    builder.SetFillColor(bgColor);
                    builder.Rectangle(cellX, cellTopY - rowHeight, cellWidth, rowHeight);
                    builder.Fill();
                }

                // Draw cell border
                if (!cell.IsNoBorder)
                {
                    var cellBorder = cell.Border ?? row.DefaultCellBorder ?? row.Border ?? DefaultCellBorder;
                    if (cellBorder is not null)
                    {
                        DrawBorder(builder, cellBorder, cellX, cellTopY - rowHeight, cellWidth, rowHeight);
                    }
                }

                // Draw cell text content
                var textState = cell.DefaultCellTextState ?? row.DefaultCellTextState ?? DefaultCellTextState;
                var fontSize = textState?.FontSize ?? 12;
                var textX = cellX + padLeft;
                var textY = cellTopY - padTop - fontSize;

                foreach (var paragraph in cell.Paragraphs)
                {
                    if (paragraph is TextFragment tf)
                    {
                        var fragFontSize = tf.TextState.FontSize > 0 ? tf.TextState.FontSize : fontSize;
                        var effectiveTextY = cellTopY - padTop - fragFontSize;

                        builder.BeginText();
                        builder.SetFont(fontName, fragFontSize);

                        // Apply text color
                        if (tf.TextState.ForegroundColor is { } fg)
                            builder.SetFillColor(fg.R / 255.0, fg.G / 255.0, fg.B / 255.0);
                        else if (textState?.ForegroundColor is { } tsFg)
                            builder.SetFillColor(tsFg.R / 255.0, tsFg.G / 255.0, tsFg.B / 255.0);
                        else
                            builder.SetFillColor(0, 0, 0);

                        builder.MoveTextPosition(textX, effectiveTextY);

                        if (cell.IsWordWrapped && tf.Text.Length > 0)
                        {
                            var availWidth = cellWidth - padLeft - padRight;
                            var lines = WrapText(tf.Text, fragFontSize, availWidth);
                            for (var li = 0; li < lines.Count; li++)
                            {
                                if (li > 0)
                                {
                                    builder.MoveTextPosition(0, -fragFontSize * 1.2);
                                }
                                builder.ShowText(lines[li]);
                            }
                        }
                        else
                        {
                            builder.ShowText(tf.Text);
                        }

                        builder.EndText();
                        textY -= fragFontSize * 1.2;
                    }
                }

                cellX += cellWidth;
            }

            currentY -= rowHeight;
        }

        // Draw outer table border last so it sits on top of cell backgrounds and borders.
        if (Border is not null)
        {
            var totalWidth = 0.0;
            foreach (var w in colWidths) totalWidth += w;
            var totalHeight = CalculateTotalHeight(colWidths);

            DrawBorder(builder, Border, tableX, tableTopY - totalHeight, totalWidth, totalHeight);
        }

        builder.RestoreState();
        return builder.Build();
    }

    private double[] ParseColumnWidths(double availableWidth = 0)
    {
        if (string.IsNullOrWhiteSpace(ColumnWidths))
        {
            // Default: one column per max cells in any row, 100pt each
            var maxCols = 1;
            for (var i = 0; i < Rows.Count; i++)
            {
                var cc = Rows.At(i).Cells.Count;
                if (cc > maxCols) maxCols = cc;
            }
            var result = new double[maxCols];
            for (var i = 0; i < maxCols; i++) result[i] = 100;
            return result;
        }

        // Accept space, tab, comma, or semicolon as column-width separators —
        // callers in the wild write ColumnWidths as "100 200 100" or "100, 200, 100"
        // interchangeably. A trailing '%' makes the value a percentage of the table's
        // available width (e.g. "3% 97%"); resolved here so percentage columns fill the
        // content area instead of collapsing to the 100pt parse-failure fallback.
        var parts = ColumnWidths.Split(new[] { ' ', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        var widths = new double[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            var tok = parts[i];
            var isPercent = tok.EndsWith("%", StringComparison.Ordinal);
            var num = isPercent ? tok.Substring(0, tok.Length - 1) : tok;
            if (!double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out var w))
                w = 100;
            if (isPercent)
                w = availableWidth > 0 ? w / 100.0 * availableWidth : w;
            widths[i] = w;
        }

        // A row may carry more cells than the specified column widths; the table is padded
        // to the widest row rather than dropping the surplus cells. Append auto-width
        // columns (reusing the last specified width) so every cell is laid out.
        var neededCols = widths.Length;
        for (var i = 0; i < Rows.Count; i++)
            if (Rows.At(i).Cells.Count > neededCols) neededCols = Rows.At(i).Cells.Count;
        if (neededCols > widths.Length)
        {
            var fill = widths.Length > 0 ? widths[widths.Length - 1] : 100;
            var extended = new double[neededCols];
            Array.Copy(widths, extended, widths.Length);
            for (var i = widths.Length; i < neededCols; i++) extended[i] = fill;
            widths = extended;
        }

        // Clamp to the page's usable width: when the (fixed) column widths overflow
        // the content area, shrink them proportionally to fit — matching the
        // generator's shrink-to-window behaviour. Without this a column wider than
        // the page (e.g. ColumnWidths="600" inside a ~415pt content band) pushes the
        // cell content — notably an image fitted to the cell width — off the page's
        // right edge. Skipped when RepeatingColumnsCount > 0, where an over-wide table
        // is sliced across pages (column-pagination) rather than shrunk.
        if (RepeatingColumnsCount == 0 && availableWidth > 0)
        {
            double total = 0;
            for (var i = 0; i < widths.Length; i++) total += widths[i];
            if (total > availableWidth + 1e-3)
            {
                var scale = availableWidth / total;
                for (var i = 0; i < widths.Length; i++) widths[i] *= scale;
            }
        }
        return widths;
    }

    /// <summary>Width available to the table's columns: the page content area inset by
    /// the table's left offset (mirrored on the right). Used to resolve percentage
    /// <see cref="ColumnWidths"/>.</summary>
    private double GetTableUsableWidth(Page page)
    {
        var leftOff = FlowLeftOffset > 0 ? FlowLeftOffset : (Left + (Margin?.Left ?? 0));
        var usable = page.Width - 2 * leftOff;
        if (usable <= 0) usable = page.Width - leftOff - 36;
        return usable > 0 ? usable : page.Width;
    }

    private double GetCellWidth(double[] colWidths, int colIndex, int colSpan)
    {
        var span = Math.Max(1, colSpan);
        var width = 0.0;
        for (var i = colIndex; i < colIndex + span && i < colWidths.Length; i++)
            width += colWidths[i];
        return width;
    }

    private double CalculateRowHeight(Row row, double[] colWidths)
    {
        if (row.FixedRowHeight > 0) return row.FixedRowHeight;

        var maxHeight = row.MinRowHeight;
        var defaultPad = row.DefaultCellPadding ?? DefaultCellPadding;

        for (var colIdx = 0; colIdx < row.Cells.Count && colIdx < colWidths.Length; colIdx++)
        {
            var cell = row.Cells.At(colIdx);
            var padding = cell.Margin ?? defaultPad;
            var padTop = padding?.Top ?? 2;
            var padBottom = padding?.Bottom ?? 2;
            var padLeft = padding?.Left ?? 2;
            var padRight = padding?.Right ?? 2;

            var textState = cell.DefaultCellTextState ?? row.DefaultCellTextState ?? DefaultCellTextState;
            var fontSize = textState?.FontSize ?? 12;
            var cellWidth = GetCellWidth(colWidths, colIdx, cell.ColSpan);
            var availWidth = cellWidth - padLeft - padRight;

            var contentHeight = 0.0;
            foreach (var paragraph in cell.Paragraphs)
            {
                if (paragraph is TextFragment tf)
                {
                    var fragFontSize = tf.TextState.FontSize > 0 ? tf.TextState.FontSize : fontSize;
                    if (cell.IsWordWrapped && tf.Text.Length > 0)
                    {
                        var lines = WrapText(tf.Text, fragFontSize, availWidth);
                        contentHeight += lines.Count * fragFontSize * 1.2;
                    }
                    else
                    {
                        contentHeight += fragFontSize * 1.2;
                    }
                }
                else if (paragraph is HtmlFragment html)
                {
                    var plainText = HtmlFragment.StripHtmlTags(html.HtmlContent ?? "");
                    if (plainText.Length > 0)
                        contentHeight += fontSize * 1.2;
                }
            }

            var cellHeight = contentHeight + padTop + padBottom;
            if (cellHeight > maxHeight) maxHeight = cellHeight;
        }

        return maxHeight > 0 ? maxHeight : 20; // fallback minimum
    }

    private double CalculateTotalHeight(double[] colWidths)
    {
        var total = 0.0;
        for (var i = 0; i < Rows.Count; i++)
            total += CalculateRowHeight(Rows.At(i), colWidths);
        return total;
    }

    private void BuildRow(ContentStreamBuilder builder, Row row, double rowHeight,
        double[] colWidths, double tableX, double currentY, string fontName)
    {
        var cellX = tableX;
        for (var colIdx = 0; colIdx < row.Cells.Count && colIdx < colWidths.Length; colIdx++)
        {
            var cell = row.Cells.At(colIdx);
            var cellWidth = GetCellWidth(colWidths, colIdx, cell.ColSpan);
            var cellTopY = currentY;

            var padding = cell.Margin ?? row.DefaultCellPadding ?? DefaultCellPadding;
            var padLeft = padding?.Left ?? 2;
            var padRight = padding?.Right ?? 2;
            var padTop = padding?.Top ?? 2;

            var bgColor = cell.BackgroundColor ?? row.BackgroundColor;
            if (bgColor is not null)
            {
                builder.SetFillColor(bgColor);
                builder.Rectangle(cellX, cellTopY - rowHeight, cellWidth, rowHeight);
                builder.Fill();
            }

            if (!cell.IsNoBorder)
            {
                var cellBorder = cell.Border ?? row.DefaultCellBorder ?? row.Border ?? DefaultCellBorder;
                if (cellBorder is not null)
                    DrawBorder(builder, cellBorder, cellX, cellTopY - rowHeight, cellWidth, rowHeight);
            }

            var textState = cell.DefaultCellTextState ?? row.DefaultCellTextState ?? DefaultCellTextState;
            var fontSize = textState?.FontSize ?? 12;
            var textX = cellX + padLeft;
            var textY = cellTopY - padTop - fontSize;

            foreach (var paragraph in cell.Paragraphs)
            {
                string? cellText = null;
                double fragFontSize = fontSize;

                if (paragraph is TextFragment tf)
                {
                    cellText = tf.Text;
                    fragFontSize = tf.TextState.FontSize > 0 ? tf.TextState.FontSize : fontSize;

                    if (tf.TextState.ForegroundColor is { } fg)
                        builder.SetFillColor(fg.R / 255.0, fg.G / 255.0, fg.B / 255.0);
                    else if (textState?.ForegroundColor is { } tsFg)
                        builder.SetFillColor(tsFg.R / 255.0, tsFg.G / 255.0, tsFg.B / 255.0);
                    else
                        builder.SetFillColor(0, 0, 0);
                }
                else if (paragraph is HtmlFragment html)
                {
                    cellText = HtmlFragment.StripHtmlTags(html.HtmlContent ?? "");
                    builder.SetFillColor(0, 0, 0);
                }

                if (cellText is not null && cellText.Length > 0)
                {
                    var effectiveTextY = cellTopY - padTop - fragFontSize;

                    builder.BeginText();
                    builder.SetFont(fontName, fragFontSize);
                    builder.MoveTextPosition(textX, effectiveTextY);

                    if (cell.IsWordWrapped && cellText.Length > 0)
                    {
                        var availWidth = cellWidth - padLeft - (padding?.Right ?? 2);
                        var lines = WrapText(cellText, fragFontSize, availWidth);
                        for (var li = 0; li < lines.Count; li++)
                        {
                            if (li > 0)
                                builder.MoveTextPosition(0, -fragFontSize * 1.2);
                            builder.ShowText(lines[li]);
                        }
                    }
                    else
                    {
                        builder.ShowText(cellText);
                    }

                    builder.EndText();
                    textY -= fragFontSize * 1.2;
                }
            }

            cellX += cellWidth;
        }
    }

    private static void DrawBorder(ContentStreamBuilder builder, BorderInfo border, double x, double y, double w, double h)
    {
        builder.SetLineWidth(border.Width);
        builder.SetStrokeColor(border.Color);

        if (border.Side.HasFlag(BorderSide.Bottom) || border.Bottom is not null)
        {
            var gi = border.Bottom;
            if (gi is not null)
            {
                builder.SetLineWidth(gi.LineWidth);
                if (gi.StrokeColor is not null)
                    builder.SetStrokeColor(gi.StrokeColor.R, gi.StrokeColor.G, gi.StrokeColor.B);
            }
            builder.MoveTo(x, y).LineTo(x + w, y).Stroke();
            // Reset to default border settings
            if (gi is not null)
            {
                builder.SetLineWidth(border.Width);
                builder.SetStrokeColor(border.Color);
            }
        }

        if (border.Side.HasFlag(BorderSide.Top) || border.Top is not null)
        {
            var gi = border.Top;
            if (gi is not null)
            {
                builder.SetLineWidth(gi.LineWidth);
                if (gi.StrokeColor is not null)
                    builder.SetStrokeColor(gi.StrokeColor.R, gi.StrokeColor.G, gi.StrokeColor.B);
            }
            builder.MoveTo(x, y + h).LineTo(x + w, y + h).Stroke();
            if (gi is not null)
            {
                builder.SetLineWidth(border.Width);
                builder.SetStrokeColor(border.Color);
            }
        }

        if (border.Side.HasFlag(BorderSide.Left) || border.Left is not null)
        {
            var gi = border.Left;
            if (gi is not null)
            {
                builder.SetLineWidth(gi.LineWidth);
                if (gi.StrokeColor is not null)
                    builder.SetStrokeColor(gi.StrokeColor.R, gi.StrokeColor.G, gi.StrokeColor.B);
            }
            builder.MoveTo(x, y).LineTo(x, y + h).Stroke();
            if (gi is not null)
            {
                builder.SetLineWidth(border.Width);
                builder.SetStrokeColor(border.Color);
            }
        }

        if (border.Side.HasFlag(BorderSide.Right) || border.Right is not null)
        {
            var gi = border.Right;
            if (gi is not null)
            {
                builder.SetLineWidth(gi.LineWidth);
                if (gi.StrokeColor is not null)
                    builder.SetStrokeColor(gi.StrokeColor.R, gi.StrokeColor.G, gi.StrokeColor.B);
            }
            builder.MoveTo(x + w, y).LineTo(x + w, y + h).Stroke();
        }
    }

    /// <summary>
    /// Simple word-wrap: splits text into lines that fit within the available width.
    /// Uses an approximate character width of 0.5 * fontSize for Helvetica.
    /// </summary>
    private static List<string> WrapText(string text, double fontSize, double availWidth)
    {
        var lines = new List<string>();
        if (availWidth <= 0 || fontSize <= 0)
        {
            lines.Add(text);
            return lines;
        }

        // Measure with real Helvetica AFM widths instead of a flat 0.5 em
        // estimate — the old estimate let noticeably more characters per
        // line than GDI+ and under-counted page breaks for long cell text.
        var words = text.Split(' ');
        var spaceW = MeasureWidth(" ", fontSize);
        string currentLine = "";
        double currentWidth = 0;

        foreach (var word in words)
        {
            var wordW = MeasureWidth(word, fontSize);
            if (currentLine.Length == 0)
            {
                currentLine = word;
                currentWidth = wordW;
                continue;
            }
            var withSpaceW = currentWidth + spaceW + wordW;
            if (withSpaceW <= availWidth)
            {
                currentLine += " " + word;
                currentWidth = withSpaceW;
            }
            else
            {
                lines.Add(currentLine);
                currentLine = word;
                currentWidth = wordW;
            }
        }

        if (currentLine.Length > 0)
            lines.Add(currentLine);

        if (lines.Count == 0)
            lines.Add("");

        return lines;
    }

    /// <summary>
    /// Measure a string's width in points using Arial glyph widths (1/1000
    /// text units scaled by font size). Arial is what GDI+ defaults to for
    /// HTML/table text when no explicit font is set, and it is ~8% wider
    /// than Helvetica on mixed lowercase text. Using Arial widths here
    /// puts our wrap breakpoints in line with the reference template.
    /// Characters outside WinAnsi fall back to the font's default width.
    /// </summary>
    private static double MeasureWidth(string s, double fontSize)
    {
        var total = 0;
        foreach (var ch in s)
        {
            var code = (int)ch;
            int w;
            if (code >= 0 && code <= 255)
            {
                w = Standard14Fonts.GetWidth("Helvetica", code);
                if (w <= 0) w = Standard14Fonts.GetDefaultWidth("Helvetica");
            }
            else
            {
                w = Standard14Fonts.GetDefaultWidth("Helvetica");
            }
            total += w;
        }
        // Arial (the layout engine's default) is only marginally wider than
        // the Helvetica AFM widths used here, so apply a small ~5% inflation rather than a
        // heavy fudge — an over-large factor wraps cell text a word too early (e.g. a
        // "living facility" run that fits the column gets split across two lines).
        return total * fontSize * 1.05 / 1000.0;
    }

    /// <summary>Exact Standard-14 advance width (no wrap-inflation) for positioning inline
    /// cell runs, so a graph/text sequence lands where the reference engine places it.</summary>
    private static double MeasureWidthExact(string s, double fontSize)
    {
        var total = 0;
        foreach (var ch in s)
        {
            var code = (int)ch;
            var w = code is >= 0 and <= 255 ? Standard14Fonts.GetWidth("Helvetica", code) : 0;
            if (w <= 0) w = Standard14Fonts.GetDefaultWidth("Helvetica");
            total += w;
        }
        return total * fontSize / 1000.0;
    }

    /// <summary>
    /// Register a Helvetica font in the page resources and return the font resource name.
    /// </summary>
    internal static string RegisterFont(Page page) => RegisterFont(page, "Helvetica");

    /// <summary>Register a standard Type1 base font (e.g. "Helvetica",
    /// "Helvetica-Bold") on the page's resource dictionary, reusing an existing
    /// matching entry, and return its resource name.</summary>
    internal static string RegisterFont(Page page, string baseFont)
    {
        // Resolve indirect /Resources and /Font in place; a bare cast would miss a
        // PdfReference and replace the real dictionary with an empty one, dropping
        // the page's existing fonts and XObjects (e.g. a background image).
        var resources = page.Reader.ResolveDict(page.Dict.Get("Resources"));
        if (resources is null)
        {
            resources = new PdfDictionary();
            page.Dict.Set("Resources", resources);
        }
        var fontDict = page.Reader.ResolveDict(resources.Get("Font"));
        if (fontDict is null)
        {
            fontDict = new PdfDictionary();
            resources.Set("Font", fontDict);
        }

        // Reuse an already-registered entry for the same base font
        foreach (var key in fontDict.Keys)
        {
            if (page.Reader.Resolve(fontDict.Get(key)) is PdfDictionary existing)
            {
                var baseFontName = existing.GetName("BaseFont");
                if (baseFontName == baseFont)
                    return key;
            }
        }

        // Find a unique font name
        var name = "F1";
        var counter = 1;
        while (fontDict.ContainsKey(name))
            name = $"F{++counter}";

        var font = new PdfDictionary();
        font.Set("Type", new PdfName("Font"));
        font.Set("Subtype", new PdfName("Type1"));
        font.Set("BaseFont", new PdfName(baseFont));
        fontDict.Set(name, font);
        return name;
    }
}

/// <summary>
/// Represents a row in a table.
/// </summary>
public sealed class Row
{
    /// <summary>The cells in this row.</summary>
    public Cells Cells { get; set; } = new();

    /// <summary>Row border.</summary>
    public BorderInfo? Border { get; set; }

    /// <summary>Default cell border for cells in this row.</summary>
    public BorderInfo? DefaultCellBorder { get; set; }

    /// <summary>Fixed row height. If > 0, the row height is exactly this value.</summary>
    public double FixedRowHeight { get; set; }

    /// <summary>Minimum row height.</summary>
    public double MinRowHeight { get; set; }

    /// <summary>Row background color.</summary>
    public Color? BackgroundColor { get; set; }

    /// <summary>Default text state for cells in this row. Auto-initialized so callers can
    /// mutate properties without null-checking.</summary>
    public TextState? DefaultCellTextState { get; set; } = new TextState();

    /// <summary>Default cell padding for cells in this row.</summary>
    public MarginInfo? DefaultCellPadding { get; set; }

    /// <summary>Vertical alignment applied to each cell's content. Consumed
    /// by <see cref="Cell.VerticalAlignment"/> when the cell itself doesn't
    /// override it.</summary>
    public VerticalAlignment VerticalAlignment { get; set; } = VerticalAlignment.Top;

    /// <summary>
    /// Indicates whether this row will be rendered on a new page during multi-page table layout.
    /// Set automatically by the table layout engine during <see cref="Table.Build(Page)"/> or document save.
    /// </summary>
    public bool IsInNewPage { get; set; }

    /// <summary>
    /// Whether this row is allowed to break across pages when its cells
    /// don't fit the remaining space on the current page. When false, the
    /// row is moved entirely to the next page.
    /// Stored only; the table layout engine does not currently split rows
    /// across pages.
    /// </summary>
    public bool IsRowBroken { get; set; }

    /// <summary>Shallow copy: a new Row whose cells reference the same
    /// <see cref="Cell"/> instances and whose scalar properties carry the
    /// same values. The Cells collection itself is independent (cloning
    /// the row and adding to one Row's Cells does not affect the other).</summary>
    public object Clone()
    {
        var clone = new Row
        {
            Border = Border,
            DefaultCellBorder = DefaultCellBorder,
            FixedRowHeight = FixedRowHeight,
            MinRowHeight = MinRowHeight,
            BackgroundColor = BackgroundColor,
            DefaultCellTextState = DefaultCellTextState,
            DefaultCellPadding = DefaultCellPadding,
            VerticalAlignment = VerticalAlignment,
            IsInNewPage = IsInNewPage,
            IsRowBroken = IsRowBroken,
        };
        foreach (var cell in Cells)
            clone.Cells.Add(cell);
        return clone;
    }
}

/// <summary>
/// Represents a cell in a table row.
/// </summary>
public sealed class Cell
{
    /// <summary>Construct an empty cell with default formatting.</summary>
    public Cell() { }

    /// <summary>Construct a cell sized to <paramref name="rect"/>. The rectangle width is
    /// recorded as the cell's <see cref="Width"/>; height is currently ignored.</summary>
    public Cell(Rectangle rect)
    {
        if (rect is not null) Width = rect.Width;
    }

    /// <summary>The paragraphs (content) in this cell. Typically TextFragment instances.</summary>
    public Paragraphs Paragraphs { get; set; } = new();

    /// <summary>Cell border.</summary>
    public BorderInfo? Border { get; set; }

    /// <summary>Cell background color.</summary>
    public Color? BackgroundColor { get; set; }

    /// <summary>Cell padding (margin around content inside the cell).</summary>
    public MarginInfo? Margin { get; set; }

    /// <summary>Number of columns this cell spans. Default is 1.</summary>
    public int ColSpan { get; set; } = 1;

    /// <summary>Number of rows this cell spans. Default is 1.</summary>
    public int RowSpan { get; set; } = 1;

    /// <summary>If true, no border is drawn for this cell.</summary>
    public bool IsNoBorder { get; set; }

    /// <summary>Default text state for this cell. Auto-initialized so callers can
    /// mutate properties (e.g. HorizontalAlignment) without null-checking.</summary>
    public TextState? DefaultCellTextState { get; set; } = new TextState();

    /// <summary>Whether text in this cell should be word-wrapped.</summary>
    public bool IsWordWrapped { get; set; }

    /// <summary>Vertical alignment of the cell content.</summary>
    public VerticalAlignment VerticalAlignment { get; set; } = VerticalAlignment.Top;

    /// <summary>Horizontal alignment of the cell content.</summary>
    public HorizontalAlignment Alignment { get; set; } = HorizontalAlignment.Left;

    /// <summary>Width of the cell as laid out (set by the renderer; default 0).</summary>
    public double Width { get; internal set; }

    /// <summary>The cell's laid-out rectangle in page space, set by the renderer when
    /// the owning table is drawn (null until then). For a cell whose row is split
    /// across page slices this is the union of its slice rectangles.</summary>
    public Rectangle? Rect { get; internal set; }

    /// <summary>Background image painted behind cell content.</summary>
    public Image? BackgroundImage { get; set; }

    /// <summary>Path to a file used as the background image. Setting this assigns a
    /// new <see cref="Image"/> to <see cref="BackgroundImage"/> with the same file name.</summary>
    public string? BackgroundImageFile
    {
        get => BackgroundImage?.File;
        set
        {
            if (string.IsNullOrEmpty(value)) { BackgroundImage = null; return; }
            BackgroundImage = new Image { File = value };
        }
    }

    /// <summary>Whether the cell's formatting can be overridden by a contained fragment's
    /// text state. Stored only.</summary>
    public bool IsOverrideByFragment { get; set; }

    /// <summary>
    /// Convenience property: gets or sets the text of the first TextFragment in Paragraphs.
    /// Getting returns the text if the first paragraph is a TextFragment, otherwise empty string.
    /// Setting clears paragraphs and adds a new TextFragment with the given text.
    /// </summary>
    public string Text
    {
        get
        {
            if (Paragraphs.Count > 0 && Paragraphs[0] is TextFragment tf)
                return tf.Text;
            return string.Empty;
        }
        set
        {
            Paragraphs.Clear();
            Paragraphs.Add(new TextFragment(value));
        }
    }

    /// <summary>Create a shallow copy of this cell. The copy shares its content list
    /// with no other cell.</summary>
    public object Clone()
    {
        var copy = new Cell
        {
            Border = Border,
            BackgroundColor = BackgroundColor,
            Margin = Margin,
            ColSpan = ColSpan,
            RowSpan = RowSpan,
            IsNoBorder = IsNoBorder,
            DefaultCellTextState = DefaultCellTextState,
            IsWordWrapped = IsWordWrapped,
            VerticalAlignment = VerticalAlignment,
            Alignment = Alignment,
            Width = Width,
            BackgroundImage = BackgroundImage,
            IsOverrideByFragment = IsOverrideByFragment,
        };
        foreach (var p in Paragraphs)
            copy.Paragraphs.Add(p);
        return copy;
    }
}

/// <summary>
/// A collection of rows in a table.
/// </summary>
public sealed class Rows : IEnumerable<Row>, IDisposable
{
    private readonly List<Row> _rows = new();
    private readonly Table? _table;
    private double _accumulatedHeight;
    // Default usable page height: Letter (792) minus 72pt top/bottom margins
    private const double DefaultPageContentHeight = 648;

    /// <summary>Construct a free-standing rows collection (no parent table).
    /// Used by callers that build a Rows instance and assign it to
    /// <see cref="Table.Rows"/> later.</summary>
    public Rows() { _table = null; }

    internal Rows(Table table) { _table = table; }

    /// <summary>Number of rows.</summary>
    public int Count => _rows.Count;

    /// <summary>Add a new empty row and return it.</summary>
    public Row Add()
    {
        var row = new Row();
        _rows.Add(row);
        UpdateIsInNewPage(row);
        return row;
    }

    /// <summary>Add an existing row.</summary>
    public void Add(Row row)
    {
        _rows.Add(row);
        UpdateIsInNewPage(row);
    }

    /// <summary>Get a row by index.</summary>
    public Row At(int index) => _rows[index];

    /// <summary>Indexer with get/set for Aspose.PDF for .NET parity.</summary>
    public Row this[int index]
    {
        get => _rows[index];
        set => _rows[index] = value;
    }

    /// <summary>Index of <paramref name="row"/> in the collection, or -1.</summary>
    public int IndexOf(Row row) => _rows.IndexOf(row);

    /// <summary>Remove the first occurrence of <paramref name="row"/>.</summary>
    public void Remove(Row row) { _rows.Remove(row); }

    /// <summary>Remove the row at the given 0-based index.</summary>
    public void RemoveAt(int index) { _rows.RemoveAt(index); }

    /// <summary>Remove <paramref name="count"/> rows starting at <paramref name="index"/>.</summary>
    public void RemoveRange(int index, int count) { _rows.RemoveRange(index, count); }

    /// <summary>Releases any resources held by the collection. The FOSS
    /// implementation holds no unmanaged resources; the call clears the
    /// row list for API parity.</summary>
    public void Dispose() { _rows.Clear(); _accumulatedHeight = 0; }

    public IEnumerator<Row> GetEnumerator() => _rows.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private void UpdateIsInNewPage(Row row)
    {
        // Estimate row height: fixed height, min height, or default (font size + padding)
        var textState = row.DefaultCellTextState ?? _table?.DefaultCellTextState;
        var fontSize = textState?.FontSize ?? 12;
        var padding = row.DefaultCellPadding ?? _table?.DefaultCellPadding;
        var padV = (padding?.Top ?? 2) + (padding?.Bottom ?? 2);
        var estimatedHeight = row.FixedRowHeight > 0
            ? row.FixedRowHeight
            : Math.Max(row.MinRowHeight, fontSize + padV);

        if (_accumulatedHeight + estimatedHeight > DefaultPageContentHeight && _rows.Count > 1)
        {
            row.IsInNewPage = true;
            _accumulatedHeight = estimatedHeight; // reset for new page
        }
        else
        {
            row.IsInNewPage = false;
            _accumulatedHeight += estimatedHeight;
        }
    }
}

/// <summary>
/// A collection of cells in a row.
/// </summary>
public sealed class Cells : IEnumerable<Cell>
{
    private readonly List<Cell> _cells = new();

    /// <summary>Number of cells.</summary>
    public int Count => _cells.Count;

    /// <summary>Add a new empty cell and return it.</summary>
    public Cell Add()
    {
        var cell = new Cell();
        _cells.Add(cell);
        return cell;
    }

    /// <summary>Add a cell with the specified text content.</summary>
    public Cell Add(string text)
    {
        var cell = new Cell();
        cell.Paragraphs.Add(new TextFragment(text));
        _cells.Add(cell);
        return cell;
    }

    /// <summary>Add a cell with the specified text content and pre-applied text state.</summary>
    public Cell Add(string text, Text.TextState ts)
    {
        var cell = new Cell();
        var fragment = new TextFragment(text);
        if (ts is not null)
        {
            fragment.TextState.ApplyChangesFrom(ts);
            // Carry the text state's horizontal alignment onto the cell so the
            // renderer centres / right-aligns the content within the cell.
            cell.Alignment = ts.HorizontalAlignment;
        }
        cell.Paragraphs.Add(fragment);
        _cells.Add(cell);
        return cell;
    }

    /// <summary>Add a cell containing a TextFragment.</summary>
    public Cell Add(Text.TextFragment textFragment)
    {
        var cell = new Cell();
        cell.Paragraphs.Add(textFragment);
        _cells.Add(cell);
        return cell;
    }

    /// <summary>Add an existing cell.</summary>
    public void Add(Cell cell) => _cells.Add(cell);

    /// <summary>Indexer access to the cell at the given zero-based index.</summary>
    public Cell this[int index]
    {
        get => _cells[index];
        set => _cells[index] = value;
    }

    /// <summary>Insert <paramref name="cell"/> at the given zero-based <paramref name="index"/>.</summary>
    public void Insert(int index, Cell cell) => _cells.Insert(index, cell);

    /// <summary>Remove <paramref name="cell"/> if present.</summary>
    public void Remove(Cell cell) => _cells.Remove(cell);

    /// <summary>Remove <paramref name="obj"/> if it is a <see cref="Cell"/> that is present.</summary>
    public void Remove(object obj)
    {
        if (obj is Cell c) _cells.Remove(c);
    }

    /// <summary>Remove <paramref name="count"/> cells starting at <paramref name="index"/>.</summary>
    public void RemoveRange(int index, int count) => _cells.RemoveRange(index, count);

    /// <summary>Releases per-cell resources. Currently a no-op — cells hold no unmanaged buffers.</summary>
    public void Dispose() => _cells.Clear();

    /// <summary>Get a cell by index.</summary>
    public Cell At(int index) => _cells[index];

    public IEnumerator<Cell> GetEnumerator() => _cells.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
