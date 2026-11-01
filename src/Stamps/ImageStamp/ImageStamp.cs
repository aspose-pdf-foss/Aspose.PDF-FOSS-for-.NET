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
    // FlateDecode'd DeviceGray alpha channel for a transparent source (GIF/PNG); emitted
    // as the image's /SMask so transparent pixels show the page behind.
    private byte[]? _smaskData;
    // Optional /DecodeParms for filters that need them (e.g. CCITTFaxDecode K/Columns/Rows).
    private PdfDictionary? _decodeParms;
    // Optional /Decode array. A standalone Adobe CMYK JPEG stores its samples
    // inverted (255 = no ink); embedding it verbatim needs /Decode [1 0 ×4] so
    // conforming readers flip the values back to direct ink amounts.
    private double[]? _decodeArray;
    // A colour space a name cannot say (a PNG palette's /Indexed array); _colorSpace names it otherwise.
    private PdfObject? _colorSpaceObject;

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
        else if (Compat.IsWindows())
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
        _decodeParms = seeded._decodeParms;
        _colorSpaceObject = seeded._colorSpaceObject;
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

    /// <summary>The part of the page the image shows through, in page space: what
    /// <see cref="Page.AddImage(System.IO.Stream, Rectangle, Rectangle)"/> crops the placed image to.</summary>
    internal Aspose.Pdf.Rectangle? ClipBox { get; set; }

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
    /// inherits — and how its saves and restores balance: <c>StrayRestores</c> is how many
    /// levels below its start its restores reach at their deepest (a Q with no q of its
    /// own), <c>OpenSaves</c> how many levels above that point it leaves open at its end.
    /// Comments, strings and inline images are stepped over; every other byte is passed in one
    /// tight run up to the next that could end an operator the scan reads (a turn baked into a
    /// page scans the whole of its content), and a cm takes the six numbers standing before it -
    /// unless <paramref name="readMatrices"/> is false, when the CTM stays the identity. With
    /// <paramref name="toFirstSave"/> the scan ends at the first q, giving the CTM the content set before it.</summary>
    internal static ((double a, double b, double c, double d, double e, double f) Ctm, int StrayRestores, int OpenSaves)
        ScanGraphicsState(byte[] data, bool readMatrices = true, bool toFirstSave = false)
    {
        (double a, double b, double c, double d, double e, double f) ctm = (1, 0, 0, 1, 0, 0);
        var stack = new System.Collections.Generic.Stack<(double, double, double, double, double, double)>();
        int level = 0, lowest = 0;
        int i = 0, n = data.Length;
        while (true)
        {
            while (i < n && !ScanStops[data[i]]) i++;
            if (i >= n) break;
            switch (data[i])
            {
                case (byte)'%':
                    // Comment to end of line
                    while (i < n && data[i] != (byte)'\n' && data[i] != (byte)'\r') i++;
                    continue;
                case (byte)'(':
                    i = SkipLiteralString(data, i);
                    continue;
                case (byte)'<':
                    // Hex string < ... > or dict << >>
                    if (i + 1 < n && data[i + 1] == (byte)'<') { i += 2; continue; } // dict open — ignore
                    while (i < n && data[i] != (byte)'>') i++;
                    i++;
                    continue;
                case (byte)'q' when StandsAlone(data, i, 1):
                    if (toFirstSave) return (ctm, 0, 0);
                    stack.Push(ctm);
                    level++;
                    break;
                case (byte)'Q' when StandsAlone(data, i, 1):
                    if (stack.Count > 0) ctm = stack.Pop();
                    lowest = System.Math.Min(lowest, --level);
                    break;
                case (byte)'m' when readMatrices && i > 0 && data[i - 1] == (byte)'c' && StandsAlone(data, i - 1, 2):
                    if (MatrixOperandsBefore(data, i - 1) is { } m)
                        ctm = Multiply(m[0], m[1], m[2], m[3], m[4], m[5], ctm);
                    break;
                case (byte)'I' when i > 0 && data[i - 1] == (byte)'B' && StandsAlone(data, i - 1, 2):
                    // Inline image: skip to the EI terminator.
                    i = SkipInlineImage(data, i + 1);
                    continue;
            }
            i++;
        }
        return (ctm, -lowest, level - lowest);
    }

    /// <summary>The bytes the content scan stops at: what opens a comment or a string, and the last
    /// letter of each operator it reads (q, Q, cm, BI).</summary>
    private static readonly bool[] ScanStops = ScanStopTable("%(<qQmI");

    private static bool[] ScanStopTable(string stops)
    {
        var table = new bool[256];
        foreach (var stop in stops) table[stop] = true;
        return table;
    }

    /// <summary>Whether the token of <paramref name="length"/> bytes at <paramref name="start"/> is
    /// one: whitespace or a delimiter other than a name's / before it, whitespace or a delimiter after.</summary>
    private static bool StandsAlone(byte[] data, int start, int length)
        => (start == 0 || (IsDelimOrWs(data[start - 1]) && data[start - 1] != (byte)'/'))
            && (start + length >= data.Length || IsDelimOrWs(data[start + length]));

    /// <summary>The six numbers standing before the operator at <paramref name="at"/>, each a token
    /// of its own; null when fewer stand there.</summary>
    private static double[]? MatrixOperandsBefore(byte[] data, int at)
    {
        const int MatrixOperands = 6;
        var operands = new double[MatrixOperands];
        var end = at;
        for (var k = MatrixOperands - 1; k >= 0; k--)
        {
            while (end > 0 && data[end - 1] is (byte)' ' or (byte)'\n' or (byte)'\r' or (byte)'\t' or (byte)'\f' or 0) end--;
            var start = end;
            while (start > 0 && data[start - 1] is (>= (byte)'0' and <= (byte)'9') or (byte)'.' or (byte)'e' or (byte)'E' or (byte)'+' or (byte)'-')
                start--;
            if (start == end || !StandsAlone(data, start, end - start)
                || !double.TryParse(System.Text.Encoding.ASCII.GetString(data, start, end - start),
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out operands[k]))
                return null;
            end = start;
        }
        return operands;
    }

    /// <summary>The index just past the literal string opening at <paramref name="i"/>: escapes
    /// and nested parentheses are its own.</summary>
    private static int SkipLiteralString(byte[] data, int i)
    {
        int depth = 1, n = data.Length;
        i++;
        while (i < n && depth > 0)
        {
            if (data[i] == (byte)'\\') { i += 2; continue; }
            if (data[i] == (byte)'(') depth++;
            else if (data[i] == (byte)')') depth--;
            i++;
        }
        return i;
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
        var i = 0;
        while (i < data.Length - 9)
        {
            if (data[i] != 0xFF) { i++; continue; }
            var marker = data[i + 1];
            if (marker is 0xC0 or 0xC1 or 0xC2)
            {
                var height = (data[i + 5] << 8) | data[i + 6];
                var width = (data[i + 7] << 8) | data[i + 8];
                return (width, height);
            }
            // Skip marker segment
            if (marker is >= 0xC0 and not 0xFF and not 0x00 and not 0xD8 and not 0xD9
                and not (>= 0xD0 and <= 0xD7) && i + 3 < data.Length)
            {
                var segLen = (data[i + 2] << 8) | data[i + 3];
                i += 1 + segLen;
            }
            i++;
        }

        return (0, 0); // couldn't determine
    }
}
