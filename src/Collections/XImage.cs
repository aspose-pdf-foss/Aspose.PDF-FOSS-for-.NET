using System.Collections;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

/// <summary>
/// Image XObject wrapper exposing the public XImage surface.
/// </summary>
public class XImage : ImageXObject
{
    internal XImage(string name, PdfStream stream, PdfReader reader) : base(name, stream, reader) { }

    /// <summary>The resource name (e.g., "Im0"); writable on the derived type.
    /// Setting it renames the image's entry in the owning /XObject resources so the
    /// new name is what a reload of the saved document reports.</summary>
    public new string Name { get => base.Name; set => RenameResource(value); }

    /// <summary>Image width in pixels.</summary>
    public new int Width => base.Width;

    /// <summary>Image height in pixels.</summary>
    public new int Height => base.Height;

    /// <summary>Whether the image has an /SMask or /Mask entry indicating per-pixel transparency.</summary>
    public bool ContainsTransparency => HasSoftMask;

    /// <summary>Delete this image from the resources it was retrieved from. The
    /// image object becomes unreachable and is dropped when the document is saved.</summary>
    public void Delete()
    {
        Reader.MayHaveOrphansOnSave = true;
        Owner?.RemoveImageResource(Name);
    }

    /// <summary>Replace this image's data with the supplied image stream, keeping
    /// its resource name so existing content-stream references show the new pixels.</summary>
    public void Replace(Stream stream)
    {
        if (stream is null) throw new ArgumentNullException(nameof(stream));
        if (stream.CanSeek) stream.Seek(0, SeekOrigin.Begin);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        ReplaceImageData(ms.ToArray());
    }

    /// <summary>Whether the image is a 1-bit stencil mask (matches base <see cref="ImageXObject.IsImageMask"/>).</summary>
    public bool ImageMask => IsImageMask;

    /// <summary>
    /// The compression filter applied to the image data. Maps the underlying /Filter
    /// PDF name to the enum; returns <see cref="ImageFilterType.Flate"/> by default.
    /// </summary>
    public ImageFilterType FilterType => Filter switch
    {
        "DCTDecode" => ImageFilterType.Jpeg,
        "JPXDecode" => ImageFilterType.Jpeg2000,
        "CCITTFaxDecode" => ImageFilterType.CCITTFax,
        _ => ImageFilterType.Flate,
    };

