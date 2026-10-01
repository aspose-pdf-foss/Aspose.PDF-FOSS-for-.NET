namespace Aspose.Pdf.Devices;

public sealed partial class SoftwarePageRenderer
{
    /// <summary>The line join that carries both edges out to their meeting point.</summary>
    private const int MiterJoin = 0;

    /// <summary>
    /// Cut a run into the pieces the dash pattern draws. The pattern starts ON at its
    /// first element with the phase consumed cyclically (PDF 32000 §8.4.3.6), and runs on
    /// around corners, so a dash that spans a vertex keeps its join there. A piece that
    /// starts where the path starts, or ends where it ends, takes the line cap at that end.
    /// </summary>
    private static List<StrokePiece> DashPieces(List<(double X, double Y)> pts, bool closed, StrokeSpec pen)
    {
        var dash = pen.Dash!;
        var total = 0.0;
        foreach (var d in dash) total += d;
        var idx = 0;
        var on = true;
        var pos = total > 0 ? Math.Max(0, pen.DashPhase) % total : 0;
        while (pos >= dash[idx])
        {
            pos -= dash[idx];
            idx = (idx + 1) % dash.Length;
            on = !on;
        }

        var pieces = new List<StrokePiece>();
        List<(double X, double Y)>? piece = on ? [pts[0]] : null;
        var pieceAtStart = on && !closed;
        var segments = closed ? pts.Count : pts.Count - 1;
        for (var s = 0; s < segments; s++)
        {
            var a = pts[s];
            var b = pts[(s + 1) % pts.Count];
            var len = Distance(a, b);
            var t = 0.0;
            while (t < len)
            {
                var take = Math.Min(dash[idx] - pos, len - t);
                t += take;
                pos += take;
                var p = Lerp(a, b, t / len);
                if (on) piece!.Add(p);
                if (pos < dash[idx] - SamePointEpsilon) continue;
                if (on) pieces.Add(new StrokePiece(piece!, false, pieceAtStart, PathEndAtEnd: false));
                pieceAtStart = false;
                idx = (idx + 1) % dash.Length;
                pos = 0;
                on = !on;
                piece = on ? [p] : null;
            }
        }

        if (on && piece is { Count: > 1 }) pieces.Add(new StrokePiece(piece, false, pieceAtStart, PathEndAtEnd: !closed));
        return pieces;
    }

    /// <summary>
    /// The polygons one piece covers: a quad along every segment, a join at every corner,
    /// and a cap at each open end. An end of the PATH takes the line cap, projecting past
    /// the end point; an end of a DASH is round only under a round cap, and then the cap
    /// sits inside the dash (the device counts it into the dash's length), else flat.
    /// </summary>
    private static IEnumerable<List<(double X, double Y)>> PieceOutline(StrokePiece piece, StrokeSpec pen)
    {
        var trimmed = piece.Closed || pen.Cap != PenGeometry.RoundCap ? piece.Points : TrimDashEnds(piece, pen.Half);
        if (trimmed is null)
        {
            yield return Circle(Midpoint(piece.Points), pen);
            yield break;
        }

        var pts = Distinct(trimmed, piece.Closed);
        if (pts.Count == 1)
        {
            foreach (var dot in Dot(pts[0], pen)) yield return dot;
            yield break;
        }

        var segments = piece.Closed ? pts.Count : pts.Count - 1;
        for (var s = 0; s < segments; s++)
            yield return Body(pts[s], pts[(s + 1) % pts.Count], pen.Half);
        var first = piece.Closed ? 0 : 1;
        for (var v = first; v < pts.Count - (piece.Closed ? 0 : 1); v++)
        {
            var prev = pts[(v + pts.Count - 1) % pts.Count];
            if (Join(prev, pts[v], pts[(v + 1) % pts.Count], pen) is { } join) yield return join;
        }

        if (piece.Closed) yield break;
        if (EndCap(pts[0], pts[1], piece.PathEndAtStart, pen) is { } startCap) yield return startCap;
        if (EndCap(pts[pts.Count - 1], pts[pts.Count - 2], piece.PathEndAtEnd, pen) is { } endCap) yield return endCap;
    }

    /// <summary>The pen dragged straight from a to b.</summary>
    private static List<(double X, double Y)> Body((double X, double Y) a, (double X, double Y) b, double half)
    {
        var (nx, ny) = LeftNormal(a, b, half);
        return [(a.X + nx, a.Y + ny), (b.X + nx, b.Y + ny), (b.X - nx, b.Y - ny), (a.X - nx, a.Y - ny)];
    }

