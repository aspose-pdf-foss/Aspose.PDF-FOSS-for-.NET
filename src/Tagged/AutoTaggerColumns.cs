using System;
using System.Collections.Generic;
using System.Linq;

namespace Aspose.Pdf.Tagged;

internal static partial class AutoTagger
{
    // A gutter between text columns is at least this many body sizes wide (and at least
    // MinGutter points): word gaps and table-cell gaps are narrower (two-column pages set in
    // 10 pt text run gutters of 10 to 15 pt; a cover's display title reaches to 8 pt of the
    // column beside it). A gutter is a channel every row of the band keeps clear, which a word
    // gap in running text does not.
    private const double MinGutterEms = 0.8;
    private const double MinGutter = 8.0;
    // A column band needs at least this many rows keeping the gutter clear.
    private const int MinColumnRows = 3;
    // Each column of a band holds text on at least this many rows.
    private const int MinColumnLines = 2;
    // A band may open with up to this many rows of one column only (a column going on above
    // the heading that opens the other), each at most this share of the page's text width.
    private const int MaxLeadRows = 6;
    private const double NarrowRowShare = 0.6;

    /// <summary>Split rows that belong to a multi-column band into one line per column and
    /// put the page in reading order: rows outside any band as they come, then each band's
    /// columns left to right, each column top to bottom. A band is a run of rows (no running
    /// line, no table row) whose combined text leaves an empty vertical channel at least a
    /// gutter wide; a row that crosses the channel (a title across the columns) ends it.</summary>
    // A glossary's terms fill less than this share of their side, as a rule.
    private const double TermFillShare = 0.6;
    // A term within this share of the size of the text beside it is set in that size.
    private const double SameSizeEms = 0.05;

    /// <summary>Whether rows parted at one channel are a glossary's: every row holding text on the left holds text on the
    /// right too (a term and its definition's first line), some rows hold text on the right only (the definitions going
    /// on), and most terms fill less than <see cref="TermFillShare"/> of their side - or, however wide the terms, no term
    /// stands on the row under another (each opens an entry its text goes on under: a list of references' labels; a column
    /// of running text has text on row after row).</summary>
    private static bool Glossary(List<List<(int Col, Line Piece)>> rows)
    {
        static bool Texted(Line l) => l.Frags.Any(f => !string.IsNullOrWhiteSpace(f.Text));
        var left = rows.Where(r => r.Any(x => x.Col == 0 && Texted(x.Piece))).ToList();
        if (left.Count < 2 || left.Any(r => !r.Any(x => x.Col == 1 && Texted(x.Piece)))) return false;
        if (!rows.Any(r => !r.Any(x => x.Col == 0 && Texted(x.Piece)) && r.Any(x => x.Col == 1 && Texted(x.Piece)))) return false;
        // (each term set in the size of the text beside it: a display title beside a smaller line is two columns)
        var termRows = rows.Select((r, k) => r.Any(x => x.Col == 0 && Texted(x.Piece)) ? k : -1).Where(k => k >= 0).ToList();
        if (termRows.Zip(termRows.Skip(1), (a, b) => b - a).All(step => step > 1)
            && left.All(r => Math.Abs(r.First(x => x.Col == 0).Piece.Size - r.First(x => x.Col == 1).Piece.Size) < SameSizeEms * r.First(x => x.Col == 1).Piece.Size))
            return true;
        var terms = left.Select(r => r.First(x => x.Col == 0).Piece).ToList();
        var side = rows.SelectMany(r => r.Where(x => x.Col == 1 && Texted(x.Piece))).Min(x => x.Piece.MinX) - terms.Min(t => t.MinX);
        if (side <= 0) return false;
        var fills = terms.Select(t => (t.Frags[^1].R - t.MinX) / side).OrderBy(f => f).ToList();
        return fills[fills.Count / 2] < TermFillShare;
    }

