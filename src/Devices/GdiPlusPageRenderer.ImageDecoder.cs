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
    private static class ImageDecoder
    {
        public static Bitmap? TryDecode(PdfStream xobj, GraphicsState state, PdfReader reader)
        {
            var dict = xobj.Dict;
            byte[] decoded;
            try { decoded = reader.DecodeStream(xobj); }
            catch { return null; }
            return Build(dict, decoded, state, reader);
        }

        public static Bitmap? TryDecodeInline(PdfDictionary dict, byte[] data, GraphicsState state, PdfReader reader)
        {
            byte[] decoded;
            try { decoded = Aspose.Pdf.IO.Filters.StreamFilter.Decode(data, dict); }
            catch { return null; }
            return Build(dict, decoded, state, reader);
        }

        private static Bitmap? Build(PdfDictionary dict, byte[] decoded, GraphicsState state, PdfReader reader)
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

            // JPEG carried verbatim through DCTDecode.
            if (decoded.Length > 2 && decoded[0] == 0xFF && decoded[1] == 0xD8)
            {
                try
                {
                    var (pixels, jw, jh, comps) = Aspose.Pdf.IO.Filters.JpegDecoder.Decode(decoded,
                        SoftwarePageRenderer.CmykDecodeInverts(dict));
                    byte[] bgraJ;
                    if (comps == 1 && csInfo.TintTransform is not null)
                        bgraJ = SeparationToBgra(pixels, jw, jh,
                            SoftwarePageRenderer.BuildSeparationLut(csInfo, DecodeInverts(dict)));
                    else
                        bgraJ = comps == 1 ? GrayToBgra(pixels, jw, jh) : RgbToBgra(pixels, jw, jh);
                    var (mb, mw, mh) = ApplyMasks(dict, reader, bgraJ, jw, jh);
                    return FromBgra(mb, mw, mh);
                }
                catch { return null; }
            }

            // JPEG 2000 (JPXDecode): raw codestream (FF4F) or JP2 box wrapper.
            bool isJ2k = (decoded.Length > 3 && decoded[0] == 0xFF && decoded[1] == 0x4F)
                || (decoded.Length > 12 && decoded[0] == 0x00 && decoded[1] == 0x00 && decoded[2] == 0x00 && decoded[3] == 0x0C
                    && decoded[4] == 0x6A && decoded[5] == 0x50);
            if (isJ2k)
            {
                if (Aspose.Pdf.IO.Filters.JpxDecoder.TryDecode(decoded, out var jp, out var jw, out var jh, out var jc))
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
            else if (csInfo.TintTransform is not null && bpc == 8 && decoded.Length >= w * h)
                bgra = SeparationToBgra(decoded, w, h,
                    SoftwarePageRenderer.BuildSeparationLut(csInfo, DecodeInverts(dict)));
            else if (csInfo.TintTransform is not null && (bpc == 1 || bpc == 2 || bpc == 4))
                // Sub-byte /Separation (or /DeviceN) image: map each sample through the tint
                // transform LUT. Without this a 1-bpc spot image falls to BilevelToBgra, which
                // ignores the colorant (e.g. a white-on-black graphic renders inverted).
                bgra = RgbToBgra(SoftwarePageRenderer.SeparationSamplesToRgb(decoded, w, h, bpc,
                    SoftwarePageRenderer.BuildSeparationLut(csInfo, SoftwarePageRenderer.GrayDecodeInverts(dict))), w, h);
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

        private static byte[] CmykToBgra(byte[] cmyk, int w, int h)
        {
            var bgra = new byte[w * h * 4];
            for (int i = 0, j = 0; i < w * h; i++, j += 4)
            {
                var s = i * 4;
                double c = cmyk[s] / 255.0, m = cmyk[s + 1] / 255.0, yv = cmyk[s + 2] / 255.0, k = cmyk[s + 3] / 255.0;
                bgra[j + 0] = (byte)(255 * (1 - yv) * (1 - k));
                bgra[j + 1] = (byte)(255 * (1 - m) * (1 - k));
                bgra[j + 2] = (byte)(255 * (1 - c) * (1 - k));
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
                    PaletteRgb(palette, pc, csInfo.BaseName, idx, out byte r, out byte g, out byte b);
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

        private static void PaletteRgb(byte[] palette, int pc, string baseName, int idx, out byte r, out byte g, out byte b)
        {
            var p = idx * pc;
            if (p < 0 || p + pc > palette.Length) { r = g = b = 0; return; }
            switch (baseName)
            {
                case "DeviceGray":
                    r = g = b = palette[p];
                    break;
                case "DeviceCMYK":
                    double c = palette[p] / 255.0, m = palette[p + 1] / 255.0, yv = palette[p + 2] / 255.0, k = palette[p + 3] / 255.0;
                    r = (byte)(255 * (1 - c) * (1 - k));
                    g = (byte)(255 * (1 - m) * (1 - k));
                    b = (byte)(255 * (1 - yv) * (1 - k));
                    break;
                default: // DeviceRGB / Cal / ICC fallback
                    r = palette[p]; g = palette[p + 1]; b = palette[p + 2];
                    break;
            }
        }

        // Apply the /SMask soft mask and explicit /Mask stencil to a base-image BGRA buffer,
        // returning the (possibly resized) result. When the stencil is markedly higher
        // resolution than the base image the result is rebuilt at the stencil resolution so
        // its sharp edges survive the bicubic scale to the page — a low-res photo gated by a
        // high-res text stencil would otherwise lose ~half its strokes to point
        // sampling. Behaviour for soft-mask-only / equal-or-lower-res masks is unchanged.
        private static (byte[] bgra, int w, int h) ApplyMasks(PdfDictionary dict, PdfReader reader, byte[] bgra, int w, int h)
        {
            var alpha = SoftwarePageRenderer.ResolveSMaskAlpha(dict.Get("SMask"), reader, out var sw, out var sh);
            var stencil = SoftwarePageRenderer.ResolveStencilMaskAlpha(dict.Get("Mask"), reader, out var stw, out var sth);
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

            for (int y = 0; y < outH; y++)
            {
                for (int x = 0; x < outW; x++)
                {
                    var o = (y * outW + x) * 4;
                    if (upscale)
                    {
                        var bo = ((y * h / outH) * w + (x * w / outW)) * 4;
                        outBgra[o + 0] = bgra[bo + 0];
                        outBgra[o + 1] = bgra[bo + 1];
                        outBgra[o + 2] = bgra[bo + 2];
                    }
                    int a = 255;
                    if (haveSMask)
                    {
                        var sy = sh == outH ? y : (int)((long)y * sh / outH);
                        var sx = sw == outW ? x : (int)((long)x * sw / outW);
                        var ai = sy * sw + sx;
                        a = ai < alpha!.Length ? alpha[ai] : 255;
                    }
                    if (haveStencil)
                    {
                        var ty = sth == outH ? y : (int)((long)y * sth / outH);
                        var tx = stw == outW ? x : (int)((long)x * stw / outW);
                        var ti = ty * stw + tx;
                        if (ti < stencil!.Length) a = a * stencil[ti] / 255;
                    }
                    if (haveColorKey && a > 0)
                    {
                        // outBgra holds B,G,R at o..o+2 (post-conversion device colour).
                        int pb = outBgra[o + 0], pg = outBgra[o + 1], pr = outBgra[o + 2];
                        if (pr >= colorKey![0] && pr <= colorKey[1]
                            && pg >= colorKey[2] && pg <= colorKey[3]
                            && pb >= colorKey[4] && pb <= colorKey[5])
                            a = 0;
                    }
                    if (unmatte && a > 0)
                    {
                        outBgra[o + 0] = Unmatte(outBgra[o + 0], mB, (byte)a);
                        outBgra[o + 1] = Unmatte(outBgra[o + 1], mG, (byte)a);
                        outBgra[o + 2] = Unmatte(outBgra[o + 2], mR, (byte)a);
                    }
                    outBgra[o + 3] = (byte)a;
                }
            }
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
