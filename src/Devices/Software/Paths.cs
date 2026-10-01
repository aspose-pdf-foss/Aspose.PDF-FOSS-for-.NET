using System.Runtime.InteropServices;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Devices.Rasterizer;
using Aspose.Pdf.IO;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Text;
namespace Aspose.Pdf.Devices;

public sealed partial class SoftwarePageRenderer : IPageRenderer
{
    // ── Path rendering ──────────────────────────────────────────────

    /// <summary>
    /// Stroke a cubic Bezier by flattening it into short line segments, then drawing
    /// each segment. Without this, strokes of curves rendered as a single line from
    /// start to endpoint (e.g. a 4-curve circle stroked as a diamond). De Casteljau
    /// subdivision at t=0.5; segment tolerance is 0.5px in device space, capped at
    /// depth 16. All inputs are in pixel coords.
    /// </summary>
    private static void StrokeCubic(RenderContext ctx,
        double x0, double y0, double cx1, double cy1, double cx2, double cy2, double x3, double y3,
        Action<double, double, double, double> emitLine, int depth = 0)
    {
        // Flatness check: if both control points lie close to the chord (x0,y0)→(x3,y3),
        // treat the bezier as a straight segment. d = perpendicular distance to the chord;
        // squared form avoids sqrt. Threshold 0.25·L² ≡ |d1|+|d2| ≤ 0.5·L which is the same
        // as EdgeTable.FlattenCubic uses for fills, keeping fill and stroke shapes consistent.
        var dx = x3 - x0;
        var dy = y3 - y0;
        var d1 = Math.Abs((cx1 - x3) * dy - (cy1 - y3) * dx);
        var d2 = Math.Abs((cx2 - x3) * dy - (cy2 - y3) * dx);
        var denom = dx * dx + dy * dy;
        if (depth >= 16 || (d1 + d2) * (d1 + d2) <= 0.25 * denom || denom < 0.001)
        {
            emitLine(x0, y0, x3, y3);
            return;
        }

        var mx01 = (x0 + cx1) * 0.5; var my01 = (y0 + cy1) * 0.5;
        var mx12 = (cx1 + cx2) * 0.5; var my12 = (cy1 + cy2) * 0.5;
        var mx23 = (cx2 + x3) * 0.5; var my23 = (cy2 + y3) * 0.5;
        var mx012 = (mx01 + mx12) * 0.5; var my012 = (my01 + my12) * 0.5;
        var mx123 = (mx12 + mx23) * 0.5; var my123 = (my12 + my23) * 0.5;
        var mx0123 = (mx012 + mx123) * 0.5; var my0123 = (my012 + my123) * 0.5;

        StrokeCubic(ctx, x0, y0, mx01, my01, mx012, my012, mx0123, my0123, emitLine, depth + 1);
        StrokeCubic(ctx, mx0123, my0123, mx123, my123, mx23, my23, x3, y3, emitLine, depth + 1);
    }

