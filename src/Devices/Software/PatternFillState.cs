using System.Runtime.InteropServices;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Devices.Rasterizer;
using Aspose.Pdf.IO;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Devices;

public sealed partial class SoftwarePageRenderer
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class PatternFillState
{
    // Tiling patterns (PatternType 1) are streams (the tile content); shading
    // patterns (PatternType 2) are plain dicts that reference a /Shading. Resolve
    // both shapes so the patternType branch below picks the right path.
    public PdfStream? patternStream;
    public Aspose.Pdf.Core.PdfDictionary? pdict;
    public int patternType;
    // Build the clipping stencil from the filled path. Cheap — one pass over the
    // same edge table the solid-fill path uses, writing 0/255 instead of RGBA.
    // When an outer clip is active (e.g. an enclosing W/W*), AND it in so the
    // pattern fill stays within both the path and the outer clip.
    public byte[] mask = null!;
    public byte[] patternContent = null!;
    // Pattern's Matrix maps pattern space → user space (PDF 32000 §8.7.3.3).
    public Aspose.Pdf.Core.PdfArray? patMatrix;
    public double[] m = null!;
    // XStep/YStep drive the tile repetition grid in pattern space.
    public double xStep;
    public double yStep;
    // Resolve the pattern's own /Resources so Image Do, font lookups etc. inside the
    // pattern content stream find the right objects. Fall back to the page's resources
    // so tiling patterns that reference outer fonts/images still work.
    public Aspose.Pdf.Core.PdfDictionary? patResources;
    public Dictionary<string, Aspose.Pdf.Core.PdfDictionary> patFonts = null!;
    public Dictionary<string, Aspose.Pdf.Core.PdfDictionary>? patExtG;
    public Dictionary<string, Aspose.Pdf.Core.PdfStream> patXObj = null!;
    // Tile iteration: find which (i, j) tiles cover the filled region in pattern space,
    // then render the pattern content once per tile with its origin offset by
    // (i*XStep, j*YStep). The PDF spec describes the pattern cell as tiling at these
    // steps (§8.7.3.3) — a real-world PDF may place pattern (0,0) outside the
    // clipped region and rely on tile (0,-1) or similar to cover it.
    // The cell's /BBox is what a tile actually paints, and it need not sit at the
    // pattern origin: an SVG pattern in objectBoundingBox units converts to a cell
    // whose BBox starts 100 units out, and one whose BBox is WIDER than its step so
    // the tiles overlap. Deriving the index range from the region alone assumed a cell
    // at the origin no bigger than its step, and every index it produced painted
    // outside the filled square - the whole pattern came out blank.
    public double[]? cellBBox;
    public RenderContext ctx = default!;
    public EdgeTable edgeTable = default!;
    public bool evenOdd = false;
    public string patternName = default!;
    public GraphicsState state = default!;
}
}
