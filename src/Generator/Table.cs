using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
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
public partial class Table : BaseParagraph
{
    /// <summary>CSS <c>line-height: normal</c> — the UA's default line box is
    /// ~1.2 em of the font size (the factor the whole HTML cell model pitches
    /// unstyled lines at).</summary>
    internal const double CssNormalLineHeight = 1.2;

    /// <summary>The browser's line box for an unstyled run: the font size in device
    /// pixels times the UA's 1.15 normal leading, ROUNDED to whole pixels, back in
    /// points — 9 pt for an 8 pt line. The rule generalises to every face.</summary>
    internal static double CssLineBoxPt(double fontPt)
        => Math.Round(fontPt * 4.0 / 3.0 * 1.15) * 0.75;

    /// <summary>The UA stylesheet's <c>td { padding: 1px }</c> in points — the white a
    /// browser keeps between a cell's border box and its content. The full-width bar
    /// path reserves the same amount explicitly.</summary>
    private const double UaCellBoxPadPt = 0.75;

    /// <summary>The generator's default cell font size (points).</summary>
    internal const double DefaultCellFontPt = 12.0;

    /// <summary>Marker for an UNCHECKED radio option riding inline in cell text —
    /// each occurrence pairs positionally with an entry of
    /// <see cref="CellLine.InlineOptions"/>. Private-use codepoints: no real
    /// document text contains them.</summary>
    internal const char InlineRadioChar = '';

    /// <summary>Marker for a CHECKED radio option riding inline in cell text
    /// (drawn with the centred dot).</summary>
    internal const char InlineRadioCheckedChar = '';

    // The inline radio control's box in an HTML form grid at the 12 pt base size
    // (values scale with the line's font size): the control
    // advances a 21 px box — 4.25 pt lead-in, a CIRCLE Ø 8.75 pt, 2.75 pt
    // trail-out — the circle's centre riding 4.89 pt above the caption baseline.
    // A checked option adds a concentric Ø 4.0 pt filled dot.
    // ⚠ Each circle paints as TWO cubic Béziers whose control
    // points overshoot the path by Ø/3 vertically, so its raw drawing bbox reads
    // 8.75 × 11.67 (= Ø × 4/3) — the RENDERED glyph is round, not oval.
    private const double InlineRadioProbeBasePt = 12.0;

    private const double InlineRadioLeadPt = 4.25;

    private const double InlineRadioGlyphDPt = 8.75;

    private const double InlineRadioTrailPt = 2.75;

    private const double InlineRadioDotDPt = 4.0;

    /// <summary>Height of the circle's centre above the caption baseline (e.g.
    /// centre 321.32 against a caption baseline at 326.2).</summary>
    private const double InlineRadioCenterRisePt = 4.89;

    /// <summary>Markers bracketing an inline PUSH BUTTON's caption in cell text
    /// (`<input type="button" value="Print">` in a form grid) — the render pass
    /// draws the 3D button chrome around the enclosed caption. Private-use
    /// codepoints: no real document text contains them.</summary>
    internal const char InlineButtonChar = '';

    internal const char InlineButtonEndChar = '';

    // The inline push button's chrome at the 12 pt base size
    // (values scale with the line's font size): a light-grey face
    // `captionW + padL + padR` wide × 15.75 tall — caption baseline 11.3 below
    // the face top, starting padL in — wrapped by a 1.0 pt black outline 2.0 pt
    // outside the face horizontally / 1.5 pt vertically, with 1.5 pt grey bevel
    // strokes inset 0.7 from the face's left/right edges and 0.75 above its
    // bottom edge (the sunken 3D look).
    private const double InlineButtonProbeBasePt = 12.0;

    private const double InlineButtonPadLPt = 6.0;

    private const double InlineButtonPadRPt = 1.35;

    private const double InlineButtonFaceHPt = 15.75;

    private const double InlineButtonBaseDropPt = 11.3;

    private const double InlineButtonOutlineOutHPt = 2.0;

    private const double InlineButtonOutlineOutVPt = 1.5;

    private const double InlineButtonBevelInsetPt = 0.7;

    private const double InlineButtonBevelWPt = 1.5;

    private const double InlineButtonBevelGray = 0.663;

    private const double InlineButtonFaceGray = 0.941;

    /// <summary>Advance the chrome adds around a button caption at the base size:
    /// face pads + the outline's outset on both sides.</summary>
    internal const double InlineButtonChromePt =
        InlineButtonPadLPt + InlineButtonPadRPt + 2 * InlineButtonOutlineOutHPt;

