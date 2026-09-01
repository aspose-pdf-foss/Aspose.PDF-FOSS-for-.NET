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

    private static void DrawPath(RenderContext ctx, IReadOnlyList<PathCommand> segments,
        string op, GraphicsState state)
    {
        if (segments.Count == 0) return;

        bool doFill = op is "f" or "F" or "f*" or "B" or "B*" or "b" or "b*";
        bool doStroke = op is "S" or "s" or "B" or "B*" or "b" or "b*";
        bool evenOdd = op is "f*" or "B*" or "b*";

        if (!doFill && !doStroke) return;
        if (!PathGeometrySane(segments, state.Ctm, ctx)) return;

        // Inherit the active blend mode for the fill/stroke pixels.
        ctx.CurrentBlendMode = state.BlendMode;
        ctx.SoftMaskAlpha = state.SoftMask is { } sm__ ? ResolveSoftMaskAlpha(ctx, sm__) : null;

        var ctm = state.Ctm;

        // Transform point from PDF user space to pixel coordinates
        void Transform(double x, double y, out double px, out double py)
        {
            var tx = ctm[0] * x + ctm[2] * y + ctm[4];
            var ty = ctm[1] * x + ctm[3] * y + ctm[5];
            px = (tx - ctx.MediaBox.LLX) * ctx.Scale;
            py = ctx.PixelH - (ty - ctx.MediaBox.LLY) * ctx.Scale;
        }

        // One builder for the fill, the clip and nothing else: this used to be an inline
        // copy of BuildPathEdgeTable and the two had already drifted apart, which is exactly
        // what the shared helper exists to prevent.
        var edgeTable = BuildPathEdgeTable(segments, ctm, ctx);

        // Any active W/W* clip constrains the fill / stroke to its stencil.
        var clip = state.ClipMask;

        if (doFill)
        {
            if (state.FillPatternName is { } patName)
            {
                // Pattern fill: build a stencil from the path and paint the pattern's
                // content stream through it instead of a solid RGBA blit.
                FillWithPattern(ctx, edgeTable, evenOdd, patName, state);
            }
            else
            {
                var r = (byte)(state.FillR * 255);
                var g = (byte)(state.FillG * 255);
                var b = (byte)(state.FillB * 255);
                ScanlineFiller.Fill(edgeTable, ctx.Pixels, ctx.PixelW, ctx.PixelH,
                    r, g, b, (byte)(state.FillAlpha * 255), evenOdd, clip, state.BlendMode,
                    knockout: ctx.IsKnockoutGroup, softMask: ctx.SoftMaskAlpha);
            }
        }

        if (doStroke)
        {
            var r = (byte)(state.StrokeR * 255);
            var g = (byte)(state.StrokeG * 255);
            var b = (byte)(state.StrokeB * 255);
            // Line width `w` is in user space (PDF 32000 §8.4.3.2). It must be transformed
            // through the CTM into device space before converting to pixels. Using ctx.Scale
            // alone ignores the CTM and produces grossly thick strokes on content streams
            // with sub-unit `cm` scaling (e.g. ACORD forms with `0.12 cm` → 8× too thick).
            var ctmScale = Math.Sqrt(Math.Abs(ctm[0] * ctm[3] - ctm[1] * ctm[2]));
            var lw = state.LineWidth * ctmScale * ctx.Scale;
            if (lw < 1) lw = 1;

            // Dash pattern (PDF 32000 §8.4.3.6). The array is in USER units; each element
            // is clamped to a minimum of the LINE WIDTH before scaling - the measured
            // dash law (rendered
            // pattern[i] = max(dashArray[i], lineWidth), zero elements included, so [0 3]
            // draws as [3 3], not as vanishing dots). The GDI+ renderer has carried this
            // for a while; the software stroke path drew every dashed trail SOLID - a
            // map of dotted footpaths came out as unbroken lines.
            double[]? dashPx = null;
            var dashOn = true; var dashIdx = 0; var dashPos = 0.0;
            if (state.DashArray is { Length: > 0 } da)
            {
                dashPx = new double[da.Length];
                var total = 0.0;
                for (var di = 0; di < da.Length; di++)
                {
                    var el = Math.Max(da[di], state.LineWidth) * ctmScale * ctx.Scale;
                    dashPx[di] = Math.Max(el, 1.0);
                    total += dashPx[di];
                }
                if (total <= 0) dashPx = null;
                else
                {
                    // Consume the phase cyclically to find the starting element and the
                    // offset inside it; the pattern starts ON at element 0 (§8.4.3.6).
                    var phase = Math.Max(0, state.DashPhase) * ctmScale * ctx.Scale % total;
                    while (phase >= dashPx[dashIdx])
                    {
                        phase -= dashPx[dashIdx];
                        dashIdx = (dashIdx + 1) % dashPx.Length;
                        dashOn = !dashOn;
                    }
                    dashPos = phase;
                }
            }

            // Every stroked segment of the path goes through here, so the dash walk keeps
            // its position ACROSS segments - a polyline dashes on around its corners
            // instead of restarting at every vertex.
            void EmitStroke(double ex0, double ey0, double ex1, double ey1)
            {
                if (dashPx is null)
                {
                    ScanlineFiller.StrokeLine(ctx.Pixels, ctx.PixelW, ctx.PixelH,
                        ex0, ey0, ex1, ey1, r, g, b, 255, lw, clip,
                        blendMode: ctx.CurrentBlendMode, knockout: ctx.IsKnockoutGroup, softMask: ctx.SoftMaskAlpha);
                    return;
                }
                var segDx = ex1 - ex0; var segDy = ey1 - ey0;
                var segLen = Math.Sqrt(segDx * segDx + segDy * segDy);
                if (segLen <= 1e-12) return;
                var ux = segDx / segLen; var uy = segDy / segLen;
                var t = 0.0;
                while (t < segLen)
                {
                    var take = Math.Min(dashPx[dashIdx] - dashPos, segLen - t);
                    if (dashOn && take > 0)
                        ScanlineFiller.StrokeLine(ctx.Pixels, ctx.PixelW, ctx.PixelH,
                            ex0 + ux * t, ey0 + uy * t, ex0 + ux * (t + take), ey0 + uy * (t + take),
                            r, g, b, 255, lw, clip,
                            blendMode: ctx.CurrentBlendMode, knockout: ctx.IsKnockoutGroup, softMask: ctx.SoftMaskAlpha);
                    t += take;
                    dashPos += take;
                    if (dashPos >= dashPx[dashIdx] - 1e-9)
                    {
                        dashIdx = (dashIdx + 1) % dashPx.Length;
                        dashPos = 0;
                        dashOn = !dashOn;
                    }
                }
            }

            // Stroke each line segment of the path
            double sx = 0, sy = 0, stX = 0, stY = 0;
            foreach (var seg in segments)
            {
                switch (seg.Op)
                {
                    case PathOp.MoveTo:
                        Transform(seg.X1, seg.Y1, out sx, out sy);
                        stX = sx; stY = sy;
                        break;
                    case PathOp.LineTo:
                        Transform(seg.X1, seg.Y1, out var slx, out var sly);
                        EmitStroke(sx, sy, slx, sly);
                        sx = slx; sy = sly;
                        break;
                    case PathOp.Close:
                        EmitStroke(sx, sy, stX, stY);
                        sx = stX; sy = stY;
                        break;
                    // Cubic bezier control points all live in user space — transform each
                    // through the CTM, then flatten in device space so the per-segment
                    // tolerance is in pixels (not user units, which can be sub-pixel after
                    // a small `cm` scale).
                    case PathOp.CurveTo:
                        Transform(seg.X1, seg.Y1, out var c1x, out var c1y);
                        Transform(seg.X2, seg.Y2, out var c2x, out var c2y);
                        Transform(seg.X3, seg.Y3, out var c3x, out var c3y);
                        StrokeCubic(ctx, sx, sy, c1x, c1y, c2x, c2y, c3x, c3y, EmitStroke);
                        sx = c3x; sy = c3y;
                        break;
                    case PathOp.CurveToV:
                        // First control point coincides with current point.
                        Transform(seg.X1, seg.Y1, out var v2x, out var v2y);
                        Transform(seg.X2, seg.Y2, out var v3x, out var v3y);
                        StrokeCubic(ctx, sx, sy, sx, sy, v2x, v2y, v3x, v3y, EmitStroke);
                        sx = v3x; sy = v3y;
                        break;
                    case PathOp.CurveToY:
                        // Second control point coincides with endpoint; the `y`
                        // operator stores its endpoint in X2/Y2.
                        Transform(seg.X1, seg.Y1, out var y1x, out var y1y);
                        Transform(seg.X2, seg.Y2, out var y3x, out var y3y);
                        StrokeCubic(ctx, sx, sy, y1x, y1y, y3x, y3y, y3x, y3y, EmitStroke);
                        sx = y3x; sy = y3y;
                        break;
                    case PathOp.Rect:
                        Transform(seg.X1, seg.Y1, out var sr1, out var sr2);
                        Transform(seg.X1 + seg.X2, seg.Y1, out var sr3, out var sr4);
                        Transform(seg.X1 + seg.X2, seg.Y1 + seg.Y2, out var sr5, out var sr6);
                        Transform(seg.X1, seg.Y1 + seg.Y2, out var sr7, out var sr8);
                        EmitStroke(sr1, sr2, sr3, sr4);
                        EmitStroke(sr3, sr4, sr5, sr6);
                        EmitStroke(sr5, sr6, sr7, sr8);
                        EmitStroke(sr7, sr8, sr1, sr2);
                        sx = sr1; sy = sr2;
                        stX = sr1; stY = sr2;
                        break;
                }
            }
        }
    }

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
        // When the path AABB already contains the whole viewport and there's no outer
        // clip, the W operator is a no-op — skip the per-call 8.4MB mask allocation.
        if (state.ClipMask is null && PathBBoxCoversViewport(segments, state.Ctm, ctx))
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

    // Compute the pixel-space AABB of a path in the active CTM. Returns true when
    // the AABB contains (0,0)..(PixelW,PixelH) with a small margin — i.e. the clip
    // is a viewport-covering rectangle that would produce an all-255 mask.
    private static bool PathBBoxCoversViewport(IReadOnlyList<PathCommand> segments,
        double[] ctm, RenderContext ctx)
    {
        PathDeviceBounds(segments, ctm, ctx, out var pxLo, out var pyLo, out var pxHi, out var pyHi);
        return pxLo <= 0 && pxHi >= ctx.PixelW && pyLo <= 0 && pyHi >= ctx.PixelH;
    }

    /// <summary>Pixel-space axis-aligned bounds of a path under the active CTM.</summary>
    private static void PathDeviceBounds(IReadOnlyList<PathCommand> segments, double[] ctm,
        RenderContext ctx, out double pxLo, out double pyLo, out double pxHi, out double pyHi)
    {
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
        PathDeviceBounds(segments, ctm, ctx, out var lo0, out var lo1, out var hi0, out var hi1);
        if (hi0 < lo0) return true;   // no points contributed — nothing to paint
        var sanity = Math.Max(1e5, Math.Max(ctx.PixelW, ctx.PixelH) * 64.0);
        return double.IsFinite(lo0) && double.IsFinite(lo1) && double.IsFinite(hi0) && double.IsFinite(hi1)
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

        void Transform(double x, double y, out double px, out double py)
        {
            var tx = ctm[0] * x + ctm[2] * y + ctm[4];
            var ty = ctm[1] * x + ctm[3] * y + ctm[5];
            px = (tx - ctx.MediaBox.LLX) * ctx.Scale;
            py = ctx.PixelH - (ty - ctx.MediaBox.LLY) * ctx.Scale;
        }

        foreach (var seg in segments)
        {
            switch (seg.Op)
            {
                case PathOp.MoveTo:
                    CloseOpenSubpath();
                    Transform(seg.X1, seg.Y1, out curX, out curY);
                    startX = curX; startY = curY;
                    break;
                case PathOp.LineTo:
                    Transform(seg.X1, seg.Y1, out var lx, out var ly);
                    et.AddLine(curX, curY, lx, ly);
                    curX = lx; curY = ly; subpathOpen = true;
                    break;
                case PathOp.CurveTo:
                    Transform(seg.X1, seg.Y1, out var c1x, out var c1y);
                    Transform(seg.X2, seg.Y2, out var c2x, out var c2y);
                    Transform(seg.X3, seg.Y3, out var c3x, out var c3y);
                    et.AddCubicBezier(curX, curY, c1x, c1y, c2x, c2y, c3x, c3y);
                    curX = c3x; curY = c3y; subpathOpen = true;
                    break;
                case PathOp.CurveToV:
                    Transform(seg.X1, seg.Y1, out var v2x, out var v2y);
                    Transform(seg.X2, seg.Y2, out var v3x, out var v3y);
                    et.AddCubicBezier(curX, curY, curX, curY, v2x, v2y, v3x, v3y);
                    curX = v3x; curY = v3y; subpathOpen = true;
                    break;
                case PathOp.CurveToY: // the `y` operator stores its endpoint in X2/Y2
                    Transform(seg.X1, seg.Y1, out var y1x, out var y1y);
                    Transform(seg.X2, seg.Y2, out var y3x, out var y3y);
                    et.AddCubicBezier(curX, curY, y1x, y1y, y3x, y3y, y3x, y3y);
                    curX = y3x; curY = y3y; subpathOpen = true;
                    break;
                case PathOp.Rect:
                    Transform(seg.X1, seg.Y1, out var rx, out var ry);
                    Transform(seg.X1 + seg.X2, seg.Y1, out var rx2, out var ry2);
                    Transform(seg.X1 + seg.X2, seg.Y1 + seg.Y2, out var rx3, out var ry3);
                    Transform(seg.X1, seg.Y1 + seg.Y2, out var rx4, out var ry4);
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
