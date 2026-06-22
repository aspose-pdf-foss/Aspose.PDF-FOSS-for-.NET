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
    /// JPEG quality (1-100). Default: 85.
    /// </summary>
    public int Quality { get; }

    /// <summary>Form presentation mode (Production renders form values; Editor renders empty fields).</summary>
    public new FormPresentationMode FormPresentationMode { get; set; } = FormPresentationMode.Production;

    public JpegDevice(IPageRenderer renderer) : base(renderer) { Quality = 85; }
    public JpegDevice(IPageRenderer renderer, Resolution resolution) : base(renderer, resolution) { Quality = 85; }
    public JpegDevice(IPageRenderer renderer, Resolution resolution, int quality) : base(renderer, resolution) { Quality = quality; }
    public JpegDevice() : base() { Quality = 85; }
    public JpegDevice(Resolution resolution) : base(resolution) { Quality = 85; }
    public JpegDevice(Resolution resolution, int quality) : base(resolution) { Quality = quality; }
    public JpegDevice(int quality) : base() { Quality = quality; }
    public JpegDevice(int width, int height) : base(width, height) { Quality = 85; }
    public JpegDevice(int width, int height, Resolution resolution) : base(width, height, resolution) { Quality = 85; }
    public JpegDevice(int width, int height, Resolution resolution, int quality) : base(width, height, resolution) { Quality = quality; }

    /// <summary>Construct sized to <paramref name="pageSize"/> at 150 DPI, default quality 85.</summary>
    public JpegDevice(Aspose.Pdf.PageSize pageSize) : base(pageSize) { Quality = 85; }

    /// <summary>Construct sized to <paramref name="pageSize"/> at <paramref name="resolution"/>, default quality 85.</summary>
    public JpegDevice(Aspose.Pdf.PageSize pageSize, Resolution resolution) : base(pageSize, resolution) { Quality = 85; }

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

        if (_encoder is not null)
        {
            var jpeg = _encoder(rgba.Data, rgba.Width, rgba.Height, Quality);
            output.Write(jpeg);
        }
        else
        {
            // Use built-in JPEG encoder. Propagate DPI so readers don't fall back
            // to the JFIF unit-less default (which most consumers report as 96 DPI).
            var jpeg = IO.JpegEncoderImpl.Encode(rgba.Data, rgba.Width, rgba.Height, Quality, Resolution.X, Resolution.Y);
            output.Write(jpeg);
        }
    }
}
