using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;

namespace Aspose.Pdf.Optimization;

internal static partial class ImageCompressor
{
    /// <summary>The reader's part of the compression pass: a JPEG is recoded from its own
    /// bytes, any other image from its decoded samples and colour space.</summary>
    private static ImageJob? PrepareRecode(PdfStream stream, PdfReader reader, int quality,
        HashSet<PdfStream> maskStreams)
    {
        var job = DescribeImage(stream, reader, maskStreams, quality, 0);
        if (job is null) return null;
        if (job.FilterName == "DCTDecode")
        {
            // A JPEG behind other filters: the codestream is what those filters decode to.
            if (!job.SoleFilter) job.Decoded = SafeDecode(stream, reader);
            return job;
        }
        job.Decoded = SafeDecode(stream, reader);
        (job.Kind, job.IndexBase, job.Lookup) =
            ClassifyColorSpace(reader.Resolve(stream.Dict.Get("ColorSpace")), reader);
        return job;
    }

    /// <summary>Recode one image: the lossless palette first, the lossy JPEG when the image
    /// is a plain 8-bit colour image, and Flate at the highest level otherwise - each kept
    /// only when smaller.</summary>
    private static ImageRecode? RecodeImage(ImageJob job)
    {
        if (job.FilterName == "DCTDecode") return RecodeExistingJpeg(job);
        if (job.Decoded is not { Length: > 0 } decoded) return null;

        // Lossy path: photographic/continuous-tone images shrink far more as JPEG than
        // as Flate. Stencil and soft-mask images are excluded so their alpha channel is
        // preserved, and so is an image under a colour-key /Mask: the key matches EXACT
        // sample values, which lossy JPEG perturbs (a logo's keyed-out background would
        // render as solid black).
        if (!job.IsMask && !job.HasColorKeyMask && TryBuildRgb(job, decoded) is { } samples
            && Shown(job, samples, job.Kind == CsKind.Gray ? 1 : RgbComponents) is { } rgb)
        {
            var lossy = TryPalettize(job, rgb) ?? EncodeJpeg(job, rgb);
            if (lossy is not null) return lossy;
        }

        // Lossless fallback: re-compress with FlateDecode at highest compression level.
        var compressed = Compress(decoded);
        return compressed.Length < job.Raw.Length ? new ImageRecode(compressed, "FlateDecode") : null;
    }

    /// <summary>An already-JPEG (DCTDecode) image: first a lossless palette re-encode
    /// (flat-colour graphics stored as bloated JPEGs shrink dramatically as /Indexed + Flate
    /// while every decoded sample is preserved, so it is safe at any quality), otherwise a
    /// re-encode at the requested quality when the caller asked for below-default
    /// compression. The encoder emits 4:2:0 JPEG, so a source stored at higher quality or
    /// without subsampling shrinks noticeably. Progressive / CMYK JPEGs the native decoder
    /// can't handle, image masks and mask images are left as-is; an image under a colour-key
    /// /Mask keeps its samples, so it is never palettized. A JPEG behind other filters is
    /// recoded from the codestream they wrap.</summary>
    private static ImageRecode? RecodeExistingJpeg(ImageJob job)
    {
        var codestream = job.SoleFilter ? job.Raw : job.Decoded;
        if (codestream is null || job.IsMask) return null;
        var decodedRgb = DecodeJpegAsRgb(codestream, job.Width, job.Height, Devices.SoftwarePageRenderer.CmykDecodeInverts(job.Stream.Dict));
        if (decodedRgb is null || Shown(job, decodedRgb, job.Components) is not { } rgb) return null;
        if (!job.HasColorKeyMask && TryPalettize(job, rgb) is { } palette) return palette;
        return job.Quality >= 75 ? null : EncodeJpeg(job, rgb);
    }

