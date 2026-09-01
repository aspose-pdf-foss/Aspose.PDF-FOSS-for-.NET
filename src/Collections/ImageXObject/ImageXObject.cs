using System.Collections;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

/// <summary>
/// Represents an image XObject found in a PDF page's resources.
/// </summary>
public partial class ImageXObject
{
    private readonly PdfStream _stream;
    private readonly PdfReader _reader;

    internal ImageXObject(string name, PdfStream stream, PdfReader reader)
    {
        Name = name;
        _stream = stream;
        _reader = reader;
    }

    /// <summary>The backing PDF stream. Used by image-extraction callers to
    /// dedupe references to the same image object across pages.</summary>
    internal PdfStream Stream => _stream;

    /// <summary>The reader that owns this image XObject (for resolving indirect
    /// entries such as the image's /Metadata stream).</summary>
    internal PdfReader Reader => _reader;

    /// <summary>The resource name (e.g., "Im0").</summary>
    public string Name { get; internal set; }

    /// <summary>The /XObject resources dictionary this image was found in, when known.
    /// Renaming the image rewrites its key here so the new name survives save.</summary>
    internal PdfDictionary? OwnerXObjects { get; set; }

    /// <summary>Rename the image's resource entry in the owning /XObject dictionary
    /// (no-op when the owner is unknown or the name is unchanged).</summary>
    internal void RenameResource(string newName)
    {
        if (string.IsNullOrEmpty(newName) || newName == Name) { Name = newName ?? Name; return; }
        // Two images in one /XObject dictionary cannot share a key — the second
        // entry would shadow the first — so a duplicate rename is refused.
        if (OwnerXObjects is not null && OwnerXObjects.ContainsKey(newName))
            throw new ArgumentException($"Duplicate image name: {newName}");
        var oldName = Name;
        if (OwnerXObjects is not null && OwnerXObjects.ContainsKey(Name))
        {
            var raw = OwnerXObjects.Get(Name);
            OwnerXObjects.Remove(Name);
            OwnerXObjects.Set(newName, raw!);
        }
        Name = newName;
        // Content-stream references follow the rename so the page keeps painting
        // the same XObject under its new key.
        Owner?.RewriteDoReferences(oldName, newName);
    }

    /// <summary>The collection this image was materialised from. Lets
    /// instance-level edits (<see cref="XImage.Delete()"/>) reach the owning
    /// resources dictionary.</summary>
    internal ImageCollection? Owner { get; set; }

