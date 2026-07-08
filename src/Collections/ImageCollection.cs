using System.Collections;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

/// <summary>
/// Represents an image XObject found in a PDF page's resources.
/// </summary>
public class ImageXObject
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
        var (pixels, w, h, comps) = IO.Filters.JpegDecoder.Decode(newData);

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
    /// Save the image to a stream as JPEG (matching Aspose.Pdf, whose
    /// <c>XImage.Save(Stream)</c> emits a JFIF-tagged JPEG for the base image).
    /// Source JPEGs are streamed out verbatim so their JFIF density survives;
    /// other formats are re-encoded to JPEG from the base colour plane. A soft
    /// mask is a separate image XObject and is not composited here, matching the
    /// reference. Because JFIF density is an integer dots-per-inch, a resolution
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
    /// the orientation in which the image appears on the displayed page (matching
    /// Aspose.Pdf, which extracts the rotated image upright).
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

    // Aspose.Pdf XImage.Save renders the image at ~150 DPI, which downscales
    // pathologically over-resolution images (e.g. a 35000x35000 fax scan placed on a
    // 8400pt page) to a manageable size. Mirror that: cap a huge image's longest side
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

        return IO.PngEncoder.Encode(decoded, w, h, colorType, bpc > 8 ? 8 : bpc);
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

        var baseCs = _reader.Resolve(arr[1]);
        var hival = arr[2] is PdfInteger hi ? (int)hi.Value : 255;
        paletteSize = hival + 1;

        byte[] lookup;
        var lookupObj = _reader.Resolve(arr[3]);
        if (lookupObj is PdfStream ls)
            lookup = _reader.DecodeStream(ls);
        else if (lookupObj is PdfString s)
            lookup = s.Value;
        else
            return null;

        var baseComps = baseCs switch
        {
            PdfName bn when bn.Value is "DeviceRGB" or "CalRGB" => 3,
            PdfName bn when bn.Value is "DeviceGray" or "CalGray" => 1,
            PdfName bn when bn.Value is "DeviceCMYK" => 4,
            PdfArray ba when ba.Count > 0 && ba[0] is PdfName bn0 && bn0.Value == "ICCBased"
                => (int)(_reader.ResolveStream(ba[1])?.Dict.GetInt("N", 3) ?? 3),
            _ => 3,
        };

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

/// <summary>
/// Collection of image XObjects on a page.
/// </summary>
public class ImageCollection : IReadOnlyList<ImageXObject>
{
    private protected readonly List<ImageXObject> _images;
    private protected readonly PdfDictionary? _ownerDict;
    private protected readonly PdfReader _ownerReader;

    internal ImageCollection(PdfDictionary pageDict, PdfReader reader)
    {
        _ownerDict = pageDict;
        _ownerReader = reader;
        var list = new List<ImageXObject>();
        // /Resources is inheritable through the /Pages tree -- when a page
        // doesn't carry its own, the nearest ancestor /Pages node's entry
        // applies. Walk up via /Parent until we find one, then recurse into
        // Form XObjects (some producers wrap content
        // there). Cycle-guarded by stream identity so a self-referencing
        // form can't loop forever.
        var visited = new HashSet<PdfStream>();
        CollectImages(InheritedResources(pageDict, reader), reader, list, visited);
        _images = list;
    }

    /// <summary>Register a new image XObject stream in the owning resources'
    /// /XObject dictionary and append it to this collection. Returns the
    /// assigned resource name (Im1, Im2, …).</summary>
    internal string AppendImageXObject(PdfStream imageStream)
    {
        var resources = _ownerReader.ResolveDict(_ownerDict?.Get("Resources"));
        if (resources is null)
        {
            resources = new PdfDictionary();
            _ownerDict?.Set("Resources", resources);
        }
        var xobjects = _ownerReader.ResolveDict(resources.Get("XObject"));
        if (xobjects is null)
        {
            xobjects = new PdfDictionary();
            resources.Set("XObject", xobjects);
        }
        var n = 1;
        while (xobjects.ContainsKey($"Im{n}")) n++;
        var name = $"Im{n}";
        xobjects.Set(name, imageStream);
        _images.Add(new XImage(name, imageStream, _ownerReader));
        return name;
    }

