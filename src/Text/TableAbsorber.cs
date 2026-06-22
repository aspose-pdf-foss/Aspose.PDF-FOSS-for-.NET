using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

/// <summary>
/// Represents a cell in an absorbed table.
/// </summary>
public sealed class AbsorbedCell : IComparable<AbsorbedCell>
{
    public string Text { get; init; } = "";
    public Rectangle? Rect { get; init; }
    /// <summary>Alias for Rect.</summary>
    public Rectangle? Rectangle => Rect;
    /// <summary>Text fragments in the cell. 1-based indexer.</summary>
    public TextFragmentCollection TextFragments { get; internal init; } = new();

    /// <summary>Per-cell border info; null when the cell uses the table-default border. Stored only.</summary>
    public BorderInfo? BorderInfo { get; internal init; }

    /// <summary>How many columns this cell spans. Stored only.</summary>
    public int ColSpan { get; internal init; } = 1;

    /// <summary>Order cells top-down then left-to-right by their bounding rectangle.</summary>
    public int CompareTo(AbsorbedCell? other)
    {
        if (other is null) return 1;
        if (Rect is null || other.Rect is null) return 0;
        // PDF Y grows up — top-first means larger URY first.
        var dy = other.Rect.URY.CompareTo(Rect.URY);
        return dy != 0 ? dy : Rect.LLX.CompareTo(other.Rect.LLX);
    }

    internal static TextFragmentCollection ToCollection(IEnumerable<TextFragment> items)
    {
        var c = new TextFragmentCollection();
        foreach (var f in items) c.Add(f);
        return c;
    }
}

/// <summary>Read-only list with 1-based indexer, matching the public API.</summary>
public sealed class OneBasedList<T>(IReadOnlyList<T> inner) : IReadOnlyList<T>
{
    public T this[int index] => inner[index - 1];
    public int Count => inner.Count;
    public IEnumerator<T> GetEnumerator() => inner.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Represents a row in an absorbed table.
/// </summary>
public sealed class AbsorbedRow : IComparable<AbsorbedRow>
{
    public IReadOnlyList<AbsorbedCell> Cells { get; init; } = [];

    /// <summary>Mutable cell list (Aspose.PDF for .NET-shape).</summary>
    public IList<AbsorbedCell> CellList
    {
        get
        {
            if (Cells is IList<AbsorbedCell> list) return list;
            return new List<AbsorbedCell>(Cells);
        }
    }

    /// <summary>Bounding rectangle of this row (computed from its cells).</summary>
    public Rectangle? Rectangle
    {
        get
        {
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
            double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
            bool any = false;
            foreach (var c in Cells)
            {
                if (c.Rect is null) continue;
                if (c.Rect.LLX < minX) minX = c.Rect.LLX;
                if (c.Rect.LLY < minY) minY = c.Rect.LLY;
                if (c.Rect.URX > maxX) maxX = c.Rect.URX;
                if (c.Rect.URY > maxY) maxY = c.Rect.URY;
                any = true;
            }
            return any ? new Rectangle(minX, minY, maxX, maxY) : null;
        }
    }

    public int CompareTo(AbsorbedRow? other)
    {
        if (other is null) return 1;
        var a = Rectangle; var b = other.Rectangle;
        if (a is null || b is null) return 0;
        // Top-down in PDF coords means highest URY first.
        return b.URY.CompareTo(a.URY);
    }
}

/// <summary>
/// Represents a table detected on a PDF page.
/// </summary>
public sealed class AbsorbedTable : IComparable<AbsorbedTable>
{
    public IReadOnlyList<AbsorbedRow> Rows { get; init; } = [];

    /// <summary>Mutable row list (Aspose.PDF for .NET-shape).</summary>
    public IList<AbsorbedRow> RowList
    {
        get
        {
            if (Rows is IList<AbsorbedRow> list) return list;
            return new List<AbsorbedRow>(Rows);
        }
    }

    public Rectangle? Rect { get; init; }
    /// <summary>Alias for Rect.</summary>
    public Rectangle? Rectangle => Rect;

    /// <summary>The 1-based page number this table was detected on (0 when unset).</summary>
    public int PageNum { get; internal init; }

    public int CompareTo(AbsorbedTable? other)
    {
        if (other is null) return 1;
        if (PageNum != other.PageNum) return PageNum.CompareTo(other.PageNum);
        if (Rect is null || other.Rect is null) return 0;
        return other.Rect.URY.CompareTo(Rect.URY);
    }
}

/// <summary>
/// Detects and extracts tables from PDF pages by analyzing text positions
/// and line drawing operations.
/// </summary>
public sealed class TableAbsorber
{
    private readonly List<AbsorbedTable> _tables = [];
    private Page? _page;

    /// <summary>Construct with default search options.</summary>
    public TableAbsorber() { }

    /// <summary>Construct with the given text-search options.</summary>
    public TableAbsorber(TextSearchOptions textSearchOptions)
    {
        TextSearchOptions = textSearchOptions;
    }

    /// <summary>Detected tables.</summary>
    public IReadOnlyList<AbsorbedTable> Tables => _tables;

    /// <summary>Detected tables (Aspose.PDF for .NET-parity surface — returns the mutable backing list).</summary>
    public IList<AbsorbedTable> TableList => _tables;

    /// <summary>Search options controlling case sensitivity, regex use, and bounded-search rectangle. Stored only.</summary>
    public TextSearchOptions? TextSearchOptions { get; set; }

    /// <summary>Whether the flow engine is used during table detection. Stored only.</summary>
    public bool UseFlowEngine { get; set; }

    /// <summary>Remove a table from the page content stream and the detected list.</summary>
    public void Remove(AbsorbedTable table)
    {
        _tables.Remove(table);
        if (_page is not null && table.Rect is not null)
            RemoveTableContent(_page, table.Rect);
    }

    /// <summary>Replace <paramref name="oldTable"/> on <paramref name="page"/> with <paramref name="newTable"/>.
    /// Stored only — the FOSS engine doesn't yet rewrite a content stream to inject a generated table.</summary>
    public void Replace(Page page, AbsorbedTable oldTable, Aspose.Pdf.Table newTable)
    {
        if (page is null) throw new ArgumentNullException(nameof(page));
        if (oldTable is null) throw new ArgumentNullException(nameof(oldTable));
        if (oldTable.Rect is not null) RemoveTableContent(page, oldTable.Rect);
    }

    /// <summary>Visit every page in <paramref name="pdf"/> and detect tables.</summary>
    public void Visit(Document pdf)
    {
        if (pdf is null) throw new ArgumentNullException(nameof(pdf));
        foreach (var page in pdf.Pages) Visit(page);
    }

    /// <summary>Visit a page and detect tables.</summary>
    public void Visit(Page page)
    {
        _page = page;
        var reader = page.Reader;
        var contentStreams = GetContentStreams(page.Dict, reader);
        var fonts = ResolveFonts(page.Dict, reader);

        var textRuns = new List<TextRun>();
        var hEdges = new List<HEdge>();
        var vEdges = new List<VEdge>();

        var (rA, rB, rC, rD, rE, rF) = PageRotationCtm(page);

        // Concatenate all content streams before processing, because PDF spec
        // treats multiple content streams as one continuous stream — text state
        // (BT/ET blocks, Tm, Td) carries over between streams.
        var combined = ConcatenateStreams(contentStreams);
        var xobjects = TextAbsorber.ResolveXObjects(page.Dict, reader);
        ExtractTextAndLines(combined, fonts, reader, textRuns, hEdges, vEdges, rA, rB, rC, rD, rE, rF, xobjects);

        if (textRuns.Count == 0 && hEdges.Count == 0 && vEdges.Count == 0) return;

        var tables = DetectTables(textRuns, hEdges, vEdges);
        _tables.AddRange(tables);
    }

    // ── Internal types ──────────────────────────────────────────────────

    private readonly record struct TextRun(string Text, double X, double Y, double W, double H);
    private readonly record struct HEdge(double Y, double X1, double X2);
    private readonly record struct VEdge(double X, double Y1, double Y2);

    // ── Constants (matching TypeScript implementation) ────────────────────

