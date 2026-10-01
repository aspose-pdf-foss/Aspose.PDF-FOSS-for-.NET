using System;
using System.Collections.Generic;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    private const double FbUaCellSpacingPx = 2.0;             // a separated-border table's default cellspacing
    private const double FbUaCellPaddingPx = 1.0;             // …and cellpadding

    /// <summary>A laid-out table's grid: the column lines, the row tops, the collapsed border width
    /// and each cell's border sides, for the border painter.</summary>
    private sealed class FbTableGrid
    {
        public double[] Gx = Array.Empty<double>();           // nCols + 1 grid lines (collapsed) / cell box edges
        public double[] RowTop = Array.Empty<double>();       // nRows + 1 (the last = the rows' bottom)
        public double[] RowMin = Array.Empty<double>();       // per row: the band its <tr> height declares (0 = none)
        public double B;                                      // the collapsed border width (pt)
        public string Rgb = "0 0 0";
        public double X0, X1;                                 // the table box, the horizontals are clipped to it
        public bool Collapsed;
        public List<FbTableCell>[] Rows = Array.Empty<List<FbTableCell>>();
        public bool[] ChargedTop = Array.Empty<bool>();       // per row: the boundary border above is in its band
        public bool[] ChargedBottom = Array.Empty<bool>();    // per row: the boundary border below is in its band
        public void Shift(double dx, double dy)
        {
            for (var i = 0; i < Gx.Length; i++) Gx[i] += dx;
            for (var i = 0; i < RowTop.Length; i++) RowTop[i] += dy;
            X0 += dx; X1 += dx;
        }
    }

    private sealed class FbTableCell
    {
        public int Col;
        public int Span = 1;                                  // colspan: the grid columns the cell covers
        public int Group;                                     // the row group (thead / tbody / tfoot / none) index
        public HtmlNode Node = null!;
        public FbBoxProps P = null!;
        public FbStyle St = null!;
        public bool Top, Right, Bottom, Left;
        public FbBox Box = null!;                             // the cell's content box (lines live here)
        public bool Bordered => Top || Right || Bottom || Left;
    }

    /// <summary>An in-flow table at the given top: its declared width is its border box, else it
    /// shrinks to its columns (capped at the containing block), at the containing block's left after
    /// its margin-left.</summary>
    private static FbBox FbLayoutTable(FbState fb, HtmlNode el, FbStyle st, FbBoxProps p, double cbX, double cbW, double top)
    {
        FbApplyTableBorderAttr(el, p);
        var box = new FbBox { Node = el, St = st, P = p, IsBfc = true, IsTable = true };
        box.W = p.WidthAuto ? 0 : p.WidthPt;
        box.X = cbX + p.Margin.L;
        box.Y = top;
        FbLayoutTableInto(fb, box, cbW);
        return box;
    }

    /// <summary>A `border=N` attribute on a table with no CSS border is its 1-px-per-unit frame.</summary>
    private static void FbApplyTableBorderAttr(HtmlNode el, FbBoxProps p)
    {
        if (p.HasVisibleBorder || el.Attrs is null || !el.Attrs.TryGetValue("border", out var b)) return;
        if (!double.TryParse(b.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) || n <= 0) return;
        p.Border.T = p.Border.R = p.Border.B = p.Border.L = n * FbPxPt;
    }

    /// <summary>Lay the table's rows and columns out into a box whose position is set (its width
    /// when declared): collapsed borders share the grid lines, separated ones space the cell boxes.</summary>
    private static void FbLayoutTableInto(FbState fb, FbBox box, double cbW)
    {
        var el = box.Node!;
        var collapsed = (FbDecl(fb, el, "border-collapse") ?? "").Trim().Equals("collapse", StringComparison.OrdinalIgnoreCase);
        var spacing = collapsed ? 0 : FbTableAttrPx(el, "cellspacing", FbUaCellSpacingPx) * FbPxPt;
        var cellPad = FbTableAttrPx(el, "cellpadding", FbUaCellPaddingPx) * FbPxPt;
        var rows = FbTableRows(fb, el, box.St, box.W, cellPad, out var rowMins);
        var nCols = 0;
        foreach (var r in rows) foreach (var c in r) nCols = Math.Max(nCols, c.Col + c.Span);
        if (nCols == 0 || rows.Count == 0) { box.H = box.P.ChromeV; if (box.W <= 0) box.W = box.P.ChromeH; return; }
        var grid = new FbTableGrid { Rows = rows.ToArray(), RowMin = rowMins.ToArray(), Collapsed = collapsed };
        grid.B = collapsed ? FbCollapsedBorderPt(rows) : 0;
        grid.Rgb = FbGridRgb(rows);
        var b = grid.B;
        var tb = box.P.Border.L;
        var bases = FbColumnBases(fb, rows, nCols, collapsed ? b : 0);
        var sum = 0.0;
        foreach (var v in bases) sum += v;
        // the box: the declared width, else the columns' sum inside the chrome (capped at the container)
        var chrome = collapsed ? (tb > 0 ? 2 * tb : b) : 2 * tb + (nCols + 1) * spacing;
        if (box.W <= 0) box.W = Math.Min(sum + chrome, Math.Max(cbW - box.P.Margin.H, chrome));
        var span = box.W - chrome;
        grid.Gx = new double[nCols + 1];
        grid.Gx[0] = box.X + (collapsed ? (tb > 0 ? tb : b / 2) : tb + spacing);
        for (var c = 0; c < nCols; c++)
            grid.Gx[c + 1] = grid.Gx[c] + (sum > 0 ? bases[c] * span / sum : span / nCols) + (collapsed ? 0 : spacing);
        grid.X0 = box.X; grid.X1 = box.X + box.W;
        FbChargeRowBorders(grid);
        FbLayoutTableRows(fb, box, grid, spacing, tb);
        box.Grid = grid;
    }

    private static double FbTableAttrPx(HtmlNode el, string attr, double fallbackPx)
    {
        if (el.Attrs is not null && el.Attrs.TryGetValue(attr, out var v)
            && double.TryParse(v.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) && n >= 0)
            return n;
        return fallbackPx;
    }

    /// <summary>The rows' bands: each row's cells lay out in their content boxes, the band is the
    /// tallest cell plus the boundary borders charged to the row; the table's height follows.</summary>
    private static void FbLayoutTableRows(FbState fb, FbBox box, FbTableGrid grid, double spacing, double tb)
    {
        var rows = grid.Rows;
        var b = grid.B;
        grid.RowTop = new double[rows.Length + 1];
        var y = box.Y + (grid.Collapsed ? (tb > 0 ? tb : 0) : tb + spacing);
        for (var r = 0; r < rows.Length; r++)
        {
            var above = grid.ChargedTop[r] ? b : 0;
            var kidsBefore = box.Kids.Count;
            var band = FbLayoutTableRowAt(fb, box, grid, rows[r], y + above, spacing);
            // a row that straddles the sheet's bottom moves whole to the next sheet (its lines
            // would otherwise jump there one cell at a time and stretch the band across the cut)
            var pageBottom = FbPageBottom(fb, FbPageOf(fb, y).page);
            if (y < pageBottom - FbEpsilon && y + above + band > pageBottom + FbEpsilon && band <= fb.laterPageStep)
            {
                box.Kids.RemoveRange(kidsBefore, box.Kids.Count - kidsBefore);
                y = pageBottom;
                band = FbLayoutTableRowAt(fb, box, grid, rows[r], y + above, spacing);
            }
            grid.RowTop[r] = y;
            band = Math.Max(band, grid.RowMin[r]);            // (a <tr> height is the band's floor)
            y += above + band + (grid.ChargedBottom[r] ? b : 0) + (grid.Collapsed ? 0 : spacing);
        }
        grid.RowTop[rows.Length] = y - (grid.Collapsed ? 0 : spacing);
        box.H = y - box.Y + (grid.Collapsed ? (tb > 0 ? tb : 0) : tb + (rows.Length == 0 ? spacing : 0));
        if (grid.Collapsed && tb == 0 && FbAnyBottomBorder(rows[^1])) box.H += b / 2;
    }

    /// <summary>Lay one row's cells out with their content tops at a y: each cell's box is added to
    /// the table's kids; the tallest cell is the band.</summary>
    private static double FbLayoutTableRowAt(FbState fb, FbBox box, FbTableGrid grid, List<FbTableCell> row, double top, double spacing)
    {
        var b = grid.B;
        var band = 0.0;
        foreach (var cell in row)
        {
            var leftInset = grid.Collapsed && cell.Left ? b / 2 : 0;
            var rightInset = grid.Collapsed && cell.Right ? b / 2 : 0;
            var cellW = grid.Gx[cell.Col + cell.Span] - grid.Gx[cell.Col] - (grid.Collapsed ? 0 : spacing) - leftInset - rightInset;
            var cellProps = new FbBoxProps { Pad = cell.P.Pad, BgRgb = grid.Collapsed ? null : cell.P.BgRgb, HeightAuto = cell.P.HeightAuto, HeightPt = cell.P.HeightPt };
            var cb = new FbBox { Node = cell.Node, St = cell.St, P = cellProps, IsBfc = true, X = grid.Gx[cell.Col] + leftInset, Y = top, W = cellW };
            var cursor = cb.ContentY;
            FbLayoutChildren(fb, cb, cell.Node, cell.St, cb.ContentX, cb.ContentW, ref cursor, new FbBfc());
            var contentH = cursor - cb.ContentY;
            if (!cell.P.HeightAuto) contentH = Math.Max(contentH, cell.P.HeightPt);
            cb.H = cell.P.Pad.V + contentH;
            cell.Box = cb;
            box.Kids.Add(cb);
            band = Math.Max(band, cb.H);
        }
        return band;
    }

    private static bool FbAnyBottomBorder(List<FbTableCell> row)
    {
        foreach (var c in row) if (c.Bottom) return true;
        return false;
    }

    /// <summary>Which row carries each boundary border of a collapsed grid: the table's own frame
    /// is nobody's; a border at a row-group boundary is charged to the row below; inside a group it
    /// is charged to the row that declares it, and to the row above when both do.</summary>
    private static void FbChargeRowBorders(FbTableGrid grid)
    {
        var rows = grid.Rows;
        grid.ChargedTop = new bool[rows.Length];
        grid.ChargedBottom = new bool[rows.Length];
        if (!grid.Collapsed) return;
        for (var j = 0; j <= rows.Length; j++)
        {
            var belowDeclares = j < rows.Length && FbAnyTopBorder(rows[j]);
            var aboveDeclares = j > 0 && FbAnyBottomBorder(rows[j - 1]);
            if (!belowDeclares && !aboveDeclares) continue;
            if (j == 0) { grid.ChargedTop[0] = true; continue; }
            if (j == rows.Length) continue;                   // the last row's own bottom border: half a width past the box
            var groupBoundary = rows[j][0].Group != rows[j - 1][0].Group;
            if (groupBoundary || (belowDeclares && !aboveDeclares)) grid.ChargedTop[j] = true;
            else grid.ChargedBottom[j - 1] = true;
        }
    }

    private static bool FbAnyTopBorder(List<FbTableCell> row)
    {
        foreach (var c in row) if (c.Top) return true;
        return false;
    }

    /// <summary>The rows of a table as cells with their column, row group, style, box props and
    /// border sides (a cell's font is the table's unless it states its own; a cell without a
    /// padding declaration takes the table's cellpadding).</summary>
    private static List<List<FbTableCell>> FbTableRows(FbState fb, HtmlNode table, FbStyle tableSt, double tableW, double cellPad, out List<double> rowMins)
    {
        var rows = new List<List<FbTableCell>>();
        var mins = new List<double>();
        var group = 0;
        void Walk(HtmlNode n, FbStyle st, int g)
        {
            foreach (var c in n.Children)
            {
                if (c.Tag == "tr")
                {
                    var cells = new List<FbTableCell>();
                    var rst = FbStyleOf(fb, c, st);
                    var rowProps = FbPropsOf(fb, c, tableW);
                    var col = 0;
                    foreach (var td in c.Children)
                    {
                        if (td.Tag is not ("td" or "th")) continue;
                        var cellSt = FbStyleOf(fb, td, rst);
                        var p = FbPropsOf(fb, td, tableW, 0, cellSt.Px);
                        if (p.Display == "none") continue;
                        if (!p.PadDeclared) p.Pad.T = p.Pad.R = p.Pad.B = p.Pad.L = cellPad;
                        // (probed on the factsheet grid: a cell's PERCENT padding resolves to nothing -
                        //  its rows pitch at the line box plus the collapsed border)
                        else if ((FbDecl(fb, td, "padding") ?? "").Contains('%')) p.Pad.T = p.Pad.R = p.Pad.B = p.Pad.L = 0;
                        var span = td.Attrs is not null && td.Attrs.TryGetValue("colspan", out var cs) && int.TryParse(cs.Trim(), out var csn) && csn > 1 ? csn : 1;
                        cells.Add(new FbTableCell
                        {
                            Col = col, Span = span, Group = g, Node = td, P = p, St = cellSt,
                            Top = p.Border.T > 0, Right = p.Border.R > 0, Bottom = p.Border.B > 0, Left = p.Border.L > 0,
                        });
                        col += span;
                    }
                    if (cells.Count > 0) { rows.Add(cells); mins.Add(rowProps.HeightAuto ? 0 : rowProps.HeightPt); }
                }
                else if (c.Tag is "thead" or "tbody" or "tfoot") Walk(c, FbStyleOf(fb, c, st), ++group);
            }
        }
        Walk(table, tableSt, 0);
        rowMins = mins;
        return rows;
    }

    /// <summary>The collapsed border width: the widest border any cell declares.</summary>
    private static double FbCollapsedBorderPt(List<List<FbTableCell>> rows)
    {
        var b = 0.0;
        foreach (var row in rows)
            foreach (var cell in row)
                b = Math.Max(b, Math.Max(Math.Max(cell.P.Border.L, cell.P.Border.R), Math.Max(cell.P.Border.T, cell.P.Border.B)));
        return b;
    }

    private static string FbGridRgb(List<List<FbTableCell>> rows)
    {
        foreach (var row in rows)
            foreach (var cell in row)
                if (cell.Bordered) return cell.P.BorderRgb;
        return "0 0 0";
    }

    /// <summary>Column bases: the widest cell of the column - its declared width when it states
    /// one, else its content on one line - plus that cell's horizontal padding, plus the collapsed
    /// border for a bordered cell.</summary>
    private static double[] FbColumnBases(FbState fb, List<List<FbTableCell>> rows, int nCols, double b)
    {
        var bases = new double[nCols];
        foreach (var row in rows)
            foreach (var cell in row)
            {
                if (cell.Col >= nCols || cell.Span > 1) continue;    // (a spanning cell sizes no single column)
                var content = cell.P.WidthAuto ? FbMaxContentPt(fb, cell.Node, cell.St) : cell.P.WidthPt;
                var v = content + cell.P.Pad.H + (cell.Bordered ? b : 0);
                if (v > bases[cell.Col]) bases[cell.Col] = v;
            }
        return bases;
    }

    /// <summary>The widest line a cell's content makes when nothing wraps: its content laid out in
    /// a trial width, the sized boxes and the text runs measured.</summary>
    private static double FbMaxContentPt(FbState fb, HtmlNode cell, FbStyle st)
    {
        var trial = new FbBox { Node = cell, St = st, P = new FbBoxProps(), IsBfc = true, X = 0, Y = 0, W = FbShrinkTrialWidth };
        var cursor = 0.0;
        FbLayoutChildren(fb, trial, cell, st, 0, FbShrinkTrialWidth, ref cursor, new FbBfc());
        return FbIntrinsicWidth(trial);
    }
}
