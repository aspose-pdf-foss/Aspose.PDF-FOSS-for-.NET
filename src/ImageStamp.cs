using System.IO.Compression;
using Aspose.Pdf.Core;

namespace Aspose.Pdf;

/// <summary>
/// Adds an image to a PDF page. Supports raw pixel data (FlateDecode) and JPEG pass-through.
/// </summary>
public class ImageStamp : BaseParagraph
{
    private byte[] _imageData;
    private int _width;
    private int _height;
    private string _colorSpace;
    private string _filter;
    private int _bitsPerComponent;
    private byte[]? _originalImageBytes;
    // FlateDecode'd DeviceGray alpha channel for a transparent source (GIF/PNG);
    // emitted as the image's /SMask so transparent pixels show the page behind.
    private byte[]? _smaskData;
    // Optional /DecodeParms for filters that need them (e.g. CCITTFaxDecode K/Columns/Rows).
    private PdfDictionary? _decodeParms;

    private ImageStamp(byte[] imageData, int width, int height,
        string colorSpace, string filter, int bitsPerComponent)
    {
        _imageData = imageData;
        _width = width;
        _height = height;
        _colorSpace = colorSpace;
        _filter = filter;
        _bitsPerComponent = bitsPerComponent;
    }

    /// <summary>Construct from an image stream (auto-detects JPEG vs PNG by header).</summary>
    public ImageStamp(System.IO.Stream image)
        : this(ReadAll(image ?? throw new ArgumentNullException(nameof(image))), useOriginal: true)
    {
    }

    /// <summary>Construct from an image file path (auto-detects JPEG vs PNG by header).</summary>
    public ImageStamp(string fileName)
        : this(System.IO.File.ReadAllBytes(fileName ?? throw new ArgumentNullException(nameof(fileName))), useOriginal: true)
    {
    }

    private ImageStamp(byte[] sourceBytes, bool useOriginal)
        : this(Array.Empty<byte>(), 0, 0, "DeviceRGB", "FlateDecode", 8)
    {
        if (useOriginal) _originalImageBytes = sourceBytes;
        // Detection cascade: JPEG → PNG → fall back to GDI+ for GIF/TIFF/EMF/
        // WMF/ICO and any other format System.Drawing's codec set recognises.
        // Without the GDI+ branch a 'new ImageStamp(\"foo.gif\")' raised
        // 'Invalid PNG data' because the PNG decoder was the
        // only non-JPEG path.
        ImageStamp? seeded = null;
        if (IsJpeg(sourceBytes))
            seeded = FromJpeg(sourceBytes);
        else if (IsPng(sourceBytes))
            seeded = FromPngData(sourceBytes);
        else if (OperatingSystem.IsWindows())
            seeded = TryFromGdiPlusDecoder(sourceBytes);
        // Final fallback: try PNG anyway so the caller still gets a
        // 'Invalid PNG data' (rather than a NullReferenceException) when the
        // bytes really are corrupt.
        seeded ??= FromPngData(sourceBytes);
        _imageData = seeded._imageData;
        _width = seeded._width;
        _height = seeded._height;
        _colorSpace = seeded._colorSpace;
        _filter = seeded._filter;
        _bitsPerComponent = seeded._bitsPerComponent;
        _smaskData = seeded._smaskData;
        DisplayWidth = seeded.DisplayWidth;
        DisplayHeight = seeded.DisplayHeight;
    }

    private static bool IsPng(byte[] data) =>
        data.Length >= 4 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47;

    /// <summary>Position X in points from bottom-left.</summary>
    public double X { get; set; }

    /// <summary>Position Y in points from bottom-left.</summary>
    public double Y { get; set; }

    /// <summary>Display width in points. Defaults to image pixel width.</summary>
    public double DisplayWidth { get; set; }

    /// <summary>Display height in points. Defaults to image pixel height.</summary>
    public double DisplayHeight { get; set; }

    /// <summary>Aspose.PDF for .NET-shape alias for <see cref="DisplayWidth"/>.</summary>
    public double Width { get => DisplayWidth; set => DisplayWidth = value; }

    /// <summary>Aspose.PDF for .NET-shape alias for <see cref="DisplayHeight"/>.</summary>
    public double Height { get => DisplayHeight; set => DisplayHeight = value; }

    /// <summary>Horizontal offset from the stamp's anchor point. Stored only.</summary>
    public double XIndent { get; set; }

    /// <summary>Vertical offset from the stamp's anchor point. Stored only.</summary>
    public double YIndent { get; set; }

    /// <summary>JPEG quality (1..100). Stored only — pass-through embedding does not re-encode.</summary>
    public int Quality { get; set; } = 100;

    /// <summary>Tagged-PDF alternate text for accessibility. Stored only.</summary>
    public string? AlternativeText { get; set; }

    /// <summary>Whether the stamp is drawn behind the page content (true) or
    /// on top of it (false). Stored only.</summary>
    public bool Background { get; set; }

    /// <summary>Opacity, 0.0 (transparent) … 1.0 (opaque). Stored only.</summary>
    public double Opacity { get; set; } = 1.0;