    /// <summary>Decode a baseline/extended-sequential JPEG to packed RGB; null for anything
    /// the native decoder cannot decode (progressive, CMYK, arithmetic) or whose geometry
    /// disagrees with the image dictionary.</summary>
    private static byte[]? DecodeJpegAsRgb(byte[] raw, int width, int height, bool invertCmyk)
    {
        byte[] pixels;
        int jw, jh, comp;
        try
        {
            (pixels, jw, jh, comp) = JpegDecoder.Decode(raw, invertCmyk);
        }
        catch
        {
            return null;
        }
        if (pixels.Length == 0 || jw != width || jh != height || (comp != 1 && comp != 3)) return null;
        return comp == 3 ? pixels : ExpandGrayToRgb(pixels, width * height);
    }

    /// <summary>The image as a 3-component YCbCr JPEG at the job's quality, kept when it is
    /// smaller than the stored stream. The stream becomes DeviceRGB regardless of the source
    /// colour model.</summary>
    private static ImageRecode? EncodeJpeg(ImageJob job, byte[] rgb)
    {
        var jpeg = EncodeRgbJpeg(rgb, job.Width, job.Height, job.Quality);
        if (jpeg.Length == 0 || jpeg.Length >= job.Raw.Length) return null;
        return new ImageRecode(jpeg, "DCTDecode", new PdfName("DeviceRGB"), 8, DropDecode: true);
    }

    /// <summary>
    /// Losslessly re-encode a packed-RGB image as /Indexed /DeviceRGB + FlateDecode when it
    /// uses at most 256 distinct colours, at the minimal bit depth (1 for 2 colours, 4 for
    /// up to 16, 8 otherwise). Every decoded sample maps to the identical colour, so this is
    /// valid at any requested ImageQuality. Null when the image has more colours or the
    /// result is not smaller.
    /// </summary>
    // The most tones a grey image may have and still be taken for drawn graphics worth a palette.
    private const int DrawnGreyTones = 16;

    private static ImageRecode? TryPalettize(ImageJob job, byte[] rgb)
    {
        var width = job.Width;
        var height = job.Height;
        var pixels = width * height;
        if (pixels <= 0 || rgb.Length < pixels * 3) return null;

        var palette = new Dictionary<int, int>();
        var indices = new byte[pixels];
        for (var i = 0; i < pixels; i++)
        {
            var packed = (rgb[i * 3] << 16) | (rgb[i * 3 + 1] << 8) | rgb[i * 3 + 2];
            if (!palette.TryGetValue(packed, out var idx))
            {
                if (palette.Count == 256) return null; // continuous-tone — not palette material
                idx = palette.Count;
                palette[packed] = idx;
            }
            indices[i] = (byte)idx;
        }

        var count = palette.Count;
        // Every 8-bit grey image fits a 256-entry palette, so for a grey image the palette tells nothing:
        // only a few tones (a 4-bit index) mark drawn graphics; a grey scan is continuous tone for JPEG.
        if (job.Kind == CsKind.Gray && count > DrawnGreyTones) return null;
        var bpc = count switch { <= 2 => 1, <= 16 => 4, _ => 8 };

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
        if (compressed.Length >= job.Raw.Length) return null;

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
        return new ImageRecode(compressed, "FlateDecode", cs, bpc, DropDecode: true);
    }