    /// <summary>
    /// Render the image as a grayscale System.Drawing.Image. Returns null on platforms
    /// where System.Drawing is unavailable (e.g. non-Windows) or when decoding fails.
    /// </summary>
    public System.Drawing.Image? Grayscaled
    {
        get
        {
            try
            {
#pragma warning disable CA1416
                using var ms = new MemoryStream(GetDecodedData());
                var bitmap = new System.Drawing.Bitmap(ms);
                return bitmap;
#pragma warning restore CA1416
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>XMP metadata attached to this image, parsed from the image
    /// XObject's /Metadata stream on first access (empty when the image carries
    /// no /Metadata). Cached so repeated reads — and edits before save — share
    /// one instance.</summary>
    public Metadata Metadata => _metadata ??= BuildMetadata();
    private Metadata? _metadata;

    private Metadata BuildMetadata()
    {
        var mdStream = Reader.ResolveStream(Stream.Dict.Get("Metadata"));
        if (mdStream is null) return new Metadata();
        var xmp = new XmpMetadata(mdStream, Reader);
        // Edits to image XMP are written straight back into this /Metadata stream
        // (the reader caches it, so the save loop serialises the mutated bytes).
        xmp.EnableWriteBackTo(mdStream);
        return new Metadata(xmp);
    }

    /// <summary>Attach a stencil mask built from the given image stream. Dark mask
    /// pixels keep the corresponding image area painted; light pixels knock it out.
    /// Stored as a 1-bit /ImageMask stream in this image's /Mask entry (sample 1 =
    /// masked under the default /Decode [0 1]), which both renderers honour.</summary>
    public void AddStencilMask(Stream maskStream)
    {
        if (maskStream is null) return;
        using var ms = new MemoryStream();
        maskStream.CopyTo(ms);
        ms.Position = 0;
        int w, h;
        byte[] packed;
#pragma warning disable CA1416
        using (var bmp = new System.Drawing.Bitmap(ms))
        {
            w = bmp.Width; h = bmp.Height;
            var rowBytes = (w + 7) / 8;
            packed = new byte[rowBytes * h];
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var p = bmp.GetPixel(x, y);
                    // Luminance with alpha composited over white — transparent mask
                    // areas count as light (masked out).
                    var a = p.A / 255.0;
                    var luma = (0.299 * p.R + 0.587 * p.G + 0.114 * p.B) * a + 255.0 * (1.0 - a);
                    if (luma >= 128.0)
                        packed[y * rowBytes + (x >> 3)] |= (byte)(0x80 >> (x & 7));
                }
            }
        }
#pragma warning restore CA1416
        using var compressed = new MemoryStream();
        using (var z = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            z.Write(packed, 0, packed.Length);
        var data = compressed.ToArray();
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("XObject"));
        dict.Set("Subtype", new PdfName("Image"));
        dict.Set("Width", new PdfInteger(w));
        dict.Set("Height", new PdfInteger(h));
        dict.Set("ImageMask", PdfBoolean.True);
        dict.Set("BitsPerComponent", new PdfInteger(1));
        dict.Set("Filter", new PdfName("FlateDecode"));
        dict.Set("Length", new PdfInteger(data.Length));
        Stream.Dict.Set("Mask", new PdfStream(dict, data));
    }

    /// <summary>Detect whether a bitmap is grayscale, RGB, or CMYK by sampling its pixels.</summary>
    public static ColorType DetectColorType(System.Drawing.Bitmap bmp)
    {
        if (bmp is null) return ColorType.Undefined;
        try
        {
#pragma warning disable CA1416
            var allGray = true;
            int w = Math.Min(bmp.Width, 64);
            int h = Math.Min(bmp.Height, 64);
            for (var y = 0; y < h && allGray; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var p = bmp.GetPixel(x, y);
                    if (p.R != p.G || p.G != p.B) { allGray = false; break; }
                }
            }
            return allGray ? ColorType.Grayscale : ColorType.Rgb;
#pragma warning restore CA1416
        }
        catch
        {
            return ColorType.Undefined;
        }
    }

    /// <summary>
    /// Alternative-text accessor — returns alt strings declared for this image in
    /// the page's structure tree. Returns an empty list when the page has no
    /// tagged-PDF structure or no alt text for this image.
    /// </summary>
    public List<string> GetAlternativeText(Page page)
    {
        var result = new List<string>();
        if (page is null) return result;
        var mcids = FindMcidsForImage(page, Name);
        if (mcids.Count == 0) return result;
        var root = page.Reader.ResolveDict(page.Reader.Catalog.Get("StructTreeRoot"));
        if (root is null) return result;
        foreach (var element in FindStructElementsForMcids(page, root, mcids))
        {
            var alt = page.Reader.Resolve(element.Get("Alt"));
            if (alt is PdfString s) result.Add(s.ToText());
        }
        return result;
    }

    /// <summary>
    /// The MCIDs of the marked-content sequences that draw the named image XObject
    /// on the page (in content order, distinct). An image drawn outside any
    /// /MCID-bearing marked content contributes nothing.
    /// </summary>
    private static List<int> FindMcidsForImage(Page page, string imageName)
    {
        var mcids = new List<int>();
        var reader = page.Reader;
        var resources = reader.ResolveDict(page.Dict.Get("Resources"));
        var properties = resources is null ? null : reader.ResolveDict(resources.Get("Properties"));

        // Innermost enclosing MCID wins; BMC and MCID-less BDC push null so
        // EMC pops stay balanced.
        var stack = new List<int?>();
        var parser = new Content.ContentStreamParser(reader);
        parser.OnMarkedContentBegin += (_, props) =>
            stack.Add(props?.Get("MCID") is PdfInteger m ? (int)m.Value : null);
        parser.OnMarkedContentEnd += () =>
        {
            if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
        };
        parser.OnImageDrawn += (name, _) =>
        {
            if (name != imageName) return;
            for (var i = stack.Count - 1; i >= 0; i--)
            {
                if (stack[i] is { } mcid)
                {
                    if (!mcids.Contains(mcid)) mcids.Add(mcid);
                    return;
                }
            }
        };

        foreach (var bytes in GetPageContentStreams(page))
            parser.Parse(bytes, properties: properties);
        return mcids;
    }