    private static List<Line> SplitColumns(List<Line> rows, List<(Aspose.Pdf.Rectangle region, int rows, int cols, List<double> colX, List<double> rowY)> tables,
        List<(double y, double x, double w, double h, int op)> figures, double bodySize, IReadOnlyList<PageContentScan.Rule>? rules = null)
    {
        var gutter = Math.Max(MinGutterEms * bodySize, MinGutter);
        var withText = rows.Where(l => l.Running == 0 && l.Frags.Count > 0).ToList();
        var span = withText.Count > 0 ? withText.Max(l => l.Frags[^1].R) - withText.Min(l => l.MinX) : 0;
        // A row crossing a table is plain when it also holds text beside the table (a column of
        // text next to a table in the other column), so the two split apart.
        // An entry of a table of contents is read whole: no column band runs through it.
        bool Plain(Line l) => l.Running == 0 && !l.Toc && tables.Where(t => InRegion(l, t.region))
            .All(t => l.Frags.Any(f => f.R < t.region.LLX - gutter / 2 || f.X > t.region.URX + gutter / 2));
        // A row lying inside a table is the table's: it covers the table's width, whatever its cells.
        (double L, double R)? TableCover(Line l)
        {
            if (l.Running != 0) return null;
            var inside = tables.Where(t => InRegion(l, t.region)).ToList();
            if (inside.Count == 0 || !l.Frags.All(f => inside.Any(t => f.X >= t.region.LLX - 1 && f.R <= t.region.URX + 1))) return null;
            return (inside.Min(t => t.region.LLX), inside.Max(t => t.region.URX));
        }
        var result = new List<Line>();
        var i = 0;
        while (i < rows.Count)
        {
            // Rows running through tables or boxes side by side (the two halves of a wrapped table, boxes set beside one
            // another) split at their edges and read region by region, the left first, each as a column of its own.
            if (tables.Count(t => InRegion(rows[i], t.region)) >= 2)
            {
                var regions = tables.Where(t => InRegion(rows[i], t.region)).Select(t => t.region).OrderBy(r => r.LLX).ToList();
                var edges = regions.SelectMany(r => new[] { r.LLX - 1, r.URX + 1 }).OrderBy(x => x).ToList();
                var last = i;
                while (last < rows.Count && regions.Any(r => InRegion(rows[last], r))
                       && rows[last].Frags.All(f => string.IsNullOrWhiteSpace(f.Text) || regions.Any(r => f.X >= r.LLX - 1 && f.R <= r.URX + 1)))
                    last++;
                last = Math.Max(last, i + 1);
                var sides = regions.Select(_ => new List<Line>()).ToList();
                for (var r = i; r < last; r++)
                    foreach (var (col, piece) in SplitRow(rows[r], edges))
                        sides[Math.Min(Math.Max((col - 1) / 2, 0), regions.Count - 1)].Add(piece);
                for (var c = 0; c < sides.Count; c++)
                {
                    // A region's text stands as far in from its right edge as from its left.
                    var inset = sides[c].Where(l => l.Frags.Count > 0).Select(l => l.MinX - regions[c].LLX).DefaultIfEmpty(0).Min();
                    for (var k = 0; k < sides[c].Count; k++)
                    {
                        sides[c][k].ColumnRight = regions[c].URX - Math.Max(0, inset);
                        sides[c][k].ColumnStart = c > 0 && k == 0;
                        result.Add(sides[c][k]);
                    }
                }
                i = last;
                continue;
            }
            var (end, channels, lead, ruled) = Plain(rows[i]) ? ColumnBand(rows, i, gutter, span, Plain, TableCover, figures, rules ?? []) : (i, new List<(double L, double R)>(), i, false);
            // A table's own gaps between its columns are no gutter.
            channels = channels.Where(c => !tables.Any(t => (c.L + c.R) / 2 > t.region.LLX && (c.L + c.R) / 2 < t.region.URX
                && rows.Skip(i).Take(end - i).Any(l => InRegion(l, t.region)))).ToList();
            // The rows before the channel showed were taken to hold one column's text: one that
            // crosses the channel (a title above the columns) is no row of the band, which
            // starts after it.
            var crossing = -1;
            for (var r = i; r < lead; r++)
                if (channels.Any(c => rows[r].Frags.Any(f => !string.IsNullOrWhiteSpace(f.Text) && f.X < c.R && f.R > c.L))) crossing = r;
            if (crossing >= i)
            {
                for (var r = i; r <= crossing; r++) result.Add(rows[r]);
                i = crossing + 1;
                continue;
            }
            // A band a rule across its channel closes may be shorter when its sides are set in sizes of their own (a heading
            // beside a note, over the rule): rows set apart at tab stops keep one size across.
            var closed = ruled && channels.Count > 0 && SidesDiffer(rows.Skip(i).Take(end - i).ToList(), (channels[0].L + channels[0].R) / 2);
            if ((end - i < MinColumnRows && !closed) || channels.Count == 0)
            {
                // A lone row holding a table's text and text beside the table splits at the table's
                // edges, the table's piece first (the text beside it reads on after it).
                var edges = tables.Where(t => InRegion(rows[i], t.region)).SelectMany(t => new[] { t.region.LLX - 1, t.region.URX + 1 }).OrderBy(x => x).ToList();
                if (edges.Count > 0 && Plain(rows[i]))
                {
                    result.AddRange(SplitRow(rows[i], edges).Select(p => p.Piece)
                        .OrderBy(p => tables.Any(t => InRegion(p, t.region)) ? 0 : 1));
                    i++;
                    continue;
                }
                result.Add(rows[i]);
                i++;
                continue;
            }
            var bounds = channels.Select(c => (c.L + c.R) / 2).ToList();
            var columns = Enumerable.Range(0, bounds.Count + 1).Select(_ => new List<Line>()).ToList();
            static bool Texted(Line l) => l.Frags.Any(f => !string.IsNullOrWhiteSpace(f.Text));
            var pieces = Enumerable.Range(i, end - i).Select(r => SplitRow(rows[r], bounds).ToList()).ToList();
            // Columns share rows: where no row holds text on both sides of a channel, and no line on one side stands
            // level with a line on the other (a display title beside a block of smaller lines, on baselines of its
            // own), the text beside it is blocks set at different heights (a release note at the right between a
            // title and a heading), read top to bottom.
            if (Enumerable.Range(0, bounds.Count).Any(b => !pieces.Any(p => p.Any(x => x.Col <= b && Texted(x.Piece))
                                                                      && p.Any(x => x.Col > b && Texted(x.Piece)))
                                                           && !StandLevel(pieces.SelectMany(p => p).Where(x => Texted(x.Piece)).ToList(), b)))
            {
                result.Add(rows[i]);
                i++;
                continue;
            }
            // A first column holding only a section's number, on one row, is no column: the number stands apart at the left
            // of its title (under the indented lines of a list item), and reads on with its row - the rows read as they come.
            if (bounds.Count == 1 && pieces.Where(p => p.Any(x => x.Col == 0 && Texted(x.Piece))).ToList() is [var numbered]
                && numbered.Where(x => x.Col == 0 && Texted(x.Piece)).ToList() is [var number]
                && SectionNumber.IsMatch(string.Concat(number.Piece.Frags.Select(f => f.Text)).Trim())
                && numbered.Any(x => x.Col == 1 && Texted(x.Piece)))
            {
                result.Add(rows[i]);
                i++;
                continue;
            }
            // A glossary is no columns: each term stands level with its definition's first line, the definition going on
            // under itself - rows holding text at the right only - and the terms leave most of their side empty.
            if (bounds.Count == 1 && Glossary(pieces))
            {
                for (var r = i; r < end; r++) result.Add(rows[r]);
                i = end;
                continue;
            }
            // A last column holding text on one row only is no column: text standing apart at the right of a row (a
            // running head's page label at the right of its title) reads on with the rest of its row.
            var lone = pieces.Where(p => p.Any(x => x.Col == bounds.Count && Texted(x.Piece))).ToList();
            if (bounds.Count > 0 && lone.Count < MinColumnLines && lone.Count > 0)
            {
                var at = pieces.IndexOf(lone[0]);
                pieces[at] = SplitRow(rows[i + at], bounds.Take(bounds.Count - 1).ToList()).ToList();
            }
            foreach (var (col, piece) in pieces.SelectMany(p => p))
                columns[col].Add(piece);
            for (var c = 0; c < columns.Count; c++)
            {
                var right = columns[c].Where(l => l.Frags.Count > 0).Select(l => l.Frags[^1].R).DefaultIfEmpty(0).Max();
                for (var k = 0; k < columns[c].Count; k++)
                {
                    columns[c][k].ColumnRight = right;
                    columns[c][k].ColumnStart = c > 0 && k == 0;
                    result.Add(columns[c][k]);
                }
            }
            i = end;
        }
        return result;
    }

