namespace Aspose.Pdf.Devices;

/// <summary>
/// Color depth options for TIFF encoding.
/// </summary>
public enum ColorDepth
{
    /// <summary>Default (24-bit RGB).</summary>
    Default,
    /// <summary>1 bit per pixel (bi-level).</summary>
    Format1bpp,
    /// <summary>4 bits per pixel.</summary>
    Format4bpp,
    /// <summary>8 bits per pixel (grayscale or palette).</summary>
    Format8bpp,
    /// <summary>24 bits per pixel (true color).</summary>
    Format24bpp,
}

/// <summary>
/// TIFF compression algorithm options.
/// </summary>
public enum CompressionType
{
    /// <summary>No compression.</summary>
    None,
    /// <summary>CCITT Group 3 (1D).</summary>
    CCITT3,
    /// <summary>CCITT Group 4.</summary>
    CCITT4,
    /// <summary>LZW compression.</summary>
    LZW,
    /// <summary>RLE (PackBits) compression.</summary>
    RLE,
    /// <summary>PackBits compression.</summary>
    Packbits,
}

/// <summary>
/// Settings for TIFF image generation (color depth, compression).
/// </summary>
public sealed class TiffSettings
{
    /// <summary>Initializes default TIFF settings (24bpp, no compression).</summary>
    public TiffSettings() { }

    /// <summary>Initializes TIFF settings with the specified color depth.</summary>
    public TiffSettings(ColorDepth colorDepth)
    {
        Depth = colorDepth;
    }

    /// <summary>Initializes TIFF settings with the specified compression type.</summary>
    public TiffSettings(CompressionType compressionType)
    {
        Compression = compressionType;
    }

    /// <summary>Initializes TIFF settings with the specified compression and color depth.</summary>
    public TiffSettings(CompressionType compression, ColorDepth colorDepth)
    {
        Compression = compression;
        Depth = colorDepth;
    }

    /// <summary>Initializes TIFF settings with the specified compression, color depth and margins (margins are stored but not applied by the renderer).</summary>
    public TiffSettings(CompressionType compressionType, ColorDepth colorDepth, Margins margins)
    {
        Compression = compressionType;
        Depth = colorDepth;
        Margins = margins ?? new Margins();
    }

    /// <summary>Initializes TIFF settings with the specified compression, color depth, margins and blank-page skipping.</summary>
    public TiffSettings(CompressionType compressionType, ColorDepth colorDepth, Margins margins, bool skipBlankPages)
        : this(compressionType, colorDepth, margins)
    {
        SkipBlankPages = skipBlankPages;
    }

    /// <summary>Initializes TIFF settings with the specified compression, color depth, margins, blank-page skipping and page orientation hint.</summary>
    public TiffSettings(CompressionType compressionType, ColorDepth colorDepth, Margins margins, bool skipBlankPages, ShapeType shapeType)
        : this(compressionType, colorDepth, margins, skipBlankPages)
    {
        Shape = shapeType;
    }

    /// <summary>Initializes default TIFF settings with the specified margins (stored but not applied by the renderer).</summary>
    public TiffSettings(Margins margins) { Margins = margins ?? new Margins(); }

    /// <summary>Initializes default TIFF settings with the specified page orientation hint (stored only).</summary>
    public TiffSettings(ShapeType shapeType) { Shape = shapeType; }

    /// <summary>Initializes default TIFF settings, optionally skipping blank pages.</summary>
    public TiffSettings(bool skipBlankPages) { SkipBlankPages = skipBlankPages; }

    /// <summary>Initializes TIFF settings with all options.</summary>
    public TiffSettings(CompressionType compression, ColorDepth colorDepth, ShapeType shape, float brightness, bool skipBlankPages = false)
    {
        Compression = compression;
        Depth = colorDepth;
        Shape = shape;
        Brightness = brightness;
        SkipBlankPages = skipBlankPages;
    }

    /// <summary>Page margins applied before the TIFF render. Stored only — the
    /// current renderer uses the page's intrinsic crop box and ignores this hint.</summary>
    public Margins Margins { get; private set; } = new Margins();

    /// <summary>The color depth (bits per pixel).</summary>
    public ColorDepth Depth { get; set; } = ColorDepth.Default;

    /// <summary>The compression algorithm.</summary>
    public CompressionType Compression { get; set; } = CompressionType.LZW;

    /// <summary>Whether to skip blank pages during conversion.</summary>
    public bool SkipBlankPages { get; set; }

    /// <summary>Brightness adjustment (0.0-1.0, default 0.5).</summary>
    public float Brightness { get; set; } = 0.5f;

    /// <summary>Preferred page orientation for rendered TIFF output. Stored
    /// for API compatibility; the current renderer keeps
    /// each page's native aspect ratio regardless of this hint.</summary>
    public ShapeType Shape { get; set; } = ShapeType.None;

    /// <summary>Which page box to use for the rendered page extents.</summary>
    public PageCoordinateType CoordinateType { get; set; } = PageCoordinateType.CropBox;
}

/// <summary>
/// Shape type for TIFF rendering.
/// </summary>
public enum ShapeType
{
    /// <summary>No shape.</summary>
    None,
    /// <summary>Landscape orientation.</summary>
    Landscape,
    /// <summary>Portrait orientation.</summary>
    Portrait,
}

/// <summary>Page margins in points for TIFF rendering.</summary>
public sealed class Margins
{
    /// <summary>Gets or sets the left margin.</summary>
    public int Left { get; set; }
    /// <summary>Gets or sets the right margin.</summary>
    public int Right { get; set; }
    /// <summary>Gets or sets the top margin.</summary>
    public int Top { get; set; }
    /// <summary>Gets or sets the bottom margin.</summary>
    public int Bottom { get; set; }

    /// <summary>Creates margins with all four sides set to 0.</summary>
    public Margins() { }

    /// <summary>Creates margins with the given left, right, top and bottom values.</summary>
    public Margins(int left, int right, int top, int bottom)
    {
        Left = left; Right = right; Top = top; Bottom = bottom;
    }
}


/// <summary>
/// Represents a physical page size in points.
/// </summary>
public sealed class PageSize
{
    /// <summary>Initializes a page size with the specified width and height in points.</summary>
    public PageSize(float width, float height)
    {
        Width = width;
        Height = height;
    }

    /// <summary>Page width in points.</summary>
    public float Width { get; }

    /// <summary>Page height in points.</summary>
    public float Height { get; }

    // ── Common predefined page sizes ──────────────────────────────────────────
    public static readonly PageSize A0 = new(2384, 3370);
    public static readonly PageSize A1 = new(1684, 2384);
    public static readonly PageSize A2 = new(1191, 1684);
    public static readonly PageSize A3 = new(842, 1191);
    public static readonly PageSize A4 = new(595, 842);
    public static readonly PageSize A5 = new(420, 595);
    public static readonly PageSize Letter = new(612, 792);
    public static readonly PageSize Legal = new(612, 1008);
    public static readonly PageSize Tabloid = new(792, 1224);
    public static readonly PageSize P11x17 = new(792, 1224);
}