    private static List<byte[]> GetPageContentStreams(Page page)
    {
        var reader = page.Reader;
        var result = new List<byte[]>();
        var contents = reader.Resolve(page.Dict.Get("Contents"));
        if (contents is PdfStream single)
        {
            result.Add(reader.DecodeStream(single));
        }
        else if (contents is PdfArray arr)
        {
            foreach (var item in arr)
            {
                var s = reader.ResolveStream(item);
                if (s is not null) result.Add(reader.DecodeStream(s));
            }
        }
        return result;
    }

    /// <summary>
    /// Structure elements (pre-order) with a marked-content kid on <paramref name="page"/>
    /// whose MCID is in <paramref name="mcids"/>. The /Pg page association is inherited
    /// down the tree; an MCR kid may override it.
    /// </summary>
    private static List<PdfDictionary> FindStructElementsForMcids(Page page, PdfDictionary root, List<int> mcids)
    {
        var reader = page.Reader;
        var found = new List<PdfDictionary>();
        var visited = new HashSet<PdfDictionary>();

        bool IsTargetPage(PdfObject? pgEntry)
        {
            if (pgEntry is null) return false;
            if (pgEntry is PdfIndirectRef r && page.SourceObjectNumber > 0)
                return r.ObjectNumber == page.SourceObjectNumber;
            return ReferenceEquals(reader.ResolveDict(pgEntry), page.Dict);
        }

        void Walk(PdfDictionary element, bool pgIsTarget)
        {
            if (!visited.Add(element)) return;
            var ownPg = element.Get("Pg");
            if (ownPg is not null) pgIsTarget = IsTargetPage(ownPg);

            var kids = reader.Resolve(element.Get("K"));
            var kidList = kids is PdfArray arr ? arr.ToList()
                : kids is not null ? new List<PdfObject> { kids }
                : new List<PdfObject>();

            // Match the element's own marked-content kids first (pre-order: an
            // element precedes its descendants), then recurse into child elements.
            foreach (var kid in kidList)
            {
                var resolved = reader.Resolve(kid);
                var matched = resolved switch
                {
                    PdfInteger mcid => pgIsTarget && mcids.Contains((int)mcid.Value),
                    PdfDictionary mcr when mcr.GetName("Type") == "MCR" =>
                        (mcr.Get("Pg") is { } p ? IsTargetPage(p) : pgIsTarget)
                        && mcids.Contains((int)mcr.GetInt("MCID")),
                    _ => false,
                };
                if (matched)
                {
                    found.Add(element);
                    break;
                }
            }
            foreach (var kid in kidList)
            {
                if (reader.Resolve(kid) is PdfDictionary child
                    && child.GetName("Type") is null or "StructElem"
                    && child.GetName("Type") != "MCR")
                    Walk(child, pgIsTarget);
            }
        }

        var rootKids = reader.Resolve(root.Get("K"));
        if (rootKids is PdfArray rootArr)
        {
            foreach (var kid in rootArr)
                if (reader.ResolveDict(kid) is { } d) Walk(d, pgIsTarget: false);
        }
        else if (rootKids is not null && reader.ResolveDict(rootKids) is { } single)
        {
            Walk(single, pgIsTarget: false);
        }
        return found;
    }

    /// <summary>Detect the colour family of the image. The declared /ColorSpace gives the
    /// base family, but an image stored in an RGB space whose pixels are all neutral
    /// (R==G==B) is really a black-and-white image and is reported as
    /// Grayscale. So RGB-family images are sampled and downgraded to Grayscale when their
    /// decoded content carries no colour. Declared Gray/CMYK keep their name-based type.</summary>
    public ColorType GetColorType()
    {
        var byName = ColorSpace switch
        {
            "DeviceGray" or "CalGray" => ColorType.Grayscale,
            "DeviceCMYK" => ColorType.Cmyk,
            _ => ColorType.Rgb,
        };
        // An RGB-declared image whose pixels are all neutral reports Grayscale. The sampling
        // ran through System.Drawing, so away from Windows every such image stayed Rgb and a
        // caller counting greyscale pictures counted none. Only ever downgrade RGB->Grayscale.
        if (byName == ColorType.Rgb
            && (OperatingSystem.IsWindows()
                ? DetectColorTypeByPixels()
                : DetectColorTypeByPixelsManaged()) == ColorType.Grayscale)
            return ColorType.Grayscale;
        return byName;
    }

