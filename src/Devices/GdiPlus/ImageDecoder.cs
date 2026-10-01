using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Text;
using GdiColor = System.Drawing.Color;
using GdiMatrix = System.Drawing.Drawing2D.Matrix;
using GraphicsState = Aspose.Pdf.Content.GraphicsState;
using GdiState = System.Drawing.Drawing2D.GraphicsState;
namespace Aspose.Pdf.Devices;

public sealed partial class GdiPlusPageRenderer : IPageRenderer
{
    // ── Image decoding (PDF image XObject / inline → GDI+ bitmap) ────

    /// <summary>
    /// Decodes PDF image streams into 32bpp ARGB GDI+ bitmaps oriented top-row-first.
    /// Image masks bake the current fill colour and per-pixel opacity into the alpha
    /// channel; soft masks (/SMask) are sampled into alpha. GDI+ then resamples with
    /// high-quality bicubic interpolation when the bitmap is placed.
    /// </summary>
    private static partial class ImageDecoder
    {
        public static Bitmap? TryDecode(PdfStream xobj, GraphicsState state, PdfReader reader, bool pdfxOverprintSim = false,
            bool replicateChroma = false)
        {
            var dict = xobj.Dict;
            byte[] decoded;
            try { decoded = reader.DecodeStream(xobj); }
            catch { return null; }
            return Build(dict, decoded, state, reader, pdfxOverprintSim, replicateChroma);
        }

        public static Bitmap? TryDecodeInline(PdfDictionary dict, byte[] data, GraphicsState state, PdfReader reader, bool pdfxOverprintSim = false)
        {
            byte[] decoded;
            try { decoded = Aspose.Pdf.IO.Filters.StreamFilter.Decode(data, dict); }
            catch { return null; }
            return Build(dict, decoded, state, reader, pdfxOverprintSim);
        }

