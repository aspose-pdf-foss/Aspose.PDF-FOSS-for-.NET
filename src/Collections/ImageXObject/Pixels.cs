using System.Collections;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

public partial class ImageXObject
{
    /// <summary>
    /// Build a streaming pixel source (RGB getter) for the decoded image.
    /// Returns null for color spaces the simple converter doesn't handle.
    /// Decodes pixels on demand — never materialises the full RGB buffer,
    /// so it works for arbitrarily large images (1-bit scanned pages etc.).
    /// </summary>
    internal IO.PixelGetter? GetPixelSource()
    {
        var bpc = BitsPerComponent;
        var w = Width;
        var decoded = _reader.DecodeStream(_stream);

        // JPEG 2000 (JPXDecode): decode the codestream to raw samples.
        if (IsJpeg2000 && IO.Filters.JpxDecoder.TryDecode(decoded, out var jp, out var jw, out var jh, out var jc))
        {
            return (int x, int y, out byte r, out byte g, out byte b) =>
            {
                int o = (y * jw + x) * jc;
                if (o < 0 || o >= jp.Length) { r = g = b = 0; return; }
                if (jc >= 3) { r = jp[o]; g = jp[o + 1]; b = jp[o + 2]; }
                else { r = g = b = jp[o]; }
            };
        }

        // JPEG (DCTDecode): DecodeStream leaves the codestream encoded, so decode it
        // here. JpegDecoder converts CMYK/YCCK to RGB and reports 3 components, so the
        // getter handles grayscale (1) and colour (3) uniformly. On a decode failure,
        // fall through rather than feeding codestream bytes to the getter.
        if (IsJpeg)
        {
            try
            {
                var (pj, pjw, pjh, pjc) = IO.Filters.JpegDecoder.Decode(decoded);
                return (int x, int y, out byte r, out byte g, out byte b) =>
                {
                    int o = (y * pjw + x) * pjc;
                    if (o < 0 || o + pjc > pj.Length) { r = g = b = 0; return; }
                    if (pjc >= 3) { r = pj[o]; g = pj[o + 1]; b = pj[o + 2]; }
                    else { r = g = b = pj[o]; }
                };
            }
            catch { return null; }
        }

        // Indexed colour space — see ToPng() for the rationale.
        var indexedPalette = ResolveIndexedPalette(out var paletteSize);
        if (indexedPalette is not null)
        {
            return (int x, int y, out byte r, out byte g, out byte b) =>
            {
                var idx = ReadPackedIndex(decoded, x, y, w, bpc);
                if (idx >= paletteSize) idx = paletteSize - 1;
                var src = idx * 3;
                r = indexedPalette[src];
                g = indexedPalette[src + 1];
                b = indexedPalette[src + 2];
            };
        }

        if (bpc == 1)
        {
            var blackIs1 = false;
            var decodeArr = _reader.Resolve(_stream.Dict.Get("Decode"));
            if (decodeArr is PdfArray da && da.Count >= 2)
            {
                var first = da[0] is PdfInteger i ? i.Value : (da[0] is PdfReal r ? (long)r.Value : 0);
                blackIs1 = first == 1;
            }
            var parms = _reader.ResolveDict(_stream.Dict.Get("DecodeParms"));
            if (parms is not null)
            {
                var bi1 = parms.Get("BlackIs1");
                if (bi1 is PdfBoolean b) blackIs1 = b.Value;
            }
            var srcBytesPerRow = (w + 7) / 8;
            return (int x, int y, out byte r, out byte g, out byte b) =>
            {
                var byteIdx = (long)y * srcBytesPerRow + (x / 8);
                var bitIdx = 7 - (x % 8);
                var bit = (byteIdx < decoded.Length) ? (decoded[byteIdx] >> bitIdx) & 1 : 0;
                var v = blackIs1
                    ? (bit == 1 ? (byte)0 : (byte)255)
                    : (bit == 1 ? (byte)255 : (byte)0);
                r = v; g = v; b = v;
            };
        }

        var components = ComponentCount;
        if (components == 1)
        {
            return (int x, int y, out byte r, out byte g, out byte b) =>
            {
                var idx = (long)y * w + x;
                var v = idx < decoded.Length ? decoded[idx] : (byte)0;
                r = v; g = v; b = v;
            };
        }
        if (components == 4 && ColorSpace is "DeviceCMYK")
        {
            return (int x, int y, out byte r, out byte g, out byte b) =>
            {
                var idx = ((long)y * w + x) * 4;
                if (idx + 3 >= decoded.Length) { r = g = b = 0; return; }
                var c = decoded[idx] / 255.0;
                var m = decoded[idx + 1] / 255.0;
                var yk = decoded[idx + 2] / 255.0;
                var k = decoded[idx + 3] / 255.0;
                r = (byte)(255 * (1 - c) * (1 - k));
                g = (byte)(255 * (1 - m) * (1 - k));
                b = (byte)(255 * (1 - yk) * (1 - k));
            };
        }
        if (components == 3)
        {
            return (int x, int y, out byte r, out byte g, out byte b) =>
            {
                var idx = ((long)y * w + x) * 3;
                if (idx + 2 >= decoded.Length) { r = g = b = 0; return; }
                r = decoded[idx]; g = decoded[idx + 1]; b = decoded[idx + 2];
            };
        }
        return null;
    }

