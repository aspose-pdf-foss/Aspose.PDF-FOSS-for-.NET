using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TableAbsorber
{
    private readonly record struct TextRun(string Text, double X, double Y, double W, double H);

    private readonly record struct HEdge(double Y, double X1, double X2);

    private readonly record struct VEdge(double X, double Y1, double Y2);

    /// <summary>Maximum gap (points) between two edge positions for them to be clustered into one boundary.
    /// Set higher than TS (3) because C# processes raw m/l operators which produce double-border pairs ~5pt apart.</summary>
    private const double EdgeTol = 6;

    /// <summary>Minimum cell width in points — prevents thin decorative rules from forming "cells".</summary>
    private const double MinCellW = 10;

    /// <summary>Minimum cell height in points.</summary>
    private const double MinCellH = 4;

    /// <summary>Minimum number of valid cells required to report a detected table.</summary>
    // A single bordered box counts as a 1-cell table (report frames,
    // header boxes), so a 1-cell component is accepted.
    private const int MinCells = 1;

    /// <summary>Maximum width/height (pt) for a thin filled rect to be treated as a line border.</summary>
    private const double LineRectThreshold = 3;

    private static List<AbsorbedTable> DetectTables(List<TextRun> runs, List<HEdge> hEdges, List<VEdge> vEdges,
        double pageW = 0, double pageH = 0)
    {
        // Ruled tables only here; borderless recognition (the flow engine's) runs on
        // the page's fragments afterwards.
        if (hEdges.Count < 2 || vEdges.Count < 2) return [];

        // Reassemble ruled lines drawn as several collinear pieces (dashed
        // borders, per-cell border strokes): abutting/overlapping pieces on the
        // same line are ONE rule, and the lattice must see them that way — a
        // dashed side border still connects its table's top and bottom.
        hEdges = MergeCollinearH(hEdges);
        vEdges = MergeCollinearV(vEdges);

        // A table is a CONNECTED LATTICE of rules: horizontal and vertical rules
        // that intersect belong to the same table; rules that touch nothing (page
        // header underlines, text decorations) belong to none. Decompose the page's
        // rules into connected components and run grid detection per component, so
        // one table's boundaries never inject phantom rows/columns into another's
        // grid (a header box top-right must not add a column to the invoice table
        // below it, and two stacked tables with different column sets keep their
        // own grids).
        var components = BuildLatticeComponents(hEdges, vEdges);

        // A component whose rules cannot bound a cell on their own (all H pieces
        // on one line, or a lone rule) is usually the missing part of a nearby
        // table drawn with open/dashed sides — a data row's bottom line whose
        // side stubs reach toward the header box above. Fold each one into the
        // grid-capable component whose column positions its verticals align with.
        components = MergeDegenerateComponents(components);
        var tables = new List<AbsorbedTable>();
        foreach (var (compH, compV) in components)
        {
            if (TableDebug)
            {
                Console.Error.WriteLine($"[tbl] component H={compH.Count} V={compV.Count}");
                foreach (var e in compH.OrderByDescending(e => e.Y))
                    Console.Error.WriteLine($"[tbl]   H y={e.Y:F2} x={e.X1:F2}..{e.X2:F2}");
                foreach (var e in compV.OrderBy(e => e.X))
                    Console.Error.WriteLine($"[tbl]   V x={e.X:F2} y={e.Y1:F2}..{e.Y2:F2}");
            }
            // A component without a 2x2 rule lattice cannot bound a cell.
            if (compH.Count < 2 || compV.Count < 2) continue;
            var minX = Math.Min(compH.Min(e => e.X1), compV.Min(e => e.X)) - EdgeTol;
            var maxX = Math.Max(compH.Max(e => e.X2), compV.Max(e => e.X)) + EdgeTol;
            var minY = Math.Min(compV.Min(e => e.Y1), compH.Min(e => e.Y)) - EdgeTol;
            var maxY = Math.Max(compV.Max(e => e.Y2), compH.Max(e => e.Y)) + EdgeTol;
            // A component the size of the page is the page FRAME (media-box border,
            // crop marks), not a table. A 90%-of-page report frame IS still
            // reported; only an (almost) exact page-boundary box is dropped.
            if (pageW > 0 && pageH > 0
                && maxX - minX >= pageW * 0.98 && maxY - minY >= pageH * 0.98)
                continue;
            // Two stacked tables can touch closely enough (a bottom border a few
            // points above the next table's top border) that they lattice into one
            // component. That seam — two long parallel rules with an empty strip
            // between them, no vertical crossing it, and a different column set on
            // each side — is a table boundary: detect grids per band, not across it.
            foreach (var (bandH, bandV) in SplitComponentBands(compH, compV))
            {
                if (bandH.Count < 2 || bandV.Count < 2) continue;
                var bMinY = Math.Min(bandV.Min(e => e.Y1), bandH.Min(e => e.Y)) - EdgeTol;
                var bMaxY = Math.Max(bandV.Max(e => e.Y2), bandH.Max(e => e.Y)) + EdgeTol;
                var compRuns = runs.Where(r =>
                    r.X + r.W >= minX && r.X <= maxX && r.Y >= bMinY && r.Y <= bMaxY).ToList();
                tables.AddRange(DetectTablesInRegion(compRuns, bandH, bandV));
            }
        }
        if (tables.Count == 0) return [];

        SortTables(tables);
        return tables;
    }

    /// <summary>Top-to-bottom, then left-to-right for tables whose tops align.</summary>
    private static void SortTables(List<AbsorbedTable> tables) =>
        tables.Sort((a, b) =>
        {
            var ay = a.Rect?.URY ?? 0; var by = b.Rect?.URY ?? 0;
            if (Math.Abs(ay - by) > 1) return by.CompareTo(ay);
            return (a.Rect?.LLX ?? 0).CompareTo(b.Rect?.LLX ?? 0);
        });

    /// <summary>Two vertical rule pieces whose x differ by no more than this are one
    /// column line: a Word table draws some rows' side bars 0.24 pt wide at x + 0.12
    /// and others 0.48 pt wide at x, and a table's 1 pt outer border sits 0.55 pt
    /// outside its 0.1 pt cell borders; each such pair reads as one line.</summary>
    private const double CollinearToleranceX = 0.6;

    /// <summary>The same for horizontal rules (a 1 pt outer border 0.55 pt outside the
    /// 0.1 pt cell rules is the table's top and bottom line).</summary>
    private const double CollinearToleranceY = 0.6;

    /// <summary>Merge collinear horizontal pieces: same rule (ΔY within
    /// <see cref="CollinearToleranceY"/>) and abutting or overlapping in X (gap ≤ 1.5).
    /// Pieces that do not overlap are one rule drawn in parts and it sits where its
    /// LEFTMOST piece does (a mixed-width rule is reported at its first
    /// piece's coordinate, not an average); overlapping pieces are parallel rules
    /// (a table's outer border over its top cells' borders) and the TOP-most such
    /// rule on the page takes the higher y, every other one the lower.</summary>
    private static List<HEdge> MergeCollinearH(List<HEdge> edges)
    {
        var sorted = edges.OrderBy(e => e.Y).ThenBy(e => e.X1).ToList();
        var result = new List<(double y, double x1, double x2, double yMin, double yMax, bool parallel)>();
        foreach (var group in GroupByValue(sorted, e => e.Y, CollinearToleranceY))
        {
            var pieces = group.OrderBy(e => e.X1).ToList();
            var y = pieces[0].Y;
            var x1 = pieces[0].X1; var x2 = pieces[0].X2;
            double yMin = y, yMax = y; var parallel = false;
            foreach (var p in pieces.Skip(1))
            {
                if (p.X1 <= x2 + 1.5)
                {
                    if (p.X1 < x2 - 0.01) parallel = true;
                    x2 = Math.Max(x2, p.X2);
                    yMin = Math.Min(yMin, p.Y); yMax = Math.Max(yMax, p.Y);
                }
                else
                {
                    result.Add((y, x1, x2, yMin, yMax, parallel));
                    y = p.Y; x1 = p.X1; x2 = p.X2; yMin = yMax = y; parallel = false;
                }
            }
            result.Add((y, x1, x2, yMin, yMax, parallel));
        }
        var topY = result.Count > 0 ? result.Max(r => r.yMax) : 0;
        return result.Select(r => new HEdge(
            !r.parallel ? r.y : (r.yMax >= topY - 1e-9 ? r.yMax : r.yMin), r.x1, r.x2)).ToList();
    }

    /// <summary>Merge collinear vertical pieces: same rule (ΔX within
    /// <see cref="CollinearToleranceX"/>) and abutting or overlapping in Y (gap ≤ 1.5) —
    /// dash segments become one rule. Pieces that do not overlap are one rule drawn
    /// in parts and it sits where its TOPMOST piece does (a table whose top row's
    /// side bar is at x + 0.12 reports every row there); overlapping pieces are
    /// parallel rules (an outer border beside the cell borders) and take the lower x.</summary>
    private static List<VEdge> MergeCollinearV(List<VEdge> edges)
    {
        var sorted = edges.OrderBy(e => e.X).ThenBy(e => e.Y1).ToList();
        var result = new List<VEdge>();
        foreach (var group in GroupByValue(sorted, e => e.X, CollinearToleranceX))
        {
            var pieces = group.OrderBy(e => e.Y1).ToList();
            var x = pieces[0].X; var top = pieces[0].Y2; var xMin = x; var parallel = false;
            var y1 = pieces[0].Y1; var y2 = pieces[0].Y2;
            foreach (var p in pieces.Skip(1))
            {
                if (p.Y1 <= y2 + 1.5)
                {
                    if (p.Y1 < y2 - 0.01) parallel = true;
                    y2 = Math.Max(y2, p.Y2);
                    xMin = Math.Min(xMin, p.X);
                    if (p.Y2 > top) { top = p.Y2; x = p.X; }
                }
                else
                {
                    result.Add(new VEdge(parallel ? xMin : x, y1, y2));
                    x = p.X; top = p.Y2; xMin = x; parallel = false; y1 = p.Y1; y2 = p.Y2;
                }
            }
            result.Add(new VEdge(parallel ? xMin : x, y1, y2));
        }
        return result;
    }

    private static List<AbsorbedTable> DetectTablesInRegion(List<TextRun> runs, List<HEdge> hEdges, List<VEdge> vEdges)
    {
        var dr = new RegionTableDetectState();
        dr.runs = runs;
        dr.hEdges = hEdges;
        dr.vEdges = vEdges;
        if (dr.hEdges.Count < 2 || dr.vEdges.Count < 2)
            return [];


        dr.rowBounds = ClusterValues(dr.hEdges.Select(e => e.Y).ToList());
        dr.rowBounds.Sort();
        dr.colBounds = ClusterValues(dr.vEdges.Select(e => e.X).ToList());
        dr.colBounds.Sort();

        // An OPEN side - rules of one direction running past the last rule of the
        // other - closes one point beyond the rules' end: a grid whose row rules
        // reach x 300 with no right-hand column rule reports its last column ending
        // at 301 (probed; the same for an open top, left and bottom).
        const double OpenSideClosure = 1.0;
        dr.rulesRight = dr.hEdges.Max(e => e.X2);
        dr.rulesLeft = dr.hEdges.Min(e => e.X1);
        if (dr.rulesRight > dr.colBounds[^1] + EdgeTol) dr.colBounds.Add(dr.rulesRight + OpenSideClosure);
        if (dr.rulesLeft < dr.colBounds[0] - EdgeTol) dr.colBounds.Insert(0, dr.rulesLeft - OpenSideClosure);
        dr.rulesTop = dr.vEdges.Max(e => e.Y2);
        dr.rulesBottom = dr.vEdges.Min(e => e.Y1);
        if (dr.rulesTop > dr.rowBounds[^1] + EdgeTol) dr.rowBounds.Add(dr.rulesTop + OpenSideClosure);
        if (dr.rulesBottom < dr.rowBounds[0] - EdgeTol) dr.rowBounds.Insert(0, dr.rulesBottom - OpenSideClosure);

        // A row boundary needs meaningful rule coverage: an H rule spanning a
        // sliver of the region (a small box ruled INSIDE one cell) must not
        // slice every column's rows.
        if (dr.rowBounds.Count > 2)
        {
            var regW = Math.Max(1, dr.vEdges.Max(e => e.X) - dr.vEdges.Min(e => e.X));
            dr.rowBounds.RemoveAll(y =>
            {
                var cover = dr.hEdges.Where(e => Math.Abs(e.Y - y) <= EdgeTol)
                    .Sum(e => e.X2 - e.X1);
                return cover < regW * 0.2;
            });
            if (dr.rowBounds.Count < 2) return [];
        }

        // Merge nearby column boundaries that form thin "double-border" gaps.
        // PDFs often draw left+right borders of adjacent columns as separate lines
        // ~20-30pt apart, creating phantom thin columns with no content — but a
        // narrow column that HOLDS text (a score/checkmark column) is real and
        // must survive, so only content-free gaps are collapsed.
        MergeNearbyBoundaries(dr.colBounds, MinCellW * 3, dr.runs);
        MergeNearbyBoundaries(dr.rowBounds, MinCellH * 1.5);

        dr.realColBounds = dr.colBounds.ToList();
        dr.realRowBounds = dr.rowBounds.ToList();

        
        if (dr.rowBounds.Count < 2 || dr.colBounds.Count < 2) return [];

        dr.nRows = dr.rowBounds.Count - 1;
        dr.nCols = dr.colBounds.Count - 1;

        dr.valid = new bool[dr.nRows, dr.nCols];
        dr.validCount = 0;
        MarkValidGridCells(dr);

        dr.totalCells = dr.nRows * dr.nCols;
        RelaxCellSideThreshold(dr);


        dr.visited = new bool[dr.nRows, dr.nCols];
        dr.tables = new List<AbsorbedTable>();

        FloodFillTableComponents(dr);

        // If flood-fill produced too many small tables (due to fragmented grids),
        // fall back to building one big table from all valid cells and splitting at separator rows
        CollapseFragmentedTables(dr);

        // Merge heavily fragmented tables (>6 fragments with same column structure)
        if (dr.tables.Count > 6)
            dr.tables = MergeVerticallyAdjacentTables(dr.tables);

        // Merge single-row tables into adjacent multi-row tables when they share
        // the same X range — these are typically header rows separated by grid gaps.
        dr.tables = MergeSingleRowFragments(dr.tables);

        // Sort tables top-to-bottom (highest Y first), left-to-right on ties
        SortTables(dr.tables);

        return dr.tables;
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
                Rect = Compat.IsFinite(minX) ? new Rectangle(minX, minY, maxX, maxY) : null,
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
            // Same line (Y within tolerance) and adjacent (gap < font height).
            // A whitespace-only run stays its OWN fragment — the ops put the
            // inter-word space in a separate show operator, and the reported
            // fragment carries the word alone ("CaseNo", not "CaseNo ").
            var yDiff = Math.Abs(next.Y - current.Y);
            var gap = next.X - (current.X + current.W);
            if (yDiff < 2.0 && gap < current.H * 0.5
                && !string.IsNullOrWhiteSpace(next.Text)
                && !string.IsNullOrWhiteSpace(current.Text))
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
    /// double-border artifacts common in PDF tables. When <paramref name="runs"/>
    /// is given, a gap that CONTAINS text (a run's centre X falls inside it) is a
    /// real narrow column and is left alone.
    /// </summary>
    private static void MergeNearbyBoundaries(List<double> bounds, double threshold,
        List<TextRun>? runs = null)
    {
        for (int i = bounds.Count - 1; i > 0; i--)
        {
            if (bounds[i] - bounds[i - 1] < threshold)
            {
                if (runs is not null && bounds[i] - bounds[i - 1] >= MinCellW
                    && runs.Any(r =>
                    {
                        var cx = r.X + r.W / 2;
                        return cx > bounds[i - 1] && cx < bounds[i];
                    }))
                    continue;
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
        // An edge only counts as a cell side when it covers a MEANINGFUL part of
        // that side (half the span) - a sliver overlap from a neighbouring box's
        // border must not validate a cell that has no rule of its own.
        var needX = (xRight - xLeft) * 0.5;
        var needY = (yTop - yBot) * 0.5;
        // Top H edge (y ~ yTop, x-span covers [xLeft, xRight])
        if (hEdges.Any(e => Math.Abs(e.Y - yTop) <= EdgeTol
            && Math.Min(e.X2, xRight) - Math.Max(e.X1, xLeft) >= needX)) n++;
        // Bottom H edge
        if (hEdges.Any(e => Math.Abs(e.Y - yBot) <= EdgeTol
            && Math.Min(e.X2, xRight) - Math.Max(e.X1, xLeft) >= needX)) n++;
        // Left V edge (x ~ xLeft, y-span covers [yBot, yTop])
        if (vEdges.Any(e => Math.Abs(e.X - xLeft) <= EdgeTol
            && Math.Min(e.Y2, yTop) - Math.Max(e.Y1, yBot) >= needY)) n++;
        // Right V edge
        if (vEdges.Any(e => Math.Abs(e.X - xRight) <= EdgeTol
            && Math.Min(e.Y2, yTop) - Math.Max(e.Y1, yBot) >= needY)) n++;
        return n;
    }

    /// <summary>Build one or more AbsorbedTables from a connected component of valid grid cells.
    /// Splits at separator rows (rows where all cells are empty) to produce separate tables.</summary>
    private static List<AbsorbedTable> BuildTablesFromComponent(List<(int r, int c)> component, List<double> rowBounds, List<double> colBounds, List<TextRun> runs, List<HEdge>? hEdges = null, List<VEdge>? vEdges = null)
    {
        var bt = new TableComponentBuildState();
        bt.component = component;
        bt.rowBounds = rowBounds;
        bt.colBounds = colBounds;
        bt.runs = runs;
        bt.hEdges = hEdges;
        bt.vEdges = vEdges;
        bt.rowIndices = bt.component.Select(p => p.r).Distinct().OrderBy(r => r).ToList();

        bt.compSet = new HashSet<(int r, int c)>(bt.component);
        bt.consumed = new HashSet<(int r, int c)>();

        bt.allRows = new List<AbsorbedRow>();
        foreach (var r in bt.rowIndices.AsEnumerable().Reverse())
        {
            if (!BuildComponentRow(bt, r)) break;
        }

        if (bt.allRows.Count == 0) return [];

        bt.sections = new List<List<AbsorbedRow>> { bt.allRows };

        bt.result = new List<AbsorbedTable>();
        AssembleTableSections(bt);
        return bt.result;
    }

    /// <summary>The drawn vertical rule nearest <paramref name="x"/> (within
    /// <see cref="EdgeTol"/>) that overlaps the row band — preferring the rule
    /// covering most of the band, then the closest. Null when nothing qualifies.</summary>
    private static double? SnapToVEdge(double x, double yBot, double yTop, List<VEdge>? vEdges)
    {
        if (vEdges is null) return null;
        double? bestX = null;
        double bestOverlap = 0;
        double bestDist = double.MaxValue;
        foreach (var ve in vEdges)
        {
            var dist = Math.Abs(ve.X - x);
            if (dist > EdgeTol) continue;
            var overlap = Math.Min(ve.Y2, yTop) - Math.Max(ve.Y1, yBot);
            if (overlap <= 0) continue;
            if (overlap > bestOverlap + 0.5 || (Math.Abs(overlap - bestOverlap) <= 0.5 && dist < bestDist))
            {
                bestX = ve.X; bestOverlap = overlap; bestDist = dist;
            }
        }
        return bestX;
    }

    /// <summary>
    /// Remove columns that are always empty AND sandwiched between two non-empty columns.
    /// Absorb each dropped column's x-span into the adjacent non-empty cell.
    /// </summary>
    private static List<AbsorbedRow> DropEmptyColumns(List<AbsorbedRow> rows)
    {
        if (rows.Count == 0) return rows;

        // Column identity comes from the cell's X span, not its position in the
        // row — ragged rows (row-spans, colspans) misalign positional indexing.
        var lefts = new List<double>();
        foreach (var row in rows)
            foreach (var cell in row.Cells)
            {
                if (cell.Rect is null) continue;
                if (!lefts.Any(x => Math.Abs(x - cell.Rect.LLX) <= EdgeTol))
                    lefts.Add(cell.Rect.LLX);
            }
        lefts.Sort();
        int ColOf(AbsorbedCell cell) =>
            cell.Rect is null ? -1
            : lefts.FindIndex(x => Math.Abs(x - cell.Rect.LLX) <= EdgeTol);

        var nCols = lefts.Count;
        if (nCols == 0) return rows;

        // Which columns are empty in every row?
        var alwaysEmpty = new bool[nCols];
        for (var c = 0; c < nCols; c++) alwaysEmpty[c] = true;
        foreach (var row in rows)
            foreach (var cell in row.Cells)
            {
                var c = ColOf(cell);
                if (c >= 0 && cell.TextFragments.Count > 0) alwaysEmpty[c] = false;
            }

        // A borders-only table (no text anywhere) keeps its full grid — "empty"
        // carries no signal when every column is empty.
        if (alwaysEmpty.All(e => e)) return rows;
        if (!alwaysEmpty.Any(e => e)) return rows;

        // An always-empty column is "spurious" — sandwiched double-border gaps
        // and decorative edge boxes (an icon/margin box ruled beside the
        // content) alike.
        return rows.Select(row =>
        {
            var newCells = new List<AbsorbedCell>();
            foreach (var cell in row.Cells)
            {
                var c = ColOf(cell);
                if (c >= 0 && alwaysEmpty[c])
                {
                    // Absorb the dropped span into the left neighbour's right edge
                    if (newCells.Count > 0 && cell.Rect is not null && newCells[^1].Rect is not null)
                    {
                        var prev = newCells[^1];
                        var merged = new Rectangle(prev.Rect!.LLX, prev.Rect.LLY, cell.Rect.URX, prev.Rect.URY);
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
                    newCells.Add(cell);
                }
            }
            return new AbsorbedRow { Cells = newCells };
        }).Where(r => r.Cells.Count > 0).ToList();
    }
}
