using System;
using System.Collections.Generic;
using System.Linq;

namespace Aspose.Pdf.Tagged;

internal static partial class AutoTagger
{
    // A header block's lines stand at most this many line sizes apart, its lowest at most this
    // many over the table's first row (a rule and a blank line between).
    private const double HeaderLinePitchFactor = 3.0;
    // A heading may reach past the table's edges by this share of a column's width, but by no
    // more than this many ems: a name centred over a column of figures narrower than it.
    private const double HeadingOverhangShare = 0.6;
    private const double HeadingOverhangEms = 2.0;
    // A mark set in a size this share of the headings' or smaller, of at most MaxMarkChars, is a
    // note call between the headings, no heading.
    private const double HeadingMarkSizeShare = 0.8;
    // A one-cell line at the table's left edge of this many words or more is its caption or a
    // sentence over it, no heading; a section label ("Assets", "Date") has fewer.
    private const int CaptionMinWords = 3;
    // Lines whose bottoms lie within this share of a size share a header row: a one-line heading
    // standing between the two lines of its neighbours joins the upper one.
    private const double HeaderRowJoinFactor = 0.7;
    // A small unruled table found among the headings (a pair of group headings) has at most
    // this many rows; it dissolves into the header block.
    private const int MaxHeaderFragmentRows = 3;
    // A table takes a header block when at least this share of its filled cells hold figures:
    // a statistical table; a list of names or a glossary set in columns takes none.
    private const double HeaderBlockNumericShare = 0.5;

