namespace Aspose.Pdf.Drawing;

/// <summary>How a gradient continues past the ends of its colour stops.</summary>
public enum GradientSpread
{
    /// <summary>It does not: nothing is painted outside the first and last stops.</summary>
    None,

    /// <summary>The end colours carry on to the edges of the area.</summary>
    Pad,

    /// <summary>The stops repeat, every other repetition mirrored.</summary>
    Reflect,

    /// <summary>The stops repeat, each repetition starting afresh.</summary>
    Repeat,
}

/// <summary>How a colour stop's position is given.</summary>
public enum GradientOffsetKind
{
    /// <summary>Not given: the first stop is at the start, the last at the end, and the
    /// ones between are spread evenly between their neighbours.</summary>
    Auto,

    /// <summary>A fraction of the gradient vector.</summary>
    Relative,

    /// <summary>A distance along the gradient vector.</summary>
    Absolute,
}

/// <summary>How the point where a stop's colour is half-way to the next one is given.</summary>
public enum GradientHintKind
{
    /// <summary>There is none: the colour changes evenly.</summary>
    None,

    /// <summary>A fraction of the way from this stop to the next.</summary>
    RelativeBetweenStops,

    /// <summary>A fraction of the gradient vector.</summary>
    RelativeOnGradient,

    /// <summary>A distance along the gradient vector.</summary>
    AbsoluteOnGradient,
}

/// <summary>One colour stop as given: its colour, its position and its hint.</summary>
public sealed class GradientStopSpec
{
    public GradientStopSpec(double[] color, double offset, GradientOffsetKind offsetKind, double hint,
        GradientHintKind hintKind)
    {
        Color = color;
        Offset = offset;
        OffsetKind = offsetKind;
        Hint = hint;
        HintKind = hintKind;
    }

    public double[] Color { get; }
    public double Offset { get; }
    public GradientOffsetKind OffsetKind { get; }
    public double Hint { get; }
    public GradientHintKind HintKind { get; }
}

/// <summary>One piece of a laid-out gradient: from one colour to another over [Start, End]
/// of the vector, along t to the power <see cref="Exponent"/>. A piece whose two ends
/// coincide is a jump from one colour to the next.</summary>
public sealed class LinearGradientSegment
{
    public LinearGradientSegment(double start, double end, double[] from, double[] to, double exponent)
    {
        Start = start;
        End = end;
        From = from;
        To = to;
        Exponent = exponent;
    }

    public double Start { get; }
    public double End { get; }
    public double[] From { get; }
    public double[] To { get; }
    public double Exponent { get; }
}

/// <summary>
/// A linear gradient laid out as an axial shading, with CSS colour-stop rules: the
/// axis to paint along (<see cref="Coords"/>), the part of the vector it covers
/// (<see cref="Domain"/>, 0 at the vector's start, 1 at its end) and the colour
/// pieces over it (<see cref="Segments"/>).
///
/// Stops are normalized first. A distance becomes a fraction of the vector's
/// length; a first stop without a position is at 0 and a last one at 1, and
/// positionless stops between are spread evenly between their neighbours; no
/// stop (or hint) may come before an earlier one, so each is moved up to the
/// furthest position before it. A hint puts the half-way colour at that point of
/// its piece by the power ln 0.5 / ln h; a hint at the very start or end of its
/// piece makes the piece one flat colour, the next or the current.
///
/// The area to cover decides how far the gradient reaches: the corners of the box
/// projected on the vector. Without a spread it is clipped to the stops (and there
/// is nothing to paint when the stops do not meet the area); padding adds flat end
/// pieces out to the area; repeating and reflecting lay whole copies of the stops
/// over it, a repeat jumping back to the first colour at each boundary. A gradient
/// of one colour (one stop, or a vector of no length) is that colour across the
/// bottom edge of the area, unless there is no spread, when there is nothing.
/// </summary>
public sealed class LinearGradientLayout
{
    /// <summary>A length or fraction below which two positions are the same.</summary>
    private const double ZeroEpsilon = 1E-10;

    private LinearGradientLayout(double[] coords, double[] domain, IReadOnlyList<LinearGradientSegment> segments)
    {
        Coords = coords;
        Domain = domain;
        Segments = segments;
    }

    /// <summary>The axis: x0 y0 x1 y1.</summary>
    public double[] Coords { get; }

    /// <summary>Where the axis starts and ends, as fractions of the gradient vector.</summary>
    public double[] Domain { get; }

    public IReadOnlyList<LinearGradientSegment> Segments { get; }

