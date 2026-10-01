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
private sealed class DrawPathState
{
    public bool doFill;
    public bool doStroke;
    public bool evenOdd;
    public double[] ctm = null!;
    // One builder for the fill, the clip and nothing else: this used to be an inline
    // copy of BuildPathEdgeTable and the two had already drifted apart, which is exactly
    // what the shared helper exists to prevent.
    public Devices.Rasterizer.EdgeTable edgeTable = null!;
    // Any active W/W* clip constrains the fill / stroke to its stencil.
    public byte[]? clip;
    public RenderContext ctx = default!;
    public IReadOnlyList<PathCommand> segments = default!;
    public string op = default!;
    public GraphicsState state = default!;
    public byte r;
    public byte g;
    public byte b;
    // Line width `w` is in user space (PDF 32000 §8.4.3.2). It must be transformed
    // through the CTM into device space before converting to pixels. Using ctx.Scale
    // alone ignores the CTM and produces grossly thick strokes on content streams
    // with sub-unit `cm` scaling (e.g. ACORD forms with `0.12 cm` → 8× too thick).
    public double ctmScale;
    public double lw;
    // The stroke's constant alpha (ExtGState /CA) as the emitter's byte alpha.
    public byte strokeA;
    // Dash pattern (PDF 32000 §8.4.3.6). The array is in USER units; each element
    // is clamped to a minimum of the LINE WIDTH before scaling - the measured
    // dash law (rendered
    // pattern[i] = max(dashArray[i], lineWidth), zero elements included, so [0 3]
    // draws as [3 3], not as vanishing dots). The GDI+ renderer has carried this
    // for a while; the software stroke path drew every dashed trail SOLID - a
    // map of dotted footpaths came out as unbroken lines.
    public double[]? dashPx;
    public bool dashOn;
    public int dashIdx;
    public double dashPos;
    // Stroke each line segment of the path
    public double sx;
    public double sy;
    public double stX;
    public double stY;
}
}
