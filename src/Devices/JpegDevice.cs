using Aspose.Pdf.IO;

namespace Aspose.Pdf.Devices;

/// <summary>How form fields are rendered (canonical Production / Editor split).</summary>
public enum FormPresentationMode
{
    Production = 0,
    Editor = 1,
}

/// <summary>
/// A function that encodes RGBA pixel data to JPEG.
/// </summary>
/// <param name="rgba">RGBA pixel data (4 bytes per pixel).</param>
/// <param name="width">Image width in pixels.</param>
/// <param name="height">Image height in pixels.</param>
/// <param name="quality">JPEG quality 1-100.</param>
/// <returns>JPEG file bytes.</returns>
public delegate byte[] JpegEncoder(byte[] rgba, int width, int height, int quality);

/// <summary>
/// Renders PDF document pages into JPEG image format.
/// Falls back to PNG output when no JPEG encoder is registered.
/// </summary>
public sealed class JpegDevice : ImageDevice
{
    private static JpegEncoder? _encoder;

    /// <summary>
    /// JPEG quality (1-100). Default: 100 (lower values subsample chroma and drift hard-edge colours).
    /// </summary>
    public int Quality { get; }

    /// <summary>Form presentation mode (Production renders form values; Editor renders empty fields).</summary>
    public new FormPresentationMode FormPresentationMode { get; set; } = FormPresentationMode.Production;

    /// <summary>Creates a JpegDevice that renders pages with the given <c>renderer</c> at the default resolution of 150 DPI. JPEG quality is 100.</summary>
    public JpegDevice(IPageRenderer renderer) : base(renderer) { Quality = 100; }
    /// <summary>Creates a JpegDevice that renders pages with the given <c>renderer</c> at the given resolution (150 DPI when <c>resolution</c> is null). JPEG quality is 100.</summary>
    public JpegDevice(IPageRenderer renderer, Resolution resolution) : base(renderer, resolution) { Quality = 100; }
    /// <summary>Creates a JpegDevice that renders pages with the given <c>renderer</c> at the given resolution and encodes with the given JPEG quality (1-100).</summary>
    public JpegDevice(IPageRenderer renderer, Resolution resolution, int quality) : base(renderer, resolution) { Quality = quality; }
    /// <summary>Creates a JpegDevice that renders pages with the built-in renderer at the default resolution of 150 DPI. JPEG quality is 100.</summary>
    public JpegDevice() : base() { Quality = 100; }
    /// <summary>Creates a JpegDevice that renders pages with the built-in renderer at the given resolution. JPEG quality is 100.</summary>
    public JpegDevice(Resolution resolution) : base(resolution) { Quality = 100; }
    /// <summary>Creates a JpegDevice that renders pages at the given resolution and encodes with the given JPEG quality (1-100).</summary>
    public JpegDevice(Resolution resolution, int quality) : base(resolution) { Quality = quality; }
    /// <summary>Creates a JpegDevice that renders pages at 150 DPI and encodes with the given JPEG quality (1-100).</summary>
    public JpegDevice(int quality) : base() { Quality = quality; }
    /// <summary>Creates a JpegDevice whose output image is resampled to the given width and height in pixels; pages are rendered at 150 DPI. JPEG quality is 100.</summary>
    public JpegDevice(int width, int height) : base(width, height) { Quality = 100; }
    /// <summary>Creates a JpegDevice that renders pages at the given resolution and resamples the output image to the given width and height in pixels. JPEG quality is 100.</summary>
    public JpegDevice(int width, int height, Resolution resolution) : base(width, height, resolution) { Quality = 100; }
    /// <summary>Creates a JpegDevice that renders pages at the given resolution, resamples the output to the given width and height in pixels, and encodes with the given JPEG quality (1-100).</summary>
    public JpegDevice(int width, int height, Resolution resolution, int quality) : base(width, height, resolution) { Quality = quality; }

    /// <summary>Construct sized to <paramref name="pageSize"/> at 150 DPI, default quality 100.</summary>
    public JpegDevice(Aspose.Pdf.PageSize pageSize) : base(pageSize) { Quality = 100; }

    /// <summary>Construct sized to <paramref name="pageSize"/> at <paramref name="resolution"/>, default quality 100.</summary>
    public JpegDevice(Aspose.Pdf.PageSize pageSize, Resolution resolution) : base(pageSize, resolution) { Quality = 100; }

    /// <summary>Construct sized to <paramref name="pageSize"/> at <paramref name="resolution"/> with explicit quality.</summary>
    public JpegDevice(Aspose.Pdf.PageSize pageSize, Resolution resolution, int quality) : base(pageSize, resolution) { Quality = quality; }

    /// <summary>
    /// Register a global JPEG encoder function.
    /// </summary>
    public static void SetEncoder(JpegEncoder encoder) => _encoder = encoder;

    /// <summary>
    /// Remove the registered JPEG encoder.
    /// </summary>
    public static void ClearEncoder() => _encoder = null;

    /// <inheritdoc />
    public override void Process(Page page, Stream output)
    {
        var rgba = RenderPage(page);
        // The density written is the resolution the page was drawn at, which the
        // large-page guard may have reduced from the requested one.
        var dpi = RenderResolution(page);

        if (_encoder is not null)
        {
            var jpeg = _encoder(rgba.Data, rgba.Width, rgba.Height, Quality);
            output.Write(jpeg);
        }
        else if (Compat.IsWindows())
        {
            // The platform (GDI+) codec, not the managed encoder: rendered-page JPEG
            // consumers compare against outputs produced by this codec, and the managed
            // encoder's chroma subsampling shifts colours at hard edges well past
            // typical comparison tolerances.
            WriteGdiPlusJpeg(rgba, dpi, output);
        }
        else
        {
            // Use built-in JPEG encoder. Propagate DPI so readers don't fall back
            // to the JFIF unit-less default (which most consumers report as 96 DPI).
            var jpeg = IO.JpegEncoderImpl.Encode(rgba.Data, rgba.Width, rgba.Height, Quality, dpi.X, dpi.Y);
            output.Write(jpeg);
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private void WriteGdiPlusJpeg(RgbaBuffer rgba, (int X, int Y) dpi, Stream output)
    {
        int w = rgba.Width, h = rgba.Height;
        System.Drawing.Imaging.ImageCodecInfo? codec = null;
        foreach (var c in System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders())
            if (c.FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid) { codec = c; break; }
        if (codec is null)
        {
            var jpeg = IO.JpegEncoderImpl.Encode(rgba.Data, w, h, Quality, dpi.X, dpi.Y);
            output.Write(jpeg);
            return;
        }
        // The rendered buffer is the bitmap's own pixels: its RGBA becomes BGRX in place (alpha
        // dropped; JPEG is opaque, matching the managed encoder) and stays pinned for the encode.
        var pixels = rgba.Data;
        for (int i = 0; i + 3 < pixels.Length; i += 4)
        {
            (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
            pixels[i + 3] = 255;
        }
        var pinned = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            using var bmp = new System.Drawing.Bitmap(w, h, w * 4, System.Drawing.Imaging.PixelFormat.Format32bppRgb, pinned.AddrOfPinnedObject());
            bmp.SetResolution(dpi.X, dpi.Y);
            using var ep = new System.Drawing.Imaging.EncoderParameters(1);
            ep.Param[0] = new System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)Quality);
            // GDI+ needs a seekable stream; buffer through memory for arbitrary outputs.
            using var ms = new MemoryStream();
            bmp.Save(ms, codec, ep);
            ms.Position = 0;
            ms.CopyTo(output);
        }
        finally { pinned.Free(); }
    }
}

