using System.Runtime.InteropServices;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Devices.Rasterizer;
using Aspose.Pdf.IO;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Devices;

public sealed partial class SoftwarePageRenderer
{
    /// <summary>
    /// Render an inline image (BI/ID/EI). Inline images carry the same fields as
    /// regular image XObjects but inside the content stream — most commonly used
    /// for Type 3 font glyphs (each character is a tiny ImageMask). Honours
    /// /ImageMask + /Decode [a b], applies any /Filter chain via StreamFilter,
    /// and paints through the existing DrawImageMask / BlitRGB / BlitGray paths.
    /// CTM at the BI operator (captured by the caller via parser.State.Ctm) maps
    /// the unit square to the destination rect — same convention as XObject Do.
    /// </summary>
    /// <summary>
    /// PDF 32000 §8.9.6.4 colour-key masking: a /Mask given as an ARRAY of component ranges
    /// (rather than a stencil stream) makes every pixel whose samples ALL fall inside those
    /// ranges fully transparent. Build that plane on the source grid and fold it into the
    /// per-pixel alpha the blits already consume, exactly as the explicit stencil is folded.
    /// Ranges are compared against the 8-bit samples about to be blitted, which is what the
    /// GDI+ decoder does. Without it a letterhead logo whose background is a single keyed
    /// colour painted that colour as a solid block over the page.
    /// </summary>
    private static void FoldColorKeyMask(PdfDictionary dict, PdfReader reader, byte[] samples,
        int w, int h, int comps, ref byte[]? alpha, ref int alphaW, ref int alphaH)
    {
        if (w <= 0 || h <= 0 || comps <= 0) return;
        if (reader.Resolve(dict.Get("Mask")) is not PdfArray ck || ck.Count < comps * 2) return;

        var key = new int[comps * 2];
        for (var i = 0; i < key.Length; i++) key[i] = (int)NumFrom(ck[i]);

        var plane = new byte[w * h];
        for (var i = 0; i < plane.Length; i++)
        {
            var b = i * comps;
            var masked = b + comps <= samples.Length;
            for (var c = 0; c < comps && masked; c++)
            {
                int v = samples[b + c];
                if (v < key[c * 2] || v > key[c * 2 + 1]) masked = false;
            }
            plane[i] = masked ? (byte)0 : (byte)255;
        }

        if (alpha is null)
        {
            alpha = plane; alphaW = w; alphaH = h;
            return;
        }
        // Both planes are sampled in base-image coordinates; combine on the existing grid.
        for (var y = 0; y < alphaH; y++)
            for (var x = 0; x < alphaW; x++)
            {
                var sxr = x * w / alphaW;
                var syr = y * h / alphaH;
                var idx = y * alphaW + x;
                alpha[idx] = (byte)(alpha[idx] * plane[syr * w + sxr] / 255);
            }
    }

    /// <summary>
    /// PDF 32000 §11.6.5.3: an /SMask carrying /Matte stores the base image's colour samples
    /// PRE-BLENDED against that matte colour, so the true colour is c = m + (c' - m) / a.
    /// Compositing the stored samples as if they were straight makes the image too dark —
    /// a full-bleed slide background came out ~50 levels grey where it should be near white.
    /// The GDI+ decoder has done this since its own Matte work; this is the software half.
    /// Like GDI+, only un-premultiply when the mask is (near-)uniform: dividing a SHAPED
    /// mask's thin edges by a tiny alpha only blows highlights out to white, and its opaque
    /// interior needs no correction anyway.
    /// </summary>
    private static void UnpremultiplyMatte(byte[] samples, int components,
        PdfObject? smaskRef, byte[]? alpha, PdfReader reader)
    {
        if (alpha is null || alpha.Length == 0 || samples.Length == 0 || components <= 0) return;
        if (reader.ResolveStream(smaskRef)?.Dict.Get("Matte") is not PdfArray matte || matte.Count == 0) return;

        int amin = 255, amax = 0;
        foreach (var a in alpha) { if (a < amin) amin = a; if (a > amax) amax = a; }
        if (amax - amin > 8 || amax <= 0) return;   // shaped mask — leave the samples alone

        var aScale = amax / 255.0;
        double M(int i) => i < matte.Count ? NumFrom(matte[i]) * 255.0 : 0;
        var m0 = M(0);
        var m = matte.Count >= 3 ? new[] { m0, M(1), M(2) } : new[] { m0, m0, m0 };

        for (var i = 0; i + components <= samples.Length; i += components)
            for (var c = 0; c < components && c < 3; c++)
            {
                var v = m[c] + (samples[i + c] - m[c]) / aScale;
                samples[i + c] = (byte)(v < 0 ? 0 : v > 255 ? 255 : v);
            }
    }

