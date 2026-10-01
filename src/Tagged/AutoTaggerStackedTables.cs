using System;
using System.Collections.Generic;
using System.Linq;

namespace Aspose.Pdf.Tagged;

internal static partial class AutoTagger
{
    /// <summary>A table read from its text standing right under another, its columns among the other's - each
    /// column's text ending where a column of the other's ends, or starting where one starts - goes on with it: rows
    /// that leave the table's last columns empty (weekly figures a series is not kept for) are rows of the table,
    /// no table of their own. The two become one, with the upper one's columns.</summary>
    private static void JoinStackedTextTables(PageWork pw, List<Line> content, int ruledCount)
    {
        if (pw.SideBySide.Count > 0) return;
        for (var i = ruledCount; i < pw.Tables.Count; i++)
            for (var j = ruledCount; j < pw.Tables.Count; j++)
            {
                if (i == j) continue;
                var (upper, lower) = (pw.Tables[i], pw.Tables[j]);
                if (lower.cols >= upper.cols || lower.region.URY > upper.region.LLY + GridLineTolerance) continue;
                var size = content.Where(l => InRegion(l, upper.region)).Select(l => l.Size).DefaultIfEmpty(0).Max();
                if (upper.region.LLY - lower.region.URY > HeaderLinePitchFactor * size) continue;
                // Nothing stands between them, and no rule across them closes the upper one.
                if (content.Any(l => l.Frags.Count > 0 && l.Y < upper.region.LLY - 2 && l.Y > lower.region.URY + 2
                                     && l.MinX < upper.region.URX && l.Frags[^1].R > upper.region.LLX)) continue;
                var between = (lower.rowY[^1] + upper.rowY[0]) / 2;
                if (pw.Rules.Any(r => r.Horizontal && r.Drawn && Math.Abs(r.At - between) <= GridLineTolerance + Math.Abs(upper.rowY[0] - lower.rowY[^1])
                                      && r.To - r.From >= GridRuleShare * (upper.colX[^1] - upper.colX[0]))) continue;
                if (ColumnsAmong(lower, upper, content) is not { } columns) continue;
                var rowY = lower.rowY.GetRange(0, lower.rowY.Count - 1);
                rowY.AddRange(upper.rowY);
                var joined = (new Rectangle(upper.region.LLX, lower.region.LLY, upper.region.URX, upper.region.URY),
                    upper.rows + lower.rows, upper.cols, upper.colX, rowY);
                var map = SingleCells(joined.Item2, upper.cols);
                var (above, under) = (pw.TableCells[i], pw.TableCells[j]);
                for (var r = 0; r < upper.rows; r++)
                    for (var c = 0; c < upper.cols; c++) map[r, c] = above[r, c];
                for (var r = 0; r < lower.rows; r++)
                    for (var c = 0; c < lower.cols; c++)
                        map[upper.rows + r, columns[c]] = (under[r, c].Row + upper.rows, columns[under[r, c].Col]);
                pw.Tables[i] = joined;
                pw.TableCells[i] = map;
                pw.Tables.RemoveAt(j);
                pw.TableCells.RemoveAt(j);
                if (j < i) i--;
                j = ruledCount - 1;
            }
    }

    /// <summary>The column of <paramref name="upper"/> each column of <paramref name="lower"/> is, left to right; null
    /// when one of them is none of the upper table's.</summary>
    private static int[]? ColumnsAmong((Rectangle region, int rows, int cols, List<double> colX, List<double> rowY) lower,
        (Rectangle region, int rows, int cols, List<double> colX, List<double> rowY) upper, List<Line> content)
    {
        (double L, double R)? Extent((Rectangle region, int rows, int cols, List<double> colX, List<double> rowY) t, int c)
        {
            var frags = content.Where(l => InRegion(l, t.region)).SelectMany(l => l.Frags)
                .Where(f => !string.IsNullOrWhiteSpace(f.Text) && (f.X + f.R) / 2 > t.colX[c] && (f.X + f.R) / 2 <= t.colX[c + 1]).ToList();
            return frags.Count == 0 ? null : (frags.Min(f => f.X), frags.Max(f => f.R));
        }
        var columns = new int[lower.cols];
        var next = 0;
        for (var c = 0; c < lower.cols; c++)
        {
            if (Extent(lower, c) is not { } own) return null;
            var at = Enumerable.Range(next, upper.cols - next).Where(u => Extent(upper, u) is { } theirs
                && (Math.Abs(theirs.R - own.R) <= AlignTolerance || Math.Abs(theirs.L - own.L) <= AlignTolerance)).Cast<int?>().FirstOrDefault();
            if (at is not { } found) return null;
            columns[c] = found;
            next = found + 1;
        }
        return columns;
    }
}
