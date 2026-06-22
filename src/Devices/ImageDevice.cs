namespace Aspose.Pdf.Devices;

/// <summary>
/// Abstract base class for image rendering devices (PNG, JPEG, BMP, TIFF).
/// Requires an <see cref="IPageRenderer"/> implementation to render PDF pages to pixels.
/// </summary>
public abstract class ImageDevice
{
    private readonly IPageRenderer _renderer;

    /// <summary>
    /// Underlying page renderer. Exposed to subclasses so they can drive
    /// it at non-default resolutions (e.g. supersampling for 1bpp output).
    /// </summary>
    protected IPageRenderer Renderer => _renderer;

    /// <summary>
    /// Output resolution in DPI.
    /// </summary>
    public Resolution Resolution { get; }

    /// <summary>Explicit output pixel width, or 0 when the output follows the page's natural size at <see cref="Resolution"/>.</summary>
    protected int TargetWidth { get; }

    /// <summary>Explicit output pixel height, or 0 when the output follows the page's natural size at <see cref="Resolution"/>.</summary>
    protected int TargetHeight { get; }

    /// <summary>Rendering options for advanced control of page-to-image rendering.</summary>
    public Aspose.Pdf.RenderingOptions RenderingOptions { get; set; } = new Aspose.Pdf.RenderingOptions();

    /// <summary>Which page box drives the rendered image extents. Stored for
    /// API-parity; current renderer always uses the CropBox (which equals
    /// the MediaBox when no explicit CropBox is set).</summary>
    public PageCoordinateType CoordinateType { get; set; } = PageCoordinateType.CropBox;

    /// <summary>
    /// Initializes a new ImageDevice with the specified renderer and resolution.
    /// </summary>
    protected ImageDevice(IPageRenderer renderer, Resolution resolution)
    {
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        Resolution = resolution ?? new Resolution(150);
    }

    /// <summary>
    /// Initializes a new ImageDevice with the specified renderer and default resolution (150 DPI).
    /// </summary>
    protected ImageDevice(IPageRenderer renderer)
        : this(renderer, new Resolution(150))
    {
    }

    /// <summary>
    /// Initializes a new ImageDevice with a Resolution using the built-in SoftwarePageRenderer.
    /// </summary>
    public ImageDevice(Resolution resolution)
        : this(DefaultRenderer(), resolution)
    {
    }

    /// <summary>
    /// Initializes a new ImageDevice with default resolution and built-in SoftwarePageRenderer.
    /// </summary>
    public ImageDevice()
        : this(DefaultRenderer(), new Resolution(150))
    {
    }

    /// <summary>
    /// Initializes a new ImageDevice with explicit output pixel dimensions using the built-in SoftwarePageRenderer
    /// at default resolution (150 DPI). The rendered page is bilinearly resampled to <paramref name="width"/>×<paramref name="height"/>.
    /// </summary>
    public ImageDevice(int width, int height)
        : this(DefaultRenderer(), new Resolution(150))
    {
        TargetWidth = width;
        TargetHeight = height;
    }

    /// <summary>
    /// Initializes a new ImageDevice with explicit output pixel dimensions and rendering resolution using the built-in SoftwarePageRenderer.
    /// The page is rendered at <paramref name="resolution"/> DPI for glyph/vector quality, then bilinearly resampled to <paramref name="width"/>×<paramref name="height"/>.
    /// </summary>
    public ImageDevice(int width, int height, Resolution resolution)
        : this(DefaultRenderer(), resolution)
    {
        TargetWidth = width;
        TargetHeight = height;
    }

    /// <summary>Initialize from a PageSize (in points; converted to integer pixel dimensions at the default 150 DPI).</summary>
    public ImageDevice(Aspose.Pdf.PageSize pageSize)
        : this(PointsToPixels(pageSize?.Width  ?? 0, 150),
               PointsToPixels(pageSize?.Height ?? 0, 150))
    {
    }