    /// <summary>What fills the corner at <paramref name="v"/> on its outer side: a round
    /// join's disc, a miter's point while it stays within the limit, else a bevel; nothing
    /// where the two bodies already meet.</summary>
    private static List<(double X, double Y)>? Join((double X, double Y) prev, (double X, double Y) v,
        (double X, double Y) next, StrokeSpec pen)
    {
        var (d0x, d0y) = Unit(prev, v);
        var (d1x, d1y) = Unit(v, next);
        var cross = d0x * d1y - d0y * d1x;
        var dot = d0x * d1x + d0y * d1y;
        if (dot > 0 && Math.Abs(cross) * pen.Half < pen.JoinGap) return null;
        if (pen.Join == PenGeometry.RoundJoin) return Circle(v, pen);
        var side = cross > 0 ? -pen.Half : pen.Half;
        (double X, double Y) o0 = (-d0y * side, d0x * side);
        (double X, double Y) o1 = (-d1y * side, d1x * side);
        var sinHalf = Math.Sqrt(Math.Max(0, (1 + dot) / 2));
        var bisX = o0.X + o1.X;
        var bisY = o0.Y + o1.Y;
        var bisLen = Math.Sqrt(bisX * bisX + bisY * bisY);
        if (pen.Join == MiterJoin && sinHalf > SamePointEpsilon && 1 / sinHalf <= pen.MiterLimit && bisLen > SamePointEpsilon)
        {
            var reach = pen.Half / sinHalf;
            return [v, (v.X + o0.X, v.Y + o0.Y), (v.X + bisX / bisLen * reach, v.Y + bisY / bisLen * reach), (v.X + o1.X, v.Y + o1.Y)];
        }

        return [v, (v.X + o0.X, v.Y + o0.Y), (v.X + o1.X, v.Y + o1.Y)];
    }

    /// <summary>The cap at <paramref name="end"/>, facing away from <paramref name="inner"/>:
    /// the line cap at an end of the path, a round cap at a round-capped dash end (already
    /// pulled in by half a pen), nothing at a flat one.</summary>
    private static List<(double X, double Y)>? EndCap((double X, double Y) end, (double X, double Y) inner,
        bool pathEnd, StrokeSpec pen)
    {
        if (pen.Cap == PenGeometry.RoundCap) return Circle(end, pen);
        if (!pathEnd || pen.Cap != PenGeometry.SquareCap) return null;
        var (dx, dy) = Unit(inner, end);
        var (nx, ny) = LeftNormal(inner, end, pen.Half);
        var ox = dx * pen.Half;
        var oy = dy * pen.Half;
        return [(end.X + nx, end.Y + ny), (end.X + nx + ox, end.Y + ny + oy),
            (end.X - nx + ox, end.Y - ny + oy), (end.X - nx, end.Y - ny)];
    }

    /// <summary>A round-capped dash with half a pen pulled in at each end that is a dash
    /// end rather than a path end, so the caps sit inside its length; null when the dash is
    /// too short to hold them and leaves only the pen's disc.</summary>
    private static List<(double X, double Y)>? TrimDashEnds(StrokePiece piece, double half)
    {
        List<(double X, double Y)>? pts = piece.Points;
        if (!piece.PathEndAtStart) pts = TrimPolyline(pts, half);
        if (pts is null || piece.PathEndAtEnd) return pts;
        return TrimPolyline(Reverse(pts), half) is { } back ? Reverse(back) : null;
    }

    /// <summary>The polyline with <paramref name="cut"/> taken off its start, or null when
    /// that is all of it.</summary>
    private static List<(double X, double Y)>? TrimPolyline(List<(double X, double Y)> pts, double cut)
    {
        for (var s = 0; s + 1 < pts.Count; s++)
        {
            var len = Distance(pts[s], pts[s + 1]);
            if (cut < len - SamePointEpsilon)
            {
                var rest = new List<(double X, double Y)> { Lerp(pts[s], pts[s + 1], cut / len) };
                rest.AddRange(pts.GetRange(s + 1, pts.Count - s - 1));
                return rest;
            }

            cut -= len;
        }

        return null;
    }

    private static List<(double X, double Y)> Reverse(List<(double X, double Y)> pts)
    {
        var copy = new List<(double X, double Y)>(pts);
        copy.Reverse();
        return copy;
    }

    /// <summary>The point halfway along the polyline.</summary>
    private static (double X, double Y) Midpoint(List<(double X, double Y)> pts)
    {
        var total = 0.0;
        for (var s = 0; s + 1 < pts.Count; s++) total += Distance(pts[s], pts[s + 1]);
        return TrimPolyline(pts, total / 2) is { Count: > 0 } rest ? rest[0] : pts[0];
    }

    private static double Distance((double X, double Y) a, (double X, double Y) b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static (double X, double Y) Lerp((double X, double Y) a, (double X, double Y) b, double t) =>
        (a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    private static (double X, double Y) Unit((double X, double Y) a, (double X, double Y) b)
    {
        var len = Distance(a, b);
        return len < SamePointEpsilon ? (1, 0) : ((b.X - a.X) / len, (b.Y - a.Y) / len);
    }

    /// <summary>The offset that carries the piece a→b to its left by <paramref name="half"/>.</summary>
    private static (double X, double Y) LeftNormal((double X, double Y) a, (double X, double Y) b, double half)
    {
        var (dx, dy) = Unit(a, b);
        return (-dy * half, dx * half);
    }
}
