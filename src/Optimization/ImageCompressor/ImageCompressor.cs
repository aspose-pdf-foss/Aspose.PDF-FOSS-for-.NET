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
internal static partial class ImageCompressor
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
        // Collect all XObject dicts and their image entries, keeping each entry's
        // stored value (normally an indirect ref) so a duplicate can be redirected
        // to the canonical entry's ref rather than to the resolved stream object.
        var allXObjectDicts = new List<PdfDictionary>();
        var allImages = new List<(PdfDictionary xobjectDict, string key, PdfStream stream, PdfObject entry)>();
        CollectXObjectDictsFromPages(reader, allXObjectDicts);

        foreach (var xobjDict in allXObjectDicts)
        {
            foreach (var key in xobjDict.Keys.ToList())
            {
                var entry = xobjDict.Get(key);
                var val = reader.Resolve(entry);
                if (entry is not null && val is PdfStream stream &&
                    stream.Dict.GetName("Subtype") == "Image")
                {
                    allImages.Add((xobjDict, key, stream, entry));
                }
            }
        }

        if (allImages.Count < 2) return 0;

        // Hash all image streams; the canonical value is the first entry's stored
        // object (its indirect ref), reused verbatim for every duplicate.
        var hashToCanonical = new Dictionary<string, PdfObject>(StringComparer.Ordinal);
        var duplicateCount = 0;

        // Identity is the DECODED pixel content, not the stored bytes — two flate
        // compressions of the same pixels differ byte-wise and must still merge.
        // The soft mask's decoded content joins the identity so images that differ
        // only in transparency never coalesce.
        string ContentHash(PdfStream s)
        {
            byte[] data;
            try { data = reader.DecodeStream(s) ?? s.RawData; }
            catch { data = s.RawData; }
            return Convert.ToHexString(Security.ShaDigest.Sha256(data));
        }

        foreach (var (xobjDict, key, stream, entry) in allImages)
        {
            var width = stream.Dict.GetInt("Width");
            var height = stream.Dict.GetInt("Height");
            var cs = stream.Dict.GetName("ColorSpace") ?? "";
            var bpc = stream.Dict.GetInt("BitsPerComponent");
            var smaskHash = reader.Resolve(stream.Dict.Get("SMask")) is PdfStream sm ? ContentHash(sm) : "";

            var hash = ContentHash(stream) + $"|{width}|{height}|{cs}|{bpc}|{smaskHash}";

            if (hashToCanonical.TryGetValue(hash, out var canonical))
            {
                xobjDict.Set(key, canonical);
                duplicateCount++;
            }
            else
            {
                hashToCanonical[hash] = entry;
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
        // (anti-aliasing the box-averaged greys keeps the text legible) — but only while the
        // target resolution still holds legible text. Below a legibility floor, box-averaging
        // a fax scan to grey destroys the very text it exists to carry for a negligible saving
        // over its existing G4 compression, so such a scan is left at full resolution.
        if (bpc == 1 && !stream.Dict.GetBool("ImageMask") && IsGrayOrCcitt(stream, reader))
        {
            const int BilevelLegibilityFloorDpi = 60;
            if (maxDpi < BilevelLegibilityFloorDpi) return;
            TryDownsampleBilevel(stream, reader, width, height, newWidth, newHeight);
            return;
        }

        // Sub-byte grayscale (2- or 4-bit DeviceGray, e.g. quantized scans): unpack to
        // one byte per pixel, box-filter down, and re-store as 8-bit gray Flate. Fewer,
        // wider samples more than offset the 2→8 bit growth, and the generic 8-bit path
        // below cannot read the packed rows.
        if ((bpc == 2 || bpc == 4) && components == 1
            && !stream.Dict.GetBool("ImageMask")
            && stream.Dict.GetName("ColorSpace") == "DeviceGray")
        {
            byte[] packed;
            try { packed = reader.DecodeStream(stream); }
            catch { return; }

            var rowBytes = (width * bpc + 7) / 8;
            if (packed.Length < rowBytes * height) return;
            var maxVal = (1 << bpc) - 1;
            var gray = new byte[width * height];
            for (var y = 0; y < height; y++)
            {
                var rowOff = y * rowBytes;
                for (var x = 0; x < width; x++)
                {
                    var bitPos = x * bpc;
                    var b = packed[rowOff + (bitPos >> 3)];
                    var shift = 8 - bpc - (bitPos & 7);
                    var sample = (b >> shift) & maxVal;
                    gray[y * width + x] = (byte)(sample * 255 / maxVal);
                }
            }

            var down = BoxFilterDownsample(gray, width, height, 1, newWidth, newHeight);
            var comp = Compress(down);
            stream.ReplaceData(comp);
            stream.Dict.Set("Filter", new PdfName("FlateDecode"));
            stream.Dict.Set("Length", new PdfInteger(comp.Length));
            stream.Dict.Set("Width", new PdfInteger(newWidth));
            stream.Dict.Set("Height", new PdfInteger(newHeight));
            stream.Dict.Set("BitsPerComponent", new PdfInteger(8));
            stream.Dict.Remove("DecodeParms");
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