    /// <summary>PDF blend mode name (e.g. "Multiply", "Overlay") applied when
    /// compositing the image onto the page. Null or "Normal" draws opaquely.</summary>
    public string? BlendMode { get; set; }

    /// <summary>Uniform zoom percentage applied to the image. Stored only.</summary>
    public double Zoom { get; set; } = 100;

    /// <summary>Horizontal zoom percentage. Stored only.</summary>
    public double ZoomX { get; set; } = 100;

    /// <summary>Vertical zoom percentage. Stored only.</summary>
    public double ZoomY { get; set; } = 100;

    /// <summary>Rotation enum (Aspose.PDF for .NET Rotation; 0/90/180/270 in degrees).
    /// Stored only.</summary>
    public Rotation Rotate { get; set; } = Rotation.None;

    /// <summary>Arbitrary rotation angle in degrees. Stored only.</summary>
    public double RotateAngle { get; set; }

    /// <summary>Set the stamp's PDF /StampId entry. Stored only.</summary>
    public void setStampId(int id) { StampId = id; }

    /// <summary>The /StampId entry written into the stamp's resource entry.
    /// Stored only.</summary>
    public int StampId { get; set; }

    /// <summary>Optional pre-computed bounding rectangle (page space). When set, the
    /// stamp emits a <c>%StampRect</c> content-stream comment so PdfContentEditor.GetStamps
    /// reports the stamp's exact geometry on reload.</summary>
    internal Aspose.Pdf.Rectangle? MetaRect { get; set; }

    /// <summary>When set, always emit the <c>%StampId</c> marker comment (even for
    /// id 0) so the stamp is discoverable by PdfContentEditor.GetStamps. Used by the
    /// PdfFileStamp facade for unnamed image stamps.</summary>
    internal bool ForceStampIdComment { get; set; }

    /// <summary>Read-only view of the original source bytes (or the decoded image bytes when
    /// constructed via FromRgb / FromGrayscale).</summary>
    public System.IO.Stream Image =>
        new System.IO.MemoryStream(_originalImageBytes ?? _imageData, writable: false);

    /// <summary>Aspose.PDF for .NET-shape alias for <see cref="ApplyTo"/>.</summary>
    public void Put(Page page) => ApplyTo(page);

    /// <summary>
    /// Create an image stamp from raw RGB pixel data.
    /// Pixels are row-major, top-to-bottom, 3 bytes per pixel (R, G, B).
    /// </summary>
    public static ImageStamp FromRgb(byte[] pixelData, int width, int height)
    {
        if (pixelData.Length != width * height * 3)
            throw new ArgumentException("Pixel data length must equal width × height × 3");

        var compressed = CompressFlate(pixelData);
        var stamp = new ImageStamp(compressed, width, height, "DeviceRGB", "FlateDecode", 8)
        {
            DisplayWidth = width,
            DisplayHeight = height
        };
        return stamp;
    }

    /// <summary>Attach an 8-bit DeviceGray soft mask (the source's alpha channel)
    /// so transparent pixels composite against the page behind instead of painting
    /// an opaque box. <paramref name="alpha"/> is row-major, one byte per pixel,
    /// matching the image dimensions.</summary>
    internal void SetAlphaMask(byte[] alpha)
    {
        if (alpha.Length != _width * _height)
            throw new ArgumentException("Alpha length must equal width × height");
        _smaskData = CompressFlate(alpha);
    }

    /// <summary>
    /// Create an image stamp from raw grayscale pixel data.
    /// </summary>
    public static ImageStamp FromGrayscale(byte[] pixelData, int width, int height)
    {
        if (pixelData.Length != width * height)
            throw new ArgumentException("Pixel data length must equal width × height");

        var compressed = CompressFlate(pixelData);
        var stamp = new ImageStamp(compressed, width, height, "DeviceGray", "FlateDecode", 8)
        {
            DisplayWidth = width,
            DisplayHeight = height
        };
        return stamp;
    }

