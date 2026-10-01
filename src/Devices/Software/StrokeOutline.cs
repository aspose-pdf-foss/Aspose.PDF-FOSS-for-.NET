using Aspose.Pdf.Content;
using Aspose.Pdf.Devices.Rasterizer;

namespace Aspose.Pdf.Devices;

public sealed partial class SoftwarePageRenderer
{
    /// <summary>The narrowest stroke, in device pixels, drawn as an outline with its caps
    /// and joins. A narrower one keeps the per-segment emitter, whose pixel-grid hinting
    /// lays a hairline rule on one clean row; its caps and joins are sub-pixel anyway.</summary>
    private const double OutlineStrokeMinimumPx = 2.0;

    /// <summary>How far, in device pixels, a round cap or join may stray from the true
    /// circle before it is cut into more pieces.</summary>
    private const double ArcTolerancePx = 0.25;

    /// <summary>The fewest pieces a full circle of the pen is cut into.</summary>
    private const int MinArcSteps = 8;

    /// <summary>The most pieces a full circle of the pen is cut into.</summary>
    private const int MaxArcSteps = 128;

    /// <summary>A corner that opens the outline by less than this, in device pixels,
    /// needs no join: the two bodies already meet (every vertex of a flattened curve).</summary>
    private const double JoinGapTolerancePx = 0.05;

    /// <summary>Two points closer than this, in pen units, are the same point.</summary>
    private const double SamePointEpsilon = 1e-9;

    /// <summary>The miter limit a state with none, or a nonsensical one, stands for.</summary>
    private const double DefaultMiterLimit = 10.0;

    /// <summary>How many page spans a pen may reach before it is taken for damage.</summary>
    private const double AbsurdPenPageSpans = 64.0;

    /// <summary>The absurd-pen bar for a page too small for the span rule.</summary>
    private const double AbsurdPenFloorPx = 1e5;

    /// <summary>
    /// Strokes the path as the region the pen covers - bodies, caps, joins and dashes -
    /// filled once under the nonzero rule, the way the GDI+ renderer's pen draws it: the
    /// pen is carried through the CTM (so a squashed transform squashes it), unless the
    /// transform is too lopsided, where it stays round on the page as wide as the CTM
    /// makes a horizontal unit and draws solid. Returns false for a pen too thin to
    /// outline, which the per-segment emitter then draws.
    /// </summary>
    private static bool TryStrokeOutline(DrawPathState dp)
    {
        var state = dp.state;
        if (state.LineWidth <= 0) return false;
        var pen = PenMap.For(state.Ctm, dp.ctx.Scale);
        var devWidth = state.LineWidth * pen.MaxStretch;
        if (!Compat.IsFinite(devWidth)) return true;
        if (devWidth < OutlineStrokeMinimumPx) return false;
        if (devWidth > Math.Max(AbsurdPenFloorPx, Math.Max(dp.ctx.PixelW, dp.ctx.PixelH) * AbsurdPenPageSpans))
            return true;

        var stroke = new StrokeSpec
        {
            Half = state.LineWidth / 2,
            Cap = state.LineCap,
            Join = state.LineJoin,
            MiterLimit = state.MiterLimit >= 1 ? state.MiterLimit : DefaultMiterLimit,
            ArcSteps = ArcStepsFor(state.LineWidth / 2 * pen.MaxStretch),
            JoinGap = JoinGapTolerancePx / pen.MaxStretch,
            Dash = pen.Carried ? DashInPenUnits(state) : null,
            DashPhase = state.DashPhase,
        };

        var et = new EdgeTable();
        foreach (var run in DeviceRuns(dp))
        {
            var penRun = new StrokeRun(run.Closed);
            foreach (var p in run.Points) penRun.Add(pen.ToPen(p));
            foreach (var polygon in StrokeRunOutline(penRun, stroke))
                AddPenPolygon(et, polygon, pen);
        }

        var a = (byte)Math.Round(Compat.Clamp(state.StrokeAlpha, 0, 1) * 255);
        ScanlineFiller.Fill(et, dp.ctx.Pixels, dp.ctx.PixelW, dp.ctx.PixelH,
            dp.r, dp.g, dp.b, a, false, dp.clip, dp.ctx.CurrentBlendMode,
            knockout: dp.ctx.IsKnockoutGroup, softMask: dp.ctx.SoftMaskAlpha);
        return true;
    }

    /// <summary>How many pieces a full circle of this device radius is cut into.</summary>
    private static int ArcStepsFor(double radiusPx)
    {
        if (radiusPx <= ArcTolerancePx) return MinArcSteps;
        var steps = (int)Math.Ceiling(Math.PI / Math.Acos(1 - ArcTolerancePx / radiusPx));
        return Compat.Clamp(steps, MinArcSteps, MaxArcSteps);
    }

