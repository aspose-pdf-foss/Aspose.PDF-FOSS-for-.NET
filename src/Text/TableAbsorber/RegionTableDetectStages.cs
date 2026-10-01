using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TableAbsorber
{
    /// <summary>The stages of the region table detect: marking the valid grid cells, relaxing the side threshold, flood-filling the components, and collapsing a fragmented grid into one table.</summary>
    private static void CollapseFragmentedTables(RegionTableDetectState dr)
    {
        if (dr.tables.Count > 6 || (dr.tables.Count > 1 && dr.tables.All(t => t.Rows.Count <= 2)))
        {
            var allValid = new List<(int r, int c)>();
            for (var r = 0; r < dr.nRows; r++)
                for (var c = 0; c < dr.nCols; c++)
                    if (dr.valid[r, c]) allValid.Add((r, c));
            if (allValid.Count >= MinCells)
            {
                var bigTables = BuildTablesFromComponent(allValid, dr.rowBounds, dr.colBounds, dr.runs, dr.hEdges, dr.vEdges);
                if (bigTables.Count > 0 && bigTables.Sum(t => t.Rows.Sum(rw => rw.Cells.Count)) > dr.tables.Sum(t => t.Rows.Sum(rw => rw.Cells.Count)) / 2)
                    dr.tables = bigTables;
            }
        }
    }

    /// <summary></summary>
    private static void FloodFillTableComponents(RegionTableDetectState dr)
    {
        for (var r = 0; r < dr.nRows; r++)
        {
            for (var c = 0; c < dr.nCols; c++)
            {
                if (!dr.valid[r, c] || dr.visited[r, c]) continue;
                var component = FloodFill(dr.valid, dr.visited, r, c, dr.nRows, dr.nCols);
                if (component.Count < MinCells) continue;
                dr.tables.AddRange(BuildTablesFromComponent(component, dr.rowBounds, dr.colBounds, dr.runs, dr.hEdges, dr.vEdges));
            }
        }
    }

    /// <summary></summary>
    private static void RelaxCellSideThreshold(RegionTableDetectState dr)
    {
        for (int minSides = 2; minSides >= 1 && dr.validCount * 2 < dr.totalCells; minSides--)
        {
            dr.validCount = 0;
            for (var r = 0; r < dr.nRows; r++)
            {
                var yBot = dr.rowBounds[r];
                var yTop = dr.rowBounds[r + 1];
                for (var c = 0; c < dr.nCols; c++)
                {
                    var xLeft = dr.colBounds[c];
                    var xRight = dr.colBounds[c + 1];
                    var sxL = dr.realColBounds.Where(b => b <= xLeft + 0.01).DefaultIfEmpty(xLeft).Max();
                    var sxR = dr.realColBounds.Where(b => b >= xRight - 0.01).DefaultIfEmpty(xRight).Min();
                    var syB = dr.realRowBounds.Where(b => b <= yBot + 0.01).DefaultIfEmpty(yBot).Max();
                    var syT = dr.realRowBounds.Where(b => b >= yTop - 0.01).DefaultIfEmpty(yTop).Min();
                    dr.valid[r, c] =
                        (xRight - xLeft) >= MinCellW &&
                        (yTop - yBot) >= MinCellH &&
                        (minSides == 0 || CountSides(dr.hEdges, dr.vEdges, syB, syT, sxL, sxR) >= minSides);
                    if (dr.valid[r, c]) dr.validCount++;
                }
            }
        }
    }

    /// <summary></summary>
    private static void MarkValidGridCells(RegionTableDetectState dr)
    {
        for (var r = 0; r < dr.nRows; r++)
        {
            var yBot = dr.rowBounds[r];
            var yTop = dr.rowBounds[r + 1];
            for (var c = 0; c < dr.nCols; c++)
            {
                var xLeft = dr.colBounds[c];
                var xRight = dr.colBounds[c + 1];
                // Side-count against the enclosing REAL (edge-derived) cell so a
                // text-inferred boundary does not orphan its sub-cells.
                var sxL = dr.realColBounds.Where(b => b <= xLeft + 0.01).DefaultIfEmpty(xLeft).Max();
                var sxR = dr.realColBounds.Where(b => b >= xRight - 0.01).DefaultIfEmpty(xRight).Min();
                var syB = dr.realRowBounds.Where(b => b <= yBot + 0.01).DefaultIfEmpty(yBot).Max();
                var syT = dr.realRowBounds.Where(b => b >= yTop - 0.01).DefaultIfEmpty(yTop).Min();
                dr.valid[r, c] =
                    (xRight - xLeft) >= MinCellW &&
                    (yTop - yBot) >= MinCellH &&
                    CountSides(dr.hEdges, dr.vEdges, syB, syT, sxL, sxR) >= 3;
                if (dr.valid[r, c]) dr.validCount++;
            }
        }
    }
}