    /// <summary>
    /// Replace the raw image stream data with the given bytes. If the new bytes
    /// look like JPEG (SOI + marker), also update the stream dictionary's
    /// /Filter, /Width, /Height, /BitsPerComponent, /ColorSpace entries to
    /// match — otherwise the PDF reader would still try to apply the original
    /// /Filter (e.g. FlateDecode) to the JPEG bytes and produce garbage.
    /// </summary>
    internal void ReplaceImageData(byte[] newData)
    {
        // Replacing an image can leave the original (often large) image object orphaned;
        // ask the save to reachability-prune so the superseded data isn't carried over.
        _reader.MayHaveOrphansOnSave = true;
        // PNG (89 50 4E 47): a PDF image XObject cannot carry a PNG codestream
        // verbatim, so decode it to raw RGB samples and re-store as a FlateDecode
        // image with a matching dictionary. Without this the PNG bytes would sit
        // under the original /Filter (e.g. FlateDecode) and decode to nothing —
        // the round-tripped image renders blank.
        if (newData.Length >= 8 && newData[0] == 0x89 && newData[1] == 0x50
            && newData[2] == 0x4E && newData[3] == 0x47)
        {
            var (px, pw, ph, hasAlpha) = Aspose.Pdf.Facades.PdfFileMend.DecodePng(newData);
            byte[] rgb = px;
            if (hasAlpha)
            {
                rgb = new byte[pw * ph * 3];
                for (var i = 0; i < pw * ph; i++)
                {
                    rgb[i * 3] = px[i * 4]; rgb[i * 3 + 1] = px[i * 4 + 1]; rgb[i * 3 + 2] = px[i * 4 + 2];
                }
            }
            using var ms = new MemoryStream();
            using (var z = new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
                z.Write(rgb, 0, rgb.Length);
            _stream.ReplaceData(ms.ToArray());
            var pdict = _stream.Dict;
            pdict.Set("Filter", new PdfName("FlateDecode"));
            pdict.Remove("DecodeParms");
            pdict.Set("Width", new PdfInteger(pw));
            pdict.Set("Height", new PdfInteger(ph));
            pdict.Set("BitsPerComponent", new PdfInteger(8));
            pdict.Set("ColorSpace", new PdfName("DeviceRGB"));
            pdict.Remove("SMask");
            pdict.Remove("Decode");
            pdict.Remove("ImageMask");
            return;
        }

        _stream.ReplaceData(newData);
        if (newData.Length >= 4 && newData[0] == 0xFF && newData[1] == 0xD8 &&
            newData[2] == 0xFF)
        {
            var dict = _stream.Dict;
            dict.Set("Filter", new PdfName("DCTDecode"));
            dict.Remove("DecodeParms");
            if (TryParseJpegSize(newData, out var w, out var h, out var nf))
            {
                dict.Set("Width", new PdfInteger(w));
                dict.Set("Height", new PdfInteger(h));
                dict.Set("BitsPerComponent", new PdfInteger(8));
                dict.Set("ColorSpace", new PdfName(nf == 1 ? "DeviceGray" : "DeviceRGB"));
            }
        }
    }

    /// <summary>
    /// Replace with re-encoded JPEG at the given quality (0–100). Decodes the
    /// input via the in-lib JpegDecoder, then re-encodes via JpegEncoderImpl
    /// at the requested quality. Used by Replace(int, Stream, int, bool) so
    /// callers can shrink an image at save time without external dependencies.
    /// </summary>
    internal void ReplaceImageData(byte[] newData, int quality, bool blackAndWhite = false)
    {
        _reader.MayHaveOrphansOnSave = true;
        var (pixels, w, h, comps) = DecodeSamples(newData);

        if (blackAndWhite)
        {
            // Threshold to bitonal and store as CCITT G4 — the compact encoding
            // used for isBlackAndWhite replacements.
            var stride = (w + 7) / 8;
            var packed = new byte[stride * h]; // bit 1 = black
            for (var i = 0; i < w * h; i++)
            {
                int lum;
                if (comps == 1) lum = pixels[i];
                else
                {
                    var src = i * comps;
                    lum = (pixels[src] * 299 + pixels[src + 1] * 587 + pixels[src + 2] * 114) / 1000;
                }
                if (lum < 128)
                    packed[i / w * stride + i % w / 8] |= (byte)(0x80 >> (i % w % 8));
            }
            var g4 = IO.Filters.CcittG4Encoder.Encode(packed, w, h, stride);
            _stream.ReplaceData(g4);
            var bwDict = _stream.Dict;
            bwDict.Set("Filter", new PdfName("CCITTFaxDecode"));
            var parms = new PdfDictionary();
            parms.Set("K", new PdfInteger(-1));
            parms.Set("Columns", new PdfInteger(w));
            parms.Set("Rows", new PdfInteger(h));
            bwDict.Set("DecodeParms", parms);
            bwDict.Set("Width", new PdfInteger(w));
            bwDict.Set("Height", new PdfInteger(h));
            bwDict.Set("BitsPerComponent", new PdfInteger(1));
            bwDict.Set("ColorSpace", new PdfName("DeviceGray"));
            bwDict.Remove("SMask");
            bwDict.Remove("Decode");
            bwDict.Remove("ImageMask");
            return;
        }

        // Encoder always emits RGB (3-component) JPEG; for grayscale input we
        // duplicate the gray channel into R/G/B so the final image is visually
        // identical even after we re-tag /ColorSpace as DeviceRGB.
        var rgba = new byte[w * h * 4];
        for (var i = 0; i < w * h; i++)
        {
            byte r, g, b;
            if (comps == 1)
            {
                r = g = b = pixels[i];
            }
            else
            {
                var src = i * comps;
                r = pixels[src];
                g = pixels[src + 1];
                b = pixels[src + 2];
            }
            var dst = i * 4;
            rgba[dst] = r;
            rgba[dst + 1] = g;
            rgba[dst + 2] = b;
            rgba[dst + 3] = 255;
        }
        var jpeg = IO.JpegEncoderImpl.Encode(rgba, w, h, quality);
        ReplaceImageData(jpeg);
    }

    /// <summary>
    /// Decode an image byte stream to interleaved 8-bit samples, choosing the codec by
    /// the stream's own signature. A caller re-encoding at a quality can hand us any
    /// raster it read back out of the document — <c>XImage.Save</c> writes PNG, and an
    /// image added from a TIFF pin keeps its TIFF bytes — so assuming JPEG here throws
    /// "Not a JPEG file" on the very round-trip (save as PNG, replace at quality) the
    /// API exists for. A multi-frame TIFF contributes its first frame.
    /// </summary>
    private static (byte[] pixels, int width, int height, int comps) DecodeSamples(byte[] data)
    {
        if (data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50
            && data[2] == 0x4E && data[3] == 0x47)
        {
            var (px, pw, ph, hasAlpha) = Aspose.Pdf.Facades.PdfFileMend.DecodePng(data);
            return (px, pw, ph, hasAlpha ? 4 : 3);
        }
        if (IO.TiffDecoder.IsTiff(data)
            && IO.TiffDecoder.DecodeFramesAsPng(data) is { Count: > 0 } frames)
        {
            var (px, pw, ph, hasAlpha) = Aspose.Pdf.Facades.PdfFileMend.DecodePng(frames[0]);
            return (px, pw, ph, hasAlpha ? 4 : 3);
        }
        return IO.Filters.JpegDecoder.Decode(data);
    }

    /// <summary>
    /// Scan a JPEG byte stream for the first SOFn marker (0xFFC0..0xFFCF except
    /// FFC4/FFC8/FFCC) and extract height, width, and number-of-components.
    /// </summary>
    private static bool TryParseJpegSize(byte[] data, out int width, out int height, out int nf)
    {
        width = height = nf = 0;
        var i = 2; // skip SOI
        while (i + 4 < data.Length)
        {
            if (data[i] != 0xFF) return false;
            // Skip fill bytes
            while (i < data.Length && data[i] == 0xFF) i++;
            if (i >= data.Length) return false;
            var marker = data[i++];
            // SOFn (baseline=C0, extended=C1..CF except DHT=C4, JPG=C8, DAC=CC)
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                if (i + 7 >= data.Length) return false;
                // length(2), precision(1), height(2), width(2), Nf(1)
                i += 2; // length
                i += 1; // precision
                height = (data[i] << 8) | data[i + 1]; i += 2;
                width = (data[i] << 8) | data[i + 1]; i += 2;
                nf = data[i];
                return true;
            }
            // Skip segment: read 2-byte length and advance
            if (marker == 0xD9 || marker == 0xDA) return false; // EOI / SOS reached
            if (i + 1 >= data.Length) return false;
            var len = (data[i] << 8) | data[i + 1];
            if (len < 2) return false;
            i += len;
        }
        return false;
    }

