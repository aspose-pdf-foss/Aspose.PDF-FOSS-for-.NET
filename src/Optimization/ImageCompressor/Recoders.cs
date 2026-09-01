using System.IO.Compression;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;

namespace Aspose.Pdf.Optimization;

internal static partial class ImageCompressor
{
    private static void TryCompressImage(PdfStream stream, PdfReader reader, int quality,
        HashSet<PdfStream> maskStreams)
    {
        var filterName = GetFilterName(stream);

        // JPEG2000 / JBIG2 have no native re-encoder — leave them untouched.
        if (filterName is "JPXDecode" or "JBIG2Decode")
            return;

        // Get image dimensions
        var width = (int)stream.Dict.GetInt("Width", 0);
        var height = (int)stream.Dict.GetInt("Height", 0);
        if (width <= 0 || height <= 0) return;

        // Already-JPEG (DCTDecode) images: first try a lossless palette re-encode (flat-colour
        // graphics stored as bloated JPEGs shrink dramatically as /Indexed + Flate while every
        // decoded sample is preserved, so it is safe at any quality — including 100). Otherwise
        // re-encode at the requested quality when the caller asked for below-default
        // compression. The encoder emits 4:2:0 JPEG, so a source that was stored at higher
        // quality / no subsampling shrinks noticeably; keep the result only when it is actually
        // smaller so size never regresses. Progressive / CMYK JPEGs the native decoder can't
        // handle, image masks, and mask images are left as-is.
        if (filterName == "DCTDecode")
        {
            if (stream.Dict.Get("Filter") is PdfArray) return;     // only the sole-filter case: RawData is raw JPEG
            if (stream.Dict.GetBool("ImageMask") || maskStreams.Contains(stream)) return;
            // A colour-key /Mask matches EXACT sample values; palettizing rewrites samples
            // into palette indices, so such images must keep their original encoding.
            if (reader.Resolve(stream.Dict.Get("Mask")) is not PdfArray &&
                TryPalettizeJpeg(stream, width, height))
                return;
            if (quality >= 75) return;                             // default/high quality: leave JPEGs intact
            TryReencodeJpeg(stream, width, height, quality);
            return;
        }

        // Decode the image data
        byte[] decoded;
        try
        {
            decoded = reader.DecodeStream(stream);
        }
        catch
        {
            return; // Can't decode — skip
        }

        if (decoded.Length == 0) return;

        // A colour-key (chroma-key) /Mask array makes pixels transparent by EXACT colour
        // match against a sample-value range. Lossy JPEG perturbs those samples so the key
        // no longer matches and the "transparent" pixels become visible (e.g. a logo's
        // keyed-out background renders as solid black). Such images must
        // stay lossless so the exact key colours survive; route them to the Flate path.
        var hasColorKeyMask = reader.Resolve(stream.Dict.Get("Mask")) is PdfArray;

        // Lossy path: photographic/continuous-tone images shrink far more as JPEG than
        // as Flate. Re-encode 8-bit colour images to DCTDecode at the requested quality,
        // keeping whichever encoding is smaller. Stencil/soft-mask images are excluded so
        // their alpha channel is preserved.
        if (!stream.Dict.GetBool("ImageMask") && !maskStreams.Contains(stream) && !hasColorKeyMask)
        {
            var rgb = TryBuildRgb(decoded, width, height, stream, reader);
            if (rgb is not null)
            {
                // Lossless palette re-encode first: for flat-colour graphics it beats both
                // JPEG and plain Flate while preserving every sample exactly.
                if (TryPalettize(stream, rgb, width, height)) return;

                byte[] jpeg;
                try
                {
                    jpeg = JpegEncoderImpl.Encode((int x, int y, out byte r, out byte g, out byte b) =>
                    {
                        var idx = (y * width + x) * 3;
                        r = rgb[idx];
                        g = rgb[idx + 1];
                        b = rgb[idx + 2];
                    }, width, height, quality);
                }
                catch
                {
                    jpeg = [];
                }

                if (jpeg.Length > 0 && jpeg.Length < stream.RawData.Length)
                {
                    stream.ReplaceData(jpeg);
                    stream.Dict.Set("Filter", new PdfName("DCTDecode"));
                    stream.Dict.Set("Length", new PdfInteger(jpeg.Length));
                    stream.Dict.Set("ColorSpace", new PdfName("DeviceRGB"));
                    stream.Dict.Set("BitsPerComponent", new PdfInteger(8));
                    // A custom /Decode array or /DecodeParms no longer matches the
                    // re-encoded DeviceRGB sample stream.
                    stream.Dict.Remove("DecodeParms");
                    stream.Dict.Remove("Decode");
                    return;
                }
            }
        }

        // Lossless fallback: re-compress with FlateDecode at highest compression level.
        var compressed = Compress(decoded);

        // Only replace if we actually achieved compression
        if (compressed.Length >= stream.RawData.Length) return;

        // Update the stream with compressed data
        stream.ReplaceData(compressed);
        stream.Dict.Set("Filter", new PdfName("FlateDecode"));
        stream.Dict.Set("Length", new PdfInteger(compressed.Length));

        // Remove old decode params that don't apply to our FlateDecode
        stream.Dict.Remove("DecodeParms");
    }

