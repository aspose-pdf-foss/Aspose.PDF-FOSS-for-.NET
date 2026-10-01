using System;

namespace Aspose.Pdf.Converters.PsToPdf;

/// <summary>
/// The arc operators. An arc becomes cubic Beziers rather than a polyline, matching
/// what the reference converter emits, and each quadrant-or-less piece uses the
/// standard circular-arc control-point distance so the curve is accurate to well
/// under a device pixel.
/// </summary>
internal static class PsArcs
{
    /// <summary>The widest sweep a single Bezier piece may cover, in degrees.</summary>
    private const double MaxSweepPerSegment = 90.0;

    /// <summary>Degrees in a half turn.</summary>
    private const double DegreesPerHalfTurn = 180.0;

    /// <summary>Degrees in a full turn.</summary>
    private const double DegreesPerTurn = 360.0;

    /// <summary>Below this radius an arc has no extent worth drawing.</summary>
    private const double MinRadius = 1e-9;

    /// <summary><c>arc</c> and <c>arcn</c>: append an arc of the given circle,
    /// preceded by a line from the current point when there is one.</summary>
    public static void Arc(PsInterpreter i, bool counterClockwise)
    {
        var endAngle = i.PopNumber();
        var startAngle = i.PopNumber();
        var radius = i.PopNumber();
        var centreY = i.PopNumber();
        var centreX = i.PopNumber();
        var sweep = NormaliseSweep(startAngle, endAngle, counterClockwise);
        AppendArc(i.Graphics, centreX, centreY, radius, startAngle, sweep);
    }

    /// <summary>The signed sweep from start to end in the requested direction, always
    /// between 0 and one full turn in magnitude.</summary>
    private static double NormaliseSweep(double start, double end, bool counterClockwise)
    {
        var delta = end - start;
        if (counterClockwise)
        {
            while (delta < 0) delta += DegreesPerTurn;
            return delta;
        }

        while (delta > 0) delta -= DegreesPerTurn;
        return delta;
    }

    /// <summary>Append an arc, joining it to the current point with a straight line
    /// the way PostScript specifies.</summary>
    private static void AppendArc(PsGraphics g, double cx, double cy, double radius,
        double startAngle, double sweep)
    {
        if (radius < MinRadius)
        {
            LineOrMove(g, cx, cy);
            return;
        }

        var start = Point(cx, cy, radius, startAngle);
        LineOrMove(g, start.X, start.Y);
        // The pieces are FULL quarter turns taken from the START angle, with whatever
        // is left over last, rather than equal shares of the sweep. Both draw the same
        // circle to well under a device pixel; the difference is where the control
        // points fall, and a path's bounding box is read off those.
        var remaining = sweep;
        var at = startAngle;
        while (Math.Abs(remaining) > SweepEpsilon)
        {
            var step = Math.Abs(remaining) > MaxSweepPerSegment
                ? Math.Sign(remaining) * MaxSweepPerSegment
                : remaining;
            AppendArcPiece(g, cx, cy, radius, at, step);
            at += step;
            remaining -= step;
        }
    }

    /// <summary>A sweep smaller than this has nothing left to draw.</summary>
    private const double SweepEpsilon = 1e-9;

    /// <summary>One Bezier covering at most a quarter turn.</summary>
    private static void AppendArcPiece(PsGraphics g, double cx, double cy, double radius,
        double fromAngle, double sweep)
    {
        var a0 = ToRadians(fromAngle);
        var a1 = ToRadians(fromAngle + sweep);
        var half = (a1 - a0) / 2;
        // The control-point distance that makes a cubic match a circular arc.
        var k = 4.0 / 3.0 * Math.Tan(half / 2) * radius;
        var x0 = cx + radius * Math.Cos(a0);
        var y0 = cy + radius * Math.Sin(a0);
        var x3 = cx + radius * Math.Cos(a1);
        var y3 = cy + radius * Math.Sin(a1);
        g.CurveTo(x0 - k * Math.Sin(a0), y0 + k * Math.Cos(a0),
            x3 + k * Math.Sin(a1), y3 - k * Math.Cos(a1), x3, y3);
    }