    /// <summary>Initialize from a PageSize and rendering Resolution.</summary>
    public ImageDevice(Aspose.Pdf.PageSize pageSize, Resolution resolution)
        : this(PointsToPixels(pageSize?.Width  ?? 0, resolution?.X ?? 150),
               PointsToPixels(pageSize?.Height ?? 0, resolution?.Y ?? 150),
               resolution!)
    {
    }

    /// <summary>
    /// The built-in renderer used when callers don't supply one. On Windows this is the
    /// GDI+ renderer (matches the platform's native rasterizer); elsewhere it is the
    /// portable software renderer, since GDI+ drawing is unavailable off Windows.
    /// </summary>
    private static IPageRenderer DefaultRenderer() =>
        OperatingSystem.IsWindows() ? new GdiPlusPageRenderer() : new SoftwarePageRenderer();

    /// <summary>Convert a points dimension to pixels at the given DPI.
    /// PDF points are 1/72 inch; pixels = points × DPI / 72.</summary>
    private static int PointsToPixels(double points, double dpi) =>
        (int)Math.Round(points * dpi / 72.0);

    /// <summary>Configured target pixel width (0 = follow the page's natural size at <see cref="Resolution"/>).</summary>
    public int Width => TargetWidth;

    /// <summary>Configured target pixel height (0 = follow the page's natural size at <see cref="Resolution"/>).</summary>
    public int Height => TargetHeight;

    /// <summary>How form fields render (Production vs Editor). Stored only — FOSS renderer
    /// always emits the production appearance regardless of this flag.</summary>
    public FormPresentationMode FormPresentationMode { get; set; } = FormPresentationMode.Production;