    /// <summary>The block of headings over an unruled table - the column names, the periods,
    /// the group headings spanning columns, stacked over two or three lines - becomes header
    /// rows of the table, each cell spanning the columns it stands over. The tables the block's
    /// own frames and groupings formed (a ruled box round a group heading, two headings read
    /// as a small table) dissolve into it. Tables set side by side are left alone. Returns how many of the page's
    /// first tables are ruled ones, after those that dissolved.</summary>
    private static int AdoptHeaderBlocks(PageWork pw, List<Line> content, int ruledCount)
    {
        if (pw.SideBySide.Count > 0) return ruledCount;
        for (var i = ruledCount; i < pw.Tables.Count; i++)
        {
            var t = pw.Tables[i];
            if (!NumericBody(t, content)) continue;
            // Headings set in a ruled box over the table: the box's bands and dividers are the header's rows and cells.
            if (HeaderBox(pw, t, content) is { } ruledBox && AdoptRuledHeader(pw, i, ruledBox, content))
            {
                for (var k = pw.Tables.Count - 1; k >= 0; k--)
                {
                    var inside = pw.Tables[k].region;
                    if (k == i || inside.LLY < ruledBox.LLY - GridLineTolerance || inside.URY > ruledBox.URY + GridLineTolerance) continue;
                    pw.Tables.RemoveAt(k);
                    pw.TableCells.RemoveAt(k);
                    if (k < ruledCount) ruledCount--;
                    if (k < i) i--;
                }
                continue;
            }
            var block = HeaderBlock(t, pw.Tables, i, content);
            if (block.Count == 0) continue;
            var rows = HeaderRows(block, t.colX);
            JoinRowsBetween(rows, t.colX);
            var standing = StandingDown(rows, t.colX, out var midway);
            // A row's edges are set by its own lines, not by the headings standing midway beside them.
            List<Line> Own(int r) => rows[r].Where(l => !midway.Contains(l)).ToList() is { Count: > 0 } own ? own : rows[r];
            var size = block.Max(l => l.Size);
            // Row edges run bottom to top: the table's own, then one between each pair of
            // header rows, then the block's top - halfway to the line above it at most.
            var rowY = t.rowY.GetRange(0, t.rowY.Count - 1);
            var firstBaseline = content.Where(l => InRegion(l, t.region)).Select(l => l.Baseline).DefaultIfEmpty(t.region.URY).Max();
            rowY.Add((rows[^1].Min(l => l.Baseline) + firstBaseline) / 2 + 0.3 * size);
            for (var r = rows.Count - 1; r > 0; r--)
                rowY.Add(Math.Min((Own(r).Max(l => l.Baseline) + Own(r - 1).Min(l => l.Baseline)) / 2 + 0.3 * size,
                    Own(r - 1).Min(l => l.Baseline) - RowEdgeClearance));
            var topLine = rows[0].OrderByDescending(l => l.Y).First();
            var above = content.Where(l => l.Y > topLine.Y + LineTolerance).Select(l => l.Y).DefaultIfEmpty(double.MaxValue).Min();
            var top = Math.Min(topLine.Y + size, (topLine.Y + above) / 2);
            // A ruled box holding nothing but lines of the block is the header's own frame: its top rule is the header's top.
            var frames = Enumerable.Range(0, pw.Tables.Count).Where(k => k != i && k < ruledCount && HoldsOnly(pw.Tables[k].region, block, content)).ToList();
            top = frames.Select(k => pw.Tables[k].region.URY).Where(y => y <= top + size).DefaultIfEmpty(top).Max();
            rowY.Add(top);
            var joined = (new Rectangle(t.region.LLX, t.region.LLY, t.region.URX, top), t.rows + rows.Count, t.cols, t.colX, rowY);
            var map = SingleCells(joined.Item2, t.cols);
            for (var r = 0; r < rows.Count; r++)
                SpanRow(map, joined, r, rows[r].SelectMany(Cells).OrderBy(c => c.X).ToList());
            // A heading standing between two heading rows, in columns neither holds, spans both.
            foreach (var (r, columns) in standing)
                foreach (var c in columns.Where(c => map[r + 1, c] == (r + 1, c)))
                    map[r + 1, c] = map[r, c];
            var old = pw.TableCells[i];
            for (var r = 0; r < t.rows; r++)
                for (var c = 0; c < t.cols; c++)
                    map[rows.Count + r, c] = (old[r, c].Row + rows.Count, old[r, c].Col);
            // A heading over a group of columns spans the columns its straddle rule runs over.
            map = SpanByRules(joined, map, content, pw.Rules, rows.Count);
            pw.Tables[i] = joined;
            pw.TableCells[i] = map;
            // The tables inside the block dissolve: their lines are the header's now.
            for (var k = pw.Tables.Count - 1; k >= 0; k--)
            {
                if (k == i) continue;
                var box = pw.Tables[k].region;
                if ((box.LLY < t.region.URY - 1 || box.URY > top + 1) && !frames.Contains(k)) continue;
                pw.Tables.RemoveAt(k);
                pw.TableCells.RemoveAt(k);
                if (k < ruledCount) ruledCount--;
                if (k < i) i--;
            }
        }
        return ruledCount;
    }

    /// <summary>Whether a table's region holds lines, every one of them a line of a header block.</summary>
    private static bool HoldsOnly(Rectangle region, List<Line> block, List<Line> content)
    {
        var inside = content.Where(l => l.Frags.Count > 0 && InRegion(l, region)).ToList();
        return inside.Count > 0 && inside.All(block.Contains);
    }

