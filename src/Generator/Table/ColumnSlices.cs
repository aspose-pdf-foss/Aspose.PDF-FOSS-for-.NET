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
    /// <summary>A host column grows to the natural width of a nested grid it holds —
    /// the grid's declared columns plus their own border pitch, plus the host cell's
    /// explicit padding and border pitch. Probed: a "261 117" host sizes its
    /// second column to the "28.29 60.42 28.29" logo grid's 123 pt pitched width plus
    /// its own 2 pt border = 125.</summary>
    private void WidenColumnsForNestedGrids(double[] colWidths)
    {
        if (!GeneratorCellModel) return;
        for (var ri = 0; ri < Rows.Count; ri++)
        {
            var row = Rows.At(ri);
            var col = 0;
            for (var ci = 0; ci < row.Cells.Count && col < colWidths.Length; ci++)
            {
                var cell = row.Cells.At(ci);
                if (cell.SpanContinuation) continue;
                var span = Math.Max(1, cell.ColSpan);
                if (span == 1)
                    foreach (var p in cell.Paragraphs)
                    {
                        if (p is not Table inner) continue;
                        var natural = inner.NaturalDeclaredWidth();
                        if (natural <= 0) continue;
                        var pad = cell.Margin ?? row.DefaultCellPadding ?? DefaultCellPadding;
                        var need = natural + (pad?.Left ?? 0) + (pad?.Right ?? 0) + _columnPitch;
                        if (need > colWidths[col]) colWidths[col] = need;
                    }
                col += span;
            }
        }
    }

    /// <summary>Σ of absolute declared ColumnWidths plus the cell-border pitch of every
    /// column; 0 when the widths are absent or not all absolute.</summary>
    private double NaturalDeclaredWidth()
    {
        if (string.IsNullOrWhiteSpace(ColumnWidths)) return 0;
        var parts = ColumnWidths.Split(new[] { ' ', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        var (pl, pr) = CellBorderPitch();
        double total = 0;
        foreach (var part in parts)
        {
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var w) || w <= 0)
                return 0;
            total += w + pl + pr;
        }
        return total;
    }

    /// <summary>Publish the laid-out GRID through <see cref="Row.Cells"/>: after layout a
    /// row holds one entry per grid column it covers, and every entry reports the width of
    /// ITS column. A cell spanning n columns therefore appears n times — the authored cell
    /// at its starting column, then n-1 continuation copies — and the authored cell's
    /// <see cref="Cell.Width"/> is its FIRST column's width, not the span total. Probed
    /// against the generator on a "40 90 130 70" grid: a ColSpan-2 cell at column 1 leaves
    /// Cells.Count 3 with widths 40/90/130, and the cell object itself reports 40. Columns
    /// past the last authored cell get no filler (a one-cell row stays Count 1). Purely a
    /// read-back contract: it runs once, after the page content is built.</summary>
    private void ApplyLaidOutCellGrid(double[] colWidths)
    {
        if (_measureOnly || colWidths is null || colWidths.Length == 0) return;
        for (var ri = 0; ri < Rows.Count; ri++)
        {
            var row = Rows.At(ri);
            if (row.CellGridPublished) continue;
            row.CellGridPublished = true;
            var cells = row.Cells;
            var col = 0;
            for (var i = 0; i < cells.Count && col < colWidths.Length; i++)
            {
                var cell = cells.At(i);
                // The published width is the DECLARED column, without the border pitch.
                cell.Width = colWidths[col] - _columnPitch;
                var span = Math.Max(1, cell.ColSpan);
                var k = 1;
                for (; k < span && col + k < colWidths.Length; k++)
                {
                    var cont = (Cell)cell.Clone();
                    cont.SpanContinuation = true;
                    cont.Width = colWidths[col + k] - _columnPitch;
                    cells.Insert(i + k, cont);
                }
                i += k - 1;
                col += span;
            }
        }
    }

    /// <summary>
    /// Clone this table restricted to the grid-column range [colStart, colEnd) for
    /// VerticalInSamePage band wrapping. Cells are walked by GRID position (ColSpan
    /// advances the cursor); a cell that starts inside the band keeps its content
    /// with its span clipped to the band, a cell whose span merely REACHES INTO the
    /// band contributes an empty continuation cell that keeps background and border.
    /// </summary>
    /// <summary>Set on the per-slice clone a column-paginating table builds for
    /// each of its page runs: a row whose cells are all spanning-cell
    /// continuations (no text in this slice) still sizes as ONE LINE of the
    /// row's cell font — the same height the row has in the slice that renders
    /// its text — not the generic 14.4 pt blank-row slot.</summary>
    internal bool ColumnSliceChild { get; set; }

    /// <summary>A cell border lives OUTSIDE the declared column width: with a drawn
    /// <see cref="DefaultCellBorder"/> every grid column's box is
    /// <c>declared + left + right</c> stroke widths, the strokes sit half a width
    /// INSIDE that box, and the text, fill and clip use the inner (declared) box.
    /// Probed: a 1 pt BorderSide.All default pitches "75 150 75" columns at
    /// 77/152/77 with the rules at box-left + 0.5, the text at box-left + 1. The
    /// HTML and XML dialects size their own cell boxes (see
    /// <see cref="HtmlCellBorderPt"/>) and switch this off.</summary>
    internal bool CellBorderInPitch { get; set; } = true;

    /// <summary>Set on a column-slice clone whose ColumnWidths already carry the
    /// cell-border pitch, so the clone does not add it a second time.</summary>
    internal bool ColumnPitchResolved { get; set; }

    /// <summary>Set once a nested grid's fixed row heights were scaled to its
    /// fixed-height host row (see <see cref="FitFixedRowsToHost"/>).</summary>
    internal bool NestedFixedRowsScaled { get; set; }

    /// <summary>A nested grid inside a host row with FixedRowHeight H whose rows'
    /// fixed heights sum past H scales every fixed row by H / Σ; the rows then lay
    /// out from the host's inner top and one that would cross the host's inner
    /// bottom is dropped. Probed (gt2_nested_probe2): 32.4 + 35.1 in a 27 pt host
    /// draws a 12.96 row (= 27 × 32.4 / 67.5) and no second row; 20 + 35.1 gives
    /// 9.8; rows summing under H keep their heights.</summary>
    private void FitFixedRowsToHost(double hostFixedHeight)
    {
        if (NestedFixedRowsScaled || hostFixedHeight <= 0) return;
        double sum = 0;
        for (var i = 0; i < Rows.Count; i++) sum += Math.Max(0, Rows.At(i).FixedRowHeight);
        if (sum <= hostFixedHeight + 1e-6) return;
        var scale = hostFixedHeight / sum;
        for (var i = 0; i < Rows.Count; i++)
        {
            var r = Rows.At(i);
            if (r.FixedRowHeight > 0) r.FixedRowHeight *= scale;
        }
        NestedFixedRowsScaled = true;
    }

    /// <summary>Set when this table's auto-fit columns were sized MAX-content
    /// (the column-paginating AutoFitToContent branch): each column already
    /// holds its widest unwrapped line, so cell text never wraps — the ~5 %
    /// inflated wrap estimate would otherwise split lines the generator draws
    /// whole. Copied onto the per-slice clones, whose columns are the same
    /// max-content widths made explicit.</summary>
    internal bool AutoFitMaxContentCells { get; set; }

    /// <summary>When positive, the bottom bound (in PDF Y) applied to every
    /// page AFTER the first. A table inside a fixed-height FloatingBox uses it:
    /// the first page's rows stop at the box's bottom edge where it stands,
    /// while each continuation page re-seats the box at the fresh page's
    /// content top and bounds the rows a box-Height below it.</summary>
    internal double ContinuationBottomOverride { get; set; }

    /// <summary>
    /// Clone this table restricted to the grid columns [0, repeat) followed by
    /// [colStart, colEnd) — one column-pagination slice (repeat == 0 gives a plain
    /// VerticalInSamePage band). Cells are walked by GRID position (ColSpan aware);
    /// a cell overlapping a range contributes its overlap as a cell that keeps the
    /// background/border, with the paragraphs attached only where the cell STARTS,
    /// so a spanning cell's text renders exactly once across the slices.
    /// </summary>
    private Table BuildColumnSliceTable(double[] colWidths, int repeat, int colStart, int colEnd)
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
            AutoFitMaxContentCells = AutoFitMaxContentCells,
            CellBorderInPitch = CellBorderInPitch,
            ColumnPitchResolved = true,
        };
        var widthTokens = new System.Text.StringBuilder();
        for (var c = 0; c < repeat; c++)
        {
            if (widthTokens.Length > 0) widthTokens.Append(' ');
            widthTokens.Append(colWidths[c].ToString("0.###", CultureInfo.InvariantCulture));
        }
        for (var c = colStart; c < colEnd; c++)
        {
            if (widthTokens.Length > 0) widthTokens.Append(' ');
            widthTokens.Append(colWidths[c].ToString("0.###", CultureInfo.InvariantCulture));
        }
        band.ColumnWidths = widthTokens.ToString();

        // A row's height is a property of the WHOLE row, not of one slice: a
        // cell that wraps in the narrow far columns makes the row taller in
        // EVERY slice (the two-digit report rows wrap in their 55.8 pt columns
        // and the first slice's rows grow to two lines with the text seated at
        // the row top). Measure each row against the FULL grid once and stamp
        // the height on the slice rows as a floor.
        var fullMap = new int[colWidths.Length];
        for (var i = 0; i < fullMap.Length; i++) fullMap[i] = i;

        for (var r = 0; r < Rows.Count; r++)
        {
            var row = Rows.At(r);
            double fullRowH = 0;
            try
            {
                var fullPlan = BuildRowPlan(row, colWidths, fullMap);
                if (fullPlan.LineCount > 0)
                    fullRowH = (fullPlan.LineCount - 1) * fullPlan.LineHeight
                        + fullPlan.TightLine + fullPlan.VertPadding;
            }
            catch { fullRowH = 0; }
            var bandRow = band.Rows.Add();
            bandRow.Border = row.Border;
            bandRow.DefaultCellBorder = row.DefaultCellBorder;
            bandRow.DefaultCellPadding = row.DefaultCellPadding;
            bandRow.DefaultCellTextState = row.DefaultCellTextState;
            bandRow.BackgroundColor = row.BackgroundColor;
            bandRow.FixedRowHeight = row.FixedRowHeight;
            bandRow.MinRowHeight = Math.Max(fullRowH, row.MinRowHeight);
            bandRow.VerticalAlignment = row.VerticalAlignment;

            // Two passes over the same row: the repeating prefix [0, repeat) and
            // the slice's own chunk [colStart, colEnd). A repeat-prefix cell keeps
            // its text in EVERY slice; a chunk cell only where it starts.
            for (var range = 0; range < 2; range++)
            {
                var rs = range == 0 ? 0 : colStart;
                var re = range == 0 ? repeat : colEnd;
                if (re <= rs) continue;
                var gridPos = 0;
                for (var ci = 0; ci < row.Cells.Count && gridPos < re; ci++)
                {
                    var cell = row.Cells.At(ci);
                    var span = Math.Max(1, cell.ColSpan);
                    var cellStart = gridPos;
                    var cellEnd = gridPos + span;
                    gridPos = cellEnd;
                    var isStart = range == 0
                        ? cellStart < repeat
                        : cellStart >= colStart && cellStart < colEnd;
                    var overlap = Math.Min(cellEnd, re) - Math.Max(cellStart, rs);
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
                        SpanCutLeft = cellStart < rs,
                        SpanCutRight = cellEnd > re,
                    };
                    if (isStart)
                        bandCell.Paragraphs = cell.Paragraphs;
                    bandRow.Cells.Add(bandCell);
                }
            }
        }
        return band;
    }

    /// <summary>Content height of a row containing CSS line-box cells: the tallest css
    /// cell's summed box heights, floored by the uniform grid the row's remaining
    /// (non-css) cells still need.</summary>
    /// <summary>Content height of ONE SLICE of a generator row that carries images, and
    /// the point at which a picture that will not fit is deferred. Every cell places the
    /// lines of <paramref name="take"/> it owns — a text line at its own size, an image
    /// at its full height, an image's leftover reserve lines at nothing — and the slice
    /// is as tall as the fullest cell. An image that does not fit the budget has its
    /// <see cref="CellImage.LineOffset"/> moved to the first line of the NEXT slice, so
    /// the render pass (which draws an image on the slice covering its line) carries it
    /// there whole.</summary>
    private double GeneratorImageSliceH(RowPlan plan, int lineIdx, int take, double budget,
        out bool deferred)
    {
        deferred = false;
        double tallest = 0;
        for (var col = 0; col < plan.CellLines.Count; col++)
        {
            var lines = plan.CellLines[col];
            plan.CellImages!.TryGetValue(col, out var images);
            double own = 0;
            for (var li = lineIdx; li < lineIdx + take && li < lines.Count; li++)
            {
                var cl = lines[li];
                if (cl.ImgReserve)
                {
                    // The reserve stands for a picture; only the line the picture is
                    // anchored to is charged, and only when the picture stays here.
                    if (images is null) continue;
                    foreach (var ci in images)
                    {
                        if (ci.LineOffset != li) continue;
                        var ciH = ci.BoxHeight > 0 ? ci.BoxHeight : ci.Height;
                        if (ci.FillsBand || own + ciH <= budget + 1e-3) own += ciH;
                        else { ci.LineOffset = lineIdx + take; deferred = true; }
                    }
                    continue;
                }
                own += cl.BoxH > 0 ? cl.BoxH : cl.FontSize + cl.Leading;
            }
            if (own > tallest) tallest = own;
        }
        return tallest + plan.CellPadV;
    }

    /// <summary>The full height one planned row occupies: its content stack plus its
    /// vertical padding, floored by an authored MinRowHeight.</summary>
    private double RowPlanHeight(RowPlan plan)
    {
        var h = plan.LineCount == 0
            ? plan.MinBlankHeight
            : (plan.ExactTotalH > 0 && plan.CellTables is not null
                ? plan.ExactTotalH
                : plan.CssContentH > 0
                    ? CssRowContentH(plan)
                    : (plan.LineCount - 1) * plan.LineHeight + plan.TightLine)
              + plan.VertPadding;
        return plan.Row.MinRowHeight > h ? plan.Row.MinRowHeight : h;
    }

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
        // The raw max cell padding band (padTop + padBottom + the cell rules). VertPadding
        // nets that against the uniform line grid and can come out zero; a slice priced
        // from the cells own stacks needs the band itself.
        public double CellPadV;
        public int LineCount;            // max line count across cells; 0 = empty row
        public double MinBlankHeight;    // height for an empty row (FixedRowHeight/MinRowHeight)
        public double ExactTotalH;       // exact row height when a control cell stacks text + box (0 = uniform grid)
        /// <summary>The leading a caller declared on this row's lines (0 = none). A slice
        /// that continues overleaf must not spend it: the cut has to fall below the
        /// glyphs, and the leading rides above them.</summary>
        public double Leading;
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
        public double LineFontSize;                 // the fragment's size: the line every segment seats against
        public bool Empty;                          // emit an empty show-text run at this position
        public byte[]? Ttf;                         // per-run embedded TrueType (Type0) — null = table default font
        public string? FontName;                    // base font name for the embedded run
        public bool Bold;                           // the SEGMENT's own weight
        public bool Italic;                         // the SEGMENT's own slant
        public bool Underline;                      // the SEGMENT's own underline
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

        /// <summary>Height of the BOX the picture occupies in its cell, when that is
        /// larger than the picture itself: an aspect-fitted vector keeps the full
        /// declared FixHeight of room and rides letterboxed inside it (probed on
        /// e.g. a 120×78 viewBox in a 45×45 Fix box draws 45 × 30 centred in 45).
        /// Zero means the picture IS its box.</summary>
        public double BoxHeight;

        /// <summary>The picture was sized to FILL the band left on the page (a vector
        /// source with no intrinsic size). Such a picture is not a block that can move —
        /// it is already exactly what is left — so a slice never defers it.</summary>
        public bool FillsBand;

        /// <summary>Number of text/content lines preceding this image in the cell, so the
        /// render pass seats the image on its own line below them instead of at the cell top
        /// (e.g. a title line above a centred logo).</summary>
        public int LineOffset;
    }
}