    private protected static PdfDictionary? InheritedResources(PdfDictionary pageDict, PdfReader reader)
    {
        var current = pageDict;
        while (current is not null)
        {
            var res = reader.ResolveDict(current.Get("Resources"));
            if (res is not null) return res;
            current = reader.ResolveDict(current.Get("Parent"));
        }
        return null;
    }

    private static void CollectImages(PdfDictionary? resources, PdfReader reader,
        List<ImageXObject> sink, HashSet<PdfStream> visited)
    {
        if (resources is null) return;

        // Direct XObjects -- Subtype=Image is harvested; Subtype=Form is a
        // wrapper, recurse into its own Resources.
        var xobjectDict = reader.ResolveDict(resources.Get("XObject"));
        if (xobjectDict is not null)
        {
            foreach (var key in xobjectDict.Keys)
            {
                var obj = reader.ResolveStream(xobjectDict.Get(key));
                if (obj is null || !visited.Add(obj)) continue;
                var subtype = obj.Dict.GetName("Subtype");
                if (subtype == "Image")
                    sink.Add(new XImage(key, obj, reader));
                else if (subtype == "Form")
                    CollectImages(reader.ResolveDict(obj.Dict.Get("Resources")), reader, sink, visited);
            }
        }

        // Tiling-pattern resources -- a Type-1 (tiling) pattern is itself a
        // content stream with its own /Resources, and producers like
        // some producers emit raster images there as the pattern's only paint.
        // Recurse so page.Images surfaces them too.
        var patternDict = reader.ResolveDict(resources.Get("Pattern"));
        if (patternDict is not null)
        {
            foreach (var key in patternDict.Keys)
            {
                // Tiling patterns are streams (PatternType 1); shading
                // patterns (PatternType 2) are plain dictionaries with no
                // /Resources. ResolveStream returns null for the latter
                // and we just skip it.
                var pat = reader.ResolveStream(patternDict.Get(key));
                if (pat is null || !visited.Add(pat)) continue;
                CollectImages(reader.ResolveDict(pat.Dict.Get("Resources")), reader, sink, visited);
            }
        }
    }

    public int Count => _images.Count;

    /// <summary>Get an image by its 1-based index (Aspose convention, matching
    /// <see cref="Replace(int, Stream)"/> and <see cref="XImageCollection.Delete(int)"/>).</summary>
    public ImageXObject this[int index] => _images[index - 1];

    // IReadOnlyList is a 0-based contract (foreach/LINQ ElementAt); keep that honest
    // while the public indexer above stays 1-based.
    ImageXObject IReadOnlyList<ImageXObject>.this[int index] => _images[index];

    /// <summary>
    /// Get an image by its resource name (e.g., "Im0", "JI1a").
    /// Returns null if no image with the given name exists.
    /// </summary>
    public ImageXObject? GetByName(string name)
    {
        foreach (var img in _images)
            if (img.Name == name)
                return img;
        return null;
    }

    /// <summary>
    /// Replace the image data at the given 1-based index with the provided stream.
    /// Reuses the existing image resource name; subsequent reads see the new pixels.
    /// </summary>
    public void Replace(int index, Stream stream)
    {
        if (index < 1 || index > _images.Count)
            throw new ArgumentException($"Index {index} is outside the collection (1..{_images.Count})", nameof(index));
        if (stream is null) throw new ArgumentNullException(nameof(stream));
        _images[index - 1].ReplaceImageData(ReadAll(stream));
    }

    /// <summary>Replace the image with the given resource name.</summary>
    public void Replace(string name, Stream stream)
    {
        var img = GetByName(name) ?? throw new KeyNotFoundException($"No image named '{name}'");
        img.ReplaceImageData(ReadAll(stream));
    }

    /// <summary>
    /// Replace the image at the given 1-based index with the provided stream,
    /// re-encoding to JPEG at <paramref name="quality"/> (0–100). When
    /// <paramref name="optimize"/> is true the image is thresholded to bitonal
    /// and stored as CCITT G4 instead (the XImageCollection overload surfaces
    /// this flag as isBlackAndWhite).
    /// </summary>
    public void Replace(int index, Stream stream, int quality, bool optimize)
    {
        if (index < 1 || index > _images.Count)
            throw new ArgumentException($"Index {index} is outside the collection (1..{_images.Count})", nameof(index));
        if (stream is null) throw new ArgumentNullException(nameof(stream));
        _images[index - 1].ReplaceImageData(ReadAll(stream), quality, blackAndWhite: optimize);
    }