    /// <summary>The heading lines over table <paramref name="index"/>, lowest first: each within
    /// the pitch of the one below, its cells over the table's columns (a cell may span several,
    /// or reach a little past an edge), in no other table but a ruled box or a small unruled
    /// fragment standing over this one, and no caption: a one-cell line starting at the table's
    /// left edge that runs to several words or is set larger.</summary>
    private static List<Line> HeaderBlock((Rectangle region, int rows, int cols, List<double> colX, List<double> rowY) t,
        List<(Rectangle region, int rows, int cols, List<double> colX, List<double> rowY)> tables, int index, List<Line> content)
    {
        var block = new List<Line>();
        var own = content.Where(l => InRegion(l, t.region)).ToList();
        if (own.Count == 0) return block;
        var size = own.Max(l => l.Size);
        var overhang = Math.Min(HeadingOverhangShare * (t.colX[^1] - t.colX[0]) / t.cols, HeadingOverhangEms * size);
        var prevY = own.Max(l => l.Y);
        foreach (var line in content.Where(l => l.Y > prevY && l.Frags.Count > 0).OrderBy(l => l.Y))
        {
            if (line.Y - prevY > HeaderLinePitchFactor * size) break;
            if (block.Count >= MaxHeaderRows) break;
            // A note call between the headings is no line of them.
            if (line.Size <= HeadingMarkSizeShare * size && line.Frags.All(f => f.Text.Trim().Length <= MaxMarkChars)) continue;
            // A line of another table is no heading of this one, unless that table is a small
            // fragment standing wholly over this one (a ruled box, a pair of group headings).
            var other = tables.FindIndex(o => !ReferenceEquals(o.colX, t.colX) && InRegion(line, o.region));
            if (other >= 0 && other != index
                && (tables[other].rows > MaxHeaderFragmentRows || tables[other].region.LLY < t.region.URY - size)) break;
            var cells = Cells(line);
            if (cells.Count == 0 || cells[0].X < t.colX[0] - overhang || cells[^1].R > t.colX[^1] + overhang) break;
            // Headings stand apart over their columns; a line of prose fills its measure, its
            // words a cell each, and no table has a row of more cells than columns.
            var filled = cells.Sum(c => c.R - c.X) / Math.Max(1, cells[^1].R - cells[0].X);
            if (cells.Count > t.cols || (cells.Count > 2 && filled >= ProseFill)) break;
            if (cells.Count == 1 && cells[0].X <= t.colX[0] + AlignTolerance
                && (line.Size > 1.15 * size || Words(line) >= CaptionMinWords)) break;
            block.Add(line);
            prevY = line.Y;
        }
        return block;
    }

    /// <summary>Whether most of the table's filled cells hold figures.</summary>
    private static bool NumericBody((Rectangle region, int rows, int cols, List<double> colX, List<double> rowY) t, List<Line> content)
    {
        var (_, text) = ScanCells(t, content);
        var filled = 0;
        var numeric = 0;
        for (var r = 0; r < t.rows; r++)
            for (var c = 0; c < t.cols; c++)
            {
                if (text[r, c] is null) continue;
                filled++;
                if (NumericCell.IsMatch(text[r, c]!.ToString())) numeric++;
            }
        return filled > 0 && numeric >= HeaderBlockNumericShare * filled;
    }

    private static int Words(Line line)
        => string.Join(" ", line.Frags.Select(f => f.Text)).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>A rule running under a table's last row across the whole table, nothing between them, within a row's
    /// height of it, closes the table: the last row runs down to it, and the rule is that row's lower edge. So over its
    /// first row: the table opens at it.</summary>
    private static void CloseTablesAtRules(PageWork pw, List<Line> content)
    {
        for (var i = 0; i < pw.Tables.Count; i++)
        {
            var t = pw.Tables[i];
            if (t.rowY.Count < 3) continue;
            var edges = t.rowY.OrderBy(y => y).ToList();
            var (bottom, top) = (edges[0], edges[^1]);
            if (EdgeRule(pw, content, t.colX, bottom, bottom - (edges[1] - edges[0])) is { } closing)
            {
                t.rowY[t.rowY.IndexOf(bottom)] = closing;
                t = (new Rectangle(t.region.LLX, Math.Min(t.region.LLY, closing), t.region.URX, t.region.URY), t.rows, t.cols, t.colX, t.rowY);
            }
            if (EdgeRule(pw, content, t.colX, top, top + (edges[^1] - edges[^2])) is { } opening)
            {
                t.rowY[t.rowY.IndexOf(top)] = opening;
                t = (new Rectangle(t.region.LLX, t.region.LLY, t.region.URX, Math.Max(t.region.URY, opening)), t.rows, t.cols, t.colX, t.rowY);
            }
            pw.Tables[i] = t;
        }
    }