        private static Bitmap? Build(PdfDictionary dict, byte[] decoded, GraphicsState state, PdfReader reader, bool pdfxOverprintSim = false, bool replicateChroma = false)
        {
            var w = (int)dict.GetInt("Width");
            var h = (int)dict.GetInt("Height");
            if (w <= 0 || h <= 0) return null;

            if (dict.Get("ImageMask") is PdfBoolean imb && imb.Value)
            {
                var invert = false;
                if (dict.Get("Decode") is PdfArray dec && dec.Count >= 2)
                    invert = NumFrom(dec[0]) > NumFrom(dec[1]);
                return BuildMask(decoded, w, h, state, invert);
            }

            var bpc = (int)dict.GetInt("BitsPerComponent");
            if (bpc == 0) bpc = 8;
            var csInfo = SoftwarePageRenderer.ResolveImageColorSpace(dict.Get("ColorSpace"), reader);
            // PDF/X + fill overprint: a spot-plate image decodes as colorant
            // COVERAGE (white at tint 0) so the overprint Multiply simulates the
            // plate instead of compositing its alternate colour.
            var opSim = pdfxOverprintSim && state.OverprintFill;

            // JPEG carried verbatim through DCTDecode.
            if (TryDecodeJpegImage(dict, reader, decoded, csInfo, opSim, replicateChroma) is { } jpegBmp) return jpegBmp;

            // JPEG 2000 (JPXDecode): raw codestream (FF4F) or JP2 box wrapper.
            bool isJ2k = (decoded.Length > 3 && decoded[0] == 0xFF && decoded[1] == 0x4F)
                || (decoded.Length > 12 && decoded[0] == 0x00 && decoded[1] == 0x00 && decoded[2] == 0x00 && decoded[3] == 0x0C
                    && decoded[4] == 0x6A && decoded[5] == 0x50);
            if (isJ2k)
            {
                if (Aspose.Pdf.IO.Filters.JpxDecoder.TryDecode(decoded) is (var jp, var jw, var jh, var jc))
                {
                    // A single-component JPX codestream under an /Indexed colour space
                    // carries palette indices, not gray levels — look each sample up in
                    // the palette (a solid-colour glow sprite otherwise renders black).
                    var bgraJ = jc == 1 && csInfo.Palette is not null
                        ? IndexedToBgra(jp, jw, jh, 8, csInfo)
                        : jc >= 3 ? RgbToBgra(jp, jw, jh) : GrayToBgra(jp, jw, jh);
                    var (mb, mw, mh) = ApplyMasks(dict, reader, bgraJ, jw, jh);
                    return FromBgra(mb, mw, mh);
                }
                return null;
            }

            byte[]? bgra = null;
            if (csInfo.Palette is not null)
                bgra = IndexedToBgra(decoded, w, h, bpc, csInfo);
            else if (csInfo.TintTransform is not null && csInfo.TintComponents > 1 && bpc == 8)
                // Multi-colorant /DeviceN image: each pixel is an N-sample tint tuple
                // mapped through the tint transform into the alternate space.
                bgra = DeviceNToBgra(decoded, w, h, csInfo);
            else if (csInfo.TintTransform is not null && bpc == 8 && decoded.Length >= w * h)
                bgra = SeparationToBgra(decoded, w, h,
                    (opSim ? SoftwarePageRenderer.BuildSeparationOverprintLut(csInfo, DecodeInverts(dict)) : SoftwarePageRenderer.BuildSeparationLut(csInfo, DecodeInverts(dict))));
            else if (csInfo.TintTransform is not null && csInfo.TintComponents <= 1 && (bpc == 1 || bpc == 2 || bpc == 4))
                // Sub-byte /Separation (or /DeviceN) image: map each sample through the tint
                // transform LUT. Without this a 1-bpc spot image falls to BilevelToBgra, which
                // ignores the colorant (e.g. a white-on-black graphic renders inverted).
                bgra = RgbToBgra(SoftwarePageRenderer.SeparationSamplesToRgb(decoded, w, h, bpc,
                    (opSim ? SoftwarePageRenderer.BuildSeparationOverprintLut(csInfo, SoftwarePageRenderer.GrayDecodeInverts(dict)) : SoftwarePageRenderer.BuildSeparationLut(csInfo, SoftwarePageRenderer.GrayDecodeInverts(dict)))), w, h);
            else if (csInfo.BaseName == "DeviceRGB" && bpc == 8 && decoded.Length >= w * h * 3)
                bgra = RgbToBgra(decoded, w, h);
            else if (csInfo.BaseName == "DeviceRGB" && (bpc == 1 || bpc == 2 || bpc == 4))
                // Sub-byte (3·bpc bits/pixel) DeviceRGB: unpack each component. Without this a
                // 1-bpc RGB image falls to BilevelToBgra (1 bit/pixel) and the rows desync.
                bgra = RgbToBgra(UnpackRgbSamples(decoded, w, h, bpc), w, h);
            else if (csInfo.BaseName == "DeviceGray" && bpc == 8 && decoded.Length >= w * h)
                bgra = GrayToBgra(decoded, w, h);
            else if (csInfo.BaseName == "DeviceGray" && (bpc == 2 || bpc == 4))
                bgra = GrayToBgra(SoftwarePageRenderer.UnpackGraySamples(decoded, w, h, bpc, SoftwarePageRenderer.GrayDecodeInverts(dict)), w, h);
            else if (csInfo.BaseName == "DeviceCMYK" && bpc == 8 && decoded.Length >= w * h * 4)
                bgra = CmykToBgra(decoded, w, h);
            else if (bpc == 1)
                // /Decode [1 0] (common on BlackIs1 CCITT scans) reverses the default
                // bit → gray mapping; without it such scans render white-on-black.
                bgra = BilevelToBgra(decoded, w, h, DecodeInverts(dict));

            if (bgra is null) return null;
            var (mbgra, mw2, mh2) = ApplyMasks(dict, reader, bgra, w, h);
            return FromBgra(mbgra, mw2, mh2);
        }