    /// <summary>
    /// Re-encode an already-JPEG (DCTDecode) image at a lower quality: decode the baseline/
    /// extended-sequential JPEG to raw pixels natively, re-encode at <paramref name="quality"/>
    /// (4:2:0), and replace the stream only when the result is smaller. Grayscale (1-component)
    /// and 3-component images are handled; anything the native decoder cannot decode
    /// (progressive, CMYK, arithmetic) or that would not shrink is left unchanged.
    /// </summary>
    private static void TryReencodeJpeg(PdfStream stream, int width, int height, int quality)
    {
        byte[] pixels;
        int jw, jh, comp;
        try
        {
            (pixels, jw, jh, comp) = JpegDecoder.Decode(stream.RawData);
        }
        catch
        {
            return; // progressive / CMYK / unsupported — keep the original JPEG
        }
        if (pixels.Length == 0 || jw <= 0 || jh <= 0 || (comp != 1 && comp != 3)) return;
        if (jw != width || jh != height) return; // decoded geometry disagrees — don't risk it

        byte[] jpeg;
        try
        {
            jpeg = JpegEncoderImpl.Encode((int x, int y, out byte r, out byte g, out byte b) =>
            {
                if (comp == 3)
                {
                    var i = (y * jw + x) * 3;
                    r = pixels[i]; g = pixels[i + 1]; b = pixels[i + 2];
                }
                else
                {
                    var v = pixels[y * jw + x];
                    r = g = b = v;
                }
            }, jw, jh, quality);
        }
        catch
        {
            return;
        }

        if (jpeg.Length == 0 || jpeg.Length >= stream.RawData.Length) return;

        stream.ReplaceData(jpeg);
        stream.Dict.Set("Filter", new PdfName("DCTDecode"));
        stream.Dict.Set("Length", new PdfInteger(jpeg.Length));
        // The re-encoder emits 3-component YCbCr JPEG, so the stream is DeviceRGB regardless
        // of the source colour model; drop decode arrays that no longer match.
        stream.Dict.Set("ColorSpace", new PdfName("DeviceRGB"));
        stream.Dict.Set("BitsPerComponent", new PdfInteger(8));
        stream.Dict.Remove("DecodeParms");
        stream.Dict.Remove("Decode");
    }

    /// <summary>
    /// Decode a baseline JPEG XObject and hand it to the lossless palette re-encoder.
    /// Returns true when the stream was replaced by an /Indexed + Flate encoding.
    /// </summary>
    private static bool TryPalettizeJpeg(PdfStream stream, int width, int height)
    {
        byte[] pixels;
        int jw, jh, comp;
        try
        {
            (pixels, jw, jh, comp) = JpegDecoder.Decode(stream.RawData);
        }
        catch
        {
            return false; // progressive / CMYK / unsupported — keep the original JPEG
        }
        if (pixels.Length == 0 || jw != width || jh != height || (comp != 1 && comp != 3))
            return false;

        byte[] rgb;
        if (comp == 3)
        {
            rgb = pixels;
        }
        else
        {
            rgb = new byte[width * height * 3];
            for (var i = 0; i < width * height; i++)
            {
                var v = pixels[i];
                rgb[i * 3] = v; rgb[i * 3 + 1] = v; rgb[i * 3 + 2] = v;
            }
        }
        return TryPalettize(stream, rgb, width, height);
    }