    /// <summary>The managed half of <see cref="DetectColorTypeByPixels"/>: the same ~64x64
    /// stride over the same decoded pixels, read through the built-in pixel source rather
    /// than a platform bitmap.</summary>
    private ColorType DetectColorTypeManagedOrUndefined()
    {
        var getter = GetPixelSource();
        if (getter is null) return ColorType.Undefined;
        int w = Width, h = Height;
        if (w <= 0 || h <= 0) return ColorType.Undefined;
        int stepX = Math.Max(1, w / 64), stepY = Math.Max(1, h / 64);
        for (var y = 0; y < h; y += stepY)
            for (var x = 0; x < w; x += stepX)
            {
                getter(x, y, out var r, out var g, out var b);
                if (r != g || g != b) return ColorType.Rgb;
            }
        return ColorType.Grayscale;
    }

    private ColorType DetectColorTypeByPixelsManaged()
    {
        try { return DetectColorTypeManagedOrUndefined(); }
        catch { return ColorType.Undefined; }
    }

    /// <summary>Sample the decoded image across a ~64×64 grid and report Grayscale when every
    /// sampled pixel is neutral, Rgb on the first coloured pixel, Undefined when decoding
    /// fails. Strides over the whole image (not just a corner) so a colour patch anywhere is
    /// caught.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private ColorType DetectColorTypeByPixels()
    {
        try
        {
#pragma warning disable CA1416
            using var ms = new MemoryStream(GetDecodedData());
            using var bmp = new System.Drawing.Bitmap(ms);
            int stepX = Math.Max(1, bmp.Width / 64);
            int stepY = Math.Max(1, bmp.Height / 64);
            for (int y = 0; y < bmp.Height; y += stepY)
                for (int x = 0; x < bmp.Width; x += stepX)
                {
                    var p = bmp.GetPixel(x, y);
                    if (p.R != p.G || p.G != p.B) return ColorType.Rgb;
                }
            return ColorType.Grayscale;
#pragma warning restore CA1416
        }
        catch
        {
            return ColorType.Undefined;
        }
    }

    /// <summary>The resource name as registered in the page's XObject dictionary.</summary>
    public string GetNameInCollection() => Name;

    /// <summary>Get a copy of the raw (encoded) image data as a seekable MemoryStream.</summary>
    public MemoryStream GetRawImageData() => new(GetRawData());

    /// <summary>Reference equality against another XImage.</summary>
    public bool IsTheSameObject(XImage image)
    {
        if (image is null) return false;
        if (ReferenceEquals(this, image)) return true;
        // Two XImage wrappers refer to the same image when they wrap the same underlying
        // indirect PDF stream — the reader shares one PdfStream instance per XObject, so a
        // reference check on the stream identifies images shared across pages (a fresh wrapper
        // is produced on every Resources.Images[...] access, so wrapper identity is not enough).
        return ReferenceEquals(Stream, image.Stream);
    }

    /// <summary>Rename the image's resource entry.</summary>
    public void Rename(string name)
    {
        if (string.IsNullOrEmpty(name)) return;
        Name = name;
    }

    /// <summary>Write the image bytes to a stream.</summary>
    public new void Save(Stream stream) => base.Save(stream);

    /// <summary>Write the image bytes to a stream re-encoded as <paramref name="format"/>.</summary>
    public new void Save(Stream stream, System.Drawing.Imaging.ImageFormat format) => base.Save(stream, format);

    /// <summary>Write the image re-encoded as the given <see cref="Aspose.Pdf.Drawing.ImageFormat"/>.
    /// TIFF is encoded directly from the decoded pixels (the System.Drawing-format path has no
    /// TIFF writer); other formats route through the existing GDI-format overload.</summary>
    public void Save(Stream stream, Aspose.Pdf.Drawing.ImageFormat format)
    {
        if (format == Aspose.Pdf.Drawing.ImageFormat.Tiff)
        {
            var getter = GetPixelSource();
            if (getter is not null)
            {
                int w = Width, h = Height;
                var rgba = new byte[w * h * 4];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        getter(x, y, out var r, out var g, out var b);
                        int o = (y * w + x) * 4;
                        rgba[o] = r; rgba[o + 1] = g; rgba[o + 2] = b; rgba[o + 3] = 255;
                    }
                Aspose.Pdf.Devices.TiffDevice.EncodeRgbaImage(rgba, w, h, stream,
                    Aspose.Pdf.Devices.CompressionType.LZW);
                return;
            }
        }
        base.Save(stream, ToGdiImageFormat(format));
    }

