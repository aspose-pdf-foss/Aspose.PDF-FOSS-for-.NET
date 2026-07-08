using System.IO.Compression;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;

namespace Aspose.Pdf.Optimization;

/// <summary>
/// Compresses raster images in a PDF by re-encoding uncompressed or
/// losslessly-compressed images with FlateDecode at a reduced bit depth or
/// by re-encoding them as JPEG (DCTDecode).
/// </summary>
internal static class ImageCompressor
{
    /// <summary>
    /// Scan all objects in the reader for image XObjects and re-compress them.
    /// </summary>
    public static void CompressImages(PdfReader reader, int quality)
    {
        // Clamp quality
        if (quality < 1) quality = 1;
        if (quality > 100) quality = 100;

        // Streams referenced as soft masks / stencil masks must stay single-component
        // (grayscale alpha). Re-encoding them as 3-component RGB JPEG would corrupt the
        // transparency channel, so they are excluded from lossy re-encoding.
        var maskStreams = CollectMaskStreams(reader);

        foreach (var stream in FindAllImageStreams(reader))
        {
            TryCompressImage(stream, reader, quality, maskStreams);
        }
    }

    /// <summary>
    /// Collect every image stream that is referenced as a soft mask (/SMask) or
    /// stencil/colour-key mask (/Mask) by another image. These carry alpha or key data
    /// and must not be re-encoded to a different colour model.
    /// </summary>
    private static HashSet<PdfStream> CollectMaskStreams(PdfReader reader)
    {
        var masks = new HashSet<PdfStream>(ReferenceEqualityComparer.Instance);
        foreach (var img in FindAllImageStreams(reader))
        {
            if (reader.Resolve(img.Dict.Get("SMask")) is PdfStream sm) masks.Add(sm);
            if (reader.Resolve(img.Dict.Get("Mask")) is PdfStream mk) masks.Add(mk);
        }
        return masks;
    }

    /// <summary>
    /// Downsample images whose effective DPI exceeds the given maximum.
    /// Images are reduced by averaging pixel blocks and re-encoded with FlateDecode.
    /// </summary>
    public static void DownsampleImages(PdfReader reader, int maxDpi, int quality = 75)
    {
        if (maxDpi <= 0) return;

        var maskStreams = CollectMaskStreams(reader);
        foreach (var stream in FindAllImageStreams(reader))
        {
            if (maskStreams.Contains(stream)) continue; // never re-model a mask's colour
            TryDownsampleImage(stream, reader, maxDpi, quality);
        }
    }

    /// <summary>
    /// Convert RGB images to grayscale, reducing data size by ~3x.
    /// </summary>
    public static void ConvertToGrayscale(PdfReader reader)
    {
        foreach (var stream in FindAllImageStreams(reader))
        {
            TryConvertToGrayscale(stream, reader);
        }
    }

    /// <summary>
    /// Find duplicate images across the document and redirect references to a single copy.
    /// Returns the number of duplicates removed.
    /// </summary>
    public static int RemoveDuplicateImages(PdfReader reader)
    {
        // Collect all XObject dicts and their image entries
        var allXObjectDicts = new List<PdfDictionary>();
        var allImages = new List<(PdfDictionary xobjectDict, string key, PdfStream stream)>();
        CollectXObjectDictsFromPages(reader, allXObjectDicts);

        foreach (var xobjDict in allXObjectDicts)
        {
            foreach (var key in xobjDict.Keys.ToList())
            {
                var val = reader.Resolve(xobjDict.Get(key));
                if (val is PdfStream stream &&
                    stream.Dict.GetName("Subtype") == "Image")
                {
                    allImages.Add((xobjDict, key, stream));
                }
            }
        }

        if (allImages.Count < 2) return 0;

        // Hash all image streams
        var hashToCanonical = new Dictionary<string, PdfStream>(StringComparer.Ordinal);
        var duplicateCount = 0;

        foreach (var (xobjDict, key, stream) in allImages)
        {
            var width = stream.Dict.GetInt("Width");
            var height = stream.Dict.GetInt("Height");
            var cs = stream.Dict.GetName("ColorSpace") ?? "";

            var hashInput = new byte[stream.RawData.Length + System.Text.Encoding.ASCII.GetByteCount($"|{width}|{height}|{cs}")];
            stream.RawData.CopyTo(hashInput, 0);
            System.Text.Encoding.ASCII.GetBytes($"|{width}|{height}|{cs}").CopyTo(hashInput, stream.RawData.Length);
            var hash = Convert.ToHexString(Security.ShaDigest.Sha256(hashInput));

            if (hashToCanonical.TryGetValue(hash, out var canonical))
            {
                // Replace this entry with a reference to the canonical stream
                xobjDict.Set(key, canonical);
                duplicateCount++;
            }
            else
            {
                hashToCanonical[hash] = stream;
            }
        }

        return duplicateCount;
    }

