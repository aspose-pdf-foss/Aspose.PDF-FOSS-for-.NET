using System;
using System.Collections.Generic;
using System.Linq;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Tagged;

internal static partial class AutoTagger
{
    // Header cells stand out: every one of them bold, while under this share of the body cells is.
    private const double BodyBoldShare = 0.5;
    // ... or they name what the figures under them are: under this share of a header row's cells
    // are numeric while at least NumericBodyShare of the body's are.
    private const double HeaderNumericShare = 0.5;
    private const double NumericBodyShare = 0.7;
    // At most this many leading rows are header rows, over at least this many body rows.
    private const int MaxHeaderRows = 6;
    private const int MinBodyRows = 2;
    // A numeric cell: a figure, money, a percentage, or a dash for none - with the letter of a note after it, if any
    // ("124.0 e", estimated).
    private static readonly System.Text.RegularExpressions.Regex NumericCell = new(
        @"^[\s$€£(\-–—]*(\d[\d,.\s]*%?|[-–—.]+)[)\s]*(\s[a-z])?\s*$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>How many leading rows of a table are header rows - the rows set in bold over a
    /// body that is not, or the rows of text over a body of figures - and whether its first
    /// column is a header column (set in bold likewise). Header cells are TH with a scope
    /// (PDF/UA-1 §7.5), not data cells.</summary>
    private static (int Rows, bool Col) TableHeaders(
        (Aspose.Pdf.Rectangle region, int rows, int cols, List<double> colX, List<double> rowY) t, List<Line> lines)
    {
        if (t.rows < 2) return (0, false);
        var (bold, text) = ScanCells(t, lines);

        bool AllBold(IEnumerable<(int R, int C)> cells)
        {
            var filled = cells.Where(c => bold[c.R, c.C] is not null).ToList();
            return filled.Count > 0 && filled.All(c => bold[c.R, c.C] == true);
        }
        bool BodyPlain(IEnumerable<(int R, int C)> cells)
        {
            var filled = cells.Where(c => bold[c.R, c.C] is not null).ToList();
            return filled.Count > 0 && filled.Count(c => bold[c.R, c.C] == true) < BodyBoldShare * filled.Count;
        }
        double NumericShare(IEnumerable<(int R, int C)> cells)
        {
            var filled = cells.Where(c => text[c.R, c.C] is not null).ToList();
            return filled.Count == 0 ? -1 : (double)filled.Count(c => NumericCell.IsMatch(text[c.R, c.C]!.ToString())) / filled.Count;
        }

        var all = (from r in Enumerable.Range(0, t.rows) from c in Enumerable.Range(0, t.cols) select (r, c)).ToList();
        // The leading rows all in bold, when the body under them is not.
        var boldRows = 0;
        while (boldRows < Math.Min(MaxHeaderRows, t.rows - 1) && AllBold(all.Where(x => x.r == boldRows))) boldRows++;
        var headerRows = boldRows > 0 && BodyPlain(all.Where(x => x.r >= boldRows)) ? boldRows : 0;
        // Else the longest run of leading rows ending in a row of text, over a body of figures
        // (a row of column numbers may stand among the header rows).
        for (var k = Math.Min(MaxHeaderRows, t.rows - MinBodyRows); headerRows == 0 && k >= 1; k--)
        {
            var body = NumericShare(all.Where(x => x.r >= k));
            var last = NumericShare(all.Where(x => x.r == k - 1));
            if (body >= NumericBodyShare && last >= 0 && last < HeaderNumericShare) headerRows = k;
        }
        var headerCol = t.cols >= 2
            && AllBold(all.Where(x => x.c == 0 && x.r >= headerRows))
            && BodyPlain(all.Where(x => x.c > 0 && x.r >= headerRows));
        return (headerRows, headerCol);
    }

    /// <summary>Each grid cell's text (null: none) and whether all of it is bold.</summary>
    private static (bool?[,] Bold, System.Text.StringBuilder?[,] Text) ScanCells(
        (Aspose.Pdf.Rectangle region, int rows, int cols, List<double> colX, List<double> rowY) t, List<Line> lines)
    {
        var bold = new bool?[t.rows, t.cols];
        var text = new System.Text.StringBuilder?[t.rows, t.cols];
        foreach (var line in lines.Where(l => InRegion(l, t.region)))
        {
            foreach (var f in line.Frags.Where(f => !string.IsNullOrWhiteSpace(f.Text)))
            {
                var cx = (f.X + f.R) / 2;
                var cy = f.Base + 0.3 * f.Size;
                // Text standing outside the grid's rows (a line just over or under the table) is none of its cells'.
                if (cy < t.rowY[0] || cy >= t.rowY[^1]) continue;
                var col = 0;
                while (col < t.cols - 1 && cx >= t.colX[col + 1]) col++;
                var fromBottom = 0;
                while (fromBottom < t.rows - 1 && cy >= t.rowY[fromBottom + 1]) fromBottom++;
                var row = t.rows - 1 - fromBottom;
                bold[row, col] = (bold[row, col] ?? true) && IsBold(f.Font);
                (text[row, col] ??= new()).Append(f.Text);
            }
        }
        return (bold, text);
    }

    // The cells of a table are set at the page's text size: text larger than this many times
    // it in a grid's rows is a cover's framed title.
    private const double GridTextFactor = 1.3;

    /// <summary>The grid cell (row from the top, column) a fragment's centre stands in; null
    /// outside the grid (text in the column beside the table is no cell's).</summary>
    private static (int Row, int Col)? GridCellOf((Aspose.Pdf.Rectangle region, int rows, int cols, List<double> colX, List<double> rowY) t, (double X, double R, string Text, double Y, double Base, double Size, string? Font) f)
    {
        var cx = (f.X + f.R) / 2;
        var cy = f.Base + 0.3 * f.Size;
        if (cx < t.colX[0] - GridLineTolerance || cx > t.colX[^1] + GridLineTolerance || cy < t.rowY[0] || cy >= t.rowY[^1]) return null;
        var col = 0;
        while (col < t.cols - 1 && cx >= t.colX[col + 1]) col++;
        var fromBottom = 0;
        while (fromBottom < t.rows - 1 && cy >= t.rowY[fromBottom + 1]) fromBottom++;
        return (t.rows - 1 - fromBottom, col);
    }

    private static IEnumerable<(double X, double R, string Text, double Y, double Base, double Size, string? Font)> GridText((Aspose.Pdf.Rectangle region, int rows, int cols, List<double> colX, List<double> rowY) t, List<Line> lines)
        => lines.Where(l => InRegion(l, t.region)).SelectMany(l => l.Frags).Where(f => !string.IsNullOrWhiteSpace(f.Text));

    // A band whose text runs across the grid's column edges (a paragraph or a title framed with
    // the table) is no row of it when taller than this many times the tallest row of cells, or
    // when it holds this many lines or more (a paragraph; a spanning header has one or two).
    private const double CrossingBandFactor = 4.0;
    private const int CrossingBandLines = 3;

    /// <summary>What a row of the grid holds: how many of its cells hold text, whether a line's
    /// text runs across one of the grid's interior column edges, how many lines stand in it and
    /// the size most of its text is set in.</summary>
    private readonly record struct GridRow(int Cells, bool Crosses, int Lines, double Size);

    private static GridRow[] RowCells((Aspose.Pdf.Rectangle region, int rows, int cols, List<double> colX, List<double> rowY) t, List<Line> lines)
    {
        var filled = new bool[t.rows, t.cols];
        var crosses = new bool[t.rows];
        var baselines = Enumerable.Range(0, t.rows).Select(_ => new HashSet<double>()).ToArray();
        var sizes = Enumerable.Range(0, t.rows).Select(_ => new List<double>()).ToArray();
        foreach (var line in lines.Where(l => InRegion(l, t.region)))
        {
            foreach (var f in line.Frags.Where(f => !string.IsNullOrWhiteSpace(f.Text)))
            {
                if (GridCellOf(t, f) is not { } cell) continue;
                filled[cell.Row, cell.Col] = true;
                baselines[cell.Row].Add(Math.Round(f.Base));
                sizes[cell.Row].Add(f.Size);
            }
            foreach (var (x, r) in Cells(line))
            {
                var mid = line.Frags.FirstOrDefault(f => f.X >= x - 0.5 && f.R <= r + 0.5);
                if (mid.Text is null || GridCellOf(t, mid) is not { } cell) continue;
                if (Enumerable.Range(1, t.cols - 1).Any(k => x < t.colX[k] - GridLineTolerance && r > t.colX[k] + GridLineTolerance))
                    crosses[cell.Row] = true;
            }
        }
        return Enumerable.Range(0, t.rows).Select(r => new GridRow(Enumerable.Range(0, t.cols).Count(c => filled[r, c]), crosses[r],
            baselines[r].Count, sizes[r].Count == 0 ? 0 : sizes[r].OrderBy(v => v).ElementAt(sizes[r].Count / 2))).ToArray();
    }

    /// <summary>Whether a row of the grid is a row of cells: text in two cells or more, none
    /// of it running across a column edge.</summary>
    private static bool IsCellRow(GridRow row) => row.Cells >= 2 && !row.Crosses;

    /// <summary>The table without the bands at its ends that are no rows of it: an empty one,
    /// one set larger than the page's text (a title), one holding text in one cell while
    /// standing taller than <see cref="ApartBandFactor"/> times the tallest row of cells, or
    /// one whose text runs across the column edges while standing taller than
    /// <see cref="CrossingBandFactor"/> times it or holding <see cref="CrossingBandLines"/>
    /// lines or more (a cover's title framed with the table, the paragraph a column's rule
    /// runs beside). <paramref name="bodySize"/> is the page's.</summary>
    private static (Aspose.Pdf.Rectangle region, int rows, int cols, List<double> colX, List<double> rowY) TrimEndBands(
        (Aspose.Pdf.Rectangle region, int rows, int cols, List<double> colX, List<double> rowY) t, List<Line> content, double bodySize)
    {
        if (t.rowY.Count != t.rows + 1 || t.rows < 2) return t;
        var rows = RowCells(t, content);
        double Height(int r) => t.rowY[t.rows - r] - t.rowY[t.rows - 1 - r];
        var dataRow = Enumerable.Range(0, t.rows).Where(r => IsCellRow(rows[r])).Select(Height).DefaultIfEmpty(0).Max();
        bool Apart(int r) => rows[r].Cells == 0
                             || rows[r].Size > GridTextFactor * bodySize
                             || (rows[r].Cells == 1 && Height(r) > ApartBandFactor * dataRow)
                             || (rows[r].Crosses && (Height(r) > CrossingBandFactor * dataRow || rows[r].Lines >= CrossingBandLines));
        int first = 0, last = t.rows - 1;
        while (first < last && Apart(first)) first++;
        while (last > first && Apart(last)) last--;
        if (first == 0 && last == t.rows - 1) return t;
        var rowY = t.rowY.GetRange(t.rows - 1 - last, last - first + 2);
        return (new Aspose.Pdf.Rectangle(t.region.LLX, rowY[0], t.region.URX, rowY[^1]), last - first + 1, t.cols, t.colX, rowY);
    }

    /// <summary>Whether a ruled grid is a table: two columns or more, and a row of cells
    /// (<see cref="IsCellRow"/>) set at the page's text size (a framed cover block holds the
    /// title, a boxed paragraph has one column). <paramref name="bodySize"/> is the page's.</summary>
    private static bool IsGrid((Aspose.Pdf.Rectangle region, int rows, int cols, List<double> colX, List<double> rowY) t, List<Line> content, double bodySize)
    {
        if (t.cols < 2 || t.rowY.Count != t.rows + 1) return false;
        var rows = RowCells(t, content);
        var sizes = GridText(t, content).Select(f => (Cell: GridCellOf(t, f), f.Size))
            .Where(x => x.Cell is { } cell && IsCellRow(rows[cell.Row])).Select(x => x.Size).OrderBy(v => v).ToList();
        return sizes.Count > 0 && sizes[sizes.Count / 2] <= GridTextFactor * bodySize;
    }

    // A band of bold rows inside a table is a header band (a table stacked under another) when
    // it holds at least this many rows; one bold row among the data is a section's label or a total.
    private const int MinInteriorHeaderRows = 2;
    // A part split off a table with fewer rows than this is a line between the tables, no table.
    private const int MinTableRows = 2;

    /// <summary>A ruled table holding a band of bold rows after its data rows - the header of a
    /// second table stacked under the first, framed with it - is two tables: it splits above
    /// each such band (a band ending the table heads the table under it, see
    /// <see cref="JoinHeaderBands"/>). A single row split off at the top is the table's title
    /// (with whatever text stood beside it), no table, nor is any other single row split off.</summary>
    private static IEnumerable<(Aspose.Pdf.Rectangle region, int rows, int cols, List<double> colX, List<double> rowY)> SplitAtInteriorHeaders(
        (Aspose.Pdf.Rectangle region, int rows, int cols, List<double> colX, List<double> rowY) t, List<Line> lines)
    {
        var (bold, _) = ScanCells(t, lines);
        // A row is bold when every cell of it with text is; plain when one is not.
        bool RowBold(int r) => Enumerable.Range(0, t.cols).Where(c => bold[r, c] is not null).ToList() is { Count: > 0 } filled
                               && filled.All(c => bold[r, c] == true);
        bool RowPlain(int r) => Enumerable.Range(0, t.cols).Any(c => bold[r, c] == false);
        var start = 0;
        for (var r = 1; r < t.rows; r++)
        {
            if (!RowBold(r) || !RowPlain(r - 1)) continue;
            var end = r;
            while (end < t.rows && RowBold(end)) end++;
            if (end - r >= MinInteriorHeaderRows && (end == t.rows || RowPlain(end)))
            {
                if (r - start >= MinTableRows) yield return RowsOf(t, start, r);
                start = r;
            }
            r = end - 1;
        }
        yield return start == 0 ? t : RowsOf(t, start, t.rows);
    }

    /// <summary>A table of bold rows only, standing right over a table with the same columns,
    /// is that table's header band (a header band that ended the framed table above it, over
    /// rows the rules did not frame): the two become one table, the band's cells spanning the
    /// columns their text does.</summary>
    private static void JoinHeaderBands(
        List<(Aspose.Pdf.Rectangle region, int rows, int cols, List<double> colX, List<double> rowY)> tables,
        List<(int Row, int Col)[,]> cells, List<Line> lines)
    {
        for (var i = 0; i < tables.Count; i++)
        {
            var band = tables[i];
            if (band.rows > MaxHeaderRows) continue;
            var size = lines.Where(l => InRegion(l, band.region)).Select(l => l.Size).DefaultIfEmpty(0).Max();
            if (size == 0) continue;
            var tolerance = Math.Max(ColumnEdgeTolerance, ColumnEdgeShare * (band.colX[^1] - band.colX[0]));
            // The same columns: as many, each edge of the table under the band between the band's
            // neighbouring edges (a ruled grid's edges and an unruled table's midpoints differ).
            bool SameColumns(List<double> other) => Enumerable.Range(0, other.Count).All(c =>
                other[c] >= band.colX[Math.Max(0, c - 1)] - tolerance && other[c] <= band.colX[Math.Min(band.colX.Count - 1, c + 1)] + tolerance);
            var j = tables.FindIndex(t => t.cols == band.cols && !ReferenceEquals(t.colX, band.colX)
                                          && band.rowY[0] - t.rowY[^1] is var gap && gap >= -tolerance && gap <= size
                                          && SameColumns(t.colX));
            if (j < 0) continue;
            var (bold, _) = ScanCells(band, lines);
            if (!Enumerable.Range(0, band.rows).All(r => Enumerable.Range(0, band.cols).Where(c => bold[r, c] is not null).ToList() is { Count: > 0 } filled
                                                          && filled.All(c => bold[r, c] == true))) continue;
            var below = tables[j];
            var rowY = new List<double>(below.rowY);
            rowY.AddRange(band.rowY.Skip(1));
            var joined = (new Aspose.Pdf.Rectangle(below.region.LLX, below.region.LLY, below.region.URX, band.region.URY),
                band.rows + below.rows, band.cols, below.colX, rowY);
            var map = SingleCells(joined.Item2, band.cols);
            for (var r = 0; r < band.rows; r++)
            {
                double top = band.rowY[band.rows - r], bottom = band.rowY[band.rows - 1 - r];
                var rowCells = lines.Where(l => InRegion(l, band.region) && l.Baseline + 0.3 * l.Size >= bottom && l.Baseline + 0.3 * l.Size < top)
                    .SelectMany(Cells).OrderBy(c => c.X).ToList();
                SpanRow(map, joined, r, rowCells);
            }
            for (var r = 0; r < below.rows; r++)
                for (var c = 0; c < band.cols; c++)
                    map[band.rows + r, c] = (cells[j][r, c].Row + band.rows, cells[j][r, c].Col);
            tables[j] = joined;
            cells[j] = map;
            tables.RemoveAt(i);
            cells.RemoveAt(i);
            i--;
        }
    }

    /// <summary>Row <paramref name="r"/> of a table's cells from the cells of text standing in it,
    /// each over the columns its text covers: from the column it starts in to the column before
    /// the last column edge in the gap after it (a header cell reaches to the cell after it), or
    /// to the column it ends in when no edge lies there.</summary>
    private static void SpanRow((int Row, int Col)[,] map,
        (Aspose.Pdf.Rectangle region, int rows, int cols, List<double> colX, List<double> rowY) t, int r, List<(double X, double R)> rowCells)
    {
        int ColumnOf(double x) => Math.Max(0, Math.Min(t.cols - 1, t.colX.FindLastIndex(edge => edge <= x)));
        var start = 0;
        for (var k = 0; k < rowCells.Count && start < t.cols; k++)
        {
            start = Math.Max(start, ColumnOf(rowCells[k].X));
            // A heading set right over its figures may overhang its column's edge a little (tight columns).
            var edge = k < rowCells.Count - 1 ? t.colX.FindLastIndex(t.cols - 1, t.cols - 1, x => x > rowCells[k].R - AlignTolerance && x < rowCells[k + 1].X + AlignTolerance) : -1;
            var end = Math.Max(start, edge > 0 ? edge - 1 : ColumnOf(rowCells[k].R - AlignTolerance));
            for (var col = start; col <= end; col++) map[r, col] = (r, start);
            start = end + 1;
        }
    }

    /// <summary>The table's rows from <paramref name="from"/> up to <paramref name="to"/> as a table of their own.</summary>
    private static (Aspose.Pdf.Rectangle region, int rows, int cols, List<double> colX, List<double> rowY) RowsOf(
        (Aspose.Pdf.Rectangle region, int rows, int cols, List<double> colX, List<double> rowY) t, int from, int to)
    {
        // rowY runs bottom to top: row r (from the top) lies between rowY[rows - 1 - r] and rowY[rows - r].
        var rowY = t.rowY.GetRange(t.rows - to, to - from + 1);
        return (new Aspose.Pdf.Rectangle(t.region.LLX, rowY[0], t.region.URX, rowY[^1]), to - from, t.cols, t.colX, rowY);
    }

    // Pieces of a rule this near end to end (points) are one rule drawn column by column.
    private const double PieceJoin = 2.0;
    // A line of a table's head, or of its last row, stands at most this many of its size from the line or rule before it.
    private const double BandLineEms = 2.0;

    /// <summary>Tables ruled between their rows only, each rule drawn in pieces, one a column: rules at two heights
    /// or more, cut at the same places, are a table's row edges, the cuts its column edges - nothing is drawn down
    /// the table, so the columns' edges are rules not drawn (added to <paramref name="rules"/>). The lines of the
    /// table's type over the first rule, each of their parts within a column, are its head; those under the last
    /// rule its last row.</summary>
    private static List<(Aspose.Pdf.Rectangle region, int rows, int cols, List<double> colX, List<double> rowY)> PieceRuledTables(
        List<PageContentScan.Rule> rules, List<Line> lines, List<(Aspose.Pdf.Rectangle region, int rows, int cols, List<double> colX, List<double> rowY)> known)
    {
        var tables = new List<(Aspose.Pdf.Rectangle, int, int, List<double>, List<double>)>();
        var drawn = rules.Where(r => r.Horizontal && r.Drawn && r.To - r.From >= MinRuleLength).ToList();
        var chains = new List<(double At, List<double> Cuts)>();
        foreach (var at in ClusterValues(drawn.Select(r => r.At), PieceJoin))
        {
            List<double>? cuts = null;
            foreach (var piece in drawn.Where(r => Math.Abs(r.At - at) <= PieceJoin).OrderBy(r => r.From))
            {
                if (cuts is not null && Math.Abs(piece.From - cuts[^1]) <= PieceJoin) { cuts.Add(piece.To); continue; }
                if (cuts is { Count: > 2 }) chains.Add((at, cuts));
                cuts = [piece.From, piece.To];
            }
            if (cuts is { Count: > 2 }) chains.Add((at, cuts));
        }
        var taken = new HashSet<int>();
        for (var i = 0; i < chains.Count; i++)
        {
            if (taken.Contains(i)) continue;
            var alike = Enumerable.Range(i, chains.Count - i).Where(k => !taken.Contains(k) && chains[k].Cuts.Count == chains[i].Cuts.Count
                && chains[k].Cuts.Zip(chains[i].Cuts, (a, b) => Math.Abs(a - b)).All(d => d <= GridLineTolerance)).ToList();
            foreach (var k in alike) taken.Add(k);
            if (alike.Count < 2) continue;
            var colX = Enumerable.Range(0, chains[i].Cuts.Count).Select(c => alike.Average(k => chains[k].Cuts[c])).ToList();
            var rowY = alike.Select(k => chains[k].At).OrderBy(y => y).ToList();
            if (known.Any(t => t.region.LLX < colX[^1] && t.region.URX > colX[0] && t.region.LLY < rowY[^1] && t.region.URY > rowY[0])) continue;
            if (PieceRuled(colX, rowY, lines) is not { } table) continue;
            for (var c = 1; c < table.colX.Count - 1; c++)
                rules.Add(new PageContentScan.Rule(false, table.colX[c], table.rowY[0], table.rowY[^1], Drawn: false));
            // The edges of the head and of the last row are where their text shows them.
            foreach (var y in new[] { table.rowY[0], table.rowY[^1] }.Where(y => !rowY.Contains(y)))
                rules.Add(new PageContentScan.Rule(true, y, table.colX[0], table.colX[^1], Drawn: false));
            tables.Add(table);
        }
        return tables;
    }

    /// <summary>The table rules drawn column by column rule the rows of, its head and last row taken in; null when
    /// its rows hold no text.</summary>
    private static (Aspose.Pdf.Rectangle region, int rows, int cols, List<double> colX, List<double> rowY)? PieceRuled(
        List<double> colX, List<double> rowY, List<Line> lines)
    {
        // A line's parts standing across the table, and whether each stands within one column of it.
        List<(double X, double R, double Y, double Size)> Across(Line l) => l.Frags.Where(f => !string.IsNullOrWhiteSpace(f.Text)
            && f.R > colX[0] && f.X < colX[^1]).Select(f => (f.X, f.R, f.Y, f.Size)).ToList();
        bool InColumns(List<(double X, double R, double Y, double Size)> parts) => parts.All(f =>
            Enumerable.Range(0, colX.Count - 1).Any(c => f.X >= colX[c] - 1 && f.R <= colX[c + 1] + 1));
        var content = lines.Where(l => l.Running == 0).Select(l => (Line: l, Parts: Across(l))).Where(l => l.Parts.Count > 0).ToList();
        var inside = content.Where(l => l.Line.Y >= rowY[0] && l.Line.Y < rowY[^1]).ToList();
        if (inside.Count == 0 || !inside.All(l => InColumns(l.Parts))) return null;
        var size = inside.SelectMany(l => l.Parts).GroupBy(f => Math.Round(f.Size, 1)).OrderByDescending(g => g.Count()).First().Key;
        // The room the rows leave between a rule and the text under it.
        var room = Enumerable.Range(0, rowY.Count - 1).Select(b => inside.Where(l => l.Line.Y >= rowY[b] && l.Line.Y < rowY[b + 1])
                .Select(l => rowY[b + 1] - l.Parts.Max(f => f.Y + f.Size)).DefaultIfEmpty(double.NaN).Min())
            .Where(v => !double.IsNaN(v)).DefaultIfEmpty(0).Min();
        room = Math.Min(Math.Max(room, 0), size);
        bool OfTheTable((Line Line, List<(double X, double R, double Y, double Size)> Parts) l)
            => InColumns(l.Parts) && l.Parts.All(f => Math.Abs(f.Size - size) <= 0.5);

        var rows = new List<double>(rowY);
        var edge = rowY[^1];
        foreach (var l in content.Where(l => l.Line.Y >= rowY[^1]).OrderBy(l => l.Line.Y))
        {
            if (!OfTheTable(l) || l.Line.Y - edge > BandLineEms * size) break;
            edge = l.Parts.Max(f => f.Y + f.Size);
        }
        if (edge > rowY[^1]) rows.Add(edge + room);
        edge = rowY[0];
        foreach (var l in content.Where(l => l.Line.Y < rowY[0]).OrderByDescending(l => l.Line.Y))
        {
            if (!OfTheTable(l) || edge - (l.Line.Y + l.Line.Size) > BandLineEms * size) break;
            edge = l.Parts.Min(f => f.Y);
        }
        if (edge < rowY[0]) rows.Insert(0, edge - room);
        if (rows.Count < 3) return null;
        return (new Aspose.Pdf.Rectangle(colX[0], rows[0], colX[^1], rows[^1]), rows.Count - 1, colX.Count - 1, colX, rows);
    }

    // A cell edge is ruled when the rules along it cover at least this share of its length.
    private const double EdgeRuleShare = 0.5;
    // Rules this close (points) to a grid line lie on it.
    private const double GridLineTolerance = 3.0;

    /// <summary>The cells of a ruled table: for each grid cell (row from the top, column from
    /// the left) the grid cell that starts the table cell it belongs to — itself, or the
    /// top-left grid cell of a merged cell. Neighbouring grid cells with no rule between them
    /// are one cell; a merged area that is no rectangle stays single cells.</summary>
    private static (int Row, int Col)[,] MergedCells(
        (Aspose.Pdf.Rectangle region, int rows, int cols, List<double> colX, List<double> rowY) t,
        List<PageContentScan.Rule> rules)
    {
        var (rows, cols) = (t.rows, t.cols);
        var parent = Enumerable.Range(0, rows * cols).ToArray();
        int Find(int i) => parent[i] == i ? i : parent[i] = Find(parent[i]);
        void Join(int a, int b) => parent[Find(a)] = Find(b);
        // rowY runs bottom to top: row r (from the top) lies between rowY[rows - 1 - r] and rowY[rows - r].
        double Top(int r) => t.rowY[rows - r];
        double Bottom(int r) => t.rowY[rows - 1 - r];

        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < cols; c++)
            {
                if (c + 1 < cols && !Ruled(rules, horizontal: false, t.colX[c + 1], Bottom(r), Top(r)))
                    Join(r * cols + c, r * cols + c + 1);
                if (r + 1 < rows && !Ruled(rules, horizontal: true, Bottom(r), t.colX[c], t.colX[c + 1]))
                    Join(r * cols + c, (r + 1) * cols + c);
            }
        }