    /// <summary>From the start of the first piece to the end of the last.</summary>
    public double[] FunctionDomain => [Segments[0].Start, Segments[^1].End];

    /// <summary>
    /// Lays the gradient out, or answers null where there is nothing to paint.
    /// <paramref name="cover"/> is the area as left, bottom, right, top; null covers
    /// just the vector.
    /// </summary>
    public static LinearGradientLayout? Compute(double x0, double y0, double x1, double y1,
        IReadOnlyList<GradientStopSpec> stops, GradientSpread spread, double[]? cover)
    {
        if (stops.Count == 0) return null;
        var length = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
        var normal = Normalize(stops, length);

        if (length < ZeroEpsilon || normal.Count == 1)
        {
            if (spread == GradientSpread.None || cover is null) return null;
            var color = normal[^1].Color;
            return new LinearGradientLayout([cover[0], cover[1], cover[2], cover[1]], [0, 1],
                [new LinearGradientSegment(0, 1, color, color, 1)]);
        }

        var domain = Covering(x0, y0, x1, y1, length, cover);
        List<LinearGradientSegment> segments;
        switch (spread)
        {
            case GradientSpread.Repeat:
            case GradientSpread.Reflect:
                segments = Tiled(normal, domain, spread == GradientSpread.Reflect);
                break;
            case GradientSpread.Pad:
                segments = Pieces(normal);
                if (domain[0] < normal[0].Offset)
                    segments.Insert(0, new LinearGradientSegment(domain[0], normal[0].Offset, normal[0].Color, normal[0].Color, 1));
                if (domain[1] > normal[^1].Offset)
                    segments.Add(new LinearGradientSegment(normal[^1].Offset, domain[1], normal[^1].Color, normal[^1].Color, 1));
                break;
            default:
                var first = normal[0].Offset;
                var last = normal[^1].Offset;
                if (last - first < ZeroEpsilon || domain[1] <= first || domain[0] >= last) return null;
                domain = [Math.Max(domain[0], first), Math.Min(domain[1], last)];
                segments = Pieces(normal);
                break;
        }

        return new LinearGradientLayout(CoordinatesForDomain(domain, x0, y0, x1, y1), domain, segments);
    }

    /// <summary>A stop with its position settled, and the power of the piece it starts.</summary>
    private sealed record Stop(double[] Color, double Offset, double Exponent);

    /// <summary>The stops with every position a fraction of the vector, rising, and every
    /// hint turned into the power of the piece it belongs to.</summary>
    private static List<Stop> Normalize(IReadOnlyList<GradientStopSpec> stops, double length)
    {
        if (length < ZeroEpsilon) return [new Stop(stops[^1].Color, 0, 1)];

        var offsets = new double[stops.Count];
        var hints = new double[stops.Count];
        for (var i = 0; i < stops.Count; i++)
        {
            offsets[i] = stops[i].OffsetKind switch
            {
                GradientOffsetKind.Absolute => stops[i].Offset / length,
                GradientOffsetKind.Relative => stops[i].Offset,
                _ => double.NaN,
            };
            hints[i] = stops[i].HintKind switch
            {
                GradientHintKind.AbsoluteOnGradient => stops[i].Hint / length,
                GradientHintKind.RelativeOnGradient => stops[i].Hint,
                _ => double.NaN,
            };
        }
        if (double.IsNaN(offsets[0])) offsets[0] = 0;
        if (double.IsNaN(offsets[^1])) offsets[^1] = 1;

        // No stop or positioned hint comes before an earlier one.
        var furthest = offsets[0];
        for (var i = 0; i < stops.Count; i++)
        {
            if (!double.IsNaN(offsets[i])) furthest = offsets[i] = Math.Max(offsets[i], furthest);
            if (!double.IsNaN(hints[i])) furthest = hints[i] = Math.Max(hints[i], furthest);
        }

        // Stops without a position are spread evenly between their neighbours.
        for (var i = 1; i < stops.Count; i++)
        {
            if (!double.IsNaN(offsets[i])) continue;
            var next = i;
            while (double.IsNaN(offsets[next])) next++;
            var step = (offsets[next] - offsets[i - 1]) / (next - i + 1);
            for (var k = i; k < next; k++) offsets[k] = offsets[k - 1] + step;
        }

        var result = new List<Stop>(stops.Count);
        for (var i = 0; i < stops.Count; i++)
        {
            var exponent = 1.0;
            if (i + 1 < stops.Count)
            {
                var between = stops[i].HintKind switch
                {
                    GradientHintKind.None => double.NaN,
                    GradientHintKind.RelativeBetweenStops => stops[i].Hint,
                    _ => offsets[i + 1] - offsets[i] < ZeroEpsilon ? 1 : (hints[i] - offsets[i]) / (offsets[i + 1] - offsets[i]),
                };
                exponent = double.IsNaN(between) ? 1
                    : between <= ZeroEpsilon ? 0
                    : between >= 1 - ZeroEpsilon ? double.PositiveInfinity
                    : Math.Log(0.5) / Math.Log(between);
            }
            result.Add(new Stop(stops[i].Color, offsets[i], exponent));
        }
        return result;
    }

