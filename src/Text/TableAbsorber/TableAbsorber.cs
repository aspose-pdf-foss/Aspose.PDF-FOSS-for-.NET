using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

/// <summary>
/// Detects and extracts tables from PDF pages by analyzing text positions
/// and line drawing operations.
/// </summary>
public sealed partial class TableAbsorber
{
    private static readonly bool TableDebug =
        Environment.GetEnvironmentVariable("ASPOSE_FOSS_TABLEDEBUG") == "1";

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

    /// <summary>Detected tables (returns the mutable backing list).</summary>
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

    /// <summary>Replace <paramref name="oldTable"/> on <paramref name="page"/> with
    /// <paramref name="newTable"/>: the old table's content is removed and the new
    /// table is drawn with its top-left corner on the old table's (a replacement
    /// re-absorbs at the old table's left edge and top edge).</summary>
    public void Replace(Page page, AbsorbedTable oldTable, Aspose.Pdf.Table newTable)
    {
        if (page is null) throw new ArgumentNullException(nameof(page));
        if (oldTable is null) throw new ArgumentNullException(nameof(oldTable));
        if (newTable is null) throw new ArgumentNullException(nameof(newTable));
        if (oldTable.Rect is not { } rect) return;
        RemoveTableContent(page, rect);
        _tables.Remove(oldTable);
        // The replacement's box sits on the old table's edges; its cell borders
        // stroke inside that box, which is what puts the re-absorbed rule half a
        // width in (a 1 pt-bordered replacement of a table at x 90.5 / y 769.5
        // re-absorbs at 91 / 769).
        newTable.Left = (float)rect.LLX;
        newTable.Top = (float)(page.Height - rect.URY);
        page.AddContentStream(newTable.Build(page));
        page.ResetContentsCache();
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

        var tables = DetectTables(textRuns, hEdges, vEdges, page.Width, page.Height);
        var pageFragments = tables.Count > 0 || UseFlowEngine ? AbsorbPageFragments(page) : null;
        if (tables.Count > 0) FillCellsFromPageFragments(tables, pageFragments!);
        if (UseFlowEngine)
        {
            // The flow engine also reads tables that have no rules, from the text
            // outside every ruled table.
            var outside = pageFragments!.Where(f => !tables.Any(t =>
                t.Rect is { } tr && f.Rectangle is { } fr
                && (fr.LLX + fr.URX) / 2 >= tr.LLX && (fr.LLX + fr.URX) / 2 <= tr.URX
                && (fr.LLY + fr.URY) / 2 >= tr.LLY && (fr.LLY + fr.URY) / 2 <= tr.URY)).ToList();
            tables.AddRange(DetectBorderlessTables(outside));
            SortTables(tables);
        }
        _tables.AddRange(tables);

        // After Visit, operators the CALLER
        // appends to page.Contents land in DEFAULT page space even when the
        // original content leaves a dangling page-level transform (print-to-PDF
        // output applies a whole-page flip cm outside any q/Q). A raw append
        // WITHOUT a prior Visit inherits the dangling transform by
        // design - so the wrap happens here, not in the append path.
        NormalizeDanglingContentState(page);
    }

    /// <summary>
    /// A cell's fragments are the page's own text fragments - the ones the
    /// TextFragmentAbsorber reports, with their canonical boxes, assembled spaces
    /// and live editing - whose centre falls inside the cell, in reading order (top
    /// line first, then left to right). The detection pipeline's private runs only
    /// locate the grid.
    /// </summary>
    private static List<TextFragment> AbsorbPageFragments(Page page)
    {
        var absorber = new TextFragmentAbsorber();
        absorber.Visit(page);
        var fragments = new List<TextFragment>();
        foreach (var f in absorber.TextFragments)
            if (f.Rectangle is not null) fragments.Add(f);
        return fragments;
    }

