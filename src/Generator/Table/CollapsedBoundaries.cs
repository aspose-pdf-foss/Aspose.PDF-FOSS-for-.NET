using Aspose.Pdf.Content;

namespace Aspose.Pdf;

public partial class Table
{
    /// <summary>One boundary segment of a collapsed grid: the rule that won it, or
    /// nothing. <see cref="Border"/> and <see cref="Side"/> say how the winner paints;
    /// <see cref="Width"/> is what it occupies.</summary>
    private readonly struct CollapsedSegment
    {
        public CollapsedSegment(double width, BorderInfo? border, GraphInfo? side)
        {
            Width = width;
            Border = border;
            Side = side;
        }

        public double Width { get; }
        public BorderInfo? Border { get; }
        public GraphInfo? Side { get; }
        public bool Draws => Width > 0 && Border is not null;
    }

    /// <summary>Every boundary of a COLLAPSED grid, resolved once per build.
    ///
    /// Two cells that meet on a boundary each bring a rule to it, and only one is
    /// drawn: the WIDER, with the earlier cell (the one to the left, or above)
    /// keeping a tie; along the grid's outer edge the table's own border is the
    /// other party, and a cell keeps a tie against it too. That is the conflict
    /// rule of CSS 2.1 §17.6.2.1, minus its style ranking.
    ///
    /// A boundary is laid out on ONE line whatever its width, so a heavier rule
    /// grows into both boxes it separates, never moves the line. What moves is what
    /// hangs off the lines: each cell's box starts half of its OWN boundaries'
    /// widths inside them, the grid starts half its widest outer rule inside the
    /// table's box, and a row is as tall as its tallest cell's box.</summary>
    private sealed class CollapsedRules
    {
        /// <summary>[row, boundary 0..columns]: the rule down each column boundary.</summary>
        public CollapsedSegment[,] Vertical = new CollapsedSegment[0, 0];

        /// <summary>[boundary 0..rows, column]: the rule across each row boundary.</summary>
        public CollapsedSegment[,] Horizontal = new CollapsedSegment[0, 0];

        /// <summary>The cell holding each grid position; null where no cell stands.</summary>
        public Cell?[,] Owner = new Cell?[0, 0];

        /// <summary>Each cell's resolved boundary widths — the widest segment on
        /// each of its sides.</summary>
        public readonly Dictionary<Cell, (double Left, double Top, double Right, double Bottom)> CellSides = new();

        public int Rows;
        public int Columns;

        public double LeftEdge;
        public double RightEdge;

        public double TopEdge(int row)
        {
            double widest = 0;
            if (row < 0 || row > Rows) return 0;
            for (var c = 0; c < Columns; c++) widest = Math.Max(widest, Horizontal[row, c].Width);
            return widest;
        }

        public CollapsedSegment VerticalAt(int row, int boundary) =>
            row < 0 || row >= Rows || boundary < 0 || boundary > Columns ? default : Vertical[row, boundary];

        public CollapsedSegment HorizontalAt(int boundary, int column) =>
            boundary < 0 || boundary > Rows || column < 0 || column >= Columns ? default : Horizontal[boundary, column];
    }

    private CollapsedRules? _collapsedRules;

    /// <summary>The resolved boundaries of this grid, or null when the grid is not
    /// collapsed on a cell rule (the model then has no per-boundary widths to give).
    /// Resolved on first use and kept until the next build starts.</summary>
    private CollapsedRules? CollapsedRulesFor(int columns)
    {
        if (!IsBordersCollapsed || columns <= 0 || CollapsedSkeleton() <= 0) return null;
        if (_collapsedRules is { } known && known.Columns == columns && known.Rows == Rows.Count) return known;
        return _collapsedRules = ResolveCollapsedRules(columns);
    }

    /// <summary>The rule width the grid's lines are laid out on: the table's cell
    /// rule, which the pitch box of every column is built around. A boundary of any
    /// other width is measured as a correction from it.</summary>
    private double CollapsedSkeleton()
    {
        var (left, right) = CellBorderPitch();
        return left + right;
    }