    /// <summary>Maximum gap (points) between two edge positions for them to be clustered into one boundary.
    /// Set higher than TS (3) because C# processes raw m/l operators which produce double-border pairs ~5pt apart.</summary>
    private const double EdgeTol = 6;
    /// <summary>Minimum cell width in points — prevents thin decorative rules from forming "cells".</summary>
    private const double MinCellW = 10;
    /// <summary>Minimum cell height in points.</summary>
    private const double MinCellH = 4;
    /// <summary>Minimum number of valid cells required to report a detected table.</summary>
    private const int MinCells = 2;
    /// <summary>Maximum width/height (pt) for a thin filled rect to be treated as a line border.</summary>
    private const double LineRectThreshold = 3;

    // ── Detection pipeline (matches TypeScript algorithm) ────────────────

    private static List<AbsorbedTable> DetectTables(List<TextRun> runs, List<HEdge> hEdges, List<VEdge> vEdges)
    {
        if (hEdges.Count < 2 || vEdges.Count < 2)
            return DetectTablesFromText(runs);

        // 1. Cluster edge positions into row/column boundary values
        var rowBounds = ClusterValues(hEdges.Select(e => e.Y).ToList());
        rowBounds.Sort();
        var colBounds = ClusterValues(vEdges.Select(e => e.X).ToList());
        colBounds.Sort();

        // Merge nearby column boundaries that form thin "double-border" gaps.
        // PDFs often draw left+right borders of adjacent columns as separate lines
        // ~20-30pt apart, creating phantom thin columns with no content.
        MergeNearbyBoundaries(colBounds, MinCellW * 3);
        MergeNearbyBoundaries(rowBounds, MinCellH * 2.5);

        // When there are very few V edges (2-3 boundaries = 1-2 columns) but many text runs,
        // detect additional column boundaries from text X-position clustering within wide columns.
        // Also extend the grid to include text just beyond the right edge.
        if (colBounds.Count >= 2 && colBounds.Count <= 3 && runs.Count > 4)
        {
            var tableLeft = colBounds[0];
            var tableRight = colBounds[^1];
            var tableYMin = rowBounds[0];
            var tableYMax = rowBounds[^1];

            // Include text runs slightly beyond the right edge that form a consistent column.
            // Only extend if the nearby text represents a majority of rows (true 3rd column).
            var nearbyRightRuns = runs.Where(r =>
                r.X > tableRight && r.X < tableRight + EdgeTol * 5 &&
                r.Y >= tableYMin - EdgeTol && r.Y <= tableYMax + EdgeTol).ToList();
            var nearbyYPositions = nearbyRightRuns.Select(r => Math.Round(r.Y / 5) * 5).Distinct().Count();
            var totalRows = rowBounds.Count - 1;
            if (nearbyRightRuns.Count >= 5 && nearbyYPositions >= totalRows / 2 && totalRows >= 4)
            {
                // Extend the right column boundary to include this text
                var maxTextRight = nearbyRightRuns.Max(r => r.X + r.W);
                colBounds[^1] = maxTextRight + EdgeTol;
                colBounds.Sort();
            }

            var additionalBounds = new List<double>();
            for (var ci = 0; ci < colBounds.Count - 1; ci++)
            {
                var left = colBounds[ci];
                var right = colBounds[ci + 1];
                var colWidth = right - left;
                if (colWidth < MinCellW * 3) continue; // Only split wide columns

                // Find text runs in this column and cluster their X positions
                var colRunXs = runs.Where(r => r.X >= left - 2 && r.X <= right + 2)
                    .Select(r => r.X).ToList();
                if (colRunXs.Count < 2) continue;

                var xClusters = ClusterValues(colRunXs);
                xClusters.Sort();
                // Look for gaps between X clusters that suggest column boundaries
                for (var k = 1; k < xClusters.Count; k++)
                {
                    var gap = xClusters[k] - xClusters[k - 1];
                    if (gap > 20 && xClusters[k] > left + MinCellW && xClusters[k] < right - MinCellW)
                        additionalBounds.Add(xClusters[k]);
                }
            }
            foreach (var ab in additionalBounds)
            {
                if (!colBounds.Any(cb => Math.Abs(cb - ab) < EdgeTol * 2))
                    colBounds.Add(ab);
            }
            colBounds.Sort();
        }

        if (rowBounds.Count < 2 || colBounds.Count < 2) return [];

        var nRows = rowBounds.Count - 1;
        var nCols = colBounds.Count - 1;

        // 2. Mark each grid position as a valid cell if it has >= 3 bounding sides
        var valid = new bool[nRows, nCols];
        int validCount = 0;
        for (var r = 0; r < nRows; r++)
        {
            var yBot = rowBounds[r];
            var yTop = rowBounds[r + 1];
            for (var c = 0; c < nCols; c++)
            {
                var xLeft = colBounds[c];
                var xRight = colBounds[c + 1];
                valid[r, c] =
                    (xRight - xLeft) >= MinCellW &&
                    (yTop - yBot) >= MinCellH &&
                    CountSides(hEdges, vEdges, yBot, yTop, xLeft, xRight) >= 3;
                if (valid[r, c]) validCount++;
            }
        }

        // Fallback: progressively lower the side threshold when less than half the
        // grid cells are valid. Tables with full-width H-edges but segmented V-edges
        // (interior columns having only top+bottom borders) need minSides=2.
        int totalCells = nRows * nCols;
        for (int minSides = 2; minSides >= 1 && validCount * 2 < totalCells; minSides--)
        {
            validCount = 0;
            for (var r = 0; r < nRows; r++)
            {
                var yBot = rowBounds[r];
                var yTop = rowBounds[r + 1];
                for (var c = 0; c < nCols; c++)
                {
                    var xLeft = colBounds[c];
                    var xRight = colBounds[c + 1];
                    valid[r, c] =
                        (xRight - xLeft) >= MinCellW &&
                        (yTop - yBot) >= MinCellH &&
                        (minSides == 0 || CountSides(hEdges, vEdges, yBot, yTop, xLeft, xRight) >= minSides);
                    if (valid[r, c]) validCount++;
                }
            }
        }

        // 3. Collect all valid cells, then use flood-fill to find connected components
        var visited = new bool[nRows, nCols];
        var tables = new List<AbsorbedTable>();

        for (var r = 0; r < nRows; r++)
        {
            for (var c = 0; c < nCols; c++)
            {
                if (!valid[r, c] || visited[r, c]) continue;
                var component = FloodFill(valid, visited, r, c, nRows, nCols);
                if (component.Count < MinCells) continue;
                tables.AddRange(BuildTablesFromComponent(component, rowBounds, colBounds, runs));
            }
        }

        // If flood-fill produced too many small tables (due to fragmented grids),
        // fall back to building one big table from all valid cells and splitting at separator rows
        if (tables.Count > 6 || (tables.Count > 1 && tables.All(t => t.Rows.Count <= 2)))
        {
            var allValid = new List<(int r, int c)>();
            for (var r = 0; r < nRows; r++)
                for (var c = 0; c < nCols; c++)
                    if (valid[r, c]) allValid.Add((r, c));
            if (allValid.Count >= MinCells)
            {
                var bigTables = BuildTablesFromComponent(allValid, rowBounds, colBounds, runs);
                if (bigTables.Count > 0 && bigTables.Sum(t => t.Rows.Sum(rw => rw.Cells.Count)) > tables.Sum(t => t.Rows.Sum(rw => rw.Cells.Count)) / 2)
                    tables = bigTables;
            }
        }

        // Merge heavily fragmented tables (>6 fragments with same column structure)
        if (tables.Count > 6)
            tables = MergeVerticallyAdjacentTables(tables);

        // Merge single-row tables into adjacent multi-row tables when they share
        // the same X range — these are typically header rows separated by grid gaps.
        tables = MergeSingleRowFragments(tables);

        // Sort tables top-to-bottom (highest Y first)
        tables.Sort((a, b) => (b.Rect?.URY ?? 0).CompareTo(a.Rect?.URY ?? 0));

        return tables;
    }