    /// <summary>Image width in pixels.</summary>
    public int Width => (int)_stream.Dict.GetInt("Width");

    /// <summary>Image height in pixels.</summary>
    public int Height => (int)_stream.Dict.GetInt("Height");

    /// <summary>Bits per component.</summary>
    public int BitsPerComponent => (int)_stream.Dict.GetInt("BitsPerComponent", 8);

    /// <summary>Color space name.</summary>
    public string? ColorSpace => _stream.Dict.GetName("ColorSpace");

    /// <summary>The filter used (e.g., "DCTDecode" for JPEG, "FlateDecode").</summary>
    public string? Filter
    {
        get
        {
            var f = _stream.Dict.Get("Filter");
            return f switch
            {
                PdfName n => n.Value,
                PdfArray a when a.Count > 0 && a[0] is PdfName n2 => n2.Value,
                _ => null,
            };
        }
    }

    /// <summary>Whether this is a JPEG image (DCTDecode filter).</summary>
    public bool IsJpeg => Filter == "DCTDecode";

    /// <summary>Whether this is a JPEG2000 image (JPXDecode filter).</summary>
    public bool IsJpeg2000 => Filter == "JPXDecode";

    /// <summary>Whether this image has a soft mask (alpha channel).</summary>
    public bool HasSoftMask => _stream.Dict.ContainsKey("SMask");

    /// <summary>Whether this is an image mask (stencil).</summary>
    public bool IsImageMask
    {
        get
        {
            var obj = _stream.Dict.Get("ImageMask");
            return obj is PdfBoolean b && b.Value;
        }
    }