    /// <summary>
    /// Render a page to a <see cref="System.Drawing.Bitmap"/>. The bitmap is built from the
    /// internal RGBA buffer; on non-Windows hosts <c>System.Drawing.Common</c> raises
    /// <see cref="System.PlatformNotSupportedException"/> at bitmap construction time
    /// — that's the cross-platform shape Aspose.PDF for .NET also ships.
    /// </summary>
    public System.Drawing.Bitmap GetBitmap(Page page)
    {
        var rendered = RenderPage(page);
#pragma warning disable CA1416
        var bmp = new System.Drawing.Bitmap(rendered.Width, rendered.Height,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var rect = new System.Drawing.Rectangle(0, 0, rendered.Width, rendered.Height);
        var data = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.WriteOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var bgra = new byte[rendered.Data.Length];
            for (int i = 0; i + 3 < rendered.Data.Length; i += 4)
            {
                bgra[i + 0] = rendered.Data[i + 2]; // B
                bgra[i + 1] = rendered.Data[i + 1]; // G
                bgra[i + 2] = rendered.Data[i + 0]; // R
                bgra[i + 3] = rendered.Data[i + 3]; // A
            }
            System.Runtime.InteropServices.Marshal.Copy(bgra, 0, data.Scan0, bgra.Length);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        return bmp;
#pragma warning restore CA1416
    }

    /// <summary>
    /// Convert a page to image bytes and write to stream.
    /// </summary>
    public abstract void Process(Page page, Stream output);

    /// <summary>
    /// Convert a page to an image file.
    /// </summary>
    public void Process(Page page, string outputFileName)
    {
        using var fs = new FileStream(outputFileName, FileMode.Create, FileAccess.Write);
        Process(page, fs);
    }

    /// <summary>
    /// Convert a page to image bytes.
    /// </summary>
    public byte[] Process(Page page)
    {
        using var ms = new MemoryStream();
        Process(page, ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Render a page to an RGBA pixel buffer using the configured renderer and resolution.
    /// When <see cref="TargetWidth"/>/<see cref="TargetHeight"/> are set, the rendered buffer is
    /// bilinearly resampled to those pixel dimensions (matches Aspose.PDF for .NET's
    /// <c>PngDevice(int, int, Resolution)</c> contract: resolution controls render quality,
    /// the size pair pins the final pixel dimensions).
    /// </summary>
    protected RgbaBuffer RenderPage(Page page)
    {
        // When the caller pinned both pixel dimensions AND the target aspect matches
        // the page aspect, render straight at that size instead of
        // render-at-DPI-then-resample. The template PNG is rasterized at the final
        // 850×1100 grid too, so any stroke-hinting rules (pixel-grid snap for ~1px
        // strokes) only take effect when we're actually drawing at 1.39-px line
        // widths — at 300-DPI intermediate resolution all lines are 4+ pixels wide
        // and the hinting path is skipped. For mismatched aspects (e.g. 500×700 on
        // a 612×792 Letter page) we fall through to render-uniformly-then-resample
        // so the content fills the target canvas the way Aspose's GDI+ renderer does.
        if (TargetWidth > 0 && TargetHeight > 0)
        {
            var pageAspect = page.Width / page.Height;
            var targetAspect = (double)TargetWidth / TargetHeight;
            if (Math.Abs(pageAspect - targetAspect) < 1e-3)
            {
                if (_renderer is SoftwarePageRenderer swDirect)
                    return swDirect.RenderPageAtPixelSize(page, TargetWidth, TargetHeight);
                if (OperatingSystem.IsWindows() && _renderer is GdiPlusPageRenderer gdiDirect)
                    return gdiDirect.RenderPageAtPixelSize(page, TargetWidth, TargetHeight);
            }
        }

        RgbaBuffer rendered;
        if (_renderer is SoftwarePageRenderer sw)
            rendered = sw.RenderPage(page, Resolution.X, Resolution.Y);
        else if (OperatingSystem.IsWindows() && _renderer is GdiPlusPageRenderer gdi)
            rendered = gdi.RenderPage(page, Resolution.X, Resolution.Y);
        else
            rendered = _renderer.RenderPage(page.Reader.RawData, page.Number, Resolution.X);

        if (TargetWidth <= 0 || TargetHeight <= 0) return rendered;
        if (rendered.Width == TargetWidth && rendered.Height == TargetHeight) return rendered;

        return ResampleBilinear(rendered, TargetWidth, TargetHeight);
    }

    /// <summary>
    /// Bilinear-resample an RGBA buffer to a new pixel size. Samples at pixel centres
    /// (GDI+-compatible offset) so straight scales of clean integer-aspect pages land on
    /// the same grid the template was rasterised against.
    /// </summary>
    private static RgbaBuffer ResampleBilinear(RgbaBuffer src, int dstW, int dstH)
    {
        var dst = new byte[dstW * dstH * 4];
        var xScale = (double)src.Width / dstW;
        var yScale = (double)src.Height / dstH;

        for (var y = 0; y < dstH; y++)
        {
            var sy = (y + 0.5) * yScale - 0.5;
            if (sy < 0) sy = 0;
            if (sy > src.Height - 1) sy = src.Height - 1;
            var iy = (int)sy;
            var fy = sy - iy;
            var iy2 = Math.Min(iy + 1, src.Height - 1);

            for (var x = 0; x < dstW; x++)
            {
                var sx = (x + 0.5) * xScale - 0.5;
                if (sx < 0) sx = 0;
                if (sx > src.Width - 1) sx = src.Width - 1;
                var ix = (int)sx;
                var fx = sx - ix;
                var ix2 = Math.Min(ix + 1, src.Width - 1);

                // Four RGBA corner samples.
                var p00 = (iy * src.Width + ix) * 4;
                var p10 = (iy * src.Width + ix2) * 4;
                var p01 = (iy2 * src.Width + ix) * 4;
                var p11 = (iy2 * src.Width + ix2) * 4;

                var w00 = (1 - fx) * (1 - fy);
                var w10 = fx * (1 - fy);
                var w01 = (1 - fx) * fy;
                var w11 = fx * fy;

                var di = (y * dstW + x) * 4;
                for (var c = 0; c < 4; c++)
                {
                    dst[di + c] = (byte)(src.Data[p00 + c] * w00 + src.Data[p10 + c] * w10 +
                                         src.Data[p01 + c] * w01 + src.Data[p11 + c] * w11);
                }
            }
        }

        return new RgbaBuffer(dst, dstW, dstH);
    }
}