    /// <summary>
    /// Merge tables that are vertically adjacent (close in Y) and share similar left edge.
    /// Only merges when the gap between tables is small relative to row height.
    /// </summary>
    private static List<AbsorbedTable> MergeVerticallyAdjacentTables(List<AbsorbedTable> tables)
    {
        if (tables.Count <= 1) return tables;

        // Group tables by similar X range
        var groups = new List<List<AbsorbedTable>>();
        var used = new bool[tables.Count];

        for (var i = 0; i < tables.Count; i++)
        {
            if (used[i]) continue;
            var group = new List<AbsorbedTable> { tables[i] };
            used[i] = true;
            var left = tables[i].Rect?.LLX ?? 0;
            var right = tables[i].Rect?.URX ?? 0;

            for (var j = i + 1; j < tables.Count; j++)
            {
                if (used[j]) continue;
                var jLeft = tables[j].Rect?.LLX ?? 0;
                var jRight = tables[j].Rect?.URX ?? 0;
                // Same left edge — fragments of one over-segmented table
                if (Math.Abs(jLeft - left) < EdgeTol * 3)
                {
                    group.Add(tables[j]);
                    used[j] = true;
                }
            }
            groups.Add(group);
        }

        var result = new List<AbsorbedTable>();
        foreach (var group in groups)
        {
            if (group.Count == 1)
            {
                result.Add(group[0]);
                continue;
            }

            // Merge: combine all rows sorted top-to-bottom, compute bounding rect
            var allRows = new List<AbsorbedRow>();
            // Sort tables by Y descending (top of page first)
            group.Sort((a, b) => (b.Rect?.URY ?? 0).CompareTo(a.Rect?.URY ?? 0));
            foreach (var t in group)
                allRows.AddRange(t.Rows);

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            foreach (var t in group)
            {
                if (t.Rect is null) continue;
                if (t.Rect.LLX < minX) minX = t.Rect.LLX;
                if (t.Rect.LLY < minY) minY = t.Rect.LLY;
                if (t.Rect.URX > maxX) maxX = t.Rect.URX;
                if (t.Rect.URY > maxY) maxY = t.Rect.URY;
            }

            result.Add(new AbsorbedTable
            {
                Rows = allRows,
                Rect = double.IsFinite(minX) ? new Rectangle(minX, minY, maxX, maxY) : null,
            });
        }

        return result;
    }

    /// <summary>
    /// Merge single-row table fragments into adjacent multi-row tables when they
    /// share a similar X range and are vertically close. This handles cases where
    /// decorative lines split a table header from its body into separate components.
    /// </summary>
    private static List<AbsorbedTable> MergeSingleRowFragments(List<AbsorbedTable> tables)
    {
        if (tables.Count < 2) return tables;

        var result = new List<AbsorbedTable>(tables);
        bool merged;
        do
        {
            merged = false;
            for (var i = result.Count - 1; i >= 0; i--)
            {
                var small = result[i];
                if (small.Rows.Count > 1 || small.Rect is null) continue;

                // Find a multi-row neighbor with overlapping X range and close Y
                for (var j = 0; j < result.Count; j++)
                {
                    if (j == i) continue;
                    var big = result[j];
                    if (big.Rows.Count < 2 || big.Rect is null) continue;

                    // Check X overlap
                    var overlapLeft = Math.Max(small.Rect.LLX, big.Rect.LLX);
                    var overlapRight = Math.Min(small.Rect.URX, big.Rect.URX);
                    var smallWidth = small.Rect.URX - small.Rect.LLX;
                    if (overlapRight - overlapLeft < smallWidth * 0.5) continue;

                    // Check Y adjacency (gap < 30pt)
                    var gap = Math.Min(
                        Math.Abs(small.Rect.LLY - big.Rect.URY),
                        Math.Abs(big.Rect.LLY - small.Rect.URY));
                    if (gap > 30) continue;

                    // Merge: prepend or append the single row
                    var allRows = small.Rect.URY > big.Rect.URY
                        ? small.Rows.Concat(big.Rows).ToList()
                        : big.Rows.Concat(small.Rows).ToList();

                    var minX = Math.Min(small.Rect.LLX, big.Rect.LLX);
                    var minY = Math.Min(small.Rect.LLY, big.Rect.LLY);
                    var maxX = Math.Max(small.Rect.URX, big.Rect.URX);
                    var maxY = Math.Max(small.Rect.URY, big.Rect.URY);

                    result[j] = new AbsorbedTable
                    {
                        Rows = allRows,
                        Rect = new Rectangle(minX, minY, maxX, maxY),
                    };
                    result.RemoveAt(i);
                    merged = true;
                    break;
                }
                if (merged) break;
            }
        } while (merged);

        return result;
    }

    /// <summary>Cluster nearby values (within EdgeTol) and return their averages.</summary>
    private static List<double> ClusterValues(List<double> values)
    {
        if (values.Count == 0) return [];
        var sorted = values.ToList();
        sorted.Sort();
        var clusters = new List<double>();
        var group = new List<double> { sorted[0] };
        for (var i = 1; i < sorted.Count; i++)
        {
            if (sorted[i] - group[^1] <= EdgeTol)
                group.Add(sorted[i]);
            else
            {
                clusters.Add(group.Sum() / group.Count);
                group = [sorted[i]];
            }
        }
        clusters.Add(group.Sum() / group.Count);
        return clusters;
    }

    /// <summary>
    /// Merge adjacent text runs on the same Y line into single runs.
    /// CID fonts often produce one run per character; this merges them into
    /// word/phrase fragments.
    /// </summary>
    private static List<TextRun> MergeCellRuns(List<TextRun> runs)
    {
        if (runs.Count <= 1) return runs;
        var sorted = runs.OrderBy(r => r.Y).ThenBy(r => r.X).ToList();
        var merged = new List<TextRun>();
        var current = sorted[0];
        for (int i = 1; i < sorted.Count; i++)
        {
            var next = sorted[i];
            // Same line (Y within tolerance) and adjacent (gap < font height)
            var yDiff = Math.Abs(next.Y - current.Y);
            var gap = next.X - (current.X + current.W);
            if (yDiff < 2.0 && gap < current.H * 0.5)
            {
                // Merge: extend current run
                var newW = (next.X + next.W) - current.X;
                current = new TextRun(current.Text + next.Text, current.X, current.Y,
                    newW, Math.Max(current.H, next.H));
            }
            else
            {
                merged.Add(current);
                current = next;
            }
        }
        merged.Add(current);
        return merged;
    }

    /// <summary>
    /// Merge sorted boundary values that are closer than <paramref name="threshold"/>.
    /// Replaces pairs of nearby boundaries with their midpoint, eliminating
    /// double-border artifacts common in PDF tables.
    /// </summary>
    private static void MergeNearbyBoundaries(List<double> bounds, double threshold)
    {
        for (int i = bounds.Count - 1; i > 0; i--)
        {
            if (bounds[i] - bounds[i - 1] < threshold)
            {
                var mid = (bounds[i] + bounds[i - 1]) * 0.5;
                bounds[i - 1] = mid;
                bounds.RemoveAt(i);
            }
        }
    }

    /// <summary>Count how many of the 4 cell sides (top, bottom, left, right) have an edge.</summary>
    private static int CountSides(List<HEdge> hEdges, List<VEdge> vEdges,
        double yBot, double yTop, double xLeft, double xRight)
    {
        var n = 0;
        // Top H edge (y ~ yTop, x-span overlaps [xLeft, xRight])
        if (hEdges.Any(e => Math.Abs(e.Y - yTop) <= EdgeTol && e.X1 < xRight && e.X2 > xLeft)) n++;
        // Bottom H edge
        if (hEdges.Any(e => Math.Abs(e.Y - yBot) <= EdgeTol && e.X1 < xRight && e.X2 > xLeft)) n++;
        // Left V edge (x ~ xLeft, y-span overlaps [yBot, yTop])
        if (vEdges.Any(e => Math.Abs(e.X - xLeft) <= EdgeTol && e.Y1 < yTop && e.Y2 > yBot)) n++;
        // Right V edge
        if (vEdges.Any(e => Math.Abs(e.X - xRight) <= EdgeTol && e.Y1 < yTop && e.Y2 > yBot)) n++;
        return n;
    }