    private CollapsedRules ResolveCollapsedRules(int columns)
    {
        var rows = Rows.Count;
        var rules = new CollapsedRules
        {
            Rows = rows,
            Columns = columns,
            Owner = new Cell?[rows, columns],
            Vertical = new CollapsedSegment[rows, columns + 1],
            Horizontal = new CollapsedSegment[rows + 1, columns],
        };
        var ownerRow = new Row?[rows, columns];
        PlaceCollapsedOwners(rules, ownerRow);

        for (var r = 0; r < rows; r++)
            for (var j = 0; j <= columns; j++)
            {
                var left = j > 0 ? rules.Owner[r, j - 1] : null;
                var right = j < columns ? rules.Owner[r, j] : null;
                if (left is not null && ReferenceEquals(left, right)) continue;
                var leftRule = left is null ? default : CellSideRule(left, ownerRow[r, j - 1]!, BorderSide.Right);
                var rightRule = right is null ? default : CellSideRule(right, ownerRow[r, j]!, BorderSide.Left);
                rules.Vertical[r, j] =
                    j == 0 ? Wider(rightRule, TableSideRule(BorderSide.Left))
                    : j == columns ? Wider(leftRule, TableSideRule(BorderSide.Right))
                    : Wider(leftRule, rightRule);
            }

        for (var b = 0; b <= rows; b++)
            for (var c = 0; c < columns; c++)
            {
                var above = b > 0 ? rules.Owner[b - 1, c] : null;
                var below = b < rows ? rules.Owner[b, c] : null;
                if (above is not null && ReferenceEquals(above, below)) continue;
                var aboveRule = above is null ? default : CellSideRule(above, ownerRow[b - 1, c]!, BorderSide.Bottom);
                var belowRule = below is null ? default : CellSideRule(below, ownerRow[b, c]!, BorderSide.Top);
                rules.Horizontal[b, c] =
                    b == 0 ? Wider(belowRule, TableSideRule(BorderSide.Top))
                    : b == rows ? Wider(aboveRule, TableSideRule(BorderSide.Bottom))
                    : Wider(aboveRule, belowRule);
            }

        for (var r = 0; r < rows; r++)
        {
            rules.LeftEdge = Math.Max(rules.LeftEdge, rules.Vertical[r, 0].Width);
            rules.RightEdge = Math.Max(rules.RightEdge, rules.Vertical[r, columns].Width);
        }
        RecordCollapsedCellSides(rules);
        return rules;
    }

    /// <summary>Stands every cell on the grid positions it covers: row by row, each
    /// cell at the first column no earlier span still holds, claiming its column and
    /// row spans (clipped to the grid).</summary>
    private void PlaceCollapsedOwners(CollapsedRules rules, Row?[,] ownerRow)
    {
        for (var r = 0; r < rules.Rows; r++)
        {
            var row = Rows.At(r);
            var cursor = 0;
            for (var ci = 0; ci < row.Cells.Count; ci++)
            {
                var cell = row.Cells.At(ci);
                if (cell.SpanContinuation) continue;
                while (cursor < rules.Columns && rules.Owner[r, cursor] is not null) cursor++;
                if (cursor >= rules.Columns) break;
                var colSpan = Math.Max(1, Math.Min(cell.ColSpan, rules.Columns - cursor));
                var rowSpan = Math.Max(1, Math.Min(cell.RowSpan, rules.Rows - r));
                for (var dr = 0; dr < rowSpan; dr++)
                    for (var dc = 0; dc < colSpan; dc++)
                    {
                        if (rules.Owner[r + dr, cursor + dc] is not null) continue;
                        rules.Owner[r + dr, cursor + dc] = cell;
                        ownerRow[r + dr, cursor + dc] = row;
                    }
                cursor += colSpan;
            }
        }
    }

    /// <summary>Each cell's widest boundary on each side, which is how far its box
    /// stands inside the grid lines around it.</summary>
    private static void RecordCollapsedCellSides(CollapsedRules rules)
    {
        for (var r = 0; r < rules.Rows; r++)
            for (var c = 0; c < rules.Columns; c++)
            {
                var cell = rules.Owner[r, c];
                if (cell is null) continue;
                rules.CellSides.TryGetValue(cell, out var sides);
                if (c == 0 || !ReferenceEquals(rules.Owner[r, c - 1], cell))
                    sides.Left = Math.Max(sides.Left, rules.Vertical[r, c].Width);
                if (c == rules.Columns - 1 || !ReferenceEquals(rules.Owner[r, c + 1], cell))
                    sides.Right = Math.Max(sides.Right, rules.Vertical[r, c + 1].Width);
                if (r == 0 || !ReferenceEquals(rules.Owner[r - 1, c], cell))
                    sides.Top = Math.Max(sides.Top, rules.Horizontal[r, c].Width);
                if (r == rules.Rows - 1 || !ReferenceEquals(rules.Owner[r + 1, c], cell))
                    sides.Bottom = Math.Max(sides.Bottom, rules.Horizontal[r + 1, c].Width);
                rules.CellSides[cell] = sides;
            }
    }

