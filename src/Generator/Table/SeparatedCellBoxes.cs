using Aspose.Pdf.Content;

namespace Aspose.Pdf;

public partial class Table
{
    /// <summary>A column's declared width is its cells' WHOLE box, rules included:
    /// a cell's rules stand inside the width its column declares, and its text box
    /// is what they and the padding leave -- the CSS separated-border model, where
    /// a 100 pt column with 0.5 pt rules is 100 wide, its rules on 0..0.5 and
    /// 99.5..100 and its text 2.5 in. The engine's own model lays the rules OUTSIDE
    /// the declared width instead (that column pitches 101; see
    /// <see cref="CellBorderInPitch"/>). Opt-in; a collapsed grid
    /// (<see cref="IsBordersCollapsed"/>) keeps its rules inside its widths already.
    /// A content-sized grid then measures each column by its cells' own rules, and
    /// shares a band it fills after its own border and its cell gaps are taken off.</summary>
    public bool RulesInsideColumnWidth { get; set; }

    /// <summary>The gap between neighbouring cell boxes across the grid, and between
    /// the outermost boxes and the grid's edge -- its own border, when it has one --
    /// in points: CSS <c>border-spacing</c>, HTML <c>cellspacing</c>. A grid of n
    /// columns is n + 1 such gaps wider than its columns, and a spanning cell covers
    /// the gaps between the columns it spans. Ignored by a collapsed grid, whose
    /// cells share their boundaries. Zero by default: the boxes abut, each drawing
    /// its own rule, so two rules stand side by side between neighbours.</summary>
    public double HorizontalCellSpacing { get; set; }

    /// <summary>The gap between one row's cell boxes and the next's, and between the
    /// first and last rows and the grid's edge, in points -- the vertical half of
    /// CSS <c>border-spacing</c>. A grid of m rows is m + 1 such gaps taller than its
    /// rows, on every page it runs onto, a repeated header band included; a
    /// row-spanning cell covers the gaps between the rows it spans. Ignored by a
    /// collapsed grid. Zero by default.</summary>
    public double VerticalCellSpacing { get; set; }

    /// <summary>The gap the grid lays between its columns: none when collapsed.</summary>
    private double CellSpacingH => IsBordersCollapsed ? 0 : HorizontalCellSpacing;

    /// <summary>The gap the grid lays between its rows: none when collapsed.</summary>
    private double CellSpacingV => IsBordersCollapsed ? 0 : VerticalCellSpacing;

    /// <summary>Where a column's box starts, from the grid's left edge: the gap
    /// before every column up to it and the widths of the columns before it.</summary>
    private double ColumnBoxOffset(double[] colWidths, int col)
    {
        var x = CellSpacingH;
        for (var c = 0; c < col && c < colWidths.Length; c++) x += colWidths[c] + CellSpacingH;
        return x;
    }

    /// <summary>The width of the grid's box: its columns and the gaps before, between
    /// and after them.</summary>
    private double GridBoxWidth(double[] colWidths)
    {
        var width = CellSpacingH;
        foreach (var w in colWidths) width += w + CellSpacingH;
        return width;
    }

    /// <summary>What the grid's box takes off a band it fills before its columns
    /// share the rest, when its rules stand inside its column widths: its own
    /// border on each side and the gap before, between and after its columns.</summary>
    private double SeparatedGridChrome(int columns) => 2 * OuterBorderWidth() + (columns + 1) * CellSpacingH;

    /// <summary>The widths of the rules a cell's left and right sides draw -- its own
    /// border, or the one it inherits; nothing for a cell that asked for no rule.</summary>
    private (double Left, double Right) CellRuleWidths(Cell cell, Row row)
    {
        if (CellRuleBorder(cell, row) is not { } b) return (0, 0);
        return (OccupiedSideWidth(b, BorderSide.Left, b.LeftAssigned, b.RawLeft),
                OccupiedSideWidth(b, BorderSide.Right, b.RightAssigned, b.RawRight));
    }

    /// <summary>The border a cell draws its rules with -- its own, or the one it
    /// inherits from its row or the grid; null for a cell that asked for none.</summary>
    private BorderInfo? CellRuleBorder(Cell cell, Row row) =>
        cell.IsNoBorder ? null : cell.Border ?? row.DefaultCellBorder ?? row.Border ?? DefaultCellBorder;

    /// <summary>Draws a cell's rules inside the box its column width gives it: each
    /// side is a FILLED band of its own width and colour along the box edge -- a 3 pt
    /// left rule fills the box's first 3 pt, a 0.5 pt top rule its first 0.5 -- and
    /// every band runs the box's full extent, so the corners are covered. A band,
    /// not a stroke: the rasteriser covers a fill's edge and a stroke's edge
    /// differently, and a grid whose neighbours each draw their own rule shows that
    /// on every boundary. (The pitch model's stroke insets a per-side border twice,
    /// once before and once inside the call; it is not used here.)</summary>
    private static void DrawRulesInsideBox(ContentStreamBuilder builder, BorderInfo border,
        double x, double y, double w, double h) => RulePainter.PaintBox(builder, border, x, y, w, h);

    /// <summary>What a cell's rules take off its box across, leaving the text box:
    /// the cell's OWN two rules where the rules stand inside the column width,
    /// otherwise the pitch the grid's default rule adds to every column.</summary>
    private double CellRuleInsetAcross(Cell cell, Row row)
    {
        if (!RulesInsideColumnWidth) return _columnPitch;
        var (left, right) = CellRuleWidths(cell, row);
        return left + right;
    }
}