    /// <summary>A table found by its text has its column edges midway between its cells' texts; where an upright rule
    /// stands between two columns' texts beside its rows, the edge is the rule, and the table reaches as far across as
    /// the rules along its rows run; a rule across it between two rows' texts is the edge between those rows.</summary>
    private static void SnapColumnsToRules(PageWork pw, List<Line> content, int ruledCount)
    {
        for (var i = ruledCount; i < pw.Tables.Count; i++)
        {
            var t = pw.Tables[i];
            var frags = content.Where(l => InRegion(l, t.region)).SelectMany(l => l.Frags).Where(f => !string.IsNullOrWhiteSpace(f.Text)).ToList();
            var colX = new List<double>(t.colX);
            for (var k = 1; k < t.cols; k++)
            {
                var edge = t.colX[k];
                var before = frags.Where(f => f.R <= edge + AlignTolerance && f.X >= t.colX[k - 1] - AlignTolerance).Select(f => (double?)f.R).Max();
                var after = frags.Where(f => f.X >= edge - AlignTolerance && f.R <= t.colX[k + 1] + AlignTolerance).Select(f => (double?)f.X).Min();
                if (before is not { } from || after is not { } to || to <= from) continue;
                var rule = pw.Rules.Where(r => !r.Horizontal && r.Drawn && r.At > from && r.At < to && r.To > t.region.LLY && r.From < t.region.URY)
                    .Select(r => (double?)r.At).OrderBy(x => Math.Abs(x!.Value - edge)).FirstOrDefault();
                if (rule is { } at) colX[k] = at;
            }
            var along = pw.Rules.Where(r => r.Horizontal && r.Drawn && r.At >= t.region.LLY - GridLineTolerance && r.At <= t.region.URY + GridLineTolerance
                                            && Math.Min(r.To, t.colX[^1]) - Math.Max(r.From, t.colX[0]) >= GridRuleShare * (t.colX[^1] - t.colX[0])).ToList();
            if (along.Count > 0)
            {
                // No text of the page stands beside the table where the rules run on.
                var (left, right) = (along.Min(r => r.From), along.Max(r => r.To));
                bool Clear(double lo, double hi) => !content.Any(l => l.Y >= t.region.LLY && l.Y <= t.region.URY
                                                                  && l.Frags.Any(f => !string.IsNullOrWhiteSpace(f.Text) && f.R > lo && f.X < hi));
                if (right > colX[^1] && Clear(t.colX[^1] + AlignTolerance, right)) colX[^1] = right;
                if (left < colX[0] && Clear(left, t.colX[0] - AlignTolerance)) colX[0] = left;
            }
            // So between its rows: a rule across the table between two rows' texts is the edge between them.
            var rowY = new List<double>(t.rowY);
            for (var j = 1; j + 1 < rowY.Count && rowY.Count == t.rows + 1; j++)
            {
                var below = frags.Where(f => f.Base > t.rowY[j - 1] && f.Base <= t.rowY[j]).Select(f => (double?)(f.Base + TextBoxAscent * f.Size)).Max();
                var above = frags.Where(f => f.Base > t.rowY[j] && f.Base <= t.rowY[j + 1]).Select(f => (double?)(f.Base - TextBoxDescent * f.Size)).Min();
                if (below is not { } from || above is not { } to || to <= from) continue;
                var rule = along.Where(r => r.At > from && r.At < to).Select(r => (double?)r.At).OrderBy(y => Math.Abs(y!.Value - t.rowY[j])).FirstOrDefault();
                if (rule is { } at) rowY[j] = at;
            }
            if ((colX.SequenceEqual(t.colX) && rowY.SequenceEqual(t.rowY)) || Enumerable.Range(1, colX.Count - 1).Any(k => colX[k] <= colX[k - 1])) continue;
            pw.Tables[i] = (new Rectangle(colX[0], t.region.LLY, colX[^1], t.region.URY), t.rows, t.cols, colX, rowY);
        }
    }

    /// <summary>Where a rule across a whole table stands beyond its edge at <paramref name="edge"/>, no further than
    /// <paramref name="reach"/>, no text between: the nearest to the edge; null when none does.</summary>
    private static double? EdgeRule(PageWork pw, List<Line> content, List<double> colX, double edge, double reach)
    {
        var (lo, hi) = (Math.Min(edge, reach), Math.Max(edge, reach));
        return pw.Rules.Where(r => r.Horizontal && r.Drawn && r.At >= lo && r.At <= hi && r.At != edge
                                   && r.From <= colX[0] + 2 * AlignTolerance && r.To >= colX[^1] - 2 * AlignTolerance
                                   && !content.Any(l => l.Frags.Count > 0 && l.Y > Math.Min(r.At, edge) && l.Y < Math.Max(r.At, edge)
                                                        && l.MinX < colX[^1] && l.Frags[^1].R > colX[0]))
            .Select(r => (double?)r.At).OrderBy(at => Math.Abs(at!.Value - edge)).FirstOrDefault();
    }

