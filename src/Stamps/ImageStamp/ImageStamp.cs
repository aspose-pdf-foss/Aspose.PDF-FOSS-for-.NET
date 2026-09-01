using System.IO.Compression;
using Aspose.Pdf.Core;

namespace Aspose.Pdf;

/// <summary>
/// Adds an image to a PDF page. Supports raw pixel data (FlateDecode) and JPEG pass-through.
/// </summary>
public partial class ImageStamp : BaseParagraph
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
    // Optional /Decode array. A standalone Adobe CMYK JPEG stores its samples
    // inverted (255 = no ink); embedding it verbatim needs /Decode [1 0 ×4] so
    // conforming readers flip the values back to direct ink amounts.
    private double[]? _decodeArray;

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
        else if (IsSvg(sourceBytes) && ImageRasterizer.RasterizeSvg(sourceBytes) is { } svgPng)
            seeded = FromPngData(svgPng);
        else if (OperatingSystem.IsWindows())
            seeded = TryFromGdiPlusDecoder(sourceBytes) ?? TryFromManagedDecoder(sourceBytes);
        else
            seeded = TryFromManagedDecoder(sourceBytes);
        if (seeded is null) ThrowIfWindowsOnlyMetafile(sourceBytes);
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

    /// <summary>Position X in points from bottom-left.</summary>
    public double X { get; set; }

    /// <summary>Position Y in points from bottom-left.</summary>
    public double Y { get; set; }

    /// <summary>Display width in points. Defaults to image pixel width.</summary>
    public double DisplayWidth { get; set; }

    /// <summary>Display height in points. Defaults to image pixel height.</summary>
    public double DisplayHeight { get; set; }

    /// <summary>Public-API-shape alias for <see cref="DisplayWidth"/>.</summary>
    public double Width { get => DisplayWidth; set => DisplayWidth = value; }

    /// <summary>Public-API-shape alias for <see cref="DisplayHeight"/>.</summary>
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

    /// <summary>Rotation enum (Aspose.Pdf Rotation; 0/90/180/270 in degrees).
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

    /// <summary>Public-API-shape alias for <see cref="ApplyTo"/>.</summary>
    public void Put(Page page) => ApplyTo(page);

    /// <summary>When set (the Page.AddImage path), the anchor rectangle is interpreted
    /// in the ROTATED (as-displayed) coordinate system of a /Rotate page and the image
    /// is drawn upright for the viewer — the AddImage path prepends the
    /// matching rotation-compensating cm. Stamps keep the raw page coordinate system.</summary>
    internal bool CompensatePageRotation;

    /// <summary>SOF component count + Adobe APP14 presence, from the JPEG marker chain.</summary>
    private static (int components, bool hasAdobeMarker) ParseJpegColorInfo(byte[] data)
    {
        int comps = 3;
        var adobe = false;
        var i = 2;
        while (i + 3 < data.Length)
        {
            if (data[i] != 0xFF) { i++; continue; }
            var marker = data[i + 1];
            if (marker == 0xDA || marker == 0xD9) break; // scan data / EOI
            if (marker == 0xFF || marker == 0x00 || marker == 0xD8
                || (marker >= 0xD0 && marker <= 0xD7)) { i += 2; continue; }
            var segLen = (data[i + 2] << 8) | data[i + 3];
            if (marker is 0xC0 or 0xC1 or 0xC2 && i + 9 < data.Length)
                comps = data[i + 9];
            else if (marker == 0xEE && segLen >= 7
                     && i + 9 < data.Length
                     && data[i + 4] == 'A' && data[i + 5] == 'd' && data[i + 6] == 'o'
                     && data[i + 7] == 'b' && data[i + 8] == 'e')
                adobe = true;
            i += 2 + segLen;
        }
        return (comps, adobe);
    }

    /// <summary>Native pixel width of the source image (the XObject /Width).</summary>
    internal int PixelWidth => _width;
    /// <summary>Native pixel height of the source image (the XObject /Height).</summary>
    internal int PixelHeight => _height;

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