    /// <summary>Fallback row line height: the default 12 pt cell font at the
    /// normal 1.2 line box (12 × 1.2 = 14.4).</summary>
    internal const double DefaultLineHeightPt = DefaultCellFontPt * CssNormalLineHeight;

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
    /// is empty. Stored as a string (e.g. <c>"100"</c>) per the public API shape.</summary>
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
            if (double.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out var w)
                || double.TryParse(tok.Replace(',', '.'), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out w))
                total += w;
        return total;
    }

    /// <summary>Approximate rendered height of the table, independent of any
    /// hosting page. Read-only: querying the height never mutates cell
    /// formatting (font size, style) of the rows it inspects.</summary>
    public double GetHeight() => GetHeight(null);

    /// <summary>Rendered height of the table on <paramref name="parentPage"/>
    /// (or on a default content band when none is given). Pure measurement,
    /// independent of layout state (same value before and after pagination):
    /// <c>height = table margin top+bottom + table border top+bottom (once) +
    /// Σ rowHeight</c>; <c>rowHeight = max over cells of (padTop + padBottom +
    /// Σ fragments (marginTop + marginBottom + lineCount·fontSize))</c>. The
    /// line height is the font size EXACTLY (no leading); a cell's Margin
    /// REPLACES the default padding for this contract; fragment margins are
    /// summed, never collapsed; explicit column widths are used as declared
    /// (no shrink-to-band). Read-only: never mutates cell formatting.</summary>
    public double GetHeight(Page? parentPage)
    {
        // Resolve columns against the hosting page's usable width, or a default
        // A4 content band, so percentage / auto widths don't collapse and force
        // spurious wraps in the line-count estimate.
        var availWidth = parentPage is not null ? GetTableUsableWidth(parentPage) : 523.0;
        var colWidths = ParseColumnWidths(availWidth, clampToAvailable: false);

        double total = (Margin?.Top ?? 0) + (Margin?.Bottom ?? 0) + BorderTopBottom(Border);
        for (var ri = 0; ri < Rows.Count; ri++)
        {
            var row = Rows.At(ri);
            if (row.FixedRowHeight > 0) { total += row.FixedRowHeight; continue; }
            var rowH = (double)row.MinRowHeight;
            for (var ci = 0; ci < row.Cells.Count && ci < colWidths.Length; ci++)
            {
                var cell = row.Cells.At(ci);
                var h = MeasureCellHeight(cell, row, GetCellWidth(colWidths, ci, cell.ColSpan));
                if (h > rowH) rowH = h;
            }
            total += rowH;
        }
        return total;
    }

    /// <summary>Effective cell padding for RENDER layout: the cell's Margin
    /// merged component-wise with the row/table default padding — a ZERO margin
    /// component is "unset" and falls back to the default's component (a
    /// Margin(-25,0,0,0) cell aligns with its (0,5,0,5)-padded neighbours
    /// while a Margin(0,8,0,3) cell keeps its explicit 3 pt top;
    /// non-zero components win, including negative ones). The measurement
    /// contract (<see cref="GetHeight(Page?)"/>) is different: there a set
    /// Margin REPLACES the default padding wholesale.</summary>
    private MarginInfo? EffectivePad(Cell cell, Row row)
    {
        var def = row.DefaultCellPadding ?? DefaultCellPadding;
        var m = cell.Margin;
        if (m is null) return def;
        if (def is null) return m;
        // A fully-shaped margin (both horizontal components set) REPLACES the
        // default padding wholesale — its explicit zero top/bottom stay zero
        // (a (12.75,0,12.75,0) checkbox cell gets no 2+2 vertical padding).
        // A partial margin merges component-wise: zero components fall back
        // to the default's (a Margin(-25,0,0,0) cell aligns with its
        // (0,5,0,5)-padded neighbours).
        if (m.Left != 0 && m.Right != 0) return m;
        return new MarginInfo(
            m.Left != 0 ? m.Left : def.Left,
            m.Bottom != 0 ? m.Bottom : def.Bottom,
            m.Right != 0 ? m.Right : def.Right,
            m.Top != 0 ? m.Top : def.Top);
    }

    /// <summary>Measured height of one cell for <see cref="GetHeight(Page?)"/>:
    /// vertical padding plus each paragraph's top/bottom margin and its line
    /// count at the paragraph's font size. Wrapping preserves every space at
    /// full advance width (leading indents eat line width) and char-splits
    /// words wider than the column; the wrap width is the column width minus
    /// left/right padding only (borders don't inset it).</summary>
    private double MeasureCellHeight(Cell cell, Row row, double cellWidth)
    {
        var pad = cell.Margin ?? row.DefaultCellPadding ?? DefaultCellPadding;
        var availW = cellWidth - (pad?.Left ?? 0) - (pad?.Right ?? 0);
        var cellFs = ResolveCellFontSize(cell, row);
        var h = (pad?.Top ?? 0) + (pad?.Bottom ?? 0);
        foreach (var p in cell.Paragraphs)
        {
            if (p is Text.TextFragment tf)
            {
                var fs = MeasureFragmentFontSize(tf, cellFs);
                h += (tf.Margin?.Top ?? 0) + (tf.Margin?.Bottom ?? 0);
                foreach (var logical in (tf.Text ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
                    h += CountMeasuredLines(logical, fs, availW) * fs;
            }
            else if (p is HtmlFragment) h += cellFs;
        }
        return h;
    }

    /// <summary>Line height used by the measurement contract: the LARGEST
    /// explicitly-sized non-empty segment wins over the fragment's own state
    /// (a mixed 8/12 pt line measures 12).</summary>
    private static double MeasureFragmentFontSize(Aspose.Pdf.Text.TextFragment tf, double fallback)
    {
        double max = 0;
        if (tf.Segments is { Count: > 0 })
            foreach (var s in tf.Segments)
                if (s.TextState.FontSizeTouched && !string.IsNullOrEmpty(s.Text) && s.TextState.FontSize > max)
                    max = s.TextState.FontSize;
        if (max > 0) return max;
        if (tf.TextState.FontSizeTouched) return tf.TextState.FontSize;
        return fallback;
    }

    /// <summary>Number of lines one logical (hard-break-free) line occupies at
    /// the given wrap width: greedy fill over alternating space/word runs, all
    /// spaces measured at full advance (leading AND trailing), a run wider
    /// than the width split at character level.</summary>
    private static int CountMeasuredLines(string text, double fontSize, double availWidth)
    {
        if (availWidth <= 0 || fontSize <= 0 || text.Length == 0) return 1;
        if (MeasureWidthExact(text, fontSize) <= availWidth) return 1;
        var lines = 1;
        double cur = 0;
        var start = 0;
        while (start < text.Length)
        {
            var isSpace = text[start] == ' ';
            var end = start;
            while (end < text.Length && (text[end] == ' ') == isSpace) end++;
            var token = text.Substring(start, end - start);
            var tw = MeasureWidthExact(token, fontSize);
            if (cur + tw <= availWidth)
                cur += tw;
            else if (tw <= availWidth)
            {
                lines++;
                cur = tw;
            }
            else
            {
                foreach (var ch in token)
                {
                    var chW = MeasureWidthExact(ch.ToString(), fontSize);
                    if (cur > 0 && cur + chW > availWidth) { lines++; cur = 0; }
                    cur += chW;
                }
            }
            start = end;
        }
        return lines;
    }

    /// <summary>Top+bottom thickness a border contributes to a row's height:
    /// the border <see cref="BorderInfo.Width"/> for each of the top/bottom
    /// sides it draws (0 for <see cref="BorderSide.None"/>).</summary>
    private static double BorderTopBottom(BorderInfo? b)
    {
        if (b is null) return 0;
        double v = 0;
        if ((b.Side & BorderSide.Top) != 0 || b.TopAssigned)
            v += b.RawTop?.LineWidth > 0 ? b.RawTop.LineWidth : b.Width;
        if ((b.Side & BorderSide.Bottom) != 0 || b.BottomAssigned)
            v += b.RawBottom?.LineWidth > 0 ? b.RawBottom.LineWidth : b.Width;
        return v;
    }

    /// <summary>Apply <paramref name="textState"/> to every cell in the
    /// given (0-based) column number. Properties a cell's own TextState
    /// already sets independently are not overwritten.</summary>
    public void SetColumnTextState(int colNumber, Aspose.Pdf.Text.TextState textState)
    {
        if (textState is null) return;
        foreach (var row in Rows)
        {
            if (colNumber < 0 || colNumber >= row.Cells.Count) continue;
            var cell = row.Cells[colNumber];
            if (cell.DefaultCellTextState is not { } st)
            {
                cell.DefaultCellTextState = textState;
                continue;
            }
            st.ForegroundColor ??= textState.ForegroundColor;
            if (st.FontSize <= 0 && textState.FontSize > 0) st.FontSize = textState.FontSize;
        }
    }

    /// <summary>Shallow clone of the table. The Rows collection is reused
    /// (the contents are not deep-copied) which matches the
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
    // returning void keep the published reflection signature exactly.

    /// <summary>Import a one-dimensional object array into the table, wrapping the
    /// values into rows by the table's column count. Filling starts at the 1-based
    /// (firstFilledRow, firstFilledColumn) offset and continues on the next row at
    /// column 1 once a row is full — so a long array spans many rows (and paginates
    /// when the table is broken) rather than a single very wide row.</summary>
    public void ImportArray(object?[] importedArray, int firstFilledRow, int firstFilledColumn, bool isLeftColumnsFilled)
    {
        if (importedArray is null) return;
        _ = isLeftColumnsFilled;

        // Column count drives the wrap. When the table declares no columns yet,
        // fall back to a single row (the whole array) so callers that rely on a
        // column-less table keep the prior one-row behaviour.
        var columnCount = ResolveImportColumnCount();
        if (columnCount <= 0) columnCount = importedArray.Length;
        if (columnCount <= 0) return;

        var row = Math.Max(1, firstFilledRow);
        var col = Math.Min(Math.Max(1, firstFilledColumn), columnCount);
        foreach (var value in importedArray)
        {
            EnsureRowsAndColumns(row, columnCount);
            var r = Rows[row - 1];
            EnsureCellCount(r, col);
            r.Cells[col - 1].Text = value?.ToString() ?? string.Empty;
            if (++col > columnCount) { col = 1; row++; }
        }
    }

    /// <summary>The table's column count used when wrapping an imported array:
    /// the number of declared <see cref="ColumnWidths"/>, else the widest existing
    /// row, else zero (no columns declared yet).</summary>
    private int ResolveImportColumnCount()
    {
        if (!string.IsNullOrEmpty(ColumnWidths))
        {
            var n = ColumnWidths.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
            if (n > 0) return n;
        }
        var max = 0;
        foreach (var r in Rows)
            if (r.Cells.Count > max) max = r.Cells.Count;
        return max;
    }

    /// <summary>Place a sequence of values into a single table row starting at the
    /// 1-based (firstFilledRow, firstFilledColumn) offset — the per-row fill used by
    /// the DataTable / DataView importers, which already iterate row by row and so
    /// must not wrap.</summary>
    private void FillSingleRow(object?[] values, int firstFilledRow, int firstFilledColumn)
    {
        // Both 0 and 1 are accepted as "first position"; positions are 1-based here.
        if (firstFilledRow < 1) firstFilledRow = 1;
        if (firstFilledColumn < 1) firstFilledColumn = 1;
        EnsureRowsAndColumns(firstFilledRow, firstFilledColumn + values.Length);
        var row = Rows[firstFilledRow - 1];
        for (int i = 0; i < values.Length; i++)
        {
            var cellIdx = firstFilledColumn - 1 + i;
            EnsureCellCount(row, cellIdx + 1);
            row.Cells[cellIdx].Text = values[i]?.ToString() ?? string.Empty;
        }
    }

    /// <summary>Import all rows of a <see cref="System.Data.DataTable"/>.</summary>
    public void ImportDataTable(System.Data.DataTable importedDataTable, bool isColumnNamesImported,
        int firstFilledRow, int firstFilledColumn)
    {
        if (importedDataTable is null) return;
        var startRow = firstFilledRow < 1 ? 1 : firstFilledRow;
        if (isColumnNamesImported)
        {
            var header = importedDataTable.Columns.Cast<System.Data.DataColumn>()
                .Select(c => (object)c.ColumnName).ToArray();
            FillSingleRow(header, startRow, firstFilledColumn);
            startRow++;
        }
        for (int r = 0; r < importedDataTable.Rows.Count; r++)
        {
            var values = importedDataTable.Rows[r].ItemArray;
            // Coerce DBNull to empty string so .ToString() doesn't surface "System.DBNull".
            for (int i = 0; i < values.Length; i++)
                if (values[i] is null || values[i] is System.DBNull) values[i] = string.Empty;
            FillSingleRow(values, startRow + r, firstFilledColumn);
        }
    }

    /// <summary>Import with explicit max-rows / max-columns and HTML support flag.</summary>
    public void ImportDataTable(System.Data.DataTable importedDataTable, bool isColumnNamesShown,
        int firstFilledRow, byte firstFilledColumn, int maxRows, int maxColumns, bool isHtmlSupported)
    {
        if (importedDataTable is null) return;
        _ = maxColumns; _ = isHtmlSupported;
        var startRow = firstFilledRow < 1 ? 1 : firstFilledRow;
        if (isColumnNamesShown)
        {
            var header = importedDataTable.Columns.Cast<System.Data.DataColumn>()
                .Take(maxColumns > 0 ? maxColumns : int.MaxValue)
                .Select(c => (object)c.ColumnName).ToArray();
            FillSingleRow(header, startRow, firstFilledColumn);
            startRow++;
        }
        var rowCap = maxRows > 0 ? Math.Min(maxRows, importedDataTable.Rows.Count) : importedDataTable.Rows.Count;
        for (int r = 0; r < rowCap; r++)
        {
            var values = importedDataTable.Rows[r].ItemArray;
            for (int i = 0; i < values.Length; i++)
                if (values[i] is null || values[i] is System.DBNull) values[i] = string.Empty;
            FillSingleRow(values, startRow + r, firstFilledColumn);
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
        var startRow = firstFilledRow < 1 ? 1 : firstFilledRow;
        if (showColumnNamesAsFirstRow)
        {
            var header = sourceColumnList
                .Where(c => c >= 0 && c < importedDataTable.Columns.Count)
                .Select(c => (object)importedDataTable.Columns[c].ColumnName)
                .ToArray();
            FillSingleRow(header, startRow, firstFilledColumn);
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
            FillSingleRow(values, startRow + r, firstFilledColumn);
        }
    }

    /// <summary>Import a <see cref="System.Data.DataView"/>.</summary>
    public void ImportDataView(System.Data.DataView sourceDataView, bool isColumnNamesImported,
        int firstFilledRow, int firstFilledColumn, int maxRows, int maxColumns)
    {
        if (sourceDataView is null) return;
        var startRow = firstFilledRow < 1 ? 1 : firstFilledRow;
        var cols = sourceDataView.Table?.Columns.Cast<System.Data.DataColumn>().ToList()
                   ?? new System.Collections.Generic.List<System.Data.DataColumn>();
        if (maxColumns > 0 && maxColumns < cols.Count) cols = cols.GetRange(0, maxColumns);
        if (isColumnNamesImported)
        {
            var header = cols.Select(c => (object)c.ColumnName).ToArray();
            FillSingleRow(header, startRow, firstFilledColumn);
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
            FillSingleRow(values, startRow + r, firstFilledColumn);
        }
    }

    private void EnsureRowsAndColumns(int rowCount, int colCount)
    {
        while (Rows.Count < rowCount) Rows.Add();
        // Keep the grid rectangular: every row carries at least the declared column
        // count (from ColumnWidths) or the width this fill needs, whichever is larger —
        // the importers leave the un-imported trailing columns as empty cells.
        var cols = Math.Max(colCount, ResolveImportColumnCount());
        if (cols > 0)
            foreach (var r in Rows)
                EnsureCellCount(r, cols);
    }

    private static void EnsureCellCount(Row row, int count)
    {
        while (row.Cells.Count < count) row.Cells.Add(new Cell());
    }

    /// <summary>When the table is flowed inside a page's paragraph stream this carries
    /// the page's left content margin so the table aligns with surrounding text instead
    /// of the page edge. Zero for absolutely-positioned tables.</summary>
    internal double FlowLeftOffset { get; set; }

    /// <summary>HTML-engine text metrics for this table's plain cells (set on the inner
    /// tables of an unwrapped container table): line pitch = font size (no 1.2 leading),
    /// baselines lifted by the Helvetica descender, and default vertical CENTERING of a
    /// cell's block within its row — the layout model for generator tables that
    /// flow out of the HTML/nested-table engine.</summary>
    internal bool HtmlEngineMetrics { get; set; }

    /// <summary>Lay the grid out on the browser's own cell box: a row's declared
    /// minimum is a CONTENT floor that the cell padding still sits outside of.</summary>
    internal bool UaCellBoxes { get; set; }

    /// <summary>The markup declared no width of its own, so the table sizes to its
    /// content rather than filling what it is offered.</summary>
    internal bool HtmlAutoWidth { get; set; }

    /// <summary>Width of the rule the markup boxes each cell with, in points; every
    /// cell grows by it on both sides. 0 when the grid draws no boxes.</summary>
    internal double HtmlCellBorderPt { get; set; }

    /// <summary>The rule came from a style rule on the cells rather than the table's own
    /// BORDER attribute: neighbours share one rule, so a column advances by a single rule
    /// even though each cell still keeps both of its own out of its text box.</summary>
    internal bool HtmlCellBorderShared { get; set; }

    /// <summary>The HTML layout pass sized this table's columns itself, so a cell wraps
    /// inside the same box that pass measured: the column less its padding AND the rule
    /// it is boxed with, against the same Standard-14 advances (no wrap inflation).</summary>
    internal bool HtmlLayoutWrap { get; set; }

    /// <summary>Footer-band tables drop text a full em from the line top (no
    /// descender lift) — the footer caption baseline sits at
    /// lineTop − fontSize exactly (60.6 for a 14 pt
    /// caption under a 74.6 line top).</summary>
    internal bool SuppressBaselineLift { get; set; }

    /// <summary>Cells whose fragments resolved a serif font draw in the embedded serif
    /// face, wrapped with its real kerned metrics (set on tables inside a float-column
    /// band document): the column widths were measured with those metrics, so the wider
    /// Standard-14 Helvetica render path over-wraps and overgrows the band's rows.</summary>
    internal bool HonorCellFontFaces { get; set; }

    /// <summary>Cells whose fragments resolved ANY real installed face wrap AND draw with
    /// that face's kerned hmtx metrics via the Type0 path (set on form-document dialect
    /// tables, whose `td {font: 10px Verdana}` cells lay out in the real
    /// face): the Standard-14 Helvetica estimate is narrower than faces like Verdana, so
    /// it under-wraps the lines the real face wraps.</summary>
    internal bool HonorCellTtfFaces { get; set; }

    /// <summary>CSS run boxes: every cell line stacks on the <c>line-height</c> its own
    /// fragment carries, not the flat 1.2 em the mixed-size path assumes and not the
    /// row's uniform pitch. A grid mixing a 24 pt run with 10 pt ones needs this — the
    /// uniform grid would stretch the 10 pt columns to the 24 pt column's pitch.</summary>
    internal bool CssRunBoxes { get; set; }

    /// <summary>Separate-borders <c>cellspacing</c> in points: the gap a browser leaves
    /// above the first row's cell boxes and between every pair after them. Zero for the
    /// collapsed grids the generator draws by default.</summary>
    internal double RowSpacingPt { get; set; }

    /// <summary>U+200B ZERO WIDTH SPACE — a soft wrap opportunity that must never reach
    /// the page: a Standard-14 face has no glyph for it and draws a hollow box.</summary>
    private const char ZeroWidthSpace = '​';

    /// <summary>Drop every zero-width space from a line about to be drawn.</summary>
    private static string StripZeroWidth(string s)
        => s.IndexOf(ZeroWidthSpace) < 0 ? s : s.Replace(ZeroWidthSpace.ToString(), "");

    /// <summary>Baseline offset below a CSS run box's top: half the leading the box adds
    /// over the glyph box, plus the face's ascent.</summary>
    private static double CssRunBaseOff(double boxH, double fontSize, double asc, double desc)
        => asc > 0 ? (boxH - fontSize * (asc + desc)) / 2 + fontSize * asc : fontSize;

    /// <summary>First space-class character (U+0020 or U+00A0) of the table's text in
    /// document order, '\0' when unset. Space and no-break space draw the same glyph, so
    /// the first of the two to occur decides how EVERY later one reads back out of the
    /// page: a document that opens with an &amp;nbsp; cell reports U+00A0 between all its
    /// words, one that opens with plain text reports plain spaces even for &amp;nbsp;
    /// entities. Applied to the laid-out cell lines at draw time (layout is already
    /// final, and both characters carry the space width in every Standard-14 table).</summary>
    internal char HtmlSpaceClassFirst { get; set; }

    /// <summary>Height (points) consumed by the table's first page after the most recent
    /// <see cref="BuildMultiPage"/>; lets the caller advance a shared flow cursor.</summary>
    internal double LastRenderedHeight { get; private set; }

    /// <summary>The HTML builder's PREFERRED (max-content) width for this table —
    /// distinct from the reported natural (a percent grid's natural is its MIN
    /// floors, but the cell that holds it sizes against what the grid would like).</summary>
    internal double HtmlPreferredWidthPt { get; set; }

    /// <summary>HTML cellspacing (points): each cell owns its border box, inset
    /// half a spacing from the row band, so adjacent rows show the page between
    /// their borders instead of sharing one line.</summary>
    internal double HtmlRowSpacingPt { get; set; }

    /// <summary>Per-column min-content floors (points), set by the lifted HTML
    /// builder so <see cref="ParseColumnWidths"/> can resolve the auto column
    /// rule at DRAW time against the real box (unknown while building).</summary>
    internal double[]? HtmlColMinPt { get; set; }

    /// <summary>Per-column max-content widths (points) — the fit test's other
    /// half; see <see cref="HtmlColMinPt"/>.</summary>
    internal double[]? HtmlColMaxPt { get; set; }

    /// <summary>True when the columns' percent shares were DECLARED in the markup
    /// (`width: 13%` cells) and act as real box-filling targets; false for the
    /// synthetic shares the builder emits for undeclared grids, which only carry
    /// proportions and must not inflate columns past their floors.</summary>
    internal bool HtmlColPctDeclared { get; set; }

    /// <summary>Which columns actually carried a declared percent (the rest hold the
    /// even leftover share the builder synthesises). CSS auto layout hands the box's
    /// surplus to the DECLARED column and lets the auto ones hug their content — the
    /// risks pill's `.CategoryName { width: 80% }` absorbs the slack so the detail
    /// button stays at its min-content width.</summary>
    internal bool[]? HtmlColPctDeclaredCols { get; set; }

    /// <summary>Columns whose width was DECLARED as an absolute length (`&lt;td
    /// width="15"&gt;`, the layout-table spacer idiom). CSS auto layout treats those as
    /// FIXED: the box's surplus goes to the auto columns and a fixed column stays at
    /// its declared width. Without this a 15 px spacer took a max-content share of the
    /// leftover and shifted everything to its right.</summary>
    internal bool[]? HtmlColFixedCols { get; set; }

    /// <summary>Column that absorbs ALL surplus width (−1 = distribute
    /// proportionally): the column holding a nested grid, which stretches to
    /// fill whatever it gets — its siblings (the title plates) hug their
    /// content at the left.</summary>
    internal int HtmlSurplusCol { get; set; } = -1;

    /// <summary>Rounded capsule painted BEHIND this whole table (the risks grid's
    /// grey pill group: a border-radius div wrapping the nested table).</summary>
    internal Color? HtmlCapsuleFill { get; set; }

    internal double HtmlCapsuleRadiusPt { get; set; }

    internal double HtmlCapsulePadHPt { get; set; }

    internal double HtmlCapsulePadVPt { get; set; }

    /// <summary>Declared <c>border-spacing</c> (points) carried as half a band on each
    /// cell's four sides. The grid's OUTER edge owes a full band, so the capsule behind
    /// it adds the missing half.</summary>
    internal double HtmlCellSpacingBandPt { get; set; }

    /// <summary>The capsule div's own <c>margin</c> (points) — white space OUTSIDE the
    /// painted pill, insetting it from the host cell's content box.</summary>
    internal double HtmlCapsuleMarginPt { get; set; }

    /// <summary>How far this grid sits inside the box the host cell reserves for it:
    /// the capsule's padding, the half spacing band the capsule makes up, and the
    /// capsule div's margin. Zero when nothing wraps the grid.</summary>
    /// <summary>The lifted grid's own CSS `margin-top` (pt): reserved above the grid
    /// in its host cell and the grid drawn one margin down — the one-sided twin of
    /// the capsule outset below.</summary>
    internal double HtmlMarginTopPt;

    /// <summary>Standing list indent (pt) at the grid's position in its host cell's
    /// markup: a table inside an <c>&lt;li&gt;</c> anchors at the item's text indent
    /// and its box resolves against the indented width, exactly like the item's
    /// own text lines.</summary>
    internal double HtmlListIndentPt;

    /// <summary>The Verdana form-grid fragment dialect's cell conventions (see
    /// Document.cs): short cells centre in their rows and Arabic runs fall back to
    /// the serif face, wrapped at the cell box. Distinct from
    /// <see cref="HonorCellTtfFaces"/>, which OTHER calibrated dialects also set.</summary>
    internal bool FormGridCells;

    internal double HtmlCapsuleOutsetVPt => HtmlCapsuleFill is null ? 0
        : HtmlCapsulePadVPt + HtmlCellSpacingBandPt / 2 + HtmlCapsuleMarginPt;

    internal double HtmlCapsuleOutsetHPt => HtmlCapsuleFill is null ? 0
        : HtmlCapsulePadHPt + HtmlCellSpacingBandPt / 2 + HtmlCapsuleMarginPt;

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

    /// <summary>True while a measure-only <see cref="BuildMultiPage"/> pre-flight runs:
    /// content is laid out and measured but no annotations/widgets touch the page.</summary>
    private bool _measureOnly;

    /// <summary>Render a cell's nested <see cref="Table"/> paragraphs as REAL grids in
    /// place (measured in the row plan, drawn into the cell box) instead of the legacy
    /// one-line-per-row flatten. Opt-in: the lifted HTML path sets it on every level.</summary>
    internal bool NestedTableRender;

    /// <summary>This grid actually holds a lifted nested table.</summary>
    internal bool HtmlLiftedGrid;

    /// <summary>A stylesheet rule styles this grid's CELLS (the chain dialect matched
    /// declarations for a td). Such a grid is laid out the browser's way throughout —
    /// including its line box — where a table the stylesheet never addresses keeps the
    /// calibrated bare-em pitch, and re-pitching it walks every row down the page.</summary>
    internal bool HtmlChainStyledCells;

    // The page the current BuildMultiPage is building against — nested-table
    // measurement needs it for font registration inside the row-plan phase.
    private Page? _buildPage;

    // The current build's page bounds (see their assignment in BuildMultiPage):
    // handed to a nested grid's draw-time build so it splits at the same page
    // bottom and resumes at the same fresh-page top as its host.
    private double _curPageBottom;

    private double _curFreshTopMargin;

    /// <summary>Height actually CONSUMED on each page of the last build — what a host
    /// row's slice must size to when this grid drives its page break (the host's
    /// line-quanta allotment would fill to the page bottom and paint the band strip
    /// that must stay bare below the break).</summary>
    internal readonly List<double> LastPageConsumedH = new();

    // True when the build comes from the page's main paragraph flow — the only
    // context where the "don't START a table inside the bottom-margin band" keep
    // rule applies (a footer's table legitimately sits at the page foot).
    private bool _contentFlow;

    /// <summary>Per-page content streams for inline cell graphs (legend swatches, bar
    /// graphs) drawn during the most recent build; the caller appends each via
    /// <see cref="Page.AddContentStream"/> once the page exists.</summary>
    internal IReadOnlyList<List<byte[]>> LastGraphDraws => _pageGraphs;

    private readonly List<List<byte[]>> _pageGraphs = new();

    /// <summary>Per-page footnote reference-marker positions collected during the most
    /// recent <see cref="BuildMultiPage"/>: the note, the end-of-text x, the line's
    /// baseline, and the line's font size. The caller draws the superscript marker
    /// and queues the note body into the page-bottom band.</summary>
    internal IReadOnlyList<List<(Note note, double x, double baseline, double size)>> LastFootnoteMarks => _pageFootnotes;

    private readonly List<List<(Note note, double x, double baseline, double size)>> _pageFootnotes = new();
}

/// <summary>
/// Represents a row in a table.
/// </summary>
/// <summary>An inline box drawn behind part of a cell line (the HTML inline-block
/// idiom: a title plate, a rounded status pill), optionally trailed by a filled
/// circle carrying a letter (traffic-light badges). Geometry is pre-measured by
/// the HTML converter with the same metrics that lay the line out; offsets are
/// relative to the line's text origin.</summary>
internal sealed class InlineBoxDecoration
{
    /// <summary>Right inset a packed plate keeps off its column edge (a
    /// title plate ends ≈2 pt short of its cell).</summary>
    internal const double PackEdgeInsetPt = 2.0;

    /// <summary>Box left edge relative to the line's text origin.</summary>
    public double XOff;
    /// <summary>Full box width (pads + text + optional circle).</summary>
    public double Width;
    public double PadTop;
    public double PadBottom;
    public double PadRight;
    public double Radius;
    /// <summary>Box fill; null draws no rectangle (a continuation line whose
    /// plate was already painted by its first line).</summary>
    public Color? Fill = Color.White;
    /// <summary>Explicit box height (a CSS-declared plate height + pads); the
    /// rectangle may span the following line(s). 0 = the line's own box.</summary>
    public double Height;
    /// <summary>Vertical white inset inside the line box (status pills keep a
    /// small gap above and below their rounded rectangle).</summary>
    public double InsetV;
    /// <summary>The text run drawn inside the box: the box model owns the pen, so
    /// the run's x is explicit and the line's flat text is not drawn.</summary>
    public string? Text;
    public double TextX;
    public double TextSize;
    public bool TextBold;
    /// <summary>CSS letter-spacing for the text run (Tc operator).</summary>
    public double TextLetterSpacing;
    /// <summary>Block-level box (a section heading bar): spans the cell's content
    /// width at draw time.</summary>
    public bool FullWidth;
    /// <summary>Centre the text run within the (possibly full-width) box.</summary>
    public bool TextCentered;
    /// <summary>The run's own colour (a heading bar's white); null = the line's.</summary>
    public Color? TextColor;
    /// <summary>Trailing circle fill; null = no circle.</summary>
    public Color? CircleFill;
    public string? CircleLetter;
    public Color? CircleLetterColor;
    /// <summary>Circle diameter in points.</summary>
    public double CircleD;
}

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

    /// <summary><see cref="MinRowHeight"/> is the CSS CONTENT box a fixed-height child
    /// claims, so the cell's own padding rides on top of it — as opposed to the legacy
    /// <c>height="N"</c> floor, which is the whole row height.</summary>
    internal bool MinRowHeightIsContent { get; set; }

    /// <summary>Row background color.</summary>
    public Color? BackgroundColor { get; set; }

    /// <summary>Default text state for cells in this row. Auto-initialized so callers can
    /// mutate properties without null-checking.</summary>
    public TextState? DefaultCellTextState { get; set; } = new TextState();

    /// <summary>Default cell padding for cells in this row.</summary>
    public MarginInfo? DefaultCellPadding { get; set; }

    /// <summary>Vertical alignment applied to each cell's content. Consumed
    /// by <see cref="Cell.VerticalAlignment"/> when the cell itself doesn't
    /// override it. None = unset (top-seated for plain rows; a row-spanning
    /// cell centres its block instead — an EXPLICIT Top there pins the block
    /// to the span top, which is why unset must stay distinguishable).</summary>
    public VerticalAlignment VerticalAlignment { get; set; } = VerticalAlignment.None;

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

    /// <summary>HTML NOWRAP: the cell's lines render whole — the layout pass never
    /// wraps them, even when the width estimate says they overflow the column.</summary>
    internal bool HtmlNoWrap { get; set; }

    /// <summary>Vertical alignment of the cell content. None = unset: plain
    /// rows seat content at the top, while a row-spanning cell centres its
    /// block; an EXPLICIT Top pins a spanning block to the span top.</summary>
    public VerticalAlignment VerticalAlignment { get; set; } = VerticalAlignment.None;

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

    /// <summary>Indexer with get/set. Reading past
    /// the end auto-extends the collection with empty rows (the Table
    /// grows on demand, so callers may address a cell before filling it).</summary>
    public Row this[int index]
    {
        get
        {
            while (index >= 0 && _rows.Count <= index) _rows.Add(new Row());
            return _rows[index];
        }
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

    /// <summary>Indexer access to the cell at the given zero-based index. Reading
    /// past the end auto-extends the row with empty cells (the Row grows
    /// on demand, so a cell may be styled before the row is fully populated).</summary>
    public Cell this[int index]
    {
        get
        {
            while (index >= 0 && _cells.Count <= index) _cells.Add(new Cell());
            return _cells[index];
        }
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