    /// <summary>Number of color components based on the color space.</summary>
    public int ComponentCount
    {
        get
        {
            var cs = ResolveColorSpace();
            return cs switch
            {
                "DeviceGray" or "CalGray" => 1,
                "DeviceRGB" or "CalRGB" => 3,
                "DeviceCMYK" => 4,
                "Indexed" => 1,
                _ => DetermineComponentsFromICC()
            };
        }
    }

    /// <summary>
    /// Get the raw (encoded) image data. For JPEG images (DCTDecode filter),
    /// this returns the JPEG bytes directly.
    /// </summary>
    public byte[] GetRawData() => _stream.RawData;

    /// <summary>
    /// Get the decoded image data (filters removed).
    /// For JPEG, this returns decompressed pixel data.
    /// </summary>
    public byte[] GetDecodedData() => _reader.DecodeStream(_stream);

    /// <summary>
    /// For JPEG images, returns the raw JPEG bytes (ready to write as .jpg file).
    /// For non-JPEG images, returns null.
    /// </summary>
    public byte[]? GetJpegBytes()
    {
        if (!IsJpeg) return null;
        return _stream.RawData;
    }

    /// <summary>
    /// Save the image to a stream as JPEG: <c>XImage.Save(Stream)</c> emits a
    /// JFIF-tagged JPEG for the base image.
    /// Source JPEGs are streamed out verbatim so their JFIF density survives;
    /// other formats are re-encoded to JPEG from the base colour plane. A soft
    /// mask is a separate image XObject and is not composited
    /// here. Because JFIF density is an integer dots-per-inch, a resolution
    /// set on the round-tripped bitmap round-trips exactly — a PNG's
    /// pixels-per-metre pHYs cannot represent e.g. 200 dpi without rounding.
    /// </summary>
    public void Save(Stream output)
    {
        if (IsJpeg)
        {
            output.Write(_stream.RawData);
            return;
        }
        if (GetPixelSource() is { } getter)
        {
            var (g2, w2, h2) = CapHugeImage(getter, Width, Height);
            output.Write(IO.JpegEncoderImpl.Encode(g2, w2, h2, 95));
            return;
        }
        output.Write(ToPng());
    }