    /// <summary>BFS flood-fill to find connected valid cells.</summary>
    private static List<(int r, int c)> FloodFill(bool[,] valid, bool[,] visited,
        int startR, int startC, int nRows, int nCols)
    {
        var queue = new Queue<(int r, int c)>();
        var result = new List<(int r, int c)>();
        visited[startR, startC] = true;
        queue.Enqueue((startR, startC));

        while (queue.Count > 0)
        {
            var (r, c) = queue.Dequeue();
            result.Add((r, c));
            ReadOnlySpan<(int dr, int dc)> dirs = [(-1, 0), (1, 0), (0, -1), (0, 1)];
            foreach (var (dr, dc) in dirs)
            {
                var nr = r + dr;
                var nc = c + dc;
                if (nr >= 0 && nr < nRows && nc >= 0 && nc < nCols &&
                    valid[nr, nc] && !visited[nr, nc])
                {
                    visited[nr, nc] = true;
                    queue.Enqueue((nr, nc));
                }
            }
        }
        return result;
    }

    /// <summary>Build one or more AbsorbedTables from a connected component of valid grid cells.
    /// Splits at separator rows (rows where all cells are empty) to produce separate tables.</summary>
    private static List<AbsorbedTable> BuildTablesFromComponent(
        List<(int r, int c)> component, List<double> rowBounds, List<double> colBounds,
        List<TextRun> runs)
    {
        var rowIndices = component.Select(p => p.r).Distinct().OrderBy(r => r).ToList();

        // Build rows top-to-bottom: in PDF coords, larger y = higher on page,
        // so reverse row index order to get visual top-to-bottom.
        var allRows = new List<AbsorbedRow>();
        foreach (var r in rowIndices.AsEnumerable().Reverse())
        {
            var yBot = rowBounds[r];
            var yTop = rowBounds[r + 1];
            var colsInRow = component.Where(p => p.r == r).Select(p => p.c).OrderBy(c => c).ToList();
            if (colsInRow.Count == 0) continue;

            var cells = new List<AbsorbedCell>();
            foreach (var c in colsInRow)
            {
                var xLeft = colBounds[c];
                var xRight = colBounds[c + 1];
                if ((xRight - xLeft) < MinCellW || (yTop - yBot) < MinCellH) continue;

                var cellRect = new Rectangle(xLeft, yBot, xRight, yTop);
                // Match text runs whose CENTER X falls within cell, and Y is within cell
                var cellRuns = runs.Where(run =>
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
                    new TextFragment(run.Text) { Position = new Position(run.X, run.Y) }));
                cells.Add(new AbsorbedCell { Text = cellText, Rect = cellRect, TextFragments = AbsorbedCell.ToCollection(frags) });
            }
            if (cells.Count == 0) continue;
            allRows.Add(new AbsorbedRow { Cells = cells });
        }

        if (allRows.Count == 0) return [];

        // Split at separator rows (rows where all cells are empty) into sub-tables
        var sections = SplitAtSeparatorRows(allRows);

        var result = new List<AbsorbedTable>();
        foreach (var section in sections)
        {
            if (section.Count < 1) continue;
            // Drop always-empty sandwiched columns
            var cleaned = DropEmptyColumns(section);
            if (cleaned.Count == 0 || !cleaned.Any(r => r.Cells.Count >= 2)) continue;

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
            var rect = double.IsFinite(minX) ? new Rectangle(minX, minY, maxX, maxY) : null;
            result.Add(new AbsorbedTable { Rows = cleaned, Rect = rect });
        }
        return result;
    }

    /// <summary>Split rows into sections at separator boundaries:
    /// empty rows, and rows with far fewer cells than the dominant column count.</summary>
    private static List<List<AbsorbedRow>> SplitAtSeparatorRows(List<AbsorbedRow> allRows)
    {
        // For small grids (≤ 4 rows), keep all rows including empty ones —
        // they're part of the grid structure, not separators between tables.
        if (allRows.Count <= 4)
            return [allRows];

        // First pass: split at empty rows (rows with no text or only whitespace)
        var pass1 = new List<List<AbsorbedRow>>();
        var current = new List<AbsorbedRow>();
        foreach (var row in allRows)
        {
            var hasContent = row.Cells.Any(c =>
                c.TextFragments.Count > 0 &&
                Enumerable.Range(1, c.TextFragments.Count)
                    .Any(fi => !string.IsNullOrWhiteSpace(c.TextFragments[fi].Text)));
            if (!hasContent && current.Count > 0) { pass1.Add(current); current = []; }
            else if (hasContent) current.Add(row);
        }
        if (current.Count > 0) pass1.Add(current);

        // Second pass: within each section, split at rows with significantly fewer cells
        // than the section's dominant column count (these are section headers/notes)
        var result = new List<List<AbsorbedRow>>();
        foreach (var section in pass1)
        {
            if (section.Count <= 2) { result.Add(section); continue; }
            var maxCols = section.Max(r => r.Cells.Count);
            if (maxCols < 3) { result.Add(section); continue; }

            var sub = new List<AbsorbedRow>();
            foreach (var row in section)
            {
                if (row.Cells.Count <= maxCols / 2 && sub.Count > 0)
                {
                    result.Add(sub); sub = [];
                    // Don't include the separator row itself
                }
                else
                {
                    sub.Add(row);
                }
            }
            if (sub.Count > 0) result.Add(sub);
        }
        return result;
    }

    /// <summary>Check whether a TextRun's bounding box overlaps with a cell rectangle.</summary>
    private static bool RectsOverlap(TextRun run, Rectangle cell)
    {
        // TextRun has position (X,Y) at baseline-left; approximate its bounding box
        var rLlx = run.X;
        var rLly = run.Y;
        var rUrx = run.X + run.W;
        var rUry = run.Y + run.H;
        return rLlx < cell.URX && rUrx > cell.LLX && rLly < cell.URY && rUry > cell.LLY;
    }

    /// <summary>
    /// Remove columns that are always empty AND sandwiched between two non-empty columns.
    /// Absorb each dropped column's x-span into the adjacent non-empty cell.
    /// </summary>
    private static List<AbsorbedRow> DropEmptyColumns(List<AbsorbedRow> rows)
    {
        if (rows.Count == 0) return rows;
        var maxCols = rows.Max(r => r.Cells.Count);

        // Which columns are empty in every row?
        var alwaysEmpty = new bool[maxCols];
        for (var c = 0; c < maxCols; c++) alwaysEmpty[c] = true;
        foreach (var row in rows)
            for (var c = 0; c < row.Cells.Count; c++)
                if (row.Cells[c].TextFragments.Count > 0) alwaysEmpty[c] = false;

        // A column is "spurious" only when always-empty AND sandwiched
        var hasNonEmptyLeft = new bool[maxCols];
        var hasNonEmptyRight = new bool[maxCols];
        for (var c = 1; c < maxCols; c++)
            hasNonEmptyLeft[c] = !alwaysEmpty[c - 1] || hasNonEmptyLeft[c - 1];
        for (var c = maxCols - 2; c >= 0; c--)
            hasNonEmptyRight[c] = !alwaysEmpty[c + 1] || hasNonEmptyRight[c + 1];

        var spurious = new bool[maxCols];
        for (var c = 0; c < maxCols; c++)
            spurious[c] = alwaysEmpty[c] && hasNonEmptyLeft[c] && hasNonEmptyRight[c];

        if (!spurious.Any(s => s)) return rows;

        return rows.Select(row =>
        {
            var newCells = new List<AbsorbedCell>();
            for (var c = 0; c < row.Cells.Count; c++)
            {
                var cell = row.Cells[c];
                if (spurious[c])
                {
                    // Absorb into the left neighbour's right edge
                    if (newCells.Count > 0)
                    {
                        var prev = newCells[^1];
                        var merged = new Rectangle(prev.Rect!.LLX, prev.Rect.LLY, cell.Rect!.URX, prev.Rect.URY);
                        newCells[^1] = new AbsorbedCell
                        {
                            Text = prev.Text,
                            Rect = merged,
                            TextFragments = prev.TextFragments
                        };
                    }
                }
                else
                {
                    // Expand left boundary to absorb preceding spurious columns with no left neighbour
                    var llx = cell.Rect!.LLX;
                    var pc = c - 1;
                    while (pc >= 0 && spurious[pc] && newCells.Count == 0)
                    {
                        llx = Math.Min(llx, row.Cells[pc].Rect!.LLX);
                        pc--;
                    }
                    var rect = new Rectangle(llx, cell.Rect.LLY, cell.Rect.URX, cell.Rect.URY);
                    newCells.Add(new AbsorbedCell { Text = cell.Text, Rect = rect, TextFragments = cell.TextFragments });
                }
            }
            return new AbsorbedRow { Cells = newCells };
        }).Where(r => r.Cells.Count > 0).ToList();
    }

