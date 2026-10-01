using System;
using System.Collections.Generic;
using System.Linq;

namespace Aspose.Pdf.Tagged;

internal static partial class AutoTagger
{
    // A rule is a band edge of a ruled header when its pieces at one height cover this share of the box's width.
    private const double HeaderBandRuleShare = 0.5;
    // The box stands over the table: across at least this share of its width.
    private const double HeaderBoxShare = 0.8;

    /// <summary>The ruled box standing right over an unruled table of figures, holding text and no figures: the box of
    /// the table's headings; null when none does.</summary>
    private static Rectangle? HeaderBox(PageWork pw, (Rectangle region, int rows, int cols, List<double> colX, List<double> rowY) t, List<Line> content)
    {
        var own = content.Where(l => l.Frags.Count > 0 && InRegion(l, t.region)).ToList();
        if (own.Count == 0) return null;
        var size = own.Max(l => l.Size);
        foreach (var box in pw.RuledBoxes.OrderBy(b => b.LLY))
        {
            // The table's rows of figures stand under the box, the first within a few lines of it.
            var under = own.Where(l => l.Baseline < box.LLY).ToList();
            if (under.Count == 0 || box.LLY - under.Max(l => l.Y) > HeaderLinePitchFactor * size) continue;
            if (Math.Min(box.URX, t.colX[^1]) - Math.Max(box.LLX, t.colX[0]) < HeaderBoxShare * (t.colX[^1] - t.colX[0])) continue;
            var inside = content.Where(l => l.Frags.Count > 0 && l.Baseline > box.LLY && l.Baseline < box.URY
                                            && l.MinX < box.URX && l.Frags[^1].R > box.LLX).ToList();
            if (inside.Count == 0 || inside.SelectMany(l => l.Frags).Any(f => f.Size > HeadingMarkSizeShare * size
                    && NumericCell.IsMatch(f.Text) && f.Text.Any(char.IsDigit) && !PeriodCell.IsMatch(f.Text.Trim()))) continue;
            return box;
        }
        return null;
    }

