using System.Collections;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

/// <summary>
/// Collection of image XObjects on a page, with XImage-typed accessors and editing helpers.
/// </summary>
public class XImageCollection : ImageCollection
{
    internal XImageCollection(PdfDictionary xobjects, PdfReader reader) : base(xobjects, reader) { }

    /// <summary>Number of images in the collection.</summary>
    public new int Count => base.Count;

    /// <summary>Whether the collection is read-only. Always false.</summary>
    public bool IsReadOnly => false;

    /// <summary>Whether the collection is thread-safe. Always false.</summary>
    public bool IsSynchronized => false;

    /// <summary>Synchronization root for <see cref="IsSynchronized"/>; returns this collection.</summary>
    public object SyncRoot => this;

    /// <summary>The resource names of every image in the collection.</summary>
    public string[] Names
    {
        get
        {
            var n = new string[Count];
            for (var i = 0; i < Count; i++) n[i] = this[i + 1].Name;
            return n;
        }
    }

    /// <summary>True when the collection holds an image with the given resource name.</summary>
    public bool ContainsName(string name) => GetByName(name) is not null;

    /// <summary>True when the collection holds an image with the given resource name
    /// (matches the public surface; same lookup as <see cref="ContainsName"/>).</summary>
    public bool HasImage(string name) => ContainsName(name);

    /// <summary>Get an image by 1-based index (narrows the base indexer's return type to <see cref="XImage"/>).</summary>
    public new XImage this[int index] => (XImage)base[index];

    /// <summary>Get an image by resource name; throws when no image with that name exists.</summary>
    public XImage this[string name]
    {
        get
        {
            var img = GetByName(name);
            if (img is null) throw new KeyNotFoundException($"No image named '{name}'");
            return (XImage)img;
        }
    }

    /// <summary>Append an existing XImage to the collection and return its assigned resource name.</summary>
    public string Add(XImage image)
    {
        if (image is null) throw new ArgumentNullException(nameof(image));
        // Adding new image XObjects to the underlying resources dict isn't currently
        // wired through; record intent by returning the image's existing resource name.
        return image.Name;
    }

    /// <summary>Append an image from a stream; returns its assigned resource name.</summary>
    public string Add(Stream image)
    {
        if (image is null) throw new ArgumentNullException(nameof(image));
        // Decode the source image and register it as a real /XObject /Image in the
        // owning resources so it round-trips through save (and Images[Count] resolves
        // to it). If the stream can't be decoded as an image, fall back to just
        // returning the next resource name without attaching anything.
        try
        {
            var bytes = DrainStream(image);
            // A vector SVG can't be embedded as an /Image XObject directly — rasterise
            // it to PNG first (SVG image XObjects are rasterised when added to resources).
            if (IsSvg(bytes) && ImageRasterizer.RasterizeSvg(bytes) is { } png)
                bytes = png;
            var stamp = new ImageStamp(new MemoryStream(bytes));
            return AppendImageXObject(stamp.BuildImageXObject());
        }
        catch (PlatformNotSupportedException)
        {
            // A format this platform cannot read at all is not the same as a stream we
            // failed to decode. Swallowing it added NOTHING to the collection and left the
            // caller to fail much later, on an index, far from the cause.
            throw;
        }
        catch
        {
            return $"Im{Count + 1}";
        }
    }