    /// <summary>
    /// Build a clip-path stencil from the current path and AND it into the enclosing
    /// <see cref="GraphicsState.ClipMask"/> — implements PDF 32000 §8.5.4.1 ("the
    /// current clipping path is set to the intersection of the current clipping path
    /// and the current path"). Called for the <c>W</c> / <c>W*</c> operators *after*
    /// the same path has been painted, so the mask only affects subsequent content.
    /// </summary>
    private static void InstallClipFromPath(RenderContext ctx, IReadOnlyList<PathCommand> segments,
        GraphicsState state, bool evenOdd)
    {
        if (segments.Count == 0) return;

        // Fast path: content streams regularly emit viewport-covering clips (e.g. a
        // full-page `W` wrapping every text block, a common PDF-generator idiom).
        // When the path is one rectangle that already contains the whole viewport and
        // there's no outer clip, the W operator is a no-op — skip the per-call 8.4MB mask
        // allocation. Only a lone axis-aligned rectangle qualifies: a page-sized frame
        // with holes cut in it, or a circle whose box covers the page, has covering
        // bounds and still clips.
        if (state.ClipMask is null && IsViewportCoveringRectangle(segments, state.Ctm, ctx))
            return;

        // A clip built from corrupt geometry would erase everything that follows it;
        // GDI+ likewise treats a clip whose bounds dwarf the page as no clip at all.
        if (!PathGeometrySane(segments, state.Ctm, ctx)) return;

        var edgeTable = BuildPathEdgeTable(segments, state.Ctm, ctx);
        var mask = new byte[ctx.PixelW * ctx.PixelH];
        ScanlineFiller.BuildMask(edgeTable, mask, ctx.PixelW, ctx.PixelH, evenOdd);

        // Intersect with any existing clip so nested W / W* keeps tightening rather
        // than replacing the outer clip wholesale.
        if (state.ClipMask is { } outer)
        {
            for (var i = 0; i < mask.Length; i++)
                mask[i] = (byte)(mask[i] & outer[i]);
        }

        state.ClipMask = mask;
        ctx.ClipMask = mask;
    }

    /// <summary>The most corners a rectangle drawn as `m l l l l h` names.</summary>
    private const int RectangleCornerOps = 5;

    /// <summary>How far, in device pixels, a corner may sit off the rectangle's edges.</summary>
    private const double RectangleCornerTolerancePx = 1e-6;

    /// <summary>True when the path is a single axis-aligned rectangle on the page whose
    /// area contains (0,0)..(PixelW,PixelH) — a clip that would produce an all-255 mask.</summary>
    private static bool IsViewportCoveringRectangle(IReadOnlyList<PathCommand> segments,
        double[] ctm, RenderContext ctx)
    {
        var starts = 0;
        var corners = 0;
        foreach (var seg in segments)
        {
            switch (seg.Op)
            {
                case PathOp.Rect: starts++; corners += RectangleCornerOps; break;
                case PathOp.MoveTo: starts++; corners++; break;
                case PathOp.LineTo: corners++; break;
                case PathOp.Close: break;
                default: return false;
            }
        }

        if (starts != 1 || corners > RectangleCornerOps) return false;
        var (pxLo, pyLo, pxHi, pyHi) = PathDeviceBounds(segments, ctm, ctx);
        if (!(pxLo <= 0 && pxHi >= ctx.PixelW && pyLo <= 0 && pyHi >= ctx.PixelH)) return false;
        return CornersOnBounds(segments, ctm, ctx, (pxLo, pyLo, pxHi, pyHi));
    }

    /// <summary>Every point of the path lies on a corner column and a corner row of its
    /// bounds - an axis-aligned rectangle, not a rotated one.</summary>
    private static bool CornersOnBounds(IReadOnlyList<PathCommand> segments, double[] ctm, RenderContext ctx,
        (double Lo, double LoY, double Hi, double HiY) b)
    {
        bool On(double v, double lo, double hi) =>
            Math.Abs(v - lo) <= RectangleCornerTolerancePx || Math.Abs(v - hi) <= RectangleCornerTolerancePx;
        bool Corner(double x, double y)
        {
            var tx = ctm[0] * x + ctm[2] * y + ctm[4];
            var ty = ctm[1] * x + ctm[3] * y + ctm[5];
            var px = (tx - ctx.MediaBox.LLX) * ctx.Scale;
            var py = ctx.PixelH - (ty - ctx.MediaBox.LLY) * ctx.Scale;
            return On(px, b.Lo, b.Hi) && On(py, b.LoY, b.HiY);
        }

        foreach (var seg in segments)
        {
            var ok = seg.Op switch
            {
                PathOp.Rect => Corner(seg.X1, seg.Y1) && Corner(seg.X1 + seg.X2, seg.Y1)
                    && Corner(seg.X1 + seg.X2, seg.Y1 + seg.Y2) && Corner(seg.X1, seg.Y1 + seg.Y2),
                PathOp.MoveTo or PathOp.LineTo => Corner(seg.X1, seg.Y1),
                _ => true,
            };
            if (!ok) return false;
        }

        return true;
    }

