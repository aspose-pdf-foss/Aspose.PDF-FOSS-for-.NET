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
        // PDF 32000 §11.3.6 composites over a backdrop that has its OWN alpha, and both
        // halves below need it. A transparency group renders onto a scratch buffer that
        // starts fully TRANSPARENT, and a transparent backdrop still has RGB in it - zero,
        // i.e. black. Reading that as a real colour made every Multiply inside a group come
        // out black (a page of coloured circles rendered as black blobs), and made a
        // partly-transparent Normal draw darken toward it. The page buffer starts opaque
        // white, so there alpha is 1 and both formulas collapse to what they were.
        var backdropA = ctx.Pixels[idx + 3] / 255.0;

        if (mode != Rasterizer.BlendMode.Normal)
        {
            if (a == 0) return;
            BlendModes.Blend(mode, ctx.Pixels[idx], ctx.Pixels[idx + 1], ctx.Pixels[idx + 2],
                r, g, b, out var ibr, out var ibg, out var ibb);
            // Cs′ = (1 − αb)·Cs + αb·B(Cb, Cs): the blend applies only in proportion to how
            // much backdrop is actually there.
            byte br = (byte)(r + (ibr - r) * backdropA);
            byte bg = (byte)(g + (ibg - g) * backdropA);
            byte bb = (byte)(b + (ibb - b) * backdropA);
            Composite(ctx, idx, br, bg, bb, a, backdropA);
            return;
        }

        Composite(ctx, idx, r, g, b, a, backdropA);
    }

    /// <summary>Source-over with STRAIGHT alpha on both sides. Against an opaque backdrop
    /// this is the familiar lerp; against a partly transparent one the destination colour
    /// is weighted by its own alpha, so a scratch buffer’s empty pixels contribute nothing
    /// rather than contributing black.</summary>
    private static void Composite(RenderContext ctx, int idx, byte r, byte g, byte b, byte a,
        double backdropA)
    {
        if (a == 255 || backdropA <= 0)
        {
            if (a == 0) return;
            ctx.Pixels[idx] = r;
            ctx.Pixels[idx + 1] = g;
            ctx.Pixels[idx + 2] = b;
            if (a > ctx.Pixels[idx + 3]) ctx.Pixels[idx + 3] = a;
            return;
        }
        if (a == 0) return;

        var srcA = a / 255.0;
        var keep = backdropA * (1.0 - srcA);
        var outA = srcA + keep;
        if (outA <= 0) return;
        ctx.Pixels[idx] = (byte)((r * srcA + ctx.Pixels[idx] * keep) / outA);
        ctx.Pixels[idx + 1] = (byte)((g * srcA + ctx.Pixels[idx + 1] * keep) / outA);
        ctx.Pixels[idx + 2] = (byte)((b * srcA + ctx.Pixels[idx + 2] * keep) / outA);
        ctx.Pixels[idx + 3] = (byte)Math.Round(outA * 255);
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
        double fillAlpha = 1.0, bool flipY = false, bool flipX = false, bool overprint = false)
    {
        // /CA /ca via the gs ExtGState arrives on state.FillAlpha; multiplied with
        // any SMask per-pixel opacity to yield the final alpha. fillAlpha=1 keeps
        // the old behaviour for callers that don't pass it.
        var fa = (int)Math.Round(fillAlpha * 255);
        if (fa < 0) fa = 0; else if (fa > 255) fa = 255;

        // Point-sampling a MINIFIED image reads one source pixel per destination pixel and
        // throws the rest away, so a scanned page reduced 2.4x lost a third of its ink and
        // came back visibly thinner than the GDI+ render of the same scan. Average the
        // source pixels each destination pixel actually covers; the mask sampler below
        // already does this, for the same reason. Magnification keeps the point sample.
        var minify = srcW > dstW || srcH > dstH;

        for (int y = 0; y < dstH; y++)
        {
            // PDF 32000 §8.9.4 image data rows are top-down. When the CTM applies
            // a vertical flip (ctm[3] < 0) the caller sets flipY so the source rows
            // are sampled bottom-up — without this, header banner images render
            // upside-down.
            var sy = flipY ? (srcH - 1 - y * srcH / dstH) : y * srcH / dstH;
            var dy = dstY + y;
            if (dy < 0 || dy >= ctx.PixelH) continue;
            var syLo = y * srcH / dstH;
            var syHi = Math.Max(syLo + 1, (y + 1) * srcH / dstH);
            if (flipY) { var t = srcH - syHi; syHi = srcH - syLo; syLo = t; }

            for (int x = 0; x < dstW; x++)
            {
                var sx = flipX ? (srcW - 1 - x * srcW / dstW) : x * srcW / dstW;
                var dx = dstX + x;
                if (dx < 0 || dx >= ctx.PixelW) continue;

                var si = (sy * srcW + sx) * 3;
                if (si + 2 >= src.Length) continue;
                byte cr = src[si], cg = src[si + 1], cb = src[si + 2];
                if (minify)
                {
                    var sxLo = x * srcW / dstW;
                    var sxHi = Math.Max(sxLo + 1, (x + 1) * srcW / dstW);
                    if (flipX) { var t = srcW - sxHi; sxHi = srcW - sxLo; sxLo = t; }
                    long ar = 0, ag = 0, ab = 0, n = 0;
                    for (var yy = syLo; yy < syHi; yy++)
                        for (var xx = sxLo; xx < sxHi; xx++)
                        {
                            var i2 = (yy * srcW + xx) * 3;
                            if (i2 < 0 || i2 + 2 >= src.Length) continue;
                            ar += src[i2]; ag += src[i2 + 1]; ab += src[i2 + 2]; n++;
                        }
                    if (n > 0)
                    {
                        cr = (byte)((ar + n / 2) / n); cg = (byte)((ag + n / 2) / n); cb = (byte)((ab + n / 2) / n);
                    }
                }

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
                if (overprint) SetPixelOverprint(ctx, dx, dy, cr, cg, cb, (byte)a);
                else SetPixel(ctx, dx, dy, cr, cg, cb, (byte)a);
            }
        }
    }

    private static void BlitGray(RenderContext ctx, byte[] src, int srcW, int srcH,
        int dstX, int dstY, int dstW, int dstH, byte[]? alpha = null, int alphaW = 0, int alphaH = 0,
        double fillAlpha = 1.0, bool flipY = false, bool flipX = false, bool overprint = false)
    {
        // Same rule as BlitRGB: a minified image is AVERAGED over the source pixels each
        // destination pixel covers, not point-sampled down to one of them.
        var minify = srcW > dstW || srcH > dstH;
        var fa = (int)Math.Round(fillAlpha * 255);
        if (fa < 0) fa = 0; else if (fa > 255) fa = 255;

        for (int y = 0; y < dstH; y++)
        {
            // Mirrored-CTM sampling, same contract as BlitRGB: flipY samples the
            // source rows bottom-up, flipX the columns right-to-left.
            var sy = flipY ? (srcH - 1 - y * srcH / dstH) : y * srcH / dstH;
            var dy = dstY + y;
            if (dy < 0 || dy >= ctx.PixelH) continue;
            var syLo = y * srcH / dstH;
            var syHi = Math.Max(syLo + 1, (y + 1) * srcH / dstH);
            if (flipY) { var t = srcH - syHi; syHi = srcH - syLo; syLo = t; }

            for (int x = 0; x < dstW; x++)
            {
                var sx = flipX ? (srcW - 1 - x * srcW / dstW) : x * srcW / dstW;
                var dx = dstX + x;
                if (dx < 0 || dx >= ctx.PixelW) continue;

                var si = sy * srcW + sx;
                if (si >= src.Length) continue;
                var g = src[si];
                if (minify)
                {
                    var sxLo = x * srcW / dstW;
                    var sxHi = Math.Max(sxLo + 1, (x + 1) * srcW / dstW);
                    if (flipX) { var t = srcW - sxHi; sxHi = srcW - sxLo; sxLo = t; }
                    long sum = 0, n = 0;
                    for (var yy = syLo; yy < syHi; yy++)
                        for (var xx = sxLo; xx < sxHi; xx++)
                        {
                            var i2 = yy * srcW + xx;
                            if (i2 < 0 || i2 >= src.Length) continue;
                            sum += src[i2]; n++;
                        }
                    if (n > 0) g = (byte)((sum + n / 2) / n);
                }

                int a = fa;
                if (alpha is not null && alphaW > 0 && alphaH > 0)
                    a = (a * SampleAlpha(alpha, alphaW, alphaH, x, y, dstW, dstH, flipX, flipY)) / 255;
                if (overprint) SetPixelOverprint(ctx, dx, dy, g, g, g, (byte)a);
                else SetPixel(ctx, dx, dy, g, g, g, (byte)a);
            }
        }
    }

    /// <summary>
    /// Overprint composite (PDF 32000 §8.6.7), approximated by a Multiply exactly as the
    /// GDI+ renderer's BlitImageMultiply does: out = dst·src/255. A no-ink (white) source
    /// pixel leaves the destination untouched, so an overprinted spot plate TINTS the process
    /// colour beneath it rather than knocking it out. Over bare paper the two agree, which is
    /// why the ordinary opaque path stays correct for everything that is not overprinting.
    /// </summary>
    private static void SetPixelOverprint(RenderContext ctx, int x, int y, byte r, byte g, byte b, byte a)
    {
        if (x < 0 || x >= ctx.PixelW || y < 0 || y >= ctx.PixelH) return;
        var pxIdx = y * ctx.PixelW + x;
        if (ctx.ClipMask is { } mask && mask[pxIdx] == 0) return;
        if (ctx.SoftMaskAlpha is { } sm)
        {
            var m = sm[pxIdx];
            if (m == 0) return;
            a = (byte)((a * m + 127) / 255);
        }
        if (a == 0) return;
        var idx = pxIdx * 4;
        // Multiply against what is already there, then weight by the source's own alpha so a
        // partially covered edge pixel still tints proportionally.
        int mr = ctx.Pixels[idx] * r / 255, mg = ctx.Pixels[idx + 1] * g / 255, mb = ctx.Pixels[idx + 2] * b / 255;
        if (a == 255)
        {
            ctx.Pixels[idx] = (byte)mr; ctx.Pixels[idx + 1] = (byte)mg; ctx.Pixels[idx + 2] = (byte)mb;
        }
        else
        {
            int inv = 255 - a;
            ctx.Pixels[idx]     = (byte)((mr * a + ctx.Pixels[idx]     * inv + 127) / 255);
            ctx.Pixels[idx + 1] = (byte)((mg * a + ctx.Pixels[idx + 1] * inv + 127) / 255);
            ctx.Pixels[idx + 2] = (byte)((mb * a + ctx.Pixels[idx + 2] * inv + 127) / 255);
        }
        if (a > ctx.Pixels[idx + 3]) ctx.Pixels[idx + 3] = a;
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

    /// <summary>Blit a CMYK image. It takes the same /SMask or stencil /Mask alpha the RGB
    /// and grayscale blits do: without it a masked CMYK image painted every pixel opaque,
    /// so a scan whose stencil should have left the paper alone covered the page in the
    /// scanner’s own dark background.</summary>
    private static void BlitCMYK(RenderContext ctx, byte[] src, int srcW, int srcH,
        int dstX, int dstY, int dstW, int dstH, byte[]? alpha = null, int alphaW = 0, int alphaH = 0,
        double fillAlpha = 1.0, bool overprint = false)
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

                int a = fa;
                if (alpha is not null && alphaW > 0 && alphaH > 0)
                    a = (a * SampleAlpha(alpha, alphaW, alphaH, x, y, dstW, dstH, false, false)) / 255;
                if (a == 0) continue;
                if (overprint) SetPixelOverprint(ctx, dx, dy, r, g, b, (byte)a);
                else SetPixel(ctx, dx, dy, r, g, b, (byte)a);
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
        // A 1-bit source has no middle: every sample is pure black or pure white. Point-
        // sampling it into a SMALLER destination therefore drops any stroke that falls
        // between two sample points outright - a scanned page reduced 2.4x lost a third of
        // its ink and came back visibly thinner than the GDI+ render of the same scan.
        // Average the source pixels each destination pixel actually covers instead, so a
        // thin stroke survives as grey. The mask sampler above already does this for the
        // same reason; magnification keeps the point sample, which stays crisp.
        var minify = srcW > dstW || srcH > dstH;
        for (int y = 0; y < dstH; y++)
        {
            var sy = (long)y * srcH / dstH;
            var syEnd = minify ? Math.Max(sy + 1, (long)(y + 1) * srcH / dstH) : sy + 1;
            var dy = dstY + y;
            if (dy < 0 || dy >= ctx.PixelH) continue;

            for (int x = 0; x < dstW; x++)
            {
                var sx = (long)x * srcW / dstW;
                var sxEnd = minify ? Math.Max(sx + 1, (long)(x + 1) * srcW / dstW) : sx + 1;
                var dx = dstX + x;
                if (dx < 0 || dx >= ctx.PixelW) continue;

                if (minify)
                {
                    long ones = 0, total = 0;
                    for (var yy = sy; yy < syEnd; yy++)
                    {
                        var rb = yy * rowBytes;
                        for (var xx = sx; xx < sxEnd; xx++)
                        {
                            var bi = rb + xx / 8;
                            if (bi < 0 || bi >= src.Length) continue;
                            ones += ((src[bi] >> (7 - (int)(xx & 7))) & 1) ^ (invert ? 1 : 0);
                            total++;
                        }
                    }
                    if (total == 0) continue;
                    var avg = (byte)((ones * 255 + total / 2) / total);
                    SetPixel(ctx, dx, dy, avg, avg, avg, 255);
                    continue;
                }

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

    /// <summary>
    /// Render an Indexed image with bilinear scaling. PDF 32000-1:2008 §8.9.5.3: pixel data is
    /// packed as bpc-bit indices (rows are byte-aligned), each index is looked up in the palette
    /// to get a baseCS tuple. GDI+
    /// uses bilinear by default, so we match that — nearest-neighbour drops colour accuracy at
    /// sub-pixel boundaries even when the decoded content is correct.
    /// </summary>
    private static void BlitIndexed(RenderContext ctx, byte[] src, int srcW, int srcH,
        int dstX, int dstY, int dstW, int dstH, int bpc, ImageColorSpaceInfo csInfo,
        bool flipY = false, bool flipX = false,
        byte[]? alpha = null, int alphaW = 0, int alphaH = 0, double fillAlpha = 1.0)
    {
        if (csInfo.Palette is null || csInfo.PaletteComponents <= 0) return;

        // Decode once into an sRGB buffer; bilinear sampling needs random access to RGB triplets,
        // and paying the decode cost up-front beats redoing palette lookups 4× per dst pixel.
        var rgb = DecodeIndexedToRgb(src, srcW, srcH, bpc, csInfo);
        if (rgb is null) return;

        BlitRgbBilinear(ctx, rgb, srcW, srcH, dstX, dstY, dstW, dstH, flipY, flipX,
            alpha, alphaW, alphaH, fillAlpha);
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
                // Guard on the components the branch below will READ, not on the entry
                // stride: the RGB path dereferences three of them whatever the stride is.
                var needed = isGray ? 1 : (isCmyk && comps == 4 ? 4 : 3);
                if (pbase + Math.Max(comps, needed) > palette.Length) continue;

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
        int dstX, int dstY, int dstW, int dstH, bool flipY = false, bool flipX = false,
        byte[]? alpha = null, int alphaW = 0, int alphaH = 0, double fillAlpha = 1.0)
    {
        if (dstW <= 0 || dstH <= 0 || srcW <= 0 || srcH <= 0) return;

        // /SMask (and a stencil /Mask folded into it) reaches the palette path too. It used to
        // stop at BlitRGB / BlitGray, so an INDEXED image with a soft mask painted every pixel
        // opaque and its transparent surround came out as whatever the palette's background
        // entry happened to be — three cut-out product renders showed up as dark boxes.
        var fa = (int)Math.Round(fillAlpha * 255);
        if (fa < 0) fa = 0; else if (fa > 255) fa = 255;

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
            // Mirrored-CTM sampling: reflect the (clamped) sample position, which
            // keeps the bilinear weights symmetric under the mirror.
            if (flipY) sy = srcH - 1 - sy;
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
                if (flipX) sx = srcW - 1 - sx;
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
                var a = fa;
                if (alpha is not null && alphaW > 0 && alphaH > 0)
                    a = (a * SampleAlpha(alpha, alphaW, alphaH, x, y, dstW, dstH, flipX, flipY)) / 255;
                if (a == 0) continue;
                SetPixel(ctx, dx, dy, r, g, b, (byte)a);
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
}
