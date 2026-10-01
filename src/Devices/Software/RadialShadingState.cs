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
private sealed class RadialShadingState
{
    // Transform circle centres to user space; radii scale by the CTM's uniform
    // component (sqrt(|det|)), which is exact for rotation+uniform-scale CTMs
    // and a best-effort approximation for skewed ones — circles become ellipses
    // only under genuinely asymmetric scale, which real-world logo gradients
    // rarely use.
    public double[] ctm = null!;
    public double radiusScale;
    public double r0;
    public double r1;
    public double domLo;
    public double domHi;
    public double domLen;
    public bool extendBefore;
    public bool extendAfter;
    // Radial shading: for each user-space point p, find the largest t ∈ [0,1]
    // such that the point lies on circle(t) of centre
    // c(t) = c0 + t*(c1-c0), radius r(t) = r0 + t*(r1-r0). Solving the circle
    // equation reduces to a quadratic in t — standard closed-form approach
    // used by all PDF rasterisers.
    public double cdx;
    public double cdy;
    public double dr;
    public double[]? bboxLocal;
    public double[]? inv;
    public double invScale;
    public double mbLlx;
    public double mbLly;
    public byte alpha;
    public string csName = null!;
    public double[] input = null!;
    public RenderContext ctx = default!;
    public RadialShading radial = default!;
    public GraphicsState state = default!;
}
}
