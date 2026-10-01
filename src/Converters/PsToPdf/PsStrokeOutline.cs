using System;
using System.Collections.Generic;

namespace Aspose.Pdf.Converters.PsToPdf;

/// <summary>
/// The outline of a stroke: the region a <c>stroke</c> would paint, expressed as a
/// path. <c>strokepath</c> hands that region back to the program, which then fills it,
/// clips to it, or strokes it again.
///
/// Curves are flattened first and each piece is offset by half the pen to either side.
/// A closed subpath yields two rings, the outer traced forwards and the inner
/// backwards, so the area between them is what either fill rule paints; an open one
/// yields a single ring that runs out along one side, turns at the cap, and comes back
/// along the other.
/// </summary>
internal static class PsStrokeOutline
{
    /// <summary>The line cap that stops the pen square at the end point.</summary>
    private const int ButtCap = 0;

    /// <summary>The line cap that rounds the pen off past the end point.</summary>
    private const int RoundCap = 1;

    /// <summary>The line join that carries both edges out to their meeting point.</summary>
    private const int MiterJoin = 0;

    /// <summary>The line join that rounds the corner off.</summary>
    private const int RoundJoin = 1;

    /// <summary>How many straight pieces a half-turn of a round cap or join is cut
    /// into. A full circle is therefore twice this, which reads as round at every
    /// resolution the corpus renders at.</summary>
    private const int RoundSteps = 12;

    /// <summary>Two points closer than this are the same point.</summary>
    private const double Epsilon = 1e-9;

    /// <summary>The region the pen paints, or an empty path when the pen has no width
    /// or the path has nothing to stroke.</summary>
    public static PsPath Outline(PsPath path, double width, int cap, int join, double miterLimit)
    {
        var outline = new PsPath();
        if (path is null || width <= 0) return outline;
        var half = width / 2;
        foreach (var run in Runs(path.Flatten()))
        {
            if (run.Points.Count == 1)
            {
                AddDot(outline, run.Points[0], half, cap);
            }
            else if (run.Closed)
            {
                AddLoop(outline, run.Points, half, join, miterLimit, closed: true, cap);
                AddLoop(outline, Reversed(run.Points), half, join, miterLimit, closed: true, cap);
            }
            else if (run.Points.Count > 1)
            {
                AddLoop(outline, run.Points, half, join, miterLimit, closed: false, cap);
            }
        }

        return outline;
    }

    /// <summary>One subpath of a flattened path: its points, and whether it closes.</summary>
    private sealed class PsRun
    {
        public List<PsPoint> Points { get; } = new();

        public bool Closed { get; set; }
    }

    private readonly struct PsPoint
    {
        public PsPoint(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; }

        public double Y { get; }
    }

    /// <summary>Cut a flattened path into its subpaths, dropping repeated points: a
    /// zero-length piece has no direction to offset along.</summary>
    private static List<PsRun> Runs(PsPath flat)
    {
        var runs = new List<PsRun>();
        PsRun? run = null;
        foreach (var s in flat.Segments)
        {
            if (s.Kind == PsSegmentKind.Move)
            {
                run = new PsRun();
                runs.Add(run);
                run.Points.Add(new PsPoint(s.X, s.Y));
            }
            else if (run is null)
            {
                continue;
            }
            else if (s.Kind == PsSegmentKind.Close)
            {
                run.Closed = true;
            }
            else
            {
                AddDistinct(run.Points, new PsPoint(s.X, s.Y));
            }
        }

        foreach (var r in runs)
        {
            if (r.Closed && r.Points.Count > 1 && Same(r.Points[0], r.Points[r.Points.Count - 1]))
                r.Points.RemoveAt(r.Points.Count - 1);
        }

        return runs;
    }

    private static void AddDistinct(List<PsPoint> points, PsPoint p)
    {
        if (points.Count > 0 && Same(points[points.Count - 1], p)) return;
        points.Add(p);
    }

    private static bool Same(PsPoint a, PsPoint b) =>
        Math.Abs(a.X - b.X) < Epsilon && Math.Abs(a.Y - b.Y) < Epsilon;

    private static List<PsPoint> Reversed(List<PsPoint> points)
    {
        var copy = new List<PsPoint>(points);
        copy.Reverse();
        return copy;
    }

    /// <summary>The pen sitting on a single point: a round cap leaves a disc, a
    /// projecting square cap a square, and a butt cap nothing at all.</summary>
    private static void AddDot(PsPath outline, PsPoint p, double half, int cap)
    {
        if (cap == ButtCap) return;
        var ring = new List<PsPoint>();
        if (cap == RoundCap)
        {
            for (var k = 0; k < 2 * RoundSteps; k++)
            {
                var a = 2 * Math.PI * k / (2 * RoundSteps);
                ring.Add(new PsPoint(p.X + half * Math.Cos(a), p.Y + half * Math.Sin(a)));
            }
        }
        else
        {
            ring.Add(new PsPoint(p.X - half, p.Y - half));
            ring.Add(new PsPoint(p.X + half, p.Y - half));
            ring.Add(new PsPoint(p.X + half, p.Y + half));
            ring.Add(new PsPoint(p.X - half, p.Y + half));
        }

        Emit(outline, ring);
    }

