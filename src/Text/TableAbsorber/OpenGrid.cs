namespace Aspose.Pdf.Text;

public sealed partial class TableAbsorber
{
    // How a lattice of rules closes into cells where the drawing leaves it open:
    // which lattices can bound a cell at all, and the cells on a side no rule closes.

    /// <summary>A lone column rule makes a grid only when at least this many row
    /// rules cross it: two rows of two cells. A single rule between a header line
    /// and a footer line is a page's column separator, not a table.</summary>
    private const int MinRowRulesOnLoneColumnRule = 3;

    /// <summary>Sides a cell needs, drawn or closed by the open-side boundary.</summary>
    private const int CellSides = 3;

    /// <summary>Whether the rules can bound a cell: two row rules and two column
    /// rules, or one column rule crossed by enough row rules to stand as the
    /// separator of an open-sided grid.</summary>
    private static bool CanBoundCells(List<HEdge> hEdges, List<VEdge> vEdges)
    {
        if (hEdges.Count < 2 || vEdges.Count < 1) return false;
        if (vEdges.Count >= 2) return true;
        var rule = vEdges[0];
        var crossing = hEdges.Where(he =>
            rule.X >= he.X1 - EdgeTol && rule.X <= he.X2 + EdgeTol
            && he.Y >= rule.Y1 - EdgeTol && he.Y <= rule.Y2 + EdgeTol).Select(he => he.Y).ToList();
        return ClusterValues(crossing).Count >= MinRowRulesOnLoneColumnRule;
    }

    /// <summary>A grid position on a side the rules leave open has that side closed
    /// by the synthesised boundary: it is a cell when its drawn sides and its closed
    /// sides together bound it. Only adds cells; what the drawn sides alone made a
    /// cell stays one.</summary>
    private static void AcceptCellsOnOpenSides(RegionTableDetectState dr)
    {
        if (!dr.openLeft && !dr.openRight && !dr.openTop && !dr.openBottom) return;
        // A cell joins only beside a cell already there, so the pass repeats while it adds.
        for (var added = true; added;)
        {
            added = false;
            for (var r = 0; r < dr.nRows; r++)
                for (var c = 0; c < dr.nCols; c++)
                    if (!dr.valid[r, c] && ClosesOnOpenSide(dr, r, c))
                    {
                        dr.valid[r, c] = true;
                        dr.validCount++;
                        added = true;
                    }
        }
    }

    private static bool ClosesOnOpenSide(RegionTableDetectState dr, int r, int c)
    {
        var closed = (c == 0 && dr.openLeft ? 1 : 0) + (c == dr.nCols - 1 && dr.openRight ? 1 : 0)
            + (r == 0 && dr.openBottom ? 1 : 0) + (r == dr.nRows - 1 && dr.openTop ? 1 : 0);
        if (closed == 0) return false;
        var besideCell = (r > 0 && dr.valid[r - 1, c]) || (r < dr.nRows - 1 && dr.valid[r + 1, c])
            || (c > 0 && dr.valid[r, c - 1]) || (c < dr.nCols - 1 && dr.valid[r, c + 1]);
        if (!besideCell) return false;
        double xLeft = dr.colBounds[c], xRight = dr.colBounds[c + 1];
        double yBot = dr.rowBounds[r], yTop = dr.rowBounds[r + 1];
        if (xRight - xLeft < MinCellW || yTop - yBot < MinCellH) return false;
        return CountSides(dr.hEdges, dr.vEdges, yBot, yTop, xLeft, xRight) + closed >= CellSides;
    }

    /// <summary>Whether every column rule that meets the outermost row rule at
    /// <paramref name="rule"/> runs on to the rules' end at <paramref name="end"/>:
    /// a row of cells without its outer rule, not one line sticking out of the grid.</summary>
    private static bool ColumnRulesRunPast(List<VEdge> vEdges, double rule, double end, bool upward)
    {
        var any = false;
        foreach (var ve in vEdges)
        {
            if (ve.Y1 > rule + EdgeTol || ve.Y2 < rule - EdgeTol) continue;
            any = true;
            if (!vEdges.Any(other => Math.Abs(other.X - ve.X) <= EdgeTol
                    && (upward ? other.Y2 >= end - EdgeTol : other.Y1 <= end + EdgeTol)))
                return false;
        }
        return any;
    }
}
