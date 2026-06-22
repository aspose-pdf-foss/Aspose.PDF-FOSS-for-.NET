using Aspose.Pdf.IO;

namespace Aspose.Pdf.Devices;

/// <summary>
/// Renders PDF document pages into PNG image format.
/// </summary>
public sealed class PngDevice : ImageDevice
{
    public PngDevice(IPageRenderer renderer) : base(renderer) { }
    public PngDevice(IPageRenderer renderer, Resolution resolution) : base(renderer, resolution) { }
    public PngDevice() : base() { }
    public PngDevice(Resolution resolution) : base(resolution) { }
    public PngDevice(int width, int height) : base(width, height) { }
    public PngDevice(int width, int height, Resolution resolution) : base(width, height, resolution) { }

    /// <summary>Construct sized to the given <paramref name="pageSize"/> at 150 DPI.</summary>
    public PngDevice(Aspose.Pdf.PageSize pageSize) : base(pageSize) { }

    /// <summary>Construct sized to <paramref name="pageSize"/> at the requested resolution.</summary>
    public PngDevice(Aspose.Pdf.PageSize pageSize, Resolution resolution) : base(pageSize, resolution) { }

    /// <summary>When true, the rendered PNG keeps the page background transparent. Stored only.</summary>
    public bool TransparentBackground { get; set; }

    /// <inheritdoc />
    public override void Process(Page page, Stream output)
    {
        var rgba = RenderPage(page);
        var png = PngEncoder.Encode(rgba.Data, rgba.Width, rgba.Height, colorType: 6); // RGBA
        output.Write(png);
    }
}
