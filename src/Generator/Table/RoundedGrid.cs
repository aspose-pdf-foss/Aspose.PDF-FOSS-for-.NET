using Aspose.Pdf.Content;

namespace Aspose.Pdf;

public partial class Table
{
    /// <summary>Rounds the grid's corners (see <see cref="Aspose.Pdf.CornerRadii"/>). The
    /// table's <see cref="BackgroundColor"/> is painted once per page, inside the rounded
    /// outline of the grid's box on that page, under the rows. A grid whose rules stand
    /// inside its column widths (<see cref="RulesInsideColumnWidth"/>) boxes it with its
    /// own <see cref="Border"/> and paints that border inside the ring the outline leaves;
    /// a collapsed grid's rules stay straight. Each part of a grid cut by a page break is
    /// rounded at all four corners. Null (the default) leaves the corners square.</summary>
    public CornerRadii? CornerRadii { get; set; }

    /// <summary>Where the grid's box starts and ends down the page for the slices that
    /// landed on it: the gaps above the first row and below the last included.</summary>
    private (double Top, double Bottom) GridBoxSpan(List<RowSlice> slices) =>
        (slices[0].TopY + CellSpacingV, slices[^1].TopY - slices[^1].Height - CellSpacingV);

    /// <summary>The box a separated grid's own border frames on this page: its columns,
    /// their gaps and the border itself.</summary>
    private (double X, double Y, double W, double H) SeparatedFrameBox(List<RowSlice> slices,
        double[] colWidths, double tableX)
    {
        var outerWidth = OuterBorderWidth();
        var totalWidth = LastColBoxOverhang + GridBoxWidth(colWidths);
        var (top, bottom) = GridBoxSpan(slices);
        return (tableX - outerWidth, bottom - outerWidth, totalWidth + 2 * outerWidth, top - bottom + 2 * outerWidth);
    }

    /// <summary>The grid's background on this page, inside its rounded outline: over the
    /// frame's box when the rules stand inside the column widths, over the grid's box
    /// when they are collapsed.</summary>
    private void PaintRoundedGridBackground(ContentStreamBuilder builder, List<RowSlice> slices,
        double[] colWidths, double tableX)
    {
        var box = RulesInsideColumnWidth
            ? SeparatedFrameBox(slices, colWidths, tableX)
            : GridBackgroundBox(slices, colWidths, tableX);
        RulePainter.PaintRoundedBox(builder, null, box.X, box.Y, box.W, box.H,
            CornerRadii!.Resolve(box.W, box.H), BackgroundColor, box, null);
    }

    /// <summary>The box a collapsed grid covers on this page, out to the outer edges of
    /// its outer rules: its columns and half the widest rule on each side across, and
    /// down from the first slice's top (already that edge) to half the closing rule
    /// below the line the last slice stands above.</summary>
    private (double X, double Y, double W, double H) GridBackgroundBox(List<RowSlice> slices,
        double[] colWidths, double tableX)
    {
        var (top, bottom) = GridBoxSpan(slices);
        if (CollapsedRulesFor(colWidths.Length) is { } rules)
            bottom -= rules.TopEdge(slices[^1].RowIndex + 1);
        var width = GridBoxWidth(colWidths) + CollapsedOuterWidth(colWidths.Length);
        return (tableX, bottom, width, top - bottom);
    }
}