        // Unpack a sub-byte (1/2/4 bpc) three-component DeviceRGB image into packed 8-bit RGB.
        // Each pixel is 3·bpc bits; rows are byte-aligned. Without this a 1-bpc RGB image
        // (3 bits/pixel) is mis-read as 1-bit bilevel (1 bit/pixel), desyncing the rows.
        private static byte[] UnpackRgbSamples(byte[] data, int w, int h, int bpc)
        {
            var outp = new byte[w * h * 3];
            var rowBytes = (w * 3 * bpc + 7) / 8;
            var maxv = (1 << bpc) - 1;
            for (int y = 0; y < h; y++)
            {
                int rowBase = y * rowBytes;
                for (int x = 0; x < w; x++)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        int bitPos = (x * 3 + c) * bpc;
                        int bi = rowBase + (bitPos >> 3);
                        int shift = 8 - bpc - (bitPos & 7);
                        int sample = bi < data.Length ? (data[bi] >> shift) & maxv : 0;
                        outp[(y * w + x) * 3 + c] = (byte)(sample * 255 / maxv);
                    }
                }
            }
            return outp;
        }

        private static Bitmap BuildMask(byte[] decoded, int w, int h, GraphicsState state, bool invert)
        {
            byte r = (byte)Clamp255(state.FillR), g = (byte)Clamp255(state.FillG), b = (byte)Clamp255(state.FillB);
            byte paintAlpha = (byte)Clamp255(state.FillAlpha);
            // Default /Decode [0 1]: bit 0 paints the fill colour, bit 1 is transparent.
            int paintBit = invert ? 1 : 0;
            var rowBytes = (w + 7) / 8;
            var bgra = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
            {
                var rowBase = y * rowBytes;
                for (int x = 0; x < w; x++)
                {
                    var bi = rowBase + x / 8;
                    if (bi >= decoded.Length) continue;
                    var bit = (decoded[bi] >> (7 - (x & 7))) & 1;
                    if (bit != paintBit) continue; // transparent
                    var o = (y * w + x) * 4;
                    bgra[o + 0] = b; bgra[o + 1] = g; bgra[o + 2] = r; bgra[o + 3] = paintAlpha;
                }
            }
            return FromBgra(bgra, w, h);
        }

        private static byte[] RgbToBgra(byte[] rgb, int w, int h)
        {
            var bgra = new byte[w * h * 4];
            for (int i = 0, j = 0; i < w * h; i++, j += 4)
            {
                var s = i * 3;
                bgra[j + 0] = rgb[s + 2]; bgra[j + 1] = rgb[s + 1]; bgra[j + 2] = rgb[s + 0]; bgra[j + 3] = 255;
            }
            return bgra;
        }

        private static byte[] GrayToBgra(byte[] gray, int w, int h)
        {
            var bgra = new byte[w * h * 4];
            for (int i = 0, j = 0; i < w * h; i++, j += 4)
            {
                var v = gray[i];
                bgra[j + 0] = v; bgra[j + 1] = v; bgra[j + 2] = v; bgra[j + 3] = 255;
            }
            return bgra;
        }

        // Map a single-component (Separation/DeviceN) sample plane to BGRA via the
        // precomputed tint LUT (sample → spot tint → alternate space → RGB).
        // Multi-colorant /DeviceN samples: evaluate the tint transform per distinct
        // sample tuple (memoised — frame art uses few distinct inks) into the
        // alternate space, then to RGB.
        private static byte[]? DeviceNToBgra(byte[] samples, int w, int h,
            SoftwarePageRenderer.ImageColorSpaceInfo cs)
        {
            int n = cs.TintComponents;
            if (n < 2 || n > 4 || cs.TintTransform is null) return null;
            long need = (long)w * h * n;
            if (samples.Length < need) return null;
            var bgra = new byte[w * h * 4];
            var memo = new Dictionary<int, (byte r, byte g, byte b)>();
            var input = new double[n];
            for (int p = 0, j = 0; p < w * h; p++, j += 4)
            {
                int off = p * n;
                int key = 0;
                for (int c = 0; c < n; c++) key = (key << 8) | samples[off + c];
                if (!memo.TryGetValue(key, out var col))
                {
                    for (int c = 0; c < n; c++) input[c] = samples[off + c] / 255.0;
                    byte r = 255, g = 255, b = 255;
                    var alt = cs.TintTransform.Evaluate(input);
                    if (alt is not null)
                        (r, g, b) = SoftwarePageRenderer.ComponentsToRgb(alt, cs.AltSpaceName ?? "DeviceCMYK");
                    col = (r, g, b);
                    memo[key] = col;
                }
                bgra[j] = col.b; bgra[j + 1] = col.g; bgra[j + 2] = col.r; bgra[j + 3] = 255;
            }
            return bgra;
        }

        private static byte[] SeparationToBgra(byte[] samples, int w, int h, byte[] lut)
        {
            var bgra = new byte[w * h * 4];
            int n = Math.Min(w * h, samples.Length);
            for (int i = 0, j = 0; i < n; i++, j += 4)
            {
                int l = samples[i] * 3;
                bgra[j + 0] = lut[l + 2]; bgra[j + 1] = lut[l + 1]; bgra[j + 2] = lut[l + 0]; bgra[j + 3] = 255;
            }
            return bgra;
        }

        // /Decode [1 0] on a 1-component image reverses the sample → tint mapping.
        private static bool DecodeInverts(PdfDictionary dict)
            => dict.Get("Decode") is PdfArray dec && dec.Count >= 2 && NumFrom(dec[0]) > NumFrom(dec[1]);

        // CMYK samples go through the same ICC-style conversion as CMYK fills
        // (CmykToRgbLut): a photo and the flat tint beside it must agree, and the
        // naive subtractive formula lands far off (oversaturated, zero blue for
        // Y=1 inks). A small memo keeps the per-pixel cost down on flat regions.
        private static byte[] CmykToBgra(byte[] cmyk, int w, int h)
        {
            var bgra = new byte[w * h * 4];
            var memo = new Dictionary<int, (byte r, byte g, byte b)>();
            for (int i = 0, j = 0; i < w * h; i++, j += 4)
            {
                var s = i * 4;
                var key = (cmyk[s] << 24) | (cmyk[s + 1] << 16) | (cmyk[s + 2] << 8) | cmyk[s + 3];
                if (!memo.TryGetValue(key, out var rgb))
                {
                    rgb = CmykToRgbLut.Convert(cmyk[s] / 255.0, cmyk[s + 1] / 255.0, cmyk[s + 2] / 255.0, cmyk[s + 3] / 255.0);
                    memo[key] = rgb;
                }
                bgra[j + 0] = rgb.b;
                bgra[j + 1] = rgb.g;
                bgra[j + 2] = rgb.r;
                bgra[j + 3] = 255;
            }
            return bgra;
        }

        private static byte[] BilevelToBgra(byte[] data, int w, int h, bool invert = false)
        {
            var bgra = new byte[w * h * 4];
            var rowBytes = (w + 7) / 8;
            var inv = invert ? 1 : 0;
            for (int y = 0; y < h; y++)
            {
                var rowBase = y * rowBytes;
                for (int x = 0; x < w; x++)
                {
                    var bi = rowBase + x / 8;
                    byte v = 0;
                    if (bi < data.Length) v = (((data[bi] >> (7 - (x & 7))) & 1) ^ inv) == 1 ? (byte)255 : (byte)0;
                    var o = (y * w + x) * 4;
                    bgra[o + 0] = v; bgra[o + 1] = v; bgra[o + 2] = v; bgra[o + 3] = 255;
                }
            }
            return bgra;
        }

        private static byte[] IndexedToBgra(byte[] data, int w, int h, int bpc, SoftwarePageRenderer.ImageColorSpaceInfo csInfo)
        {
            var palette = csInfo.Palette!;
            var pc = csInfo.PaletteComponents;
            var bgra = new byte[w * h * 4];
            var rowBits = w * bpc;
            var rowBytes = (rowBits + 7) / 8;
            var maxIndex = pc > 0 ? palette.Length / pc - 1 : 0;
            for (int y = 0; y < h; y++)
            {
                var rowBase = y * rowBytes;
                for (int x = 0; x < w; x++)
                {
                    int idx = ReadBits(data, rowBase, x * bpc, bpc);
                    if (idx > maxIndex) idx = maxIndex;
                    (byte r, byte g, byte b) = PaletteRgb(palette, pc, csInfo.BaseName, idx);
                    var o = (y * w + x) * 4;
                    bgra[o + 0] = b; bgra[o + 1] = g; bgra[o + 2] = r; bgra[o + 3] = 255;
                }
            }
            return bgra;
        }

        private static int ReadBits(byte[] data, int rowBase, int bitOffset, int bpc)
        {
            int value = 0;
            for (int i = 0; i < bpc; i++)
            {
                int bit = bitOffset + i;
                int bi = rowBase + bit / 8;
                int b = bi < data.Length ? (data[bi] >> (7 - (bit & 7))) & 1 : 0;
                value = (value << 1) | b;
            }
            return value;
        }

        private static (byte r, byte g, byte b) PaletteRgb(byte[] palette, int pc, string baseName, int idx)
        {
            byte r = default;
            byte g = default;
            byte b = default;
            var p = idx * pc;
            if (p < 0 || p + pc > palette.Length) { r = g = b = 0; return (r, g, b); }
            switch (baseName)
            {
                case "DeviceGray":
                    r = g = b = palette[p];
                    break;
                case "DeviceCMYK":
                    (r, g, b) = CmykToRgbLut.Convert(palette[p] / 255.0, palette[p + 1] / 255.0, palette[p + 2] / 255.0, palette[p + 3] / 255.0);
                    break;
                default: // DeviceRGB / Cal / ICC fallback
                    r = palette[p]; g = palette[p + 1]; b = palette[p + 2];
                    break;
            }
            return (r, g, b);
        }

        // Apply the /SMask soft mask and explicit /Mask stencil to a base-image BGRA buffer,
        // returning the (possibly resized) result. When the stencil is markedly higher
        // resolution than the base image the result is rebuilt at the stencil resolution so
        // its sharp edges survive the bicubic scale to the page — a low-res photo gated by a
        // high-res text stencil would otherwise lose ~half its strokes to point
        // sampling. Behaviour for soft-mask-only / equal-or-lower-res masks is unchanged.
        private static (byte[] bgra, int w, int h) ApplyMasks(PdfDictionary dict, PdfReader reader, byte[] bgra, int w, int h)
        {
            (var alpha, var sw, var sh) = SoftwarePageRenderer.ResolveSMaskAlpha(dict.Get("SMask"), reader);
            (var stencil, var stw, var sth) = SoftwarePageRenderer.ResolveStencilMaskAlpha(dict.Get("Mask"), reader);
            bool haveSMask = alpha is not null && sw > 0 && sh > 0;
            bool haveStencil = stencil is not null && stw > 0 && sth > 0;

            // Colour-key (chroma-key) masking: /Mask as a PdfArray of [min max] sample
            // ranges, one pair per colour component. A pixel whose samples all fall in
            // their range is fully transparent (PDF §8.9.6.4). Matched against the
            // post-conversion RGB buffer, so it is only applied to the 3-component (RGB)
            // form, where the buffer's R/G/B equal the raw samples for the device RGB
            // spaces. A 6-entry key cannot occur on a 1-component Indexed space, so this
            // avoids mis-masking Indexed/CMYK images whose buffer no longer holds samples.
            int[]? colorKey = null;
            if (reader.Resolve(dict.Get("Mask")) is PdfArray ck && ck.Count == 6)
            {
                colorKey = new int[ck.Count];
                for (int i = 0; i < ck.Count; i++) colorKey[i] = (int)NumFrom(ck[i]);
            }
            bool haveColorKey = colorKey is not null;

            if (!haveSMask && !haveStencil && !haveColorKey) return (bgra, w, h);

            // A /Matte entry means the colour samples are pre-blended against the matte
            // colour (premultiplied). The true colour is recovered per PDF §11.6.5.3:
            // c = m + (c' - m) / alpha. We only un-premultiply when the mask is (near-)
            // uniform — a flat translucent overlay (e.g. a tiled background texture)
            // where leaving the samples pre-blended visibly darkens the result. Shaped
            // masks (varying coverage, e.g. a vignetted photo) are left untouched: their
            // opaque interior needs no correction and dividing thin edges by tiny alpha
            // only blows highlights out to white.
            byte mB = 0, mG = 0, mR = 0;
            bool unmatte = false;
            if (haveSMask && reader.ResolveStream(dict.Get("SMask"))?.Dict.Get("Matte") is PdfArray matte && matte.Count > 0)
            {
                int amin = 255, amax = 0;
                foreach (var a in alpha!) { if (a < amin) amin = a; if (a > amax) amax = a; }
                if (amax - amin <= 8 && amax > 0)
                {
                    unmatte = true;
                    // Matte components are colour values in [0,1]; Clamp255 rescales that to
                    // a [0,255] byte. (Multiplying by 255 first double-scales and pins any
                    // non-zero matte to 255.)
                    double M(int i) => i < matte.Count ? NumFrom(matte[i]) : 0;
                    if (matte.Count >= 3) { mR = (byte)Clamp255(M(0)); mG = (byte)Clamp255(M(1)); mB = (byte)Clamp255(M(2)); }
                    else { var v = (byte)Clamp255(M(0)); mR = mG = mB = v; }
                }
            }

            // Output grid: the finest of the base image and its masks. A mask finer
            // than the base must set the grid — the common "text as alpha" idiom
            // stretches a 2×2 solid-colour base over a high-res text-shaped /SMask,
            // and compositing on the base grid would collapse the text to smears.
            int outW = w, outH = h;
            if (haveStencil && (long)stw * sth > (long)outW * outH) { outW = stw; outH = sth; }
            if (haveSMask && (long)sw * sh > (long)outW * outH) { outW = sw; outH = sh; }
            bool upscale = outW != w || outH != h;
            var outBgra = upscale ? new byte[outW * outH * 4] : bgra;

            ApplyMaskRows(bgra, w, h, outBgra, outW, outH, upscale, haveSMask, haveStencil, haveColorKey, unmatte,
                mB, mG, mR, alpha, sw, sh, stencil, stw, sth, colorKey);
            return (outBgra, outW, outH);
        }

        private static byte Unmatte(byte cPrime, byte matte, byte alpha)
        {
            // v is already in device [0,255] range; clamp directly. (Clamp255 expects a
            // normalised [0,1] value and rescales by 255, which would blow every non-255
            // channel out to white — the reason Matte'd images lost their mid-tones.)
            double v = matte + (cPrime - matte) * 255.0 / alpha;
            return (byte)(v < 0 ? 0 : v > 255 ? 255 : v);
        }

        private static Bitmap FromBgra(byte[] bgra, int w, int h)
        {
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            var rect = new System.Drawing.Rectangle(0, 0, w, h);
            var bits = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < h; y++)
                    System.Runtime.InteropServices.Marshal.Copy(bgra, y * w * 4, bits.Scan0 + y * bits.Stride, w * 4);
            }
            finally { bmp.UnlockBits(bits); }
            return bmp;
        }
    }
}