    /// <summary>The rule that wins a boundary: the wider one, the FIRST keeping a tie.</summary>
    private static CollapsedSegment Wider(CollapsedSegment first, CollapsedSegment second) =>
        second.Width > first.Width ? second : first;

    /// <summary>The rule a cell brings to one of its sides: the same border the grid
    /// would draw for it anywhere else, and nothing for a cell that asked for none.</summary>
    private CollapsedSegment CellSideRule(Cell cell, Row row, BorderSide side)
    {
        if (cell.IsNoBorder) return default;
        return SideRule(cell.Border ?? row.DefaultCellBorder ?? row.Border ?? DefaultCellBorder, side);
    }

    /// <summary>The rule the table's own border brings to its outer edge.</summary>
    private CollapsedSegment TableSideRule(BorderSide side) => SideRule(Border, side);

    private static CollapsedSegment SideRule(BorderInfo? border, BorderSide side)
    {
        if (border is null) return default;
        var (assigned, gi) = side switch
        {
            BorderSide.Left => (border.LeftAssigned, border.RawLeft),
            BorderSide.Right => (border.RightAssigned, border.RawRight),
            BorderSide.Top => (border.TopAssigned, border.RawTop),
            _ => (border.BottomAssigned, border.RawBottom),
        };
        var width = DrawnSideWidth(border, side, assigned, gi);
        return width > 0 ? new CollapsedSegment(width, border, gi) : default;
    }

    /// <summary>A cell's resolved boundary widths, or null outside a resolved grid.</summary>
    private (double Left, double Top, double Right, double Bottom)? CollapsedCellSides(Cell cell, int columns) =>
        CollapsedRulesFor(columns) is { } rules && rules.CellSides.TryGetValue(cell, out var sides) ? sides : null;

    /// <summary>How far a resolved box's fill stands inside its pitch box on each side
    /// (left, bottom, right, top): half its own rule there, from lines that stand half
    /// a skeleton rule inside the pitch box's top and left and outside its bottom and
    /// right.</summary>
    private (double Left, double Bottom, double Right, double Top) CollapsedFillInsets(
        (double Left, double Top, double Right, double Bottom) sides)
    {
        var skeleton = CollapsedSkeleton() / 2;
        return (skeleton + sides.Left / 2, sides.Bottom / 2 - skeleton,
            sides.Right / 2 - skeleton, skeleton + sides.Top / 2);
    }

    /// <summary>How much lower than the uniform grid's the lines of a page's first row
    /// start: half the widest rule on that row's top boundary, less the half rule
    /// the uniform grid already allows for.</summary>
    private double CollapsedTopShift(int row, int columns) =>
        CollapsedRulesFor(columns) is { } rules ? (rules.TopEdge(row) - CollapsedSkeleton()) / 2 : 0;

    /// <summary>How far right of the uniform grid's the grid's first line stands: half
    /// the widest rule on its left edge, less the half rule already allowed for.</summary>
    private double CollapsedLeftShift(int columns) =>
        CollapsedRulesFor(columns) is { } rules ? (rules.LeftEdge - CollapsedSkeleton()) / 2 : 0;

    /// <summary>How much wider than its columns the grid's box is: half its widest
    /// rule on each outer edge.</summary>
    private double CollapsedOuterWidth(int columns) =>
        CollapsedRulesFor(columns) is { } rules ? (rules.LeftEdge + rules.RightEdge) / 2 : 0;