    private static void FillCellsFromPageFragments(List<AbsorbedTable> tables, List<TextFragment> fragments)
    {

        const double CellSlack = 2.0;
        // A run drawn across several cells of a row (a Word header row set as one
        // Tj with wide spaces) is cut at the column rules into one fragment per
        // cell - such a run reports as "INGREDIENTS ", "WEIGHTS/MEASURES ",
        // "SCALE UP", each cut before the first glyph past a rule.
        foreach (var table in tables)
            foreach (var row in table.Rows)
            {
                if (row.Rectangle is not { } rr || row.Cells.Count < 2) continue;
                var bounds = row.Cells.Skip(1).Select(c => c.Rect?.LLX ?? double.NaN)
                    .Where(b => !double.IsNaN(b)).OrderBy(b => b).ToList();
                for (var fi = fragments.Count - 1; fi >= 0; fi--)
                {
                    var f = fragments[fi];
                    var r = f.Rectangle!;
                    var cy = (r.LLY + r.URY) / 2;
                    if (cy < rr.LLY - CellSlack || cy > rr.URY + CellSlack) continue;
                    var crossed = bounds.Where(b => b > r.LLX + CellSlack && b < r.URX - CellSlack).ToList();
                    if (crossed.Count == 0) continue;
                    var pieces = SplitAtColumns(f, crossed);
                    if (pieces.Count < 2) continue;
                    fragments.RemoveAt(fi);
                    fragments.InsertRange(fi, pieces);
                }
            }

        foreach (var table in tables)
            foreach (var row in table.Rows)
                foreach (var cell in row.Cells)
                {
                    if (cell.Rect is not { } rect) continue;
                    var inside = fragments.Where(f =>
                    {
                        var r = f.Rectangle!;
                        var cx = (r.LLX + r.URX) / 2;
                        var cy = (r.LLY + r.URY) / 2;
                        return cx >= rect.LLX - CellSlack && cx <= rect.URX + CellSlack
                            && cy >= rect.LLY - CellSlack && cy <= rect.URY + CellSlack;
                    })
                    .OrderByDescending(f => f.Rectangle!.URY)
                    .ThenBy(f => f.Rectangle!.LLX)
                    .ToList();
                    cell.TextFragments.Inner.Clear();
                    cell.TextFragments.Inner.AddRange(inside);
                }
    }

    /// <summary>Cut a single-segment fragment at the given x boundaries by its
    /// character geometry: a glyph goes to the piece on the side its left edge
    /// lies on. Pieces keep the source page so they stay editable.</summary>
    private static List<TextFragment> SplitAtColumns(TextFragment f, List<double> bounds)
    {
        var result = new List<TextFragment>();
        if (f.Segments.Count != 1) return result;
        var chars = f.Segments[1].Characters;
        var text = f.Text ?? string.Empty;
        if (chars.Count != text.Length || chars.Count == 0) return result;
        var start = 0;
        void Emit(int from, int to)
        {
            if (to <= from) return;
            double llx = double.MaxValue, lly = double.MaxValue, urx = double.MinValue, ury = double.MinValue;
            for (var k = from; k < to; k++)
            {
                var cr = chars[k + 1].Rectangle;
                llx = Math.Min(llx, cr.LLX); lly = Math.Min(lly, cr.LLY);
                urx = Math.Max(urx, cr.URX); ury = Math.Max(ury, cr.URY);
            }
            var piece = new TextFragment(text.Substring(from, to - from), new Rectangle(llx, lly, urx, ury), f.TextState)
            {
                SourcePage = f.SourcePage,
                Form = f.Form,
                PageIndex = f.PageIndex,
                Position = new Position(chars[from + 1].Position.XIndent, f.Position?.YIndent ?? lly),
            };
            if (piece.Segments.Count > 0)
            {
                piece.Segments[1].Rectangle = piece.Rectangle;
                piece.Segments[1].Position = piece.Position;
            }
            result.Add(piece);
        }
        foreach (var b in bounds)
        {
            var cut = start;
            while (cut < chars.Count && chars[cut + 1].Rectangle.LLX < b) cut++;
            Emit(start, cut);
            start = cut;
        }
        Emit(start, chars.Count);
        return result;
    }