    /// <summary>Pixel-space axis-aligned bounds of a path under the active CTM.</summary>
    private static (double pxLo, double pyLo, double pxHi, double pyHi) PathDeviceBounds(IReadOnlyList<PathCommand> segments, double[] ctm, RenderContext ctx)
    {
        double pxLo = default;
        double pyLo = default;
        double pxHi = default;
        double pyHi = default;
        double lx = double.MaxValue, ly = double.MaxValue, hx = double.MinValue, hy = double.MinValue;

        void Visit(double x, double y)
        {
            var tx = ctm[0] * x + ctm[2] * y + ctm[4];
            var ty = ctm[1] * x + ctm[3] * y + ctm[5];
            var px = (tx - ctx.MediaBox.LLX) * ctx.Scale;
            var py = ctx.PixelH - (ty - ctx.MediaBox.LLY) * ctx.Scale;
            if (px < lx) lx = px; if (px > hx) hx = px;
            if (py < ly) ly = py; if (py > hy) hy = py;
        }

        foreach (var seg in segments)
        {
            switch (seg.Op)
            {
                case PathOp.MoveTo:
                case PathOp.LineTo:
                    Visit(seg.X1, seg.Y1); break;
                case PathOp.CurveTo:
                    Visit(seg.X1, seg.Y1); Visit(seg.X2, seg.Y2); Visit(seg.X3, seg.Y3); break;
                case PathOp.CurveToV:
                case PathOp.CurveToY:
                    Visit(seg.X1, seg.Y1); Visit(seg.X2, seg.Y2); break;
                case PathOp.Rect:
                    Visit(seg.X1, seg.Y1);
                    Visit(seg.X1 + seg.X2, seg.Y1 + seg.Y2); break;
                // Close contributes no new point
            }
        }
        pxLo = lx; pyLo = ly; pxHi = hx; pyHi = hy;
        return (pxLo, pyLo, pxHi, pyHi);
    }

    /// <summary>
    /// Corrupt-geometry tolerance, the same contract <see cref="GdiPlusPageRenderer"/>
    /// applies before it paints: a path whose device bounds are non-finite or
    /// astronomically beyond the page comes from a damaged content stream (an inflate
    /// that desynced re-emits run-together coordinates), not from an author, and
    /// painting it smears connecting lines across the whole page. Treat such an op as
    /// if it were absent. The threshold matches the GDI+ side exactly so the two
    /// renderers keep or drop the same paths.
    /// </summary>
    private static bool PathGeometrySane(IReadOnlyList<PathCommand> segments, double[] ctm, RenderContext ctx)
    {
        var (lo0, lo1, hi0, hi1) = PathDeviceBounds(segments, ctm, ctx);
        if (hi0 < lo0) return true;   // no points contributed — nothing to paint
        var sanity = Math.Max(1e5, Math.Max(ctx.PixelW, ctx.PixelH) * 64.0);
        return Compat.IsFinite(lo0) && Compat.IsFinite(lo1) && Compat.IsFinite(hi0) && Compat.IsFinite(hi1)
            && Math.Abs(lo0) <= sanity && Math.Abs(lo1) <= sanity
            && Math.Abs(hi0) <= sanity && Math.Abs(hi1) <= sanity;
    }