    // A row edge stands at least this far (points) under the lowest baseline of the row above it.
    private const double RowEdgeClearance = 0.5;

    /// <summary>The header rows' headings set lower than the rest of their row, in columns the row under it leaves
    /// empty - "Account" set midway between a row of years and the row of months under it - span down over that row:
    /// each row with the columns it spans down in.</summary>
    private static List<(int Row, List<int> Columns)> StandingDown(List<List<Line>> rows, List<double> colX, out HashSet<Line> midway)
    {
        var standing = new List<(int Row, List<int> Columns)>();
        midway = new HashSet<Line>();
        for (var r = 0; r < rows.Count - 1; r++)
        {
            var below = rows[r + 1].SelectMany(l => ColumnsOf(l, colX)).ToHashSet();
            var top = rows[r].Max(l => l.Baseline);
            var next = rows[r + 1].Max(l => l.Baseline);
            // Set lower than its row's top line, and no further from the row under it than from that line.
            var lines = rows[r].Where(l => l.Baseline < top && l.Baseline - next <= top - l.Baseline + RowEdgeClearance
                                           && ColumnsOf(l, colX) is { Count: > 0 } c && !c.Any(below.Contains)).ToList();
            midway.UnionWith(lines);
            var columns = lines.SelectMany(l => ColumnsOf(l, colX)).ToList();
            if (columns.Count > 0) standing.Add((r, columns));
        }
        return standing;
    }

    /// <summary>A header row whose headings stand in columns the rows over and under it both leave empty is no row of its
    /// own: its headings stand midway beside those two rows ("Date" beside a group heading and the names under it), and
    /// its lines join the row above.</summary>
    private static void JoinRowsBetween(List<List<Line>> rows, List<double> colX)
    {
        for (var r = 1; r + 1 < rows.Count; r++)
        {
            var own = rows[r].SelectMany(l => ColumnsOf(l, colX)).ToHashSet();
            if (own.Count == 0 || rows[r - 1].SelectMany(l => ColumnsOf(l, colX)).Any(own.Contains)
                || rows[r + 1].SelectMany(l => ColumnsOf(l, colX)).Any(own.Contains)) continue;
            rows[r - 1].AddRange(rows[r]);
            rows.RemoveAt(r--);
        }
    }

    /// <summary>The columns a line's cells stand in, by their middles.</summary>
    private static List<int> ColumnsOf(Line line, List<double> colX)
        => Cells(line).Select(c => Math.Max(0, Math.Min(colX.Count - 2, colX.FindLastIndex(edge => edge <= (c.X + c.R) / 2)))).Distinct().ToList();

    /// <summary>The block's lines as rows, top to bottom: lines whose bottoms lie within a share of a size of the row's
    /// first line share it, and so does a line within that of the row's last line heading columns the row leaves empty
    /// ("Account" a little under the years, over the months).</summary>
    private static List<List<Line>> HeaderRows(List<Line> block, List<double> colX)
    {
        var rows = new List<List<Line>>();
        foreach (var line in block.OrderByDescending(l => l.Y))
        {
            bool Near(Line other) => Math.Abs(other.Y - line.Y) <= HeaderRowJoinFactor * Math.Max(line.Size, other.Size);
            // A line joins the row above near its first line, or near its last where it heads columns the row leaves empty.
            if (rows.Count > 0 && (Near(rows[^1][0])
                                   || (Near(rows[^1][^1]) && !ColumnsOf(line, colX).Intersect(rows[^1].SelectMany(l => ColumnsOf(l, colX))).Any())))
                rows[^1].Add(line);
            else rows.Add(new List<Line> { line });
        }
        return rows;
    }
}
