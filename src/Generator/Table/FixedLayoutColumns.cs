namespace Aspose.Pdf;

public partial class Table
{
    /// <summary>Lays a content-sized grid (<see cref="SizesColumnsToContent"/>) by its
    /// DECLARATIONS alone once it has a width to fill -- a fixed table layout, the
    /// CSS <c>table-layout: fixed</c> model -- and leaves it to the content-sized
    /// rules until then.
    ///
    /// With a width to fill (<see cref="StretchesToBand"/>, or a <see cref="BoxWidth"/>)
    /// the span between the outermost rule centres is shared like this: a percent
    /// column takes its share of that span; when any column declares nothing the
    /// declared ones keep their declarations and the undeclared share what is left
    /// EQUALLY; otherwise every declaration scales in proportion to fill the span --
    /// or, when together they already overrun it, stays as declared and the grid
    /// overruns its box. What a column holds never widens it: a word too long for
    /// its column breaks. Without a width to fill nothing here applies.</summary>
    public bool FixedLayout { get; set; }

    /// <summary>The width of the grid's own box when the caller declared one, in
    /// points: the grid is laid in that box instead of the band it stands in, and
    /// fills it the way <see cref="StretchesToBand"/> fills the band. Zero when the
    /// caller declared none.</summary>
    public double BoxWidth { get; set; }

    /// <summary>The column pitches of a fixed-layout grid over the
    /// <paramref name="span"/> between its outermost rule centres.</summary>
    private static double[] FixedLayoutColumnWidths(double span, double?[] declared, double?[] percents, int columns)
    {
        var widths = new double?[columns];
        double declaredTotal = 0;
        var undeclared = 0;
        for (var i = 0; i < columns; i++)
        {
            widths[i] = i < percents.Length && percents[i] is { } share ? share * span
                : i < declared.Length ? declared[i]
                : null;
            if (widths[i] is { } w) declaredTotal += w;
            else undeclared++;
        }
        var result = new double[columns];
        if (undeclared > 0)
        {
            var each = Math.Max(0, span - declaredTotal) / undeclared;
            for (var i = 0; i < columns; i++) result[i] = widths[i] ?? each;
            return result;
        }
        var scale = declaredTotal > 0 && declaredTotal <= span ? span / declaredTotal : 1;
        for (var i = 0; i < columns; i++) result[i] = widths[i]!.Value * scale;
        return result;
    }
}
