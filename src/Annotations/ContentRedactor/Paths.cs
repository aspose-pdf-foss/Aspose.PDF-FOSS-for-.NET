using System.Text;
using Aspose.Pdf.Content;

namespace Aspose.Pdf.Annotations;

internal sealed partial class ContentRedactor
{
    // A curve that crosses the area is cut as a chain of straight pieces this long (page space) at
    // most, and never more than this many of them.
    private const double CurveStep = 0.5;
    private const int MaxCurvePieces = 256;

    /// <summary>One sub-path: the commands that built it, the outline they trace (curves as
    /// chains of lines, in the path's own space), and whether it was closed.</summary>
    private sealed class Subpath
    {
        public readonly List<PathCommand> Commands = new();
        public readonly List<(double X, double Y)> Outline = new();
        public bool Closed;
    }

    /// <summary>What replaces the path the operation <paramref name="op"/> paints, or null when
    /// none of it runs through the area. Sub-paths clear of the area are written as they were,
    /// sub-paths inside it are dropped, and a sub-path crossing its edge is cut there: its fill
    /// becomes the pieces of its outline outside the area, its stroke the stretches of its outline
    /// outside the area. A clip the path installs stays whole (it paints nothing).</summary>
    private string? PlanPath(Walk walk, string op, GraphicsState state, IReadOnlyList<PathCommand> commands)
    {
        if (op == "n" || commands.Count == 0) return null;
        var area = AreaIn(state.Ctm);
        if (area is null) return null;
        var scale = Math.Sqrt(Math.Abs(state.Ctm[0] * state.Ctm[3] - state.Ctm[1] * state.Ctm[2]));
        var subpaths = Subpaths(commands, scale);
        var coverage = subpaths.Select(s => CoverageOf(area, s)).ToList();
        if (coverage.All(c => c == Coverage.Outside)) return null;

        var fills = op is "f" or "F" or "f*" or "B" or "B*" or "b" or "b*";
        var strokes = op is "S" or "s" or "B" or "B*" or "b" or "b*";
        var closesForStroke = op is "s" or "b" or "b*";
        var sb = new StringBuilder();
        if (fills) AppendFill(sb, area, subpaths, coverage, op is "f*" or "B*" or "b*" ? "f*" : "f");
        if (strokes && AppendStroke(sb, area, subpaths, coverage, closesForStroke))
            sb.Append("S\n");
        if (walk.PathClip is { } clip)
        {
            foreach (var s in subpaths) AppendCommands(sb, s.Commands);
            sb.Append(clip).Append(" n\n");
        }
        return sb.ToString().TrimEnd();
    }

    private static Coverage CoverageOf(ConvexRegion area, Subpath s)
    {
        if (s.Outline.All(p => area.Contains(p.X, p.Y))) return Coverage.Inside;
        var count = s.Outline.Count;
        for (var i = 0; i < count; i++)
            if (area.ClipSegment(s.Outline[i], s.Outline[(i + 1) % count]) is not null) return Coverage.Partly;
        // A closed outline can hold the whole area without crossing it.
        var inside = area.Clip(s.Outline);
        return inside.Count >= 3 && ConvexRegion.Area(inside) > 0 ? Coverage.Partly : Coverage.Outside;
    }

    /// <summary>The fill, as paths of its own: the sub-paths clear of the area as they were when
    /// none crosses it; else the outline of every sub-path not inside it cut to each piece of the
    /// plane outside the area, one filled path per piece. The pieces do not overlap, so each fills
    /// under the fill rule as the whole did there - and no piece spans the area, which a renderer
    /// drawing a thin fill across its bounds would paint over.</summary>
    private static void AppendFill(StringBuilder sb, ConvexRegion area, List<Subpath> subpaths, List<Coverage> coverage,
        string fill)
    {
        if (!coverage.Contains(Coverage.Partly))
        {
            var any = false;
            for (var i = 0; i < subpaths.Count; i++)
            {
                if (coverage[i] != Coverage.Outside) continue;
                AppendCommands(sb, subpaths[i].Commands);
                any = true;
            }
            if (any) sb.Append(fill).Append('\n');
            return;
        }
        foreach (var region in area.Complement())
        {
            var any = false;
            for (var i = 0; i < subpaths.Count; i++)
            {
                if (coverage[i] == Coverage.Inside || subpaths[i].Outline.Count < 3) continue;
                var piece = region.Clip(subpaths[i].Outline);
                if (piece.Count < 3 || ConvexRegion.Area(piece) <= 0) continue;
                AppendPolyline(sb, piece, 0, piece.Count, closed: true);
                any = true;
            }
            if (any) sb.Append(fill).Append('\n');
        }
    }

    private static bool AppendStroke(StringBuilder sb, ConvexRegion area, List<Subpath> subpaths, List<Coverage> coverage,
        bool closesForStroke)
    {
        var any = false;
        for (var i = 0; i < subpaths.Count; i++)
        {
            var s = subpaths[i];
            if (coverage[i] == Coverage.Inside) continue;
            any = true;
            if (coverage[i] == Coverage.Outside)
            {
                AppendCommands(sb, s.Commands);
                if (closesForStroke && !s.Closed) sb.Append("h\n");
                continue;
            }
            var points = new List<(double X, double Y)>(s.Outline);
            if (s.Closed || closesForStroke) points.Add(points[0]);
            AppendStretchesOutside(sb, area, points);
        }
        return any;
    }