        var cells = new (int Row, int Col)[rows, cols];
        foreach (var group in Enumerable.Range(0, rows * cols).GroupBy(Find))
        {
            var members = group.Select(i => (Row: i / cols, Col: i % cols)).ToList();
            var (top, left) = (members.Min(m => m.Row), members.Min(m => m.Col));
            var area = (members.Max(m => m.Row) - top + 1) * (members.Max(m => m.Col) - left + 1);
            var rectangle = area == members.Count;
            foreach (var m in members) cells[m.Row, m.Col] = rectangle ? (top, left) : m;
        }
        return cells;
    }

    // A cell holding this many lines or more, on the same baselines as another such cell of its
    // row, holds data rows that no rule separates.
    private const int MinDataRowLines = 2;
    // A line of label reaching within this many ems of its cell's edge filled the line: its label wraps.
    private const double WrapSlackEms = 2.0;

    /// <summary>A ruled table whose rules frame groups of data rows (a rule every five rows of a
    /// wage table, or none between the rows of a framed block) gets a grid row per line of text:
    /// in a grid row where at least two cells each hold several lines standing on shared
    /// baselines, and no cell's lines run on as prose, the grid row splits between its baselines.
    /// Each split is ruled only across the columns with text on both sides of it (added to
    /// <paramref name="rules"/>), so a one-line cell beside the data rows spans them.</summary>
    private static (Aspose.Pdf.Rectangle region, int rows, int cols, List<double> colX, List<double> rowY) SplitDataRows(
        (Aspose.Pdf.Rectangle region, int rows, int cols, List<double> colX, List<double> rowY) t,
        (int Row, int Col)[,] cells, List<Line> lines, List<PageContentScan.Rule> rules)
    {
        var rowY = new List<double>(t.rowY);
        // The bands over the first band of figures are heading bands: their lines are their headings' lines, no rows.
        var firstFigures = Enumerable.Range(0, t.rows).Where(r => HoldsFigures(t, lines, r)).DefaultIfEmpty(0).First();
        for (var r = firstFigures; r < t.rows; r++)
        {
            double top = t.rowY[t.rows - r], bottom = t.rowY[t.rows - 1 - r];
            // The row's lines, top to bottom, each with the cells (by the grid cell starting
            // them) its text stands in.
            var rowLines = new List<(Line Line, Dictionary<(int, int), string> Cells)>();
            // Per line, how far right its text reaches in each cell, and how far right the cell goes.
            var reach = new List<Dictionary<(int, int), (double R, double Edge)>>();
            foreach (var line in lines.Where(l => InRegion(l, t.region)).OrderByDescending(l => l.Baseline))
            {
                var inCells = new Dictionary<(int, int), string>();
                var rights = new Dictionary<(int, int), (double R, double Edge)>();
                foreach (var f in line.Frags.Where(f => !string.IsNullOrWhiteSpace(f.Text)))
                {
                    var cy = f.Base + 0.3 * f.Size;
                    if (cy < bottom || cy >= top) continue;
                    var cx = (f.X + f.R) / 2;
                    if (cx < t.colX[0] || cx > t.colX[^1]) continue; // beside the table (another text column)
                    var col = 0;
                    while (col < t.cols - 1 && cx >= t.colX[col + 1]) col++;
                    var cell = cells[r, col];
                    if (cell.Row != r || CellSpan(cells, cell.Row, cell.Col).Rows != 1) continue;
                    inCells[cell] = inCells.TryGetValue(cell, out var text) ? text + " " + f.Text.Trim() : f.Text.Trim();
                    rights[cell] = (Math.Max(rights.TryGetValue(cell, out var had) ? had.R : 0, f.R), t.colX[cell.Col + CellSpan(cells, cell.Row, cell.Col).Cols]);
                }
                if (inCells.Count == 0) continue;
                rowLines.Add((line, inCells));
                reach.Add(rights);
            }
            if (rowLines.Count < MinDataRowLines) continue;

            var byCell = rowLines.SelectMany((l, i) => l.Cells.Select(c => (c.Key, Index: i, Text: c.Value)))
                .GroupBy(x => x.Key).ToDictionary(g => g.Key, g => g.ToList());
            var multi = byCell.Where(kv => kv.Value.Count >= MinDataRowLines).ToList();
            if (multi.Count < 2) continue;
            // Cells of figures, several lines of them on shared baselines: each such line is a row of figures, whatever
            // the label column does - a label wrapped over the line above its figures joins them (as a wrapped label
            // over a text table's row does), one going on under them stays with them.
            var figureCells = multi.Where(kv => kv.Value.All(x => NumericCell.IsMatch(x.Text))).ToList();
            // (two cells of figures standing on shared baselines prove the band holds rows; every line holding a figure in
            // any cell is then a row - a table whose later columns start lower, stair-wise, has rows its first columns alone fill)
            var shared = figureCells.Count >= 2
                ? figureCells.Select(kv => kv.Value.Select(x => x.Index).ToHashSet()).Aggregate((a, b) => { a.IntersectWith(b); return a; })
                : [];
            var figureLines = shared.Count >= MinDataRowLines ? figureCells.SelectMany(kv => kv.Value.Select(x => x.Index)).ToHashSet() : [];
            List<int> splits;
            if (figureLines.Count >= MinDataRowLines)
            {
                // Every line is a row, but for a label wrapped: a line of label alone filling its cell's width goes on
                // on the line under it (over its figures), and one starting in lower case goes on from the line over it.
                bool LabelOnly(int i) => !figureLines.Contains(i);
                bool Fills(int i) => reach[i].Any(kv => kv.Value.Edge - kv.Value.R <= WrapSlackEms * rowLines[i].Line.Size);
                bool GoesOn(int i) => rowLines[i].Cells.Values.Any(text => text.Length > 0 && char.IsLower(text[0]));
                splits = [];
                for (var i = 1; i < rowLines.Count; i++)
                    if (!(LabelOnly(i - 1) && Fills(i - 1)) && !(LabelOnly(i) && GoesOn(i)))
                        splits.Add(i);
            }
            else
            {
                // Lines that run on as prose: most lines after a cell's first start in lower case.
                if (multi.Any(kv => RunsOn(kv.Value.Skip(1).Select(x => x.Text)))) continue;
                // The multi-line cells stand on shared baselines: data rows, not wrapped paragraphs.
                var sharedByAll = multi.Select(kv => kv.Value.Select(x => x.Index).ToHashSet())
                    .Aggregate((a, b) => { a.IntersectWith(b); return a; });
                if (sharedByAll.Count < MinDataRowLines) continue;
                splits = Enumerable.Range(1, rowLines.Count - 1).ToList();
            }

            foreach (var i in splits)
            {
                var (upper, lower) = (rowLines[i - 1].Line, rowLines[i].Line);
                var at = (upper.Baseline + lower.Baseline) / 2 + 0.3 * Math.Max(upper.Size, lower.Size);
                rowY.Add(at);
                for (var c = 0; c < t.cols; c++)
                {
                    var cell = cells[r, c];
                    if (!byCell.TryGetValue(cell, out var held)) continue;
                    if (held.Any(x => x.Index < i) && held.Any(x => x.Index >= i))
                        rules.Add(new PageContentScan.Rule(true, at, t.colX[c], t.colX[c + 1], Drawn: false));
                }
            }
        }
        if (rowY.Count == t.rowY.Count) return t;
        rowY.Sort();
        return (t.region, rowY.Count - 1, t.cols, t.colX, rowY);
    }

    /// <summary>The table without a band at its end that is ruled into columns of its own: upright rules standing in
    /// that band alone, which the text of the table's other rows runs across (a box of another table's headings framed
    /// under the table's last row). The band and its column edges go; its text is read as the table it is.</summary>
    private static (Aspose.Pdf.Rectangle region, int rows, int cols, List<double> colX, List<double> rowY) WithoutForeignEndBands(
        (Aspose.Pdf.Rectangle region, int rows, int cols, List<double> colX, List<double> rowY) t, List<PageContentScan.Rule> rules, List<Line> content)
    {
        while (t.rows >= 2 && t.rowY.Count == t.rows + 1)
        {
            var band = new[] { t.rows - 1, 0 }.Select(r => (Row: r, Edges: ForeignEdges(t, rules, content, r))).FirstOrDefault(b => b.Edges.Count > 0);
            if (band.Edges is not { Count: > 0 }) return t;
            var colX = t.colX.Where((_, k) => !band.Edges.Contains(k)).ToList();
            // rowY runs bottom to top: the last row is the lowest band.
            var rowY = band.Row == 0 ? t.rowY.GetRange(0, t.rows) : t.rowY.GetRange(1, t.rows);
            t = (new Aspose.Pdf.Rectangle(t.region.LLX, rowY[0], t.region.URX, rowY[^1]), t.rows - 1, colX.Count - 1, colX, rowY);
        }
        return t;
    }

    /// <summary>The interior column edges upright rules draw in row <paramref name="r"/> alone, each run across by the text
    /// of several lines of the table's other rows (a figure standing over the edge, by however little: a character's
    /// box is its glyph's own, so a figure ending a hair past a rule none of the table's stands across it).</summary>
    private static List<int> ForeignEdges((Aspose.Pdf.Rectangle region, int rows, int cols, List<double> colX, List<double> rowY) t,
        List<PageContentScan.Rule> rules, List<Line> content, int r)
    {
        double top = t.rowY[t.rows - r], bottom = t.rowY[t.rows - 1 - r];
        // The lines of the other rows, each with its text there (one heading over a group of columns runs across the
        // edges under it; the rows of a table run across edges that are none of theirs line after line).
        var others = content.Where(l => InRegion(l, t.region))
            .Select(l => l.Frags.Where(f => !string.IsNullOrWhiteSpace(f.Text) && (f.Base + 0.3 * f.Size < bottom || f.Base + 0.3 * f.Size >= top)).ToList())
            .Where(frags => frags.Count > 0).ToList();
        return Enumerable.Range(1, Math.Max(0, t.cols - 1)).Where(k =>
        {
            var uprights = rules.Where(v => !v.Horizontal && Math.Abs(v.At - t.colX[k]) <= GridLineTolerance && v.To > t.region.LLY && v.From < t.region.URY).ToList();
            return uprights.Count > 0 && uprights.All(v => v.From >= bottom - GridLineTolerance && v.To <= top + GridLineTolerance)
                   && others.Count(frags => frags.Any(f => f.X < t.colX[k] && f.R > t.colX[k])) >= MinDataColumnRows;
        }).ToList();
    }

    /// <summary>Whether a grid row holds figures: text at the table's size reading as a number, no period (a year, a
    /// month) and no note mark set small.</summary>
    private static bool HoldsFigures((Aspose.Pdf.Rectangle region, int rows, int cols, List<double> colX, List<double> rowY) t, List<Line> lines, int r)
    {
        double top = t.rowY[t.rows - r], bottom = t.rowY[t.rows - 1 - r];
        var inside = lines.Where(l => InRegion(l, t.region)).SelectMany(l => l.Frags)
            .Where(f => !string.IsNullOrWhiteSpace(f.Text) && (f.X + f.R) / 2 >= t.colX[0] && (f.X + f.R) / 2 <= t.colX[^1]).ToList();
        if (inside.Count == 0) return false;
        var size = inside.Max(f => f.Size);
        return inside.Any(f => f.Base + 0.3 * f.Size >= bottom && f.Base + 0.3 * f.Size < top && f.Size > HeadingMarkSizeShare * size
                               && NumericCell.IsMatch(f.Text) && f.Text.Any(char.IsDigit) && !PeriodCell.IsMatch(f.Text.Trim()));
    }

    /// <summary>Whether a value is one character that is no letter or figure: a bullet, whatever its glyph.</summary>
    private static bool LoneSymbol(string text) => text.Trim() is { Length: 1 } symbol && !char.IsLetterOrDigit(symbol[0]);

    // A column splits where at least this many lines have text on both sides of a clear gap.
    private const int MinDataColumnRows = 3;
    // The gap between two data columns is at least this share of the text size across.
    private const double DataColumnGapEms = 1.0;
    // ... and the rows with values on both sides of it outnumber the rows whose text crosses it
    // (headings over both columns) at least this many times.
    private const double DataColumnMajority = 2.0;

    /// <summary>A ruled table whose rules frame groups of columns (a wage table's filing statuses)
    /// gets a grid column per column of values: in the cells starting at a grid column, a clear gap between values that at least
    /// <see cref="MinDataColumnRows"/> lines share, and that few lines' text crosses, is a column edge (a frame ruled round its
    /// rows and under its head only holds the rows' lines in one cell). It is ruled (added to
    /// <paramref name="rules"/>) in the rows with values on both sides, so a heading or a single
    /// value standing across it spans both columns.</summary>
    private static (Aspose.Pdf.Rectangle region, int rows, int cols, List<double> colX, List<double> rowY) SplitDataColumns(
        (Aspose.Pdf.Rectangle region, int rows, int cols, List<double> colX, List<double> rowY) t,
        (int Row, int Col)[,] cells, List<Line> lines, List<PageContentScan.Rule> rules)
    {
        // The values of each cell one grid row high (it may span columns), by the grid cell
        // starting it: the extents of its text fragments.
        var values = new List<(double X, double R, double Size, int Line, string Text)>[t.rows, t.cols];
        var within = lines.Where(l => InRegion(l, t.region)).ToList();
        foreach (var line in within)
            foreach (var f in line.Frags.Where(f => !string.IsNullOrWhiteSpace(f.Text)))
            {
                double cx = (f.X + f.R) / 2, cy = f.Base + 0.3 * f.Size;
                if (cx < t.colX[0] || cx > t.colX[^1] || cy < t.rowY[0] || cy > t.rowY[^1]) continue;
                var col = 0;
                while (col < t.cols - 1 && cx >= t.colX[col + 1]) col++;
                var fromBottom = 0;
                while (fromBottom < t.rows - 1 && cy >= t.rowY[fromBottom + 1]) fromBottom++;
                var (row, start) = cells[t.rows - 1 - fromBottom, col];
                if (CellSpan(cells, row, start).Rows != 1) continue;
                (values[row, start] ??= []).Add((f.X, f.R, f.Size, within.IndexOf(line), f.Text));
            }

        var colX = new List<double>(t.colX);
        for (var c = 0; c < t.cols; c++)
        {
            var rowsWith = Enumerable.Range(0, t.rows).Where(r => values[r, c] is { Count: > 0 }).ToList();
            // Each line's clear gaps between its values, at least a text size wide.
            var gaps = new List<(int Row, int Line, double L, double R)>();
            var linesWith = rowsWith.SelectMany(r => values[r, c]!.GroupBy(v => v.Line).Select(g => (Row: r, Values: g.ToList()))).ToList();
            foreach (var (r, ofLine) in linesWith)
            {
                var sorted = ofLine.OrderBy(v => v.X).ToList();
                var reach = sorted[0].R;
                // (a lone symbol opening the line - a bullet - labels its line: the gap after it parts no column)
                var labelled = LoneSymbol(sorted[0].Text);
                foreach (var v in sorted.Skip(1))
                {
                    if (v.X - reach >= DataColumnGapEms * v.Size && !(labelled && reach == sorted[0].R)) gaps.Add((r, v.Line, reach, v.X));
                    reach = Math.Max(reach, v.R);
                }
            }
            // A gap shared by enough rows (their clear stretches overlapping), which few rows'
            // values cross, is a column edge.
            var used = new HashSet<int>();
            foreach (var gi in Enumerable.Range(0, gaps.Count).OrderBy(i => gaps[i].L))
            {
                if (used.Contains(gi)) continue;
                var (l, rr) = (gaps[gi].L, gaps[gi].R);
                var members = new List<int>();
                for (var i = 0; i < gaps.Count; i++)
                {
                    var o = gaps[i];
                    if (used.Contains(i) || members.Any(m => gaps[m].Line == o.Line) || o.R <= l || o.L >= rr) continue;
                    (l, rr) = (Math.Max(l, o.L), Math.Min(rr, o.R));
                    members.Add(i);
                }
                foreach (var m in members) used.Add(m);
                // The column starts as far before its text as the column it is cut from does before its own.
                var inset = Math.Max(0, rowsWith.SelectMany(r => values[r, c]!).Min(v => v.X) - t.colX[c]);
                var at = inset < rr - l ? rr - inset : (l + rr) / 2;
                var split = members.Select(m => gaps[m].Row).Distinct().ToList();
                // (a value of any cell of the line standing across the gap's middle crosses it: a figure in the column
                // beside a spanning cell's gap keeps the column whole)
                var crossed = within.Count(line => line.Frags.Any(f => !string.IsNullOrWhiteSpace(f.Text) && f.X < at && f.R > at
                                                                      && f.Base + 0.3 * f.Size >= t.rowY[0] && f.Base + 0.3 * f.Size <= t.rowY[^1]));
                if (members.Count < MinDataColumnRows || members.Count < DataColumnMajority * crossed) continue;
                // A grid line already standing in the gap (one the cells span) is ruled there.
                if (colX.FirstOrDefault(x => x > l && x < rr) is var existing && existing > 0) at = existing;
                else colX.Add(at);
                foreach (var r in split)
                    rules.Add(new PageContentScan.Rule(false, at, t.rowY[t.rows - 1 - r], t.rowY[t.rows - r], Drawn: false));
            }
        }
        if (colX.Count == t.colX.Count) return t;
        colX.Sort();
        return (t.region, t.rows, colX.Count - 1, colX, t.rowY);
    }

    // Two halves of a table are alike when each column of one is as wide as its fellow in the
    // other within this many points or this share of the column.
    private const double HalfWidthTolerance = 3.0;
    private const double HalfWidthShare = 0.05;

    /// <summary>A ruled table set in two halves side by side (a long table wrapped into two runs
    /// of the same columns, like a newspaper column) is two tables, left then right: its columns
    /// fall into two runs of the same widths, meeting at a rule or parted by a grid column no text
    /// stands in, and a heading repeated in both. Other tables come back as they are.</summary>
    private static IEnumerable<(Aspose.Pdf.Rectangle region, int rows, int cols, List<double> colX, List<double> rowY)> SplitSideBySide(
        (Aspose.Pdf.Rectangle region, int rows, int cols, List<double> colX, List<double> rowY) t, List<Line> lines)
    {
        var widths = Enumerable.Range(0, t.cols).Select(c => t.colX[c + 1] - t.colX[c]).ToList();
        bool Empty(int c) => !lines.SelectMany(l => l.Frags).Any(f => !string.IsNullOrWhiteSpace(f.Text)
            && (f.X + f.R) / 2 > t.colX[c] && (f.X + f.R) / 2 < t.colX[c + 1]
            && f.Base + 0.3 * f.Size > t.rowY[0] && f.Base + 0.3 * f.Size < t.rowY[^1]);
        bool Alike(int from, int to, int count) => Enumerable.Range(0, count).All(k =>
            Math.Abs(widths[from + k] - widths[to + k]) <= Math.Max(HalfWidthTolerance, HalfWidthShare * widths[from + k]));
        // The text of grid row r (from the bottom) between two x.
        string Text(int r, double x0, double x1) => string.Join(" ", lines.SelectMany(l => l.Frags)
            .Where(f => (f.X + f.R) / 2 > x0 && (f.X + f.R) / 2 < x1 && f.Base + 0.3 * f.Size > t.rowY[r] && f.Base + 0.3 * f.Size < t.rowY[r + 1])
            .OrderByDescending(f => Math.Round(f.Base)).ThenBy(f => f.X).Select(f => f.Text.Trim()));
        // ... and the halves repeat a heading: a row reads the same in both.
        bool Repeats(int half, int gap) => Enumerable.Range(0, t.rows).Any(r =>
            Text(r, t.colX[0], t.colX[half]) is { } left && left.Any(char.IsLetter) && left == Text(r, t.colX[half + gap], t.colX[^1]));
        // (the columns of the left half, the columns between the halves)
        foreach (var (half, gap) in new[] { (t.cols / 2, 0), ((t.cols - 1) / 2, 1) })
        {
            if (half < 2 || 2 * half + gap != t.cols || (gap == 1 && !Empty(half)) || !Alike(0, half + gap, half) || !Repeats(half, gap)) continue;
            (Aspose.Pdf.Rectangle, int, int, List<double>, List<double>) Part(int first)
            {
                var colX = t.colX.GetRange(first, half + 1);
                return (new Aspose.Pdf.Rectangle(colX[0], t.region.LLY, colX[^1], t.region.URY), t.rows, half, colX, t.rowY);
            }
            yield return Part(0);
            yield return Part(half + gap);
            yield break;
        }
        yield return t;
    }

    /// <summary>Whether the lines continue a sentence from one to the next: at least
    /// <see cref="ProseContinuation"/> of those of words (a lone word is a value) starting with a letter
    /// start in lower case.</summary>
    private static bool RunsOn(IEnumerable<string> continuations)
    {
        var starts = continuations.Where(s => s.Length > 0 && char.IsLetter(s[0]) && s.Trim().Contains(' ')).Select(s => char.IsLower(s[0])).ToList();
        return starts.Count > 0 && starts.Count(s => s) >= ProseContinuation * starts.Count;
    }

    /// <summary>Every grid cell a cell of its own (an unruled table's cells never merge).</summary>
    private static (int Row, int Col)[,] SingleCells(int rows, int cols)
    {
        var cells = new (int Row, int Col)[rows, cols];
        for (var r = 0; r < rows; r++)
            for (var c = 0; c < cols; c++)
                cells[r, c] = (r, c);
        return cells;
    }

    /// <summary>Whether rules along the line at <paramref name="at"/> cover most of the edge
    /// from <paramref name="from"/> to <paramref name="to"/>.</summary>
    private static bool Ruled(List<PageContentScan.Rule> rules, bool horizontal, double at, double from, double to)
    {
        var covered = Union(rules.Where(r => r.Horizontal == horizontal && Math.Abs(r.At - at) <= GridLineTolerance)
                .Select(r => (Math.Max(r.From, from), Math.Min(r.To, to))).Where(iv => iv.Item2 > iv.Item1))
            .Sum(iv => iv.R - iv.L);
        return covered >= EdgeRuleShare * (to - from);
    }

    /// <summary>How many grid rows and columns the cell starting at (<paramref name="row"/>,
    /// <paramref name="col"/>) covers.</summary>
    private static (int Rows, int Cols) CellSpan((int Row, int Col)[,] cells, int row, int col)
    {
        var rows = 1;
        while (row + rows < cells.GetLength(0) && cells[row + rows, col] == (row, col)) rows++;
        var cols = 1;
        while (col + cols < cells.GetLength(1) && cells[row, col + cols] == (row, col)) cols++;
        return (rows, cols);
    }

    /// <summary>A header cell: TH with its scope in a Table attribute object.</summary>
    private static LogicalStructure.StructureElement HeaderCell(TagTreeBuildState tg, string scope)
    {
        var th = tg.tc.CreateTableTHElement();
        var attrs = new PdfDictionary();
        attrs.Set("O", new PdfName("Table"));
        attrs.Set("Scope", new PdfName(scope));
        th._dict.Set("A", attrs);
        return th;
    }
}
