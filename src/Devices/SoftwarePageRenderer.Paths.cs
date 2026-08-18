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

        // Build edge table from path commands
        var edgeTable = new EdgeTable();
        double curX = 0, curY = 0;   // current point (pixel coords)
        double startX = 0, startY = 0; // subpath start

        foreach (var seg in segments)
        {
            switch (seg.Op)
            {
                case PathOp.MoveTo:
                    Transform(seg.X1, seg.Y1, out curX, out curY);
                    startX = curX;
                    startY = curY;
                    break;
                case PathOp.LineTo:
                    Transform(seg.X1, seg.Y1, out var lx, out var ly);
                    edgeTable.AddLine(curX, curY, lx, ly);
                    curX = lx; curY = ly;
                    break;
                case PathOp.CurveTo:
                    Transform(seg.X1, seg.Y1, out var c1x, out var c1y);
                    Transform(seg.X2, seg.Y2, out var c2x, out var c2y);
                    Transform(seg.X3, seg.Y3, out var c3x, out var c3y);
                    edgeTable.AddCubicBezier(curX, curY, c1x, c1y, c2x, c2y, c3x, c3y);
                    curX = c3x; curY = c3y;
                    break;
                case PathOp.CurveToV: // first control point = current point
                    Transform(seg.X1, seg.Y1, out var v2x, out var v2y);
                    Transform(seg.X2, seg.Y2, out var v3x, out var v3y);
                    edgeTable.AddCubicBezier(curX, curY, curX, curY, v2x, v2y, v3x, v3y);
                    curX = v3x; curY = v3y;
                    break;
                case PathOp.CurveToY: // second control point = endpoint; the `y`
                                      // operator stores its endpoint in X2/Y2
                    Transform(seg.X1, seg.Y1, out var y1x, out var y1y);
                    Transform(seg.X2, seg.Y2, out var y3x, out var y3y);
                    edgeTable.AddCubicBezier(curX, curY, y1x, y1y, y3x, y3y, y3x, y3y);
                    curX = y3x; curY = y3y;
                    break;
                case PathOp.Rect:
                    Transform(seg.X1, seg.Y1, out var rx, out var ry);
                    Transform(seg.X1 + seg.X2, seg.Y1, out var rx2, out var ry2);
                    Transform(seg.X1 + seg.X2, seg.Y1 + seg.Y2, out var rx3, out var ry3);
                    Transform(seg.X1, seg.Y1 + seg.Y2, out var rx4, out var ry4);
                    edgeTable.AddLine(rx, ry, rx2, ry2);
                    edgeTable.AddLine(rx2, ry2, rx3, ry3);
                    edgeTable.AddLine(rx3, ry3, rx4, ry4);
                    edgeTable.AddLine(rx4, ry4, rx, ry);
                    curX = rx; curY = ry;
                    startX = rx; startY = ry;
                    break;
                case PathOp.Close:
                    edgeTable.AddLine(curX, curY, startX, startY);
                    curX = startX; curY = startY;
                    break;
            }
        }

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
                        ScanlineFiller.StrokeLine(ctx.Pixels, ctx.PixelW, ctx.PixelH,
                            sx, sy, slx, sly, r, g, b, 255, lw, clip,
                            blendMode: ctx.CurrentBlendMode, knockout: ctx.IsKnockoutGroup, softMask: ctx.SoftMaskAlpha);
                        sx = slx; sy = sly;
                        break;
                    case PathOp.Close:
                        ScanlineFiller.StrokeLine(ctx.Pixels, ctx.PixelW, ctx.PixelH,
                            sx, sy, stX, stY, r, g, b, 255, lw, clip,
                            blendMode: ctx.CurrentBlendMode, knockout: ctx.IsKnockoutGroup, softMask: ctx.SoftMaskAlpha);
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
                        StrokeCubic(ctx, sx, sy, c1x, c1y, c2x, c2y, c3x, c3y, r, g, b, lw, clip);
                        sx = c3x; sy = c3y;
                        break;
                    case PathOp.CurveToV:
                        // First control point coincides with current point.
                        Transform(seg.X1, seg.Y1, out var v2x, out var v2y);
                        Transform(seg.X2, seg.Y2, out var v3x, out var v3y);
                        StrokeCubic(ctx, sx, sy, sx, sy, v2x, v2y, v3x, v3y, r, g, b, lw, clip);
                        sx = v3x; sy = v3y;
                        break;
                    case PathOp.CurveToY:
                        // Second control point coincides with endpoint; the `y`
                        // operator stores its endpoint in X2/Y2.
                        Transform(seg.X1, seg.Y1, out var y1x, out var y1y);
                        Transform(seg.X2, seg.Y2, out var y3x, out var y3y);
                        StrokeCubic(ctx, sx, sy, y1x, y1y, y3x, y3y, y3x, y3y, r, g, b, lw, clip);
                        sx = y3x; sy = y3y;
                        break;
                    case PathOp.Rect:
                        Transform(seg.X1, seg.Y1, out var sr1, out var sr2);
                        Transform(seg.X1 + seg.X2, seg.Y1, out var sr3, out var sr4);
                        Transform(seg.X1 + seg.X2, seg.Y1 + seg.Y2, out var sr5, out var sr6);
                        Transform(seg.X1, seg.Y1 + seg.Y2, out var sr7, out var sr8);
                        ScanlineFiller.StrokeLine(ctx.Pixels, ctx.PixelW, ctx.PixelH, sr1, sr2, sr3, sr4, r, g, b, 255, lw, clip,
                            blendMode: ctx.CurrentBlendMode, knockout: ctx.IsKnockoutGroup, softMask: ctx.SoftMaskAlpha);
                        ScanlineFiller.StrokeLine(ctx.Pixels, ctx.PixelW, ctx.PixelH, sr3, sr4, sr5, sr6, r, g, b, 255, lw, clip,
                            blendMode: ctx.CurrentBlendMode, knockout: ctx.IsKnockoutGroup, softMask: ctx.SoftMaskAlpha);
                        ScanlineFiller.StrokeLine(ctx.Pixels, ctx.PixelW, ctx.PixelH, sr5, sr6, sr7, sr8, r, g, b, 255, lw, clip,
                            blendMode: ctx.CurrentBlendMode, knockout: ctx.IsKnockoutGroup, softMask: ctx.SoftMaskAlpha);
                        ScanlineFiller.StrokeLine(ctx.Pixels, ctx.PixelW, ctx.PixelH, sr7, sr8, sr1, sr2, r, g, b, 255, lw, clip,
                            blendMode: ctx.CurrentBlendMode, knockout: ctx.IsKnockoutGroup, softMask: ctx.SoftMaskAlpha);
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
        byte r, byte g, byte b, double lw, byte[]? clip, int depth = 0)
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
            ScanlineFiller.StrokeLine(ctx.Pixels, ctx.PixelW, ctx.PixelH,
                x0, y0, x3, y3, r, g, b, 255, lw, clip,
                blendMode: ctx.CurrentBlendMode, knockout: ctx.IsKnockoutGroup, softMask: ctx.SoftMaskAlpha);
            return;
        }

        var mx01 = (x0 + cx1) * 0.5; var my01 = (y0 + cy1) * 0.5;
        var mx12 = (cx1 + cx2) * 0.5; var my12 = (cy1 + cy2) * 0.5;
        var mx23 = (cx2 + x3) * 0.5; var my23 = (cy2 + y3) * 0.5;
        var mx012 = (mx01 + mx12) * 0.5; var my012 = (my01 + my12) * 0.5;
        var mx123 = (mx12 + mx23) * 0.5; var my123 = (my12 + my23) * 0.5;
        var mx0123 = (mx012 + mx123) * 0.5; var my0123 = (my012 + my123) * 0.5;

        StrokeCubic(ctx, x0, y0, mx01, my01, mx012, my012, mx0123, my0123, r, g, b, lw, clip, depth + 1);
        StrokeCubic(ctx, mx0123, my0123, mx123, my123, mx23, my23, x3, y3, r, g, b, lw, clip, depth + 1);
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
        double pxLo = double.MaxValue, pxHi = double.MinValue;
        double pyLo = double.MaxValue, pyHi = double.MinValue;

        void Visit(double x, double y)
        {
            var tx = ctm[0] * x + ctm[2] * y + ctm[4];
            var ty = ctm[1] * x + ctm[3] * y + ctm[5];
            var px = (tx - ctx.MediaBox.LLX) * ctx.Scale;
            var py = ctx.PixelH - (ty - ctx.MediaBox.LLY) * ctx.Scale;
            if (px < pxLo) pxLo = px; if (px > pxHi) pxHi = px;
            if (py < pyLo) pyLo = py; if (py > pyHi) pyHi = py;
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
        return pxLo <= 0 && pxHi >= ctx.PixelW && pyLo <= 0 && pyHi >= ctx.PixelH;
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
                    Transform(seg.X1, seg.Y1, out curX, out curY);
                    startX = curX; startY = curY;
                    break;
                case PathOp.LineTo:
                    Transform(seg.X1, seg.Y1, out var lx, out var ly);
                    et.AddLine(curX, curY, lx, ly);
                    curX = lx; curY = ly;
                    break;
                case PathOp.CurveTo:
                    Transform(seg.X1, seg.Y1, out var c1x, out var c1y);
                    Transform(seg.X2, seg.Y2, out var c2x, out var c2y);
                    Transform(seg.X3, seg.Y3, out var c3x, out var c3y);
                    et.AddCubicBezier(curX, curY, c1x, c1y, c2x, c2y, c3x, c3y);
                    curX = c3x; curY = c3y;
                    break;
                case PathOp.CurveToV:
                    Transform(seg.X1, seg.Y1, out var v2x, out var v2y);
                    Transform(seg.X2, seg.Y2, out var v3x, out var v3y);
                    et.AddCubicBezier(curX, curY, curX, curY, v2x, v2y, v3x, v3y);
                    curX = v3x; curY = v3y;
                    break;
                case PathOp.CurveToY: // the `y` operator stores its endpoint in X2/Y2
                    Transform(seg.X1, seg.Y1, out var y1x, out var y1y);
                    Transform(seg.X2, seg.Y2, out var y3x, out var y3y);
                    et.AddCubicBezier(curX, curY, y1x, y1y, y3x, y3y, y3x, y3y);
                    curX = y3x; curY = y3y;
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
                    break;
                case PathOp.Close:
                    et.AddLine(curX, curY, startX, startY);
                    curX = startX; curY = startY;
                    break;
            }
        }
        return et;
    }

    // ── Pixel operations ────────────────────────────────────────────

    private static void SetPixel(RenderContext ctx, int x, int y, byte r, byte g, byte b, byte a)
    {
        if (x < 0 || x >= ctx.PixelW || y < 0 || y >= ctx.PixelH) return;
        // Clip-mask gate: zero in the mask means this pixel is outside the current clip
        // region (e.g. outside a pattern-filled path) and must be left untouched.
        var pxIdx = y * ctx.PixelW + x;
        if (ctx.ClipMask is { } mask && mask[pxIdx] == 0) return;
        // Soft mask (PDF 32000 §11.6.5.4): per-pixel alpha multiplied into the
        // fragment's effective alpha. Mask = 0 → pixel is fully masked, skip.
        if (ctx.SoftMaskAlpha is { } sm)
        {
            var m = sm[pxIdx];
            if (m == 0) return;
            a = (byte)((a * m + 127) / 255);
            if (a == 0) return;
        }
        var idx = pxIdx * 4;

        // Knockout group (PDF 32000 §11.4.4): compositing inside the group uses the
        // group's original transparent backdrop, not the accumulated scratch state, so
        // every fragment replaces whatever was there. Blend modes against a transparent
        // backdrop reduce to src·α per the spec's compositing equation, so dispatch is
        // skipped here too — overlapping draws inside a /K group show only the topmost.
        if (ctx.IsKnockoutGroup)
        {
            if (a == 0) return;
            ctx.Pixels[idx]     = r;
            ctx.Pixels[idx + 1] = g;
            ctx.Pixels[idx + 2] = b;
            ctx.Pixels[idx + 3] = a;
            return;
        }

        // PDF 32000 §11.3.5: apply the blend formula B(Cb, Cs) — separable per
        // channel for most modes, full-RGB-triple for HSL — then alpha-blend the
        // blended source with the destination at the source's alpha (Porter-Duff
        // source-over). The Normal-mode fast path below stays as a byte-copy so
        // the common case (no blend mode, no alpha) keeps its speed — only
        // non-Normal modes pay the dispatch cost.
        var mode = BlendModes.Parse(ctx.CurrentBlendMode);
        if (mode != Rasterizer.BlendMode.Normal)
        {
            if (a == 0) return;
            BlendModes.Blend(mode, ctx.Pixels[idx], ctx.Pixels[idx + 1], ctx.Pixels[idx + 2],
                r, g, b, out var ibr, out var ibg, out var ibb);
            byte br = (byte)ibr, bg = (byte)ibg, bb = (byte)ibb;
            if (a == 255)
            {
                ctx.Pixels[idx] = br;
                ctx.Pixels[idx + 1] = bg;
                ctx.Pixels[idx + 2] = bb;
                ctx.Pixels[idx + 3] = 255;
            }
            else
            {
                var srcAm = a / 255.0;
                var dstAm = 1.0 - srcAm;
                ctx.Pixels[idx] = (byte)(br * srcAm + ctx.Pixels[idx] * dstAm);
                ctx.Pixels[idx + 1] = (byte)(bg * srcAm + ctx.Pixels[idx + 1] * dstAm);
                ctx.Pixels[idx + 2] = (byte)(bb * srcAm + ctx.Pixels[idx + 2] * dstAm);
                ctx.Pixels[idx + 3] = 255;
            }
            return;
        }

        if (a == 255)
        {
            ctx.Pixels[idx] = r;
            ctx.Pixels[idx + 1] = g;
            ctx.Pixels[idx + 2] = b;
            ctx.Pixels[idx + 3] = 255;
        }
        else if (a > 0)
        {
            // Alpha blend
            var srcA = a / 255.0;
            var dstA = 1.0 - srcA;
            ctx.Pixels[idx] = (byte)(r * srcA + ctx.Pixels[idx] * dstA);
            ctx.Pixels[idx + 1] = (byte)(g * srcA + ctx.Pixels[idx + 1] * dstA);
            ctx.Pixels[idx + 2] = (byte)(b * srcA + ctx.Pixels[idx + 2] * dstA);
            ctx.Pixels[idx + 3] = 255;
        }
    }

    private static void FillRect(RenderContext ctx, int x, int y, int w, int h,
        byte r, byte g, byte b, byte a)
    {
        var x0 = Math.Max(0, x);
        var y0 = Math.Max(0, y);
        var x1 = Math.Min(ctx.PixelW, x + w);
        var y1 = Math.Min(ctx.PixelH, y + h);

        for (int py = y0; py < y1; py++)
        {
            for (int px = x0; px < x1; px++)
            {
                SetPixel(ctx, px, py, r, g, b, a);
            }
        }
    }

    private static void BlitRGB(RenderContext ctx, byte[] src, int srcW, int srcH,
        int dstX, int dstY, int dstW, int dstH, byte[]? alpha = null, int alphaW = 0, int alphaH = 0,
        double fillAlpha = 1.0, bool flipY = false, bool flipX = false)
    {
        // /CA /ca via the gs ExtGState arrives on state.FillAlpha; multiplied with
        // any SMask per-pixel opacity to yield the final alpha. fillAlpha=1 keeps
        // the old behaviour for callers that don't pass it.
        var fa = (int)Math.Round(fillAlpha * 255);
        if (fa < 0) fa = 0; else if (fa > 255) fa = 255;

        for (int y = 0; y < dstH; y++)
        {
            // PDF 32000 §8.9.4 image data rows are top-down. When the CTM applies
            // a vertical flip (ctm[3] < 0) the caller sets flipY so the source rows
            // are sampled bottom-up — without this, header banner images render
            // upside-down.
            var sy = flipY ? (srcH - 1 - y * srcH / dstH) : y * srcH / dstH;
            var dy = dstY + y;
            if (dy < 0 || dy >= ctx.PixelH) continue;

            for (int x = 0; x < dstW; x++)
            {
                var sx = flipX ? (srcW - 1 - x * srcW / dstW) : x * srcW / dstW;
                var dx = dstX + x;
                if (dx < 0 || dx >= ctx.PixelW) continue;

                var si = (sy * srcW + sx) * 3;
                if (si + 2 >= src.Length) continue;

                // SMask / stencil-Mask alpha (PDF 32000 §11.6.5.3, §8.9.6.3). The mask is
                // a separate image that maps to the same unit square as the base image but
                // may use a very different resolution. Sampling it at the DEST resolution
                // (with area-averaging when it is higher-res than the output) preserves
                // thin strokes that a single point-sample at the low base-image resolution
                // would drop — e.g. a 2480×3507 stencil over a 207×293 photo. For a
                // same-resolution mask this reduces to the previous point sample.
                int a = fa;
                if (alpha is not null && alphaW > 0 && alphaH > 0)
                    a = (a * SampleAlpha(alpha, alphaW, alphaH, x, y, dstW, dstH, flipX, flipY)) / 255;
                SetPixel(ctx, dx, dy, src[si], src[si + 1], src[si + 2], (byte)a);
            }
        }
    }

    private static void BlitGray(RenderContext ctx, byte[] src, int srcW, int srcH,
        int dstX, int dstY, int dstW, int dstH, byte[]? alpha = null, int alphaW = 0, int alphaH = 0,
        double fillAlpha = 1.0)
    {
        var fa = (int)Math.Round(fillAlpha * 255);
        if (fa < 0) fa = 0; else if (fa > 255) fa = 255;

        for (int y = 0; y < dstH; y++)
        {
            var sy = y * srcH / dstH;
            var dy = dstY + y;
            if (dy < 0 || dy >= ctx.PixelH) continue;

            for (int x = 0; x < dstW; x++)
            {
                var sx = x * srcW / dstW;
                var dx = dstX + x;
                if (dx < 0 || dx >= ctx.PixelW) continue;

                var si = sy * srcW + sx;
                if (si >= src.Length) continue;
                var g = src[si];

                int a = fa;
                if (alpha is not null && alphaW > 0 && alphaH > 0)
                    a = (a * SampleAlpha(alpha, alphaW, alphaH, x, y, dstW, dstH, false, false)) / 255;
                SetPixel(ctx, dx, dy, g, g, g, (byte)a);
            }
        }
    }

    /// <summary>
    /// Sample a mask/alpha plane for dest pixel (<paramref name="x"/>,<paramref name="y"/>)
    /// of a <paramref name="dstW"/>×<paramref name="dstH"/> blit. The mask maps to the same
    /// unit square as the base image, so it is sampled in dest space (mirrored when the blit
    /// flips) and area-averaged over the dest pixel's footprint — preserving thin features
    /// of a mask that is higher resolution than the output. A box footprint of at most a few
    /// samples per axis is taken to bound cost. Returns the 0..255 opacity.
    /// </summary>
    private static int SampleAlpha(byte[] alpha, int alphaW, int alphaH, int x, int y,
        int dstW, int dstH, bool flipX, bool flipY)
    {
        // Footprint of this dest pixel in the mask grid, mirrored per flip.
        long ax0 = flipX ? (long)(dstW - 1 - x) * alphaW / dstW : (long)x * alphaW / dstW;
        long ax1 = flipX ? (long)(dstW - x) * alphaW / dstW : (long)(x + 1) * alphaW / dstW;
        long ay0 = flipY ? (long)(dstH - 1 - y) * alphaH / dstH : (long)y * alphaH / dstH;
        long ay1 = flipY ? (long)(dstH - y) * alphaH / dstH : (long)(y + 1) * alphaH / dstH;
        if (ax1 <= ax0) ax1 = ax0 + 1;
        if (ay1 <= ay0) ay1 = ay0 + 1;
        // Cap the averaged footprint so heavy downscales stay cheap (sub-sampled box).
        const int MaxSpan = 8;
        long stepX = Math.Max(1, (ax1 - ax0 + MaxSpan - 1) / MaxSpan);
        long stepY = Math.Max(1, (ay1 - ay0 + MaxSpan - 1) / MaxSpan);
        long sum = 0, cnt = 0;
        for (long ay = ay0; ay < ay1; ay += stepY)
        {
            long rowBase = ay * alphaW;
            for (long ax = ax0; ax < ax1; ax += stepX)
            {
                long ai = rowBase + ax;
                if ((ulong)ai < (ulong)alpha.Length) { sum += alpha[ai]; cnt++; }
            }
        }
        return cnt > 0 ? (int)(sum / cnt) : 255;
    }

    private static void BlitCMYK(RenderContext ctx, byte[] src, int srcW, int srcH,
        int dstX, int dstY, int dstW, int dstH)
    {
        for (int y = 0; y < dstH; y++)
        {
            var sy = y * srcH / dstH;
            var dy = dstY + y;
            if (dy < 0 || dy >= ctx.PixelH) continue;

            for (int x = 0; x < dstW; x++)
            {
                var sx = x * srcW / dstW;
                var dx = dstX + x;
                if (dx < 0 || dx >= ctx.PixelW) continue;

                var si = (sy * srcW + sx) * 4;
                if (si + 3 >= src.Length) continue;
                var c = src[si];
                var m = src[si + 1];
                var yy = src[si + 2];
                var k = src[si + 3];
                // Simple CMYK→RGB: R = 255*(1-C/255)*(1-K/255)
                var r = (byte)(255 * (255 - c) * (255 - k) / 65025);
                var g = (byte)(255 * (255 - m) * (255 - k) / 65025);
                var b = (byte)(255 * (255 - yy) * (255 - k) / 65025);
                SetPixel(ctx, dx, dy, r, g, b, 255);
            }
        }
    }

    private static void BlitBilevel(RenderContext ctx, byte[] src, int srcW, int srcH,
        int dstX, int dstY, int dstW, int dstH, bool invert = false)
    {
        if (src.Length == 0 || srcW <= 0 || srcH <= 0) return;
        // Use long for the running offset — sy*rowBytes can overflow int for very
        // large (≥50k-pixel) bilevel images, which flips byteIdx negative and bypasses
        // the `>= src.Length` guard, throwing IndexOutOfRangeException on line 1813.
        // Seen on documents with many (3600) bilevel images in one PDF.
        long rowBytes = (srcW + 7) / 8;
        for (int y = 0; y < dstH; y++)
        {
            var sy = (long)y * srcH / dstH;
            var dy = dstY + y;
            if (dy < 0 || dy >= ctx.PixelH) continue;

            for (int x = 0; x < dstW; x++)
            {
                var sx = (long)x * srcW / dstW;
                var dx = dstX + x;
                if (dx < 0 || dx >= ctx.PixelW) continue;

                var byteIdx = sy * rowBytes + sx / 8;
                if (byteIdx < 0 || byteIdx >= src.Length) continue;
                var bit = ((src[byteIdx] >> (7 - (int)(sx & 7))) & 1) ^ (invert ? 1 : 0);
                // Default /Decode [0 1] for 1bpc DeviceGray: bit=1 → 1 → white,
                // bit=0 → 0 → black. (ImageMask uses the opposite convention but
                // takes the DrawImageMask branch before reaching here.) Treating
                // bit=1 as black inverts every B&W background image — that would
                // render a form-with-data PDF as light-text-on-black instead
                // of dark-text-on-white. A /Decode [1 0] entry (common on BlackIs1
                // CCITT scans) reverses the mapping — that's `invert`.
                var val = (byte)(bit == 1 ? 255 : 0);
                SetPixel(ctx, dx, dy, val, val, val, 255);
            }
        }
    }

    /// <summary>Resolve ColorSpace entry to a simple device color space name.</summary>
    private static string ResolveColorSpaceName(PdfObject? csObj, PdfReader reader)
        => ResolveImageColorSpace(csObj, reader).BaseName;

    /// <summary>Resolved image colour space: base device name and, for Indexed, the palette bytes.</summary>
    internal readonly struct ImageColorSpaceInfo
    {
        public string BaseName { get; init; }
        public byte[]? Palette { get; init; }
        public int PaletteComponents { get; init; } // bytes per palette entry (1=Gray, 3=RGB, 4=CMYK)
        // For a single-component /Separation (or 1-colorant /DeviceN) image: the tint
        // transform plus its alternate-space family, so each sample maps tint →
        // alternate components → RGB. Null for ordinary device spaces.
        public Functions.PdfFunction? TintTransform { get; init; }
        public string? AltSpaceName { get; init; }
    }

    /// <summary>
    /// Resolve a /ColorSpace entry. For Indexed, walks down to the base device space and extracts
    /// the palette lookup bytes (spec §8.6.6.3: [/Indexed base hival lookup], lookup is a string or stream).
    /// </summary>
    internal static ImageColorSpaceInfo ResolveImageColorSpace(PdfObject? csObj, PdfReader reader)
    {
        if (csObj is PdfName name)
            return new ImageColorSpaceInfo { BaseName = name.Value, PaletteComponents = ComponentsForBase(name.Value) };

        csObj = reader.Resolve(csObj);
        if (csObj is PdfName name2)
            return new ImageColorSpaceInfo { BaseName = name2.Value, PaletteComponents = ComponentsForBase(name2.Value) };

        if (csObj is PdfArray arr && arr.Count > 0)
        {
            var first = arr[0] is PdfName fn ? fn.Value : null;
            if (first == "ICCBased" && arr.Count > 1)
            {
                var profileStream = reader.ResolveStream(arr[1]);
                if (profileStream is not null)
                {
                    var n = (int)profileStream.Dict.GetInt("N");
                    var derived = n switch { 1 => "DeviceGray", 4 => "DeviceCMYK", _ => "DeviceRGB" };
                    return new ImageColorSpaceInfo { BaseName = derived, PaletteComponents = ComponentsForBase(derived) };
                }
            }
            if (first == "Indexed" && arr.Count >= 4)
            {
                var baseInfo = ResolveImageColorSpace(arr[1], reader);
                var lookupObj = reader.Resolve(arr[3]);
                var palette = ExtractPaletteBytes(lookupObj, reader);

                // A /Separation (or single-colorant /DeviceN) base stores TINT samples in
                // the palette — one byte per entry that only becomes colour through the
                // tint transform. Bake the palette to RGB here so both rasterisers'
                // palette lookups (which know Gray/RGB/CMYK layouts only) work unchanged;
                // without this the 1-byte entries are read as 3-byte RGB and the image
                // draw dies out of bounds (dropping the image from the page).
                if (baseInfo.TintTransform is not null && palette is not null)
                {
                    var lut = BuildSeparationLut(baseInfo, invert: false);
                    var rgbPalette = new byte[palette.Length * 3];
                    for (int i = 0; i < palette.Length; i++)
                    {
                        int t = palette[i] * 3;
                        rgbPalette[i * 3] = lut[t];
                        rgbPalette[i * 3 + 1] = lut[t + 1];
                        rgbPalette[i * 3 + 2] = lut[t + 2];
                    }
                    return new ImageColorSpaceInfo
                    {
                        BaseName = "DeviceRGB",
                        Palette = rgbPalette,
                        PaletteComponents = 3,
                    };
                }

                return new ImageColorSpaceInfo
                {
                    BaseName = baseInfo.BaseName,
                    Palette = palette,
                    PaletteComponents = baseInfo.PaletteComponents,
                };
            }
            // PDF 32000 §8.6.5.2-3: CalRGB / CalGray are device-space colours with
            // an attached calibration (Gamma/WhitePoint/Matrix). For raster image
            // decode they behave exactly like DeviceRGB / DeviceGray — without this
            // alias the blit dispatch below sees "CalRGB" / "CalGray" and silently
            // drops the image, producing a blank page for screenshot PDFs whose
            // single content op is "/Img0 Do".
            if (first == "CalRGB")
                return new ImageColorSpaceInfo { BaseName = "DeviceRGB", PaletteComponents = 3 };
            if (first == "CalGray")
                return new ImageColorSpaceInfo { BaseName = "DeviceGray", PaletteComponents = 1 };
            // /Separation (1 colorant) and single-colorant /DeviceN images carry a tint
            // transform that turns the stored sample into colour in an alternate space
            // (PDF 32000 §8.6.6.4). Capture it so the spot sample is rendered as the
            // spot colour, not as a raw grayscale plate.
            if ((first == "Separation" && arr.Count >= 4)
                || (first == "DeviceN" && arr.Count >= 4 && reader.Resolve(arr[1]) is PdfArray dn && dn.Count == 1))
            {
                var tint = Functions.PdfFunction.Parse(arr[3], reader);
                var alt = ResolveAltSpaceFamily(arr[2], reader);
                if (tint is not null && alt is not null)
                    return new ImageColorSpaceInfo { BaseName = "Separation", PaletteComponents = 1, TintTransform = tint, AltSpaceName = alt };
            }
            if (first is not null)
                return new ImageColorSpaceInfo { BaseName = first, PaletteComponents = ComponentsForBase(first) };
        }
        return new ImageColorSpaceInfo { BaseName = "DeviceRGB", PaletteComponents = 3 };
    }

    /// <summary>
    /// Resolve the alternate-space family of a /Separation or /DeviceN colorspace's
    /// tint output to DeviceGray/DeviceRGB/DeviceCMYK (ICCBased maps by component
    /// count). Returns null when unrecognised.
    /// </summary>
    internal static string? ResolveAltSpaceFamily(PdfObject? obj, PdfReader reader)
    {
        var resolved = reader.Resolve(obj);
        if (resolved is PdfName n)
        {
            if (n.Value is "DeviceCMYK" or "DeviceRGB" or "DeviceGray") return n.Value;
            if (n.Value == "CalGray") return "DeviceGray";
            if (n.Value == "CalRGB") return "DeviceRGB";
            return null;
        }
        if (resolved is PdfArray a && a.Count > 0 && a[0] is PdfName fam)
        {
            if (fam.Value == "ICCBased" && a.Count > 1 && reader.ResolveStream(a[1]) is { } icc)
            {
                var iccN = (int)icc.Dict.GetInt("N");
                return iccN switch { 1 => "DeviceGray", 3 => "DeviceRGB", 4 => "DeviceCMYK", _ => null };
            }
            if (fam.Value == "CalGray") return "DeviceGray";
            if (fam.Value == "CalRGB") return "DeviceRGB";
            if (fam.Value == "Lab") return "Lab";
        }
        return null;
    }

    /// <summary>
    /// Build a 256-entry RGB lookup table for a single-component /Separation image:
    /// sample byte → tint (0..1) → alternate components → RGB. <paramref name="invert"/>
    /// applies a /Decode [1 0] reversal. Returns 256×3 packed RGB bytes.
    /// </summary>
    /// <summary>
    /// Expand a 1/2/4/8-bpc single-component /Separation (or /DeviceN) image to packed RGB
    /// using a 256-entry tint LUT. Sub-byte samples are scaled to the LUT's 0..255 index
    /// range (so a 1-bpc sample of 1 maps to LUT[255] = full tint).
    /// </summary>
    internal static byte[] SeparationSamplesToRgb(byte[] data, int w, int h, int bpc, byte[] lut256)
    {
        var rgb = new byte[w * h * 3];
        var rowBytes = (w * bpc + 7) / 8;
        var maxv = (1 << bpc) - 1;
        for (int y = 0; y < h; y++)
        {
            int rowBase = y * rowBytes;
            for (int x = 0; x < w; x++)
            {
                int bitPos = x * bpc;
                int bi = rowBase + (bitPos >> 3);
                int sample = bi < data.Length ? (data[bi] >> (8 - bpc - (bitPos & 7))) & maxv : 0;
                int idx = bpc == 8 ? sample : sample * 255 / maxv;
                int o = (y * w + x) * 3;
                rgb[o] = lut256[idx * 3]; rgb[o + 1] = lut256[idx * 3 + 1]; rgb[o + 2] = lut256[idx * 3 + 2];
            }
        }
        return rgb;
    }

    internal static byte[] BuildSeparationLut(ImageColorSpaceInfo cs, bool invert)
    {
        var lut = new byte[256 * 3];
        var input = new double[1];
        for (int i = 0; i < 256; i++)
        {
            input[0] = (invert ? 255 - i : i) / 255.0;
            byte r, g, b;
            var alt = cs.TintTransform!.Evaluate(input);
            if (alt is null) { r = g = b = (byte)(255 - i); }
            else ComponentsToRgb(alt, cs.AltSpaceName ?? "DeviceGray", out r, out g, out b);
            lut[i * 3] = r; lut[i * 3 + 1] = g; lut[i * 3 + 2] = b;
        }
        return lut;
    }

    /// <summary>
    /// Resolve a page's /Resources by walking up the /Parent chain. PDF 32000 §7.7.3.4
    /// lists /Resources as one of the inheritable page attributes — pages routinely
    /// omit their own /Resources when the parent /Pages dict carries the shared font /
    /// pattern / XObject table. The depth cap protects against malformed PDFs whose
    /// /Parent chain loops back on itself.
    /// </summary>
    internal static PdfDictionary? ResolveInheritedPageResources(PdfDictionary pageDict, PdfReader reader)
    {
        var dict = pageDict;
        for (var depth = 0; dict is not null && depth < 32; depth++)
        {
            var res = reader.ResolveDict(dict.Get("Resources"));
            if (res is not null) return res;
            dict = reader.ResolveDict(dict.Get("Parent"));
        }
        return null;
    }

    private static int ComponentsForBase(string baseName) => baseName switch
    {
        "DeviceGray" or "G" or "CalGray" => 1,
        "DeviceCMYK" or "CMYK" => 4,
        _ => 3,
    };

    private static byte[]? ExtractPaletteBytes(PdfObject? lookup, PdfReader reader)
    {
        // Lookup can be a literal PdfString or a referenced stream.
        if (lookup is PdfString str) return str.Value;
        if (lookup is PdfStream ps)
        {
            try { return reader.DecodeStream(ps); }
            catch { return null; }
        }
        return null;
    }

    /// <summary>
    /// Render an Indexed image with bilinear scaling. PDF 32000-1:2008 §8.9.5.3: pixel data is
    /// packed as bpc-bit indices (rows are byte-aligned), each index is looked up in the palette
    /// to get a baseCS tuple. GDI+
    /// uses bilinear by default, so we match that — nearest-neighbour drops colour accuracy at
    /// sub-pixel boundaries even when the decoded content is correct.
    /// </summary>
    private static void BlitIndexed(RenderContext ctx, byte[] src, int srcW, int srcH,
        int dstX, int dstY, int dstW, int dstH, int bpc, ImageColorSpaceInfo csInfo)
    {
        if (csInfo.Palette is null || csInfo.PaletteComponents <= 0) return;

        // Decode once into an sRGB buffer; bilinear sampling needs random access to RGB triplets,
        // and paying the decode cost up-front beats redoing palette lookups 4× per dst pixel.
        var rgb = DecodeIndexedToRgb(src, srcW, srcH, bpc, csInfo);
        if (rgb is null) return;

        BlitRgbBilinear(ctx, rgb, srcW, srcH, dstX, dstY, dstW, dstH);
    }

    /// <summary>Unpack a palette-coded image into a flat RGB byte[]. Returns null if the lookup fails.</summary>
    private static byte[]? DecodeIndexedToRgb(byte[] src, int srcW, int srcH, int bpc,
        ImageColorSpaceInfo csInfo)
    {
        var palette = csInfo.Palette!;
        var comps = csInfo.PaletteComponents;
        var isCmyk = csInfo.BaseName == "DeviceCMYK";
        var isGray = comps == 1;
        var rowBytes = (srcW * bpc + 7) / 8;
        var paletteEntries = palette.Length / comps;
        var rgb = new byte[srcW * srcH * 3];

        for (var y = 0; y < srcH; y++)
        {
            var rowStart = y * rowBytes;
            var dstRow = y * srcW * 3;
            for (var x = 0; x < srcW; x++)
            {
                var idx = ReadPackedIndex(src, rowStart, x, bpc);
                if (idx < 0 || idx >= paletteEntries) continue;

                var pbase = idx * comps;
                if (pbase + comps > palette.Length) continue;

                byte r, g, b;
                if (isGray)
                {
                    r = g = b = palette[pbase];
                }
                else if (isCmyk && comps == 4)
                {
                    var c = palette[pbase];
                    var m = palette[pbase + 1];
                    var yy = palette[pbase + 2];
                    var k = palette[pbase + 3];
                    r = (byte)(255 * (255 - c) * (255 - k) / 65025);
                    g = (byte)(255 * (255 - m) * (255 - k) / 65025);
                    b = (byte)(255 * (255 - yy) * (255 - k) / 65025);
                }
                else
                {
                    r = palette[pbase];
                    g = palette[pbase + 1];
                    b = palette[pbase + 2];
                }
                var di = dstRow + x * 3;
                rgb[di] = r; rgb[di + 1] = g; rgb[di + 2] = b;
            }
        }
        return rgb;
    }

    /// <summary>Bilinear-sample a flat-RGB source image into the pixel buffer at dst rect.</summary>
    private static void BlitRgbBilinear(RenderContext ctx, byte[] src, int srcW, int srcH,
        int dstX, int dstY, int dstW, int dstH)
    {
        if (dstW <= 0 || dstH <= 0 || srcW <= 0 || srcH <= 0) return;

        // Sample at pixel centres (+0.5) so the edges of the dst rectangle stay inside the
        // source bounds after rounding. sx ∈ [0, srcW-1] lets us use (ix, ix+1) pairs safely.
        var xScale = (double)srcW / dstW;
        var yScale = (double)srcH / dstH;

        for (var y = 0; y < dstH; y++)
        {
            var dy = dstY + y;
            if (dy < 0 || dy >= ctx.PixelH) continue;

            var sy = (y + 0.5) * yScale - 0.5;
            if (sy < 0) sy = 0;
            if (sy > srcH - 1) sy = srcH - 1;
            var iy = (int)sy;
            var fy = sy - iy;
            var iy2 = Math.Min(iy + 1, srcH - 1);

            for (var x = 0; x < dstW; x++)
            {
                var dx = dstX + x;
                if (dx < 0 || dx >= ctx.PixelW) continue;

                var sx = (x + 0.5) * xScale - 0.5;
                if (sx < 0) sx = 0;
                if (sx > srcW - 1) sx = srcW - 1;
                var ix = (int)sx;
                var fx = sx - ix;
                var ix2 = Math.Min(ix + 1, srcW - 1);

                // Four corner samples — top-left, top-right, bottom-left, bottom-right.
                var p00 = (iy * srcW + ix) * 3;
                var p10 = (iy * srcW + ix2) * 3;
                var p01 = (iy2 * srcW + ix) * 3;
                var p11 = (iy2 * srcW + ix2) * 3;

                var w00 = (1 - fx) * (1 - fy);
                var w10 = fx * (1 - fy);
                var w01 = (1 - fx) * fy;
                var w11 = fx * fy;

                var r = (byte)(src[p00] * w00 + src[p10] * w10 + src[p01] * w01 + src[p11] * w11);
                var g = (byte)(src[p00 + 1] * w00 + src[p10 + 1] * w10 + src[p01 + 1] * w01 + src[p11 + 1] * w11);
                var b = (byte)(src[p00 + 2] * w00 + src[p10 + 2] * w10 + src[p01 + 2] * w01 + src[p11 + 2] * w11);
                SetPixel(ctx, dx, dy, r, g, b, 255);
            }
        }
    }

    /// <summary>Read the nth bpc-bit index from a byte-aligned row in <paramref name="src"/>.</summary>
    private static int ReadPackedIndex(byte[] src, int rowStart, int xIndex, int bpc)
    {
        // Fast paths for the common widths (8, 4, 1 bit) avoid bit-walking per pixel.
        if (bpc == 8)
        {
            var i = rowStart + xIndex;
            return i < src.Length ? src[i] : -1;
        }
        if (bpc == 4)
        {
            var i = rowStart + (xIndex >> 1);
            if (i >= src.Length) return -1;
            return (xIndex & 1) == 0 ? src[i] >> 4 : src[i] & 0x0F;
        }
        if (bpc == 1)
        {
            var i = rowStart + (xIndex >> 3);
            if (i >= src.Length) return -1;
            return (src[i] >> (7 - (xIndex & 7))) & 1;
        }
        if (bpc == 2)
        {
            var i = rowStart + (xIndex >> 2);
            if (i >= src.Length) return -1;
            var shift = 6 - ((xIndex & 3) << 1);
            return (src[i] >> shift) & 0x03;
        }
        return -1;
    }

    // ── Resource resolution ─────────────────────────────────────────

    internal static Dictionary<string, PdfDictionary> ResolveFontDicts(PdfDictionary? resources, PdfReader reader)
    {
        var result = new Dictionary<string, PdfDictionary>(StringComparer.Ordinal);
        if (resources is null) return result;
        var fontDict = reader.ResolveDict(resources.Get("Font"));
        if (fontDict is null) return result;
        foreach (var key in fontDict.Keys)
        {
            var fd = reader.ResolveDict(fontDict.Get(key));
            if (fd is not null) result[key] = fd;
        }
        return result;
    }

    internal static Dictionary<string, PdfDictionary>? ResolveExtGStates(PdfDictionary? resources, PdfReader reader)
    {
        if (resources is null) return null;
        var gsDict = reader.ResolveDict(resources.Get("ExtGState"));
        if (gsDict is null) return null;
        var result = new Dictionary<string, PdfDictionary>(StringComparer.Ordinal);
        foreach (var key in gsDict.Keys)
        {
            var d = reader.ResolveDict(gsDict.Get(key));
            if (d is not null) result[key] = d;
        }
        return result;
    }

    internal static Dictionary<string, PdfStream> ResolveAllXObjects(PdfDictionary? resources, PdfReader reader)
    {
        var result = new Dictionary<string, PdfStream>(StringComparer.Ordinal);
        if (resources is null) return result;
        var xobjectDict = reader.ResolveDict(resources.Get("XObject"));
        if (xobjectDict is null) return result;
        foreach (var key in xobjectDict.Keys)
        {
            var obj = reader.ResolveStream(xobjectDict.Get(key));
            if (obj is not null)
                result[key] = obj;
        }
        return result;
    }

    internal static byte[] GetPageContent(PdfDictionary pageDict, PdfReader reader)
    {
        var obj = reader.Resolve(pageDict.Get("Contents"));
        if (obj is PdfStream stream) return reader.DecodeStream(stream);
        if (obj is PdfArray arr)
        {
            using var ms = new MemoryStream();
            foreach (var item in arr)
            {
                var s = reader.ResolveStream(item);
                if (s is not null)
                {
                    var data = reader.DecodeStream(s);
                    ms.Write(data);
                    ms.WriteByte((byte)'\n');
                }
            }
            return ms.ToArray();
        }
        return [];
    }

    // ── Context ─────────────────────────────────────────────────────

    private sealed class RenderContext(byte[] pixels, int pixelW, int pixelH,
        double scale, Rectangle mediaBox, PdfReader reader)
    {
        public byte[] Pixels => pixels;
        public int PixelW => pixelW;
        public int PixelH => pixelH;
        public double Scale => scale;
        public Rectangle MediaBox => mediaBox;
        public PdfReader Reader => reader;
        public Dictionary<string, PdfStream>? AllXObjects { get; set; }
        public Dictionary<string, PdfDictionary>? FontDicts { get; set; }
        public Dictionary<string, (IGlyphOutlineSource? parser, double hScale)> FontParsers { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, CidFontInfo?> CidFontInfos { get; } = new(StringComparer.Ordinal);
        // Per-font byte→GID map built from the PDF /Encoding /Differences glyph names
        // (resolved through the embedded font's name table). null = no usable map.
        public Dictionary<string, int[]?> EncodingGidMaps { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// Page's /Resources/Pattern dict — looked up when resolving a <c>scn</c> pattern name.
        /// Propagated into child contexts (Form XObjects, pattern tiles) so nested fills resolve
        /// correctly. Null when the page has no Pattern resources.
        /// </summary>
        public PdfDictionary? Patterns { get; set; }

        /// <summary>
        /// Page / Form-XObject / Pattern /Resources/Shading dict — looked up by the
        /// <c>sh</c> operator to paint a smooth-gradient fill inside the current clip.
        /// Null means this scope has no shading resources.
        /// </summary>
        public PdfDictionary? Shadings { get; set; }

        /// <summary>
        /// Page resources /ColorSpace dictionary — Separation / DeviceN array
        /// references named in `cs`/`CS` operators. Passed through to the
        /// content stream parser so tint transforms can convert named-spot
        /// colors into the renderer's RGB pipeline. Null when the page has
        /// no named colorspaces (the common case).
        /// </summary>
        public PdfDictionary? ColorSpaces { get; set; }

        /// <summary>
        /// Page resources /Properties dict — referenced by name in BDC's
        /// second operand (<c>/OC /MC0 BDC</c>). Stored on the context so
        /// child renders (Form XObjects, patterns) can pass it through to
        /// their nested content-stream parser as well.
        /// </summary>
        public PdfDictionary? Properties { get; set; }

        /// <summary>
        /// OCG dicts (resolved instances) that the current OC config marks as
        /// hidden — i.e. content inside their <c>/OC /Name BDC … EMC</c> ranges
        /// must NOT be drawn. Built once per page from /OCProperties/D.
        /// Reference equality is fine because PdfReader caches resolved dicts.
        /// </summary>
        public HashSet<PdfDictionary>? OcgHidden { get; set; }

        /// <summary>
        /// Stack of "did this marked-content frame hide its content?". Pushed
        /// on every BMC/BDC, popped on every EMC. <see cref="IsContentHidden"/>
        /// reports true while any frame in the stack is true.
        /// </summary>
        public Stack<bool> OcgHiddenStack { get; } = new();

        /// <summary>True when the current draw operation lies inside a marked-content
        /// range belonging to an OCG flagged invisible by the OC config.</summary>
        public bool IsContentHidden
        {
            get
            {
                foreach (var hidden in OcgHiddenStack)
                    if (hidden) return true;
                return false;
            }
        }

        /// <summary>
        /// Optional 1-byte-per-pixel stencil: non-zero pixels are paintable, zero pixels are
        /// masked out. Used during tiling-pattern fill so the pattern only paints inside the
        /// current path. Null means no clipping (the normal unmasked case).
        /// </summary>
        public byte[]? ClipMask { get; set; }

        /// <summary>
        /// Active PDF 32000 §11.3.5 blend mode for the next pixel write — set by callers
        /// (DrawText / DrawPath / DrawImage / etc.) from <c>state.BlendMode</c> before each
        /// blit, read by SetPixel. "Normal" means straight Porter-Duff source-over alpha;
        /// "Multiply" applies the multiplicative blend separable formula. Other modes fall
        /// back to Normal until they're implemented.
        /// </summary>
        public string CurrentBlendMode { get; set; } = "Normal";

        /// <summary>
        /// True while this context is the scratch buffer of a knockout transparency group
        /// (PDF 32000 §11.4.4 / §11.6.6, /Group dict with /K true). Each new draw inside a
        /// knockout group composites against the group's ORIGINAL backdrop (transparent for
        /// /S /Transparency groups), not against accumulated prior draws — so overlapping
        /// elements show only the topmost. We implement that by switching pixel writes to
        /// "replace with src·alpha" instead of source-over, and by skipping blend-mode
        /// dispatch (a non-Normal blend against a transparent backdrop reduces to src*α
        /// per the spec's compositing equation). Strokes don't currently honour this flag —
        /// rare in practice, parallel to the existing stroke/blend-mode gap.
        /// </summary>
        public bool IsKnockoutGroup { get; set; }

        /// <summary>
        /// Per-pixel soft-mask alpha (PDF 32000 §11.6.5.4) installed by paint sites
        /// from the active <c>state.SoftMask</c>. One byte per pixel (page-sized);
        /// each fragment's effective alpha is multiplied by <c>SoftMaskAlpha[idx]</c>
        /// before blending. Null means no soft mask. Resolved lazily and cached per
        /// page in <see cref="SoftMaskCache"/>.
        /// </summary>
        public byte[]? SoftMaskAlpha { get; set; }

        /// <summary>
        /// Per-page cache keyed by the soft-mask group dict's object number, mapping
        /// to the rendered alpha buffer. A single PDF often references one SMask in
        /// dozens of paint operations; resolving (rendering the group) on each is
        /// prohibitive. Page-scoped because the same SMask dict can be referenced
        /// from multiple paint sites without re-rendering.
        /// </summary>
        public Dictionary<int, byte[]> SoftMaskCache { get; } = new();
    }
}
