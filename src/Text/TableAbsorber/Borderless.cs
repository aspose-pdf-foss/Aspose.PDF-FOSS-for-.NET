namespace Aspose.Pdf.Text;

public sealed partial class TableAbsorber
{
    // ── Borderless (flow-engine) table recognition ───────────────────────────
    //
    // With UseFlowEngine the absorber also recognises tables that have no rules:
    // runs of consecutive text lines whose gap-separated cells line up in columns.
    // The model, fitted to the expected row counts on the two probed fixtures:
    //  * a line splits into CELLS at any horizontal gap wider than
    //    BorderlessCellGapEm of the line's font size (a word space is ~0.3 em, a
    //    column gap at least one);
    //  * a line with at least two cells is TABULAR; a region is a run of tabular
    //    lines in which each line's cells overlap the columns seen so far (a cell
    //    bridging two columns merges them);
    //  * a tabular line filling fewer than half the region's columns continues the
    //    row above it (wrapped cell content) whether or not it aligns; it does not
    //    open a row;
    //  * single-cell lines (titles, prose) bound the region on either side.

    /// <summary>Horizontal gap, in em of the line's font size, that separates two
    /// cells on a line. A word space is 0.25-0.35 em; the narrowest probed column
    /// gap that splits is 0.63 em ("Column1" / "Column2" headings).</summary>
    private const double BorderlessCellGapEm = 0.5;
    /// <summary>Two baselines closer than this are one line.</summary>
    private const double BorderlessLineTolerance = 2.0;
    /// <summary>A table needs at least this many rows.</summary>
    private const int BorderlessMinRows = 2;

    private sealed class BorderlessCell
    {
        public List<TextFragment> Fragments = [];
        public double X1, X2, Y1, Y2;
    }

    private sealed class BorderlessLine
    {
        public double Baseline;
        public double FontSize;
        public List<BorderlessCell> Cells = [];
    }

    /// <summary>Tables recognised from text alignment alone among
    /// <paramref name="fragments"/> (page fragments outside every ruled table).</summary>
    private static List<AbsorbedTable> DetectBorderlessTables(List<TextFragment> fragments)
    {
        var lines = BuildBorderlessLines(fragments);
        if (TableDebug)
            foreach (var l in lines)
                Console.Error.WriteLine($"[flow] y={l.Baseline:F1} fs={l.FontSize:F1} cells={l.Cells.Count}: {string.Join(" | ", l.Cells.Select(c => $"{c.X1:F0}-{c.X2:F0}"))}");
        var tables = new List<AbsorbedTable>();
        var i = 0;
        while (i < lines.Count)
        {
            if (lines[i].Cells.Count < 2) { i++; continue; }
            // Open a region at this tabular line and extend it downward.
            var columns = new List<(double x1, double x2)>();
            var rows = new List<List<BorderlessCell>>();
            var j = i;
            while (j < lines.Count && lines[j].Cells.Count >= 2)
            {
                var line = lines[j];
                // A sparse line continues the row above whatever its cells align
                // with (a right-aligned "1  0  0" under a "Day Column1 ..." header
                // sits between the header's columns and still belongs to it); its
                // cells then widen or add columns for the lines below.
                var sparse = rows.Count > 0 && line.Cells.Count * 2 < columns.Count;
                if (!sparse && rows.Count > 0 && !LineFitsColumns(line, columns)) break;
                foreach (var c in line.Cells) AddColumn(columns, c.X1, c.X2);
                if (sparse) rows[^1].AddRange(line.Cells);
                else rows.Add(new List<BorderlessCell>(line.Cells));
                j++;
            }
            if (rows.Count >= BorderlessMinRows)
                tables.Add(BuildBorderlessTable(rows, columns));
            i = Math.Max(j, i + 1);
        }
        return tables;
    }