    /// <summary>Grow a column band from row <paramref name="start"/>: rows join while the
    /// union of their text still leaves at least one interior channel a gutter wide. Returns
    /// the end (exclusive), the channels, and the row the channel first showed at (the rows
    /// before it held one column's text only).</summary>
    private static (int End, List<(double L, double R)> Channels, int Lead, bool Ruled) ColumnBand(
        List<Line> rows, int start, double gutter, double span, Func<Line, bool> plain,
        Func<Line, (double L, double R)?> tableCover,
        List<(double y, double x, double w, double h, int op)> figures, IReadOnlyList<PageContentScan.Rule> rules)
    {
        var cover = new List<(double L, double R)>();
        var text = new List<(double L, double R)>();
        var channels = new List<(double L, double R)>();
        var lead = start;
        var j = start;
        for (; j < rows.Count; j++)
        {
            // An image beside the row counts as covered: text flowing around a figure is not
            // two columns. A gutter has text on both sides, not text and a picture. A row of a
            // table is the table's width (a table standing in one column beside text).
            var row = rows[j];
            // A rule drawn across the band's text between the rows ends the band: the rows under it are set apart from it (a
            // short rule within one side - a sum's - parts nothing).
            if (channels.Count > 0 && text.Count > 0 && rules.Any(r => r.Horizontal && r.Drawn && r.At < rows[j - 1].Y && r.At > row.Y + row.Size
                                                                       && r.From <= text.Min(t => t.L) + gutter && r.To >= text.Max(t => t.R) - gutter))
                return (j, channels, lead, true);
            List<(double L, double R)> rowText;
            if (plain(row)) rowText = row.Frags.Where(f => !string.IsNullOrWhiteSpace(f.Text)).Select(f => (L: f.X, R: f.R)).ToList();
            else if (tableCover(row) is { } table) rowText = [table];
            else break;
            var nextText = Union(text.Concat(rowText));
            var next = Union(cover.Concat(rowText)
                .Concat(figures.Where(f => f.y <= row.Y + row.Size && f.y + f.h >= row.Y).Select(f => (f.x, f.x + f.w))));
            bool HasText((double L, double R) iv) => nextText.Any(t => t.R > iv.L && t.L < iv.R);
            // A side has text when its nearest stretch does, or a picture standing beside text on that side, as near as text beside a picture stands (an
            // icon at the start of a note is the note's column's).
            bool TextLeft(int k) => HasText(next[k]) || (k > 0 && HasText(next[k - 1]) && next[k].L - next[k - 1].R <= PictureGap);
            bool TextRight(int k) => HasText(next[k]) || (k + 1 < next.Count && HasText(next[k + 1]) && next[k + 1].L - next[k].R <= PictureGap);
            var gaps = new List<(double L, double R)>();
            for (var k = 1; k < next.Count; k++)
                if (next[k].L - next[k - 1].R >= gutter && TextLeft(k - 1) && TextRight(k))
                    gaps.Add((next[k - 1].R, next[k].L));
            // The gap after a list's labels is no gutter: on every row with text left of it, that
            // text is the row's label and the row's body starts where the gap ends.
            gaps.RemoveAll(gap => Enumerable.Range(start, j - start + 1).All(r =>
                !rows[r].Frags.Any(f => !string.IsNullOrWhiteSpace(f.Text) && (f.X + f.R) / 2 < gap.L)
                || (ListBodyX(rows[r]) is { } bodyX && Math.Abs(bodyX - gap.R) <= AlignTolerance)));
            if (gaps.Count == 0)
            {
                // Before the channel shows, the first rows may hold one column's text only
                // (columns rarely start on the same baseline); a wide row ends the band. Once
                // it shows, a row keeping to one side of every channel stays in the band: text
                // beside a figure, or one column going on after the other ended.
                var width = rowText.Count > 0 ? rowText.Max(t => t.R) - rowText.Min(t => t.L) : 0;
                var aside = channels.Count > 0 && rowText.Count > 0
                            && channels.All(c => rowText.All(t => t.R <= c.L || t.L >= c.R));
                // (the band's first row may be wide: rows before the channel that cross it are no rows of the band)
                if (!aside && (channels.Count > 0 || j - start >= MaxLeadRows || (width > NarrowRowShare * span && j > start))) break;
                cover = next;
                text = nextText;
                continue;
            }
            if (channels.Count == 0) lead = j;
            cover = next;
            text = nextText;
            channels = gaps;
        }
        return (j, channels, lead, false);
    }

