using Aspose.Pdf.IO;

namespace Aspose.Pdf.Devices;

/// <summary>
/// Renders PDF document pages into PNG image format.
/// </summary>
public sealed class PngDevice : ImageDevice
{
    /// <summary>Creates a PngDevice that renders pages with the given <c>renderer</c> at the default resolution of 150 DPI.</summary>
    public PngDevice(IPageRenderer renderer) : base(renderer) { }
    /// <summary>Creates a PngDevice that renders pages with the given <c>renderer</c> at the given resolution (150 DPI when <c>resolution</c> is null).</summary>
    public PngDevice(IPageRenderer renderer, Resolution resolution) : base(renderer, resolution) { }
    /// <summary>Creates a PngDevice that renders pages with the built-in renderer at the default resolution of 150 DPI.</summary>
    public PngDevice() : base() { }
    /// <summary>Creates a PngDevice that renders pages with the built-in renderer at the given resolution.</summary>
    public PngDevice(Resolution resolution) : base(resolution) { }
    /// <summary>Creates a PngDevice whose output image is resampled to the given width and height in pixels; pages are rendered at 150 DPI.</summary>
    public PngDevice(int width, int height) : base(width, height) { }
    /// <summary>Creates a PngDevice that renders pages at the given resolution and resamples the output image to the given width and height in pixels.</summary>
    public PngDevice(int width, int height, Resolution resolution) : base(width, height, resolution) { }

    /// <summary>Construct sized to the given <paramref name="pageSize"/> at 150 DPI.</summary>
    public PngDevice(Aspose.Pdf.PageSize pageSize) : base(pageSize) { }

    /// <summary>Construct sized to <paramref name="pageSize"/> at the requested resolution.</summary>
    public PngDevice(Aspose.Pdf.PageSize pageSize, Resolution resolution) : base(pageSize, resolution) { }

    /// <summary>When true, the rendered PNG keeps the page background transparent: bare
    /// paper is written as transparent black (0, 0, 0, 0), as the reference writes it,
    /// and only the ink carries colour and coverage.</summary>
    public bool TransparentBackground { get; set; }

    /// <inheritdoc />
    public override void Process(Page page, Stream output)
    {
        var rgba = RenderPage(page, TransparentBackground);
        var png = PngEncoder.Encode(rgba.Data, rgba.Width, rgba.Height, colorType: 6); // RGBA
        output.Write(png);
    }
}
