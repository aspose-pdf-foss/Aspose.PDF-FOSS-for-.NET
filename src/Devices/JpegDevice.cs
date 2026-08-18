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

    public JpegDevice(IPageRenderer renderer) : base(renderer) { Quality = 100; }
    public JpegDevice(IPageRenderer renderer, Resolution resolution) : base(renderer, resolution) { Quality = 100; }
    public JpegDevice(IPageRenderer renderer, Resolution resolution, int quality) : base(renderer, resolution) { Quality = quality; }
    public JpegDevice() : base() { Quality = 100; }
    public JpegDevice(Resolution resolution) : base(resolution) { Quality = 100; }
    public JpegDevice(Resolution resolution, int quality) : base(resolution) { Quality = quality; }
    public JpegDevice(int quality) : base() { Quality = quality; }
    public JpegDevice(int width, int height) : base(width, height) { Quality = 100; }
    public JpegDevice(int width, int height, Resolution resolution) : base(width, height, resolution) { Quality = 100; }
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

        if (_encoder is not null)
        {
            var jpeg = _encoder(rgba.Data, rgba.Width, rgba.Height, Quality);
            output.Write(jpeg);
        }
        else if (System.OperatingSystem.IsWindows())
        {
            // The platform (GDI+) codec, not the managed encoder: rendered-page JPEG
            // consumers compare against outputs produced by this codec, and the managed
            // encoder's chroma subsampling shifts colours at hard edges well past
            // typical comparison tolerances.
            WriteGdiPlusJpeg(rgba, output);
        }
        else
        {
            // Use built-in JPEG encoder. Propagate DPI so readers don't fall back
            // to the JFIF unit-less default (which most consumers report as 96 DPI).
            var jpeg = IO.JpegEncoderImpl.Encode(rgba.Data, rgba.Width, rgba.Height, Quality, Resolution.X, Resolution.Y);
            output.Write(jpeg);
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private void WriteGdiPlusJpeg(RgbaBuffer rgba, Stream output)
    {
        int w = rgba.Width, h = rgba.Height;
        using var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        var bd = bmp.LockBits(new System.Drawing.Rectangle(0, 0, w, h),
            System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        try
        {
            // RGBA → BGRX rows (alpha dropped; JPEG is opaque, matching the managed encoder).
            var row = new byte[w * 4];
            var src = rgba.Data;
            for (int y = 0; y < h; y++)
            {
                int si = y * w * 4;
                for (int x = 0; x < w; x++)
                {
                    int o = x * 4;
                    row[o] = src[si + o + 2];
                    row[o + 1] = src[si + o + 1];
                    row[o + 2] = src[si + o];
                    row[o + 3] = 255;
                }
                System.Runtime.InteropServices.Marshal.Copy(row, 0, bd.Scan0 + y * bd.Stride, w * 4);
            }
        }
        finally { bmp.UnlockBits(bd); }
        bmp.SetResolution(Resolution.X, Resolution.Y);

        System.Drawing.Imaging.ImageCodecInfo? codec = null;
        foreach (var c in System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders())
            if (c.FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid) { codec = c; break; }
        if (codec is null)
        {
            var jpeg = IO.JpegEncoderImpl.Encode(rgba.Data, w, h, Quality, Resolution.X, Resolution.Y);
            output.Write(jpeg);
            return;
        }
        using var ep = new System.Drawing.Imaging.EncoderParameters(1);
        ep.Param[0] = new System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)Quality);
        // GDI+ needs a seekable stream; buffer through memory for arbitrary outputs.
        using var ms = new MemoryStream();
        bmp.Save(ms, codec, ep);
        ms.Position = 0;
        ms.CopyTo(output);
    }
}