    /// <summary>Paint an ImageMask whose current fill is a pattern: build a device-space
    /// coverage stencil from the mask bits (AND-ed with the active clip) and fill the mask's
    /// quad with the pattern through it, so the pattern shows through the stencil rather than
    /// the stale solid fill colour over-inking the masked region.</summary>
    private static void DrawImageMaskWithPattern(RenderContext ctx, byte[] decoded, int imgW, int imgH,
        int px, int py, int pw, int ph, GraphicsState state, bool invertDecode)
    {
        if (state.FillPatternName is null || pw <= 0 || ph <= 0 || imgW <= 0 || imgH <= 0) return;
        var rowBytes = (imgW + 7) / 8;
        var paintBit = invertDecode ? 1 : 0;
        // Device-resolution stencil: a dest pixel is painted when the majority of the source
        // mask bits it covers are paint bits (area-averaged downsample, same mapping as
        // DrawImageMask). AND with the active clip so the fill stays inside both.
        var cov = new byte[ctx.PixelW * ctx.PixelH];
        for (int dy = 0; dy < ph; dy++)
        {
            int destY = py + dy;
            if (destY < 0 || destY >= ctx.PixelH) continue;
            long sy0 = (long)dy * imgH / ph, sy1 = (long)(dy + 1) * imgH / ph;
            if (sy1 == sy0) sy1 = sy0 + 1;
            for (int dx = 0; dx < pw; dx++)
            {
                int destX = px + dx;
                if (destX < 0 || destX >= ctx.PixelW) continue;
                long sx0 = (long)dx * imgW / pw, sx1 = (long)(dx + 1) * imgW / pw;
                if (sx1 == sx0) sx1 = sx0 + 1;
                long paint = 0, total = 0;
                for (long sy = sy0; sy < sy1; sy++)
                {
                    long rb = sy * rowBytes;
                    for (long sx = sx0; sx < sx1; sx++)
                    {
                        long bi = rb + (sx >> 3);
                        int bit = bi < decoded.Length ? (decoded[(int)bi] >> (int)(7 - (sx & 7))) & 1 : 1 - paintBit;
                        if (bit == paintBit) paint++;
                        total++;
                    }
                }
                if (total > 0 && paint * 2 >= total)
                    cov[destY * ctx.PixelW + destX] = 255;
            }
        }
        if (ctx.ClipMask is { } outer)
            for (int i = 0; i < cov.Length; i++)
                if (outer[i] == 0) cov[i] = 0;

        // Fill the mask's unit-square quad with the pattern, clipped to the stencil.
        var quad = new System.Collections.Generic.List<PathCommand>
        {
            new(PathOp.MoveTo, 0, 0), new(PathOp.LineTo, 1, 0),
            new(PathOp.LineTo, 1, 1), new(PathOp.LineTo, 0, 1), new(PathOp.LineTo, 0, 0),
        };
        var quadEdges = BuildPathEdgeTable(quad, state.Ctm, ctx);
        var savedClip = ctx.ClipMask;
        ctx.ClipMask = cov;
        try { FillWithPattern(ctx, quadEdges, false, state.FillPatternName, state); }
        finally { ctx.ClipMask = savedClip; }
    }

