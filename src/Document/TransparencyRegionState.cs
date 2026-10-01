using Aspose.Pdf.Core;
using Aspose.Pdf.Devices;

namespace Aspose.Pdf;

public sealed partial class Document
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class TransparencyRegionState
{
    public Aspose.Pdf.Core.PdfDictionary? resources;
    public byte[]? content;
    public List<double[]> contentRegions = null!;
    // Transparent paints inside Form XObjects (Illustrator/InDesign wrap page art
    // in forms whose OWN ExtGState carries the alpha) are collected by recursion;
    // their rewrites are DEFERRED until after the original-appearance render below,
    // or the render itself would miss the very paints being preserved.
    public List<(Aspose.Pdf.Core.PdfStream form, byte[] bytes)> formRewrites = null!;
    public byte[]? rewritten;
    // Region boxes plus, for a highlight annotation's region, the constant
    // colour its appearance multiplies over the page (baked into the crop
    // below — the raster is taken WITHOUT the appearance, so multiplying
    // reproduces the blend exactly).
    public List<(double[] Box, double[]? MulColor)> regions = null!;
    // Highlight annotations: flatten the /AP form into the content at the
    // annotation rectangle (it is drawn, then covered with the composite
    // raster) and delete the annotation.
    public System.Text.StringBuilder flattenOps = null!;
    public List<int> annotIndices = null!;
    public Aspose.Pdf.Core.PdfArray? annotsArr;
    // Content regions of one transparency scope merge into one composite;
    // highlight-annotation regions stay separate (their crop is multiplied
    // by the appearance colour).
    // Thousands of tiny regions (an Illustrator map's per-stroke paths) would
    // make the pairwise merge quadratic-slow AND stamp thousands of images —
    // collapse them to their common bounding box first: one composite carries
    // the same appearance.
    public int maxDiscreteRegions;
    // Render the ORIGINAL appearance (transparent paints intact, highlight
    // appearances NOT drawn — their multiply is baked into the crop) at the
    // nominal 300 dpi.
    public byte[] pixels = null!;
    public int pngW;
    public int pngH;
    public bool hasAlpha;
    // Render WITHOUT annotations: the device paints annotation appearances,
    // which would bake the (not yet correctly blended) highlight bar over
    // the text this pass is trying to preserve.
    public Aspose.Pdf.Core.PdfObject? savedAnnots;
    public Rectangle pageRect = null!;
    public double scalePx;
    public double scalePy;
    public System.Text.StringBuilder drawOps = null!;
    // Final content: suppressed transparent paints + flattened highlight
    // appearances + the opaque composites on top.
    public byte[] baseBytes = null!;
    public string tail = null!;
    public Page page = default!;
    public bool recolorConstantAlpha = false;
}
}