    /// <summary>
    /// Create a 1-bit black/white image stamp from arbitrary image bytes (the
    /// <see cref="Image.IsBlackWhite"/> path). The source is decoded, each pixel is
    /// thresholded to black or white by luminance, and the result is packed at one
    /// bit per pixel (DeviceGray, FlateDecode) — far smaller than an 8-bit embed for
    /// scanned/bilevel documents. Returns null when the platform decoder is
    /// unavailable so the caller can fall back to a normal embed.
    /// </summary>
    internal static ImageStamp? FromBlackWhite(byte[] imageData)
    {
        if (imageData is null || imageData.Length < 4) return null;
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
#pragma warning disable CA1416
            using var ms = new MemoryStream(imageData);
            using var src = System.Drawing.Image.FromStream(ms);
            var w = src.Width;
            var h = src.Height;
            if (w <= 0 || h <= 0) return null;
            using var rgb = new System.Drawing.Bitmap(w, h,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(rgb))
            {
                // Flatten any transparency onto white so transparent areas stay white.
                g.Clear(System.Drawing.Color.White);
                g.DrawImage(src, 0, 0, w, h);
            }
            var rect = new System.Drawing.Rectangle(0, 0, w, h);
            var bits = rgb.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                var stride = bits.Stride;
                var rowBytes = (w + 7) / 8;          // packed 1-bpp row, byte-aligned
                var packed = new byte[rowBytes * h];
                var srcPtr = bits.Scan0;
                for (var y = 0; y < h; y++)
                {
                    var srcOffset = y * stride;
                    var dstRow = y * rowBytes;
                    for (var x = 0; x < w; x++)
                    {
                        var b = System.Runtime.InteropServices.Marshal.ReadByte(srcPtr, srcOffset + x * 4 + 0);
                        var g2 = System.Runtime.InteropServices.Marshal.ReadByte(srcPtr, srcOffset + x * 4 + 1);
                        var r = System.Runtime.InteropServices.Marshal.ReadByte(srcPtr, srcOffset + x * 4 + 2);
                        // ITU-R BT.601 luma; >=128 is white (bit set), else black (bit clear).
                        var lum = (r * 299 + g2 * 587 + b * 114) / 1000;
                        if (lum >= 128)
                            packed[dstRow + (x >> 3)] |= (byte)(0x80 >> (x & 7));
                    }
                }
                var compressed = CompressFlate(packed);
                return new ImageStamp(compressed, w, h, "DeviceGray", "FlateDecode", 1)
                {
                    DisplayWidth = w,
                    DisplayHeight = h
                };
            }
            finally { rgb.UnlockBits(bits); }
#pragma warning restore CA1416
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Create a 1-bit image stamp that embeds pre-encoded CCITT Group 4 (T.6) data
    /// directly as a CCITTFaxDecode XObject — used to pass a bilevel TIFF's existing
    /// G4 strip through to the PDF without re-encoding (the <see cref="Image.IsBlackWhite"/>
    /// fast path for fax/scan documents).
    /// </summary>
    internal static ImageStamp FromCcittG4(byte[] g4Data, int width, int height, bool blackIs1)
    {
        var parms = new PdfDictionary();
        parms.Set("K", new PdfInteger(-1));          // pure two-dimensional (G4 / T.6)
        parms.Set("Columns", new PdfInteger(width));
        parms.Set("Rows", new PdfInteger(height));
        if (blackIs1) parms.Set("BlackIs1", PdfBoolean.True);
        var stamp = new ImageStamp(g4Data, width, height, "DeviceGray", "CCITTFaxDecode", 1)
        {
            DisplayWidth = width,
            DisplayHeight = height,
            _decodeParms = parms
        };
        return stamp;
    }

    /// <summary>
    /// Create an image stamp from JPEG bytes. The JPEG data is embedded directly
    /// without re-encoding.
    /// </summary>
    public static ImageStamp FromJpeg(byte[] jpegData, int width, int height)
    {
        var stamp = new ImageStamp(jpegData, width, height, "DeviceRGB", "DCTDecode", 8)
        {
            DisplayWidth = width,
            DisplayHeight = height
        };
        return stamp;
    }

    /// <summary>
    /// Create an image stamp from JPEG bytes, auto-detecting dimensions from the JPEG header.
    /// </summary>
    public static ImageStamp FromJpeg(byte[] jpegData)
    {
        var (w, h) = ParseJpegDimensions(jpegData);
        if (w == 0 || h == 0)
            throw new ArgumentException("Could not determine JPEG dimensions from header.");
        return FromJpeg(jpegData, w, h);
    }

    /// <summary>
    /// Create an image stamp from a JPEG file stream. Reads JPEG header for dimensions.
    /// </summary>
    public static ImageStamp FromJpegStream(Stream jpegStream)
    {
        using var ms = new MemoryStream();
        jpegStream.CopyTo(ms);
        var data = ms.ToArray();

        var (w, h) = ParseJpegDimensions(data);
        return FromJpeg(data, w, h);
    }

