using System;
using System.Collections.Generic;
using System.Linq;
using LS = Aspose.Pdf.LogicalStructure;

namespace Aspose.Pdf.Tagged;

internal static partial class AutoTagger
{
    // A straddle rule is shorter than this share of its table's width (a rule across the table heads no group).
    private const double StraddleRuleShare = 0.9;

    /// <summary>A text table's heading rows with straddle rules: a heading over a group of columns (a year over
    /// its quarters) set centred over them, a short rule under it running as far as the group does. Its text
    /// tells where it stands, not how far it reaches; the rule tells: the heading spans the columns whose middles
    /// the rule runs over. A heading the rule's columns took from goes back to its own column.</summary>
    private static (int Row, int Col)[,] SpanByRules(
        (Aspose.Pdf.Rectangle region, int rows, int cols, List<double> colX, List<double> rowY) t,
        (int Row, int Col)[,] map, List<Line> lines, List<PageContentScan.Rule> rules, int? headingRows = null)
    {
        var width = t.colX[^1] - t.colX[0];
        for (var r = 0; r < (headingRows ?? t.rows); r++)
        {
            // Row r (from the top) lies between rowY[rows - 1 - r] and rowY[rows - r].
            double top = t.rowY[t.rows - r], bottom = t.rowY[t.rows - r - 1];
            var inRow = lines.Where(l => l.Baseline > bottom && l.Baseline < top && l.Frags.Count > 0
                                         && l.MinX >= t.colX[0] - AlignTolerance && l.Frags[^1].R <= t.colX[^1] + AlignTolerance).ToList();
            var pieces = inRow.SelectMany(l => Cells(l).Select(p => (p.X, p.R, l.Y))).ToList();
            if (pieces.Count == 0 || pieces.Count >= t.cols) continue;
            foreach (var (x, right, y) in pieces)
            {
                var middle = (x + right) / 2;
                var rule = rules.Where(h => h.Horizontal && h.At >= bottom - GridLineTolerance && h.At <= y + GridLineTolerance
                                            && h.To - h.From < StraddleRuleShare * width && h.From <= middle && h.To >= middle)
                    .Select(h => (PageContentScan.Rule?)h).FirstOrDefault();
                if (rule is not { } h) continue;
                var covered = Enumerable.Range(0, t.cols)
                    .Where(c => (t.colX[c] + t.colX[c + 1]) / 2 >= h.From - AlignTolerance && (t.colX[c] + t.colX[c + 1]) / 2 <= h.To + AlignTolerance).ToList();
                if (covered.Count < 2) continue;
                int first = covered[0], last = covered[^1];
                // The cells the group's columns cut into go back to single cells; the group is one cell.
                var cut = Enumerable.Range(first, last - first + 1).Select(c => map[r, c].Col).ToHashSet();
                for (var c = 0; c < t.cols; c++)
                    if (cut.Contains(map[r, c].Col)) map[r, c] = (r, c);
                for (var c = first; c <= last; c++) map[r, c] = (r, first);
            }
        }
        return map;
    }

    /// <summary>Where the text of a table's cell stands: the left and right ends of the page's text inside the
    /// cell's box, and the text; null for an empty cell.</summary>
    private static (double L, double R, string Text)? TextIn(List<Line> lines, double left, double right, double bottom, double top)
    {
        var frags = lines.Where(l => l.Baseline > bottom && l.Baseline <= top)
            .SelectMany(l => l.Frags)
            .Where(f => !string.IsNullOrWhiteSpace(f.Text) && (f.X + f.R) / 2 >= left && (f.X + f.R) / 2 <= right).ToList();
        return frags.Count == 0 ? null : (frags.Min(f => f.X), frags.Max(f => f.R), string.Concat(frags.Select(f => f.Text)).Trim());
    }