    /// <summary>Walk one side of the run, joining at every corner. A closed run comes
    /// back to where it started and is written as it stands; an open one turns at the
    /// far cap, walks the other side back, and turns again at the near cap.</summary>
    private static void AddLoop(PsPath outline, List<PsPoint> points, double half,
        int join, double miterLimit, bool closed, int cap)
    {
        var ring = new List<PsPoint>();
        Walk(ring, points, half, join, miterLimit, closed);
        if (!closed)
        {
            var back = Reversed(points);
            AddCap(ring, points[points.Count - 1], Direction(points[points.Count - 2], points[points.Count - 1]), half, cap);
            Walk(ring, back, half, join, miterLimit, closed: false);
            AddCap(ring, points[0], Direction(points[1], points[0]), half, cap);
        }

        Emit(outline, ring);
    }

    /// <summary>Offset every piece of the run to its left, joining at the corners
    /// between them.</summary>
    private static void Walk(List<PsPoint> ring, List<PsPoint> points, double half,
        int join, double miterLimit, bool closed)
    {
        var last = closed ? points.Count : points.Count - 1;
        for (var k = 0; k < last; k++)
        {
            var a = points[k];
            var b = points[(k + 1) % points.Count];
            var n = Normal(a, b, half);
            if (k > 0 || closed)
            {
                var previous = points[(k + points.Count - 1) % points.Count];
                AddJoin(ring, a, Normal(previous, a, half), n, half, join, miterLimit);
            }

            ring.Add(new PsPoint(a.X + n.X, a.Y + n.Y));
            ring.Add(new PsPoint(b.X + n.X, b.Y + n.Y));
        }
    }

    /// <summary>Bridge the gap between two offset edges at a corner. Where the corner
    /// turns towards this side the edges overlap and meet on their own; where it turns
    /// away they part, and the join fills what is between.</summary>
    private static void AddJoin(List<PsPoint> ring, PsPoint vertex, PsPoint before, PsPoint after,
        double half, int join, double miterLimit)
    {
        var cross = before.X * after.Y - before.Y * after.X;
        if (cross >= 0) return;
        var a = new PsPoint(vertex.X + before.X, vertex.Y + before.Y);
        var b = new PsPoint(vertex.X + after.X, vertex.Y + after.Y);
        if (join == RoundJoin)
        {
            AddArc(ring, vertex, a, b, half);
            return;
        }

        if (join == MiterJoin && TryMiter(vertex, before, after, half, miterLimit, out var m))
            ring.Add(m);
    }

    /// <summary>Where the two offset edges would meet, when the corner is not so sharp
    /// that the meeting point runs past the miter limit.</summary>
    private static bool TryMiter(PsPoint vertex, PsPoint before, PsPoint after,
        double half, double miterLimit, out PsPoint miter)
    {
        miter = default;
        var bx = before.X + after.X;
        var by = before.Y + after.Y;
        var length = Math.Sqrt(bx * bx + by * by);
        if (length < Epsilon) return false;
        // The meeting point lies along the bisector of the two offsets, at half the pen
        // divided by the sine of the half-angle between the edges — which is what the
        // bisector's own length reports.
        var reach = half * half * 2 / length;
        if (reach > miterLimit * half) return false;
        miter = new PsPoint(vertex.X + bx / length * reach, vertex.Y + by / length * reach);
        return true;
    }

    /// <summary>Turn the corner the long way round, along the pen's own circle.</summary>
    private static void AddArc(List<PsPoint> ring, PsPoint centre, PsPoint from, PsPoint to, double half)
    {
        var a0 = Math.Atan2(from.Y - centre.Y, from.X - centre.X);
        var a1 = Math.Atan2(to.Y - centre.Y, to.X - centre.X);
        while (a1 > a0) a1 -= 2 * Math.PI;
        for (var k = 1; k < RoundSteps; k++)
        {
            var a = a0 + (a1 - a0) * k / RoundSteps;
            ring.Add(new PsPoint(centre.X + half * Math.Cos(a), centre.Y + half * Math.Sin(a)));
        }
    }

    /// <summary>Turn from one side of the run to the other at an end point.</summary>
    private static void AddCap(List<PsPoint> ring, PsPoint end, PsPoint direction, double half, int cap)
    {
        var n = new PsPoint(-direction.Y * half, direction.X * half);
        if (cap == RoundCap)
        {
            AddArc(ring, end, new PsPoint(end.X + n.X, end.Y + n.Y),
                new PsPoint(end.X - n.X, end.Y - n.Y), half);
        }
        else if (cap != ButtCap)
        {
            var d = new PsPoint(direction.X * half, direction.Y * half);
            ring.Add(new PsPoint(end.X + n.X + d.X, end.Y + n.Y + d.Y));
            ring.Add(new PsPoint(end.X - n.X + d.X, end.Y - n.Y + d.Y));
        }
    }

    /// <summary>The unit vector from one point to another.</summary>
    private static PsPoint Direction(PsPoint a, PsPoint b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        return length < Epsilon ? new PsPoint(1, 0) : new PsPoint(dx / length, dy / length);
    }

    /// <summary>The offset that carries a piece to its left by half the pen.</summary>
    private static PsPoint Normal(PsPoint a, PsPoint b, double half)
    {
        var d = Direction(a, b);
        return new PsPoint(-d.Y * half, d.X * half);
    }

    private static void Emit(PsPath outline, List<PsPoint> ring)
    {
        if (ring.Count < 3) return;
        outline.MoveTo(ring[0].X, ring[0].Y);
        for (var k = 1; k < ring.Count; k++)
        {
            if (Same(ring[k], ring[k - 1])) continue;
            outline.LineTo(ring[k].X, ring[k].Y);
        }

        outline.ClosePath();
    }
}