    // ── Text-based fallback detection ──────────────────────────────────

    private static List<AbsorbedTable> DetectTablesFromText(List<TextRun> runs)
    {
        if (runs.Count == 0) return [];
        const double yThreshold = 3.0;
        runs.Sort((a, b) => { var yComp = -a.Y.CompareTo(b.Y); return yComp != 0 ? yComp : a.X.CompareTo(b.X); });
        var rowGroups = new List<List<TextRun>>();
        List<TextRun>? currentRow = null; var lastY = double.MaxValue;
        foreach (var run in runs)
        { if (currentRow is null || Math.Abs(run.Y - lastY) > yThreshold) { currentRow = []; rowGroups.Add(currentRow); } currentRow.Add(run); lastY = run.Y; }

        var columnBreaks = DetectColumnBreaks(runs);
        if (columnBreaks.Count < 3 || rowGroups.Count < 2) return [];

        var tableRows = new List<AbsorbedRow>();
        foreach (var rowRuns in rowGroups)
        {
            // Y-span of this row, taken from every run so empty cells still get a rectangle.
            // Y is the run baseline/bottom and H its height, so [minY, max(Y+H)] is an
            // upright (un-flipped) band — the original bug reported the cell position as
            // flipped because no rectangle was emitted here at all.
            var rowLly = rowRuns.Min(r => r.Y);
            var rowUry = rowRuns.Max(r => r.Y + r.H);
            var cells = new List<AbsorbedCell>();
            foreach (var colRange in columnBreaks.Zip(columnBreaks.Skip(1)))
            {
                var cellRuns = rowRuns.Where(r => r.X >= colRange.First - 5 && r.X < colRange.Second - 5).ToList();
                var cellText = string.Join(" ", cellRuns.Select(r => r.Text)).Trim();
                // Add leading position-only fragment
                var frags = new List<TextFragment>();
                if (cellRuns.Count > 0)
                    frags.Add(new TextFragment(" ") { Position = new Position(cellRuns[0].X, cellRuns[0].Y) });
                frags.AddRange(cellRuns.Select(r => new TextFragment(r.Text) { Position = new Position(r.X, r.Y) }));
                cells.Add(new AbsorbedCell
                {
                    Text = cellText,
                    Rect = new Rectangle(colRange.First, rowLly, colRange.Second, rowUry),
                    TextFragments = AbsorbedCell.ToCollection(frags)
                });
            }
            if (cells.Any(c => !string.IsNullOrWhiteSpace(c.Text))) tableRows.Add(new AbsorbedRow { Cells = cells });
        }
        if (tableRows.Count < 2) return [];
        var totalCells = tableRows.Sum(r => r.Cells.Count);
        var filledCells = tableRows.Sum(r => r.Cells.Count(c => !string.IsNullOrWhiteSpace(c.Text)));
        if (filledCells < totalCells / 3) return [];
        return [new AbsorbedTable { Rows = tableRows }];
    }

    private static List<double> DetectColumnBreaks(List<TextRun> runs)
    {
        if (runs.Count == 0) return [];
        var xPositions = runs.Select(r => r.X).OrderBy(x => x).ToList();
        var breaks = new List<double> { xPositions[0] };
        const double gapThreshold = 30.0;
        for (var i = 1; i < xPositions.Count; i++)
            if (xPositions[i] - xPositions[i - 1] > gapThreshold) breaks.Add(xPositions[i]);
        breaks.Add(runs.Max(r => r.X) + 200);
        return breaks;
    }

    // ── Remove table content from page stream ───────────────────────────

    private static void RemoveTableContent(Page page, Rectangle tableRect)
    {
        var reader = page.Reader;
        var contentStreams = GetContentStreams(page.Dict, reader);
        if (contentStreams.Count == 0) return;
        using var combined = new MemoryStream();
        foreach (var cs in contentStreams)
        {
            if (combined.Length > 0) combined.WriteByte((byte)'\n');
            combined.Write(cs);
        }
        var allBytes = combined.ToArray();
        var filtered = FilterContentStream(allBytes, tableRect, page);
        page.SetContentStream(filtered);
    }

    /// <summary>
    /// Removes graphics and text from the content stream that fall inside <paramref name="tableRect"/>.
    /// Walks the stream token-by-token, tracking the current transformation matrix (CTM) and
    /// text matrix so that path/text coordinates can be mapped to page space. Any path or BT…ET
    /// block whose transformed points lie inside the table rectangle is stripped out.
    /// </summary>
    private static byte[] FilterContentStream(byte[] stream, Rectangle tableRect, Page page)
    {
        var (rA, rB, rC, rD, rE, rF) = PageRotationCtm(page);
        var lexer = new PdfLexer(stream);
        var result = new MemoryStream();
        var removals = new List<(int start, int end)>();
        var operands = new List<PdfObject>();

        // CTM state — initialized to the page rotation matrix
        var ctmStack = new Stack<(double a, double b, double c, double d, double e, double f)>();
        double ctmA = rA, ctmB = rB, ctmC = rC, ctmD = rD, ctmE = rE, ctmF = rF;

        // Path construction state — track byte offset of first path operator and all points
        var pathStart = -1;
        var pathPoints = new List<(double x, double y)>();

        // Text block state — track BT offset, text positions, and text matrix components
        var btStart = -1;
        var textPoints = new List<(double x, double y)>();
        double tx = 0, ty = 0, txLine = 0, tyLine = 0;
        double tmA = 1, tmB = 0, tmC = 0, tmD = 1, leading = 0;

        while (true)
        {
            var tokenStart = (int)lexer.Position;
            var token = lexer.NextToken();
            if (token.Kind == TokenKind.Eof) break;

            switch (token.Kind)
            {
                case TokenKind.Integer:
                    operands.Add(new PdfInteger(token.IntValue));
                    break;
                case TokenKind.Real:
                    operands.Add(new PdfReal(token.RealValue));
                    break;
                case TokenKind.LiteralString:
                    operands.Add(new PdfString(token.BytesValue!));
                    break;
                case TokenKind.HexString:
                    operands.Add(new PdfString(token.BytesValue!, isHex: true));
                    break;
                case TokenKind.Name:
                    operands.Add(new PdfName(token.StringValue!));
                    break;
                case TokenKind.ArrayStart:
                    operands.Add(ParseArray(lexer));
                    break;
                case TokenKind.Keyword:
                    HandleFilterOperator(token.StringValue!, operands, tokenStart, (int)lexer.Position,
                        ref ctmA, ref ctmB, ref ctmC, ref ctmD, ref ctmE, ref ctmF, ctmStack,
                        ref pathStart, pathPoints, ref btStart, textPoints,
                        ref tx, ref ty, ref txLine, ref tyLine,
                        ref tmA, ref tmB, ref tmC, ref tmD, ref leading,
                        tableRect, removals, lexer);
                    operands.Clear();
                    break;
                default:
                    operands.Clear();
                    break;
            }
        }

        if (removals.Count == 0) return stream;
        return ApplyRemovals(stream, removals, result);
    }