    /// <summary>Join to a point with a line when a path is open, else start there.</summary>
    private static void LineOrMove(PsGraphics g, double x, double y)
    {
        if (g.Path.HasCurrentPoint) g.LineTo(x, y);
        else g.MoveTo(x, y);
    }

    private static (double X, double Y) Point(double cx, double cy, double radius, double angle)
    {
        var a = ToRadians(angle);
        return (cx + radius * Math.Cos(a), cy + radius * Math.Sin(a));
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / DegreesPerHalfTurn;

    /// <summary><c>arct</c> and <c>arcto</c>: the arc of the given radius tangent to
    /// the two lines from the current point through the first point to the second.
    /// <c>arcto</c> additionally reports the two tangent points.</summary>
    public static void ArcTo(PsInterpreter i, bool leaveTangents)
    {
        var radius = i.PopNumber();
        var y2 = i.PopNumber();
        var x2 = i.PopNumber();
        var y1 = i.PopNumber();
        var x1 = i.PopNumber();
        if (!i.Graphics.TryGetCurrentPoint(out var x0, out var y0))
            throw new PsErrorSignal("nocurrentpoint");
        var tangents = Tangents(x0, y0, x1, y1, x2, y2, radius);
        AppendArc(i.Graphics, tangents.Cx, tangents.Cy, Math.Abs(radius),
            tangents.StartAngle, tangents.Sweep);
        if (!leaveTangents) return;
        i.Push(tangents.T1X);
        i.Push(tangents.T1Y);
        i.Push(tangents.T2X);
        i.Push(tangents.T2Y);
    }

    private readonly struct TangentArc
    {
        public TangentArc(double cx, double cy, double startAngle, double sweep,
            double t1X, double t1Y, double t2X, double t2Y)
        {
            Cx = cx;
            Cy = cy;
            StartAngle = startAngle;
            Sweep = sweep;
            T1X = t1X;
            T1Y = t1Y;
            T2X = t2X;
            T2Y = t2Y;
        }

        public double Cx { get; }

        public double Cy { get; }

        public double StartAngle { get; }

        public double Sweep { get; }

        public double T1X { get; }

        public double T1Y { get; }

        public double T2X { get; }

        public double T2Y { get; }
    }

    /// <summary>Where the inscribed circle of the given radius touches the two legs,
    /// and the arc between those touch points.</summary>
    private static TangentArc Tangents(double x0, double y0, double x1, double y1,
        double x2, double y2, double radius)
    {
        var (u0X, u0Y) = Unit(x0 - x1, y0 - y1);
        var (u1X, u1Y) = Unit(x2 - x1, y2 - y1);
        var cosine = u0X * u1X + u0Y * u1Y;
        cosine = Math.Max(-1, Math.Min(1, cosine));
        var halfAngle = Math.Acos(cosine) / 2;
        var tangentLength = Math.Abs(halfAngle) < MinRadius
            ? 0
            : Math.Abs(radius) / Math.Tan(halfAngle);
        var t1X = x1 + u0X * tangentLength;
        var t1Y = y1 + u0Y * tangentLength;
        var t2X = x1 + u1X * tangentLength;
        var t2Y = y1 + u1Y * tangentLength;
        var (bisX, bisY) = Unit(u0X + u1X, u0Y + u1Y);
        var centreDistance = Math.Abs(Math.Sin(halfAngle)) < MinRadius
            ? 0
            : Math.Abs(radius) / Math.Sin(halfAngle);
        var cx = x1 + bisX * centreDistance;
        var cy = y1 + bisY * centreDistance;
        var startAngle = Degrees(t1X - cx, t1Y - cy);
        var endAngle = Degrees(t2X - cx, t2Y - cy);
        var cross = u0X * u1Y - u0Y * u1X;
        var sweep = NormaliseSweep(startAngle, endAngle, cross < 0);
        return new TangentArc(cx, cy, startAngle, sweep, t1X, t1Y, t2X, t2Y);
    }

    private static (double X, double Y) Unit(double x, double y)
    {
        var length = Math.Sqrt(x * x + y * y);
        return length < MinRadius ? (0.0, 0.0) : (x / length, y / length);
    }

    private static double Degrees(double x, double y)
    {
        var d = Math.Atan2(y, x) * DegreesPerHalfTurn / Math.PI;
        return d < 0 ? d + DegreesPerTurn : d;
    }
}