    /// <summary>Wrap the page content in q..Q when it leaves a non-identity CTM
    /// at stream end with a balanced q/Q stack, so subsequently appended operators
    /// are interpreted in default page space.</summary>
    private static void NormalizeDanglingContentState(Page page)
    {
        double a = 1, b = 0, c = 0, d = 1, e = 0, f = 0;
        var depth = 0;
        var stack = new List<(double a, double b, double c, double d, double e, double f)>();
        foreach (var opText in page.Contents.PeekOps())
        {
            var t = opText.TrimEnd();
            if (t == "q") { stack.Add((a, b, c, d, e, f)); depth++; }
            else if (t == "Q")
            {
                if (stack.Count > 0)
                {
                    (a, b, c, d, e, f) = stack[^1];
                    stack.RemoveAt(stack.Count - 1);
                    depth--;
                }
            }
            else if (t.EndsWith(" cm", StringComparison.Ordinal))
            {
                var parts = t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 7
                    && double.TryParse(parts[^7], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ma)
                    && double.TryParse(parts[^6], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var mb)
                    && double.TryParse(parts[^5], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var mc)
                    && double.TryParse(parts[^4], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var md)
                    && double.TryParse(parts[^3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var me)
                    && double.TryParse(parts[^2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var mf))
                {
                    (a, b, c, d, e, f) = (
                        ma * a + mb * c, ma * b + mb * d,
                        mc * a + md * c, mc * b + md * d,
                        me * a + mf * c + e, me * b + mf * d + f);
                }
            }
        }
        var identity = Math.Abs(a - 1) < 1e-9 && Math.Abs(b) < 1e-9 && Math.Abs(c) < 1e-9
            && Math.Abs(d - 1) < 1e-9 && Math.Abs(e) < 1e-9 && Math.Abs(f) < 1e-9;
        if (identity || depth != 0) return;

        var appender = new ContentsAppender(page);
        appender.AppendToBegin(new Aspose.Pdf.Operators.GSave());
        appender.AppendToEnd(new Aspose.Pdf.Operators.GRestore());
        appender.UpdateData();
    }

    // ── Internal types ──────────────────────────────────────────────────

    // ── Constants (matching TypeScript implementation) ────────────────────

    // ── Detection pipeline (matches TypeScript algorithm) ────────────────

    /// <summary>Group a pre-sorted list into runs whose key values chain within
    /// <paramref name="tol"/> of the previous element.</summary>
    private static IEnumerable<List<T>> GroupByValue<T>(List<T> sorted, Func<T, double> key, double tol)
    {
        var group = new List<T>();
        foreach (var item in sorted)
        {
            if (group.Count > 0 && key(item) - key(group[^1]) > tol)
            {
                yield return group;
                group = [];
            }
            group.Add(item);
        }
        if (group.Count > 0) yield return group;
    }

    /// <summary>Fold components that cannot form a grid on their own (all H rules on
    /// one line, or lone rules) into the grid-capable component whose columns their
    /// verticals align with. An open-sided data row — full-width bottom line plus
    /// side stubs — rejoins the header box drawn above it.</summary>
    private static List<(List<HEdge> H, List<VEdge> V)> MergeDegenerateComponents(
        List<(List<HEdge> H, List<VEdge> V)> components)
    {
        static bool GridCapable((List<HEdge> H, List<VEdge> V) c) =>
            c.H.Count >= 2 && c.V.Count >= 2
            && ClusterValues(c.H.Select(e => e.Y).ToList()).Count >= 2
            && ClusterValues(c.V.Select(e => e.X).ToList()).Count >= 2;

        var capable = components.Where(GridCapable).ToList();
        var degenerate = components.Where(c => !GridCapable(c)).ToList();
        if (capable.Count == 0 || degenerate.Count == 0) return components;

        var leftovers = new List<(List<HEdge> H, List<VEdge> V)>();
        foreach (var deg in degenerate)
        {
            if (deg.V.Count == 0) { leftovers.Add(deg); continue; } // nothing to align on
            var degXs = deg.V.Select(e => e.X).ToList();
            var degMinY = deg.V.Min(e => e.Y1);
            var degMaxY = Math.Max(deg.V.Max(e => e.Y2), deg.H.Count > 0 ? deg.H.Max(e => e.Y) : double.MinValue);

            (List<HEdge> H, List<VEdge> V)? best = null;
            var bestScore = 0;
            var bestGap = double.MaxValue;
            foreach (var cap in capable)
            {
                var capXs = cap.V.Select(e => e.X).Distinct().ToList();
                var score = degXs.Count(x => capXs.Any(cx => Math.Abs(cx - x) <= EdgeTol));
                if (score * 2 < degXs.Count) continue; // most stubs must land on columns
                var capMinY = Math.Min(cap.V.Min(e => e.Y1), cap.H.Min(e => e.Y));
                var capMaxY = Math.Max(cap.V.Max(e => e.Y2), cap.H.Max(e => e.Y));
                var gap = capMinY > degMaxY ? capMinY - degMaxY
                        : degMinY > capMaxY ? degMinY - capMaxY : 0;
                if (gap > 30) continue;
                if (score > bestScore || (score == bestScore && gap < bestGap))
                {
                    best = cap; bestScore = score; bestGap = gap;
                }
            }
            if (best is { } target)
            {
                target.H.AddRange(deg.H);
                target.V.AddRange(deg.V);
            }
            else
            {
                leftovers.Add(deg); // keep unmatched pieces as their own component
            }
        }
        capable.AddRange(leftovers);
        return capable;
    }

    /// <summary>Split a component's rules into vertical bands at table seams: two
    /// long parallel H rules a few points apart with no vertical rule crossing the
    /// strip between them and a materially different column set on each side.</summary>
    private static List<(List<HEdge> H, List<VEdge> V)> SplitComponentBands(
        List<HEdge> hEdges, List<VEdge> vEdges)
    {
        var ys = hEdges.Select(e => e.Y).Distinct().OrderByDescending(y => y).ToList();
        var cuts = new List<double>();
        for (var i = 1; i < ys.Count; i++)
        {
            var yHi = ys[i - 1];
            var yLo = ys[i];
            var gap = yHi - yLo;
            if (gap <= 1.5 || gap >= 8) continue;
            // A vertical rule crossing the strip means the strip is a (thin) row.
            var crossed = vEdges.Any(ve =>
                Math.Min(ve.Y2, yHi - 0.5) - Math.Max(ve.Y1, yLo + 0.5) > 0);
            if (crossed) continue;
            // Column sets: verticals ending at/above the strip vs starting at/below.
            var above = vEdges.Where(ve => ve.Y1 >= yHi - 0.75).Select(ve => ve.X).Distinct().ToList();
            var below = vEdges.Where(ve => ve.Y2 <= yLo + 0.75).Select(ve => ve.X).Distinct().ToList();
            if (above.Count == 0 || below.Count == 0) continue;
            var unmatched =
                above.Count(x => !below.Any(b => Math.Abs(b - x) <= EdgeTol)) +
                below.Count(x => !above.Any(a => Math.Abs(a - x) <= EdgeTol));
            if (unmatched < 2) continue; // same grid continuing — a double rule, not a seam
            cuts.Add((yHi + yLo) / 2);
        }
        if (cuts.Count == 0) return [(hEdges, vEdges)];

        cuts.Sort();
        var bands = new List<(List<HEdge> H, List<VEdge> V)>();
        for (var i = 0; i <= cuts.Count; i++)
        {
            var lo = i == 0 ? double.MinValue : cuts[i - 1];
            var hi = i == cuts.Count ? double.MaxValue : cuts[i];
            bands.Add((
                hEdges.Where(e => e.Y > lo && e.Y <= hi).ToList(),
                vEdges.Where(e => (e.Y1 + e.Y2) / 2 > lo && (e.Y1 + e.Y2) / 2 <= hi).ToList()));
        }
        return bands.Where(b => b.H.Count > 0 || b.V.Count > 0).ToList();
    }

    /// <summary>Union-find decomposition of the rule lattice: a horizontal and a
    /// vertical rule are connected when they intersect (within <see cref="EdgeTol"/>);
    /// horizontal pieces of one ruled line join through the shared verticals they
    /// cross. Returns each component's edges.</summary>
    private static List<(List<HEdge> H, List<VEdge> V)> BuildLatticeComponents(
        List<HEdge> hEdges, List<VEdge> vEdges)
    {
        var n = hEdges.Count + vEdges.Count;
        var parent = new int[n];
        for (var i = 0; i < n; i++) parent[i] = i;
        int Find(int i) { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; }
        void Union(int a, int b) { var ra = Find(a); var rb = Find(b); if (ra != rb) parent[ra] = rb; }

        for (var hi = 0; hi < hEdges.Count; hi++)
        {
            var he = hEdges[hi];
            for (var vi = 0; vi < vEdges.Count; vi++)
            {
                var ve = vEdges[vi];
                if (ve.X >= he.X1 - EdgeTol && ve.X <= he.X2 + EdgeTol &&
                    he.Y >= ve.Y1 - EdgeTol && he.Y <= ve.Y2 + EdgeTol)
                    Union(hi, hEdges.Count + vi);
            }
        }

        var byRoot = new Dictionary<int, (List<HEdge> H, List<VEdge> V)>();
        for (var hi = 0; hi < hEdges.Count; hi++)
        {
            var r = Find(hi);
            if (!byRoot.TryGetValue(r, out var comp)) byRoot[r] = comp = (new List<HEdge>(), new List<VEdge>());
            comp.H.Add(hEdges[hi]);
        }
        for (var vi = 0; vi < vEdges.Count; vi++)
        {
            var r = Find(hEdges.Count + vi);
            if (!byRoot.TryGetValue(r, out var comp)) byRoot[r] = comp = (new List<HEdge>(), new List<VEdge>());
            comp.V.Add(vEdges[vi]);
        }
        return byRoot.Values.ToList();
    }

    /// <summary>
    /// Cluster horizontal-edge Y positions into bands separated by large vertical gaps.
    /// Two tables stacked with a wide blank strip between them fall into separate bands;
    /// a single table's inter-row gaps stay within one band. Returns each band's
    /// (minY, maxY) span. A gap threshold well above normal row spacing keeps a single
    /// table (even one with a tall row) intact — only genuine table-to-table gaps split.
    /// </summary>
    private static List<(double Min, double Max)> ComputeYBands(List<HEdge> hEdges)
    {
        var ys = hEdges.Select(e => e.Y).Distinct().ToList();
        ys.Sort();
        var bands = new List<(double Min, double Max)>();
        if (ys.Count == 0) return bands;

        // A band break is a blank vertical strip much larger than typical row spacing.
        const double BandGap = 40;
        double start = ys[0];
        double prev = ys[0];
        for (var i = 1; i < ys.Count; i++)
        {
            if (ys[i] - prev > BandGap)
            {
                bands.Add((start, prev));
                start = ys[i];
            }
            prev = ys[i];
        }
        bands.Add((start, prev));
        return bands;
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

    // ── Text-based fallback detection ──────────────────────────────────

    // ── Remove table content from page stream ───────────────────────────

    /// <summary>Transforms a point using the given matrix and returns the result.</summary>
    private static (double x, double y) TransformPoint(PdfObject xObj, PdfObject yObj,
        double a, double b, double c, double d, double e, double f)
        => ApplyMatrix(Num(xObj), Num(yObj), a, b, c, d, e, f);

    // ── Page rotation CTM ───────────────────────────────────────────────

    private static (double a, double b, double c, double d, double e, double f) PageRotationCtm(Page page)
    {
        var rotate = ((page.RotateDegrees % 360) + 360) % 360;
        var mb = page.MediaBox; var w = mb.URX - mb.LLX; var h = mb.URY - mb.LLY;
        return rotate switch
        { 90 => (0,-1,1,0,0,w), 180 => (-1,0,0,-1,w,h), 270 => (0,1,-1,0,h,0), _ => (1,0,0,1,0,0) };
    }

    // ── Content stream extraction ───────────────────────────────────────

    private static (double x, double y) ApplyMatrix(double x, double y, double a, double b, double c, double d, double e, double f)
        => (a * x + c * y + e, b * x + d * y + f);

}
