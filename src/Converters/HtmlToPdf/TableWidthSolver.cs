using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>The width solver: turns the parse loop's column model (min/max/header/
/// declared/percent widths, colgroup, span constraints) into the table's final
/// column widths and its natural sheet width. Lifted verbatim out of
/// <c>BuildTableFromHtml</c>; one call, straight after the parse.</summary>
    private static double SolveColumnWidths(TableColumnModel colModel, Table table,
        Dictionary<string, string> tblStyle, Match tblTag, List<CssElem>? chainBase,
        double availWidthPt, double cellFontSize, bool cellFontShorthand, bool dwFormCells,
        bool fullWidthCjkMin, bool overDeclaredDraw, bool uaDocGrid, double padSide,
        double rowPctDeclMax, int headerRows, bool ptCellWidths, bool uaCellBoxes,
        bool uaSerifMin, double rowPxAtMax, int rowPxCellsAtMax, double naturalWidthPt, bool wordMailCells = false, bool nestedGrid = false)
    {
        var ws = new TableWidthSolveState();
        ws.colModel = colModel;
        ws.table = table;
        ws.tblStyle = tblStyle;
        ws.tblTag = tblTag;
        ws.chainBase = chainBase;
        ws.availWidthPt = availWidthPt;
        ws.cellFontSize = cellFontSize;
        ws.cellFontShorthand = cellFontShorthand;
        ws.dwFormCells = dwFormCells;
        ws.fullWidthCjkMin = fullWidthCjkMin;
        ws.overDeclaredDraw = overDeclaredDraw;
        ws.uaDocGrid = uaDocGrid;
        ws.padSide = padSide;
        ws.rowPctDeclMax = rowPctDeclMax;
        ws.headerRows = headerRows;
        ws.ptCellWidths = ptCellWidths;
        ws.uaCellBoxes = uaCellBoxes;
        ws.uaSerifMin = uaSerifMin;
        ws.wordMailCells = wordMailCells;
        ws.rowPxAtMax = rowPxAtMax;
        ws.rowPxCellsAtMax = rowPxCellsAtMax;
        ws.pctCapW = 0.0;
        ws.pctNaturalW = 0.0;
        ws.pctMinsForDraw = null;
        ws.naturalWidthPt = naturalWidthPt;
        // A UA-boxed NESTED grid's declared cell width is a floor on its column's content box
        // (CSS: the width is the cell's content box, its padding on top): the column's min-
        // and max-content are no narrower than the declared box (measured: a 176 px label
        // column beside two auto columns keeps 132 + its pads, wider than its widest label).
        // A sheet-wide grid keeps its measured columns: its declared px cells do not grow the
        // sheet past the ink the reference keeps (measured on the twelve-page report).
        // (…a nested grid declaring a PERCENT box keeps no such floor when its declared cells OVER-FILL the box:
        //  they yield inside it - the mailing's sixteen 40 px number boxes in a `width="100%"` grid share the
        //  450 pt container; a 10 px spacer column inside its box keeps its floor - the resume's list gutter)
        var declSumPt = 0.0;
        foreach (var dw in colModel.colDeclW) if (dw > 0) declSumPt += dw + colModel.cellExtraPt;
        var pctBoxPt = colModel.tableWidthPctOfBox && availWidthPt > 0 ? availWidthPt * colModel.tableWidthFrac : 0;
        MeasureDeclaredColumnSum(ws, colModel, nestedGrid, pctBoxPt, declSumPt, uaCellBoxes);
        // A DOCUMENT-rule element width (`table { width: 650px }`) is a fill
        // target the same way the attribute is: every grid on the page
        // stretches to the declared box (its one-cell banner rows span
        // the full width, and a narrow grid's columns scale up).
        if (ws.tableWidthAbsPt <= 0 && ws.colModel.tableWidthFromDocRule && ws.colModel.tableWidthDeclAbsPt > 0)
            ws.tableWidthAbsPt = ws.colModel.tableWidthDeclAbsPt;
        // (a width spelt in the table's own style or class declares the box the attribute does)
        var declBoxPt = ws.tableWidthAbsPt > 0 ? ws.tableWidthAbsPt : ws.colModel.tableWidthDeclAbsPt;
        ws.table.HtmlDeclaredBoxPt = declBoxPt;
        ws.table.HtmlDeclaredBoxAbs = colModel.tableWidthDeclaredAbs;
        var fillBoxPt = ws.tableWidthAbsPt;
        SettleTableFillAndCapWidths(ws, colModel, fillBoxPt, declBoxPt);
        AdoptHtmlSpaceClassWinner(ws);
        if (ws.wordMailCells) PinWordMailDeclaredColumns(ws);
        double minContent = (colModel.maxCols + 1) * colModel.tblCellSpacingPt;
        foreach (var w in colModel.colMinW) minContent += w;
        ws.table.HtmlMinContentPt = minContent;
        double maxContent = (colModel.maxCols + 1) * colModel.tblCellSpacingPt;
        for (var i = 0; i < colModel.colMaxW.Count; i++)
            maxContent += Math.Max(colModel.colMaxW[i], i < colModel.colMinW.Count ? colModel.colMinW[i] : 0);
        ws.table.HtmlMaxContentPt = maxContent;
        if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_CELL") == "1")
            Console.Error.WriteLine($"[cols] avail={availWidthPt:0.#} decl=[{string.Join(" ", colModel.colDeclW.ConvertAll(v => v.ToString("0.#")))}] min=[{string.Join(" ", colModel.colMinW.ConvertAll(v => v.ToString("0.#")))}] max=[{string.Join(" ", colModel.colMaxW.ConvertAll(v => v.ToString("0.#")))}] chosen=[{table.ColumnWidths}] natural={ws.naturalWidthPt:0.#}");
        // A collapsed-border grid's box extends half of its outermost cell rules past the columns (CSS
        // border-collapse: the mailing's `.left-column { border-right: 1px }` stands its 630 px content grid
        // 630.5 px wide - the sheet's 652.875).
        if (colModel.sheetCollapsed && ws.naturalWidthPt > 0)
            ws.naturalWidthPt += CollapsedOuterBorderHalfPt(table);
        // A UA-boxed grid declaring a PERCENT box scales its declared columns alike into that box when they
        // over-fill it (CSS: the mailing's sixteen 40 px number boxes share the 450 pt `width="100%"` band).
        if (uaCellBoxes && colModel.tableWidthPctOfBox && colModel.colDeclW.Exists(w => w > 0))
            ws.table.FitColumnsToBand = true;
        return ws.naturalWidthPt;
    }

    private static double CollapsedOuterBorderHalfPt(Table table)
    {
        double left = 0, right = 0;
        foreach (Row row in table.Rows)
        {
            if (row.Cells.Count == 0) continue;
            if (row.Cells[0].Border is { } lb && (lb.Side.HasFlag(BorderSide.Left) || lb.LeftAssigned)) left = Math.Max(left, lb.Left.LineWidth > 0 ? lb.Left.LineWidth : lb.Width);
            if (row.Cells[row.Cells.Count - 1].Border is { } rb && (rb.Side.HasFlag(BorderSide.Right) || rb.RightAssigned)) right = Math.Max(right, rb.Right.LineWidth > 0 ? rb.Right.LineWidth : rb.Width);
        }
        return (left + right) / 2;
    }

    /// <summary>Whether the widest nowrap span runs across every column that holds content (a
    /// column whose minimum is no more than the per-cell slack holds none).</summary>
    private static bool SpanCoversContentColumns(TableColumnModel colModel)
    {
        if (colModel.spanNoWrapCols <= 0) return false;
        int first = -1, last = -1;
        for (var i = 0; i < colModel.colMinW.Count; i++)
        {
            if (colModel.colMinW[i] <= colModel.cellExtraPt + 0.01) continue;
            if (first < 0) first = i;
            last = i;
        }
        if (first < 0) return true;
        return colModel.spanNoWrapStart <= first && colModel.spanNoWrapStart + colModel.spanNoWrapCols > last;
    }

    /// <summary>The first non-empty grid cell decides the space class every grid in the table
    /// draws with: the winner is the first ordinary or non-breaking space found in cell text.</summary>
    private static void AdoptHtmlSpaceClassWinner(TableWidthSolveState ws)
    {
        ws.grids = new List<Table>();
        CollectGrids(ws.table, ws.grids);
        foreach (var g in ws.grids)
        {
            foreach (var r in g.Rows)
                foreach (Cell c in r.Cells)
                    foreach (var p in c.Paragraphs)
                        if (p is Text.TextFragment { Text: { Length: > 0 } t })
                            foreach (var ch in t)
                                if (ch is ' ' or ' ')
                                {
                                    foreach (var gg in ws.grids) gg.HtmlSpaceClassFirst = ch;
                                    return;
                                }
        }
    }

    /// <summary>Word mail (fixed table layout): a column with a declared width keeps exactly that width; the
    /// columns without one share what the box leaves, in the proportion the solve gave them, each floored at
    /// its min-content. A middle cell's nested content never squeezes a declared picture column.</summary>
    private static void PinWordMailDeclaredColumns(TableWidthSolveState ws)
    {
        var cm = ws.colModel;
        var chosen = new List<double>();
        if (ws.table.ColumnWidths is not { Length: > 0 } widths) return;
        foreach (var part in widths.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!double.TryParse(part, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) return;
            chosen.Add(v);
        }
        if (chosen.Count == 0 || chosen.Count > cm.colDeclW.Count || ws.availWidthPt <= 0) return;
        double fixedSum = 0, autoSum = 0; var autos = 0;
        for (var i = 0; i < chosen.Count; i++)
        {
            if (cm.colDeclW[i] > 0) fixedSum += cm.colDeclW[i];
            else { autoSum += chosen[i]; autos++; }
        }
        // A grid whose EVERY column is declared, and whose declarations fill its box, lays each column at
        // exactly its declaration - there is nothing left to share and no column to share it with. The
        // solve had handed the empty trailing columns their min-content and spread the 664 pt that freed
        // over the painted ones (probed on the Word mail's 1289 pt grid: 15 painted columns at 36-40 pt
        // and 16 empty ones at 45, summing to the box; the reference's painted block ends at 682.77).
        // (the box those declarations fill is the grid's OWN declared width, not the page it is first
        //  solved against: a 1289 pt grid is solved at the A4 content box first and widened after, and
        //  its columns are its declarations either way)
        var ownBox = cm.tableWidthDeclaredAbs && cm.tableWidthDeclAbsPt > 0
            && Math.Abs(fixedSum - cm.tableWidthDeclAbsPt) <= 0.01 * cm.tableWidthDeclAbsPt + 3;
        var pinAll = autos == 0 && (fixedSum <= ws.availWidthPt + 0.5 || ownBox);
        if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_TABLEW") == "1")
            Console.Error.WriteLine($"[wm] cols={chosen.Count} autos={autos} fixedSum={fixedSum:0.##} avail={ws.availWidthPt:0.##} ownBox={cm.tableWidthDeclAbsPt:0.##} pinAll={pinAll}");
        if (pinAll)
        {
            for (var i = 0; i < chosen.Count; i++)
            {
                chosen[i] = cm.colDeclW[i];
                // (the draw-time re-solve reads the per-column model, so the pin is written there too)
                if (i < cm.colMinW.Count) cm.colMinW[i] = chosen[i];
                if (i < cm.colMaxW.Count) cm.colMaxW[i] = chosen[i];
            }
            ws.table.ColumnWidths = string.Join(" ", chosen.ConvertAll(v => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)));
            return;
        }
        if (autos == 0 || autos == chosen.Count || fixedSum >= ws.availWidthPt) return;
        var rest = ws.availWidthPt - fixedSum;
        for (var i = 0; i < chosen.Count; i++)
        {
            if (cm.colDeclW[i] > 0) { chosen[i] = cm.colDeclW[i]; continue; }
            var share = autoSum > 0 ? chosen[i] / autoSum : 1.0 / autos;
            chosen[i] = Math.Max(i < cm.colMinW.Count ? cm.colMinW[i] : 0, rest * share);
        }
        ws.table.ColumnWidths = string.Join(" ", chosen.ConvertAll(v => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)));
    }
}