    /// <summary>Pattern-fill an ImageMask under an arbitrary affine CTM. Builds the coverage
    /// stencil by inverse-mapping each destination pixel back through the CTM into the mask's
    /// unit square (same inverse map as BlitRgbaAffine), then fills the transformed unit
    /// square with the pattern through that stencil.</summary>
    private static void DrawImageMaskWithPatternAffine(RenderContext ctx, byte[] decoded,
        int imgW, int imgH, double[] ctm, GraphicsState state, bool invertDecode)
    {
        if (state.FillPatternName is null || imgW <= 0 || imgH <= 0) return;
        double det = ctm[0] * ctm[3] - ctm[1] * ctm[2];
        if (Math.Abs(det) < 1e-12) return;
        double inv = 1.0 / det;
        long rowBytes = (imgW + 7) / 8;
        int paintBit = invertDecode ? 1 : 0;

        double x0 = ctm[4], x1 = ctm[4] + ctm[0], x2 = ctm[4] + ctm[2], x3 = ctm[4] + ctm[0] + ctm[2];
        double y0 = ctm[5], y1 = ctm[5] + ctm[1], y2 = ctm[5] + ctm[3], y3 = ctm[5] + ctm[1] + ctm[3];
        double minX = Math.Min(Math.Min(x0, x1), Math.Min(x2, x3)), maxX = Math.Max(Math.Max(x0, x1), Math.Max(x2, x3));
        double minY = Math.Min(Math.Min(y0, y1), Math.Min(y2, y3)), maxY = Math.Max(Math.Max(y0, y1), Math.Max(y2, y3));
        int pxMin = Math.Max(0, (int)Math.Floor((minX - ctx.MediaBox.LLX) * ctx.Scale));
        int pxMax = Math.Min(ctx.PixelW, (int)Math.Ceiling((maxX - ctx.MediaBox.LLX) * ctx.Scale));
        int pyMin = Math.Max(0, ctx.PixelH - (int)Math.Ceiling((maxY - ctx.MediaBox.LLY) * ctx.Scale));
        int pyMax = Math.Min(ctx.PixelH, ctx.PixelH - (int)Math.Floor((minY - ctx.MediaBox.LLY) * ctx.Scale));

        var cov = new byte[ctx.PixelW * ctx.PixelH];
        bool any = false;
        for (int dy = pyMin; dy < pyMax; dy++)
        {
            double uy = (ctx.PixelH - dy - 0.5) / ctx.Scale + ctx.MediaBox.LLY;
            for (int dx = pxMin; dx < pxMax; dx++)
            {
                double ux = (dx + 0.5) / ctx.Scale + ctx.MediaBox.LLX;
                double rx = ux - ctm[4], ry = uy - ctm[5];
                double u = (rx * ctm[3] - ry * ctm[2]) * inv;
                double v = (-rx * ctm[1] + ry * ctm[0]) * inv;
                if (u < 0 || u >= 1 || v < 0 || v >= 1) continue;
                int sx = (int)(u * imgW); if (sx >= imgW) sx = imgW - 1;
                int sy = (int)((1.0 - v) * imgH); if (sy >= imgH) sy = imgH - 1; if (sy < 0) sy = 0;
                long bi = (long)sy * rowBytes + (sx >> 3);
                int bit = bi < decoded.Length ? (decoded[(int)bi] >> (7 - (sx & 7))) & 1 : 1 - paintBit;
                if (bit == paintBit) { cov[dy * ctx.PixelW + dx] = 255; any = true; }
            }
        }
        if (!any) return;
        if (ctx.ClipMask is { } outer)
            for (int i = 0; i < cov.Length; i++)
                if (outer[i] == 0) cov[i] = 0;

        var quad = new System.Collections.Generic.List<PathCommand>
        {
            new(PathOp.MoveTo, 0, 0), new(PathOp.LineTo, 1, 0),
            new(PathOp.LineTo, 1, 1), new(PathOp.LineTo, 0, 1), new(PathOp.LineTo, 0, 0),
        };
        var quadEdges = BuildPathEdgeTable(quad, ctm, ctx);
        var savedClip = ctx.ClipMask;
        ctx.ClipMask = cov;
        try { FillWithPattern(ctx, quadEdges, false, state.FillPatternName, state); }
        finally { ctx.ClipMask = savedClip; }
    }