    /// <summary>The headings of a table set in a ruled box over it are its header rows, one per band of the box (the
    /// bands lie between the rules across it). In a band the cells run between the upright rules crossing it; a
    /// heading's lines, however many, are one cell. A cell with no rule drawn under it spans the band under it too
    /// ("Date" beside both bands). Returns false, changing nothing, where the box's rules show no such header.</summary>
    private static bool AdoptRuledHeader(PageWork pw, int index, Rectangle box, List<Line> content)
    {
        var t = pw.Tables[index];
        var width = box.URX - box.LLX;
        var inner = ClusterValues(pw.Rules.Where(r => r.Horizontal && r.Drawn && r.At > box.LLY + GridLineTolerance && r.At < box.URY - GridLineTolerance)
                .Select(r => r.At), GridLineTolerance)
            .Where(y => Union(pw.Rules.Where(r => r.Horizontal && r.Drawn && Math.Abs(r.At - y) <= GridLineTolerance)
                .Select(r => (Math.Max(r.From, box.LLX), Math.Min(r.To, box.URX))).Where(iv => iv.Item2 > iv.Item1)).Sum(iv => iv.R - iv.L) >= HeaderBandRuleShare * width).ToList();
        // A box of one band is a row of headings, read as the lines of a heading block are.
        if (inner.Count == 0) return false;
        // The band edges, top to bottom: the box's top, the rules across it, its bottom.
        var edges = new[] { box.URY }.Concat(inner.OrderByDescending(y => y)).Append(box.LLY).ToList();
        var rows = edges.Count - 1;

        // The table's own rows are those under the box; the first of them starts at the box's bottom.
        var body = Enumerable.Range(0, t.rows).Count(k => (t.rowY[k] + t.rowY[k + 1]) / 2 < box.LLY);
        if (body == 0) return false;
        var rowY = t.rowY.GetRange(0, body);
        for (var e = edges.Count - 1; e >= 0; e--) rowY.Add(edges[e]);
        // The header is as wide as its box, and the table with it.
        var colX = new List<double>(t.colX);
        colX[0] = Math.Min(colX[0], box.LLX);
        colX[^1] = Math.Max(colX[^1], box.URX);
        // A divider of the box is the edge between the columns whose figures stand at its two sides.
        var figures = content.Where(l => InRegion(l, t.region) && l.Baseline < box.LLY).SelectMany(l => l.Frags)
            .Where(f => !string.IsNullOrWhiteSpace(f.Text)).ToList();
        int? EdgeAt(double x)
        {
            if (figures.Any(f => f.X < x && f.R > x)) return null;
            var k = Enumerable.Range(0, t.cols).Count(c => figures.Any(f => f.R <= x && (f.X + f.R) / 2 > t.colX[c] && (f.X + f.R) / 2 <= t.colX[c + 1]));
            var right = Enumerable.Range(0, t.cols).Count(c => figures.Any(f => f.X >= x && (f.X + f.R) / 2 > t.colX[c] && (f.X + f.R) / 2 <= t.colX[c + 1]));
            return k >= 1 && right >= 1 && k + right == t.cols ? k : null;
        }
        foreach (var rule in pw.Rules.Where(r => !r.Horizontal && r.Drawn && r.To > box.LLY && r.From < box.URY))
            if (EdgeAt(rule.At) is { } k) colX[k] = rule.At;
        if (Enumerable.Range(1, colX.Count - 1).Any(k => colX[k] <= colX[k - 1])) return false;
        t = (t.region, t.rows, t.cols, colX, t.rowY);
        var joined = (new Rectangle(colX[0], t.region.LLY, colX[^1], box.URY), body + rows, t.cols, colX, rowY);
        var map = SingleCells(joined.Item2, t.cols);

        // Each band's cells: the columns between the upright rules crossing it.
        var starts = new List<int>[rows];
        for (var b = 0; b < rows; b++)
        {
            var (top, bottom) = (edges[b], edges[b + 1]);
            starts[b] = new[] { 0 }.Concat(pw.Rules.Where(r => !r.Horizontal && r.Drawn && r.From < bottom + GridLineTolerance && r.To > top - GridLineTolerance)
                .Select(r => EdgeAt(r.At)).OfType<int>()).Distinct().OrderBy(k => k).ToList();
            for (var s = 0; s < starts[b].Count; s++)
                for (var c = starts[b][s]; c < (s + 1 < starts[b].Count ? starts[b][s + 1] : t.cols); c++)
                    map[b, c] = (b, starts[b][s]);
        }
        if (starts.All(s => s.Count < 2)) return false;
        // Down the bands: a cell over one of the same columns, no rule drawn between them, spans both.
        for (var b = 0; b + 1 < rows; b++)
            foreach (var from in starts[b + 1])
            {
                var to = starts[b + 1].Where(s => s > from).DefaultIfEmpty(t.cols).First();
                if (!starts[b].Contains(from) || starts[b].Where(s => s > from).DefaultIfEmpty(t.cols).First() != to) continue;
                var middle = (t.colX[from] + t.colX[to]) / 2;
                if (pw.Rules.Any(r => r.Horizontal && r.Drawn && Math.Abs(r.At - edges[b + 1]) <= GridLineTolerance && r.From < middle && r.To > middle)) continue;
                for (var c = from; c < to; c++) map[b + 1, c] = map[b, from];
            }
        // The rows under the box keep their cells.
        var old = pw.TableCells[index];
        var dropped = t.rows - body;
        for (var r = 0; r < body; r++)
            for (var c = 0; c < t.cols; c++)
                map[rows + r, c] = old[dropped + r, c].Row >= dropped ? (old[dropped + r, c].Row - dropped + rows, old[dropped + r, c].Col) : (rows + r, c);
        pw.Tables[index] = joined;
        pw.TableCells[index] = map;
        return true;
    }
}