    /// <summary>
    /// Collect all image PdfStream objects from both xref entries and page resource trees.
    /// </summary>
    private static List<PdfStream> FindAllImageStreams(PdfReader reader)
    {
        var images = new HashSet<PdfStream>(ReferenceEqualityComparer.Instance);

        // 1. Walk xref table entries
        foreach (var entry in reader.XRefTable.Entries.Values)
        {
            if (!entry.InUse || entry.ObjectNumber == 0) continue;

            var obj = reader.Resolve(new PdfIndirectRef(entry.ObjectNumber, entry.Generation));
            if (obj is PdfStream stream && IsImageXObject(stream))
            {
                images.Add(stream);
            }
        }

        // 2. Walk page resource trees for inline image streams
        var xobjDicts = new List<PdfDictionary>();
        CollectXObjectDictsFromPages(reader, xobjDicts);

        foreach (var xobjDict in xobjDicts)
        {
            foreach (var key in xobjDict.Keys)
            {
                var val = reader.Resolve(xobjDict.Get(key));
                if (val is PdfStream stream && IsImageXObject(stream))
                {
                    images.Add(stream);
                }
            }
        }

        return images.ToList();
    }

    private static bool IsImageXObject(PdfStream stream)
    {
        return stream.Dict.GetName("Subtype") == "Image" &&
               stream.Dict.GetName("Type") is null or "XObject";
    }

    /// <summary>
    /// Walk pages tree and collect all XObject resource dictionaries.
    /// </summary>
    private static void CollectXObjectDictsFromPages(PdfReader reader, List<PdfDictionary> result)
    {
        var catalog = reader.Catalog;
        var pagesObj = reader.Resolve(catalog.Get("Pages"));
        if (pagesObj is PdfDictionary pagesDict)
        {
            WalkPagesForXObjects(pagesDict, reader, result);
        }
    }

    private static void WalkPagesForXObjects(PdfDictionary node, PdfReader reader, List<PdfDictionary> result)
    {
        var kids = reader.Resolve(node.Get("Kids")) as PdfArray;
        if (kids is null)
        {
            // This is a leaf page
            CollectXObjectDictFromNode(node, reader, result);
            return;
        }

        for (var i = 0; i < kids.Count; i++)
        {
            var kid = reader.Resolve(kids[i]);
            if (kid is PdfDictionary kidDict)
            {
                var type = kidDict.GetName("Type");
                if (type == "Pages")
                    WalkPagesForXObjects(kidDict, reader, result);
                else
                    CollectXObjectDictFromNode(kidDict, reader, result);
            }
        }
    }

    private static void CollectXObjectDictFromNode(PdfDictionary pageDict, PdfReader reader,
        List<PdfDictionary> result)
    {
        var resources = reader.Resolve(pageDict.Get("Resources")) as PdfDictionary;
        if (resources is null) return;

        var xobjects = reader.Resolve(resources.Get("XObject")) as PdfDictionary;
        if (xobjects is not null)
        {
            result.Add(xobjects);
        }
    }

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