    /// <summary>Strokes the boundaries one box of a collapsed grid is responsible
    /// for: the ones it OPENS on (its top, per column, and its left, per row), and
    /// the grid's far edges when it stands on them or the page closes under it.
    ///
    /// <paramref name="xs"/> are the column lines from the box's left one onward;
    /// <paramref name="ys"/> the row lines from its top one down, one per row the box
    /// covers on this page plus the line under the last.
    ///
    /// Where two rules cross, the wider one runs through the crossing and the
    /// narrower one stops at its edge; equal rules both run through.</summary>
    private void DrawCollapsedBoxRules(ContentStreamBuilder builder, CollapsedRules rules,
        int firstRow, int column, int colSpan, IReadOnlyList<double> xs, IReadOnlyList<double> ys,
        bool closesBottom, bool closesRight)
    {
        var rowCount = ys.Count - 1;
        for (var k = 0; k < colSpan; k++)
            DrawCollapsedHorizontal(builder, rules, firstRow, column + k, xs[k], xs[k + 1], ys[0]);
        for (var k = 0; k < rowCount; k++)
            DrawCollapsedVertical(builder, rules, firstRow + k, column, xs[0], ys[k], ys[k + 1]);
        // Past the grid's far edges, and where no cell stands beyond the box (a row the
        // caller left short), no box opens on the boundary: this one draws it.
        var below = firstRow + rowCount;
        for (var k = 0; k < colSpan; k++)
            if (closesBottom || (below < rules.Rows && rules.Owner[below, column + k] is null))
                DrawCollapsedHorizontal(builder, rules, below, column + k, xs[k], xs[k + 1], ys[rowCount],
                    closing: closesBottom);
        var right = column + colSpan;
        for (var k = 0; k < rowCount; k++)
            if (closesRight || (right < rules.Columns && rules.Owner[firstRow + k, right] is null))
                DrawCollapsedVertical(builder, rules, firstRow + k, right, xs[colSpan], ys[k], ys[k + 1]);
    }

    /// <summary>A row-spanning block of a resolved grid, on one page: its fill inside
    /// the half of each of its own rules, then the boundaries it opens on, down every
    /// row it covers here.</summary>
    private void PaintResolvedSpanBlock(ContentStreamBuilder builder, CollapsedRules rules, List<RowSlice> slices,
        double[] colWidths, SpanBlock block, double x, double top, double bottom, Color? fill, bool closesPage)
    {
        var skeleton = CollapsedSkeleton() / 2;
        var colSpan = Math.Max(1, Math.Min(block.ColSpan, colWidths.Length - block.GridCol));
        if (fill is not null && rules.CellSides.TryGetValue(block.Cell, out var sides))
        {
            var (fl, fb, fr, ft) = CollapsedFillInsets(sides);
            var w = GetCellWidth(colWidths, block.GridCol, colSpan);
            builder.SetFillColor(fill);
            builder.Rectangle(x + fl, bottom + fb, w - fl - fr, top - bottom - fb - ft);
            builder.Fill();
        }
        var xs = new double[colSpan + 1];
        xs[0] = x + skeleton;
        for (var k = 0; k < colSpan; k++) xs[k + 1] = xs[k] + colWidths[block.GridCol + k];
        var ys = new List<double>();
        var firstRow = -1;
        foreach (var slice in slices)
        {
            if (slice.RowIndex < block.StartRow || slice.RowIndex >= block.EndRow) continue;
            if (firstRow < 0) { firstRow = slice.RowIndex; ys.Add(slice.TopY - skeleton); }
            ys.Add(slice.TopY - slice.Height - skeleton);
        }
        if (firstRow < 0) return;
        DrawCollapsedBoxRules(builder, rules, firstRow, block.GridCol, colSpan, xs, ys,
            closesBottom: block.EndRow >= Rows.Count || closesPage,
            closesRight: block.GridCol + colSpan >= colWidths.Length);
    }

    /// <summary>A horizontal boundary segment. A boundary that falls INSIDE a box —
    /// a span cut by a page break — has no resolved rule, so the box closes itself
    /// with its own.</summary>
    private void DrawCollapsedHorizontal(ContentStreamBuilder builder, CollapsedRules rules,
        int boundary, int column, double x1, double x2, double y, bool closing = false)
    {
        var segment = boundary <= rules.Rows && column < rules.Columns ? rules.Horizontal[boundary, column] : default;
        if (!segment.Draws && boundary > 0 && boundary < rules.Rows
            && rules.Owner[boundary - 1, column] is { } cut && ReferenceEquals(cut, rules.Owner[boundary, column]))
            segment = CellSideRule(cut, RowOf(cut, boundary - 1), closing ? BorderSide.Bottom : BorderSide.Top);
        if (!segment.Draws) return;
        // (a page that closes under this boundary has no rules below it)
        var leftBelow = closing ? default : rules.VerticalAt(boundary, column);
        var rightBelow = closing ? default : rules.VerticalAt(boundary, column + 1);
        StrokeCollapsedSegment(builder, segment,
            x1 - CornerReach(segment, rules.HorizontalAt(boundary, column - 1), rules.VerticalAt(boundary - 1, column),
                segment, leftBelow, horizontal: true), y,
            x2 + CornerReach(segment, segment, rules.VerticalAt(boundary - 1, column + 1),
                rules.HorizontalAt(boundary, column + 1), rightBelow, horizontal: true), y);
    }