    /// <summary>Convert this image's samples to grayscale (DeviceGray, 8 bpc) in place,
    /// using the on-demand RGB pixel decoder. A no-op for image masks, already-gray
    /// images, JPEG (DCT) samples, or colour spaces the decoder can't read.</summary>
    internal void ConvertToGrayscale()
    {
        if (_stream.Dict.Get("ImageMask") is PdfBoolean { Value: true }) return;
        var cs = ResolveColorSpace();
        if (cs is "DeviceGray" or "CalGray") return;
        int w = Width, h = Height;
        if (w <= 0 || h <= 0) return;

        byte[] gray;
        if (IsJpeg)
        {
            // DCT samples aren't readable through the pixel source — decode the JPEG.
            try
            {
                var (px, jw, jh, comps) = IO.Filters.JpegDecoder.Decode(GetRawData());
                gray = new byte[(long)jw * jh];
                for (long i = 0; i < (long)jw * jh; i++)
                {
                    long src = i * comps;
                    if (src + comps > px.Length) { gray[i] = 0; continue; }
                    gray[i] = comps >= 3
                        ? (byte)(0.299 * px[src] + 0.587 * px[src + 1] + 0.114 * px[src + 2] + 0.5)
                        : px[src];
                }
                w = jw; h = jh;
            }
            catch { return; }
        }
        else
        {
            var getter = GetPixelSource();
            if (getter is null) return;
            gray = new byte[(long)w * h];
            long o = 0;
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    getter(x, y, out var r, out var g, out var b);
                    gray[o++] = (byte)(0.299 * r + 0.587 * g + 0.114 * b + 0.5);
                }
        }