    /// <summary>
    /// Losslessly re-encode a packed-RGB image as /Indexed /DeviceRGB + FlateDecode when it
    /// uses at most 256 distinct colours, at the minimal bit depth (1 for 2 colours, 4 for
    /// up to 16, 8 otherwise). Every decoded sample maps to the identical colour, so this is
    /// valid at any requested ImageQuality. Replaces the stream only when the result is
    /// smaller; returns true when replaced.
    /// </summary>
    private static bool TryPalettize(PdfStream stream, byte[] rgb, int width, int height)
    {
        var pixels = width * height;
        if (pixels <= 0 || rgb.Length < pixels * 3) return false;

        var palette = new Dictionary<int, int>();
        var indices = new byte[pixels];
        for (var i = 0; i < pixels; i++)
        {
            var packed = (rgb[i * 3] << 16) | (rgb[i * 3 + 1] << 8) | rgb[i * 3 + 2];
            if (!palette.TryGetValue(packed, out var idx))
            {
                if (palette.Count == 256) return false; // continuous-tone — not palette material
                idx = palette.Count;
                palette[packed] = idx;
            }
            indices[i] = (byte)idx;
        }

        var count = palette.Count;
        var bpc = count <= 2 ? 1 : count <= 16 ? 4 : 8;

        byte[] samples;
        if (bpc == 8)
        {
            samples = indices;
        }
        else
        {
            // Pack indices MSB-first with byte-aligned rows, as the imaging model requires.
            var rowBytes = (width * bpc + 7) / 8;
            samples = new byte[rowBytes * height];
            for (var y = 0; y < height; y++)
            {
                var rowOff = y * rowBytes;
                for (var x = 0; x < width; x++)
                {
                    var bitPos = x * bpc;
                    samples[rowOff + (bitPos >> 3)] |=
                        (byte)(indices[y * width + x] << (8 - bpc - (bitPos & 7)));
                }
            }
        }

        var compressed = Compress(samples);
        if (compressed.Length >= stream.RawData.Length) return false;

        var lookup = new byte[count * 3];
        foreach (var (packed, idx) in palette)
        {
            lookup[idx * 3] = (byte)(packed >> 16);
            lookup[idx * 3 + 1] = (byte)(packed >> 8);
            lookup[idx * 3 + 2] = (byte)packed;
        }

        var cs = new PdfArray();
        cs.Add(new PdfName("Indexed"));
        cs.Add(new PdfName("DeviceRGB"));
        cs.Add(new PdfInteger(count - 1));
        cs.Add(new PdfString(lookup, isHex: true));

        stream.ReplaceData(compressed);
        stream.Dict.Set("Filter", new PdfName("FlateDecode"));
        stream.Dict.Set("Length", new PdfInteger(compressed.Length));
        stream.Dict.Set("ColorSpace", cs);
        stream.Dict.Set("BitsPerComponent", new PdfInteger(bpc));
        stream.Dict.Remove("DecodeParms");
        stream.Dict.Remove("Decode");
        return true;
    }