#pragma warning disable CA1416 // System.Drawing.Imaging.ImageFormat members are Windows-gated; the base writer only branches on the format's GUID.
    private static System.Drawing.Imaging.ImageFormat ToGdiImageFormat(Aspose.Pdf.Drawing.ImageFormat format) => format switch
    {
        Aspose.Pdf.Drawing.ImageFormat.Bmp => System.Drawing.Imaging.ImageFormat.Bmp,
        Aspose.Pdf.Drawing.ImageFormat.Gif => System.Drawing.Imaging.ImageFormat.Gif,
        Aspose.Pdf.Drawing.ImageFormat.Jpeg => System.Drawing.Imaging.ImageFormat.Jpeg,
        Aspose.Pdf.Drawing.ImageFormat.Tiff => System.Drawing.Imaging.ImageFormat.Tiff,
        _ => System.Drawing.Imaging.ImageFormat.Png,
    };
#pragma warning restore CA1416

    /// <summary>Write the image bytes to a stream re-encoded as <paramref name="format"/> at the supplied resolution. Resolution is recorded but the underlying writer does not currently scale.</summary>
    public void Save(Stream stream, System.Drawing.Imaging.ImageFormat format, int resolution)
    {
        _ = resolution;
        base.Save(stream, format);
    }

    /// <summary>Write the image bytes to a stream at the supplied resolution. Resolution is recorded but the underlying writer does not currently scale.</summary>
    public void Save(Stream stream, int resolution)
    {
        _ = resolution;
        base.Save(stream);
    }

    /// <summary>Return the image bytes as a seekable stream.</summary>
    public Stream ToStream() => new MemoryStream(GetDecodedData());

    /// <summary>
    /// Attach alternative text to this image via the page's structure tree.
    /// With exactly one structure element referencing the image's marked content,
    /// its /Alt is replaced. With none, the image's Do is wrapped in a new
    /// /Figure marked-content sequence and a matching Figure structure element
    /// (with /Alt) is created under /StructTreeRoot — both created on demand.
    /// Returns false when the association is ambiguous (multiple elements) or
    /// the image is not drawn at the page level.
    /// </summary>
    public bool TrySetAlternativeText(string alternativeText, Page page)
    {
        if (alternativeText is null || page is null) return false;
        var reader = page.Reader;
        var mcids = FindMcidsForImage(page, Name);
        var root = reader.ResolveDict(reader.Catalog.Get("StructTreeRoot"));

        if (mcids.Count > 0 && root is not null)
        {
            var elements = FindStructElementsForMcids(page, root, mcids);
            if (elements.Count > 1) return false;
            if (elements.Count == 1)
            {
                elements[0].Set("Alt", MakeTextString(alternativeText));
                return true;
            }
        }

        // No structure element references this image yet: mark the image's Do with
        // a fresh MCID and grow the structure tree around it.
        var mcid = WrapImageDoInFigureMarkedContent(page, Name);
        if (mcid < 0) return false;

        if (root is null)
        {
            root = new PdfDictionary();
            root.Set("Type", new PdfName("StructTreeRoot"));
            reader.Catalog.Set("StructTreeRoot", root);
            var markInfo = new PdfDictionary();
            markInfo.Set("Marked", PdfBoolean.True);
            reader.Catalog.Set("MarkInfo", markInfo);
        }

        var figure = new PdfDictionary();
        figure.Set("Type", new PdfName("StructElem"));
        figure.Set("S", new PdfName("Figure"));
        figure.Set("Alt", MakeTextString(alternativeText));
        figure.Set("K", new PdfInteger(mcid));
        if (page.SourceObjectNumber > 0)
            figure.Set("Pg", new PdfIndirectRef(page.SourceObjectNumber, 0));
        else if (reader.OwnerDocument is { } ownerDoc)
            // New page — no object number until save; the save pipeline stamps /Pg.
            ownerDoc.PendingStructPgFixups.Add((figure, page));

        var kids = reader.Resolve(root.Get("K"));
        if (kids is PdfArray arr)
            arr.Add(figure);
        else if (kids is not null)
            root.Set("K", new PdfArray(new List<PdfObject> { kids, figure }));
        else
            root.Set("K", new PdfArray(new List<PdfObject> { figure }));
        return true;
    }

    /// <summary>Encode a text string for a PDF string object — UTF-16BE with BOM
    /// when any character is outside Latin-1, plain bytes otherwise.</summary>
    private static PdfString MakeTextString(string text)
    {
        var needsUnicode = text.Any(c => c > 0xFF);
        if (!needsUnicode)
            return new PdfString(System.Text.Encoding.Latin1.GetBytes(text));
        var utf16 = System.Text.Encoding.BigEndianUnicode.GetBytes(text);
        var bytes = new byte[utf16.Length + 2];
        bytes[0] = 0xFE;
        bytes[1] = 0xFF;
        utf16.CopyTo(bytes, 2);
        return new PdfString(bytes);
    }

    /// <summary>
    /// Wrap the first page-level <c>/name Do</c> invocation in a
    /// <c>/Figure &lt;&lt;/MCID n&gt;&gt; BDC … EMC</c> pair, rewriting the containing
    /// content stream in place. Returns the new MCID, or -1 when the image is not
    /// drawn at the page level.
    /// </summary>
    private static int WrapImageDoInFigureMarkedContent(Page page, string imageName)
    {
        var reader = page.Reader;
        var contents = reader.Resolve(page.Dict.Get("Contents"));
        var streams = new List<PdfStream>();
        if (contents is PdfStream single) streams.Add(single);
        else if (contents is PdfArray arr)
            foreach (var item in arr)
                if (reader.ResolveStream(item) is { } s) streams.Add(s);
        if (streams.Count == 0) return -1;

        // New MCID = one past the page's current maximum so /ParentTree-less
        // consumers (our own reader included) can't collide with existing ids.
        var maxMcid = -1;
        var propsDict = reader.ResolveDict(reader.ResolveDict(page.Dict.Get("Resources"))?.Get("Properties"));
        var parser = new Content.ContentStreamParser(reader);
        parser.OnMarkedContentBegin += (_, props) =>
        {
            if (props?.Get("MCID") is PdfInteger m && m.Value > maxMcid) maxMcid = (int)m.Value;
        };
        foreach (var s in streams)
            parser.Parse(reader.DecodeStream(s), properties: propsDict);
        var mcid = maxMcid + 1;

        // Textual wrap of the first "/name Do" occurrence. Resource names in these
        // streams are plain (/Im0 …); the token match requires the exact name
        // followed by whitespace and the Do keyword, so substring names can't hit.
        foreach (var s in streams)
        {
            var text = System.Text.Encoding.Latin1.GetString(reader.DecodeStream(s));
            var idx = FindDoInvocation(text, imageName);
            if (idx < 0) continue;
            var doEnd = text.IndexOf("Do", idx, StringComparison.Ordinal) + 2;
            var rewritten = text[..idx]
                + $"/Figure <</MCID {mcid}>> BDC\n"
                + text[idx..doEnd]
                + "\nEMC"
                + text[doEnd..];
            s.ReplaceData(System.Text.Encoding.Latin1.GetBytes(rewritten));
            s.Dict.Remove("Filter");
            s.Dict.Remove("DecodeParms");
            s.Dict.Set("Length", new PdfInteger(rewritten.Length));
            return mcid;
        }
        return -1;
    }

    /// <summary>Index of the first <c>/name … Do</c> token pair, or -1.</summary>
    private static int FindDoInvocation(string content, string imageName)
    {
        var needle = "/" + imageName;
        var from = 0;
        while (true)
        {
            var idx = content.IndexOf(needle, from, StringComparison.Ordinal);
            if (idx < 0) return -1;
            var after = idx + needle.Length;
            // Exact name token (not a prefix of a longer name) followed by "Do".
            if (after >= content.Length || char.IsWhiteSpace(content[after]))
            {
                var scan = after;
                while (scan < content.Length && char.IsWhiteSpace(content[scan])) scan++;
                if (scan + 1 < content.Length && content[scan] == 'D' && content[scan + 1] == 'o')
                    return idx;
            }
            from = idx + 1;
        }
    }
}
