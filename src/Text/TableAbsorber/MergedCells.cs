namespace Aspose.Pdf.Text;

public sealed partial class TableAbsorber
{
    // The cells merged over grid positions both across and down.

    /// <summary>Grid positions that together make one ruled cell - a rectangle of positions no rule divides,
    /// with a rule along each of its four sides - each count as a cell position, although no three rules of
    /// their own close them: a cell merged over two rows and two columns, or the middle of a cell across three
    /// columns, would otherwise give its text no cell. The cell is then built as any span is: across its
    /// columns where their rules run through the grid, down its rows where no row rule crosses it.</summary>
    private static void AcceptMergedCells(RegionTableDetectState dr)
    {
        var root = new int[dr.nRows * dr.nCols];
        for (var i = 0; i < root.Length; i++) root[i] = i;
        int Find(int i) { while (root[i] != i) i = root[i] = root[root[i]]; return i; }
        void Join(int a, int b) => root[Find(a)] = Find(b);
        for (var r = 0; r < dr.nRows; r++)
            for (var c = 0; c < dr.nCols; c++)
            {
                if (c + 1 < dr.nCols && !ColumnRuleInBand(dr.vEdges, dr.colBounds[c + 1], dr.rowBounds[r], dr.rowBounds[r + 1]))
                    Join(r * dr.nCols + c, r * dr.nCols + c + 1);
                if (r + 1 < dr.nRows && !RowRuleAcross(dr.hEdges, dr.rowBounds[r + 1], dr.colBounds[c], dr.colBounds[c + 1]))
                    Join(r * dr.nCols + c, (r + 1) * dr.nCols + c);
            }
        foreach (var group in Enumerable.Range(0, root.Length).GroupBy(Find))
        {
            var positions = group.Select(i => (r: i / dr.nCols, c: i % dr.nCols)).ToList();
            if (positions.Count < 2 || positions.All(p => dr.valid[p.r, p.c])) continue;
            int r0 = positions.Min(p => p.r), r1 = positions.Max(p => p.r), c0 = positions.Min(p => p.c), c1 = positions.Max(p => p.c);
            if (positions.Count != (r1 - r0 + 1) * (c1 - c0 + 1)) continue;
            if (CountSides(dr.hEdges, dr.vEdges, dr.rowBounds[r0], dr.rowBounds[r1 + 1], dr.colBounds[c0], dr.colBounds[c1 + 1]) < CellSides) continue;
            foreach (var (r, c) in positions.Where(p => !dr.valid[p.r, p.c]))
            {
                dr.valid[r, c] = true;
                dr.validCount++;
            }
        }
    }

    /// <summary>Whether a column rule at <paramref name="x"/> runs through most of the band between the two heights.</summary>
    private static bool ColumnRuleInBand(List<VEdge> vEdges, double x, double yBot, double yTop) =>
        vEdges.Any(e => Math.Abs(e.X - x) <= EdgeTol && Math.Min(e.Y2, yTop) - Math.Max(e.Y1, yBot) >= (yTop - yBot) * SideCoverShare);

    /// <summary>Whether a row rule at <paramref name="y"/> runs along most of the stretch between the two x.</summary>
    private static bool RowRuleAcross(List<HEdge> hEdges, double y, double xLeft, double xRight) =>
        hEdges.Any(e => Math.Abs(e.Y - y) <= EdgeTol && Math.Min(e.X2, xRight) - Math.Max(e.X1, xLeft) >= (xRight - xLeft) * SideCoverShare);
}