    private static void TryDownsampleImage(PdfStream stream, PdfReader reader, int maxDpi, int quality)
    {
        var width = (int)stream.Dict.GetInt("Width", 0);
        var height = (int)stream.Dict.GetInt("Height", 0);
        if (width <= 0 || height <= 0) return;

        var filterName = GetFilterName(stream);

        // JPEG that exceeds the target resolution: decode, box-filter down, and re-encode
        // at the requested quality. (JPX/JBIG2 have no managed codec, so leave them alone.)
        if (filterName is "DCTDecode")
        {
            TryDownsampleJpeg(stream, reader, maxDpi, quality);
            return;
        }
        if (filterName is "JPXDecode" or "JBIG2Decode")
            return;

        // Determine components per pixel from ColorSpace
        var components = GetComponents(stream);
        var bpc = (int)stream.Dict.GetInt("BitsPerComponent", 8);

        // Estimate DPI: assume the image is displayed at full page width (612pt = 8.5in)
        var estimatedDpiX = width * 72.0 / 612.0;
        var estimatedDpiY = height * 72.0 / 792.0;
        var estimatedDpi = Math.Max(estimatedDpiX, estimatedDpiY);

        if (estimatedDpi <= maxDpi) return;

        var scaleFactor = (double)maxDpi / estimatedDpi;
        var newWidth = Math.Max(1, (int)(width * scaleFactor));
        var newHeight = Math.Max(1, (int)(height * scaleFactor));

        // Don't bother if reduction is trivial
        if (newWidth >= width && newHeight >= height) return;

        // Bilevel (1-bit) scans — typically high-resolution CCITT G4 fax images — are by
        // far the largest objects in scanned documents. Down-rezzing them to the target DPI
        // and re-encoding the result as a low-rate grayscale JPEG shrinks them dramatically
        // (anti-aliasing the box-averaged greys keeps the text legible).
        if (bpc == 1 && !stream.Dict.GetBool("ImageMask") && IsGrayOrCcitt(stream, reader))
        {
            TryDownsampleBilevel(stream, reader, width, height, newWidth, newHeight);
            return;
        }

        if (bpc != 8) return; // Only handle 8-bit images otherwise

        // Decode the image data
        byte[] decoded;
        try
        {
            decoded = reader.DecodeStream(stream);
        }
        catch
        {
            return;
        }

        var expectedSize = width * height * components;
        if (decoded.Length < expectedSize) return;

        // Downsample using box filter
        var downsampled = BoxFilterDownsample(decoded, width, height, components, newWidth, newHeight);

        // Compress
        var compressed = Compress(downsampled);

        // Update stream
        stream.ReplaceData(compressed);
        stream.Dict.Set("Filter", new PdfName("FlateDecode"));
        stream.Dict.Set("Length", new PdfInteger(compressed.Length));
        stream.Dict.Set("Width", new PdfInteger(newWidth));
        stream.Dict.Set("Height", new PdfInteger(newHeight));
        stream.Dict.Remove("DecodeParms");
    }

    /// <summary>True for a single-component (grayscale) image, including CCITT fax streams
    /// whose /ColorSpace may be absent (G4 fax is implicitly bilevel gray).</summary>
    private static bool IsGrayOrCcitt(PdfStream stream, PdfReader reader)
    {
        var filterName = GetFilterName(stream);
        if (filterName is "CCITTFaxDecode" or "CCF") return true;
        var csObj = reader.Resolve(stream.Dict.Get("ColorSpace"));
        return csObj is PdfName { Value: "DeviceGray" or "G" or "CalGray" };
    }

