using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

/// <summary>
/// Represents a table that can be added to a PDF page.
/// </summary>
public partial class Table : BaseParagraph
{
    /// <summary>CSS <c>line-height: normal</c> — the UA's default line box is
    /// ~1.2 em of the font size (the factor the whole HTML cell model pitches
    /// unstyled lines at).</summary>
    internal const double CssNormalLineHeight = 1.2;

    /// <summary>CSS `thin` border stroke in points (1px medium-thin at the CSS
    /// 0.75 px→pt factor): the band a boxed div adds above and below its line
    /// box (probed on the boxed-header cells, rows pitch fs·1.2 + 2×0.7).</summary>
    internal const double HtmlThinBorderPt = 0.7;

    /// <summary>The browser's line box for an unstyled run: the font size in device
    /// pixels times the UA's 1.15 normal leading, ROUNDED to whole pixels, back in
    /// points — 9 pt for an 8 pt line. The rule generalises to every face.</summary>
    internal static double CssLineBoxPt(double fontPt)
        => Math.Round(fontPt * 4.0 / 3.0 * 1.15) * 0.75;

    /// <summary>The browser's CSS `line-height: normal` box of a REAL face: the face's
    /// hhea line height (ascent+descent as a fraction of em) in device pixels, rounded
    /// to whole pixels, back in points — 8.25 pt (11 px) for Verdana at 9 px.</summary>
    internal static double FaceCssLineBoxPt(double fontPt, double faceLineRatio)
        => Math.Round(fontPt * 4.0 / 3.0 * faceLineRatio, MidpointRounding.AwayFromZero) * 0.75;

    /// <summary>An inline-styled grid (`&lt;table style="font-family: X; font-size: …"&gt;`
    /// with X a resolvable installed face): its rows pitch at X's own CSS line box —
    /// this is the face's hhea (asc+desc)/em ratio. Zero = not that dialect.</summary>
    internal double InlineFaceGridRatio { get; set; }

    /// <summary>The caller lays every spill slice onto pages that share ONE /Font
    /// resource dictionary (the HTML converter's per-conversion dict) — Type0
    /// embedding through the first page reaches them all, so spill slices may
    /// draw embedded faces too. False: spill slices draw Standard-14 only.</summary>
    internal bool SpillPagesShareFontDict { get; set; }

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

    // A DataWorks form-grid TEXT INPUT / SELECT drawn as its declared box with
    // the value typeset inside (entries ride CellLine.InputBoxes in order).
    internal const char InlineInputChar = '';

    // …and a CHECKED checkbox drawn as a bare checkmark glyph.
    internal const char InlineCheckChar = '';

    /// <summary>DataWorks form grid: an UNCHECKED (borderless) checkbox — draws
    /// nothing but advances the pen by its widget width.</summary>
    internal const char InlineCheckboxGapChar = '';

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

    // Built by Document.BindXml: lay this table out with the classic
    // XML-generator model (bare-em line grid, padded blank rows, centred cell
    // content). See the XmlGeneratorModel gates in Table.RowLayout/MultiPage.
    internal bool XmlGeneratorModel;

    // The document DefaultTextState LineSpacing under the XML model: every cell
    // line's pitch (and its baseline seat) grows by this leading.
    internal double XmlLineSpacing;

    /// <summary>How far the LAST column's box overhangs the page's content band. The
    /// cell border joins the column pitch AFTER the declared widths were fitted, so a
    /// grid that exactly filled its band overruns it by one pitch per column: the
    /// reference draws that column's box at its full width and wraps and centres its
    /// text in what is left of the page. Content is measured on the clamped width
    /// (colWidths already carries it); this is what the BOX gets back.</summary>
    internal double LastColBoxOverhang;

    // XML header band: the table-level BackgroundColor fill bleeds to this full
    // width from x = 0 (an era header band paints edge-to-edge while its
    // columns stay in the content band).
    internal double XmlBandBleedWidth;

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
    /// How columns are sized when the table is rendered (API compatibility).
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