    /// <summary>
    /// Dispatches a single PDF operator during content stream filtering.
    /// Updates CTM/text matrix state and records byte ranges to remove.
    /// </summary>
    private static void HandleFilterOperator(
        string op, List<PdfObject> operands, int tokenStart, int tokenEnd,
        ref double ctmA, ref double ctmB, ref double ctmC, ref double ctmD, ref double ctmE, ref double ctmF,
        Stack<(double a, double b, double c, double d, double e, double f)> ctmStack,
        ref int pathStart, List<(double x, double y)> pathPoints,
        ref int btStart, List<(double x, double y)> textPoints,
        ref double tx, ref double ty, ref double txLine, ref double tyLine,
        ref double tmA, ref double tmB, ref double tmC, ref double tmD, ref double leading,
        Rectangle tableRect, List<(int start, int end)> removals, PdfLexer lexer)
    {
        switch (op)
        {
            // ── Graphics state ──
            case "q":
                ctmStack.Push((ctmA, ctmB, ctmC, ctmD, ctmE, ctmF));
                break;
            case "Q":
                if (ctmStack.Count > 0)
                    (ctmA, ctmB, ctmC, ctmD, ctmE, ctmF) = ctmStack.Pop();
                break;
            case "cm":
                // Concatenate matrix: CTM' = operand × CTM (PDF 32000 §8.3.4)
                if (operands.Count >= 6)
                    ConcatenateCtm(operands, ref ctmA, ref ctmB, ref ctmC, ref ctmD, ref ctmE, ref ctmF);
                break;

            // ── Path construction (PDF 32000 §8.5.2) ──
            case "m" or "l":
                if (pathStart < 0) pathStart = tokenStart;
                if (operands.Count >= 2)
                    pathPoints.Add(TransformPoint(operands[0], operands[1], ctmA, ctmB, ctmC, ctmD, ctmE, ctmF));
                break;
            case "re":
                // Rectangle: add opposite corners to capture the full extent
                if (pathStart < 0) pathStart = tokenStart;
                if (operands.Count >= 4)
                {
                    var rx = Num(operands[0]); var ry = Num(operands[1]);
                    var rw = Num(operands[2]); var rh = Num(operands[3]);
                    pathPoints.Add(ApplyMatrix(rx, ry, ctmA, ctmB, ctmC, ctmD, ctmE, ctmF));
                    pathPoints.Add(ApplyMatrix(rx + rw, ry + rh, ctmA, ctmB, ctmC, ctmD, ctmE, ctmF));
                }
                break;
            case "c" or "v" or "y":
                // Curve operators — only the endpoint matters for hit testing
                if (pathStart < 0) pathStart = tokenStart;
                if (operands.Count >= 2)
                    pathPoints.Add(TransformPoint(operands[^2], operands[^1], ctmA, ctmB, ctmC, ctmD, ctmE, ctmF));
                break;
            case "h":
                if (pathStart < 0) pathStart = tokenStart;
                break;

            // ── Path painting — finalize and check if path falls inside table ──
            case "S" or "s" or "f" or "F" or "f*" or "B" or "B*" or "b" or "b*" or "n":
                if (pathStart >= 0 && pathPoints.Count > 0 && AnyPointInRect(pathPoints, tableRect))
                    removals.Add((pathStart, tokenEnd));
                pathStart = -1;
                pathPoints.Clear();
                break;

            case "W" or "W*":
                // Clipping — preserve as-is
                break;

            // ── Text block (PDF 32000 §9.4) ──
            case "BT":
                btStart = tokenStart;
                textPoints.Clear();
                tx = txLine = ty = tyLine = 0;
                tmA = 1; tmB = 0; tmC = 0; tmD = 1;
                leading = 0;
                break;
            case "ET":
                if (btStart >= 0 && textPoints.Count > 0 && AnyPointInRect(textPoints, tableRect))
                    removals.Add((btStart, tokenEnd));
                btStart = -1;
                textPoints.Clear();
                break;

            // ── Text positioning operators ──
            case "TL":
                if (operands.Count >= 1) leading = Num(operands[0]);
                break;
            case "Td":
                if (operands.Count >= 2)
                    UpdateTextPosition(operands, ref tx, ref ty, ref txLine, ref tyLine, tmA, tmB, tmC, tmD);
                break;
            case "TD":
                // TD sets leading and moves — equivalent to: -ty2 TL tx ty Td
                if (operands.Count >= 2)
                {
                    leading = -Num(operands[1]);
                    UpdateTextPosition(operands, ref tx, ref ty, ref txLine, ref tyLine, tmA, tmB, tmC, tmD);
                }
                break;
            case "T*":
                // Move to start of next line using current leading
                txLine = tmC * (-leading) + txLine;
                tyLine = tmD * (-leading) + tyLine;
                tx = txLine; ty = tyLine;
                break;
            case "Tm":
                if (operands.Count >= 6)
                {
                    tmA = Num(operands[0]); tmB = Num(operands[1]);
                    tmC = Num(operands[2]); tmD = Num(operands[3]);
                    tx = txLine = Num(operands[4]);
                    ty = tyLine = Num(operands[5]);
                }
                break;

            // ── Text showing — record the current text position ──
            case "Tj" or "TJ" or "'" or "\"":
                if (btStart >= 0)
                    textPoints.Add(ApplyMatrix(tx, ty, ctmA, ctmB, ctmC, ctmD, ctmE, ctmF));
                break;

            case "BI":
                SkipInlineImage(lexer);
                break;
        }
    }

    /// <summary>
    /// Concatenates a 6-element matrix from operands into the current CTM.
    /// Formula: CTM' = M × CTM (PDF 32000 §8.3.4).
    /// </summary>
    private static void ConcatenateCtm(List<PdfObject> operands,
        ref double ctmA, ref double ctmB, ref double ctmC, ref double ctmD, ref double ctmE, ref double ctmF)
    {
        var a = Num(operands[0]); var b = Num(operands[1]);
        var c = Num(operands[2]); var d = Num(operands[3]);
        var e = Num(operands[4]); var f = Num(operands[5]);
        var nA = a * ctmA + b * ctmC;
        var nB = a * ctmB + b * ctmD;
        var nC = c * ctmA + d * ctmC;
        var nD = c * ctmB + d * ctmD;
        var nE = e * ctmA + f * ctmC + ctmE;
        var nF = e * ctmB + f * ctmD + ctmF;
        ctmA = nA; ctmB = nB; ctmC = nC; ctmD = nD; ctmE = nE; ctmF = nF;
    }

    /// <summary>Transforms a point using the given matrix and returns the result.</summary>
    private static (double x, double y) TransformPoint(PdfObject xObj, PdfObject yObj,
        double a, double b, double c, double d, double e, double f)
        => ApplyMatrix(Num(xObj), Num(yObj), a, b, c, d, e, f);

    /// <summary>Applies Td/TD text position update using the text matrix.</summary>
    private static void UpdateTextPosition(List<PdfObject> operands,
        ref double tx, ref double ty, ref double txLine, ref double tyLine,
        double tmA, double tmB, double tmC, double tmD)
    {
        var dx = Num(operands[0]); var dy = Num(operands[1]);
        txLine = tmA * dx + tmC * dy + txLine;
        tyLine = tmB * dx + tmD * dy + tyLine;
        tx = txLine; ty = tyLine;
    }

    /// <summary>
    /// Removes byte ranges from <paramref name="stream"/> by merging overlapping removals
    /// and writing only the surviving segments.
    /// </summary>
    private static byte[] ApplyRemovals(byte[] stream, List<(int start, int end)> removals, MemoryStream result)
    {
        removals.Sort((a, b) => a.start.CompareTo(b.start));

        // Merge overlapping/adjacent removal ranges
        var merged = new List<(int start, int end)> { removals[0] };
        for (var i = 1; i < removals.Count; i++)
        {
            var last = merged[^1];
            if (removals[i].start <= last.end)
                merged[^1] = (last.start, Math.Max(last.end, removals[i].end));
            else
                merged.Add(removals[i]);
        }

        // Write only the bytes outside merged removal ranges
        var pos = 0;
        foreach (var (start, end) in merged)
        {
            if (start > pos)
                result.Write(stream, pos, start - pos);
            pos = end;
        }
        if (pos < stream.Length)
            result.Write(stream, pos, stream.Length - pos);

        return result.ToArray();
    }

