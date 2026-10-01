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
    /// <summary>
    /// Render a soft-mask group to a page-sized 8-bit alpha buffer (§11.6.5.4). The mask
    /// form is rasterised into a scratch buffer at the given page geometry, then reduced
    /// to alpha (Luminosity ⇒ Rec.601 luma × coverage, Alpha ⇒ coverage), with the
    /// optional /TR transfer function applied. Shared by both renderers.
    /// </summary>
    // One page-sized scratch per thread, reused across mask renders: a document
    // styled with per-element soft masks (hundreds of distinct /SMask gs entries)
    // otherwise allocates a fresh full-page buffer for every mask it renders.
    // The busy flag covers re-entrancy — a mask group whose own content carries
    // a nested /SMask recurses into this method mid-render and must not clobber
    // the outer render's scratch.
    [ThreadStatic] private static byte[]? _softMaskScratch;

    [ThreadStatic] private static bool _softMaskScratchBusy;

    /// <summary>
    /// Sample a soft-mask /BC backdrop colour array as an RGB triple. /BC is in
    /// the group's color space; we approximate by treating 1-component as gray
    /// (R=G=B) and 3-component as RGB. Default per spec: black.
    /// </summary>
    internal static (byte R, byte G, byte B) SampleBackdropRgb(PdfArray? bc)
    {
        if (bc is null || bc.Count == 0) return (0, 0, 0);
        // /BC is written in the mask GROUP's own colour space (PDF 32000 §11.6.5.2), so its
        // component COUNT names the space: 1 gray, 3 RGB, 4 CMYK. Reading a four-component
        // backdrop as if its first three were RGB turns full ink - [1 1 1 1], i.e. BLACK -
        // into white, and a luminosity mask that should black out everything outside its
        // gradient instead came back fully opaque: a soft drop shadow painted as a hard
        // near-black band around every panel it framed.
        var comps = new double[bc.Count];
        for (var i = 0; i < bc.Count; i++) comps[i] = NumFrom(bc[i]);
        return ColorToRgb(comps);
    }

    // ── Shading paint (`sh` operator, PDF 32000 §8.7.4.5) ───────────
    //
    // The `sh` operator paints a named shading dictionary into the current clip
    // region. Shading coordinates are in the coordinate system that was active
    // at the moment of the paint, so the CTM is applied to the shading's
    // endpoints before the per-pixel parameter is computed. Only Type-2 (axial)
    // and Type-3 (radial) shadings are handled here — the cases that show up in
    // real-world PDFs with icon/logo gradients.

    private static void DrawShading(RenderContext ctx, string name, GraphicsState state)
    {
        if (ctx.Shadings is null) return;
        var shadingObj = ctx.Shadings.Get(name);
        if (shadingObj is null) return;

        // Inherit the blend mode and the ExtGState soft mask for this paint, the same way
        // DrawPath / DrawImage / DrawText do. Without it `sh` ignored a /SMask entirely —
        // a full-ink DeviceN band meant to show through a luminosity mask painted solid
        // near-black over a whole page section — AND it inherited whatever SoftMaskAlpha
        // the previous draw happened to leave on the context, which makes the result
        // depend on operator order.
        ctx.CurrentBlendMode = state.BlendMode;
        ctx.SoftMaskAlpha = state.SoftMask is { } smSh ? ResolveSoftMaskAlpha(ctx, smSh) : null;

        var shading = ShadingBase.Parse(shadingObj, ctx.Reader);
        switch (shading)
        {
            case FunctionBasedShading fn:
                DrawFunctionShading(ctx, fn, state);
                break;
            case AxialShading axial:
                DrawAxialShading(ctx, axial, state, bareSh: true);
                break;
            case RadialShading radial:
                DrawRadialShading(ctx, radial, state);
                break;
            case FreeFormGouraudShading gouraud:
                DrawGouraudMesh(ctx, gouraud.Vertices, gouraud.Triangles, gouraud.ColorSpaceName, state);
                break;
            case LatticeFormGouraudShading lat:
                DrawGouraudMesh(ctx, lat.Vertices, lat.Triangles, lat.ColorSpaceName, state);
                break;
            case CoonsPatchShading coons:
                DrawPatchMesh(ctx, coons.Patches, coons.ColorSpaceName, state);
                break;
            case TensorPatchShading tensor:
                DrawPatchMesh(ctx, tensor.Patches, tensor.ColorSpaceName, state);
                break;
        }
    }

    // ── Mesh shadings (Types 4-7) ───────────────────────────────────
    //
    // Strategy: tessellate the mesh into many small flat-shaded or per-vertex
    // interpolated triangles, then scanline-fill each. Patch types (Coons /
    // tensor) are subdivided into an N×N parameter grid evaluated through the
    // tensor cubic Bézier; each grid cell becomes two triangles with the
    // four corner colours bilinearly interpolated across (u,v).

    private static void DrawGouraudMesh(RenderContext ctx,
        MeshVertex[] verts, (int A, int B, int C)[] tris, string csName,
        GraphicsState state)
    {
        if (verts.Length == 0 || tris.Length == 0) return;
        var ctm = state.Ctm;
        var alpha = (byte)(state.FillAlpha * 255);
        foreach (var (ia, ib, ic) in tris)
        {
            var va = verts[ia]; var vb = verts[ib]; var vc = verts[ic];
            var (ax, ay) = TransformPoint(ctm, va.X, va.Y);
            var (bx, by) = TransformPoint(ctm, vb.X, vb.Y);
            var (cx, cy) = TransformPoint(ctm, vc.X, vc.Y);
            RasterizeColoredTriangle(ctx, csName, alpha,
                ax, ay, va.Color,
                bx, by, vb.Color,
                cx, cy, vc.Color);
        }
    }

    private static void DrawPatchMesh(RenderContext ctx, MeshPatch[] patches,
        string csName, GraphicsState state)
    {
        if (patches.Length == 0) return;
        var ctm = state.Ctm;
        var alpha = (byte)(state.FillAlpha * 255);
        // Sub-grid resolution. 16×16 gives 256 cells per patch — plenty of
        // smoothness even for full-page gradients while keeping rasterisation
        // bounded. Real-world mesh-shading PDFs typically have only a handful
        // of patches per page.
        const int N = 16;
        var px = new double[N + 1, N + 1];
        var py = new double[N + 1, N + 1];
        foreach (var patch in patches)
        {
            // Sample patch positions on the (N+1)×(N+1) grid.
            for (var i = 0; i <= N; i++)
            {
                var u = i / (double)N;
                for (var j = 0; j <= N; j++)
                {
                    var v = j / (double)N;
                    var x = EvalBicubic(patch.Px, u, v);
                    var y = EvalBicubic(patch.Py, u, v);
                    var (ux, uy) = TransformPoint(ctm, x, y);
                    px[i, j] = ux; py[i, j] = uy;
                }
            }
            // For each cell, produce two triangles with bilinear-interpolated colours.
            var ncc = patch.CornerColors[0]?.Length ?? 0;
            if (ncc == 0) continue;
            for (var i = 0; i < N; i++)
            {
                var u0 = i / (double)N; var u1 = (i + 1) / (double)N;
                for (var j = 0; j < N; j++)
                {
                    var v0 = j / (double)N; var v1 = (j + 1) / (double)N;
                    var c00 = BilinearColor(patch.CornerColors, u0, v0, ncc);
                    var c10 = BilinearColor(patch.CornerColors, u1, v0, ncc);
                    var c11 = BilinearColor(patch.CornerColors, u1, v1, ncc);
                    var c01 = BilinearColor(patch.CornerColors, u0, v1, ncc);
                    RasterizeColoredTriangle(ctx, csName, alpha,
                        px[i, j], py[i, j], c00,
                        px[i + 1, j], py[i + 1, j], c10,
                        px[i + 1, j + 1], py[i + 1, j + 1], c11);
                    RasterizeColoredTriangle(ctx, csName, alpha,
                        px[i, j], py[i, j], c00,
                        px[i + 1, j + 1], py[i + 1, j + 1], c11,
                        px[i, j + 1], py[i, j + 1], c01);
                }
            }
        }
    }

    /// <summary>Patch corner-colour storage order: [c00, c30, c33, c03]
    /// (bottom-left, bottom-right, top-right, top-left in (u,v) space).
    /// Bilinearly interpolate at arbitrary (u, v).</summary>
    private static double[] BilinearColor(double[][] cc, double u, double v, int ncc)
    {
        var c00 = cc[0]; var c30 = cc[1]; var c33 = cc[2]; var c03 = cc[3];
        var result = new double[ncc];
        var omu = 1 - u; var omv = 1 - v;
        for (var i = 0; i < ncc; i++)
        {
            var v0 = omu * omv * (c00?[i] ?? 0) + u * omv * (c30?[i] ?? 0);
            var v1 = omu * v * (c03?[i] ?? 0) + u * v * (c33?[i] ?? 0);
            result[i] = v0 + v1;
        }
        return result;
    }

    private static double EvalBicubic(double[,] g, double u, double v)
    {
        double r = default;
        // S(u,v) = sum_{i,j} B_i(u) B_j(v) * g[i,j]
        var bu0 = (1 - u) * (1 - u) * (1 - u);
        var bu1 = 3 * (1 - u) * (1 - u) * u;
        var bu2 = 3 * (1 - u) * u * u;
        var bu3 = u * u * u;
        var bv0 = (1 - v) * (1 - v) * (1 - v);
        var bv1 = 3 * (1 - v) * (1 - v) * v;
        var bv2 = 3 * (1 - v) * v * v;
        var bv3 = v * v * v;
        // Row sums then column dot to limit reads.
        var r0 = bu0 * g[0, 0] + bu1 * g[1, 0] + bu2 * g[2, 0] + bu3 * g[3, 0];
        var r1 = bu0 * g[0, 1] + bu1 * g[1, 1] + bu2 * g[2, 1] + bu3 * g[3, 1];
        var r2 = bu0 * g[0, 2] + bu1 * g[1, 2] + bu2 * g[2, 2] + bu3 * g[3, 2];
        var r3 = bu0 * g[0, 3] + bu1 * g[1, 3] + bu2 * g[2, 3] + bu3 * g[3, 3];
        r = bv0 * r0 + bv1 * r1 + bv2 * r2 + bv3 * r3;
        return r;
    }

    /// <summary>Fill a triangle in user space with per-vertex colours
    /// barycentrically interpolated. Coordinates are user-space (pre-pixel);
    /// we map to pixel space via the standard MediaBox-relative formula used
    /// by axial/radial shadings.</summary>
    private static void RasterizeColoredTriangle(RenderContext ctx, string csName, byte alpha,
        double x0u, double y0u, double[] c0,
        double x1u, double y1u, double[] c1,
        double x2u, double y2u, double[] c2)
    {
        if (c0 is null || c1 is null || c2 is null) return;
        var scale = ctx.Scale;
        var mbLlx = ctx.MediaBox.LLX;
        var mbLly = ctx.MediaBox.LLY;
        var pixelH = ctx.PixelH;

        // User space → pixel space (origin top-left, y inverted).
        double Px(double xu) => (xu - mbLlx) * scale;
        double Py(double yu) => pixelH - (yu - mbLly) * scale;
        var p0x = Px(x0u); var p0y = Py(y0u);
        var p1x = Px(x1u); var p1y = Py(y1u);
        var p2x = Px(x2u); var p2y = Py(y2u);

        // Bounding box in pixels.
        var minX = (int)Math.Floor(Math.Min(p0x, Math.Min(p1x, p2x)));
        var maxX = (int)Math.Ceiling(Math.Max(p0x, Math.Max(p1x, p2x)));
        var minY = (int)Math.Floor(Math.Min(p0y, Math.Min(p1y, p2y)));
        var maxY = (int)Math.Ceiling(Math.Max(p0y, Math.Max(p1y, p2y)));
        if (minX < 0) minX = 0; if (minY < 0) minY = 0;
        if (maxX > ctx.PixelW) maxX = ctx.PixelW; if (maxY > ctx.PixelH) maxY = ctx.PixelH;
        if (maxX <= minX || maxY <= minY) return;

        // Edge-function denominator (twice signed area).
        var denom = (p1x - p0x) * (p2y - p0y) - (p1y - p0y) * (p2x - p0x);
        if (Math.Abs(denom) < 1e-9) return;
        var invDenom = 1.0 / denom;
        var ncc = c0.Length;
        var col = new double[ncc];

        for (var py = minY; py < maxY; py++)
        {
            var rowBase = py * ctx.PixelW;
            for (var px = minX; px < maxX; px++)
            {
                if (ctx.ClipMask is { } mask && mask[rowBase + px] == 0) continue;
                // Sample at pixel centre.
                var sx = px + 0.5; var sy = py + 0.5;
                // Barycentric coordinates.
                var w0 = ((p1x - sx) * (p2y - sy) - (p1y - sy) * (p2x - sx)) * invDenom;
                var w1 = ((p2x - sx) * (p0y - sy) - (p2y - sy) * (p0x - sx)) * invDenom;
                var w2 = 1 - w0 - w1;
                // Accept points with strictly non-negative weights (with a
                // tiny tolerance to cover shared edges between adjacent
                // patches/triangles).
                if (w0 < -1e-6 || w1 < -1e-6 || w2 < -1e-6) continue;
                for (var k = 0; k < ncc; k++)
                    col[k] = w0 * c0[k] + w1 * c1[k] + w2 * c2[k];
                var (r, g, b) = ComponentsToRgb(col, csName);
                SetPixel(ctx, px, py, r, g, b, alpha);
            }
        }
    }

    /// <summary>
    /// Function-based shading (Type 1, PDF 32000 §8.7.4.5.3): the colour at a point is the
    /// shading's function evaluated at that point, in a space the /Matrix maps the /Domain
    /// into. Every other shading type was painted and this one was not - it simply fell
    /// through the dispatch - so a fill that used it (a table header banner, in the case
    /// that turned this up) came out blank, taking any white text on it with it.
    /// </summary>
    private static void DrawFunctionShading(RenderContext ctx, FunctionBasedShading fn, GraphicsState state)
    {
        if (fn.Function is null) return;

        // A point on the page maps back through the CTM and then the shading's own Matrix
        // to land in the function's domain.
        var full = GraphicsState.MultiplyMatrices(fn.Matrix, state.Ctm);
        var inv = InvertMatrix(full);
        if (inv is null) return;

        var dom = fn.Domain.Length >= 4 ? fn.Domain : new double[] { 0, 1, 0, 1 };
        double dx0 = Math.Min(dom[0], dom[1]), dx1 = Math.Max(dom[0], dom[1]);
        double dy0 = Math.Min(dom[2], dom[3]), dy1 = Math.Max(dom[2], dom[3]);

        var (xStart, xEnd, yStart, yEnd) = ComputeShadingPixelBounds(ctx);
        var bboxLocal = fn.BBox;
        double[]? ctmInv = bboxLocal is not null ? InvertMatrix(state.Ctm) : null;

        var invScale = 1.0 / ctx.Scale;
        var mbLlx = ctx.MediaBox.LLX;
        var mbLly = ctx.MediaBox.LLY;
        var alpha = (byte)(state.FillAlpha * 255);
        var csName = fn.ColorSpaceName;
        var input = new double[2];

        for (var py = yStart; py < yEnd; py++)
        {
            var uy = mbLly + (ctx.PixelH - py - 0.5) * invScale;
            var rowBase = py * ctx.PixelW;
            for (var px = xStart; px < xEnd; px++)
            {
                if (ctx.ClipMask is { } mask && mask[rowBase + px] == 0) continue;
                var ux = mbLlx + (px + 0.5) * invScale;

                if (bboxLocal is not null && ctmInv is not null)
                {
                    var (lx, ly) = TransformPoint(ctmInv, ux, uy);
                    if (lx < bboxLocal[0] || lx > bboxLocal[2] ||
                        ly < bboxLocal[1] || ly > bboxLocal[3])
                        continue;
                }

                var (fx, fy) = TransformPoint(inv, ux, uy);
                // Outside the domain the shading paints nothing (§8.7.4.5.3).
                if (fx < dx0 || fx > dx1 || fy < dy0 || fy > dy1) continue;

                input[0] = fx; input[1] = fy;
                var col = fn.Function.Evaluate(input);
                if (col is null) continue;
                var (r, g, b) = ComponentsToRgb(col, csName, fn.TintTransform, fn.AltSpaceName);
                SetPixel(ctx, px, py, r, g, b, alpha);
            }
        }
    }

    private static void DrawAxialShading(RenderContext ctx, AxialShading axial, GraphicsState state, bool bareSh = false)
    {
        var ash = new AxialShadingState();
        ash.ctx = ctx;
        ash.axial = axial;
        ash.state = state;
        ash.bareSh = bareSh;
        if (ash.axial.Function is null) return;

        ash.ctm = ash.state.Ctm;
        var (x0u, y0u) = TransformPoint(ash.ctm, ash.axial.X0, ash.axial.Y0);
        var (x1u, y1u) = TransformPoint(ash.ctm, ash.axial.X1, ash.axial.Y1);

        ash.dx = x1u - x0u;
        ash.dy = y1u - y0u;
        ash.denom = ash.dx * ash.dx + ash.dy * ash.dy;
        if (ash.denom < 1e-12) return; // axis collapsed to a point — nothing to draw

        ash.domLo = ash.axial.Domain.Length > 0 ? ash.axial.Domain[0] : 0;
        ash.domHi = ash.axial.Domain.Length > 1 ? ash.axial.Domain[1] : 1;
        ash.domLen = ash.domHi - ash.domLo;
        ash.extendBefore = ash.axial.Extend.Length > 0 && ash.axial.Extend[0];
        ash.extendAfter = ash.axial.Extend.Length > 1 && ash.axial.Extend[1];

        var (xStart, xEnd, yStart, yEnd) = ComputeShadingPixelBounds(ash.ctx);

        ash.bboxLocal = ash.axial.BBox;
        ash.inv = null;
        if (ash.bboxLocal is not null)
            ash.inv = InvertMatrix(ash.ctm);

        ash.invScale = 1.0 / ash.ctx.Scale;
        ash.mbLlx = ash.ctx.MediaBox.LLX;
        ash.mbLly = ash.ctx.MediaBox.LLY;
        ash.alpha = (byte)(ash.state.FillAlpha * 255);
        ash.csName = ash.axial.ColorSpaceName;
        ash.input = new double[1];

        ash.subtractive = ash.bareSh && (ash.csName is "DeviceN"
                                     || (ash.csName is not "Separation" and not "DeviceCMYK"
                                         && ash.axial.AltSpaceName is "DeviceCMYK"));
        ash.savedBlend = ash.ctx.CurrentBlendMode;
        if (ash.subtractive && ash.ctx.SoftMaskAlpha is null && ash.savedBlend == "Normal")
            ash.ctx.CurrentBlendMode = "Multiply";
        try
        {
            DrawAxialRows(ash, x0u, xEnd, xStart, y0u, yEnd, yStart);
        }
        finally { ash.ctx.CurrentBlendMode = ash.savedBlend; }
    }

    // 2D affine inverse for shading-BBox transforms. The CTM is
    // a row-vector [a b c d e f] in PDF spec layout, equivalent to
    // the matrix [[a, b, 0], [c, d, 0], [e, f, 1]]. Returns null
    // if singular (callers fall back to the default no-BBox path).
    private static double[]? InvertMatrix(double[] m)
    {
        var det = m[0] * m[3] - m[1] * m[2];
        if (Math.Abs(det) < 1e-12) return null;
        var invDet = 1.0 / det;
        var a = m[3] * invDet;
        var b = -m[1] * invDet;
        var c = -m[2] * invDet;
        var d = m[0] * invDet;
        var e = -(m[4] * a + m[5] * c);
        var f = -(m[4] * b + m[5] * d);
        return new[] { a, b, c, d, e, f };
    }

    private static void DrawRadialShading(RenderContext ctx, RadialShading radial, GraphicsState state)
    {
        var rs = new RadialShadingState();
        rs.ctx = ctx;
        rs.radial = radial;
        rs.state = state;
        if (rs.radial.Function is null) return;

        rs.ctm = rs.state.Ctm;
        var (x0u, y0u) = TransformPoint(rs.ctm, rs.radial.X0, rs.radial.Y0);
        var (x1u, y1u) = TransformPoint(rs.ctm, rs.radial.X1, rs.radial.Y1);
        rs.radiusScale = Math.Sqrt(Math.Abs(rs.ctm[0] * rs.ctm[3] - rs.ctm[1] * rs.ctm[2]));
        rs.r0 = rs.radial.R0 * rs.radiusScale;
        rs.r1 = rs.radial.R1 * rs.radiusScale;

        rs.domLo = rs.radial.Domain.Length > 0 ? rs.radial.Domain[0] : 0;
        rs.domHi = rs.radial.Domain.Length > 1 ? rs.radial.Domain[1] : 1;
        rs.domLen = rs.domHi - rs.domLo;
        rs.extendBefore = rs.radial.Extend.Length > 0 && rs.radial.Extend[0];
        rs.extendAfter = rs.radial.Extend.Length > 1 && rs.radial.Extend[1];

        rs.cdx = x1u - x0u;
        rs.cdy = y1u - y0u;
        rs.dr = rs.r1 - rs.r0;

        var (xStart, xEnd, yStart, yEnd) = ComputeShadingPixelBounds(rs.ctx);

        rs.bboxLocal = rs.radial.BBox;
        rs.inv = null;
        if (rs.bboxLocal is not null)
            rs.inv = InvertMatrix(rs.ctm);

        rs.invScale = 1.0 / rs.ctx.Scale;
        rs.mbLlx = rs.ctx.MediaBox.LLX;
        rs.mbLly = rs.ctx.MediaBox.LLY;
        rs.alpha = (byte)(rs.state.FillAlpha * 255);
        rs.csName = rs.radial.ColorSpaceName;
        rs.input = new double[1];

        // Pixel centres (+0.5), same rationale as DrawAxialShading.
        for (var py = yStart; py < yEnd; py++)
        {
            DrawRadialRow(rs, py, xStart, xEnd, x0u, y0u);
        }
    }

    /// <summary>Restrict the per-pixel shading loop to the clip mask's bounding box when set.</summary>
    private static (int xStart, int xEnd, int yStart, int yEnd) ComputeShadingPixelBounds(RenderContext ctx)
    {
        int xStart = default;
        int xEnd = default;
        int yStart = default;
        int yEnd = default;
        xStart = 0; xEnd = ctx.PixelW; yStart = 0; yEnd = ctx.PixelH;
        if (ctx.ClipMask is null) return (xStart, xEnd, yStart, yEnd);

        var mask = ctx.ClipMask;
        int minX = ctx.PixelW, maxX = -1, minY = ctx.PixelH, maxY = -1;
        for (var y = 0; y < ctx.PixelH; y++)
        {
            var rowBase = y * ctx.PixelW;
            for (var x = 0; x < ctx.PixelW; x++)
            {
                if (mask[rowBase + x] == 0) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }
        if (maxX < 0) { xEnd = 0; yEnd = 0; return (xStart, xEnd, yStart, yEnd); }
        xStart = minX; xEnd = maxX + 1;
        yStart = minY; yEnd = maxY + 1;
        return (xStart, xEnd, yStart, yEnd);
    }

    /// <summary>Apply an affine matrix [a b c d e f] to a user-space point.</summary>
    private static (double xo, double yo) TransformPoint(double[] m, double x, double y)
    {
        double xo = default;
        double yo = default;
        xo = m[0] * x + m[2] * y + m[4];
        yo = m[1] * x + m[3] * y + m[5];
        return (xo, yo);
    }

    /// <summary>
    /// Convert a shading function's output components to 8-bit RGB according to
    /// the shading's colour space name. Handles the common device colour spaces
    /// (Gray/RGB/CMYK); anything exotic falls back to mid-grey so the gradient
    /// still paints something rather than leaving the region blank.
    /// </summary>
    internal static (byte r, byte g, byte b) ComponentsToRgb(double[] components, string csName, Functions.PdfFunction? tint = null, string? altName = null)
    {
        byte r = default;
        byte g = default;
        byte b = default;
        // /Separation or /DeviceN output: map the tint components into the alternate
        // device space first, then convert that to RGB.
        if (tint is not null && altName is not null)
        {
            var alt = tint.Evaluate(components);
            if (alt is not null)
            {
                (r, g, b) = ComponentsToRgb(alt, altName);
                return (r, g, b);
            }
        }

        double rd, gd, bd;
        if (csName == "DeviceGray" || csName == "G" || components.Length == 1)
        {
            rd = gd = bd = components[0];
        }
        else if (csName == "DeviceCMYK" || csName == "CMYK" || components.Length == 4)
        {
            if (Environment.GetEnvironmentVariable("Q_SHLUT") == "0")
            {
                (rd, gd, bd) = CmykToRgbClamp(components[0], components[1], components[2], components[3]);
                r = ToByteClamp(rd); g = ToByteClamp(gd); b = ToByteClamp(bd);
                return (r, g, b);
            }
            // Same ICC-style conversion the content-stream `k`/`K` operators use
            // (CmykToRgbLut): a gradient authored in the same ink as an adjacent flat
            // fill must land on the same RGB, and the naïve subtractive formula
            // lands far off that mark (oversaturated, zero blue for Y=1 inks).
            var (rb, gb, bb) = Aspose.Pdf.Devices.CmykToRgbLut.Convert(
                components[0], components[1], components[2], components[3]);
            r = rb; g = gb; b = bb;
            return (r, g, b);
        }
        else if (csName == "Lab" && components.Length >= 3)
        {
            (rd, gd, bd) = LabColor.ToRgb(components[0], components[1], components[2]);
        }
        else if (csName == "LabEnc" && components.Length >= 3)
        {
            // Lab-encoded scanner-class ICC channels (L/100, (a+128)/255,
            // (b+128)/255 — see ContentStreamParser.IsLabEncodedIcc).
            static double C(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
            (rd, gd, bd) = LabColor.ToRgb(C(components[0]) * 100.0, C(components[1]) * 255.0 - 128.0,
                C(components[2]) * 255.0 - 128.0);
        }
        else if (components.Length >= 3)
        {
            rd = components[0];
            gd = components[1];
            bd = components[2];
        }
        else
        {
            rd = gd = bd = 0.5;
        }

        r = ToByteClamp(rd);
        g = ToByteClamp(gd);
        b = ToByteClamp(bd);
        return (r, g, b);
    }

    private static (double r, double g, double b) CmykToRgbClamp(double c, double m, double y, double k)
    {
        double r = default;
        double g = default;
        double b = default;
        r = (1 - c) * (1 - k);
        g = (1 - m) * (1 - k);
        b = (1 - y) * (1 - k);
        return (r, g, b);
    }

    private static byte ToByteClamp(double v)
    {
        if (v <= 0) return 0;
        if (v >= 1) return 255;
        return (byte)(v * 255);
    }

    // ── Tiling-pattern fill ────────────────────────────────────────
    //
    // PDF 32000 §8.7.3 describes PatternType 1 (tiling) patterns: the colour for a fill
    // operator `f` is produced by executing the pattern's content stream once, then
    // tiling the result at XStep×YStep through the painted region. For the common case
    // where the path fits within a single tile (XStep ≥ BBox width etc.), execution
    // reduces to: run the pattern's content stream with (CTM ← CTM × Pattern.Matrix)
    // into the page buffer, with the filled path acting as a stencil so nothing leaks
    // outside. That's what `FillWithPattern` does — it covers real-world files that
    // paint an image through a circular clip (a PatternType-1 whose
    // content is "q 550 0 0 550 0 0 cm /Image Do Q").

}