    /// <summary>Sum of column widths from <see cref="ColumnWidths"/>. An
    /// AutoFitToContent table with no configured widths reports its measured
    /// width: per column the widest cell's unwrapped text plus its padding plus
    /// the 0.01 pt measure guard (two "Cell N text" cells report 88.94).
    /// Zero when no widths are configured otherwise.</summary>
    public double GetWidth()
    {
        if (string.IsNullOrEmpty(ColumnWidths))
        {
            if (ColumnAdjustment != ColumnAdjustment.AutoFitToContent || Rows.Count == 0)
                return 0;
            var cols = 1;
            for (var i = 0; i < Rows.Count; i++)
                if (Rows.At(i).Cells.Count > cols) cols = Rows.At(i).Cells.Count;
            var w = new double[cols];
            for (var ri = 0; ri < Rows.Count; ri++)
            {
                var row = Rows.At(ri);
                for (var ci = 0; ci < row.Cells.Count && ci < cols; ci++)
                {
                    var cell = row.Cells[ci];
                    var pad = cell.Margin ?? row.DefaultCellPadding ?? DefaultCellPadding;
                    var need = MaxLineWidth(cell, row, exact: true) + (pad?.Left ?? 0) + (pad?.Right ?? 0)
                        + AutoFitMeasureGuardPt;
                    if (need > w[ci]) w[ci] = need;
                }
            }
            double autoTotal = 0;
            foreach (var cw in w) autoTotal += cw;
            return Math.Round(autoTotal, 2);
        }
        double total = 0;
        foreach (var tok in ColumnWidths.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            if (TryParseWidthToken(tok, out var w))
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
        var availWidth = parentPage is not null ? GetMeasureBandWidth(parentPage) : 523.0;
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
                if (cell.SpanContinuation) continue;
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
        // A DOUBLED side is two rules with a gap between them: it takes three stroke
        // widths of the row's height, not one.
        return OccupiedSideWidth(b, BorderSide.Top, b.TopAssigned, b.RawTop)
             + OccupiedSideWidth(b, BorderSide.Bottom, b.BottomAssigned, b.RawBottom);
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
            // The column's text state also ALIGNS the column: a currency column given a
            // right-aligned state right-aligns its cells, a date column centres them
            // (the report's date/currency columns are set this way and the rows come out
            // centred / flush right against the column edge). Cells.Add(text, state)
            // carries the same alignment; a cell that set its own keeps it.
            if (textState.HorizontalAlignment != HorizontalAlignment.None
                && cell.Alignment == HorizontalAlignment.Left)
                cell.Alignment = textState.HorizontalAlignment;
            if (cell.DefaultCellTextState is not { } st)
            {
                cell.DefaultCellTextState = textState;
                continue;
            }
            st.ForegroundColor ??= textState.ForegroundColor;
            if (st.FontSize <= 0 && textState.FontSize > 0) st.FontSize = textState.FontSize;
            if (st.Font is null && textState.Font is not null) st.Font = textState.Font;
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

    // Redline diff cells: baselines seat a full AscLead (0.929 em) below the
    // line-box top instead of the legacy full-em-minus-descender drop.
    internal bool RedlineCellSeat { get; set; }

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

    /// <summary>Top/bottom margins of the table's n-th spill page (1-based), asked
    /// as the builder breaks to it — the flow answers with the page the
    /// OnBeforePageGenerate handler prepared for that slot. Null: every spill
    /// page keeps the first page's margins.</summary>
    internal Func<int, (double top, double bottom)>? SpillPageMargins { get; set; }

    /// <summary>pt-styled fragment: cell paragraph margins inset the WRAP box
    /// (the column keeps width + pads; the text wraps inside the margins).</summary>
    internal bool HtmlWrapInsetsCellMargins { get; set; }

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

    /// <summary>Set by the HTML builder when the grid (or one nested in it) carries
    /// an OVER-DECLARED fixed-layout attribute row — width attributes summing past
    /// 100%. The converter reads it off the width probe to pick the render path:
    /// these documents draw their nested grids as real grids.</summary>
    internal bool HtmlOverDeclaredGrid { get; set; }

    /// <summary>Widget width this grid draws that its HOST column's min-content
    /// excludes (the DataWorks borderless checkbox: the nested results grid
    /// overflows its outer column by exactly this).</summary>
    internal double HtmlDwGapReservePt { get; set; }

    /// <summary>DataWorks form dialect: serif control-cell text, hidden-widget
    /// reserves and the h1 title seat are scoped to it.</summary>
    internal bool DwFormCells { get; set; }

    /// <summary>This table was built FOR the over-declared grid document's render
    /// pass — cell text wraps at its column width even when the fragment carries
    /// its own embedded CJK face (the legacy path draws those unwrapped).</summary>
    internal bool HtmlOverDeclaredDraw { get; set; }

    /// <summary>Set by the HTML builder when a &lt;pre&gt; cell's unbreakable
    /// longest line grew the grid past its declared width (the phantom surplus
    /// column). The converter seats such a document's content at the UA top
    /// margin instead of the legacy calibrated one.</summary>
    internal bool HtmlPreGrownGrid { get; set; }

    /// <summary>The reported natural width is the UA-serif percent-grid
    /// min-content floor sum (BuildTableFromHtml's uaSerifMin) - the
    /// ink-widen sheet model applies when this table drives the widen.</summary>
    internal bool HtmlPctMinNatural { get; set; }

    /// <summary>When positive, a row-band fill on the row's LAST cell extends its
    /// right edge to this x (the page's right edge): the over-declared grid
    /// document's reference paints its section bands page-wide while the table
    /// content keeps the standard box.</summary>
    internal double HtmlBandBleedRightPt { get; set; }

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

    /// <summary>Per built page, the checkbox widgets laid out in its cells with the
    /// rectangle each landed on. Page 0's are bound to the page at build time; a
    /// spill page's are bound by the flow dispatcher once that page exists.</summary>
    internal IReadOnlyList<List<(Aspose.Pdf.Forms.CheckboxField cbf, Rectangle rect)>> LastCheckboxDraws => _pageCheckboxes;
    private readonly List<List<(Aspose.Pdf.Forms.CheckboxField cbf, Rectangle rect)>> _pageCheckboxes = new();

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