    // Two sides are set in sizes of their own when their largest sizes differ by this much (points).
    private const double SizeApart = 1.0;

    /// <summary>Whether the text left of <paramref name="bound"/> and the text right of it are set in sizes of their own.</summary>
    private static bool SidesDiffer(List<Line> rows, double bound)
    {
        var frags = rows.SelectMany(l => l.Frags).Where(f => !string.IsNullOrWhiteSpace(f.Text)).ToList();
        var (left, right) = (frags.Where(f => f.R <= bound).ToList(), frags.Where(f => f.X >= bound).ToList());
        return left.Count > 0 && right.Count > 0 && Math.Abs(left.Max(f => f.Size) - right.Max(f => f.Size)) >= SizeApart;
    }

    // A line's glyphs stand from this share of its size under its baseline to this share over it.
    private const double GlyphsUnder = 0.2;
    private const double GlyphsOver = 0.75;
    // Two lines stand level when their glyphs share at least this share of the smaller one's size in height.
    private const double LevelShare = 0.25;

    /// <summary>Whether a line left of column bound <paramref name="b"/> stands level with one right of it: their
    /// glyphs share part of their height.</summary>
    private static bool StandLevel(List<(int Col, Line Piece)> pieces, int b)
    {
        static (double Low, double High) Glyphs(Line l) => (l.Baseline - GlyphsUnder * l.Size, l.Baseline + GlyphsOver * l.Size);
        return pieces.Where(x => x.Col <= b).Any(l => pieces.Where(x => x.Col > b).Any(r =>
        {
            var (a, c) = (Glyphs(l.Piece), Glyphs(r.Piece));
            return Math.Min(a.High, c.High) - Math.Max(a.Low, c.Low) >= LevelShare * Math.Min(l.Piece.Size, r.Piece.Size);
        }));
    }