    private static void DrawImageMask(RenderContext ctx, byte[] decoded, int imgW, int imgH,
        int px, int py, int pw, int ph, GraphicsState state, bool invertDecode = false,
        bool flipY = false, bool flipX = false)
    {
        var r = (byte)(state.FillR * 255);
        var g = (byte)(state.FillG * 255);
        var b = (byte)(state.FillB * 255);
        var rowBytes = (imgW + 7) / 8;

        // PDF 32000 §8.9.5.4 + §8.9.5.1: default /Decode [0 1] means bit=0 paints
        // the current fill colour and bit=1 is transparent. /Decode [1 0] flips
        // it. Type 3 fonts in old dot-matrix report PDFs commonly ship glyphs
        // as inline ImageMasks with /D [1 0].
        var paintBit = invertDecode ? 1 : 0;

        // Iterate destination pixels. For each, area-average the source bits that
        // map to it: count paint-bits / total-bits inside the inverse-mapped src
        // rect, use that fraction as the fragment alpha. This is anti-aliased
        // downsampling — without it a 600×144 banner mask drawn at 144×35 pixels
        // (a dot-matrix invoice's header banners, a 4× downscale)
        // hashes between paint and transparent depending on which source pixel
        // each dest happens to land on. Per-pixel area averaging makes the
        // banner stripe (~30% paint bits) appear as a solid colour fill while
        // letters carved into the banner (bit=1 holes) come out as transparent
        // areas — the intended white-on-dark banner appearance.
        if (pw <= 0 || ph <= 0) return;
        for (int dy = 0; dy < ph; dy++)
        {
            var destY = py + dy;
            if (destY < 0 || destY >= ctx.PixelH) continue;

            // Inverse-map this dest row into a [srcY0, srcY1) source span. A negative
            // ctm[3] mirrors the image vertically (ctm[0] horizontally): the dest rect is
            // placed from |ctm|, so only the SAMPLING direction carries the mirror.
            var mdy = flipY ? ph - 1 - dy : dy;
            var srcY0 = (long)mdy * imgH / ph;
            var srcY1 = (long)(mdy + 1) * imgH / ph;
            if (srcY1 == srcY0) srcY1 = srcY0 + 1;

            for (int dx = 0; dx < pw; dx++)
            {
                var destX = px + dx;
                if (destX < 0 || destX >= ctx.PixelW) continue;

                var mdx = flipX ? pw - 1 - dx : dx;
                var srcX0 = (long)mdx * imgW / pw;
                var srcX1 = (long)(mdx + 1) * imgW / pw;
                if (srcX1 == srcX0) srcX1 = srcX0 + 1;

                long paintCount = 0, totalCount = 0;
                for (var sy = srcY0; sy < srcY1; sy++)
                {
                    var rowBase = sy * rowBytes;
                    for (var sx = srcX0; sx < srcX1; sx++)
                    {
                        var bi = rowBase + sx / 8;
                        if (bi < 0 || bi >= decoded.Length) continue;
                        var bit = (decoded[(int)bi] >> (7 - (int)(sx & 7))) & 1;
                        if (bit == paintBit) paintCount++;
                        totalCount++;
                    }
                }
                if (totalCount == 0 || paintCount == 0) continue;
                var alpha = (byte)(paintCount * 255 / totalCount);
                SetPixel(ctx, destX, destY, r, g, b, alpha);
            }
        }
    }
}