    private static readonly System.Text.RegularExpressions.Regex MarkedFigure = new(
        "^" + Figure + @"\s+[a-z]$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Where the figure of a table's cell ends: a figure with the letter of a note after it ("124.0 e") ends
    /// where its last digit does, the letter standing past the edge the column's figures end at.</summary>
    private static (double L, double R, string Text)? FigureIn(List<Line> lines, double left, double right, double bottom, double top)
    {
        if (TextIn(lines, left, right, bottom, top) is not { } whole) return null;
        if (!MarkedFigure.IsMatch(whole.Text)) return whole;
        var frags = lines.Where(l => l.Baseline > bottom && l.Baseline <= top).SelectMany(l => l.Frags)
            .Where(f => !string.IsNullOrWhiteSpace(f.Text) && (f.X + f.R) / 2 >= left && (f.X + f.R) / 2 <= right).OrderBy(f => f.X).ToList();
        var figure = whole.Text[..^1].TrimEnd();
        // The letter drawn on its own ends the cell; drawn with the figure, it takes its share of the run's width.
        if (frags.Count > 1 && frags[^1].Text.Trim().Length == 1) return (whole.L, frags[^2].R, figure);
        var last = frags[^1];
        var text = last.Text.TrimEnd();
        var kept = text[..^1].TrimEnd();
        return (whole.L, last.X + (last.R - last.X) * FigureWidth(kept) / FigureWidth(text), figure);
    }

    // How wide a figure's characters run against each other, in a face whose figures are one width: a digit 1, a point,
    // a comma or a space half of it.
    private static double FigureWidth(string text) => text.Sum(c => char.IsDigit(c) || char.IsLetter(c) ? 1.0 : 0.5);

    private static readonly System.Text.RegularExpressions.Regex OneFigure = new(
        "^" + Figure + "$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>The columns of a table set flush right: their body cells (under the heading rows) end at one edge
    /// and start ragged - a statistical table's figures - with the edge they end at.</summary>
    private static (bool[] Flush, double[] Right) FlushRightColumns(List<Line> lines, (int Row, int Col)[,]? cells, List<double> colX,
        List<double> rowY, int rows, int headerRows)
    {
        var cols = colX.Count - 1;
        var flush = new bool[cols];
        var rightEdge = new double[cols];
        // A column of figures as wide as each other ("n.a." all down) ends and starts at one edge: it is set as
        // the table's other columns of figures are.
        var even = new bool[cols];
        for (var c = 0; c < cols; c++)
        {
            var ends = new List<(double L, double R, string Text)>();
            for (var r = headerRows; r < rows; r++)
            {
                if (cells is not null && (cells[r, c] != (r, c) || (c + 1 < cols && cells[r, c + 1] == (r, c)))) continue;
                if (FigureIn(lines, colX[c], colX[c + 1], rowY[rows - r - 1], rowY[rows - r]) is { } e) ends.Add(e);
            }
            flush[c] = ends.Count >= 2 && ends.Max(e => e.R) - ends.Min(e => e.R) <= AlignTolerance
                                       && ends.Max(e => e.L) - ends.Min(e => e.L) > AlignTolerance;
            even[c] = c > 0 && ends.Count >= 2 && ends.Max(e => e.R) - ends.Min(e => e.R) <= AlignTolerance
                      && ends.Max(e => e.L) - ends.Min(e => e.L) <= AlignTolerance && ends.All(e => OneFigure.IsMatch(e.Text));
            if (flush[c] || even[c]) rightEdge[c] = ends.Max(e => e.R);
        }
        if (flush.Any(f => f))
            for (var c = 0; c < cols; c++) flush[c] |= even[c];
        return (flush, rightEdge);
    }

    /// <summary>Where each column's body cells start furthest left: the edge a cell set in from it (a sub-item's label under
    /// its group's, a level further in) is set in from; NaN for a column with no text.</summary>
    private static double[] ColumnStarts(List<Line> lines, (int Row, int Col)[,]? cells, List<double> colX, List<double> rowY, int rows, int headerRows)
    {
        var starts = new double[colX.Count - 1];
        for (var c = 0; c < starts.Length; c++)
        {
            starts[c] = double.NaN;
            for (var r = headerRows; r < rows; r++)
            {
                if (cells is not null && (cells[r, c] != (r, c) || (c + 1 < starts.Length && cells[r, c + 1] == (r, c)))) continue;
                if (TextIn(lines, colX[c], colX[c + 1], rowY[rows - r - 1], rowY[rows - r]) is { } e && !(e.L >= starts[c]))
                    starts[c] = e.L;
            }
        }
        return starts;
    }

    // A run of spaces ending within this many points of where a label starts is set before it.
    private const double SpaceJoin = 0.5;

    /// <summary>State how far a body cell's label starts in from where its column's cells start furthest left (the Layout
    /// /StartIndent): a label set in a level under the one above it. Nothing for a cell starting with the column.</summary>
    private static void StateCellIndent(LS.StructureElement cell, List<Line> lines, double columnStart, double left, double right, double bottom, double top)
    {
        if (double.IsNaN(columnStart) || TextIn(lines, left, right, bottom, top) is not { } e || OneFigure.IsMatch(e.Text)) return;
        // Spaces the label is set in with show its indent themselves: it starts where they do.
        var start = lines.Where(l => l.Baseline > bottom && l.Baseline <= top).SelectMany(l => l.Frags)
            .Where(f => string.IsNullOrWhiteSpace(f.Text) && f.X >= left - AlignTolerance && f.X < e.L && f.R <= e.L + SpaceJoin)
            .Select(f => f.X).DefaultIfEmpty(e.L).Min();
        if (start - columnStart <= AlignTolerance) return;
        var a = new LS.StructureAttribute(LS.AttributeKey.StartIndent);
        a.SetNumberValue(Math.Round(start - columnStart, 1));
        cell.Attributes.GetAttributes(LS.AttributeOwnerStandard.Layout).SetAttribute(a);
    }

    /// <summary>The columns of a table set centred: their cells, headings and all, stand round one middle, not all
    /// as wide as each other (cells of one width starting at one edge show nothing).</summary>
    private static bool[] CentredColumns(List<Line> lines, (int Row, int Col)[,]? cells, List<double> colX, List<double> rowY, int rows)
    {
        var centred = new bool[colX.Count - 1];
        for (var c = 0; c < centred.Length; c++)
        {
            var set = new List<(double L, double R, string Text)>();
            for (var r = 0; r < rows; r++)
            {
                if (cells is not null && (cells[r, c] != (r, c) || (c + 1 < centred.Length && cells[r, c + 1] == (r, c)))) continue;
                if (TextIn(lines, colX[c], colX[c + 1], rowY[rows - r - 1], rowY[rows - r]) is { } e) set.Add(e);
            }
            centred[c] = set.Count >= MinCentredCells
                         && set.Max(e => (e.L + e.R) / 2) - set.Min(e => (e.L + e.R) / 2) <= SameEdgeTolerance
                         && set.Max(e => e.L) - set.Min(e => e.L) > SameEdgeTolerance;
        }
        return centred;
    }

    // A column shows it is set centred with this many cells or more.
    private const int MinCentredCells = 3;

    // A column of figures set flush right ends this far (points) past its figures, as the table's last column does.
    private const double FlushRightPad = 1;

    /// <summary>The column edges a table's cells state their widths by. Between two columns of figures set flush
    /// right the edge stands right after the first one's figures, not midway between them: a reader sets such a
    /// cell's figures against its right edge, so the figures of every column stand where the PDF has them, the
    /// narrow columns' too ("n.a." beside wide figures). An edge an upright rule is drawn on stays where the rule
    /// stands: the figures before it state how far short of it they end (<see cref="StateEndIndent"/>).</summary>
    private static List<double> WidthEdges(List<double> colX, bool[] flush, double[] right, List<PageContentScan.Rule>? edgeRules)
    {
        var edges = new List<double>(colX);
        for (var c = 1; c < flush.Length; c++)
            if (flush[c - 1] && flush[c] && edgeRules?.Any(r => !r.Horizontal && r.Drawn && r.At == colX[c]) != true)
                edges[c] = Math.Min(right[c - 1] + FlushRightPad, colX[c]);
        return edges;
    }

    /// <summary>State how far short of its column's edge a cell set flush right ends (the Layout /EndIndent): figures
    /// standing in the middle of a ruled column, flush right among themselves. Nothing for figures ending at the edge.</summary>
    private static void StateEndIndent(LS.StructureElement cell, double indent)
    {
        if (indent <= FlushRightPad + AlignTolerance) return;
        var a = new LS.StructureAttribute(LS.AttributeKey.EndIndent);
        a.SetNumberValue(Math.Round(indent, 1));
        cell.Attributes.GetAttributes(LS.AttributeOwnerStandard.Layout).SetAttribute(a);
    }

    /// <summary>How a table cell's text is set in its box: flush right in a column set so; centred when it spans
    /// columns and stands clear of both its sides, as far from the one as from the other within half the room
    /// they leave; null (flush left) otherwise.</summary>
    private static LS.AttributeName? CellAlign(List<Line> lines, bool[] flushRight, int c, int colSpan,
        double left, double right, double bottom, double top, List<PageContentScan.Rule>? rules = null, (double L, double R) table = default,
        bool[]? centred = null)
    {
        // A column of figures set flush right is no centred one, whatever stands over it.
        if (colSpan == 1 && centred is not null && centred[c] && !flushRight[c]) return LS.AttributeName.TextAlign_Center;
        if (colSpan == 1)
        {
            if (rules is null || RuledBox(lines, rules, table, left, right, bottom, top) is not { } box)
                return flushRight[c] ? LS.AttributeName.TextAlign_End : null;
            (left, right) = box;
        }
        if (TextIn(lines, left, right, bottom, top) is not { } e) return null;
        double before = e.L - left, after = right - e.R;
        // A line of text running across its cell is set from its start, however even the room left round it (a worksheet's
        // entry with its leader dots); a heading centred over its columns is short.
        var size = lines.Where(l => l.Baseline > bottom && l.Baseline <= top && l.Frags.Count > 0).Select(l => l.Size).DefaultIfEmpty(0).Max();
        if (e.R - e.L >= FillingShare * (right - left) && e.R - e.L >= RunningLineEms * size) return null;
        return before > AlignTolerance && after > AlignTolerance && Math.Abs(before - after) <= (before + after) / 2
            ? LS.AttributeName.TextAlign_Center : null;
    }

    /// <summary>The box upright rules close a heading cell's text in across: the nearest rule standing beside its row on
    /// each side of the text, or the table's edge on a side no rule stands; null when no rule stands on either side.</summary>
    private static (double L, double R)? RuledBox(List<Line> lines, List<PageContentScan.Rule> rules, (double L, double R) table,
        double left, double right, double bottom, double top)
    {
        if (TextIn(lines, left, right, bottom, top) is not { } e) return null;
        var uprights = rules.Where(r => !r.Horizontal && r.Drawn && r.From < top && r.To > bottom
                                        && Math.Min(r.To, top) - Math.Max(r.From, bottom) >= RuledSideShare * (top - bottom)).ToList();
        var before = uprights.Where(r => r.At <= e.L && r.At >= table.L - AlignTolerance).Select(r => (double?)r.At).Max();
        var after = uprights.Where(r => r.At >= e.R && r.At <= table.R + AlignTolerance).Select(r => (double?)r.At).Min();
        if (before is null && after is null) return null;
        // On a side no upright rule closes, the row's rules end the box: the rule running over or under it across its text.
        var runs = rules.Where(r => r.Horizontal && r.Drawn && r.From <= e.L && r.To >= e.R
                                    && r.At >= bottom - BorderReach && r.At <= top + BorderReach).ToList();
        return (before ?? runs.Select(r => (double?)r.From).Min() ?? table.L, after ?? runs.Select(r => (double?)r.To).Max() ?? table.R);
    }

    // Text across at least this share of its cell, and at least this many of its size long, runs across it.
    private const double FillingShare = 0.9;
    private const double RunningLineEms = 20.0;

    // An upright rule stands beside a row when it runs along at least this share of the row's height.
    private const double RuledSideShare = 0.5;

    /// <summary>State a table cell's /TextAlign (the Layout attribute, ISO 32000-1 §14.8.5.4.2).</summary>
    private static void StateTextAlign(LS.StructureElement cell, LS.AttributeName? align)
    {
        if (align is not { } name) return;
        var a = new LS.StructureAttribute(LS.AttributeKey.TextAlign);
        a.SetNameValue(name);
        cell.Attributes.GetAttributes(LS.AttributeOwnerStandard.Layout).SetAttribute(a);
    }
}