    private void DrawCollapsedVertical(ContentStreamBuilder builder, CollapsedRules rules,
        int row, int boundary, double x, double y1, double y2)
    {
        if (row >= rules.Rows || boundary > rules.Columns) return;
        var segment = rules.Vertical[row, boundary];
        if (!segment.Draws) return;
        StrokeCollapsedSegment(builder, segment,
            x, y1 + CornerReach(segment, rules.HorizontalAt(row, boundary - 1), rules.VerticalAt(row - 1, boundary),
                rules.HorizontalAt(row, boundary), segment, horizontal: false),
            x, y2 - CornerReach(segment, rules.HorizontalAt(row + 1, boundary - 1), segment,
                rules.HorizontalAt(row + 1, boundary), rules.VerticalAt(row + 1, boundary), horizontal: false));
    }

    /// <summary>How far past a crossing a rule runs. Up to four rules meet there --
    /// the one arriving from the left, from above, leaving to the right and leaving
    /// downward -- and the WIDEST owns the crossing, a tie going to the first of them
    /// in that order. The owner, and any rule drawn exactly like it, runs through
    /// the crossing by half the width of the rules crossing its own line; every
    /// other rule stops short of them by the same half.</summary>
    private static double CornerReach(CollapsedSegment self, CollapsedSegment fromLeft, CollapsedSegment fromAbove,
        CollapsedSegment toRight, CollapsedSegment below, bool horizontal)
    {
        var crossing = horizontal ? Math.Max(fromAbove.Width, below.Width) : Math.Max(fromLeft.Width, toRight.Width);
        var owner = fromLeft;
        if (fromAbove.Width > owner.Width) owner = fromAbove;
        if (toRight.Width > owner.Width) owner = toRight;
        if (below.Width > owner.Width) owner = below;
        return DrawnAlike(self, owner) ? crossing / 2 : -crossing / 2;
    }

    /// <summary>True when two rules paint the same stroke: width, colour, dash and style.</summary>
    private static bool DrawnAlike(CollapsedSegment a, CollapsedSegment b)
    {
        if (Math.Abs(a.Width - b.Width) > 1e-6 || !a.Draws || !b.Draws) return false;
        var (ar, ag, ab) = StrokeRgb(a);
        var (br, bg, bb) = StrokeRgb(b);
        return Math.Abs(ar - br) < 1e-6 && Math.Abs(ag - bg) < 1e-6 && Math.Abs(ab - bb) < 1e-6
            && RulePainter.HasDash(a.Side) == RulePainter.HasDash(b.Side)
            && (a.Side?.Style ?? RuleStyle.Solid) == (b.Side?.Style ?? RuleStyle.Solid);
    }

    private static (double R, double G, double B) StrokeRgb(CollapsedSegment segment) =>
        segment.Side?.StrokeColor is { } sc
            ? (sc.R, sc.G, sc.B)
            : (segment.Border!.Color.R / 255.0, segment.Border.Color.G / 255.0, segment.Border.Color.B / 255.0);

    private Row RowOf(Cell cell, int gridRow)
    {
        for (var r = gridRow; r >= 0; r--)
        {
            var row = Rows.At(r);
            for (var ci = 0; ci < row.Cells.Count; ci++)
                if (ReferenceEquals(row.Cells.At(ci), cell)) return row;
        }
        return Rows.At(gridRow);
    }

    /// <summary>One segment, centred on its line, in the rule's own style and dash
    /// (see <see cref="RulePainter.StrokeSegment"/>).</summary>
    private static void StrokeCollapsedSegment(ContentStreamBuilder builder, CollapsedSegment segment,
        double x1, double y1, double x2, double y2) =>
        RulePainter.StrokeSegment(builder, segment.Side, segment.Border!, segment.Width, x1, y1, x2, y2);
}
