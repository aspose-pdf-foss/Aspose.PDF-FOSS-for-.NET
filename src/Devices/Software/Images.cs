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
            if (IO.Filters.JpxDecoder.TryDecode(decoded) is (var jp, var jw, var jh, var jc))
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
        else if (cs == "DeviceRGB" && bpc is 1 or 2 or 4) rgb = UnpackComponentSamples(decoded, imgW, imgH, bpc, 3);
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
            // Same ICC-style conversion as CMYK fills (CmykToRgbLut), so an image and
            // the flat tint beside it agree.
            var (r, gg, b) = CmykToRgbLut.Convert(d[j] / 255.0, d[j + 1] / 255.0, d[j + 2] / 255.0, d[j + 3] / 255.0);
            o[k] = r; o[k + 1] = gg; o[k + 2] = b;
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

        // A single sample per destination pixel throws away every source pixel it did not
        // land on, and on a MINIFIED image that is most of them: a scanned page rotated onto
        // a landscape sheet and reduced 2.4x lost a third of its ink and came back visibly
        // thinner than the GDI+ render of the same scan. Supersample the destination pixel
        // when the image is being reduced, at a rate taken from how many source pixels it
        // covers, capped so a heavy reduction stays cheap.
        var uAxisPx = Math.Sqrt(ctm[0] * ctm[0] + ctm[1] * ctm[1]) * ctx.Scale;
        var vAxisPx = Math.Sqrt(ctm[2] * ctm[2] + ctm[3] * ctm[3]) * ctx.Scale;
        var perPixel = Math.Max(uAxisPx > 1e-9 ? srcW / uAxisPx : 1, vAxisPx > 1e-9 ? srcH / vAxisPx : 1);
        const int MaxSubSamples = 4;
        var n = (int)Math.Ceiling(perPixel);
        if (n < 1) n = 1; else if (n > MaxSubSamples) n = MaxSubSamples;
        var step = 1.0 / n;

        for (int dy = pyMin; dy < pyMax; dy++)
        {
            for (int dx = pxMin; dx < pxMax; dx++)
            {
                long ar = 0, ag = 0, ab = 0, aa = 0; int hit = 0;
                for (var jy = 0; jy < n; jy++)
                {
                    double uy = (ctx.PixelH - dy - (jy + 0.5) * step) / ctx.Scale + ctx.MediaBox.LLY;
                    for (var jx = 0; jx < n; jx++)
                    {
                        double ux = (dx + (jx + 0.5) * step) / ctx.Scale + ctx.MediaBox.LLX;
                        double rx = ux - ctm[4], ry = uy - ctm[5];
                        double u = (rx * ctm[3] - ry * ctm[2]) * inv;
                        double v = (-rx * ctm[1] + ry * ctm[0]) * inv;
                        if (u < 0 || u >= 1 || v < 0 || v >= 1) continue;
                        int sx = (int)(u * srcW); if (sx >= srcW) sx = srcW - 1;
                        int sy = (int)((1.0 - v) * srcH); if (sy >= srcH) sy = srcH - 1; if (sy < 0) sy = 0;
                        int o = (sy * srcW + sx) * 4;
                        ar += rgba[o]; ag += rgba[o + 1]; ab += rgba[o + 2]; aa += rgba[o + 3];
                        hit++;
                    }
                }
                if (hit == 0) continue;
                // Sub-samples that fell outside the unit square contribute nothing, so the
                // colour is the mean of the ones that landed while the alpha is scaled by
                // the covered fraction - that is what gives the image an anti-aliased edge.
                var total = n * n;
                byte a = (byte)((aa / hit) * hit / total);
                if (a == 0) continue;
                byte fa = fillAlpha >= 1.0 ? a : (byte)(a * fillAlpha);
                SetPixel(ctx, dx, dy, (byte)(ar / hit), (byte)(ag / hit), (byte)(ab / hit), fa);
            }
        }
    }

    private static void DrawInlineImage(RenderContext ctx, PdfDictionary dict, byte[] data, GraphicsState state)
    {
        var imgW = (int)dict.GetInt("Width");
        var imgH = (int)dict.GetInt("Height");
        if (imgW <= 0 || imgH <= 0) return;

        byte[] decoded;
        try { decoded = IO.Filters.StreamFilter.Decode(data, dict); }
        catch { return; }

        var ctm = state.Ctm;
        var isMask = dict.Get("ImageMask") is PdfBoolean im && im.Value;

        // A rotated or skewed CTM carries the image's whole scale in ctm[1]/ctm[2], leaving
        // ctm[0] and ctm[3] at zero — so the axis-aligned placement below reads a dest size
        // of 0, takes its "degenerate, assume 1:1" fallback, and paints the image at
        // imgW x imgH POINTS. On a /Rotate 90 page that is ~4x oversized and unrotated,
        // and a report page built from a few hundred inline images turns into a wall of
        // black. `Do` XObjects have had the affine path since the /Rotate 270 CCITT case;
        // inline images take exactly the same route.
        if (Math.Abs(ctm[1]) > 1e-4 || Math.Abs(ctm[2]) > 1e-4)
        {
            if (isMask && state.FillPatternName is not null)
            {
                var invAff = dict.Get("Decode") is PdfArray dmA && dmA.Count >= 2 && NumFrom(dmA[0]) > NumFrom(dmA[1]);
                DrawImageMaskWithPatternAffine(ctx, decoded, imgW, imgH, ctm, state, invAff);
                return;
            }
            var aff = DecodeImageToRgba(ctx, dict, decoded, imgW, imgH, isMask, null, 0, 0, state);
            if (aff is not null) BlitRgbaAffine(ctx, aff.Value.rgba, aff.Value.w, aff.Value.h, ctm, state.FillAlpha);
            return;
        }

        // Mirror-aware placement, the same contract DrawImage uses: either bound of the
        // unit square can be the higher one, so take the MIN x and the MAX y rather than
        // assuming a positive scale. Type 3 glyphs are inline ImageMasks drawn under a
        // negative ctm[3] (glyph space is y-up inside a y-down text run), so without this
        // every glyph on a dot-matrix invoice came out upside down AND misplaced.
        var destX = Math.Min(ctm[4], ctm[4] + ctm[0]);
        var destW = Math.Abs(ctm[0]);
        var destH = Math.Abs(ctm[3]);
        var topPdfY = Math.Max(ctm[5], ctm[5] + ctm[3]);
        if (destW < 0.01) destW = imgW;
        if (destH < 0.01) { destH = imgH; topPdfY = ctm[5] + destH; }
        var flipY = ctm[3] < 0;
        var flipX = ctm[0] < 0;
        var px = (int)Math.Round((destX - ctx.MediaBox.LLX) * ctx.Scale);
        var py = ctx.PixelH - (int)Math.Round((topPdfY - ctx.MediaBox.LLY) * ctx.Scale);
        // A rectangle that is non-empty in user space must cover at least one device
        // pixel. A raster logo exploded into ~2700 one-unit-tall inline scanline strips
        // (0.12 pt each under the page's 0.12 scale = 0.25 px at 150 dpi) rounded EVERY
        // strip to a height of zero and the whole logo silently vanished; GDI+ paints
        // each sliver's partial coverage instead. Clamp so a thin sliver still lands.
        var pw = Math.Max(1, (int)Math.Round(destW * ctx.Scale));
        var ph = Math.Max(1, (int)Math.Round(destH * ctx.Scale));

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
            DrawImageMask(ctx, decoded, imgW, imgH, px, py, pw, ph, state, invertDecode, flipY, flipX);
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
    /// <summary>
    /// Widen a bit-packed multi-component image to 8 bits per component. A 1/2/4-bit
    /// DeviceRGB pixel is 3 (or 4 for CMYK) samples of that width packed back to back,
    /// with each ROW padded to a byte boundary - not one bit per pixel, which is what the
    /// bilevel path assumed when it caught these: a 1-bit RGB scan came out as horizontal
    /// streaks because every row was read at a third of its true stride.
    /// </summary>
    internal static byte[] UnpackComponentSamples(byte[] data, int w, int h, int bpc, int comps)
    {
        var outp = new byte[w * h * comps];
        var rowBytes = (w * comps * bpc + 7) / 8;
        var maxv = (1 << bpc) - 1;
        for (int y = 0; y < h; y++)
        {
            long rowBase = (long)y * rowBytes;
            for (int x = 0; x < w; x++)
            {
                for (int cIdx = 0; cIdx < comps; cIdx++)
                {
                    int bitPos = (x * comps + cIdx) * bpc;
                    long bi = rowBase + (bitPos >> 3);
                    int shift = 8 - bpc - (bitPos & 7);
                    int sample = bi < data.Length ? (data[(int)bi] >> shift) & maxv : 0;
                    outp[(y * w + x) * comps + cIdx] = (byte)(sample * 255 / maxv);
                }
            }
        }
        return outp;
    }

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

    // ── Annotation rendering ────────────────────────────────────────

    /// <summary>Map an annotation's axis-aligned page-space rectangle through the same base
    /// CTM and return its axis-aligned bounds - exact for the quarter-turn rotations /Rotate
    /// allows and for the canvas stretch.</summary>
    private static (double MinX, double MinY, double MaxX, double MaxY) MapAnnotRect(
        RenderContext ctx, double minX, double minY, double maxX, double maxY)
    {
        if (ctx.PageCtm is not { } m) return (minX, minY, maxX, maxY);
        double oMinX = double.PositiveInfinity, oMinY = double.PositiveInfinity;
        double oMaxX = double.NegativeInfinity, oMaxY = double.NegativeInfinity;
        foreach (var (cx, cy) in new[] { (minX, minY), (maxX, minY), (maxX, maxY), (minX, maxY) })
        {
            var tx = m[0] * cx + m[2] * cy + m[4];
            var ty = m[1] * cx + m[3] * cy + m[5];
            if (tx < oMinX) oMinX = tx;
            if (tx > oMaxX) oMaxX = tx;
            if (ty < oMinY) oMinY = ty;
            if (ty > oMaxY) oMaxY = ty;
        }
        return (oMinX, oMinY, oMaxX, oMaxY);
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
        return ((byte)Compat.Clamp(r * 255, 0, 255),
                (byte)Compat.Clamp(g * 255, 0, 255),
                (byte)Compat.Clamp(b * 255, 0, 255));
    }

    // ── Form XObject rendering ──────────────────────────────────────

    // Pathological PDFs can chain Form XObjects cyclically. Cap recursion to protect
    // the renderer from stack exhaustion / infinite loops.
    [ThreadStatic] private static int _formDepth;
}