    /// <summary>One piece per pair of neighbouring stops. A power of 0 is the next colour
    /// throughout, an infinite one the current colour throughout.</summary>
    private static List<LinearGradientSegment> Pieces(List<Stop> stops)
    {
        var pieces = new List<LinearGradientSegment>(stops.Count - 1);
        for (var i = 0; i + 1 < stops.Count; i++) pieces.Add(Piece(stops[i], stops[i + 1], stops[i].Offset, stops[i + 1].Offset));
        return pieces;
    }

    private static LinearGradientSegment Piece(Stop from, Stop to, double start, double end) => from.Exponent switch
    {
        0 => new LinearGradientSegment(start, end, to.Color, to.Color, 1),
        double.PositiveInfinity => new LinearGradientSegment(start, end, from.Color, from.Color, 1),
        _ => new LinearGradientSegment(start, end, from.Color, to.Color, from.Exponent),
    };

    /// <summary>Whole copies of the stops laid over the covered part of the vector.</summary>
    private static List<LinearGradientSegment> Tiled(List<Stop> stops, double[] domain, bool reflect)
    {
        var first = stops[0].Offset;
        var period = stops[^1].Offset - first;
        var from = (int)Math.Floor((domain[0] - first) / period);
        var to = (int)Math.Ceiling((domain[1] - first) / period);
        var pieces = new List<LinearGradientSegment>();
        var basic = Pieces(stops);
        for (var k = from; k < to; k++)
        {
            var shift = k * period;
            var mirrored = reflect && Math.Abs(k) % 2 == 1;
            if (!reflect && k > from)
                pieces.Add(new LinearGradientSegment(first + shift, first + shift, stops[^1].Color, stops[0].Color, 1));
            if (!mirrored)
            {
                foreach (var piece in basic)
                    pieces.Add(new LinearGradientSegment(piece.Start + shift, piece.End + shift, piece.From, piece.To, piece.Exponent));
                continue;
            }
            for (var i = basic.Count - 1; i >= 0; i--)
            {
                var piece = basic[i];
                var end = stops[^1].Offset;
                pieces.Add(new LinearGradientSegment(first + end - piece.End + shift, first + end - piece.Start + shift,
                    piece.To, piece.From, piece.Exponent));
            }
        }
        return pieces;
    }

    /// <summary>The part of the vector (x0, y0)-(x1, y1) the area left, bottom, right, top
    /// projects onto, as fractions of it: [0 1] for no area.</summary>
    public static double[] CoveringDomain(double x0, double y0, double x1, double y1, double[]? cover) =>
        Covering(x0, y0, x1, y1, Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)), cover);

    /// <summary>The axis that runs over <paramref name="domain"/> of the vector: its start
    /// moved to domain[0] and its end to domain[1].</summary>
    public static double[] CoordinatesForDomain(double[] domain, double x0, double y0, double x1, double y1) =>
        [x0 + (x1 - x0) * domain[0], y0 + (y1 - y0) * domain[0], x1 + (x1 - x0) * (domain[1] - 1), y1 + (y1 - y0) * (domain[1] - 1)];

    /// <summary>The corners of the area projected on the vector, as fractions of it: the
    /// least and the greatest.</summary>
    private static double[] Covering(double x0, double y0, double x1, double y1, double length, double[]? cover)
    {
        if (cover is null) return [0, 1];
        var ux = (x1 - x0) / length;
        var uy = (y1 - y0) / length;
        double min = double.MaxValue, max = double.MinValue;
        foreach (var (x, y) in new[] { (cover[0], cover[1]), (cover[2], cover[1]), (cover[2], cover[3]), (cover[0], cover[3]) })
        {
            var along = ((x - x0) * ux + (y - y0) * uy) / length;
            min = Math.Min(min, along);
            max = Math.Max(max, along);
        }
        // Adding zero turns a negative zero into zero: an edge exactly at the start is at 0.
        return [min + 0.0, max + 0.0];
    }
}
