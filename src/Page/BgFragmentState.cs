using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public sealed partial class Page
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class BgFragmentState
{
    public Content.ContentStreamBuilder builder = null!;
    public Color? fragBg;
    // The run's own transform context, shared by the fragment- and
    // segment-level emitters below.
    public Matrix? ctm;
    public double ctmScaleX;
    public bool hasCtm;
    public bool ctmNonIdentity;
    // An axis-aligned translated/scaled frame — including a y-DOWN
    // (flipped, D < 0) one, where the local rect anchors one height below
    // the inverse-mapped page bottom edge and the flip renders it back.
    public Matrix? frame;
    // A quarter-turn frame (axis-swapping, |B|,|C| carry the scale — the
    // page-rotation composition for /Rotate content).
    public bool quarterTurn;
    // Segment-level: collect segments with their own bg colour
    public List<Aspose.Pdf.Text.TextSegment> segList = null!;
    // Merge consecutive segments with the same font size into single rectangles.
    // The the public API emits one rect per font-size group on the same line.
    public int si;
}
}