    /// <summary>Last-resort image decoder: delegate to System.Drawing so any
    /// format the GDI+ codec set understands (GIF, TIFF, EMF, WMF, ICO, …)
    /// is converted to a raw-RGB ImageStamp. The bitmap is upgraded to 24bpp
    /// RGB if necessary (RGBA inputs drop the alpha channel for now — the
    /// stamp emitter writes /ColorSpace DeviceRGB without SMask).
    ///
    /// Returns null when no codec recognises the bytes — callers fall back
    /// to their format-specific error path.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal static ImageStamp? TryFromGdiPlusDecoder(byte[] imageData)
    {
        if (imageData is null || imageData.Length < 4) return null;
        try
        {
            using var ms = new MemoryStream(imageData);
            using var src = System.Drawing.Image.FromStream(ms);
            var w = src.Width;
            var h = src.Height;
            // Decode into 32bpp ARGB so a transparent GIF/PNG keeps its alpha.
            using var argb = new System.Drawing.Bitmap(w, h,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(argb))
            {
                g.Clear(System.Drawing.Color.Transparent);
                g.DrawImage(src, 0, 0, w, h);
            }
            var rect = new System.Drawing.Rectangle(0, 0, w, h);
            var bits = argb.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                var stride = bits.Stride;
                var rgbBytes = new byte[w * h * 3];
                var alpha = new byte[w * h];
                var hasAlpha = false;
                var srcPtr = bits.Scan0;
                for (var y = 0; y < h; y++)
                {
                    var srcOffset = y * stride;
                    // GDI+ Format32bppArgb is BGRA in memory; swap to RGB + alpha.
                    for (var x = 0; x < w; x++)
                    {
                        var b = System.Runtime.InteropServices.Marshal.ReadByte(srcPtr, srcOffset + x * 4 + 0);
                        var g2 = System.Runtime.InteropServices.Marshal.ReadByte(srcPtr, srcOffset + x * 4 + 1);
                        var r = System.Runtime.InteropServices.Marshal.ReadByte(srcPtr, srcOffset + x * 4 + 2);
                        var a = System.Runtime.InteropServices.Marshal.ReadByte(srcPtr, srcOffset + x * 4 + 3);
                        var pi = y * w + x;
                        rgbBytes[pi * 3 + 0] = r;
                        rgbBytes[pi * 3 + 1] = g2;
                        rgbBytes[pi * 3 + 2] = b;
                        alpha[pi] = a;
                        if (a != 255) hasAlpha = true;
                    }
                }
                var stamp = FromRgb(rgbBytes, w, h);
                if (hasAlpha) stamp._smaskData = CompressFlate(alpha);
                return stamp;
            }
            finally { argb.UnlockBits(bits); }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Create an image stamp from PNG file data. Decodes PNG to raw RGB pixels.
    /// </summary>
    public static ImageStamp FromPngData(byte[] pngData)
    {
        // Parse PNG IHDR for dimensions and color type
        if (pngData.Length < 24 || pngData[0] != 0x89 || pngData[1] != 0x50)
            throw new ArgumentException("Invalid PNG data");

        int ReadInt32BE(byte[] d, int offset) =>
            (d[offset] << 24) | (d[offset + 1] << 16) | (d[offset + 2] << 8) | d[offset + 3];

        var width = ReadInt32BE(pngData, 16);
        var height = ReadInt32BE(pngData, 20);
        var bitDepth = pngData[24];
        var colorType = pngData[25];

        // Collect all IDAT chunks, plus the PLTE palette for indexed-colour PNGs.
        var idatData = new MemoryStream();
        byte[]? palette = null;
        var pos = 8; // skip signature
        while (pos + 8 < pngData.Length)
        {
            var chunkLen = ReadInt32BE(pngData, pos);
            var chunkType = System.Text.Encoding.ASCII.GetString(pngData, pos + 4, 4);
            if (chunkType == "IDAT")
                idatData.Write(pngData, pos + 8, chunkLen);
            else if (chunkType == "PLTE" && pos + 8 + chunkLen <= pngData.Length)
            {
                palette = new byte[chunkLen];
                Array.Copy(pngData, pos + 8, palette, 0, chunkLen);
            }
            else if (chunkType == "IEND")
                break;
            pos += 12 + chunkLen; // length + type + data + CRC
        }

        // Decompress (skip 2-byte zlib header)
        var compressed = idatData.ToArray();
        if (compressed.Length < 2)
            throw new ArgumentException("No IDAT data in PNG");

        byte[] rawScanlines;
        using (var input = new MemoryStream(compressed, 2, compressed.Length - 2))
        using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
        using (var output = new MemoryStream())
        {
            deflate.CopyTo(output);
            rawScanlines = output.ToArray();
        }

        // Determine bytes per pixel and extract RGB data
        var channels = colorType switch
        {
            0 => 1,  // Grayscale
            2 => 3,  // RGB
            3 => 1,  // Indexed (palette) — one index sample per pixel; PNG filter bpp is 1
            4 => 2,  // Grayscale + Alpha
            6 => 4,  // RGBA
            _ => 3
        };

        // Indexed PNGs pack the sample at the image bit depth (1/2/4/8); every other
        // supported colour type here is byte-per-channel.
        var stride = colorType == 3
            ? (width * bitDepth + 7) / 8
            : width * channels;
        var rgb = new byte[width * height * 3];

        // Reverse PNG filtering and extract RGB
        var prevRow = new byte[stride];
        var curRow = new byte[stride];
        var scanPos = 0;

        for (var y = 0; y < height; y++)
        {
            if (scanPos >= rawScanlines.Length) break;
            var filterByte = rawScanlines[scanPos++];

            // Read filtered row
            var bytesToRead = Math.Min(stride, rawScanlines.Length - scanPos);
            Array.Copy(rawScanlines, scanPos, curRow, 0, bytesToRead);
            scanPos += stride;

            // Apply PNG filter
            for (var x = 0; x < stride; x++)
            {
                byte a = x >= channels ? curRow[x - channels] : (byte)0;
                byte b = prevRow[x];
                byte c = x >= channels ? prevRow[x - channels] : (byte)0;

                curRow[x] = filterByte switch
                {
                    1 => (byte)(curRow[x] + a),             // Sub
                    2 => (byte)(curRow[x] + b),             // Up
                    3 => (byte)(curRow[x] + (a + b) / 2),   // Average
                    4 => (byte)(curRow[x] + PaethPredictor(a, b, c)), // Paeth
                    _ => curRow[x]                           // None
                };
            }

            // Convert to RGB
            for (var x = 0; x < width; x++)
            {
                var rgbIdx = (y * width + x) * 3;
                switch (colorType)
                {
                    case 0: // Grayscale
                        rgb[rgbIdx] = rgb[rgbIdx + 1] = rgb[rgbIdx + 2] = curRow[x];
                        break;
                    case 2: // RGB
                        rgb[rgbIdx] = curRow[x * 3];
                        rgb[rgbIdx + 1] = curRow[x * 3 + 1];
                        rgb[rgbIdx + 2] = curRow[x * 3 + 2];
                        break;
                    case 4: // Grayscale + Alpha (ignore alpha)
                        rgb[rgbIdx] = rgb[rgbIdx + 1] = rgb[rgbIdx + 2] = curRow[x * 2];
                        break;
                    case 6: // RGBA (ignore alpha)
                        rgb[rgbIdx] = curRow[x * 4];
                        rgb[rgbIdx + 1] = curRow[x * 4 + 1];
                        rgb[rgbIdx + 2] = curRow[x * 4 + 2];
                        break;
                    case 3: // Indexed: unpack the index at the image bit depth, look up /PLTE
                        int idx;
                        if (bitDepth == 8)
                            idx = curRow[x];
                        else
                        {
                            var bitPos = x * bitDepth;
                            var shift = 8 - bitDepth - (bitPos % 8);
                            idx = (curRow[bitPos / 8] >> shift) & ((1 << bitDepth) - 1);
                        }
                        var pi = idx * 3;
                        if (palette is not null && pi + 2 < palette.Length)
                        {
                            rgb[rgbIdx] = palette[pi];
                            rgb[rgbIdx + 1] = palette[pi + 1];
                            rgb[rgbIdx + 2] = palette[pi + 2];
                        }
                        break;
                }
            }

            // Swap prev/cur
            (prevRow, curRow) = (curRow, prevRow);
        }

        return FromRgb(rgb, width, height);
    }

    private static byte PaethPredictor(byte a, byte b, byte c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    /// <summary>
    /// Add this image to a page.
    /// </summary>
    public void ApplyTo(Page page)
    {
        var imgName = RegisterXObject(page);

        // Build content stream operators to place the image.
        var w = DisplayWidth;
        var h = DisplayHeight;

        // Anchor at XIndent/YIndent (the bottom-left placement) when
        // set, else fall back to X/Y.
        double ax = XIndent != 0 ? XIndent : X;
        double ay = YIndent != 0 ? YIndent : Y;

        // Compose scale + rotation into the cm matrix, then translate so the
        // rotated image's bounding box bottom-left lands at the anchor.
        double deg = RotateAngle != 0 ? RotateAngle : (double)Rotate;
        double rad = deg * System.Math.PI / 180.0;
        double cos = System.Math.Cos(rad), sin = System.Math.Sin(rad);
        double ma = w * cos, mb = w * sin, mc = -h * sin, md = h * cos;
        double minX = System.Math.Min(System.Math.Min(0, ma), System.Math.Min(mc, ma + mc));
        double minY = System.Math.Min(System.Math.Min(0, mb), System.Math.Min(md, mb + md));
        double me = ax - minX, mf = ay - minY;

        // Always emit a graphics-state operator (/GS gs) before placing the image so
        // the stamp composites against an explicit ExtGState rather than inheriting a
        // residual one from prior page content — otherwise a background image
        // watermark could hide the underlying content. The
        // ExtGState carries a non-default blend mode and/or partial opacity when
        // requested; otherwise it is empty (an /Type /ExtGState no-op).
        bool wantBlend = !string.IsNullOrEmpty(BlendMode) && BlendMode != "Normal";
        var gsName = RegisterGsExtGState(page, wantBlend ? BlendMode : null, Opacity);
        var gsOp = $"/{gsName} gs ";
        // A %StampId comment makes this stamp discoverable by PdfContentEditor.GetStamps
        // when an id was assigned via setStampId; the PdfFileStamp facade keeps its own
        // ImageStamp's StampId at 0 (it injects the id itself), so there is no double-mark.
        var idComment = (StampId != 0 || ForceStampIdComment) ? $"%StampId={StampId}\n" : "";
        var rectComment = MetaRect is { } mr
            ? $"%StampRect={Format(mr.LLX)} {Format(mr.LLY)} {Format(mr.URX)} {Format(mr.URY)}\n" : "";
        // A foreground stamp is appended after the page's existing content, so it
        // inherits whatever CTM that content leaves active. Pages that were
        // flattened or are slightly malformed can leave a residual CTM — a scale
        // (e.g. a page authored in 1/600" units with a leading "0.12 0 0 -0.12 0
        // 792 cm") and/or an unbalanced q — that would silently transform the
        // stamp, placing it at the wrong position and size. Undo that residual by
        // prefixing the inverse of the active CTM, so the stamp's anchor
        // coordinates are interpreted against the page's base coordinate system.
        var resetCm = string.Empty;
        if (!Background && TryGetResidualCtmInverse(page, out var ia, out var ib,
                out var ic, out var id, out var ie, out var iff))
        {
            resetCm = $"{Format(ia)} {Format(ib)} {Format(ic)} {Format(id)} {Format(ie)} {Format(iff)} cm ";
        }
        var contentOps = $"{idComment}{rectComment}q {resetCm}{gsOp}{Format(ma)} {Format(mb)} {Format(mc)} {Format(md)} {Format(me)} {Format(mf)} cm /{imgName} Do Q\n";
        var contentBytes = System.Text.Encoding.ASCII.GetBytes(contentOps);

        // Add the stamp as a separate content stream so the page's existing
        // content is preserved — AddContentStream/PrependContentStream are
        // array-aware (a page whose /Contents is a stream array would otherwise
        // be overwritten). Background stamps go behind the page content.
        if (Background)
            page.PrependContentStream(contentBytes);
        else
            page.AddContentStream(contentBytes);
    }

    /// <summary>Native pixel width of the source image (the XObject /Width).</summary>
    internal int PixelWidth => _width;
    /// <summary>Native pixel height of the source image (the XObject /Height).</summary>
    internal int PixelHeight => _height;

    /// <summary>Build the decoded image as a standalone /XObject /Image stream
    /// (carrying a DeviceGray /SMask when the source has transparency),
    /// independent of any page. Reused by page placement (<see cref="RegisterXObject"/>)
    /// and by form-field appearance streams (image-button fill).</summary>
    internal PdfStream BuildImageXObject()
    {
        // Honour Quality for DCTDecode (JPEG) images by re-encoding below the default.
        var imageData = _imageData;
        if (_filter == "DCTDecode" && Quality < 100)
            imageData = ReencodeJpeg(imageData, Quality);

        var imgDict = new PdfDictionary();
        imgDict.Set("Type", new PdfName("XObject"));
        imgDict.Set("Subtype", new PdfName("Image"));
        imgDict.Set("Width", new PdfInteger(_width));
        imgDict.Set("Height", new PdfInteger(_height));
        imgDict.Set("BitsPerComponent", new PdfInteger(_bitsPerComponent));
        imgDict.Set("ColorSpace", new PdfName(_colorSpace));
        imgDict.Set("Filter", new PdfName(_filter));
        if (_decodeParms is not null)
            imgDict.Set("DecodeParms", _decodeParms);
        imgDict.Set("Length", new PdfInteger(imageData.Length));

        // A transparent source carries its alpha as a DeviceGray /SMask so the
        // renderer composites it (rather than a white box).
        if (_smaskData is not null)
        {
            var smDict = new PdfDictionary();
            smDict.Set("Type", new PdfName("XObject"));
            smDict.Set("Subtype", new PdfName("Image"));
            smDict.Set("Width", new PdfInteger(_width));
            smDict.Set("Height", new PdfInteger(_height));
            smDict.Set("BitsPerComponent", new PdfInteger(8));
            smDict.Set("ColorSpace", new PdfName("DeviceGray"));
            smDict.Set("Filter", new PdfName("FlateDecode"));
            smDict.Set("Length", new PdfInteger(_smaskData.Length));
            imgDict.Set("SMask", new PdfStream(smDict, _smaskData));
        }

        return new PdfStream(imgDict, imageData);
    }

    // Re-encode JPEG bytes at the given quality (1..100) to honour Quality. System.Drawing
    // JPEG encoding is Windows-only; on other platforms (or on any failure) the original
    // bytes are returned unchanged.
    private static byte[] ReencodeJpeg(byte[] data, int quality)
    {
        if (!OperatingSystem.IsWindows()) return data;
        try { return ReencodeJpegWindows(data, quality); }
        catch { return data; }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static byte[] ReencodeJpegWindows(byte[] data, int quality)
    {
        using var inMs = new MemoryStream(data);
        using var bmp = new System.Drawing.Bitmap(inMs);
        System.Drawing.Imaging.ImageCodecInfo? jpegCodec = null;
        foreach (var c in System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders())
            if (c.FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid) { jpegCodec = c; break; }
        if (jpegCodec is null) return data;
        using var ep = new System.Drawing.Imaging.EncoderParameters(1);
        ep.Param[0] = new System.Drawing.Imaging.EncoderParameter(
            System.Drawing.Imaging.Encoder.Quality, (long)System.Math.Clamp(quality, 1, 100));
        using var outMs = new MemoryStream();
        bmp.Save(outMs, jpegCodec, ep);
        return outMs.ToArray();
    }

    /// <summary>Register the image as an XObject under a fresh /Im name in the
    /// page's resources and return that name, without emitting any placement
    /// operators. Lets callers (e.g. a watermark artifact) emit the <c>Do</c> inside
    /// their own marked-content block.</summary>
    internal string RegisterXObject(Page page)
    {
        var imgStream = BuildImageXObject();

        var resources = GetOrCreateResources(page);
        var xobjectDict = GetOrCreateDict(page, resources, "XObject");

        var imgName = "Im0";
        var counter = 0;
        while (xobjectDict.ContainsKey(imgName))
            imgName = $"Im{++counter}";

        xobjectDict.Set(imgName, imgStream);
        return imgName;
    }

    /// <summary>Register an ExtGState resource carrying an optional /BM blend
    /// mode and (when <paramref name="opacity"/> &lt; 1) /ca + /CA alpha, under a
    /// fresh /GS name; return that name.</summary>
    private static string RegisterGsExtGState(Page page, string? blendMode, double opacity)
    {
        var resources = GetOrCreateResources(page);
        var gsDict = GetOrCreateDict(page, resources, "ExtGState");

        var gsName = "GS0";
        var counter = 0;
        while (gsDict.ContainsKey(gsName))
            gsName = $"GS{++counter}";

        var gs = new PdfDictionary();
        gs.Set("Type", new PdfName("ExtGState"));
        if (!string.IsNullOrEmpty(blendMode))
            gs.Set("BM", new PdfName(blendMode!));
        if (opacity < 0.999)
        {
            gs.Set("ca", new PdfReal(opacity));
            gs.Set("CA", new PdfReal(opacity));
        }
        gsDict.Set(gsName, gs);
        return gsName;
    }

    private static PdfDictionary GetOrCreateResources(Page page)
    {
        var pageDict = page.Dict;
        // /Resources is frequently an indirect reference; resolving it (rather
        // than a bare `as PdfDictionary` cast that yields null) avoids replacing
        // the real dictionary — and silently dropping its /Font, /ExtGState, … —
        // with a fresh empty one.
        var res = pageDict.Get("Resources") as PdfDictionary
            ?? page.Reader.ResolveDict(pageDict.Get("Resources"));
        if (res is null)
        {
            res = new PdfDictionary();
            pageDict.Set("Resources", res);
        }
        return res;
    }

    private static PdfDictionary GetOrCreateDict(Page page, PdfDictionary parent, string key)
    {
        var dict = parent.Get(key) as PdfDictionary
            ?? page.Reader.ResolveDict(parent.Get(key));
        if (dict is null)
        {
            dict = new PdfDictionary();
            parent.Set(key, dict);
        }
        return dict;
    }

    private static byte[] CompressFlate(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var zlib = new ZLibStream(ms, CompressionMode.Compress, leaveOpen: true))
            zlib.Write(data);
        return ms.ToArray();
    }

    private static string Format(double v)
    {
        // Snap floating-point dust (e.g. cos(270°) ≈ -5.5e-14) to zero and emit
        // fixed-point text: PDF reals do not allow exponential notation, so a
        // "G"-formatted tiny value like "-5.5E-14" would be invalid syntax.
        if (System.Math.Abs(v) < 1e-6) v = 0;
        return v.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static bool IsJpeg(byte[] data) =>
        data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF;

    /// <summary>Compute the inverse of the CTM left active at the end of the page's
    /// existing content, so a foreground stamp can cancel it and lay out against
    /// the page's base coordinate system. Returns false (no correction needed)
    /// when the active CTM is already the identity, or when it cannot be parsed
    /// or is singular.</summary>
    private static bool TryGetResidualCtmInverse(Page page, out double ia, out double ib,
        out double ic, out double id, out double ie, out double iff)
    {
        ia = id = 1; ib = ic = ie = iff = 0;
        try
        {
            var content = page.GetContentStreamBytes();
            if (content is null || content.Length == 0) return false;
            var (a, b, c, d, e, f) = ComputeActiveCtm(content);
            // Already identity → nothing to undo (the common, well-formed case).
            if (System.Math.Abs(a - 1) < 1e-6 && System.Math.Abs(b) < 1e-6
                && System.Math.Abs(c) < 1e-6 && System.Math.Abs(d - 1) < 1e-6
                && System.Math.Abs(e) < 1e-6 && System.Math.Abs(f) < 1e-6)
                return false;
            var det = a * d - b * c;
            if (System.Math.Abs(det) < 1e-9) return false;
            ia = d / det;
            ib = -b / det;
            ic = -c / det;
            id = a / det;
            ie = (c * f - d * e) / det;
            iff = (b * e - a * f) / det;
            return true;
        }
        catch { return false; }
    }

    /// <summary>Replay a content stream's graphics-state operators (q, Q, cm) to
    /// determine the CTM active at the end — the transform that appended content
    /// inherits. Strings, inline images and other operators are skipped; only the
    /// matrix-affecting operators are tracked.</summary>
    private static (double a, double b, double c, double d, double e, double f) ComputeActiveCtm(byte[] data)
    {
        (double a, double b, double c, double d, double e, double f) ctm = (1, 0, 0, 1, 0, 0);
        var stack = new System.Collections.Generic.Stack<(double, double, double, double, double, double)>();
        var nums = new System.Collections.Generic.List<double>();
        int i = 0, n = data.Length;
        while (i < n)
        {
            byte ch = data[i];
            // Whitespace
            if (ch is (byte)' ' or (byte)'\n' or (byte)'\r' or (byte)'\t' or (byte)'\f' or 0) { i++; continue; }
            // Comment to end of line
            if (ch == (byte)'%') { while (i < n && data[i] != (byte)'\n' && data[i] != (byte)'\r') i++; continue; }
            // Literal string ( ... ) with escapes and nested parens
            if (ch == (byte)'(')
            {
                int depth = 1; i++;
                while (i < n && depth > 0)
                {
                    if (data[i] == (byte)'\\') { i += 2; continue; }
                    if (data[i] == (byte)'(') depth++;
                    else if (data[i] == (byte)')') depth--;
                    i++;
                }
                nums.Clear();
                continue;
            }
            // Hex string < ... > or dict << >>
            if (ch == (byte)'<')
            {
                if (i + 1 < n && data[i + 1] == (byte)'<') { i += 2; continue; } // dict open — ignore
                while (i < n && data[i] != (byte)'>') i++;
                i++;
                nums.Clear();
                continue;
            }
            if (ch == (byte)'>') { i++; continue; }
            if (ch == (byte)'[' || ch == (byte)']' || ch == (byte)'{' || ch == (byte)'}') { i++; continue; }
            // Name /Xxx
            if (ch == (byte)'/')
            {
                i++;
                while (i < n && !IsDelimOrWs(data[i])) i++;
                continue;
            }
            // Number
            if (ch is (>= (byte)'0' and <= (byte)'9') or (byte)'+' or (byte)'-' or (byte)'.')
            {
                int s = i; i++;
                while (i < n && (data[i] is (>= (byte)'0' and <= (byte)'9') or (byte)'.' or (byte)'e' or (byte)'E' or (byte)'+' or (byte)'-')) i++;
                if (double.TryParse(System.Text.Encoding.ASCII.GetString(data, s, i - s),
                        System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
                    nums.Add(v);
                continue;
            }
            // Operator token (letters and a few symbols)
            int os = i;
            while (i < n && !IsDelimOrWs(data[i])) i++;
            var op = System.Text.Encoding.ASCII.GetString(data, os, i - os);
            switch (op)
            {
                case "q":
                    stack.Push(ctm);
                    break;
                case "Q":
                    if (stack.Count > 0) ctm = stack.Pop();
                    break;
                case "cm":
                    if (nums.Count >= 6)
                    {
                        var m = nums.GetRange(nums.Count - 6, 6);
                        ctm = Multiply(m[0], m[1], m[2], m[3], m[4], m[5], ctm);
                    }
                    break;
                case "BI":
                    // Inline image: skip to the EI terminator.
                    i = SkipInlineImage(data, i);
                    break;
            }
            nums.Clear();
        }
        return ctm;
    }

    private static bool IsDelimOrWs(byte b) =>
        b is (byte)' ' or (byte)'\n' or (byte)'\r' or (byte)'\t' or (byte)'\f' or 0
        or (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']'
        or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%';

    // Multiply matrix m (applied first) by ctm (applied second): result = m × ctm.
    private static (double, double, double, double, double, double) Multiply(
        double a, double b, double c, double d, double e, double f,
        (double a, double b, double c, double d, double e, double f) t)
        => (a * t.a + b * t.c,
            a * t.b + b * t.d,
            c * t.a + d * t.c,
            c * t.b + d * t.d,
            e * t.a + f * t.c + t.e,
            e * t.b + f * t.d + t.f);

    private static int SkipInlineImage(byte[] data, int i)
    {
        // Advance to the EI operator (whitespace-delimited) that ends the image.
        while (i < data.Length - 1)
        {
            if ((data[i] == (byte)'E' && data[i + 1] == (byte)'I')
                && (i == 0 || IsDelimOrWs(data[i - 1]))
                && (i + 2 >= data.Length || IsDelimOrWs(data[i + 2])))
                return i + 2;
            i++;
        }
        return data.Length;
    }

    private static byte[] ReadAll(System.IO.Stream s)
    {
        // Read the whole image from the start. Callers commonly hand us a stream
        // they have just written to (e.g. Image.Save(ms, Png)), leaving the
        // position at the end; copying from there would yield no bytes and raise
        // a spurious "Invalid PNG data". Rewind when the stream supports it.
        if (s.CanSeek) s.Position = 0;
        using var ms = new System.IO.MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private static (int width, int height) ParseJpegDimensions(byte[] data)
    {
        // Scan for SOF0 (0xFF 0xC0) or SOF2 (0xFF 0xC2) marker
        for (var i = 0; i < data.Length - 9; i++)
        {
            if (data[i] != 0xFF) continue;
            var marker = data[i + 1];
            if (marker is 0xC0 or 0xC1 or 0xC2)
            {
                var height = (data[i + 5] << 8) | data[i + 6];
                var width = (data[i + 7] << 8) | data[i + 8];
                return (width, height);
            }
            // Skip marker segment
            if (marker is >= 0xC0 and not 0xFF and not 0x00 and not 0xD8 and not 0xD9
                and not (>= 0xD0 and <= 0xD7))
            {
                if (i + 3 < data.Length)
                {
                    var segLen = (data[i + 2] << 8) | data[i + 3];
                    i += 1 + segLen;
                }
            }
        }

        return (0, 0); // couldn't determine
    }
}
