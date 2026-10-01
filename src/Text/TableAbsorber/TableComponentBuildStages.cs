using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TableAbsorber
{
// The stages of building tables from one grid component: the boundary cover test, one row, one cell and the section assembly.
    private static bool BoundaryCovered(TableComponentBuildState bt, double y, double xL, double xR)
    {
        if (bt.hEdges is null) return true;
        var need = Math.Min((xR - xL) * 0.5, (xR - xL) - 2 * EdgeTol);
        foreach (var he in bt.hEdges)
        {
            if (Math.Abs(he.Y - y) > EdgeTol) continue;
            var overlap = Math.Min(he.X2, xR) - Math.Max(he.X1, xL);
            if (overlap >= need) return true;
        }
        return false;
    }

    /// <summary></summary>
    private static void AssembleTableSections(TableComponentBuildState bt)
    {
        foreach (var section in bt.sections)
        {
            if (section.Count < 1) continue;
            // Drop always-empty sandwiched columns
            var cleaned = DropEmptyColumns(section);
            // Single-column bordered grids are real tables
            // (stacked label/value panels, report frames) - requiring >= 2 cells
            // per row would erase them.
            if (cleaned.Count == 0) continue;
            // ...but a lone bordered box holding no text at all is page
            // decoration (a title frame, a signature box), not a table.
            if (cleaned.Count == 1 && cleaned[0].Cells.Count == 1
                && string.IsNullOrWhiteSpace(cleaned[0].Cells[0].Text))
                continue;

            // Compute bounding rect
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var row in cleaned)
                foreach (var cell in row.Cells)
                {
                    if (cell.Rect is null) continue;
                    if (cell.Rect.LLX < minX) minX = cell.Rect.LLX;
                    if (cell.Rect.LLY < minY) minY = cell.Rect.LLY;
                    if (cell.Rect.URX > maxX) maxX = cell.Rect.URX;
                    if (cell.Rect.URY > maxY) maxY = cell.Rect.URY;
                }
            var rect = Compat.IsFinite(minX) ? new Rectangle(minX, minY, maxX, maxY) : null;
            bt.result.Add(new AbsorbedTable { Rows = cleaned, Rect = rect });
        }
    }

    /// <summary></summary>
    private static bool BuildComponentRow(TableComponentBuildState bt, int r)
    {
        bt.yBot = bt.rowBounds[r];
        bt.yTop = bt.rowBounds[r + 1];
        bt.colsInRow = bt.component.Where(p => p.r == r).Select(p => p.c).OrderBy(c => c).ToList();
        if (bt.colsInRow.Count == 0) return true;

        bt.gridMinCol = int.MaxValue; var gridMaxCol = int.MinValue;
        foreach (var p in bt.component)
        {
            if (p.c < bt.gridMinCol) bt.gridMinCol = p.c;
            if (p.c > gridMaxCol) gridMaxCol = p.c;
        }
        bt.spanning = bt.colsInRow.Count == 2
            && bt.colsInRow[0] == bt.gridMinCol && bt.colsInRow[1] == gridMaxCol
            && gridMaxCol - bt.gridMinCol >= 2
            // ...and no interior vertical rule CROSSES the band: crossing
            // verticals mean the sparse row is the pass-through interior of
            // row-span cells, not a caption spanning the grid.
            && (bt.vEdges is null || !bt.vEdges.Any(ve =>
                ve.X > bt.colBounds[bt.gridMinCol] + EdgeTol
                && ve.X < bt.colBounds[gridMaxCol + 1] - EdgeTol
                && Math.Min(ve.Y2, bt.yTop) - Math.Max(ve.Y1, bt.yBot) >= (bt.yTop - bt.yBot) * 0.5));
        bt.cellSpans = bt.spanning
            ? new List<(int cFrom, int cTo)> { (bt.colsInRow[0], bt.colsInRow[^1]) }
            : bt.colsInRow.Select(c => (cFrom: c, cTo: c)).ToList();

        bt.cells = new List<AbsorbedCell>();
        foreach (var (cFrom, cTo) in bt.cellSpans)
        {
            if (!BuildComponentCell(bt, r, cFrom, cTo)) break;
        }
        if (bt.cells.Count == 0) return true;
        bt.allRows.Add(new AbsorbedRow { Cells = bt.cells });
        return true;
    }

    /// <summary></summary>
    private static bool BuildComponentCell(TableComponentBuildState bt, int r, int cFrom, int cTo)
    {
        if (bt.consumed.Contains((r, cFrom))) return true;
        var xLeft = bt.colBounds[cFrom];
        var xRight = bt.colBounds[cTo + 1];
        if ((xRight - xLeft) < MinCellW || (bt.yTop - bt.yBot) < MinCellH) return true;

        // Extend a single-column cell down across uncovered boundaries
        // (row-span); the swallowed grid positions emit no cell of their own.
        var cellBot = bt.yBot;
        if (cFrom == cTo)
        {
            var minRowIdx = bt.rowIndices[0];
            var rCur = r;
            // Walk down through grid rows regardless of their own side
            // validation - an interior position under an uncovered
            // boundary is the INSIDE of this tall cell.
            while (rCur - 1 >= minRowIdx
                && !bt.consumed.Contains((rCur - 1, cFrom))
                && !BoundaryCovered(bt, bt.rowBounds[rCur], xLeft, xRight))
            {
                rCur--;
                bt.consumed.Add((rCur, cFrom));
                cellBot = bt.rowBounds[rCur];
            }
        }

        // Snap the cell's X sides from the CLUSTERED boundary (an average
        // over nearby parallel rules) to the ACTUAL rule bounding this
        // cell — the drawn vertical overlapping this row band nearest the
        // boundary. Reported cell geometry follows the ink, not the
        // cluster average.
        var xLeftSnap = SnapToVEdge(xLeft, cellBot, bt.yTop, bt.vEdges) ?? xLeft;
        var xRightSnap = SnapToVEdge(xRight, cellBot, bt.yTop, bt.vEdges) ?? xRight;
        if (xRightSnap - xLeftSnap < MinCellW) { xLeftSnap = xLeft; xRightSnap = xRight; }

        var cellRect = new Rectangle(xLeftSnap, cellBot, xRightSnap, bt.yTop);
        // Match text runs whose CENTER X falls within cell, and Y is within cell
        var cellRuns = bt.runs.Where(run =>
        {
            var runCenterX = run.X + run.W / 2;
            return runCenterX >= cellRect.LLX - 2 && runCenterX <= cellRect.URX + 2 &&
                   run.Y >= cellRect.LLY - 2 && run.Y <= cellRect.URY + 2;
        }).ToList();
        // Merge adjacent same-line text runs into single fragments.
        // CID fonts often produce one run per character; merge them into words.
        var mergedRuns = MergeCellRuns(cellRuns);
        var cellText = string.Join(" ", mergedRuns.Select(run => run.Text)).Trim();
        var frags = new List<TextFragment>();
        frags.AddRange(mergedRuns.Select(run =>
        {
            var frag = new TextFragment(run.Text) { Position = new Position(run.X, run.Y) };
            // The fragment and its single segment carry the RUN's page box —
            // the fragment's right border and its last segment's right
            // border are the same drawn edge.
            var runRect = new Rectangle(run.X, run.Y, run.X + run.W, run.Y + run.H);
            frag.Rectangle = runRect;
            frag.Segments[1].Rectangle = runRect;
            return frag;
        }));
        bt.cells.Add(new AbsorbedCell { Text = cellText, Rect = cellRect, TextFragments = AbsorbedCell.ToCollection(frags) });
        return true;
    }
}
