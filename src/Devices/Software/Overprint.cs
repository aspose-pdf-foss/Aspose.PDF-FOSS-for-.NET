using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Devices;

public sealed partial class SoftwarePageRenderer
{
    private const string MultiplyBlend = "Multiply";

    /// <summary>
    /// The blend a vector paint composites with: Multiply for an overprinted spot colour
    /// (see <see cref="IsOverprintedSpot"/>), the state's blend mode for every other paint.
    /// </summary>
    private static string PaintBlend(RenderContext ctx, string blendMode, bool overprint, string? colorSpace) =>
        blendMode == "Normal" && IsOverprintedSpot(ctx.Reader, ctx.ColorSpaces, overprint, colorSpace)
            ? MultiplyBlend : blendMode;

    /// <summary>
    /// True for a paint in a /Separation or /DeviceN space under op / OP (PDF 32000 §8.6.7):
    /// a spot ink laid over the process inks beneath, not a knockout of them. Both renderers
    /// approximate it by a Multiply, as their image paths already do, so the artwork under a
    /// varnish or a pale spot layer stays visible.
    /// </summary>
    internal static bool IsOverprintedSpot(PdfReader reader, PdfDictionary? colorSpaces, bool overprint, string? colorSpace)
    {
        if (!overprint || colorSpace is null) return false;
        var family = colorSpace is "Separation" or "DeviceN"
            ? colorSpace
            : ((reader.Resolve(colorSpaces?.Get(colorSpace)) as PdfArray) is { Count: > 0 } array
                ? (reader.Resolve(array[0]) as PdfName)?.Value : null);
        return family is "Separation" or "DeviceN";
    }
}
