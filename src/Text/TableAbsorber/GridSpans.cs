namespace Aspose.Pdf.Text;

public sealed partial class TableAbsorber
{
    // The cells drawn across several columns of the grid.

    /// <summary>A column rule missing from an unruled row's band marks a cell drawn
    /// across the columns when the rule runs through at least this share of the
    /// grid's height; a rule drawn only here and there (a header's separators, axis
    /// ticks) leaves the unruled rows it misses as they are.</summary>
    private const double SpanRuleShare = 0.5;

    /// <summary>A rule is drawn along a cell's side when it covers this share of it.</summary>
    private const double SideCoverShare = 0.5;

    /// <summary>The cells of one grid row as column ranges: neighbouring positions
    /// with no column rule through the row's band between them are one cell when
    /// the row draws column rules of its own, or when that rule runs through most
    /// of the grid elsewhere.</summary>
    private static List<(int cFrom, int cTo)> RowCellSpans(TableComponentBuildState bt, int r)
    {
        var spans = new List<(int cFrom, int cTo)>();
        foreach (var c in bt.colsInRow)
        {
            if (spans.Count > 0 && spans[^1].cTo == c - 1
                && !bt.consumed.Contains((r, c)) && !bt.consumed.Contains((r, c - 1))
                && SpansColumnRule(bt, bt.colBounds[c]))
                spans[^1] = (spans[^1].cFrom, c);
            else
                spans.Add((c, c));
        }
        return spans;
    }

    /// <summary>Whether the column rule at <paramref name="x"/> is absent from the
    /// current row band, which then joins the columns either side of it: a row that
    /// draws column rules of its own is divided by those alone (the rows of a form
    /// ruled under one set of columns, a header block ruled under another), and an
    /// unruled row only where the rule is drawn through most of the grid's height.</summary>
    private static bool SpansColumnRule(TableComponentBuildState bt, double x)
    {
        if (bt.vEdges is null) return false;
        var gridBot = bt.rowBounds[bt.rowIndices[0]];
        var gridTop = bt.rowBounds[bt.rowIndices[^1] + 1];
        double drawn = 0;
        foreach (var ve in bt.vEdges)
        {
            if (Math.Abs(ve.X - x) > EdgeTol) continue;
            if (Math.Min(ve.Y2, bt.yTop) - Math.Max(ve.Y1, bt.yBot) >= (bt.yTop - bt.yBot) * SideCoverShare) return false;
            drawn += Math.Max(0, Math.Min(ve.Y2, gridTop) - Math.Max(ve.Y1, gridBot));
        }
        return bt.rowRuled || drawn >= (gridTop - gridBot) * SpanRuleShare;
    }
}