    /// <summary>
    /// Decode a 1-bit image to a byte-per-pixel buffer, box-filter it down to the target size,
    /// threshold the averaged greys back to 1-bit, and re-store as Flate. Bilevel scans must
    /// stay bilevel — re-encoding their sharp edges as a JPEG bloats rather than shrinks them.
    /// Honours a /Decode [1 0] inversion. Replaces the stream only when smaller.
    /// </summary>
    private static void TryDownsampleBilevel(PdfStream stream, PdfReader reader,
        int width, int height, int newWidth, int newHeight)
    {
        byte[] decoded;
        try { decoded = reader.DecodeStream(stream); }
        catch { return; }

        var rowBytes = (width + 7) / 8;
        if (decoded.Length < rowBytes * height) return;

        // PDF samples: 0 = black, 1 = white for DeviceGray, unless /Decode [1 0] inverts it.
        var invert = false;
        if (reader.Resolve(stream.Dict.Get("Decode")) is PdfArray dec && dec.Count >= 2)
        {
            var d0 = dec[0] is PdfInteger di ? di.Value : (dec[0] as PdfReal)?.Value ?? 0;
            if (d0 >= 0.5) invert = true;
        }

        var gray = new byte[width * height];
        for (var y = 0; y < height; y++)
        {
            var rowOff = y * rowBytes;
            for (var x = 0; x < width; x++)
            {
                var bit = (decoded[rowOff + (x >> 3)] >> (7 - (x & 7))) & 1;
                if (invert) bit ^= 1;
                gray[y * width + x] = bit != 0 ? (byte)255 : (byte)0;
            }
        }

        var down = BoxFilterDownsample(gray, width, height, 1, newWidth, newHeight);

        // Threshold back to 1-bit and pack (MSB first), 1 = white.
        var newRowBytes = (newWidth + 7) / 8;
        var packed = new byte[newRowBytes * newHeight];
        for (var y = 0; y < newHeight; y++)
        {
            var rowOff = y * newRowBytes;
            for (var x = 0; x < newWidth; x++)
            {
                if (down[y * newWidth + x] >= 128)
                    packed[rowOff + (x >> 3)] |= (byte)(1 << (7 - (x & 7)));
            }
        }

        var compressed = Compress(packed);
        if (compressed.Length >= stream.RawData.Length) return;

        stream.ReplaceData(compressed);
        stream.Dict.Set("Filter", new PdfName("FlateDecode"));
        stream.Dict.Set("Length", new PdfInteger(compressed.Length));
        stream.Dict.Set("Width", new PdfInteger(newWidth));
        stream.Dict.Set("Height", new PdfInteger(newHeight));
        stream.Dict.Set("ColorSpace", new PdfName("DeviceGray"));
        stream.Dict.Set("BitsPerComponent", new PdfInteger(1));
        stream.Dict.Remove("DecodeParms");
        stream.Dict.Remove("Decode");
    }

    /// <summary>
    /// Decode a JPEG XObject, box-filter it down to the target DPI when it is over-resolved,
    /// and re-encode at the requested quality. Only grayscale/RGB JPEGs are handled (no
    /// managed CMYK-JPEG re-encoder). Replaces the stream only when the result is smaller.
    /// </summary>
    private static void TryDownsampleJpeg(PdfStream stream, PdfReader reader, int maxDpi, int quality)
    {
        byte[] raw;
        try { raw = reader.DecodeStream(stream); }
        catch { return; }

        int w, h, comp;
        byte[] px;
        try { (px, w, h, comp) = IO.Filters.JpegDecoder.Decode(raw); }
        catch { return; }

        if (w <= 0 || h <= 0 || (comp != 1 && comp != 3) || px.Length < w * h * comp) return;

        var estimatedDpi = Math.Max(w * 72.0 / 612.0, h * 72.0 / 792.0);
        var scale = estimatedDpi > maxDpi ? maxDpi / estimatedDpi : 1.0;
        var newWidth = Math.Max(1, (int)(w * scale));
        var newHeight = Math.Max(1, (int)(h * scale));

        // Expand to packed RGB so the encoder (and box filter) work on a uniform layout.
        byte[] srcRgb;
        if (comp == 3)
        {
            srcRgb = px;
        }
        else
        {
            srcRgb = new byte[w * h * 3];
            for (var i = 0; i < w * h; i++)
            {
                var v = px[i];
                srcRgb[i * 3] = v; srcRgb[i * 3 + 1] = v; srcRgb[i * 3 + 2] = v;
            }
        }

        var rgb = (newWidth == w && newHeight == h)
            ? srcRgb
            : BoxFilterDownsample(srcRgb, w, h, 3, newWidth, newHeight);

        byte[] jpeg;
        try
        {
            jpeg = JpegEncoderImpl.Encode((int x, int y, out byte r, out byte g, out byte b) =>
            {
                var idx = (y * newWidth + x) * 3;
                r = rgb[idx]; g = rgb[idx + 1]; b = rgb[idx + 2];
            }, newWidth, newHeight, quality);
        }
        catch { return; }

        if (jpeg.Length == 0 || jpeg.Length >= stream.RawData.Length) return;

        stream.ReplaceData(jpeg);
        stream.Dict.Set("Filter", new PdfName("DCTDecode"));
        stream.Dict.Set("Length", new PdfInteger(jpeg.Length));
        stream.Dict.Set("Width", new PdfInteger(newWidth));
        stream.Dict.Set("Height", new PdfInteger(newHeight));
        stream.Dict.Set("ColorSpace", new PdfName("DeviceRGB"));
        stream.Dict.Set("BitsPerComponent", new PdfInteger(8));
        stream.Dict.Remove("DecodeParms");
        stream.Dict.Remove("Decode");
    }