    /// <summary>
    /// Save the image to a stream in the specified format. JPEG output uses the
    /// embedded JPEG bytes when source is JPEG, otherwise re-encodes pixels via
    /// the in-lib baseline JPEG encoder. All other formats fall back to PNG.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416",
        Justification = "ImageFormat.Guid is read-only and works on all platforms.")]
    public void Save(Stream output, System.Drawing.Imaging.ImageFormat format)
    {
        var fmtGuid = format?.Guid ?? System.Drawing.Imaging.ImageFormat.Png.Guid;
        if (fmtGuid == System.Drawing.Imaging.ImageFormat.Jpeg.Guid)
        {
            if (IsJpeg)
            {
                output.Write(_stream.RawData);
                return;
            }
            var getter = GetPixelSource();
            if (getter is not null)
            {
                var (g2, w2, h2) = CapHugeImage(getter, Width, Height);
                var jpg = IO.JpegEncoderImpl.Encode(g2, w2, h2, 90);
                output.Write(jpg);
                return;
            }
        }
        var png = ToPng();
        output.Write(png);
    }

    /// <summary>
    /// Save the decoded image rotated <paramref name="clockwiseQuarterTurns"/> × 90°
    /// clockwise. A JPEG source re-encodes as JPEG; every other format is written as
    /// PNG. Used when an image is drawn on a rotated page so the extracted file keeps
    /// the orientation in which the image appears on the displayed page (the
    /// rotated image extracts upright).
    /// </summary>
    internal void SaveRotated(Stream output, int clockwiseQuarterTurns)
    {
        var turns = ((clockwiseQuarterTurns % 4) + 4) % 4;
        var src = turns == 0 ? null : GetPixelSource();
        if (src is null)
        {
            // No rotation requested, or a colour space the pixel decoder can't read —
            // fall back to the verbatim save rather than producing a blank image.
            Save(output);
            return;
        }

        int w = Width, h = Height;
        int ow = turns == 2 ? w : h;
        int oh = turns == 2 ? h : w;

        IO.PixelGetter rotated = turns switch
        {
            // 90° CW: output(x,y) = source(y, h-1-x)
            1 => (int x, int y, out byte r, out byte g, out byte b) => src(y, h - 1 - x, out r, out g, out b),
            // 180°: output(x,y) = source(w-1-x, h-1-y)
            2 => (int x, int y, out byte r, out byte g, out byte b) => src(w - 1 - x, h - 1 - y, out r, out g, out b),
            // 270° CW: output(x,y) = source(w-1-y, x)
            _ => (int x, int y, out byte r, out byte g, out byte b) => src(w - 1 - y, x, out r, out g, out b),
        };

        if (IsJpeg)
        {
            var jpg = IO.JpegEncoderImpl.Encode(rotated, ow, oh, 90);
            output.Write(jpg);
            return;
        }

        // Materialise the rotated RGB samples and PNG-encode them.
        var rgb = new byte[(long)ow * oh * 3];
        long p = 0;
        for (var y = 0; y < oh; y++)
            for (var x = 0; x < ow; x++)
            {
                rotated(x, y, out var r, out var g, out var b);
                rgb[p++] = r; rgb[p++] = g; rgb[p++] = b;
            }
        output.Write(IO.PngEncoder.Encode(rgb, ow, oh, 2, 8));
    }

    // XImage.Save effectively renders the image at ~150 DPI, which downscales
    // pathologically over-resolution images (e.g. a 35000x35000 fax scan placed on a
    // 8400pt page) to a manageable size: cap a huge image's longest side
    // and box-average the source so sparse scan lines survive the reduction (a plain
    // subsample would drop them and leave the output blank). Normal-sized images
    // (below the threshold) are returned untouched.
    private const int HugeImageThreshold = 10000;
    private const int HugeImageMaxDim = 5250;

    private static (IO.PixelGetter getter, int width, int height) CapHugeImage(IO.PixelGetter src, int w, int h)
    {
        var maxDim = Math.Max(w, h);
        if (maxDim <= HugeImageThreshold) return (src, w, h);
        var scale = (double)HugeImageMaxDim / maxDim;
        var tw = Math.Max(1, (int)(w * scale));
        var th = Math.Max(1, (int)(h * scale));
        IO.PixelGetter scaled = (int x, int y, out byte r, out byte g, out byte b) =>
        {
            var sx0 = (int)((long)x * w / tw);
            var sx1 = (int)((long)(x + 1) * w / tw);
            var sy0 = (int)((long)y * h / th);
            var sy1 = (int)((long)(y + 1) * h / th);
            if (sx1 <= sx0) sx1 = sx0 + 1;
            if (sy1 <= sy0) sy1 = sy0 + 1;
            long ar = 0, ag = 0, ab = 0, n = 0;
            for (var yy = sy0; yy < sy1; yy++)
                for (var xx = sx0; xx < sx1; xx++)
                {
                    src(xx, yy, out var cr, out var cg, out var cb);
                    ar += cr; ag += cg; ab += cb; n++;
                }
            r = (byte)(ar / n); g = (byte)(ag / n); b = (byte)(ab / n);
        };
        return (scaled, tw, th);
    }

    /// <summary>
    /// Save the image to a file. Uses the file extension to determine format.
    /// .jpg/.jpeg saves JPEG (if available), otherwise PNG.
    /// All other extensions save as PNG.
    /// </summary>
    public void Save(string path)
    {
        using var fs = File.Create(path);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".jpg" or ".jpeg" && IsJpeg)
        {
            fs.Write(_stream.RawData);
        }
        else
        {
            var png = ToPng();
            fs.Write(png);
        }
    }

    /// <summary>Interleave an alpha plane into an RGB (→RGBA) or gray (→gray+alpha)
    /// pixel buffer, returning the widened buffer and its PNG colour type.</summary>
    private static (byte[] pixels, int colorType) InterleaveAlpha(
        byte[] color, byte[] alpha, int w, int h, int colorComponents)
    {
        var outComponents = colorComponents + 1;
        var outColorType = colorComponents == 3 ? 6 : 4;
        var result = new byte[w * h * outComponents];
        for (var i = 0; i < w * h; i++)
        {
            var src = i * colorComponents;
            var dst = i * outComponents;
            for (var c = 0; c < colorComponents; c++)
                result[dst + c] = src + c < color.Length ? color[src + c] : (byte)0;
            result[dst + colorComponents] = i < alpha.Length ? alpha[i] : (byte)255;
        }
        return (result, outColorType);
    }

}