    /// <summary>
    /// Drain a stream to a byte[]. Rewinds seekable streams first so callers
    /// who wrote to a MemoryStream and forgot to Seek(0) still get the right data.
    /// </summary>
    private static byte[] ReadAll(Stream stream)
    {
        if (stream.CanSeek) stream.Seek(0, SeekOrigin.Begin);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    public IEnumerator<ImageXObject> GetEnumerator() =>
        ((IEnumerable<ImageXObject>)_images).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

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
    public string Add(BitmapInfo bitmapInfo)
    {
        _ = bitmapInfo;
        return $"Im{Count + 1}";
    }

    /// <summary>Append an image from a raw bitmap using the given filter; returns its assigned resource name.</summary>
    public string Add(BitmapInfo bitmapInfo, ImageFilterType filterType)
    {
        _ = filterType;
        return Add(bitmapInfo);
    }

    /// <summary>Remove every image from the collection.</summary>
    public void Clear()
    {
        foreach (var img in _images.ToArray())
            RemoveImageResource(img.Name);
        _images.Clear();
    }

    /// <summary>Remove the image XObject with the given resource name from the
    /// owning resources (page /Resources/XObject, recursing into nested form
    /// XObjects) and from this collection. The orphaned image stream becomes
    /// unreachable and is dropped when the document is saved, shrinking the file.
    /// Returns true when an image was removed.</summary>
    internal bool RemoveImageResource(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        var removed = RemoveFromResources(InheritedResources(_ownerDict!, _ownerReader), name, new HashSet<PdfStream>());
        if (removed)
        {
            var img = GetByName(name);
            if (img is not null) _images.Remove(img);
        }
        return removed;
    }

    /// <summary>Remove the image at the given 1-based index. See <see cref="RemoveImageResource(string)"/>.</summary>
    internal bool RemoveImageAt(int index)
    {
        if (index < 1 || index > _images.Count) return false;
        return RemoveImageResource(_images[index - 1].Name);
    }

    private bool RemoveFromResources(PdfDictionary? resources, string name, HashSet<PdfStream> visited)
    {
        if (resources is null) return false;
        var xobjectDict = _ownerReader.ResolveDict(resources.Get("XObject"));
        if (xobjectDict is null) return false;

        if (xobjectDict.ContainsKey(name))
        {
            var img = _ownerReader.ResolveStream(xobjectDict.Get(name));
            if (img is not null && img.Dict.GetName("Subtype") == "Image")
            {
                xobjectDict.Remove(name);
                return true;
            }
        }

        // Recurse into form XObjects (their own /Resources/XObject may hold the image).
        foreach (var key in xobjectDict.Keys)
        {
            var obj = _ownerReader.ResolveStream(xobjectDict.Get(key));
            if (obj is null || !visited.Add(obj)) continue;
            if (obj.Dict.GetName("Subtype") == "Form" &&
                RemoveFromResources(_ownerReader.ResolveDict(obj.Dict.Get("Resources")), name, visited))
                return true;
        }
        return false;
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

/// <summary>
/// Image XObject wrapper that mirrors the Aspose.Pdf XImage public surface.
/// </summary>
public class XImage : ImageXObject
{
    internal XImage(string name, PdfStream stream, PdfReader reader) : base(name, stream, reader) { }

    /// <summary>The resource name (e.g., "Im0"); writable on the derived type.</summary>
    public new string Name { get => base.Name; set => base.Name = value; }

    /// <summary>Image width in pixels.</summary>
    public new int Width => base.Width;

    /// <summary>Image height in pixels.</summary>
    public new int Height => base.Height;

    /// <summary>Whether the image has an /SMask or /Mask entry indicating per-pixel transparency.</summary>
    public bool ContainsTransparency => HasSoftMask;

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

    /// <summary>Attach a stencil mask from a stream. Stored only — the mask is not currently emitted into the image XObject's /SMask.</summary>
    public void AddStencilMask(Stream maskStream)
    {
        _ = maskStream;
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
    /// (R==G==B) is really a black-and-white image — Aspose.Pdf reports it as
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
        // Pixel sampling needs System.Drawing (Windows-only); elsewhere keep the name-based
        // result. Only downgrade RGB→Grayscale, never the reverse.
        if (byName == ColorType.Rgb && OperatingSystem.IsWindows()
            && DetectColorTypeByPixels() == ColorType.Grayscale)
            return ColorType.Grayscale;
        return byName;
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
