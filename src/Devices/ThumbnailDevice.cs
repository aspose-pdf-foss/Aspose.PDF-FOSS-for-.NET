using Aspose.Pdf.IO;

namespace Aspose.Pdf.Devices;

/// <summary>
/// Renders PDF document pages into thumbnail PNG images. The page is rendered at
/// the device resolution and resampled to the configured pixel size (same
/// fixed-size contract as <see cref="PngDevice"/> with explicit dimensions).
/// </summary>
public sealed class ThumbnailDevice : ImageDevice
{
    public ThumbnailDevice() : base() { }
    public ThumbnailDevice(Resolution resolution) : base(resolution) { }
    public ThumbnailDevice(int width, int height) : base(width, height) { }
    public ThumbnailDevice(int width, int height, Resolution resolution) : base(width, height, resolution) { }

    /// <summary>Construct sized to the given <paramref name="pageSize"/> at 150 DPI.</summary>
    public ThumbnailDevice(Aspose.Pdf.PageSize pageSize) : base(pageSize) { }

    /// <summary>Construct sized to <paramref name="pageSize"/> at the requested resolution.</summary>
    public ThumbnailDevice(Aspose.Pdf.PageSize pageSize, Resolution resolution) : base(pageSize, resolution) { }

    /// <inheritdoc />
    public override void Process(Page page, Stream output)
    {
        var rgba = RenderPage(page);
        var png = PngEncoder.Encode(rgba.Data, rgba.Width, rgba.Height, colorType: 6); // RGBA
        output.Write(png);
    }
}