    /// <summary>Returns true if any point falls within the rectangle (with tolerance margin).</summary>
    private static bool AnyPointInRect(List<(double x, double y)> points, Rectangle rect)
    {
        const double margin = 10.0;
        foreach (var (x, y) in points)
        {
            if (x >= rect.LLX - margin && x <= rect.URX + margin &&
                y >= rect.LLY - margin && y <= rect.URY + margin)
                return true;
        }
        return false;
    }

    // ── Page rotation CTM ───────────────────────────────────────────────

    private static (double a, double b, double c, double d, double e, double f) PageRotationCtm(Page page)
    {
        var rotate = ((page.RotateDegrees % 360) + 360) % 360;
        var mb = page.MediaBox; var w = mb.URX - mb.LLX; var h = mb.URY - mb.LLY;
        return rotate switch
        { 90 => (0,-1,1,0,0,w), 180 => (-1,0,0,-1,w,h), 270 => (0,1,-1,0,h,0), _ => (1,0,0,1,0,0) };
    }

    // ── Content stream extraction ───────────────────────────────────────

    /// <summary>Buffered line segment within a path being constructed.</summary>
    private readonly record struct PendingLine(double X1, double Y1, double X2, double Y2);

    /// <summary>Buffered rect within a path being constructed.</summary>
    private readonly record struct PendingRect(double X, double Y, double W, double H);

    private static void ExtractTextAndLines(byte[] stream, Dictionary<string, PdfDictionary> fonts,
        PdfReader reader, List<TextRun> textRuns, List<HEdge> hEdges, List<VEdge> vEdges,
        double ctmA = 1, double ctmB = 0, double ctmC = 0, double ctmD = 1, double ctmE = 0, double ctmF = 0,
        PdfDictionary? xobjects = null, int depth = 0)
    {
        var lexer = new PdfLexer(stream); var operands = new List<PdfObject>();
        Dictionary<int, string>? toUnicode = null; PdfDictionary? fontDict = null;
        double fontSize = 12, tx = 0, ty = 0, txLine = 0, tyLine = 0;
        double tmA = 1, tmB = 0, tmC = 0, tmD = 1, leading = 0;
        double curX = 0, curY = 0, moveX = 0, moveY = 0;
        var ctmStack = new Stack<(double a, double b, double c, double d, double e, double f)>();

        // Buffer path segments until a paint operator finalizes them
        var pendingLines = new List<PendingLine>();
        var pendingRects = new List<PendingRect>();

        while (true)
        {
            var token = lexer.NextToken(); if (token.Kind == TokenKind.Eof) break;
            switch (token.Kind)
            {
                case TokenKind.Integer: operands.Add(new PdfInteger(token.IntValue)); break;
                case TokenKind.Real: operands.Add(new PdfReal(token.RealValue)); break;
                case TokenKind.LiteralString: operands.Add(new PdfString(token.BytesValue!)); break;
                case TokenKind.HexString: operands.Add(new PdfString(token.BytesValue!, isHex: true)); break;
                case TokenKind.Name: operands.Add(new PdfName(token.StringValue!)); break;
                case TokenKind.ArrayStart: operands.Add(ParseArray(lexer)); break;
                case TokenKind.Keyword:
                {
                    var op = token.StringValue!;
                    switch (op)
                    {
                        case "q": ctmStack.Push((ctmA,ctmB,ctmC,ctmD,ctmE,ctmF)); break;
                        case "Q": if (ctmStack.Count > 0) (ctmA,ctmB,ctmC,ctmD,ctmE,ctmF) = ctmStack.Pop(); break;
                        case "cm":
                            if (operands.Count >= 6)
                            { var a=Num(operands[0]);var b=Num(operands[1]);var c=Num(operands[2]);var d=Num(operands[3]);var e=Num(operands[4]);var f=Num(operands[5]);
                              var nA=a*ctmA+b*ctmC;var nB=a*ctmB+b*ctmD;var nC=c*ctmA+d*ctmC;var nD=c*ctmB+d*ctmD;var nE=e*ctmA+f*ctmC+ctmE;var nF=e*ctmB+f*ctmD+ctmF;
                              ctmA=nA;ctmB=nB;ctmC=nC;ctmD=nD;ctmE=nE;ctmF=nF; }
                            break;
                        case "BT": tx=txLine=0;ty=tyLine=0;tmA=1;tmB=0;tmC=0;tmD=1;leading=0; break;
                        case "TL": if (operands.Count>=1) leading=Num(operands[0]); break;
                        case "Tf":
                            if (operands.Count>=1&&operands[0] is PdfName fn&&fonts.TryGetValue(fn.Value,out var fd)){fontDict=fd;toUnicode=TextAbsorber.ParseToUnicodeFromDict(fd,reader);}
                            if (operands.Count>=2) fontSize=Math.Abs(Num(operands[1])); break;
                        case "Td": if (operands.Count>=2){var tdX=Num(operands[0]);var tdY=Num(operands[1]);txLine=tmA*tdX+tmC*tdY+txLine;tyLine=tmB*tdX+tmD*tdY+tyLine;tx=txLine;ty=tyLine;} break;
                        case "TD": if (operands.Count>=2){var tdX=Num(operands[0]);var tdY=Num(operands[1]);leading=-tdY;txLine=tmA*tdX+tmC*tdY+txLine;tyLine=tmB*tdX+tmD*tdY+tyLine;tx=txLine;ty=tyLine;} break;
                        case "T*": txLine=tmC*(-leading)+txLine;tyLine=tmD*(-leading)+tyLine;tx=txLine;ty=tyLine; break;
                        case "Tm": if (operands.Count>=6){tmA=Num(operands[0]);tmB=Num(operands[1]);tmC=Num(operands[2]);tmD=Num(operands[3]);tx=txLine=Num(operands[4]);ty=tyLine=Num(operands[5]);} break;
                        case "Tj":
                            if (operands.Count>=1&&operands[0] is PdfString s){var text=Decode(s.Value,toUnicode,fontDict);var(px,py)=ApplyMatrix(tx,ty,ctmA,ctmB,ctmC,ctmD,ctmE,ctmF);textRuns.Add(new TextRun(text,px,py,text.Length*fontSize*0.5,fontSize));} break;
                        case "TJ":
                            if (operands.Count>=1&&operands[0] is PdfArray arr){var sb=new StringBuilder();foreach(var item in arr)if(item is PdfString ps)sb.Append(Decode(ps.Value,toUnicode,fontDict));if(sb.Length>0){var(px,py)=ApplyMatrix(tx,ty,ctmA,ctmB,ctmC,ctmD,ctmE,ctmF);textRuns.Add(new TextRun(sb.ToString(),px,py,sb.Length*fontSize*0.5,fontSize));}} break;
                        case "m":
                            if (operands.Count>=2){var(px,py)=ApplyMatrix(Num(operands[0]),Num(operands[1]),ctmA,ctmB,ctmC,ctmD,ctmE,ctmF);curX=moveX=px;curY=moveY=py;} break;
                        case "l":
                            if (operands.Count>=2)
                            {
                                var(lx,ly)=ApplyMatrix(Num(operands[0]),Num(operands[1]),ctmA,ctmB,ctmC,ctmD,ctmE,ctmF);
                                pendingLines.Add(new PendingLine(curX, curY, lx, ly));
                                curX=lx;curY=ly;
                            }
                            break;
                        case "h":
                            // Close subpath: add a line from current point back to the move-to point
                            if (Math.Abs(curX - moveX) > 0.01 || Math.Abs(curY - moveY) > 0.01)
                                pendingLines.Add(new PendingLine(curX, curY, moveX, moveY));
                            curX = moveX; curY = moveY;
                            break;
                        case "re":
                            if (operands.Count>=4)
                            {
                                var rx=Num(operands[0]);var ry=Num(operands[1]);var rw=Num(operands[2]);var rh=Num(operands[3]);
                                var(p0x,p0y)=ApplyMatrix(rx,ry,ctmA,ctmB,ctmC,ctmD,ctmE,ctmF);
                                var(p2x,p2y)=ApplyMatrix(rx+rw,ry+rh,ctmA,ctmB,ctmC,ctmD,ctmE,ctmF);
                                var x=Math.Min(p0x,p2x); var y=Math.Min(p0y,p2y);
                                var w=Math.Abs(p2x-p0x); var h=Math.Abs(p2y-p0y);
                                pendingRects.Add(new PendingRect(x, y, w, h));
                            }
                            break;
                        // Paint operators: finalize buffered path segments as edges
                        case "S" or "s" or "f" or "F" or "f*" or "B" or "B*" or "b" or "b*":
                            FlushPendingEdges(pendingLines, pendingRects, hEdges, vEdges);
                            pendingLines.Clear(); pendingRects.Clear(); break;
                        case "n":
                            // No-paint: discard pending paths (clip-only)
                            pendingLines.Clear(); pendingRects.Clear(); break;
                        case "W" or "W*": break; // Clip modifiers don't finalize path
                        case "BI": SkipInlineImage(lexer); operands.Clear(); continue;
                        case "Do":
                            // Recurse into Form XObjects so table grids drawn inside them are
                            // extracted (nested tables are emitted as forms).
                            if (xobjects is not null && depth < 12 && operands.Count >= 1 && operands[0] is PdfName xn)
                            {
                                var form = reader.ResolveStream(xobjects.Get(xn.Value));
                                if (form is not null && form.Dict.GetName("Subtype") == "Form")
                                {
                                    byte[]? formBytes = null;
                                    try { formBytes = reader.DecodeStream(form); } catch { }
                                    if (formBytes is not null)
                                    {
                                        double fA = ctmA, fB = ctmB, fC = ctmC, fD = ctmD, fE = ctmE, fF = ctmF;
                                        if (reader.Resolve(form.Dict.Get("Matrix")) is PdfArray ma && ma.Count >= 6)
                                        {
                                            double m0=Num(ma[0]),m1=Num(ma[1]),m2=Num(ma[2]),m3=Num(ma[3]),m4=Num(ma[4]),m5=Num(ma[5]);
                                            fA=m0*ctmA+m1*ctmC; fB=m0*ctmB+m1*ctmD; fC=m2*ctmA+m3*ctmC; fD=m2*ctmB+m3*ctmD;
                                            fE=m4*ctmA+m5*ctmC+ctmE; fF=m4*ctmB+m5*ctmD+ctmF;
                                        }
                                        var formFonts = TextAbsorber.ResolveFonts(form.Dict, reader);
                                        foreach (var kv in fonts) formFonts.TryAdd(kv.Key, kv.Value);
                                        var formXObjects = TextAbsorber.ResolveXObjects(form.Dict, reader) ?? xobjects;
                                        ExtractTextAndLines(formBytes, formFonts, reader, textRuns, hEdges, vEdges,
                                            fA, fB, fC, fD, fE, fF, formXObjects, depth + 1);
                                    }
                                }
                            }
                            break;
                    }
                    operands.Clear(); break;
                }
                default: operands.Clear(); break;
            }
        }
    }

