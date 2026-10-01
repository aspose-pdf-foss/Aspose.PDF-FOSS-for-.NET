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
private sealed class AxialShadingState
{
    // The shading's two axis endpoints live in shading-local coordinates; the CTM
    // at the moment of `sh` maps them into user space (§8.7.4.3).
    public double[] ctm = null!;
    public double dx;
    public double dy;
    public double denom;
    public double domLo;
    public double domHi;
    public double domLen;
    public bool extendBefore;
    public bool extendAfter;
    // PDF 32000 §8.7.4.5.2: a shading's optional /BBox is its bounding box in
    // shading-local coordinates (before CTM). The shading "need not be applied
    // outside that rectangle". Without this, a Form XObject wrapping a small
    // axial gradient (e.g. a thin footer stripe) instead floods the entire
    // Form BBox / page clip, covering everything previously drawn. For
    // arbitrary CTMs we pre-compute the inverse and test each pixel in
    // shading-local space.
    public double[]? bboxLocal;
    public double[]? inv;
    public double invScale;
    public double mbLlx;
    public double mbLly;
    public byte alpha;
    public string csName = null!;
    public double[] input = null!;
    // A bare `sh` in a MULTI-SPOT ink space (DeviceN, or another tint space resolving
    // to CMYK) is ink laid over the page - a `sh` vignette painted across a photo, say.
    // Composite it with an overprint Multiply so its no-ink end (which converts to
    // white) leaves the content beneath unchanged instead of knocking it out; over bare
    // paper Multiply equals an opaque paint. A SPOT-colour (/Separation) shading is the
    // opposite case - a decorative panel whose plate replaces what sits under it - and
    // plain process-CMYK is opaque paint too, so both keep the straight paint. This is
    // the GDI+ renderer's rule (DrawAxialShading/MultiplyBrushFill); the two rasterisers
    // have to agree, and without it a DeviceN vignette wiped out the photo under it.
    public bool subtractive;
    // Like GDI+, the multiply only stands in for the plain paint: an explicit blend
    // mode or a soft mask already carries its own compositing and wins.
    public string savedBlend = null!;
    public RenderContext ctx = default!;
    public AxialShading axial = default!;
    public GraphicsState state = default!;
    public bool bareSh = false;
}
}