    private static List<BorderlessLine> BuildBorderlessLines(List<TextFragment> fragments)
    {
        var ordered = fragments
            .Where(f => f.Rectangle is not null && !string.IsNullOrWhiteSpace(f.Text))
            .OrderByDescending(f => f.Rectangle!.LLY)
            .ThenBy(f => f.Rectangle!.LLX)
            .ToList();
        var lines = new List<BorderlessLine>();
        var groups = new List<List<TextFragment>>();
        foreach (var f in ordered)
        {
            if (groups.Count > 0 && Math.Abs(groups[^1][0].Rectangle!.LLY - f.Rectangle!.LLY) <= BorderlessLineTolerance)
                groups[^1].Add(f);
            else
                groups.Add([f]);
        }
        foreach (var g in groups)
        {
            var line = new BorderlessLine
            {
                Baseline = g[0].Rectangle!.LLY,
                FontSize = g.Max(f => f.TextState.FontSize > 0 ? f.TextState.FontSize : 12),
            };
            var gap = BorderlessCellGapEm * line.FontSize;
            BorderlessCell? cell = null;
            foreach (var f in g.OrderBy(f => f.Rectangle!.LLX))
            {
                var r = f.Rectangle!;
                if (cell is null || r.LLX - cell.X2 > gap)
                {
                    cell = new BorderlessCell { X1 = r.LLX, X2 = r.URX, Y1 = r.LLY, Y2 = r.URY };
                    line.Cells.Add(cell);
                }
                cell.Fragments.Add(f);
                cell.X2 = Math.Max(cell.X2, r.URX);
                cell.Y1 = Math.Min(cell.Y1, r.LLY);
                cell.Y2 = Math.Max(cell.Y2, r.URY);
            }
            lines.Add(line);
        }
        return lines;
    }

    /// <summary>At least half the line's cells overlap a known column.</summary>
    private static bool LineFitsColumns(BorderlessLine line, List<(double x1, double x2)> columns)
    {
        var hits = line.Cells.Count(c => columns.Any(col => c.X2 > col.x1 && c.X1 < col.x2));
        return hits * 2 >= line.Cells.Count;
    }

    /// <summary>Add a cell extent to the column set, merging every column it bridges.</summary>
    private static void AddColumn(List<(double x1, double x2)> columns, double x1, double x2)
    {
        var lo = x1; var hi = x2;
        for (var k = columns.Count - 1; k >= 0; k--)
        {
            var (cx1, cx2) = columns[k];
            if (hi > cx1 && lo < cx2)
            {
                lo = Math.Min(lo, cx1); hi = Math.Max(hi, cx2);
                columns.RemoveAt(k);
            }
        }
        columns.Add((lo, hi));
        columns.Sort((a, b) => a.x1.CompareTo(b.x1));
    }

    private static AbsorbedTable BuildBorderlessTable(List<List<BorderlessCell>> rows,
        List<(double x1, double x2)> columns)
    {
        var absorbedRows = new List<AbsorbedRow>();
        foreach (var row in rows)
        {
            var rowY1 = row.Min(c => c.Y1);
            var rowY2 = row.Max(c => c.Y2);
            var cells = new List<AbsorbedCell>();
            foreach (var (cx1, cx2) in columns)
            {
                var inCol = row.Where(c => c.X2 > cx1 && c.X1 < cx2).ToList();
                if (inCol.Count == 0) continue;
                var frags = inCol.SelectMany(c => c.Fragments)
                    .OrderByDescending(f => f.Rectangle!.URY).ThenBy(f => f.Rectangle!.LLX).ToList();
                var cell = new AbsorbedCell
                {
                    Text = string.Join(" ", frags.Select(f => f.Text)).Trim(),
                    Rect = new Rectangle(inCol.Min(c => c.X1), rowY1, inCol.Max(c => c.X2), rowY2),
                };
                cell.TextFragments.Inner.AddRange(frags);
                cells.Add(cell);
            }
            absorbedRows.Add(new AbsorbedRow { Cells = cells });
        }
        var rect = new Rectangle(
            absorbedRows.Min(r => r.Rectangle!.LLX), absorbedRows.Min(r => r.Rectangle!.LLY),
            absorbedRows.Max(r => r.Rectangle!.URX), absorbedRows.Max(r => r.Rectangle!.URY));
        return new AbsorbedTable { Rows = absorbedRows, Rect = rect };
    }
}