    /// <summary>The stretches of an open outline outside the area, each as its own sub-path.</summary>
    private static void AppendStretchesOutside(StringBuilder sb, ConvexRegion area, List<(double X, double Y)> points)
    {
        var run = new List<(double X, double Y)>();
        void Flush()
        {
            if (run.Count >= 2) AppendPolyline(sb, run, 0, run.Count, closed: false);
            run.Clear();
        }
        (double X, double Y) At((double X, double Y) p, (double X, double Y) q, double t) =>
            (p.X + t * (q.X - p.X), p.Y + t * (q.Y - p.Y));

        for (var i = 0; i + 1 < points.Count; i++)
        {
            var (p, q) = (points[i], points[i + 1]);
            if (run.Count == 0) run.Add(p);
            if (area.ClipSegment(p, q) is not { } inside)
            {
                run.Add(q);
                continue;
            }
            // The stretch before the area ends where the segment enters it; the next starts where
            // the segment leaves it and runs on to the segment's end.
            if (inside.From > 0) run.Add(At(p, q, inside.From));
            Flush();
            if (inside.To < 1)
            {
                run.Add(At(p, q, inside.To));
                run.Add(q);
            }
        }
        Flush();
    }

    private static List<Subpath> Subpaths(IReadOnlyList<PathCommand> commands, double scale)
    {
        var result = new List<Subpath>();
        Subpath? current = null;
        (double X, double Y) point = (0, 0), start = (0, 0);
        foreach (var c in commands)
        {
            switch (c.Op)
            {
                case PathOp.MoveTo:
                    current = new Subpath();
                    result.Add(current);
                    point = start = (c.X1, c.Y1);
                    current.Outline.Add(point);
                    break;
                case PathOp.Rect:
                    current = new Subpath { Closed = true };
                    result.Add(current);
                    current.Outline.AddRange([(c.X1, c.Y1), (c.X1 + c.X2, c.Y1), (c.X1 + c.X2, c.Y1 + c.Y2), (c.X1, c.Y1 + c.Y2)]);
                    point = start = (c.X1, c.Y1);
                    current.Commands.Add(c);
                    current = null;
                    continue;
                case PathOp.LineTo when current is not null:
                    point = (c.X1, c.Y1);
                    current.Outline.Add(point);
                    break;
                case PathOp.CurveTo or PathOp.CurveToV or PathOp.CurveToY when current is not null:
                    var (c1, c2, end) = c.Op switch
                    {
                        PathOp.CurveTo => ((c.X1, c.Y1), (c.X2, c.Y2), (c.X3, c.Y3)),
                        PathOp.CurveToV => (point, (c.X1, c.Y1), (c.X2, c.Y2)),
                        _ => ((c.X1, c.Y1), (c.X2, c.Y2), (c.X2, c.Y2)),
                    };
                    AppendCurve(current.Outline, point, c1, c2, end, scale);
                    point = end;
                    break;
                case PathOp.Close when current is not null:
                    current.Closed = true;
                    point = start;
                    break;
                default:
                    continue;
            }
            current?.Commands.Add(c);
        }
        return result;
    }

    private static void AppendCurve(List<(double X, double Y)> outline, (double X, double Y) p0, (double X, double Y) p1,
        (double X, double Y) p2, (double X, double Y) p3, double scale)
    {
        double Distance((double X, double Y) a, (double X, double Y) b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        var length = (Distance(p0, p1) + Distance(p1, p2) + Distance(p2, p3)) * scale;
        var pieces = (int)Math.Min(MaxCurvePieces, Math.Max(1, Math.Ceiling(length / CurveStep)));
        for (var i = 1; i <= pieces; i++)
        {
            var t = (double)i / pieces;
            var u = 1 - t;
            double a = u * u * u, b = 3 * u * u * t, c = 3 * u * t * t, d = t * t * t;
            outline.Add((a * p0.X + b * p1.X + c * p2.X + d * p3.X, a * p0.Y + b * p1.Y + c * p2.Y + d * p3.Y));
        }
    }

    private static void AppendCommands(StringBuilder sb, List<PathCommand> commands)
    {
        foreach (var c in commands)
        {
            var line = c.Op switch
            {
                PathOp.MoveTo => $"{Number(c.X1)} {Number(c.Y1)} m",
                PathOp.LineTo => $"{Number(c.X1)} {Number(c.Y1)} l",
                PathOp.CurveTo => $"{Number(c.X1)} {Number(c.Y1)} {Number(c.X2)} {Number(c.Y2)} {Number(c.X3)} {Number(c.Y3)} c",
                PathOp.CurveToV => $"{Number(c.X1)} {Number(c.Y1)} {Number(c.X2)} {Number(c.Y2)} v",
                PathOp.CurveToY => $"{Number(c.X1)} {Number(c.Y1)} {Number(c.X2)} {Number(c.Y2)} y",
                PathOp.Rect => $"{Number(c.X1)} {Number(c.Y1)} {Number(c.X2)} {Number(c.Y2)} re",
                _ => "h",
            };
            sb.Append(line).Append('\n');
        }
    }

    private static void AppendPolyline(StringBuilder sb, List<(double X, double Y)> points, int from, int to, bool closed)
    {
        for (var i = from; i < to; i++)
            sb.Append(Number(points[i].X)).Append(' ').Append(Number(points[i].Y)).Append(i == from ? " m\n" : " l\n");
        if (closed) sb.Append("h\n");
    }
}
