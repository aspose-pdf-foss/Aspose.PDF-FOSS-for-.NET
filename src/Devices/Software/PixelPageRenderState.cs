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
private sealed class PixelPageRenderState
{
    public IO.PdfReader reader = null!;
    public Rectangle rawMb = null!;
    // The visible region is the crop box clipped to the media box; content is
    // sized and offset to it so anything outside the crop area is excluded.
    public Rectangle crop = null!;
    // PDF 32000 §14.8.2.7 — /Rotate (0/90/180/270, clockwise) defines how the
    // page is displayed. The content stream is authored in the unrotated
    // coordinate system; we have to compose the rotation into the initial CTM
    // so glyphs/images/paths land on the (rotated) visible canvas. Otherwise
    // a 90°-Rotate landscape page draws as portrait content shoved into the
    // left half of a landscape canvas (the symptom seen on a
    // facility-plan diagram).
    public int rot;
    public Aspose.Pdf.Rectangle effectiveMb = null!;
    public double[]? initialPageCtm;
    // Uniform scale: caller is expected to pick pixelW/pixelH with the visible box's
    // own aspect ratio. When they don't match exactly, X scale wins (height drifts
    // ±1px which is swallowed by the comparison tolerance anyway).
    public double scale;
    // A caller CAN pin a target size that is NOT the page's aspect - SaveAsTIFF takes an
    // explicit width and height - and then the page is STRETCHED to fill it, which is
    // what the GDI+ renderer's independent _scaleY does. Rather than thread a second
    // scale through every blit, shading and glyph placement, stretch the page's own
    // coordinate system by the ratio of the two scales and grow the device box to
    // match: a uniform scale over a k-times-taller box is the same device transform.
    // Without it a page pinned to 1000x2000 rendered at its own 1000x1294 and sat in
    // the bottom of the canvas. Only a real mismatch is corrected, so a size derived
    // from the page at some DPI (where the ratio is a rounding artefact) is untouched
    // and its AA-calibrated bilevel output stays exact.
    public double yFit;
    public byte[] pixels = null!;
    // Resolve page resources, walking up the Pages tree if the page itself omits
    // /Resources. PDF 32000 §7.7.3.4 makes /Resources an inheritable attribute —
    // many real PDFs list only /Group + /MediaBox + /Contents on the page and put
    // patterns / XObjects on the parent /Pages dict. Without inheritance, every
    // "/P1 scn" / "/X1 Do" resolves to nothing and the page renders blank.
    public Aspose.Pdf.Core.PdfDictionary? resources;
    public Dictionary<string, Aspose.Pdf.Core.PdfDictionary>? extGStates;
    public Dictionary<string, Aspose.Pdf.Core.PdfDictionary> fontDicts = null!;
    public Dictionary<string, Aspose.Pdf.Core.PdfStream> allXObjects = null!;
    // Parse and render content stream
    public byte[] contentBytes = null!;
    public Page page = default!;
    // The objects resolved before the render began: they outlive its cache clear.
    public HashSet<(int objNum, int gen)> cachedBefore = null!;
    public int pixelW = 0;
    public int pixelH = 0;
    public double? xScale = null;
    public double? yScale = null;
}
}