        _stream.Dict.Remove("Filter");
        _stream.Dict.Remove("DecodeParms");
        _stream.Dict.Remove("Decode");
        _stream.Dict.Set("ColorSpace", new PdfName("DeviceGray"));
        _stream.Dict.Set("BitsPerComponent", new PdfInteger(8));
        _stream.Dict.Set("Width", new PdfInteger(w));
        _stream.Dict.Set("Height", new PdfInteger(h));
        _stream.Dict.Set("Length", new PdfInteger(gray.Length));
        _stream.ReplaceData(gray);
    }

    /// <summary>
    /// Export the image as PNG bytes.
    /// Works for all image types (JPEG, Flate, CCITT, etc.).
    /// </summary>
    public byte[] ToPng()
    {
        var decoded = _reader.DecodeStream(_stream);
        var w = Width;
        var h = Height;
        var bpc = BitsPerComponent;

        // JPEG 2000 (JPXDecode): decode the codestream to raw samples.
        if (IsJpeg2000 && IO.Filters.JpxDecoder.TryDecode(decoded, out var jp, out var jw, out var jh, out var jc))
            return IO.PngEncoder.Encode(jp, jw, jh, jc >= 3 ? 2 : 0, 8);

        // JPEG (DCTDecode): DecodeStream leaves the codestream encoded; decode it to
        // RGB/gray samples (JpegDecoder converts CMYK/YCCK to RGB and reports 3
        // components) rather than feeding the codestream bytes to the PNG encoder.
        if (IsJpeg)
        {
            try
            {
                var (pj, pjw, pjh, pjc) = IO.Filters.JpegDecoder.Decode(decoded);
                return IO.PngEncoder.Encode(pj, pjw, pjh, pjc >= 3 ? 2 : 0, 8);
            }
            catch { /* fall through to the raw-sample path below */ }
        }

        // Indexed colour space — look up palette indices into an RGB triple
        // and emit a regular 24-bit RGB PNG. Indexed images use 1/2/4/8-bpc
        // packing for the indices, so the row stride is bit-aligned, not
        // byte-aligned-per-pixel. Bypass the 1-bit branch below so the
        // CCITT-style polarity logic doesn't run on a 1-bpc indexed sample.
        var indexedPalette = ResolveIndexedPalette(out var paletteSize);
        if (indexedPalette is not null)
        {
            var rgb = new byte[w * h * 3];
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var idx = ReadPackedIndex(decoded, x, y, w, bpc);
                    if (idx >= paletteSize) idx = paletteSize - 1;
                    var src = idx * 3;
                    var dst = (y * w + x) * 3;
                    rgb[dst] = indexedPalette[src];
                    rgb[dst + 1] = indexedPalette[src + 1];
                    rgb[dst + 2] = indexedPalette[src + 2];
                }
            return IO.PngEncoder.Encode(rgb, w, h, colorType: 2, bitDepth: 8);
        }

        if (bpc == 1)
        {
            // 1-bit image (CCITT, JBIG2, etc.)
            var blackIs1 = false;
            var decodeArr = _reader.Resolve(_stream.Dict.Get("Decode"));
            if (decodeArr is PdfArray da && da.Count >= 2)
            {
                var first = da[0] is PdfInteger i ? i.Value : (da[0] is PdfReal r ? (long)r.Value : 0);
                blackIs1 = first == 1;
            }
            // Also check BlackIs1 in DecodeParms
            var parms = _reader.ResolveDict(_stream.Dict.Get("DecodeParms"));
            if (parms is not null)
            {
                var bi1 = parms.Get("BlackIs1");
                if (bi1 is PdfBoolean b) blackIs1 = b.Value;
            }
            return IO.PngEncoder.Encode1Bit(decoded, w, h, blackIs1);
        }

        var components = ComponentCount;
        var colorType = components switch
        {
            1 => 0, // Grayscale
            3 => 2, // RGB
            4 => 6, // RGBA (treat CMYK as RGBA for now — simple inversion)
            _ => 2,
        };

        // Handle CMYK → RGB conversion
        if (components == 4 && ColorSpace is "DeviceCMYK")
        {
            decoded = CmykToRgb(decoded, w, h);
            colorType = 2; // RGB
        }

        // A soft mask (PDF 32000 §11.6.5.3) carries per-pixel opacity. PNG can
        // represent it as an alpha channel, so composite it into an RGBA/gray+alpha
        // image (JPEG output, which cannot, still drops it). Only the gray (0) and
        // RGB (2) raster paths are promoted; other paths already return above.
        var bd = bpc > 8 ? 8 : bpc;
        if (bd == 8 && colorType is 0 or 2 &&
            ((HasSoftMask && TryBuildSoftMaskAlpha(w, h, out var alpha))
             || TryBuildColorKeyAlpha(decoded, w, h, colorType == 2 ? 3 : 1, out alpha)))
        {
            var (withAlpha, alphaColorType) = InterleaveAlpha(decoded, alpha, w, h, colorType == 2 ? 3 : 1);
            return IO.PngEncoder.Encode(withAlpha, w, h, alphaColorType, 8);
        }

        return IO.PngEncoder.Encode(decoded, w, h, colorType, bd);
    }

    /// <summary>Build a per-pixel alpha plane from a colour-key /Mask array
    /// (PDF 32000 §8.9.6.4): a pixel whose every component falls inside its
    /// [min,max] range is fully transparent. The common producer shape is
    /// /Mask [255 255 255 255 255 255] — white knocked out — for annotation
    /// overlays drawn on a white ground.</summary>
    private bool TryBuildColorKeyAlpha(byte[] decoded, int w, int h, int components, out byte[] alpha)
    {
        alpha = System.Array.Empty<byte>();
        if (_reader.Resolve(_stream.Dict.Get("Mask")) is not PdfArray maskArr
            || maskArr.Count != components * 2)
            return false;
        var lo = new int[components];
        var hi = new int[components];
        for (var c = 0; c < components; c++)
        {
            lo[c] = _reader.Resolve(maskArr[c * 2]) is PdfInteger l ? (int)l.Value : 0;
            hi[c] = _reader.Resolve(maskArr[c * 2 + 1]) is PdfInteger u ? (int)u.Value : 0;
        }
        if (decoded.Length < w * h * components) return false;
        alpha = new byte[w * h];
        for (var i = 0; i < w * h; i++)
        {
            var masked = true;
            for (var c = 0; c < components; c++)
            {
                int v = decoded[i * components + c];
                if (v < lo[c] || v > hi[c]) { masked = false; break; }
            }
            alpha[i] = masked ? (byte)0 : (byte)255;
        }
        return true;
    }

    /// <summary>Decode the image's /SMask soft mask into a per-pixel alpha plane
    /// resampled to this image's dimensions (0=transparent, 255=opaque).</summary>
    private bool TryBuildSoftMaskAlpha(int w, int h, out byte[] alpha)
    {
        alpha = System.Array.Empty<byte>();
        var raw = Devices.SoftwarePageRenderer.ResolveSMaskAlpha(
            _stream.Dict.Get("SMask"), _reader, out var mw, out var mh);
        if (raw is null || mw <= 0 || mh <= 0) return false;

        if (mw == w && mh == h)
        {
            alpha = raw;
            return true;
        }
        // Nearest-neighbour resample the mask onto the base-image grid.
        var scaled = new byte[w * h];
        for (var y = 0; y < h; y++)
        {
            var sy = mh == h ? y : (int)((long)y * mh / h);
            if (sy >= mh) sy = mh - 1;
            for (var x = 0; x < w; x++)
            {
                var sx = mw == w ? x : (int)((long)x * mw / w);
                if (sx >= mw) sx = mw - 1;
                scaled[y * w + x] = raw[sy * mw + sx];
            }
        }
        alpha = scaled;
        return true;
    }

    private static byte[] CmykToRgb(byte[] cmyk, int width, int height)
    {
        var rgb = new byte[width * height * 3];
        var pixelCount = width * height;
        for (var i = 0; i < pixelCount; i++)
        {
            var srcIdx = i * 4;
            if (srcIdx + 3 >= cmyk.Length) break;

            var c = cmyk[srcIdx] / 255.0;
            var m = cmyk[srcIdx + 1] / 255.0;
            var y = cmyk[srcIdx + 2] / 255.0;
            var k = cmyk[srcIdx + 3] / 255.0;

            var dstIdx = i * 3;
            rgb[dstIdx] = (byte)(255 * (1 - c) * (1 - k));
            rgb[dstIdx + 1] = (byte)(255 * (1 - m) * (1 - k));
            rgb[dstIdx + 2] = (byte)(255 * (1 - y) * (1 - k));
        }
        return rgb;
    }

    private string? ResolveColorSpace()
    {
        var csObj = _reader.Resolve(_stream.Dict.Get("ColorSpace"));
        return csObj switch
        {
            PdfName n => n.Value,
            PdfArray a when a.Count > 0 && a[0] is PdfName n2 => n2.Value,
            _ => null
        };
    }

    /// <summary>
    /// Resolve an Indexed colorspace to a flat 256*3 RGB palette, or null when
    /// the colorspace isn't Indexed (or the palette can't be parsed).
    /// PDF 32000 §8.6.6.3 — `[/Indexed base hival lookup]` where lookup is
    /// either a string or a stream containing (hival+1)*N base-component bytes.
    /// Indexed-of-DeviceRGB / DeviceGray are mapped here; CMYK / ICC bases
    /// produce a best-effort RGB palette by replicating gray or treating CMYK
    /// as RGBA-like inversion.
    /// </summary>
    private byte[]? ResolveIndexedPalette(out int paletteSize)
    {
        paletteSize = 0;
        var csObj = _reader.Resolve(_stream.Dict.Get("ColorSpace"));
        if (csObj is not PdfArray arr || arr.Count < 4) return null;
        if (arr[0] is not PdfName name || name.Value != "Indexed") return null;

        var hival = arr[2] is PdfInteger hi ? (int)hi.Value : 255;
        paletteSize = hival + 1;

        // Shared resolver: bakes a /Separation or single-colorant /DeviceN base's
        // tint palette to RGB and reports the per-entry component count for the
        // device bases (an indexed-of-DeviceN palette is 1 byte per entry, not 3).
        var info = Devices.SoftwarePageRenderer.ResolveImageColorSpace(arr, _reader);
        var lookup = info.Palette;
        if (lookup is null) return null;
        var baseComps = info.PaletteComponents;

        var rgb = new byte[paletteSize * 3];
        for (var i = 0; i < paletteSize; i++)
        {
            var src = i * baseComps;
            if (src >= lookup.Length) break;
            byte r, g, b;
            if (baseComps == 1)
            {
                r = g = b = lookup[src];
            }
            else if (baseComps == 4)
            {
                // CMYK → RGB
                var c = lookup[src] / 255.0;
                var m = lookup[src + 1] / 255.0;
                var y = lookup[src + 2] / 255.0;
                var k = src + 3 < lookup.Length ? lookup[src + 3] / 255.0 : 0;
                r = (byte)(255 * (1 - c) * (1 - k));
                g = (byte)(255 * (1 - m) * (1 - k));
                b = (byte)(255 * (1 - y) * (1 - k));
            }
            else // 3
            {
                r = lookup[src];
                g = src + 1 < lookup.Length ? lookup[src + 1] : (byte)0;
                b = src + 2 < lookup.Length ? lookup[src + 2] : (byte)0;
            }
            var dst = i * 3;
            rgb[dst] = r;
            rgb[dst + 1] = g;
            rgb[dst + 2] = b;
        }
        return rgb;
    }

    /// <summary>
    /// Read a pixel-index value from a packed 1/2/4/8-bpc buffer; returns 0
    /// when the index is past the end. Big-endian MSB-first bit packing as
    /// PDF 32000 specifies for image XObjects.
    /// </summary>
    private static int ReadPackedIndex(byte[] data, int x, int y, int width, int bpc)
    {
        var bitsPerRow = ((width * bpc + 7) / 8) * 8;
        var bitPos = (long)y * bitsPerRow + x * bpc;
        var byteIdx = (int)(bitPos / 8);
        if (byteIdx >= data.Length) return 0;
        var bitInByte = (int)(bitPos % 8);
        // For bpc ≤ 8, the index fits in a single byte read.
        var b = data[byteIdx];
        var shift = 8 - bitInByte - bpc;
        var mask = (1 << bpc) - 1;
        return (b >> shift) & mask;
    }

    private int DetermineComponentsFromICC()
    {
        var csObj = _reader.Resolve(_stream.Dict.Get("ColorSpace"));
        if (csObj is PdfArray a && a.Count >= 2 && a[0] is PdfName n && n.Value == "ICCBased")
        {
            var iccStream = _reader.ResolveStream(a[1]);
            if (iccStream is not null)
            {
                return (int)iccStream.Dict.GetInt("N", 3);
            }
        }
        return 3; // default to RGB
    }
}