    /// <summary>Merge intervals into disjoint ones, left to right.</summary>
    private static List<(double L, double R)> Union(IEnumerable<(double L, double R)> intervals)
    {
        var result = new List<(double L, double R)>();
        foreach (var iv in intervals.OrderBy(v => v.L))
        {
            if (result.Count > 0 && iv.L <= result[^1].R) result[^1] = (result[^1].L, Math.Max(result[^1].R, iv.R));
            else result.Add(iv);
        }
        return result;
    }

    /// <summary>A row's fragments split at the column bounds: one line per column it has text in.</summary>
    private static IEnumerable<(int Col, Line Piece)> SplitRow(Line row, List<double> bounds)
    {
        foreach (var group in row.Frags.GroupBy(f => bounds.Count(b => b < (f.X + f.R) / 2)).OrderBy(g => g.Key))
        {
            var frags = group.OrderBy(f => f.X).ToList();
            var size = DominantSize(frags.Select(f => (f.Size, f.Text)));
            var baseline = TextBaseline(frags.Select(f => (f.Size, f.Base, f.Text)), size);
            // The piece stands where its text does: a mark raised over it is no part of its place.
            var at = frags.OrderBy(f => Math.Abs(f.Base - baseline)).First();
            yield return (group.Key, new Line
            {
                Y = at.Y, Baseline = baseline, Size = size, MinX = frags[0].X, Frags = frags,
            });
        }
    }

    /// <summary>At the top of a column the previous line is the bottom of the column before:
    /// the paragraph goes on only when the text visibly continues there
    /// (<see cref="ContinuesAfterBreak"/>, the column's right edge being the edge its lines run to).</summary>
    private static bool ContinuesInNextColumn(Line last, Line first)
        => ContinuesAfterBreak(last, first, last.ColumnRight);
}
