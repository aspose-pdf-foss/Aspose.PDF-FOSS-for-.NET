namespace Aspose.Pdf;

/// <summary>
/// A convex region of the plane, held as the half-planes it is the intersection of, and the
/// clipping a redaction needs against it: how much of a polygon lies inside, the pieces of a
/// polygon or a segment that lie outside. A region may be unbounded (the pieces of a
/// complement are).
/// </summary>
internal sealed class ConvexRegion
{
    // A segment whose run across an edge is this small runs along it.
    private const double Parallel = 1e-12;
    // How far past an edge, relative to its coefficients, a point still counts as on it.
    private const double EdgeSlack = 1e-9;

    // Inside an edge: A*x + B*y <= C.
    private readonly (double A, double B, double C)[] _edges;

    private ConvexRegion((double A, double B, double C)[] edges) => _edges = edges;

    /// <summary>The region inside a convex polygon, its corners in either turning order.
    /// Null for a polygon that encloses no area (all corners on one line).</summary>
    internal static ConvexRegion? FromPolygon(IReadOnlyList<(double X, double Y)> corners)
    {
        var turn = Math.Sign(SignedArea(corners));
        if (turn == 0) return null;
        var edges = new (double A, double B, double C)[corners.Count];
        for (var i = 0; i < corners.Count; i++)
        {
            var (px, py) = corners[i];
            var (qx, qy) = corners[(i + 1) % corners.Count];
            // Counter-clockwise, the inside lies left of p->q.
            double a = (qy - py) * turn, b = -(qx - px) * turn;
            edges[i] = (a, b, a * px + b * py);
        }
        return new ConvexRegion(edges);
    }

    /// <summary>Convex regions that together cover the plane outside this one, without
    /// overlapping: the k-th lies outside edge k and inside the edges before it.</summary>
    internal ConvexRegion[] Complement()
    {
        var pieces = new ConvexRegion[_edges.Length];
        for (var k = 0; k < _edges.Length; k++)
        {
            var edges = new (double A, double B, double C)[k + 1];
            Array.Copy(_edges, edges, k);
            var (a, b, c) = _edges[k];
            edges[k] = (-a, -b, -c);
            pieces[k] = new ConvexRegion(edges);
        }
        return pieces;
    }

    internal bool Contains(double x, double y)
    {
        foreach (var (a, b, c) in _edges)
            if (a * x + b * y > c + Tolerance(a, b, c)) return false;
        return true;
    }

    /// <summary>The part of a polygon inside the region (Sutherland-Hodgman). At every point
    /// inside the region the result winds as many times as the polygon did, so pieces cut from
    /// several sub-paths fill the same under either fill rule.</summary>
    internal List<(double X, double Y)> Clip(IReadOnlyList<(double X, double Y)> polygon)
    {
        var current = new List<(double X, double Y)>(polygon);
        foreach (var (a, b, c) in _edges)
        {
            if (current.Count == 0) break;
            var next = new List<(double X, double Y)>(current.Count + 2);
            for (var i = 0; i < current.Count; i++)
            {
                var p = current[i];
                var q = current[(i + 1) % current.Count];
                var dp = a * p.X + b * p.Y - c;
                var dq = a * q.X + b * q.Y - c;
                if (dp <= 0) next.Add(p);
                if ((dp < 0 && dq > 0) || (dp > 0 && dq < 0))
                {
                    var t = dp / (dp - dq);
                    next.Add((p.X + t * (q.X - p.X), p.Y + t * (q.Y - p.Y)));
                }
            }
            current = next;
        }
        return current;
    }

    /// <summary>The stretch of the segment p-q inside the region, as parameters along it
    /// (Cyrus-Beck); null when none of it is.</summary>
    internal (double From, double To)? ClipSegment((double X, double Y) p, (double X, double Y) q)
    {
        double from = 0, to = 1;
        foreach (var (a, b, c) in _edges)
        {
            var dp = a * p.X + b * p.Y - c;
            var slope = a * (q.X - p.X) + b * (q.Y - p.Y);
            if (Math.Abs(slope) < Parallel)
            {
                if (dp > 0) return null;
                continue;
            }
            var t = -dp / slope;
            if (slope < 0) from = Math.Max(from, t);
            else to = Math.Min(to, t);
            if (from >= to) return null;
        }
        return (from, to);
    }

    internal static double Area(IReadOnlyList<(double X, double Y)> polygon) => Math.Abs(SignedArea(polygon));

    private static double SignedArea(IReadOnlyList<(double X, double Y)> polygon)
    {
        double sum = 0;
        for (var i = 0; i < polygon.Count; i++)
        {
            var p = polygon[i];
            var q = polygon[(i + 1) % polygon.Count];
            sum += p.X * q.Y - q.X * p.Y;
        }
        return sum / 2;
    }

    // A point on an edge counts as inside it; the slack scales with the edge's coefficients
    // so it means the same distance in any unit.
    private static double Tolerance(double a, double b, double c) =>
        EdgeSlack * (Math.Abs(a) + Math.Abs(b) + Math.Abs(c));
}