    /// <summary>
    /// Interpret a decoded 8-bit image sample buffer as a packed RGB buffer
    /// (3 bytes/pixel). Handles DeviceRGB/CalRGB, DeviceGray/CalGray, ICCBased (N=1 or 3)
    /// and 8-bit Indexed colour spaces. Returns null for anything else (CMYK, sub-byte
    /// depths, unknown spaces) so the caller falls back to lossless Flate.
    /// </summary>
    private static byte[]? TryBuildRgb(ImageJob job, byte[] decoded)
    {
        if (job.Bpc != 8) return null;
        var pixels = job.Width * job.Height;

        switch (job.Kind)
        {
            case CsKind.Rgb:
                if (decoded.Length < pixels * 3) return null;
                if (decoded.Length == pixels * 3) return decoded;
                var rgb = new byte[pixels * 3];
                Array.Copy(decoded, rgb, pixels * 3);
                return rgb;

            case CsKind.Gray:
                return decoded.Length < pixels ? null : ExpandGrayToRgb(decoded, pixels);

            case CsKind.Indexed:
                var lookup = job.Lookup;
                if (lookup is null || decoded.Length < pixels) return null;
                var baseComps = job.IndexBase switch { CsKind.Rgb => 3, CsKind.Gray => 1, _ => 0 };
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

    private const int RgbComponents = 3;
    private const int CmykComponents = 4;

    /// <summary>An image's packed RGB samples as the image shows them: through its /Decode array, which every
    /// recode below writes the samples without. A gray or RGB image read through an inverting /Decode shows its
    /// negative; recoded from the raw samples and the /Decode dropped, it came out inverted. A CMYK JPEG's /Decode
    /// is taken by its decoder, which hands over RGB. Null when an /Indexed image's /Decode remaps its indices,
    /// which leaves it to the lossless recode that keeps the /Decode.</summary>
    private static byte[]? Shown(ImageJob job, byte[] rgb, int components)
    {
        if (components == CmykComponents || job.Stream.Dict.Get("Decode") is not PdfArray decode) return rgb;
        var maps = new byte[RgbComponents][];
        var identity = true;
        for (var c = 0; c < RgbComponents; c++)
        {
            var k = components == 1 ? 0 : c;
            if (decode.Count < 2 * k + 2 || Number(decode[2 * k]) is not { } low || Number(decode[2 * k + 1]) is not { } high)
                return rgb;
            identity &= low == 0 && high == 1;
            maps[c] = new byte[byte.MaxValue + 1];
            for (var v = 0; v <= byte.MaxValue; v++)
            {
                var shown = low + v / (double)byte.MaxValue * (high - low);
                maps[c][v] = (byte)Math.Round(Math.Max(0, Math.Min(1, shown)) * byte.MaxValue);
            }
        }
        if (identity) return rgb;
        if (job.Kind == CsKind.Indexed) return null;
        var result = new byte[rgb.Length];
        for (var i = 0; i < rgb.Length; i++) result[i] = maps[i % RgbComponents][rgb[i]];
        return result;
    }

    private static double? Number(PdfObject value) => value switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => null,
    };

    private enum CsKind { Unknown, Rgb, Gray, Indexed }

    /// <summary>Classify a (resolved) colour-space object into an RGB/Gray/Indexed kind.
    /// For Indexed spaces, also returns the base-space kind and its decoded lookup table.</summary>
    private static (CsKind kind, CsKind indexBase, byte[]? lookup) ClassifyColorSpace(PdfObject? csObj, PdfReader reader)
    {
        static (CsKind kind, CsKind indexBase, byte[]? lookup) Plain(CsKind kind) => (kind, CsKind.Unknown, null);

        switch (csObj)
        {
            case PdfName name:
                return Plain(name.Value switch
                {
                    "DeviceRGB" or "RGB" or "CalRGB" => CsKind.Rgb,
                    "DeviceGray" or "G" or "CalGray" => CsKind.Gray,
                    _ => CsKind.Unknown,
                });

            case PdfArray arr when arr.Count > 0 && (reader.Resolve(arr[0]) as PdfName)?.Value is { } family:
                switch (family)
                {
                    case "ICCBased":
                        if (reader.Resolve(arr.Count > 1 ? arr[1] : null) is PdfStream icc)
                        {
                            return Plain((int)icc.Dict.GetInt("N", 0) switch
                            {
                                1 => CsKind.Gray,
                                3 => CsKind.Rgb,
                                _ => CsKind.Unknown,
                            });
                        }
                        return Plain(CsKind.Unknown);

                    case "CalRGB":
                        return Plain(CsKind.Rgb);
                    case "CalGray":
                        return Plain(CsKind.Gray);

                    case "Indexed" or "I":
                        if (arr.Count < 4) return Plain(CsKind.Unknown);
                        return (CsKind.Indexed,
                            ClassifyColorSpace(reader.Resolve(arr[1]), reader).kind,
                            ResolveIndexedLookup(reader.Resolve(arr[3]), reader));

                    default:
                        return Plain(CsKind.Unknown);
                }

            default:
                return Plain(CsKind.Unknown);
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
}