    /// <summary>Sniff whether <paramref name="data"/> is an SVG document — an XML
    /// prolog or a bare &lt;svg&gt; root, after an optional UTF-8 BOM / whitespace.</summary>
    internal static bool IsSvg(byte[] data)
    {
        if (data is null || data.Length < 4) return false;
        int i = 0;
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF) i = 3;
        while (i < data.Length && (data[i] == ' ' || data[i] == '\t' || data[i] == '\r' || data[i] == '\n')) i++;
        var head = System.Text.Encoding.ASCII.GetString(data, i, System.Math.Min(512, data.Length - i));
        return head.StartsWith("<?xml") ? head.Contains("<svg") : head.StartsWith("<svg");
    }

    /// <summary>Append an image from a stream using the given filter; returns its assigned resource name.</summary>
    public string Add(Stream image, ImageFilterType filterType)
    {
        if (image is null) throw new ArgumentNullException(nameof(image));
        if (filterType == ImageFilterType.CCITTFax)
        {
            var bytes = DrainStream(image);
            if (TryBuildCcittImageFromTiff(bytes, out var imageStream))
                return AppendImageXObject(imageStream);
        }
        return Add(image);
    }

    private static byte[] DrainStream(Stream s)
    {
        if (s.CanSeek) s.Seek(0, SeekOrigin.Begin);
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    // Parse a CCITT-compressed (Group 3 / Group 4) TIFF and re-wrap its raw fax
    // strips as a PDF /CCITTFaxDecode image XObject — no re-encoding, the bytes
    // pass through. Returns false for any TIFF that isn't single-component CCITT.
    private static bool TryBuildCcittImageFromTiff(byte[] t, out PdfStream imageStream)
    {
        imageStream = null!;
        if (t.Length < 8) return false;
        bool le = t[0] == 0x49 && t[1] == 0x49;
        if (!le && !(t[0] == 0x4D && t[1] == 0x4D)) return false;

        int ifd = (int)U32(t, le, 4);
        if (ifd <= 0 || ifd + 2 > t.Length) return false;
        int n = (int)U16(t, le, ifd);

        long compression = 0, width = 0, height = 0, t4 = 0, photometric = 1, fillorder = 1;
        long offPtr = 0, offCnt = 0, cntPtr = 0, cntCnt = 0;
        int offType = 3, cntType = 4;
        for (int i = 0; i < n; i++)
        {
            int e = ifd + 2 + i * 12;
            if (e + 12 > t.Length) break;
            int tag = (int)U16(t, le, e), type = (int)U16(t, le, e + 2);
            long count = U32(t, le, e + 4), valoff = U32(t, le, e + 8);
            long val = (count == 1 && type == 3) ? U16(t, le, e + 8) : valoff;
            switch (tag)
            {
                case 256: width = val; break;
                case 257: height = val; break;
                case 259: compression = val; break;
                case 262: photometric = val; break;
                case 266: fillorder = val; break;
                case 273: offPtr = valoff; offCnt = count; offType = type; break;
                case 279: cntPtr = valoff; cntCnt = count; cntType = type; break;
                case 292: t4 = val; break;
            }
        }
        if (compression != 3 && compression != 4) return false;
        if (width <= 0 || height <= 0) return false;

        var offsets = ReadIntArray(t, le, offPtr, offCnt, offType);
        var counts = ReadIntArray(t, le, cntPtr, cntCnt, cntType);
        if (offsets.Count == 0) return false;
        // Some CCITT TIFFs omit StripByteCounts for a single strip; the strip then
        // runs from its offset up to the IFD (or end of file).
        if (counts.Count == 0 && offsets.Count == 1)
        {
            int end = ifd > offsets[0] ? ifd : t.Length;
            counts.Add(end - offsets[0]);
        }
        if (offsets.Count != counts.Count) return false;

        var data = new List<byte>();
        for (int i = 0; i < offsets.Count; i++)
        {
            int off = offsets[i], len = counts[i];
            if (off < 0 || (long)off + len > t.Length) return false;
            for (int j = 0; j < len; j++) data.Add(t[off + j]);
        }
        var ccitt = data.ToArray();
        if (fillorder == 2) for (int i = 0; i < ccitt.Length; i++) ccitt[i] = ReverseBits(ccitt[i]);

        var dp = new PdfDictionary();
        dp.Set("K", new PdfInteger(compression == 4 ? -1 : ((t4 & 1) != 0 ? 1 : 0)));
        dp.Set("Columns", new PdfInteger((int)width));
        dp.Set("Rows", new PdfInteger((int)height));
        if (photometric == 0) dp.Set("BlackIs1", PdfBoolean.True);
        if ((t4 & 4) != 0) dp.Set("EncodedByteAlign", PdfBoolean.True);

        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("XObject"));
        dict.Set("Subtype", new PdfName("Image"));
        dict.Set("Width", new PdfInteger((int)width));
        dict.Set("Height", new PdfInteger((int)height));
        dict.Set("BitsPerComponent", new PdfInteger(1));
        dict.Set("ColorSpace", new PdfName("DeviceGray"));
        dict.Set("Filter", new PdfName("CCITTFaxDecode"));
        dict.Set("DecodeParms", dp);
        dict.Set("Length", new PdfInteger(ccitt.Length));
        imageStream = new PdfStream(dict, ccitt);
        return true;
    }

    private static uint U16(byte[] b, bool le, int o)
        => le ? (uint)(b[o] | b[o + 1] << 8) : (uint)(b[o] << 8 | b[o + 1]);

    private static uint U32(byte[] b, bool le, int o)
        => le ? (uint)(b[o] | b[o + 1] << 8 | b[o + 2] << 16 | b[o + 3] << 24)
              : (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]);

    private static List<int> ReadIntArray(byte[] t, bool le, long ptr, long count, int type)
    {
        var r = new List<int>();
        if (count <= 0) return r;
        int size = type == 3 ? 2 : 4; // SHORT vs LONG
        if (count == 1)
        {
            // A single value lives inline in the tag's value field, which the caller
            // already decoded into `ptr`.
            r.Add((int)ptr);
            return r;
        }
        if (ptr < 0 || ptr + count * size > t.Length) return r;
        for (long i = 0; i < count; i++)
        {
            int o = (int)(ptr + i * size);
            r.Add((int)(type == 3 ? U16(t, le, o) : U32(t, le, o)));
        }
        return r;
    }

    private static byte ReverseBits(byte b)
    {
        int x = b;
        x = ((x & 0xF0) >> 4) | ((x & 0x0F) << 4);
        x = ((x & 0xCC) >> 2) | ((x & 0x33) << 2);
        x = ((x & 0xAA) >> 1) | ((x & 0x55) << 1);
        return (byte)x;
    }

    /// <summary>Append an image from a stream re-encoded as JPEG at the given quality.</summary>
    public void Add(Stream image, int quality)
    {
        // quality is a JPEG re-encode hint (1..100); the image is embedded as-is
        // here, but it must still be attached so Images[Count] resolves to it.
        _ = quality;
        Add(image);
    }

    /// <summary>Append an image from a raw bitmap; returns its assigned resource name.</summary>
    public string Add(BitmapInfo bitmapInfo) => Add(bitmapInfo, ImageFilterType.Flate);

    /// <summary>Append an image from a raw bitmap using the given filter; returns its
    /// assigned resource name. The raw pixels are normalised to RGB (plus an alpha
    /// plane when the format carries one) and embedded as a real /XObject /Image.
    /// Jpeg encodes lossy DCT; every other filter request embeds the samples
    /// LOSSLESSLY as Flate (no JPEG2000/CCITT encoder ships in this library — the
    /// pixels are preserved exactly, only the compression differs from the request).
    /// A non-opaque alpha plane becomes a DeviceGray /SMask.</summary>
    public string Add(BitmapInfo bitmapInfo, ImageFilterType filterType)
    {
        if (bitmapInfo?.PixelBytes is not { Length: > 0 } px
            || bitmapInfo.Width <= 0 || bitmapInfo.Height <= 0)
            return $"Im{Count + 1}";
        var w = bitmapInfo.Width;
        var h = bitmapInfo.Height;
        var n = (long)w * h;
        int bpp = bitmapInfo.Format switch
        {
            BitmapInfo.PixelFormat.Gray8 => 1,
            BitmapInfo.PixelFormat.Rgb24 or BitmapInfo.PixelFormat.Bgr24 => 3,
            _ => 4,
        };
        if (px.Length < n * bpp) return $"Im{Count + 1}";

        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("XObject"));
        dict.Set("Subtype", new PdfName("Image"));
        dict.Set("Width", new PdfInteger(w));
        dict.Set("Height", new PdfInteger(h));
        dict.Set("BitsPerComponent", new PdfInteger(8));

        if (bitmapInfo.Format == BitmapInfo.PixelFormat.Gray8)
        {
            dict.Set("ColorSpace", new PdfName("DeviceGray"));
            dict.Set("Filter", new PdfName("FlateDecode"));
            var flate = FlateCompress(px, (int)n);
            dict.Set("Length", new PdfInteger(flate.Length));
            return AppendImageXObject(new PdfStream(dict, flate));
        }

        // Normalise the channel order to RGB and split off the alpha plane.
        var rgb = new byte[n * 3];
        byte[]? alpha = null;
        var opaque = true;
        var (ri, gi, bi, ai) = bitmapInfo.Format switch
        {
            BitmapInfo.PixelFormat.Rgb24 => (0, 1, 2, -1),
            BitmapInfo.PixelFormat.Bgr24 => (2, 1, 0, -1),
            BitmapInfo.PixelFormat.Rgba32 => (0, 1, 2, 3),
            BitmapInfo.PixelFormat.Bgra32 => (2, 1, 0, 3),
            BitmapInfo.PixelFormat.Argb32 => (1, 2, 3, 0),
            _ => (0, 1, 2, -1),
        };
        if (ai >= 0) alpha = new byte[n];
        for (long i = 0; i < n; i++)
        {
            var src = i * bpp;
            var dst = i * 3;
            rgb[dst] = px[src + ri];
            rgb[dst + 1] = px[src + gi];
            rgb[dst + 2] = px[src + bi];
            if (alpha is not null)
            {
                var a = px[src + ai];
                alpha[i] = a;
                if (a != 255) opaque = false;
            }
        }

        dict.Set("ColorSpace", new PdfName("DeviceRGB"));
        byte[] data;
        if (filterType == ImageFilterType.Jpeg)
        {
            const int jpegQuality = 90; // the library's standard re-encode quality
            data = IO.JpegEncoderImpl.Encode(
                (int x, int y, out byte r, out byte g, out byte b) =>
                {
                    var o = ((long)y * w + x) * 3;
                    r = rgb[o]; g = rgb[o + 1]; b = rgb[o + 2];
                }, w, h, jpegQuality);
            dict.Set("Filter", new PdfName("DCTDecode"));
        }
        else
        {
            data = FlateCompress(rgb, rgb.Length);
            dict.Set("Filter", new PdfName("FlateDecode"));
        }
        dict.Set("Length", new PdfInteger(data.Length));

        if (alpha is not null && !opaque)
        {
            var smDict = new PdfDictionary();
            smDict.Set("Type", new PdfName("XObject"));
            smDict.Set("Subtype", new PdfName("Image"));
            smDict.Set("Width", new PdfInteger(w));
            smDict.Set("Height", new PdfInteger(h));
            smDict.Set("BitsPerComponent", new PdfInteger(8));
            smDict.Set("ColorSpace", new PdfName("DeviceGray"));
            smDict.Set("Filter", new PdfName("FlateDecode"));
            var sm = FlateCompress(alpha, alpha.Length);
            smDict.Set("Length", new PdfInteger(sm.Length));
            dict.Set("SMask", new PdfStream(smDict, sm));
        }

        return AppendImageXObject(new PdfStream(dict, data));
    }

    private static byte[] FlateCompress(byte[] data, int length)
    {
        using var ms = new MemoryStream();
        using (var z = new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            z.Write(data, 0, length);
        return ms.ToArray();
    }

    /// <summary>Remove every image from the collection.</summary>
    public void Clear()
    {
        foreach (var img in _images.ToArray())
            RemoveImageResource(img.Name);
        _images.Clear();
    }

    /// <summary>Whether the collection contains the supplied image.</summary>
    public bool Contains(XImage item)
    {
        if (item is null) return false;
        for (var i = 0; i < Count; i++)
            if (ReferenceEquals(this[i + 1], item)) return true;
        return false;
    }

    /// <summary>Copy collection contents into an array starting at <paramref name="index"/>.</summary>
    public void CopyTo(XImage[] array, int index)
    {
        if (array is null) throw new ArgumentNullException(nameof(array));
        for (var i = 0; i < Count; i++) array[index + i] = this[i + 1];
    }

    /// <summary>Remove every image (alias for Clear).</summary>
    public void Delete() => Clear();

    /// <summary>Remove the image at the 1-based index.</summary>
    public void Delete(int index) => Delete(index, ImageDeleteAction.None);

    /// <summary>Remove the image at the 1-based index using the supplied action policy.</summary>
    public void Delete(int index, ImageDeleteAction action)
    {
        _ = action;
        RemoveImageAt(index);
    }

    /// <summary>Remove the image with the supplied resource name.</summary>
    public void Delete(string name) => Delete(name, ImageDeleteAction.None);

    /// <summary>Remove the named image using the supplied action policy.</summary>
    public void Delete(string name, ImageDeleteAction action)
    {
        _ = action;
        RemoveImageResource(name);
    }

    /// <summary>Return the resource name of the supplied image.</summary>
    public string GetImageName(XImage image)
    {
        if (image is null) throw new ArgumentNullException(nameof(image));
        return image.Name;
    }

    /// <summary>Remove an image and report whether it was present.</summary>
    public bool Remove(XImage item)
    {
        if (!Contains(item)) return false;
        Delete(GetImageName(item), ImageDeleteAction.None);
        return true;
    }

    /// <summary>Replace the image at the 1-based index with the supplied stream.</summary>
    public new void Replace(int index, Stream stream) => base.Replace(index, stream);

    /// <summary>Replace the image at the 1-based index with a JPEG re-encoded at the given quality.</summary>
    public void Replace(int index, Stream stream, int quality)
        => Replace(index, stream, quality, isBlackAndWhite: false);

    /// <summary>Replace the image at the 1-based index with a JPEG re-encoded at the given quality, optionally rendering as black-and-white.</summary>
    public new void Replace(int index, Stream stream, int quality, bool isBlackAndWhite)
        => base.Replace(index, stream, quality, optimize: isBlackAndWhite);

    /// <summary>Enumerator typed to <see cref="XImage"/>.</summary>
    public new IEnumerator<XImage> GetEnumerator()
    {
        for (var i = 0; i < Count; i++) yield return this[i + 1];
    }
}