    /// <summary>
    /// Interpret a decoded 8-bit image sample buffer as a packed RGB buffer
    /// (3 bytes/pixel). Handles DeviceRGB/CalRGB, DeviceGray/CalGray, ICCBased (N=1 or 3)
    /// and 8-bit Indexed colour spaces. Returns null for anything else (CMYK, sub-byte
    /// depths, unknown spaces) so the caller falls back to lossless Flate.
    /// </summary>
    private static byte[]? TryBuildRgb(byte[] decoded, int width, int height,
        PdfStream stream, PdfReader reader)
    {
        var bpc = (int)stream.Dict.GetInt("BitsPerComponent", 8);
        if (bpc != 8) return null;

        var pixels = width * height;
        var csObj = reader.Resolve(stream.Dict.Get("ColorSpace"));
        var kind = ClassifyColorSpace(csObj, reader, out var indexBase, out var lookup);

        switch (kind)
        {
            case CsKind.Rgb:
                if (decoded.Length < pixels * 3) return null;
                if (decoded.Length == pixels * 3) return decoded;
                var rgb = new byte[pixels * 3];
                Array.Copy(decoded, rgb, pixels * 3);
                return rgb;

            case CsKind.Gray:
                if (decoded.Length < pixels) return null;
                var g = new byte[pixels * 3];
                for (var i = 0; i < pixels; i++)
                {
                    var v = decoded[i];
                    g[i * 3] = v; g[i * 3 + 1] = v; g[i * 3 + 2] = v;
                }
                return g;

            case CsKind.Indexed:
                if (lookup is null || decoded.Length < pixels) return null;
                var baseComps = indexBase == CsKind.Rgb ? 3 : indexBase == CsKind.Gray ? 1 : 0;
                if (baseComps == 0) return null;
                var ix = new byte[pixels * 3];
                for (var i = 0; i < pixels; i++)
                {
                    var off = decoded[i] * baseComps;
                    if (baseComps == 3)
                    {
                        if (off + 2 >= lookup.Length) return null;
                        ix[i * 3] = lookup[off]; ix[i * 3 + 1] = lookup[off + 1]; ix[i * 3 + 2] = lookup[off + 2];
                    }
                    else
                    {
                        if (off >= lookup.Length) return null;
                        var v = lookup[off];
                        ix[i * 3] = v; ix[i * 3 + 1] = v; ix[i * 3 + 2] = v;
                    }
                }
                return ix;

            default:
                return null;
        }
    }

    private enum CsKind { Unknown, Rgb, Gray, Indexed }

    /// <summary>Classify a (resolved) colour-space object into an RGB/Gray/Indexed kind.
    /// For Indexed spaces, also returns the base-space kind and its decoded lookup table.</summary>
    private static CsKind ClassifyColorSpace(PdfObject? csObj, PdfReader reader,
        out CsKind indexBase, out byte[]? lookup)
    {
        indexBase = CsKind.Unknown;
        lookup = null;

        switch (csObj)
        {
            case PdfName name:
                return name.Value switch
                {
                    "DeviceRGB" or "RGB" or "CalRGB" => CsKind.Rgb,
                    "DeviceGray" or "G" or "CalGray" => CsKind.Gray,
                    _ => CsKind.Unknown,
                };

            case PdfArray arr when arr.Count > 0 && (reader.Resolve(arr[0]) as PdfName)?.Value is { } family:
                switch (family)
                {
                    case "ICCBased":
                        if (reader.Resolve(arr.Count > 1 ? arr[1] : null) is PdfStream icc)
                        {
                            return (int)icc.Dict.GetInt("N", 0) switch
                            {
                                1 => CsKind.Gray,
                                3 => CsKind.Rgb,
                                _ => CsKind.Unknown,
                            };
                        }
                        return CsKind.Unknown;

                    case "CalRGB":
                        return CsKind.Rgb;
                    case "CalGray":
                        return CsKind.Gray;

                    case "Indexed" or "I":
                        if (arr.Count < 4) return CsKind.Unknown;
                        indexBase = ClassifyColorSpace(reader.Resolve(arr[1]), reader, out _, out _);
                        lookup = ResolveIndexedLookup(reader.Resolve(arr[3]), reader);
                        return CsKind.Indexed;

                    default:
                        return CsKind.Unknown;
                }

            default:
                return CsKind.Unknown;
        }
    }

    /// <summary>Decode an Indexed colour-space lookup table, which may be stored either
    /// as a byte string or as a (possibly filtered) stream.</summary>
    private static byte[]? ResolveIndexedLookup(PdfObject? obj, PdfReader reader)
    {
        return obj switch
        {
            PdfString s => s.Value,
            PdfStream st => SafeDecode(st, reader),
            _ => null,
        };
    }

    private static byte[]? SafeDecode(PdfStream st, PdfReader reader)
    {
        try { return reader.DecodeStream(st); }
        catch { return null; }
    }
}
