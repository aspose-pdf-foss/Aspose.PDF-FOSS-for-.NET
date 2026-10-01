using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public partial class Table
{
    /// <summary>Sizes the columns from what they HOLD -- an automatic table layout.
    /// Each column has a narrowest box (its widest word) and a widest one (its
    /// longest line on one line), both with the cell's padding, its share of the
    /// grid's rules and the measure guard. A column is DECLARED when
    /// <see cref="ColumnWidths"/> names it at least as wide as that narrowest box,
    /// and AUTO otherwise -- including a declaration too narrow for its own content.
    ///
    /// Left alone the grid is as wide as its columns want: every auto column its
    /// widest box, every declared one its declaration. When that overflows the band,
    /// auto columns give back what they can above their narrowest box, in
    /// proportion to how much they have to give.
    ///
    /// A cell spanning several columns asks for its boxes over all of them: what
    /// it needs beyond what they already ask for is split equally between them.
    /// A column declared as a PERCENT is a share of the grid, not a width; see
    /// <see cref="PercentSizedColumnWidths"/> for how a grid of shares is laid.</summary>
    public bool SizesColumnsToContent { get; set; }

    /// <summary>Stretches a content-sized grid (<see cref="SizesColumnsToContent"/>)
    /// across the whole band. The room it gains goes to the auto columns in
    /// proportion to their widest box, and only when there are none to the
    /// declared ones -- in proportion to how far each declaration stands above its
    /// own narrowest box, which is also how a declared grid wider than the band
    /// gives the difference back.</summary>
    public bool StretchesToBand { get; set; }

    /// <summary>One column's content boxes: the narrowest it can be without
    /// breaking a word, and the widest its content asks for.</summary>
    internal readonly struct ContentBoxes
    {
        public readonly double Min;
        public readonly double Max;
        public ContentBoxes(double min, double max) { Min = min; Max = max; }
    }

    /// <summary>The column pitches of a content-sized grid, laid in
    /// <paramref name="availableWidth"/>.</summary>
    private double[] ContentSizedColumnWidths(double availableWidth)
    {
        var declared = DeclaredPointWidths();
        var boxes = MeasureContentBoxes(Math.Max(declared.Length, ContentColumnCount()), out var spans);
        var (pitchL, pitchR) = CellBorderPitch();
        // The band holds the grid's box, which stands half a rule out beyond the
        // outermost rule on each side. A declared box is laid in instead of the
        // band, and filled the way a stretched grid fills the band.
        var fills = StretchesToBand || BoxWidth > 0;
        // …or, with the rules inside the column widths, the grid's own border and
        // its cell gaps come off the box and the columns share the rest.
        var band = (BoxWidth > 0 ? BoxWidth : availableWidth)
            - (RulesInsideColumnWidth ? SeparatedGridChrome(boxes.Length) : pitchL + pitchR);
        return SizeColumnsToContent(boxes, spans, declared, DeclaredPercentWidths(), band, fills, FixedLayout);
    }

    /// <summary>The column widths of a content-sized grid, from its columns' content
    /// boxes (the narrowest and widest each asks for, padding, rules and measure guard
    /// included) and the boxes of the cells spanning several: declared point widths
    /// and percent shares, the band the columns share, whether the grid fills it and
    /// whether it lays its columns by their declarations alone. Any layout that
    /// measures its cells its own way sizes its columns with this.</summary>
    internal static double[] SizeColumnsToContent(ContentBoxes[] boxes, List<SpanBoxes> spans, double?[] declared,
        double?[] percents, double band, bool fills, bool fixedLayout)
    {
        if (fixedLayout && fills && band > 0)
            return FixedLayoutColumnWidths(band, declared, percents, boxes.Length);
        if (band > 0 && Array.Exists(percents, p => p is { } share && share > 0))
            return PercentSizedColumnWidths(band, declared, percents, boxes, spans, fills);
        var widths = new double[boxes.Length];
        var isDeclared = new bool[boxes.Length];
        for (var i = 0; i < boxes.Length; i++)
        {
            isDeclared[i] = i < declared.Length && declared[i] is { } d && d >= boxes[i].Min;
            widths[i] = isDeclared[i] ? declared[i]!.Value : boxes[i].Max;
        }
        if (band <= 0) return widths;
        double total = 0;
        foreach (var w in widths) total += w;
        if (fills || total > band + 1e-6)
            ShareBandAcrossColumns(widths, isDeclared, boxes, band);
        return widths;
    }

    /// <summary>Brings the columns to exactly <paramref name="band"/>: auto columns
    /// take a surplus in proportion to their widest box and give a shortfall back
    /// down to their narrowest; with no auto column the declared ones move in
    /// proportion to their room above their narrowest box.</summary>
    private static void ShareBandAcrossColumns(double[] widths, bool[] isDeclared, ContentBoxes[] boxes, double band)
    {
        double declaredTotal = 0, autoMax = 0, autoMin = 0, flex = 0;
        var autos = 0;
        for (var i = 0; i < widths.Length; i++)
        {
            if (isDeclared[i])
            {
                declaredTotal += widths[i];
                flex += widths[i] - boxes[i].Min;
                continue;
            }
            autos++;
            autoMax += boxes[i].Max;
            autoMin += boxes[i].Min;
        }
        if (autos == 0)
        {
            if (flex <= 0) return;
            var change = band - declaredTotal;
            for (var i = 0; i < widths.Length; i++)
                widths[i] += change * (widths[i] - boxes[i].Min) / flex;
            return;
        }
        var room = band - declaredTotal;
        if (room >= autoMax)
        {
            if (autoMax <= 0) return;
            for (var i = 0; i < widths.Length; i++)
                if (!isDeclared[i]) widths[i] = room * boxes[i].Max / autoMax;
            return;
        }
        // Too narrow for every auto column's widest box: each keeps its narrowest
        // and shares what is left in proportion to what it wanted beyond it.
        var give = autoMax - autoMin;
        var share = give > 0 ? Math.Max(0, room - autoMin) / give : 0;
        for (var i = 0; i < widths.Length; i++)
            if (!isDeclared[i]) widths[i] = boxes[i].Min + (boxes[i].Max - boxes[i].Min) * share;
    }

    /// <summary>Every column's content boxes, and the boxes of the cells that span
    /// several. A spanning cell asks for its boxes over all its columns: what it
    /// needs beyond their own sum is split equally between them (a column that
    /// declares a width keeps it regardless). A column nothing sizes is as narrow
    /// as its padding and rules.</summary>
    /// <summary>The widest picture among a cell's paragraphs, its own left and
    /// right margins included: its fixed width, else its own pixel width; zero
    /// when it holds none.</summary>
    private static double WidestPicture(Cell cell)
    {
        double widest = 0;
        foreach (var paragraph in cell.Paragraphs)
            if (paragraph is Image image)
                widest = Math.Max(widest, (image.FixWidth > 0 ? image.FixWidth : image.BitmapSize.Width)
                    + (image.Margin?.Left ?? 0) + (image.Margin?.Right ?? 0));
            // A reserved block is as wide as its caller says it can be at most.
            else if (paragraph is ReservedBlock { MeasureWidths: { } measure })
                widest = Math.Max(widest, measure().Max);
        return widest;
    }

    private ContentBoxes[] MeasureContentBoxes(int columns, out List<SpanBoxes> spans)
    {
        var min = new double[columns];
        var max = new double[columns];
        spans = new List<SpanBoxes>();
        var (pitchL, pitchR) = CellBorderPitch();
        // The columns are sized by what the grid's FIRST page lays: a header band
        // held back from that page (see RepeatingRowsSkipFirstPage) sizes nothing,
        // and wraps in the columns it finds when it appears (probed: a 60 pt column
        // keeps 60 under a skipped "No (continued)" heading and wraps it overleaf,
        // where the same heading laid on page one widens the column to 83.21).
        var firstRow = RepeatingRowsSkipFirstPage ? Math.Max(0, Math.Min(RepeatingRowsCount, Rows.Count)) : 0;
        for (var ri = firstRow; ri < Rows.Count; ri++)
        {
            var row = Rows.At(ri);
            var col = 0;
            for (var ci = 0; ci < row.Cells.Count && col < columns; ci++)
            {
                var cell = row.Cells[ci];
                var span = Math.Max(1, Math.Min(cell.ColSpan, columns - col));
                var pad = cell.Margin ?? row.DefaultCellPadding ?? DefaultCellPadding;
                // A cell whose rules stand inside its column's width is boxed by its
                // OWN two rules, not the grid's default pitch (probed: a 3 pt-ruled
                // cell beside 0.5 pt ones measures text + padding + 6).
                var (ruleL, ruleR) = RulesInsideColumnWidth ? CellRuleWidths(cell, row) : (pitchL, pitchR);
                var chrome = (pad?.Left ?? 0) + (pad?.Right ?? 0) + ruleL + ruleR + AutoFitMeasureGuardPt;
                // A picture does not wrap: it floors the narrowest box as much as the widest.
                var picture = WidestPicture(cell);
                var narrowest = Math.Max(MaxWordWidth(cell, row, exact: true), picture) + chrome;
                var widest = Math.Max(MaxLineWidth(cell, row, exact: true), picture) + chrome;
                if (span == 1)
                {
                    min[col] = Math.Max(min[col], narrowest);
                    max[col] = Math.Max(max[col], widest);
                }
                else
                {
                    // A span's box carries the measure guard once per column it
                    // covers (probed: a two-column span is 0.01 wider than its
                    // text and chrome, a three-column one 0.02).
                    var guards = AutoFitMeasureGuardPt * (span - 1);
                    spans.Add(new SpanBoxes(col, span, narrowest + guards, widest + guards));
                }
                col += span;
            }
        }
        return ColumnContentBoxes(min, max, spans, EmptyColumnBox());
    }

    /// <summary>The columns' content boxes from their single-column cells' narrowest
    /// and widest boxes: each spanning cell raises its columns equally to what it asks
    /// beyond their sum, and no column is narrower than <paramref name="floor"/>, the box
    /// of a column holding nothing.</summary>
    internal static ContentBoxes[] ColumnContentBoxes(double[] min, double[] max, List<SpanBoxes> spans, double floor)
    {
        foreach (var span in spans)
        {
            ShareSpanBox(min, span.Column, span.Span, span.Min);
            ShareSpanBox(max, span.Column, span.Span, span.Max);
        }
        var boxes = new ContentBoxes[min.Length];
        for (var i = 0; i < min.Length; i++)
            boxes[i] = new ContentBoxes(Math.Max(min[i], floor), Math.Max(max[i], floor));
        return boxes;
    }

    /// <summary>Raises the boxes of a span's columns, equally, to what the span
    /// asks for beyond their sum.</summary>
    private static void ShareSpanBox(double[] boxes, int column, int span, double asked)
    {
        double have = 0;
        for (var k = column; k < column + span; k++) have += boxes[k];
        if (asked <= have) return;
        var each = (asked - have) / span;
        for (var k = column; k < column + span; k++) boxes[k] += each;
    }

    /// <summary>The box a column holding nothing still has: its padding, its
    /// rules and the measure guard.</summary>
    private double EmptyColumnBox()
    {
        var (pitchL, pitchR) = CellBorderPitch();
        var pad = DefaultCellPadding;
        return (pad?.Left ?? 0) + (pad?.Right ?? 0) + pitchL + pitchR + AutoFitMeasureGuardPt;
    }

    /// <summary>How many columns the rows fill, spans included.</summary>
    private int ContentColumnCount()
    {
        var columns = 1;
        for (var ri = 0; ri < Rows.Count; ri++)
        {
            var row = Rows.At(ri);
            var filled = 0;
            for (var ci = 0; ci < row.Cells.Count; ci++) filled += Math.Max(1, row.Cells[ci].ColSpan);
            if (filled > columns) columns = filled;
        }
        return columns;
    }

    /// <summary>The widths <see cref="ColumnWidths"/> names in points, one per
    /// column; null where a column names none or names a percentage, which a
    /// content-sized grid treats as undeclared.</summary>
    private double?[] DeclaredPointWidths()
    {
        if (string.IsNullOrWhiteSpace(ColumnWidths)) return Array.Empty<double?>();
        var tokens = ColumnWidths!.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var widths = new double?[tokens.Length];
        for (var i = 0; i < tokens.Length; i++)
            widths[i] = tokens[i].EndsWith("%", StringComparison.Ordinal) ? null : TryParseWidthToken(tokens[i]);
        return widths;
    }
}