    private static void TryConvertToGrayscale(PdfStream stream, PdfReader reader)
    {
        var cs = stream.Dict.GetName("ColorSpace");
        if (cs != "DeviceRGB") return;

        var width = (int)stream.Dict.GetInt("Width", 0);
        var height = (int)stream.Dict.GetInt("Height", 0);
        var bpc = (int)stream.Dict.GetInt("BitsPerComponent", 8);
        if (width <= 0 || height <= 0 || bpc != 8) return;

        var filterName = GetFilterName(stream);
        // Skip JPEG — can't easily re-encode
        if (filterName is "DCTDecode" or "JPXDecode" or "JBIG2Decode")
            return;

        byte[] decoded;
        try
        {
            decoded = reader.DecodeStream(stream);
        }
        catch
        {
            return;
        }

        var expectedSize = width * height * 3;
        if (decoded.Length < expectedSize) return;

        // Convert RGB to grayscale using luminance formula
        var gray = new byte[width * height];
        for (var i = 0; i < width * height; i++)
        {
            var r = decoded[i * 3];
            var g = decoded[i * 3 + 1];
            var b = decoded[i * 3 + 2];
            // ITU-R BT.601 luma coefficients
            gray[i] = (byte)(0.299 * r + 0.587 * g + 0.114 * b);
        }

        var compressed = Compress(gray);

        stream.ReplaceData(compressed);
        stream.Dict.Set("Filter", new PdfName("FlateDecode"));
        stream.Dict.Set("Length", new PdfInteger(compressed.Length));
        stream.Dict.Set("ColorSpace", new PdfName("DeviceGray"));
        stream.Dict.Remove("DecodeParms");
    }

    /// <summary>
    /// Downsample pixel data using a box filter (average NxN blocks).
    /// </summary>
    internal static byte[] BoxFilterDownsample(byte[] src, int srcW, int srcH,
        int components, int dstW, int dstH)
    {
        var dst = new byte[dstW * dstH * components];

        for (var dy = 0; dy < dstH; dy++)
        {
            var srcY0 = dy * srcH / dstH;
            var srcY1 = Math.Min((dy + 1) * srcH / dstH, srcH);

            for (var dx = 0; dx < dstW; dx++)
            {
                var srcX0 = dx * srcW / dstW;
                var srcX1 = Math.Min((dx + 1) * srcW / dstW, srcW);

                var count = (srcY1 - srcY0) * (srcX1 - srcX0);
                if (count == 0) count = 1;

                // Accumulate
                var sums = new int[components];
                for (var sy = srcY0; sy < srcY1; sy++)
                {
                    for (var sx = srcX0; sx < srcX1; sx++)
                    {
                        var srcIdx = (sy * srcW + sx) * components;
                        for (var c = 0; c < components; c++)
                        {
                            sums[c] += src[srcIdx + c];
                        }
                    }
                }

                var dstIdx = (dy * dstW + dx) * components;
                for (var c = 0; c < components; c++)
                {
                    dst[dstIdx + c] = (byte)(sums[c] / count);
                }
            }
        }

        return dst;
    }

    private static string? GetFilterName(PdfStream stream)
    {
        var filter = stream.Dict.Get("Filter");
        return filter switch
        {
            PdfName n => n.Value,
            PdfArray arr when arr.Count > 0 && arr[0] is PdfName fn => fn.Value,
            _ => null,
        };
    }

    private static int GetComponents(PdfStream stream)
    {
        var cs = stream.Dict.GetName("ColorSpace");
        return cs switch
        {
            "DeviceRGB" => 3,
            "DeviceCMYK" => 4,
            "DeviceGray" => 1,
            _ => 3, // Default to RGB
        };
    }

    private static byte[] Compress(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var zlib = new ZLibStream(ms, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(data);
        }
        return ms.ToArray();
    }
}
