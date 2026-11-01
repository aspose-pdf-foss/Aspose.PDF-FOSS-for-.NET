namespace Aspose.Pdf.Text;

public sealed partial class TableAbsorber
{
    // Columns nothing rules, inside a lattice that is ruled. A block ruled row by
    // row, or a table ruled by its row lines and one separator, sets its labels
    // and figures in columns the reader sees by the white between them. Within one
    // ruled column of an already detected table, a strip of white that separates
    // the text of enough lines, in more than one ruled row, is a column boundary;
    // a cell whose text runs across the strip stays one cell.

    /// <summary>White between two pieces of text on a line, in line heights, that
    /// can separate columns. A word space is about a quarter of the height.</summary>
    private const double TextColumnGapEm = 1.0;

    /// <summary>A text column boundary needs this many lines with text on both sides of it.</summary>
    private const int MinTextColumnLines = 3;

    /// <summary>...and those lines must lie in at least this many ruled rows: the
    /// aligned lines of a single cell are a list, not columns.</summary>
    private const int MinTextColumnRows = 2;

    /// <summary>Cells whose sides differ by no more than this share a ruled column.</summary>
    private const double SameColumnTolerance = 0.5;

    private readonly record struct TextSpan(double X1, double X2, int Row, int Line, double Height);

    /// <summary>Split the cells of each ruled column of each table at the text
    /// column boundaries found in it. Tables without such boundaries are returned
    /// as they are.</summary>
    private static void SplitUnruledColumns(List<AbsorbedTable> tables, List<TextFragment> fragments)
    {
        for (var t = 0; t < tables.Count; t++)
        {
            var table = tables[t];
            if (table.Rect is not { } tr || table.Rows.Count < MinTextColumnRows) continue;
            var inside = fragments.Where(f => !string.IsNullOrWhiteSpace(f.Text) && CentreInside(f.Rectangle!, tr)).ToList();
            if (inside.Count == 0) continue;

            // Boundaries per ruled column, keyed by the column's left and right side;
            // each column is examined once, whatever it gives.
            var cuts = new List<(double left, double right, List<double> xs, List<TextSpan> text)>();
            foreach (var cell in table.Rows.SelectMany(r => r.Cells))
            {
                if (cell.Rect is not { } cr || cuts.Any(c => SameColumn(c.left, c.right, cr))) continue;
                var text = ColumnTextSpans(table, cr, inside);
                cuts.Add((cr.LLX, cr.URX, TextColumnBoundaries(text), text));
            }
            cuts.RemoveAll(c => c.xs.Count == 0);
            if (cuts.Count == 0) continue;

            var rows = new List<AbsorbedRow>(table.Rows.Count);
            for (var r = 0; r < table.Rows.Count; r++)
            {
                var cells = new List<AbsorbedCell>();
                foreach (var cell in table.Rows[r].Cells)
                {
                    var cut = cell.Rect is { } cr ? cuts.FindIndex(c => SameColumn(c.left, c.right, cr)) : -1;
                    if (cut < 0) cells.Add(cell);
                    else cells.AddRange(SplitCell(cell, r, cuts[cut].xs, cuts[cut].text));
                }
                rows.Add(new AbsorbedRow { Cells = cells });
            }
            tables[t] = new AbsorbedTable { Rows = rows, Rect = table.Rect, PageNum = table.PageNum };
        }
    }

    private static bool CentreInside(Rectangle r, Rectangle box)
    {
        var cx = (r.LLX + r.URX) / 2;
        var cy = (r.LLY + r.URY) / 2;
        return cx >= box.LLX && cx <= box.URX && cy >= box.LLY && cy <= box.URY;
    }

    private static bool SameColumn(double left, double right, Rectangle cell) =>
        Math.Abs(cell.LLX - left) <= SameColumnTolerance && Math.Abs(cell.URX - right) <= SameColumnTolerance;

