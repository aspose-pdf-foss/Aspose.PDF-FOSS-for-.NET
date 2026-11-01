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
        RecodeAll(FindAllImageStreams(reader), s => PrepareRecode(s, reader, quality, maskStreams), RecodeImage);
    }

    /// <summary>
    /// Collect every image stream that is referenced as a soft mask (/SMask) or
    /// stencil/colour-key mask (/Mask) by another image. These carry alpha or key data
    /// and must not be re-encoded to a different colour model.
    /// </summary>
    private static HashSet<PdfStream> CollectMaskStreams(PdfReader reader)
    {
        var masks = new HashSet<PdfStream>(ReferenceEqualityComparer.Instance);
        foreach (var dict in FindAllImageStreams(reader).Select(img => img.Dict))
        {
            if (reader.Resolve(dict.Get("SMask")) is PdfStream sm) masks.Add(sm);
            if (reader.Resolve(dict.Get("Mask")) is PdfStream mk) masks.Add(mk);
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
        // never re-model a mask's colour
        var images = FindAllImageStreams(reader).Where(s => !maskStreams.Contains(s)).ToList();
        var drawnSizes = DrawnSizes(reader, new HashSet<PdfStream>(images.Where(CouldResample), ReferenceEqualityComparer.Instance));
        RecodeAll(images, s => PrepareDownsample(s, reader, maxDpi, quality, maskStreams, drawnSizes), Downsample);
    }

    /// <summary>Whether the downsample pass can change the image wherever it is drawn (a codec it
    /// recodes, a size): only such images are looked for on the pages.</summary>
    private static bool CouldResample(PdfStream image) =>
        GetFilterName(image) is not ("JPXDecode" or "JBIG2Decode")
        && image.Dict.GetInt("Width", 0) > 0 && image.Dict.GetInt("Height", 0) > 0;

    /// <summary>
    /// Convert RGB images to grayscale, reducing data size by ~3x.
    /// </summary>
    public static void ConvertToGrayscale(PdfReader reader) =>
        RecodeAll(FindAllImageStreams(reader), s => PrepareGrayscale(s, reader), ToGrayscale);

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
        // only in transparency never coalesce. Each stream is hashed once, with the
        // platform's digest, however many pages share it.
        var contentHashes = new Dictionary<PdfStream, string>(ReferenceEqualityComparer.Instance);
        string ContentHash(PdfStream s)
        {
            if (contentHashes.TryGetValue(s, out var known)) return known;
            byte[] data;
            try { data = reader.DecodeStream(s) ?? s.RawData; }
            catch { data = s.RawData; }
            return contentHashes[s] = Compat.ToHexString(Compat.Sha256(data));
        }

        // The size, colour space, depth and soft-mask presence are part of the identity: an
        // image whose shape no other image shares cannot be a duplicate, so it is not decoded
        // and stands for itself.
        var shapes = new Dictionary<PdfStream, string>(ReferenceEqualityComparer.Instance);
        foreach (var image in allImages.Select(i => i.stream))
        {
            if (shapes.ContainsKey(image)) continue;
            shapes[image] = $"{image.Dict.GetInt("Width")}|{image.Dict.GetInt("Height")}|" +
                $"{image.Dict.GetName("ColorSpace") ?? ""}|{image.Dict.GetInt("BitsPerComponent")}|" +
                $"{reader.Resolve(image.Dict.Get("SMask")) is PdfStream}";
        }
        var imagesOfShape = shapes.Values.GroupBy(s => s, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        foreach (var (xobjDict, key, stream, entry) in allImages)
        {
            var shape = shapes[stream];
            string hash;
            if (imagesOfShape[shape] == 1)
            {
                // One image has this shape, and '#' never starts a hex digest: the key is its own.
                hash = "#" + shape;
            }
            else
            {
                var smaskHash = reader.Resolve(stream.Dict.Get("SMask")) is PdfStream sm ? ContentHash(sm) : "";
                hash = ContentHash(stream) + $"|{shape}|{smaskHash}";
            }

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
        foreach (var page in PageDicts(reader))
        {
            CollectXObjectDictFromNode(page, reader, result);
        }
    }

    /// <summary>The page dictionaries of the page tree, in order.</summary>
    private static List<PdfDictionary> PageDicts(PdfReader reader)
    {
        var pages = new List<PdfDictionary>();
        if (reader.Resolve(reader.Catalog.Get("Pages")) is PdfDictionary root)
        {
            WalkPages(root, reader, pages);
        }
        return pages;
    }

    private static void WalkPages(PdfDictionary node, PdfReader reader, List<PdfDictionary> pages)
    {
        var kids = reader.Resolve(node.Get("Kids")) as PdfArray;
        if (kids is null)
        {
            // This is a leaf page
            pages.Add(node);
            return;
        }

        for (var i = 0; i < kids.Count; i++)
        {
            var kid = reader.Resolve(kids[i]);
            if (kid is PdfDictionary kidDict)
            {
                var type = kidDict.GetName("Type");
                if (type == "Pages")
                    WalkPages(kidDict, reader, pages);
                else
                    pages.Add(kidDict);
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

    /// <summary>
    /// Downsample pixel data using a box filter (average NxN blocks).
    /// </summary>
    internal static byte[] BoxFilterDownsample(byte[] src, int srcW, int srcH,
        int components, int dstW, int dstH)
    {
        var dst = new byte[dstW * dstH * components];
        var sums = new int[components];

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
                Array.Clear(sums, 0, components);
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

    /// <summary>The image's codec: the last filter of a chain, which the filters before it only
    /// wrap (a JPEG stored deflated is [/FlateDecode /DCTDecode]).</summary>
    private static string? GetFilterName(PdfStream stream)
    {
        var filter = stream.Dict.Get("Filter");
        return filter switch
        {
            PdfName n => n.Value,
            PdfArray arr when arr.Count > 0 && arr[arr.Count - 1] is PdfName fn => fn.Value,
            _ => null,
        };
    }

    /// <summary>The samples per pixel of the image's colour space; 0 when it is not known, so no
    /// pass reads a buffer of another layout as this one.</summary>
    private static int GetComponents(PdfStream stream, PdfReader reader) =>
        reader.Resolve(stream.Dict.Get("ColorSpace")) switch
        {
            PdfName name => name.Value switch
            {
                "DeviceRGB" or "RGB" or "CalRGB" => 3,
                "DeviceCMYK" or "CMYK" => 4,
                "DeviceGray" or "G" or "CalGray" => 1,
                _ => 0,
            },
            PdfArray { Count: > 1 } arr when reader.Resolve(arr[0]) is PdfName family => family.Value switch
            {
                "ICCBased" => reader.Resolve(arr[1]) is PdfStream icc ? (int)icc.Dict.GetInt("N", 0) : 0,
                "CalRGB" or "Lab" => 3,
                "CalGray" or "Separation" or "Indexed" or "I" => 1,
                "DeviceN" => reader.Resolve(arr[1]) is PdfArray names ? names.Count : 0,
                _ => 0,
            },
            _ => 0,
        };

    private static byte[] Compress(byte[] data) => IO.Filters.ManagedDeflater.DeflateZlib(data, IO.Filters.DeflateLevel.Best);
}
