namespace Aspose.Pdf.Devices;

public sealed partial class SoftwarePageRenderer
{
    /// <summary>What the switch answers when the environment does not: the pixel-centre
    /// convention puts an integer device coordinate at the centre of its pixel, the edge
    /// convention at the pixel's top-left corner.</summary>
    private const bool PixelCentreOriginDefault = false;

    /// <summary>Switch of the device convention, read once: <c>ASPOSE_PDF_DEVICE_ORIGIN</c>
    /// set to <c>centre</c> or <c>edge</c> overrides <see cref="PixelCentreOriginDefault"/>,
    /// so the two can be compared over a corpus without a rebuild; a test sets it for the
    /// span of its render. Under the edge convention every fill, image and glyph of a page
    /// sits half a pixel left and up of where a renderer that reads integer coordinates as
    /// pixel centres puts it - which is where the render templates of this library's
    /// comparisons have their ink.</summary>
    internal static bool PixelCentreOrigin { get; set; } =
        Environment.GetEnvironmentVariable("ASPOSE_PDF_DEVICE_ORIGIN") switch
        {
            "centre" => true,
            "edge" => false,
            _ => PixelCentreOriginDefault,
        };

    /// <summary>How far, in device pixels, the page's content sits right and down of the
    /// edge convention's place: half a pixel under the pixel-centre convention, nothing
    /// under the edge one. A region that reaches the canvas's edges does so at this offset.</summary>
    private static double DeviceOriginOffset => PixelCentreOrigin ? HalfPixel : 0;

    /// <summary>The device box of the page under the pixel-centre convention: the box the
    /// device maps to the canvas, moved half a pixel left and up in page units, so every
    /// mapping through it lands half a pixel right and down. One translation here reaches
    /// every path, image, glyph, shading, annotation and soft mask of the render, since
    /// they all map through the context's box, and the page's size in pixels is untouched.</summary>
    private static Rectangle PixelCentreBox(Rectangle box, double scale)
    {
        var half = HalfPixel / scale;
        return new Rectangle(box.LLX - half, box.LLY + half, box.URX - half, box.URY + half);
    }
}
