using System.IO;
using Aspose.Pdf.Converters;
using Aspose.Pdf.Devices;

namespace Aspose.Pdf;

/// <summary>
/// Converts a vector image source (SVG) into raster bytes so it can be embedded
/// through <see cref="Page.AddImage(byte[], Rectangle)"/>, which only accepts
/// raster formats. The SVG is converted to a one-page <see cref="Document"/>
/// sized to the SVG viewport, then that page is rendered to a PNG.
/// </summary>
internal static class ImageRasterizer
{
    /// <summary>Render DPI for the rasterised SVG (2× the 72-dpi base for crispness).</summary>
    private const int RasterDpi = 144;

    /// <summary>Rasterise SVG bytes to a PNG, or null if conversion fails.</summary>
    public static byte[]? RasterizeSvg(byte[] svgData)
    {
        try
        {
            var doc = SvgToPdfConverter.Convert(svgData);
            if (doc.Pages.Count == 0) return null;
            using var ms = new MemoryStream();
            var device = new PngDevice(new Resolution(RasterDpi));
            device.Process(doc.Pages[1], ms);
            var bytes = ms.ToArray();
            return bytes.Length > 0 ? bytes : null;
        }
        catch
        {
            return null;
        }
    }
}