    /// <summary>The text of one ruled column as spans per line: fragments of a line
    /// closer than a column gap are one span. A line is the fragments of one cell
    /// whose bottoms agree.</summary>
    private static List<TextSpan> ColumnTextSpans(AbsorbedTable table, Rectangle column, List<TextFragment> fragments)
    {
        var spans = new List<TextSpan>();
        var line = 0;
        for (var r = 0; r < table.Rows.Count; r++)
        {
            foreach (var cell in table.Rows[r].Cells)
            {
                if (cell.Rect is not { } cr || !SameColumn(column.LLX, column.URX, cr)) continue;
                var mine = fragments.Where(f => CentreInside(f.Rectangle!, cr))
                    .OrderByDescending(f => f.Rectangle!.LLY).ToList();
                foreach (var lineFragments in GroupByValue(mine, f => -f.Rectangle!.LLY, BorderlessLineTolerance))
                {
                    line++;
                    var first = true;
                    foreach (var fr in lineFragments.Select(f => f.Rectangle!).OrderBy(fr => fr.LLX))
                    {
                        var gap = TextColumnGapEm * (fr.URY - fr.LLY);
                        if (!first && fr.LLX - spans[^1].X2 < gap)
                            spans[^1] = spans[^1] with { X2 = Math.Max(spans[^1].X2, fr.URX) };
                        else
                            spans.Add(new TextSpan(fr.LLX, fr.URX, r, line, fr.URY - fr.LLY));
                        first = false;
                    }
                }
            }
        }
        return spans;
    }

    /// <summary>The x positions, left to right, where a strip of white separates the
    /// spans of enough lines in enough rows. Each is the middle of the strip those
    /// lines share.</summary>
    private static List<double> TextColumnBoundaries(List<TextSpan> spans)
    {
        // The white between consecutive spans of a line.
        var gaps = new List<TextSpan>();
        for (var i = 1; i < spans.Count; i++)
            if (spans[i].Line == spans[i - 1].Line)
                gaps.Add(new TextSpan(spans[i - 1].X2, spans[i].X1, spans[i].Row, spans[i].Line, spans[i].Height));

        var result = new List<double>();
        foreach (var order in Enumerable.Range(0, gaps.Count).OrderBy(i => gaps[i].X2))
        {
            // The gaps that reach across this gap's right end share a strip ending there.
            var x2 = gaps[order].X2;
            var members = Enumerable.Range(0, gaps.Count)
                .Where(i => gaps[i].X1 < x2 && gaps[i].X2 >= x2).ToList();
            var x1 = members.Max(i => gaps[i].X1);
            // A strip already cut (a ragged left edge seeds it once per line) is not cut again.
            if (result.Any(cut => cut > x1 && cut <= x2)) continue;
            // The strip the lines share must itself be as wide as a column gap.
            if (x2 - x1 < TextColumnGapEm * gaps[order].Height) continue;
            if (members.Count < MinTextColumnLines
                || members.Select(i => gaps[i].Row).Distinct().Count() < MinTextColumnRows)
                continue;
            var x = (x1 + x2) / 2;
            // More lines must be separated here than run across.
            if (spans.Count(s => s.X1 < x && s.X2 > x) >= members.Count) continue;
            result.Add(x);
        }
        result.Sort();
        return result;
    }

    /// <summary>The pieces of a cell cut at the boundaries that separate two pieces of
    /// its own text. A piece covering several text columns carries their count as its span.</summary>
    private static List<AbsorbedCell> SplitCell(AbsorbedCell cell, int row, List<double> xs, List<TextSpan> text)
    {
        var rect = cell.Rect!;
        var pieces = new List<AbsorbedCell>();
        var left = rect.LLX;
        var span = 1;
        foreach (var x in xs)
        {
            // A boundary cuts a cell only between two pieces of its own text: a
            // heading centred in its row, or a row with nothing set past the
            // boundary, has nothing there to separate and stays one cell.
            var pieceLeft = left;
            var separates = text.Any(s => s.Row == row && s.X1 >= pieceLeft && s.X2 <= x)
                && text.Any(s => s.Row == row && s.X1 >= x)
                && !text.Any(s => s.Row == row && s.X1 < x && s.X2 > x);
            if (!separates) { span++; continue; }
            pieces.Add(new AbsorbedCell { Rect = new Rectangle(left, rect.LLY, x, rect.URY), ColSpan = span });
            left = x;
            span = 1;
        }
        pieces.Add(new AbsorbedCell { Rect = new Rectangle(left, rect.LLY, rect.URX, rect.URY), ColSpan = span });
        return pieces;
    }
}
