using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Stamps;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public sealed partial class HeaderFooter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class StampImageState
{
    public byte[]? imgData;
    // SVG sources must be rasterised first — Page.AddImage only
    // accepts raster formats. The viewport size comes back with it so the
    // drawing can keep its aspect ratio inside the box below.
    public double svgViewW;
    public double svgViewH;
    public double imgW;
    public double imgH;
    // A VECTOR source keeps its viewport aspect ratio inside the declared
    // box (SVG's default `xMidYMid meet`): it is fitted, not stretched, and
    // centred on both axes — a 10:1 logo in a 50×50 box draws as a 50×5 band
    // halfway down it.
    public double boxW;
    public double boxH;
    public double boxOffX;
    public double boxOffY;
    // An image honours its own horizontal alignment inside the band
    // (the band's left margin, its right edge, or centred between them);
    // the band's own x is the fallback.
    public double imgRight;
    public double boxX;
    public double imgX;
    public Rectangle rect = null!;
}
}
