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
    private static void DrawImage(RenderContext ctx, PdfStream xobjStream, GraphicsState state)
    {
        var dict = xobjStream.Dict;
        var imgW = (int)dict.GetInt("Width");
        var imgH = (int)dict.GetInt("Height");
        if (imgW <= 0 || imgH <= 0) return;

        // Skip an image whose optional-content group/membership is hidden by the
        // document's default configuration (PDF 32000 §8.11.4.4).
        if (IsOcHidden(dict.Get("OC"), ctx.Reader, ctx.OcgHidden)) return;

        // Inherit blend mode and fill-alpha (CA/ca via /gs) for this draw. Set on the
        // context up front; Blit* paths read it via SetPixel.
        ctx.CurrentBlendMode = state.BlendMode;
        ctx.SoftMaskAlpha = state.SoftMask is { } sm__ ? ResolveSoftMaskAlpha(ctx, sm__) : null;

        byte[] decoded;
        try { decoded = ctx.Reader.DecodeStream(xobjStream); }
        catch { return; }

        // Check for image mask
        var isImageMask = dict.Get("ImageMask") is PdfBoolean imb && imb.Value;

        // /SMask: PDF 32000 §11.6.5.3 — an indirect reference to a soft-mask
        // grayscale image (W×H bytes, 8bpc). Sample value = per-pixel opacity.
        // Resolved here so the various Blit branches below can pass it through.
        var smask = ResolveSMaskAlpha(dict.Get("SMask"), ctx.Reader, out var smaskW, out var smaskH);

        // /Mask: PDF 32000 §8.9.6.3 — an explicit 1-bit stencil mask selecting which
        // base-image pixels are painted. Folded into the same per-pixel alpha plane the
        // blit branches consume; when both /SMask and /Mask are present their opacities
        // multiply.
        var stencil = ResolveStencilMaskAlpha(dict.Get("Mask"), ctx.Reader, out var stencilW, out var stencilH);
        if (stencil is not null)
        {
            if (smask is null)
            {
                smask = stencil; smaskW = stencilW; smaskH = stencilH;
            }
            else
            {
                // Combine on the SMask grid (both are sampled in base-image coords).
                var combined = new byte[smaskW * smaskH];
                for (int y = 0; y < smaskH; y++)
                    for (int x = 0; x < smaskW; x++)
                    {
                        var sxr = x * stencilW / smaskW;
                        var syr = y * stencilH / smaskH;
                        combined[y * smaskW + x] = (byte)(smask[y * smaskW + x] * stencil[syr * stencilW + sxr] / 255);
                    }
                smask = combined;
            }
        }

        var ctm = state.Ctm;

        // A rotated or skewed image CTM (e.g. any image on a /Rotate 90|270 page) cannot
        // be represented by the axis-aligned blit paths below — they sample the source on
        // a straight x/y scale, so they place the image at the wrong size and orientation
        // (e.g. a /Rotate 270 page drew its CCITT text mask 3.7x off-canvas and
        // the page rendered blank). Decode the image to RGBA once and inverse-map each
        // destination pixel through the CTM. Axis-aligned images keep their optimised paths.
        if (Math.Abs(ctm[1]) > 1e-4 || Math.Abs(ctm[2]) > 1e-4)
        {
            // A rotated/skewed mask painted while a /Pattern fill is active shows the
            // pattern through the stencil, not a flat colour (see DrawImageMaskWithPattern).
            if (isImageMask && state.FillPatternName is not null)
            {
                var inv = dict.Get("Decode") is PdfArray dm && dm.Count >= 2 && NumFrom(dm[0]) > NumFrom(dm[1]);
                DrawImageMaskWithPatternAffine(ctx, decoded, imgW, imgH, ctm, state, inv);
                return;
            }
            var aff = DecodeImageToRgba(ctx, dict, decoded, imgW, imgH, isImageMask, smask, smaskW, smaskH, state);
            if (aff is not null) BlitRgbaAffine(ctx, aff.Value.rgba, aff.Value.w, aff.Value.h, ctm, state.FillAlpha);
            return;
        }

        // Compute destination rectangle in page coordinates. The PDF unit square
        // (0,0)-(1,1) maps to ctm[5]…ctm[5]+ctm[3] vertically; either bound can be
        // higher depending on the sign of ctm[3]. Most PDFs use positive ctm[3]
        // (image-y=0 at the bottom of the rect, image-y=1 at the top), but
        // generators that emit pre-flipped image data use ctm[3]<0 to compensate.
        // Pick the higher PDF y as the top of the rendered rectangle and the
        // lower as the bottom; pixel coordinates are origin-top-left, so the
        // higher PDF y becomes the lower pixel row.
        var destX = Math.Min(ctm[4], ctm[4] + ctm[0]);
        var destW = Math.Abs(ctm[0]);
        var destH = Math.Abs(ctm[3]);
        var topPdfY = Math.Max(ctm[5], ctm[5] + ctm[3]);
        if (destW < 0.01) destW = imgW;
        if (destH < 0.01) destH = imgH;

        // Convert to pixel coords. Round (not truncate) matches GDI+ behaviour on the
        // nearest integer pixel, keeping image placement aligned with the GDI+ renderer.
        var px = (int)Math.Round((destX - ctx.MediaBox.LLX) * ctx.Scale);
        var py = ctx.PixelH - (int)Math.Round((topPdfY - ctx.MediaBox.LLY) * ctx.Scale);
        var pw = (int)Math.Round(destW * ctx.Scale);
        var ph = (int)Math.Round(destH * ctx.Scale);

        if (isImageMask)
        {
            // /Decode [a b]: when a > b (e.g. [1 0]) the bit-to-opacity mapping is
            // inverted from the default. PDF 32000 §8.9.5.1: Decode component values
            // map source samples to colour-component values; for ImageMasks the
            // default is [0 1] (sample 0 ⇒ paint, 1 ⇒ transparent) and [1 0] flips it.
            var invertDecode = false;
            if (dict.Get("Decode") is PdfArray decodeArr && decodeArr.Count >= 2)
                invertDecode = NumFrom(decodeArr[0]) > NumFrom(decodeArr[1]);
            // A mask painted while a /Pattern fill is active (e.g. PowerPoint masks a
            // gradient pattern through a stencil) shows the pattern, not a flat colour;
            // painting the stale solid fill over-inks it dark. Fill the stencil with the
            // pattern instead.
            if (state.FillPatternName is not null)
            {
                DrawImageMaskWithPattern(ctx, decoded, imgW, imgH, px, py, pw, ph, state, invertDecode);
                return;
            }
            DrawImageMask(ctx, decoded, imgW, imgH, px, py, pw, ph, state, invertDecode);
            return;
        }

        // Decode pixels based on color space
        var bpc = (int)dict.GetInt("BitsPerComponent");
        if (bpc == 0) bpc = 8;
        var csInfo = ResolveImageColorSpace(dict.Get("ColorSpace"), ctx.Reader);
        var cs = csInfo.BaseName;

        // Decode JPEG images
        if (decoded.Length > 2 && decoded[0] == 0xFF && decoded[1] == 0xD8)
        {
            try
            {
                var jpeg = IO.Filters.JpegDecoder.Decode(decoded, CmykDecodeInverts(dict));
                if (jpeg.components == 1)
                    BlitGray(ctx, jpeg.pixels, jpeg.width, jpeg.height, px, py, pw, ph, smask, smaskW, smaskH, state.FillAlpha);
                else if (jpeg.components >= 3)
                    BlitRGB(ctx, jpeg.pixels, jpeg.width, jpeg.height, px, py, pw, ph, smask, smaskW, smaskH, state.FillAlpha);
                return;
            }
            catch
            {
                // No fallback — leave area as page background rather than painting a
                // false gray rectangle over the area's real content.
                return;
            }
        }

        // JPEG 2000 (JPXDecode filter): a JP2 box wrapper (signature box
        // 00 00 00 0C 6A 50 …) or a bare J2K codestream (0xFF 0x4F SOC).
        var isJ2kFile = decoded.Length > 12
            && decoded[0] == 0x00 && decoded[1] == 0x00 && decoded[2] == 0x00 && decoded[3] == 0x0C
            && decoded[4] == 0x6A && decoded[5] == 0x50;
        var isJ2kCodestream = decoded.Length > 4 && decoded[0] == 0xFF && decoded[1] == 0x4F;
        if (isJ2kFile || isJ2kCodestream)
        {
            if (IO.Filters.JpxDecoder.TryDecode(decoded, out var jp, out var jw, out var jh, out var jc))
            {
                // Single-component JPX under /Indexed: the samples are palette
                // indices, not gray levels — look them up before blitting.
                if (jc == 1 && csInfo.Palette is not null)
                {
                    var rgbIdx = DecodeIndexedToRgb(jp, jw, jh, 8, csInfo);
                    if (rgbIdx is not null)
                        BlitRGB(ctx, rgbIdx, jw, jh, px, py, pw, ph, smask, smaskW, smaskH, state.FillAlpha);
                }
                else if (jc >= 3)
                    BlitRGB(ctx, jp, jw, jh, px, py, pw, ph, smask, smaskW, smaskH, state.FillAlpha);
                else
                    BlitGray(ctx, jp, jw, jh, px, py, pw, ph, smask, smaskW, smaskH, state.FillAlpha);
            }
            return;
        }

        // Indexed: unpack bit-packed palette indices and look up RGB per pixel.
        // 4-bpc indexed is common for palette-based screenshots; 8-bpc indexed also appears.
        if (csInfo.Palette is not null)
        {
            BlitIndexed(ctx, decoded, imgW, imgH, px, py, pw, ph, bpc, csInfo);
            return;
        }

        // CTM-driven mirror flags: a negative ctm[3] flips image data vertically
        // (image-data is top-down per PDF spec, so the rendered raster has to be
        // sampled bottom-up to land upright). Negative ctm[0] mirrors horizontally.
        var flipY = ctm[3] < 0;
        var flipX = ctm[0] < 0;

        // Render raw pixel data
        if (cs == "DeviceRGB" && bpc == 8 && decoded.Length >= imgW * imgH * 3)
            BlitRGB(ctx, decoded, imgW, imgH, px, py, pw, ph, smask, smaskW, smaskH, state.FillAlpha, flipY, flipX);
        else if (cs == "DeviceCMYK" && bpc == 8 && decoded.Length >= imgW * imgH * 4)
            BlitCMYK(ctx, decoded, imgW, imgH, px, py, pw, ph);
        else if (cs == "DeviceGray" && bpc == 8 && decoded.Length >= imgW * imgH)
            BlitGray(ctx, decoded, imgW, imgH, px, py, pw, ph, smask, smaskW, smaskH, state.FillAlpha);
        else if (cs == "DeviceGray" && (bpc == 2 || bpc == 4))
            BlitGray(ctx, UnpackGraySamples(decoded, imgW, imgH, bpc, GrayDecodeInverts(dict)), imgW, imgH, px, py, pw, ph, smask, smaskW, smaskH, state.FillAlpha);
        else if (csInfo.TintTransform is not null && (bpc is 1 or 2 or 4 or 8))
            // Single-colorant /Separation (or /DeviceN) image: map each sample through the
            // tint transform. A 1-bpc /Separation/Black image (sample 1 ⇒ full ink) would
            // otherwise reach BlitBilevel and render inverted (e.g. a white-on-black
            // graphic comes out black-on-white).
            BlitRGB(ctx, SeparationSamplesToRgb(decoded, imgW, imgH, bpc, BuildSeparationLut(csInfo, GrayDecodeInverts(dict))),
                imgW, imgH, px, py, pw, ph, smask, smaskW, smaskH, state.FillAlpha, flipY, flipX);
        else if (bpc == 1)
            BlitBilevel(ctx, decoded, imgW, imgH, px, py, pw, ph, GrayDecodeInverts(dict));
        // No fallback — a solid gray rectangle over an unrecognised image is false
        // content. Leaving the area white (page background) is less damaging
        // than painting a false rectangle over the area's real content.
    }

    /// <summary>
    /// Decode an image XObject into a flat RGBA buffer (w*h*4) for the affine blit path
    /// (rotated/skewed CTMs). ImageMask pixels carry the current fill colour with alpha
    /// 0/255; other formats are opaque (alpha 255) unless an /SMask supplies per-pixel
    /// alpha. Returns null for formats this path doesn't recognise.
    /// </summary>
    private static (byte[] rgba, int w, int h)? DecodeImageToRgba(RenderContext ctx, PdfDictionary dict,
        byte[] decoded, int imgW, int imgH, bool isImageMask, byte[]? smask, int smaskW, int smaskH, GraphicsState state)
    {
        if (imgW <= 0 || imgH <= 0 || (long)imgW * imgH * 4 > int.MaxValue) return null;

        if (isImageMask)
        {
            var rgbaM = new byte[imgW * imgH * 4];
            byte fr = (byte)(state.FillR * 255), fg = (byte)(state.FillG * 255), fb = (byte)(state.FillB * 255);
            var invert = dict.Get("Decode") is PdfArray dm && dm.Count >= 2 && NumFrom(dm[0]) > NumFrom(dm[1]);
            int paintBit = invert ? 1 : 0;
            long rb = (imgW + 7) / 8;
            for (int y = 0; y < imgH; y++)
                for (int x = 0; x < imgW; x++)
                {
                    var bi = y * rb + (x >> 3);
                    int bit = bi < decoded.Length ? (decoded[(int)bi] >> (7 - (x & 7))) & 1 : 1;
                    if (bit == paintBit) { var o = (y * imgW + x) * 4; rgbaM[o] = fr; rgbaM[o + 1] = fg; rgbaM[o + 2] = fb; rgbaM[o + 3] = 255; }
                }
            return (rgbaM, imgW, imgH);
        }

        var bpc = (int)dict.GetInt("BitsPerComponent"); if (bpc == 0) bpc = 8;
        var csInfo = ResolveImageColorSpace(dict.Get("ColorSpace"), ctx.Reader);
        var cs = csInfo.BaseName;

        byte[]? rgb = null; int rw = imgW, rh = imgH;
        if (decoded.Length > 2 && decoded[0] == 0xFF && decoded[1] == 0xD8)
        {
            try { var j = IO.Filters.JpegDecoder.Decode(decoded, CmykDecodeInverts(dict)); rw = j.width; rh = j.height; rgb = j.components == 1 ? GrayToRgbBuf(j.pixels, rw, rh) : j.pixels; }
            catch { return null; }
        }
        else if ((decoded.Length > 12 && decoded[0] == 0 && decoded[1] == 0 && decoded[2] == 0 && decoded[3] == 0x0C && decoded[4] == 0x6A && decoded[5] == 0x50)
                 || (decoded.Length > 4 && decoded[0] == 0xFF && decoded[1] == 0x4F))
        {
            if (IO.Filters.JpxDecoder.TryDecode(decoded, out var jp, out var jw, out var jh, out var jc))
            {
                rw = jw; rh = jh;
                // Single-component JPX under /Indexed carries palette indices.
                rgb = jc == 1 && csInfo.Palette is not null
                    ? DecodeIndexedToRgb(jp, jw, jh, 8, csInfo)
                    : jc >= 3 ? jp : GrayToRgbBuf(jp, jw, jh);
                if (rgb is null) return null;
            }
            else return null;
        }
        else if (csInfo.Palette is not null) rgb = IndexedToRgbBuf(decoded, imgW, imgH, bpc, csInfo);
        else if (cs == "DeviceRGB" && bpc == 8 && decoded.Length >= imgW * imgH * 3) rgb = decoded;
        else if (cs == "DeviceCMYK" && bpc == 8 && decoded.Length >= imgW * imgH * 4) rgb = CmykToRgbBuf(decoded, imgW, imgH);
        else if (cs == "DeviceGray" && bpc == 8 && decoded.Length >= imgW * imgH) rgb = GrayToRgbBuf(decoded, imgW, imgH);
        else if (cs == "DeviceGray" && (bpc == 2 || bpc == 4)) rgb = GrayToRgbBuf(UnpackGraySamples(decoded, imgW, imgH, bpc, GrayDecodeInverts(dict)), imgW, imgH);
        else if (csInfo.TintTransform is not null && bpc is 1 or 2 or 4 or 8) rgb = SeparationSamplesToRgb(decoded, imgW, imgH, bpc, BuildSeparationLut(csInfo, GrayDecodeInverts(dict)));
        else if (bpc == 1) rgb = BilevelToRgbBuf(decoded, imgW, imgH, GrayDecodeInverts(dict));
        else return null;

        if (rgb is null || rgb.Length < rw * rh * 3) return null;
        var rgba = new byte[rw * rh * 4];
        for (int i = 0, j = 0, k = 0; i < rw * rh; i++, j += 3, k += 4) { rgba[k] = rgb[j]; rgba[k + 1] = rgb[j + 1]; rgba[k + 2] = rgb[j + 2]; rgba[k + 3] = 255; }

        if (smask is not null && smaskW > 0 && smaskH > 0 && smask.Length >= smaskW * smaskH)
            for (int y = 0; y < rh; y++)
                for (int x = 0; x < rw; x++)
                    rgba[(y * rw + x) * 4 + 3] = smask[(y * smaskH / rh) * smaskW + (x * smaskW / rw)];

        return (rgba, rw, rh);
    }

    private static byte[] GrayToRgbBuf(byte[] g, int w, int h)
    {
        var o = new byte[w * h * 3];
        for (int i = 0, j = 0; i < w * h; i++, j += 3) { byte v = i < g.Length ? g[i] : (byte)255; o[j] = o[j + 1] = o[j + 2] = v; }
        return o;
    }

    private static byte[] CmykToRgbBuf(byte[] d, int w, int h)
    {
        var o = new byte[w * h * 3];
        for (int i = 0, j = 0, k = 0; i < w * h; i++, j += 4, k += 3)
        {
            CmykToRgbClamp(d[j] / 255.0, d[j + 1] / 255.0, d[j + 2] / 255.0, d[j + 3] / 255.0, out var r, out var gg, out var b);
            o[k] = ToByteClamp(r * 255); o[k + 1] = ToByteClamp(gg * 255); o[k + 2] = ToByteClamp(b * 255);
        }
        return o;
    }

    private static byte[] BilevelToRgbBuf(byte[] d, int w, int h, bool invert = false)
    {
        var o = new byte[w * h * 3]; long rb = (w + 7) / 8;
        var inv = invert ? 1 : 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var bi = y * rb + (x >> 3);
                byte v = (byte)(bi < d.Length ? ((((d[(int)bi] >> (7 - (x & 7))) & 1) ^ inv) == 1 ? 255 : 0) : 255);
                var k = (y * w + x) * 3; o[k] = o[k + 1] = o[k + 2] = v;
            }
        return o;
    }

    private static byte[] IndexedToRgbBuf(byte[] data, int w, int h, int bpc, ImageColorSpaceInfo cs)
    {
        var pal = cs.Palette!; var pc = cs.PaletteComponents; var o = new byte[w * h * 3];
        var rowBytes = (w * bpc + 7) / 8; var maxIdx = pc > 0 ? pal.Length / pc - 1 : 0;
        for (int y = 0; y < h; y++)
        {
            var rb = (long)y * rowBytes;
            for (int x = 0; x < w; x++)
            {
                int idx;
                if (bpc == 8) { var bi = rb + x; idx = bi < data.Length ? data[(int)bi] : 0; }
                else { var bit = x * bpc; var bi = rb + bit / 8; idx = bi < data.Length ? (data[(int)bi] >> (8 - bpc - (bit & 7))) & ((1 << bpc) - 1) : 0; }
                if (idx > maxIdx) idx = maxIdx; if (idx < 0) idx = 0;
                var po = idx * pc; var k = (y * w + x) * 3;
                if (pc >= 3 && po + 2 < pal.Length) { o[k] = pal[po]; o[k + 1] = pal[po + 1]; o[k + 2] = pal[po + 2]; }
                else if (pc == 1 && po < pal.Length) { o[k] = o[k + 1] = o[k + 2] = pal[po]; }
            }
        }
        return o;
    }

    /// <summary>
    /// Paint an RGBA source image under an arbitrary affine CTM by inverse-mapping each
    /// destination pixel back into the image's unit square. Used for rotated/skewed image
    /// CTMs that the axis-aligned blit paths can't represent (point-sampled).
    /// </summary>
    private static void BlitRgbaAffine(RenderContext ctx, byte[] rgba, int srcW, int srcH, double[] ctm, double fillAlpha)
    {
        if (srcW <= 0 || srcH <= 0 || rgba.Length < srcW * srcH * 4) return;
        double det = ctm[0] * ctm[3] - ctm[1] * ctm[2];
        if (Math.Abs(det) < 1e-12) return;
        double inv = 1.0 / det;

        // Destination bounding box from the four transformed unit-square corners.
        double x0 = ctm[4], x1 = ctm[4] + ctm[0], x2 = ctm[4] + ctm[2], x3 = ctm[4] + ctm[0] + ctm[2];
        double y0 = ctm[5], y1 = ctm[5] + ctm[1], y2 = ctm[5] + ctm[3], y3 = ctm[5] + ctm[1] + ctm[3];
        double minX = Math.Min(Math.Min(x0, x1), Math.Min(x2, x3)), maxX = Math.Max(Math.Max(x0, x1), Math.Max(x2, x3));
        double minY = Math.Min(Math.Min(y0, y1), Math.Min(y2, y3)), maxY = Math.Max(Math.Max(y0, y1), Math.Max(y2, y3));
        int pxMin = Math.Max(0, (int)Math.Floor((minX - ctx.MediaBox.LLX) * ctx.Scale));
        int pxMax = Math.Min(ctx.PixelW, (int)Math.Ceiling((maxX - ctx.MediaBox.LLX) * ctx.Scale));
        int pyMin = Math.Max(0, ctx.PixelH - (int)Math.Ceiling((maxY - ctx.MediaBox.LLY) * ctx.Scale));
        int pyMax = Math.Min(ctx.PixelH, ctx.PixelH - (int)Math.Floor((minY - ctx.MediaBox.LLY) * ctx.Scale));

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
                int sx = (int)(u * srcW); if (sx >= srcW) sx = srcW - 1;
                int sy = (int)((1.0 - v) * srcH); if (sy >= srcH) sy = srcH - 1; if (sy < 0) sy = 0;
                int o = (sy * srcW + sx) * 4;
                byte a = rgba[o + 3];
                if (a == 0) continue;
                byte fa = fillAlpha >= 1.0 ? a : (byte)(a * fillAlpha);
                SetPixel(ctx, dx, dy, rgba[o], rgba[o + 1], rgba[o + 2], fa);
            }
        }
    }

    /// <summary>
    /// Render an inline image (BI/ID/EI). Inline images carry the same fields as
    /// regular image XObjects but inside the content stream — most commonly used
    /// for Type 3 font glyphs (each character is a tiny ImageMask). Honours
    /// /ImageMask + /Decode [a b], applies any /Filter chain via StreamFilter,
    /// and paints through the existing DrawImageMask / BlitRGB / BlitGray paths.
    /// CTM at the BI operator (captured by the caller via parser.State.Ctm) maps
    /// the unit square to the destination rect — same convention as XObject Do.
    /// </summary>
    private static void DrawInlineImage(RenderContext ctx, PdfDictionary dict, byte[] data, GraphicsState state)
    {
        var imgW = (int)dict.GetInt("Width");
        var imgH = (int)dict.GetInt("Height");
        if (imgW <= 0 || imgH <= 0) return;

        byte[] decoded;
        try { decoded = IO.Filters.StreamFilter.Decode(data, dict); }
        catch { return; }

        var ctm = state.Ctm;
        var destX = ctm[4];
        var destY = ctm[5];
        var destW = Math.Abs(ctm[0]);
        var destH = Math.Abs(ctm[3]);
        if (destW < 0.01) destW = imgW;
        if (destH < 0.01) destH = imgH;
        var px = (int)Math.Round((destX - ctx.MediaBox.LLX) * ctx.Scale);
        var py = ctx.PixelH - (int)Math.Round((destY + destH - ctx.MediaBox.LLY) * ctx.Scale);
        var pw = (int)Math.Round(destW * ctx.Scale);
        var ph = (int)Math.Round(destH * ctx.Scale);

        var isMask = dict.Get("ImageMask") is PdfBoolean im && im.Value;
        if (isMask)
        {
            var invertDecode = false;
            if (dict.Get("Decode") is PdfArray decodeArr && decodeArr.Count >= 2)
                invertDecode = NumFrom(decodeArr[0]) > NumFrom(decodeArr[1]);
            if (state.FillPatternName is not null)
            {
                DrawImageMaskWithPattern(ctx, decoded, imgW, imgH, px, py, pw, ph, state, invertDecode);
                return;
            }
            DrawImageMask(ctx, decoded, imgW, imgH, px, py, pw, ph, state, invertDecode);
            return;
        }

        var bpc = (int)dict.GetInt("BitsPerComponent");
        if (bpc == 0) bpc = 8;
        var csInfo = ResolveImageColorSpace(dict.Get("ColorSpace"), ctx.Reader);
        var cs = csInfo.BaseName;
        if (cs == "DeviceRGB" && bpc == 8 && decoded.Length >= imgW * imgH * 3)
            BlitRGB(ctx, decoded, imgW, imgH, px, py, pw, ph, null, 0, 0, state.FillAlpha);
        else if (cs == "DeviceGray" && bpc == 8 && decoded.Length >= imgW * imgH)
            BlitGray(ctx, decoded, imgW, imgH, px, py, pw, ph, null, 0, 0, state.FillAlpha);
        else if (bpc == 1)
            BlitBilevel(ctx, decoded, imgW, imgH, px, py, pw, ph, GrayDecodeInverts(dict));
    }

    /// <summary>True when a DeviceGray image's /Decode array reverses the default [0 1]
    /// mapping (i.e. sample 0 ⇒ white instead of black).</summary>
    internal static bool GrayDecodeInverts(PdfDictionary dict)
        => dict.Get("Decode") is PdfArray d && d.Count >= 2 && NumFrom(d[0]) > NumFrom(d[1]);

    /// <summary>An inverting 4-colour /Decode ([1 0 1 0 1 0 1 0]) on a DCT image —
    /// the embedder's signal that the JPEG stores Adobe-inverted CMYK samples
    /// rather than direct ink values.</summary>
    internal static bool CmykDecodeInverts(PdfDictionary dict)
        => dict.Get("Decode") is PdfArray d && d.Count >= 8 && NumFrom(d[0]) > NumFrom(d[1]);

    /// <summary>
    /// Expand a sub-byte (1/2/4-bpc) DeviceGray image to one 8-bit grey byte per pixel.
    /// Samples are packed MSB-first and each row starts on a byte boundary (PDF 32000
    /// §8.9.5.2). The N-bit value is scaled to 0..255; /Decode [1 0] inverts it.
    /// </summary>
    internal static byte[] UnpackGraySamples(byte[] data, int w, int h, int bpc, bool invert)
    {
        var outp = new byte[w * h];
        var rowBytes = (w * bpc + 7) / 8;
        var maxv = (1 << bpc) - 1;
        for (int y = 0; y < h; y++)
        {
            int rowBase = y * rowBytes;
            for (int x = 0; x < w; x++)
            {
                int bitPos = x * bpc;
                int bi = rowBase + (bitPos >> 3);
                int shift = 8 - bpc - (bitPos & 7);
                int sample = bi < data.Length ? (data[bi] >> shift) & maxv : 0;
                int v = sample * 255 / maxv;
                outp[y * w + x] = (byte)(invert ? 255 - v : v);
            }
        }
        return outp;
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
        int px, int py, int pw, int ph, GraphicsState state, bool invertDecode = false)
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

            // Inverse-map this dest row into a [srcY0, srcY1) source span.
            var srcY0 = (long)dy * imgH / ph;
            var srcY1 = (long)(dy + 1) * imgH / ph;
            if (srcY1 == srcY0) srcY1 = srcY0 + 1;

            for (int dx = 0; dx < pw; dx++)
            {
                var destX = px + dx;
                if (destX < 0 || destX >= ctx.PixelW) continue;

                var srcX0 = (long)dx * imgW / pw;
                var srcX1 = (long)(dx + 1) * imgW / pw;
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

    // ── Annotation rendering ────────────────────────────────────────

    /// <summary>
    /// Paint annotations on top of the page. Currently supports text-markup annotations
    /// (/Highlight, /Underline, /StrikeOut, /Squiggly) because they have a simple geometric
    /// appearance derived from /QuadPoints. Other subtypes carry an /AP appearance stream
    /// which we don't yet rasterise.
    /// </summary>
    private static void DrawAnnotations(RenderContext ctx, PdfDictionary pageDict)
    {
        var annots = ctx.Reader.Resolve(pageDict.Get("Annots")) as PdfArray;
        if (annots is null) return;
        foreach (var item in annots)
        {
            var annot = ctx.Reader.ResolveDict(item);
            if (annot is null) continue;
            // Skip annotations that are hidden (bit 2 of /F). Bits are 1-indexed in the spec.
            var flags = (int)annot.GetInt("F");
            if ((flags & 0x02) != 0) continue;
            var subtype = annot.GetName("Subtype");
            if (subtype == "Highlight")
            {
                DrawHighlightAnnotation(ctx, annot);
                continue;
            }
            // For everything else, first try the /AP /N appearance stream per
            // PDF 32000-1:2008 §12.5.5. Covers /FreeText, /Stamp, /Widget, /Popup,
            // /Ink, /Line, /Polygon and any subtype that ships its appearance
            // as a Form XObject.
            var hasAppearance = ctx.Reader.ResolveDict(annot.Get("AP")) is not null;
            if (hasAppearance)
            {
                DrawAppearanceAnnotation(ctx, annot);
                continue;
            }
            // No /AP → the spec requires we generate a default appearance from
            // the subtype's attributes. /Square and /Circle have a simple
            // default (filled/stroked rectangle or ellipse in page space).
            // PDF 32000-1:2008 §12.5.6.8.
            if (subtype == "Square" || subtype == "Circle")
            {
                DrawSquareCircleDefault(ctx, annot, isCircle: subtype == "Circle");
            }
            // A /Text (sticky-note) annotation without /AP renders as its standard
            // note icon (PDF 32000 §12.5.6.4): NoZoom notes stretch the icon into
            // /Rect; otherwise the icon paints at its natural size anchored at the
            // rectangle's top-left corner.
            else if (subtype == "Text")
            {
                var form = Aspose.Pdf.Annotations.TextAnnotationIcons.BuildIconForm(annot, ctx.Reader);
                DrawAppearanceForm(ctx, annot, form, (flags & 0x08) != 0 ? null : TextIconNaturalRect(annot));
            }
            // Open /Popup notes carry no /AP (they are interactive UI); synthesise the
            // comment box so "render comments to image" bakes it in. PDF 32000 §12.5.6.14.
            else if (subtype == "Popup")
            {
                var form = Aspose.Pdf.Annotations.PopupAppearance.BuildOpenPopupForm(annot, ctx.Reader);
                if (form is not null) DrawAppearanceForm(ctx, annot, form);
            }
        }
    }

    /// <summary>
    /// Paint an annotation whose visual is expressed as an /AP /N Form XObject.
    /// Computes the CTM that maps the appearance stream's transformed /BBox into the
    /// annotation's page-space /Rect (PDF 32000 §12.5.5), then dispatches to the
    /// regular Form XObject renderer so all normal content-stream operators work
    /// (text, images, paths, nested XObjects).
    /// </summary>
    private static void DrawAppearanceAnnotation(RenderContext ctx, PdfDictionary annot)
    {
        var ap = ctx.Reader.ResolveDict(annot.Get("AP"));
        if (ap is null) return;
        // /N can be either a stream directly (simple markup annotations) or a dict
        // of appearance-state → stream (checkboxes, radio buttons, multi-state Widgets)
        // selected by the annotation's /AS entry (PDF 32000 §12.5.5).
        var nEntry = ctx.Reader.Resolve(ap.Get("N"));
        PdfStream? formStream = null;
        if (nEntry is PdfStream direct)
        {
            formStream = direct;
        }
        else if (nEntry is PdfDictionary stateDict)
        {
            var asName = annot.GetName("AS");
            if (!string.IsNullOrEmpty(asName))
                formStream = ctx.Reader.ResolveStream(stateDict.Get(asName));
        }
        if (formStream is null) return;
        DrawAppearanceForm(ctx, annot, formStream);
    }

    /// <summary>Paint a Form XObject appearance into the annotation's /Rect (PDF 32000
    /// §12.5.5). Shared by the /AP path and synthesised appearances (e.g. open popups).</summary>
    /// <summary>Natural-size target box for a note icon: the icon's own box
    /// anchored at the annotation rectangle's top-left corner.</summary>
    private static (double MinX, double MinY, double MaxX, double MaxY)? TextIconNaturalRect(PdfDictionary annot)
    {
        if (annot.Get("Rect") is not PdfArray rect || rect.Count < 4) return null;
        double rx1 = NumFrom(rect[0]), ry1 = NumFrom(rect[1]);
        double rx2 = NumFrom(rect[2]), ry2 = NumFrom(rect[3]);
        double minX = Math.Min(rx1, rx2), maxY = Math.Max(ry1, ry2);
        var s = Aspose.Pdf.Annotations.TextAnnotationIcons.BoxSize;
        return (minX, maxY - s, minX + s, maxY);
    }

    private static void DrawAppearanceForm(RenderContext ctx, PdfDictionary annot, PdfStream formStream,
        (double MinX, double MinY, double MaxX, double MaxY)? targetRect = null)
    {
        double rMinX, rMinY, rMaxX, rMaxY;
        if (targetRect is { } tr)
        {
            (rMinX, rMinY, rMaxX, rMaxY) = tr;
        }
        else
        {
            if (annot.Get("Rect") is not PdfArray rect || rect.Count < 4) return;

            // Normalise /Rect (some PDFs emit it with corners out of order).
            double rx1 = NumFrom(rect[0]), ry1 = NumFrom(rect[1]);
            double rx2 = NumFrom(rect[2]), ry2 = NumFrom(rect[3]);
            rMinX = Math.Min(rx1, rx2); rMaxX = Math.Max(rx1, rx2);
            rMinY = Math.Min(ry1, ry2); rMaxY = Math.Max(ry1, ry2);
        }
        var rW = rMaxX - rMinX; var rH = rMaxY - rMinY;
        if (rW <= 0 || rH <= 0) return;

        // Transform the form's /BBox through its /Matrix to get the source rectangle
        // in the form's post-matrix space. DrawFormXObject concatenates /Matrix again,
        // so the outer CTM we build here must operate on the *post-matrix* bbox.
        if (formStream.Dict.Get("BBox") is not PdfArray bbox || bbox.Count < 4) return;
        double bx1 = NumFrom(bbox[0]), by1 = NumFrom(bbox[1]);
        double bx2 = NumFrom(bbox[2]), by2 = NumFrom(bbox[3]);
        var formMatrix = ExtractFormMatrix(formStream.Dict) ?? new double[] { 1, 0, 0, 1, 0, 0 };
        double tMinX = double.PositiveInfinity, tMinY = double.PositiveInfinity;
        double tMaxX = double.NegativeInfinity, tMaxY = double.NegativeInfinity;
        foreach (var (cx, cy) in new[] { (bx1, by1), (bx2, by1), (bx2, by2), (bx1, by2) })
        {
            var tx = formMatrix[0] * cx + formMatrix[2] * cy + formMatrix[4];
            var ty = formMatrix[1] * cx + formMatrix[3] * cy + formMatrix[5];
            if (tx < tMinX) tMinX = tx;
            if (tx > tMaxX) tMaxX = tx;
            if (ty < tMinY) tMinY = ty;
            if (ty > tMaxY) tMaxY = ty;
        }
        var tW = tMaxX - tMinX; var tH = tMaxY - tMinY;
        if (tW <= 0 || tH <= 0) return;

        var sx = rW / tW;
        var sy = rH / tH;
        // Outer CTM maps the form's transformed bbox origin to /Rect's lower-left.
        // DrawFormXObject will left-multiply this with /Matrix internally.
        var outerCtm = new double[]
        {
            sx, 0, 0, sy,
            rMinX - tMinX * sx,
            rMinY - tMinY * sy,
        };
        var state = new GraphicsState();
        state.Ctm = outerCtm;
        // Annotations are painted after page content; any clip mask left over from the
        // content stream would wrongly clip the annotation. Clear before rendering,
        // restore after so subsequent annotations start from a clean state too.
        var savedClip = ctx.ClipMask;
        ctx.ClipMask = null;
        DrawFormXObject(ctx, formStream, state, null);
        ctx.ClipMask = savedClip;
    }

    /// <summary>
    /// Render a /Highlight annotation as a multiply-blended coloured rectangle per QuadPoint
    /// quad, falling back to the single /Rect if QuadPoints is absent. Multiply blending
    /// (PDF §11.3.5.3) preserves any text underneath — which is exactly why Acrobat uses it
    /// for highlights.
    /// </summary>
    private static void DrawHighlightAnnotation(RenderContext ctx, PdfDictionary annot)
    {
        // /C is the annotation colour in the current colour space (1, 3, or 4 components).
        // Default to yellow — Acrobat's factory default for the highlight tool.
        var color = ResolveColorArray(annot.Get("C")) ?? new[] { 1.0, 1.0, 0.0 };
        var (hr, hg, hb) = ColorToRgb(color);

        var quads = ctx.Reader.Resolve(annot.Get("QuadPoints")) as PdfArray;
        if (quads is not null && quads.Count >= 8 && quads.Count % 8 == 0)
        {
            // Each quad is 8 numbers: x1 y1 x2 y2 x3 y3 x4 y4 — the four corners in an
            // implementation-specific order. We compute the axis-aligned bounding box of
            // the four corners and fill it; that matches what Acrobat does visually for
            // axis-aligned text lines (the usual case).
            for (var i = 0; i + 8 <= quads.Count; i += 8)
            {
                double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
                double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
                for (var k = 0; k < 4; k++)
                {
                    var qx = ToDouble(quads[i + k * 2]);
                    var qy = ToDouble(quads[i + k * 2 + 1]);
                    if (qx < minX) minX = qx;
                    if (qx > maxX) maxX = qx;
                    if (qy < minY) minY = qy;
                    if (qy > maxY) maxY = qy;
                }
                // Acrobat / GDI+ shrink the QuadPoints box so the colour only covers from
                // the glyph cap-height down to just below the baseline; the box as written
                // in the annotation typically extends ~25–30% further down into the
                // descender/leading area. Match that visually so our output doesn't spill
                // yellow into blank line-gaps.
                var height = maxY - minY;
                minY += height * 0.28;
                FillMultiplyRect(ctx, minX, minY, maxX, maxY, hr, hg, hb);
            }
            return;
        }

        var rect = ctx.Reader.Resolve(annot.Get("Rect")) as PdfArray;
        if (rect is not null && rect.Count >= 4)
        {
            var x1 = ToDouble(rect[0]);
            var y1 = ToDouble(rect[1]);
            var x2 = ToDouble(rect[2]);
            var y2 = ToDouble(rect[3]);
            FillMultiplyRect(ctx, Math.Min(x1, x2), Math.Min(y1, y2),
                Math.Max(x1, x2), Math.Max(y1, y2), hr, hg, hb);
        }
    }

    /// <summary>
    /// Render the default appearance of a /Square or /Circle annotation when
    /// it lacks an /AP /N stream. PDF 32000 §12.5.6.8: a Square's default
    /// appearance is the /Rect filled with the interior colour /IC and stroked
    /// with /C using /BS's line width (default 1 pt). /Circle is the same
    /// shape fit inside /Rect.
    /// </summary>
    private static void DrawSquareCircleDefault(RenderContext ctx, PdfDictionary annot, bool isCircle)
    {
        if (ctx.Reader.Resolve(annot.Get("Rect")) is not PdfArray rect || rect.Count < 4) return;

        double x1 = ToDouble(rect[0]);
        double y1 = ToDouble(rect[1]);
        double x2 = ToDouble(rect[2]);
        double y2 = ToDouble(rect[3]);
        var minX = Math.Min(x1, x2); var maxX = Math.Max(x1, x2);
        var minY = Math.Min(y1, y2); var maxY = Math.Max(y1, y2);
        if (maxX <= minX || maxY <= minY) return;

        // Interior colour (/IC). Optional — no fill if absent.
        var icColor = ResolveColorArray(annot.Get("IC"));

        // Border colour (/C). Optional — default black when a border is drawn.
        var borderArr = ResolveColorArray(annot.Get("C"));
        byte br = 0, bg = 0, bb = 0;
        var borderColorSet = borderArr is not null;
        if (borderArr is not null)
        {
            var (r, g, b) = ColorToRgb(borderArr);
            br = r; bg = g; bb = b;
        }

        // Border width from /BS /W (preferred) or legacy /Border [hr vr w].
        // Default 1pt per spec.
        double borderW = 1.0;
        if (ctx.Reader.ResolveDict(annot.Get("BS")) is PdfDictionary bs)
        {
            var w = ctx.Reader.Resolve(bs.Get("W"));
            if (w is not null) borderW = ToDouble(w);
        }
        else if (ctx.Reader.Resolve(annot.Get("Border")) is PdfArray legacyBorder && legacyBorder.Count >= 3)
        {
            borderW = ToDouble(legacyBorder[2]);
        }

        // Convert page-space rect to pixel-space. CTM for base-page rendering
        // is identity apart from the DPI scale + the y-flip.
        var pxMinX = (int)Math.Round((minX - ctx.MediaBox.LLX) * ctx.Scale);
        var pxMaxX = (int)Math.Round((maxX - ctx.MediaBox.LLX) * ctx.Scale);
        var pxMinY = (int)Math.Round(ctx.PixelH - (maxY - ctx.MediaBox.LLY) * ctx.Scale);
        var pxMaxY = (int)Math.Round(ctx.PixelH - (minY - ctx.MediaBox.LLY) * ctx.Scale);

        // Fill interior.
        if (icColor is not null)
        {
            var (ir, ig, ib) = ColorToRgb(icColor);
            if (isCircle)
                FillEllipse(ctx, pxMinX, pxMinY, pxMaxX, pxMaxY, ir, ig, ib, 255);
            else
                FillRect(ctx, pxMinX, pxMinY, pxMaxX - pxMinX, pxMaxY - pxMinY, ir, ig, ib, 255);
        }

        // Stroke border only when /C is explicitly specified. Acrobat's
        // default-appearance behavior is:
        // no /C → no visible outline, regardless of /IC or /BS. Without /IC
        // and without /C the annotation collapses to nothing on the page.
        if (borderW > 0 && borderColorSet)
        {
            var lw = (float)(borderW * ctx.Scale);
            if (lw < 1) lw = 1;
            var clip = ctx.ClipMask;
            // Draw 4 line segments forming the rect outline. Circle fallback
            // also uses the rect outline; a proper elliptical stroke can come
            // later. Callers only see Circle here when /IC is set too.
            ScanlineFiller.StrokeLine(ctx.Pixels, ctx.PixelW, ctx.PixelH,
                pxMinX, pxMinY, pxMaxX, pxMinY, br, bg, bb, 255, lw, clip,
                blendMode: ctx.CurrentBlendMode, knockout: ctx.IsKnockoutGroup, softMask: ctx.SoftMaskAlpha);
            ScanlineFiller.StrokeLine(ctx.Pixels, ctx.PixelW, ctx.PixelH,
                pxMaxX, pxMinY, pxMaxX, pxMaxY, br, bg, bb, 255, lw, clip,
                blendMode: ctx.CurrentBlendMode, knockout: ctx.IsKnockoutGroup, softMask: ctx.SoftMaskAlpha);
            ScanlineFiller.StrokeLine(ctx.Pixels, ctx.PixelW, ctx.PixelH,
                pxMaxX, pxMaxY, pxMinX, pxMaxY, br, bg, bb, 255, lw, clip,
                blendMode: ctx.CurrentBlendMode, knockout: ctx.IsKnockoutGroup, softMask: ctx.SoftMaskAlpha);
            ScanlineFiller.StrokeLine(ctx.Pixels, ctx.PixelW, ctx.PixelH,
                pxMinX, pxMaxY, pxMinX, pxMinY, br, bg, bb, 255, lw, clip,
                blendMode: ctx.CurrentBlendMode, knockout: ctx.IsKnockoutGroup, softMask: ctx.SoftMaskAlpha);
        }
    }

    // Simple axis-aligned ellipse fill using the implicit equation — good
    // enough for /Circle annotation defaults. Pixel-space coordinates.
    private static void FillEllipse(RenderContext ctx, int minX, int minY, int maxX, int maxY,
        byte r, byte g, byte b, byte a)
    {
        var cx = (minX + maxX) / 2.0;
        var cy = (minY + maxY) / 2.0;
        var rx = (maxX - minX) / 2.0;
        var ry = (maxY - minY) / 2.0;
        if (rx <= 0 || ry <= 0) return;

        var y0 = Math.Max(0, minY);
        var y1 = Math.Min(ctx.PixelH, maxY);
        var x0 = Math.Max(0, minX);
        var x1 = Math.Min(ctx.PixelW, maxX);
        for (var y = y0; y < y1; y++)
        {
            var dy = (y - cy) / ry;
            for (var x = x0; x < x1; x++)
            {
                var dx = (x - cx) / rx;
                if (dx * dx + dy * dy <= 1.0)
                    SetPixel(ctx, x, y, r, g, b, a);
            }
        }
    }

    /// <summary>Convert a PDF colour array (1/3/4 components in 0..1) to sRGB bytes.</summary>
    private static (byte r, byte g, byte b) ColorToRgb(double[] c)
    {
        if (c.Length == 4)
        {
            // CMYK runs through the embedded ICC LUT — the algebraic
            // (1−C)(1−K) formula gives spec-correct but visually-off
            // results vs. GDI+ / Acrobat. See CmykToRgbLut for the
            // background.
            return CmykToRgbLut.Convert(c[0], c[1], c[2], c[3]);
        }

        double r, g, b;
        if (c.Length == 1)
        {
            r = g = b = c[0];
        }
        else
        {
            r = c[0]; g = c.Length > 1 ? c[1] : c[0]; b = c.Length > 2 ? c[2] : c[0];
        }
        return ((byte)Math.Clamp(r * 255, 0, 255),
                (byte)Math.Clamp(g * 255, 0, 255),
                (byte)Math.Clamp(b * 255, 0, 255));
    }

    private static double[]? ResolveColorArray(PdfObject? obj)
    {
        if (obj is not PdfArray arr) return null;
        var result = new double[arr.Count];
        for (var i = 0; i < arr.Count; i++) result[i] = ToDouble(arr[i]);
        return result;
    }

    private static double ToDouble(PdfObject? obj) => obj switch
    {
        PdfInteger pi => pi.Value,
        PdfReal pr => pr.Value,
        _ => 0,
    };

    /// <summary>
    /// Fill a user-space rectangle into the pixel buffer using PDF Multiply blending
    /// (result = dest × src). Used for translucent text-markup annotations such as
    /// /Highlight, where the colour must not obscure the text it covers.
    /// </summary>
    private static void FillMultiplyRect(RenderContext ctx, double x1, double y1,
        double x2, double y2, byte sr, byte sg, byte sb)
    {
        // 2-pixel outward dilation on each edge: an anti-aliased rasterisation of
        // the rectangle carries a couple of rows/columns of partial-coverage
        // edge pixels (half-yellow). Our hard-edged multiply produces no such
        // fringe, so we paint those edge pixels fully — within a 6-pixel
        // neighbourhood the edge colouring then comes out close either way.
        var px1 = (int)Math.Floor((x1 - ctx.MediaBox.LLX) * ctx.Scale) - 2;
        var px2 = (int)Math.Ceiling((x2 - ctx.MediaBox.LLX) * ctx.Scale) + 2;
        // PDF y grows upward; pixel y grows downward, so the lower-left corner becomes the
        // higher pixel-y and vice versa.
        var py1 = (int)Math.Floor(ctx.PixelH - (y2 - ctx.MediaBox.LLY) * ctx.Scale) - 2;
        var py2 = (int)Math.Ceiling(ctx.PixelH - (y1 - ctx.MediaBox.LLY) * ctx.Scale) + 2;

        if (px1 < 0) px1 = 0;
        if (py1 < 0) py1 = 0;
        if (px2 > ctx.PixelW) px2 = ctx.PixelW;
        if (py2 > ctx.PixelH) py2 = ctx.PixelH;
        if (px1 >= px2 || py1 >= py2) return;

        var pix = ctx.Pixels;
        for (var y = py1; y < py2; y++)
        {
            var rowBase = y * ctx.PixelW * 4;
            for (var x = px1; x < px2; x++)
            {
                var p = rowBase + x * 4;
                pix[p]     = (byte)(pix[p]     * sr / 255);
                pix[p + 1] = (byte)(pix[p + 1] * sg / 255);
                pix[p + 2] = (byte)(pix[p + 2] * sb / 255);
                // Alpha stays fully opaque — the highlight doesn't punch through the page.
            }
        }
    }

    // ── Form XObject rendering ──────────────────────────────────────

    // Pathological PDFs can chain Form XObjects cyclically. Cap recursion to protect
    // the renderer from stack exhaustion / infinite loops.
    [ThreadStatic] private static int _formDepth;
}