    /// <summary>Flush buffered path segments into H/V edge lists, applying the TS collectEdges logic.</summary>
    private static void FlushPendingEdges(List<PendingLine> lines, List<PendingRect> rects,
        List<HEdge> hEdges, List<VEdge> vEdges)
    {
        // Process line segments
        foreach (var line in lines)
        {
            var dy = Math.Abs(line.Y2 - line.Y1);
            var dx = Math.Abs(line.X2 - line.X1);
            if (dy <= LineRectThreshold && dx > LineRectThreshold)
                hEdges.Add(new HEdge((line.Y1 + line.Y2) / 2, Math.Min(line.X1, line.X2), Math.Max(line.X1, line.X2)));
            else if (dx <= LineRectThreshold && dy > LineRectThreshold)
                vEdges.Add(new VEdge((line.X1 + line.X2) / 2, Math.Min(line.Y1, line.Y2), Math.Max(line.Y1, line.Y2)));
        }

        // Process rects (matching TS collectEdges logic)
        foreach (var rect in rects)
        {
            var (x, y, w, h) = (rect.X, rect.Y, rect.W, rect.H);

            // Thin filled rects → treat as line borders
            if (h <= LineRectThreshold && w >= MinCellW)
            {
                hEdges.Add(new HEdge(y + h / 2, x, x + w));
                continue;
            }
            if (w <= LineRectThreshold && h >= MinCellH)
            {
                vEdges.Add(new VEdge(x + w / 2, y, y + h));
                continue;
            }

            // Cell boundary rects: emit all 4 edges
            if (w >= MinCellW && h >= MinCellH)
            {
                hEdges.Add(new HEdge(y + h, x, x + w)); // top
                hEdges.Add(new HEdge(y, x, x + w));     // bottom
                vEdges.Add(new VEdge(x, y, y + h));      // left
                vEdges.Add(new VEdge(x + w, y, y + h));  // right
            }
        }
    }

    private static (double x, double y) ApplyMatrix(double x, double y, double a, double b, double c, double d, double e, double f)
        => (a * x + c * y + e, b * x + d * y + f);

    private static string Decode(byte[] bytes, Dictionary<int, string>? toUnicode, PdfDictionary? fontDict)
    {
        if (toUnicode is not null)
        {
            var isCid = fontDict?.GetName("Subtype") == "Type0"; var sb = new StringBuilder();
            if (isCid && bytes.Length >= 2) for (var i = 0; i + 1 < bytes.Length; i += 2) { var code = (bytes[i] << 8) | bytes[i + 1]; sb.Append(toUnicode.TryGetValue(code, out var m) ? m : "\uFFFD"); }
            else foreach (var b in bytes) sb.Append(toUnicode.TryGetValue(b, out var m) ? m : ((char)b).ToString());
            return sb.ToString();
        }
        return Encoding.Latin1.GetString(bytes);
    }

    private static Dictionary<string, PdfDictionary> ResolveFonts(PdfDictionary pageDict, PdfReader reader)
        => TextAbsorber.ResolveFonts(pageDict, reader);

    private static byte[] ConcatenateStreams(List<byte[]> streams)
    {
        if (streams.Count == 1) return streams[0];
        using var ms = new MemoryStream();
        foreach (var s in streams) { if (ms.Length > 0) ms.WriteByte((byte)'\n'); ms.Write(s); }
        return ms.ToArray();
    }

    private static List<byte[]> GetContentStreams(PdfDictionary pageDict, PdfReader reader)
    {
        var result = new List<byte[]>();
        var obj = reader.Resolve(pageDict.Get("Contents"));
        if (obj is PdfStream stream) result.Add(reader.DecodeStream(stream));
        else if (obj is PdfArray arr) foreach (var item in arr) { var s = reader.ResolveStream(item); if (s is not null) result.Add(reader.DecodeStream(s)); }
        return result;
    }

    // Delegate to the shared implementation, which sizes Flate-compressed inline images by
    // inflate-probing the "EI" candidates instead of a fragile byte scan that desyncs the
    // lexer on binary data.
    private static void SkipInlineImage(PdfLexer lexer) => TextAbsorber.SkipInlineImage(lexer);

    private static PdfArray ParseArray(PdfLexer lexer)
    {
        var arr = new PdfArray();
        while (true) { var t = lexer.NextToken(); if (t.Kind == TokenKind.ArrayEnd || t.Kind == TokenKind.Eof) break;
            switch (t.Kind) { case TokenKind.Integer: arr.Add(new PdfInteger(t.IntValue)); break; case TokenKind.Real: arr.Add(new PdfReal(t.RealValue)); break;
                case TokenKind.LiteralString: arr.Add(new PdfString(t.BytesValue!)); break; case TokenKind.HexString: arr.Add(new PdfString(t.BytesValue!, isHex: true)); break;
                case TokenKind.Name: arr.Add(new PdfName(t.StringValue!)); break; } }
        return arr;
    }

    private static double Num(PdfObject obj) => obj switch { PdfInteger i => i.Value, PdfReal r => r.Value, _ => 0 };
}