    /// <summary>The dash pattern in pen units (the user space the pen is measured in), or
    /// null for a solid stroke.</summary>
    private static double[]? DashInPenUnits(GraphicsState state)
    {
        if (PenGeometry.DashPatternInPenWidths(state.DashArray, state.LineWidth, state.LineCap) is not { } widths)
            return null;
        var w = state.LineWidth > 0 ? state.LineWidth : 1;
        var pattern = new double[widths.Length];
        for (var i = 0; i < widths.Length; i++) pattern[i] = widths[i] * w;
        return pattern;
    }

    /// <summary>Map a pen-space polygon to the page, wind it positively so that the
    /// nonzero rule paints the union of every piece, and add its edges.</summary>
    private static void AddPenPolygon(EdgeTable et, List<(double X, double Y)> polygon, PenMap pen)
    {
        if (polygon.Count < 3) return;
        var dev = new (double X, double Y)[polygon.Count];
        var area = 0.0;
        for (var i = 0; i < polygon.Count; i++) dev[i] = pen.ToDevice(polygon[i].X, polygon[i].Y);
        for (var i = 0; i < dev.Length; i++)
        {
            var (x0, y0) = dev[i];
            var (x1, y1) = dev[(i + 1) % dev.Length];
            area += x0 * y1 - x1 * y0;
        }

        if (area < 0) Array.Reverse(dev);
        for (var i = 0; i < dev.Length; i++)
        {
            var (x0, y0) = dev[i];
            var (x1, y1) = dev[(i + 1) % dev.Length];
            et.AddLine(x0, y0, x1, y1);
        }
    }

    /// <summary>The path's subpaths in device pixels, curves flattened the way the
    /// segment emitter flattens them, and whether each one closes.</summary>
    private static List<StrokeRun> DeviceRuns(DrawPathState dp)
    {
        var runs = new List<StrokeRun>();
        StrokeRun? current = null;
        (double X, double Y) start = default;
        void Begin((double X, double Y) p)
        {
            current = new StrokeRun(false);
            current.Add(p);
            runs.Add(current);
            start = p;
        }

        void LineTo(double x0, double y0, double x1, double y1)
        {
            if (current is null) Begin((x0, y0));
            current!.Add((x1, y1));
        }

        foreach (var seg in dp.segments)
        {
            switch (seg.Op)
            {
                case PathOp.MoveTo:
                    Begin(Transform(dp, seg.X1, seg.Y1));
                    break;
                case PathOp.LineTo:
                    var (lx, ly) = Transform(dp, seg.X1, seg.Y1);
                    LineTo(current?.Last.X ?? lx, current?.Last.Y ?? ly, lx, ly);
                    break;
                case PathOp.Close:
                    if (current is null) break;
                    current.Closed = true;
                    Begin(start);
                    break;
                case PathOp.Rect:
                    AddRectRun(dp, seg, runs);
                    current = null;
                    break;
                default:
                    AddCurveRun(dp, seg, current, LineTo);
                    break;
            }
        }

        return runs;
    }

    /// <summary>A rectangle is a closed subpath of its four corners.</summary>
    private static void AddRectRun(DrawPathState dp, PathCommand seg, List<StrokeRun> runs)
    {
        var rect = new StrokeRun(true);
        rect.Add(Transform(dp, seg.X1, seg.Y1));
        rect.Add(Transform(dp, seg.X1 + seg.X2, seg.Y1));
        rect.Add(Transform(dp, seg.X1 + seg.X2, seg.Y1 + seg.Y2));
        rect.Add(Transform(dp, seg.X1, seg.Y1 + seg.Y2));
        runs.Add(rect);
    }

    /// <summary>Flatten one Bezier segment (c, v or y) onto the current subpath.</summary>
    private static void AddCurveRun(DrawPathState dp, PathCommand seg, StrokeRun? current,
        Action<double, double, double, double> lineTo)
    {
        if (current is null) return;
        var (sx, sy) = current.Last;
        switch (seg.Op)
        {
            case PathOp.CurveTo:
                var (c1x, c1y) = Transform(dp, seg.X1, seg.Y1);
                var (c2x, c2y) = Transform(dp, seg.X2, seg.Y2);
                var (c3x, c3y) = Transform(dp, seg.X3, seg.Y3);
                StrokeCubic(dp.ctx, sx, sy, c1x, c1y, c2x, c2y, c3x, c3y, lineTo);
                break;
            case PathOp.CurveToV:
                // First control point coincides with current point.
                var (v2x, v2y) = Transform(dp, seg.X1, seg.Y1);
                var (v3x, v3y) = Transform(dp, seg.X2, seg.Y2);
                StrokeCubic(dp.ctx, sx, sy, sx, sy, v2x, v2y, v3x, v3y, lineTo);
                break;
            case PathOp.CurveToY:
                // Second control point coincides with the endpoint, stored in X2/Y2.
                var (y1x, y1y) = Transform(dp, seg.X1, seg.Y1);
                var (y3x, y3y) = Transform(dp, seg.X2, seg.Y2);
                StrokeCubic(dp.ctx, sx, sy, y1x, y1y, y3x, y3y, y3x, y3y, lineTo);
                break;
        }
    }
}