    /// <summary>
    /// Flatten a content-stream path into an <see cref="EdgeTable"/> in pixel coords.
    /// Shared between <see cref="DrawPath"/> and <see cref="InstallClipFromPath"/> so
    /// clip-path geometry matches the painted-path geometry exactly.
    /// </summary>
    private static EdgeTable BuildPathEdgeTable(IReadOnlyList<PathCommand> segments,
        double[] ctm, RenderContext ctx)
    {
        var et = new EdgeTable();
        double curX = 0, curY = 0, startX = 0, startY = 0;
        // PDF 32000 §8.5.3.1: f, f*, B, b and the W clip all close every open subpath
        // before applying the fill rule - `h` is only needed when the STROKE wants a join
        // there. Leaving the closing edge out cost a scanline filler the whole polygon:
        // an "m l l l f" rectangle (which is what a form-field background is) came out
        // with three edges and painted nothing at all. This table is only ever consumed
        // by a fill or a clip; the stroke pass walks the segments itself.
        var subpathOpen = false;
        void CloseOpenSubpath()
        {
            if (!subpathOpen) return;
            if (curX != startX || curY != startY) et.AddLine(curX, curY, startX, startY);
            subpathOpen = false;
        }

        (double px, double py) Transform(double x, double y)
        {
            var tx = ctm[0] * x + ctm[2] * y + ctm[4];
            var ty = ctm[1] * x + ctm[3] * y + ctm[5];
            return ((tx - ctx.MediaBox.LLX) * ctx.Scale, ctx.PixelH - (ty - ctx.MediaBox.LLY) * ctx.Scale);
        }

        foreach (var seg in segments)
        {
            switch (seg.Op)
            {
                case PathOp.MoveTo:
                    CloseOpenSubpath();
                    (curX, curY) = Transform(seg.X1, seg.Y1);
                    startX = curX; startY = curY;
                    break;
                case PathOp.LineTo:
                    var (lx, ly) = Transform(seg.X1, seg.Y1);
                    et.AddLine(curX, curY, lx, ly);
                    curX = lx; curY = ly; subpathOpen = true;
                    break;
                case PathOp.CurveTo:
                    var (c1x, c1y) = Transform(seg.X1, seg.Y1);
                    var (c2x, c2y) = Transform(seg.X2, seg.Y2);
                    var (c3x, c3y) = Transform(seg.X3, seg.Y3);
                    et.AddCubicBezier(curX, curY, c1x, c1y, c2x, c2y, c3x, c3y);
                    curX = c3x; curY = c3y; subpathOpen = true;
                    break;
                case PathOp.CurveToV:
                    var (v2x, v2y) = Transform(seg.X1, seg.Y1);
                    var (v3x, v3y) = Transform(seg.X2, seg.Y2);
                    et.AddCubicBezier(curX, curY, curX, curY, v2x, v2y, v3x, v3y);
                    curX = v3x; curY = v3y; subpathOpen = true;
                    break;
                case PathOp.CurveToY: // the `y` operator stores its endpoint in X2/Y2
                    var (y1x, y1y) = Transform(seg.X1, seg.Y1);
                    var (y3x, y3y) = Transform(seg.X2, seg.Y2);
                    et.AddCubicBezier(curX, curY, y1x, y1y, y3x, y3y, y3x, y3y);
                    curX = y3x; curY = y3y; subpathOpen = true;
                    break;
                case PathOp.Rect:
                    var (rx, ry) = Transform(seg.X1, seg.Y1);
                    var (rx2, ry2) = Transform(seg.X1 + seg.X2, seg.Y1);
                    var (rx3, ry3) = Transform(seg.X1 + seg.X2, seg.Y1 + seg.Y2);
                    var (rx4, ry4) = Transform(seg.X1, seg.Y1 + seg.Y2);
                    et.AddLine(rx, ry, rx2, ry2);
                    et.AddLine(rx2, ry2, rx3, ry3);
                    et.AddLine(rx3, ry3, rx4, ry4);
                    et.AddLine(rx4, ry4, rx, ry);
                    curX = rx; curY = ry;
                    startX = rx; startY = ry;
                    subpathOpen = false;   // re re-emits all four edges itself
                    break;
                case PathOp.Close:
                    et.AddLine(curX, curY, startX, startY);
                    curX = startX; curY = startY;
                    subpathOpen = false;
                    break;
            }
        }
        CloseOpenSubpath();
        return et;
    }

    // ── Pixel operations ────────────────────────────────────────────

    // ── Resource resolution ─────────────────────────────────────────

    // ── Context ─────────────────────────────────────────────────────

}
