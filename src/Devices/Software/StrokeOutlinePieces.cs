namespace Aspose.Pdf.Devices;

public sealed partial class SoftwarePageRenderer
{
    /// <summary>One subpath on its way to the outliner: its points, whether it closes,
    /// and whether it drew anything at all - a lone moveto paints nothing, while a
    /// segment of no length still leaves the pen's dot.</summary>
    private sealed class StrokeRun
    {
        public StrokeRun(bool closed) => Closed = closed;

        public List<(double X, double Y)> Points { get; } = new();

        public bool Closed { get; set; }

        public bool HasSegment => Points.Count > 1;

        public (double X, double Y) Last => Points[Points.Count - 1];

        public void Add((double X, double Y) p) => Points.Add(p);
    }

    /// <summary>The pen, measured in pen space where it is a circle of radius
    /// <see cref="Half"/>.</summary>
    private sealed class StrokeSpec
    {
        public double Half;
        public int Cap;
        public int Join;
        public double MiterLimit;
        public int ArcSteps;
        public double JoinGap;
        public double[]? Dash;
        public double DashPhase;
    }

    /// <summary>
    /// The linear map between pen space - where the pen is round - and device pixels.
    /// Carried through the CTM it is the CTM itself (scaled to pixels, y flipped), so a
    /// squashed transform squashes the pen; for a transform past the anisotropy limit
    /// it is a plain scale by the length the CTM gives a horizontal unit, and the pen
    /// stays round on the page.
    /// </summary>
    private readonly struct PenMap
    {
        private readonly double _a, _b, _c, _d, _ia, _ib, _ic, _id;

        private PenMap(double a, double b, double c, double d, bool carried)
        {
            _a = a; _b = b; _c = c; _d = d;
            var det = a * d - b * c;
            _ia = d / det; _ib = -b / det; _ic = -c / det; _id = a / det;
            Carried = carried;
            MaxStretch = PenGeometry.CtmMaxScale([a, b, c, d]);
        }

        /// <summary>True when the pen is carried through the CTM (and dashes apply).</summary>
        public bool Carried { get; }

        /// <summary>The most a unit of pen space stretches on the page.</summary>
        public double MaxStretch { get; }

        public static PenMap For(double[] ctm, double scale)
        {
            if (PenGeometry.CtmAnisotropy(ctm) <= PenGeometry.AnisotropyLimit
                && Math.Abs(ctm[0] * ctm[3] - ctm[1] * ctm[2]) > 0)
                return new PenMap(ctm[0] * scale, -ctm[1] * scale, ctm[2] * scale, -ctm[3] * scale, carried: true);
            var k = PenGeometry.CtmPenScale(ctm) * scale;
            return new PenMap(k, 0, 0, k, carried: false);
        }

        public (double X, double Y) ToDevice(double x, double y) => (_a * x + _c * y, _b * x + _d * y);

        public (double X, double Y) ToPen((double X, double Y) p) =>
            (_ia * p.X + _ic * p.Y, _ib * p.X + _id * p.Y);
    }

    /// <summary>Every polygon the pen covers along one subpath, in pen space: dashed
    /// into pieces when the pen dashes, each piece bodies plus joins plus caps.</summary>
    private static IEnumerable<List<(double X, double Y)>> StrokeRunOutline(StrokeRun run, StrokeSpec pen)
    {
        if (!run.HasSegment) yield break;
        var pts = Distinct(run.Points, run.Closed);
        if (pts.Count == 1)
        {
            foreach (var dot in Dot(pts[0], pen)) yield return dot;
            yield break;
        }

        var pieces = pen.Dash is null
            ? [new StrokePiece(pts, run.Closed, PathEndAtStart: true, PathEndAtEnd: true)]
            : DashPieces(pts, run.Closed, pen);
        foreach (var piece in pieces)
            foreach (var polygon in PieceOutline(piece, pen))
                yield return polygon;
    }

    /// <summary>A stretch of the path the pen draws without lifting: its points, whether
    /// it closes on itself, and which of its ends are ends of the path (they take the
    /// line cap) rather than ends of a dash (a dash end takes the dash cap).</summary>
    private sealed record StrokePiece(List<(double X, double Y)> Points, bool Closed,
        bool PathEndAtStart, bool PathEndAtEnd);

    /// <summary>The run's points with repeats dropped; a closed run also drops a last
    /// point that returns to the first.</summary>
    private static List<(double X, double Y)> Distinct(List<(double X, double Y)> points, bool closed)
    {
        var result = new List<(double X, double Y)>();
        foreach (var p in points)
            if (result.Count == 0 || !SamePoint(result[result.Count - 1], p)) result.Add(p);
        if (closed && result.Count > 1 && SamePoint(result[0], result[result.Count - 1]))
            result.RemoveAt(result.Count - 1);
        return result;
    }

    private static bool SamePoint((double X, double Y) a, (double X, double Y) b) =>
        Math.Abs(a.X - b.X) < SamePointEpsilon && Math.Abs(a.Y - b.Y) < SamePointEpsilon;

    /// <summary>The pen resting on one point: a round cap leaves a disc, a projecting
    /// square cap a square, a butt cap nothing.</summary>
    private static IEnumerable<List<(double X, double Y)>> Dot((double X, double Y) p, StrokeSpec pen)
    {
        if (pen.Cap == PenGeometry.RoundCap) yield return Circle(p, pen);
        else if (pen.Cap == PenGeometry.SquareCap)
            yield return [(p.X - pen.Half, p.Y - pen.Half), (p.X + pen.Half, p.Y - pen.Half),
                (p.X + pen.Half, p.Y + pen.Half), (p.X - pen.Half, p.Y + pen.Half)];
    }

    private static List<(double X, double Y)> Circle((double X, double Y) c, StrokeSpec pen)
    {
        var ring = new List<(double X, double Y)>(pen.ArcSteps);
        for (var k = 0; k < pen.ArcSteps; k++)
        {
            var a = 2 * Math.PI * k / pen.ArcSteps;
            ring.Add((c.X + pen.Half * Math.Cos(a), c.Y + pen.Half * Math.Sin(a)));
        }

        return ring;
    }
}
