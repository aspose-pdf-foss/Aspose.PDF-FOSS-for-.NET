using System.Collections.Generic;
using Aspose.Pdf.Operators;

namespace Aspose.Pdf.Vector;

public sealed partial class GraphicsAbsorber
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class GraphicsWalkState
{
    public Vector.GraphicsAbsorber.WalkState gs = null!;
    public Stack<Aspose.Pdf.Vector.GraphicsAbsorber.WalkState> stack = null!;
    // Sub-paths constructed for the current (not-yet-painted) path, each with
    // the operator index span of its construction ops.
    public List<(Aspose.Pdf.Matrix ctm, List<Aspose.Pdf.Operator> ops, double minX, double minY, double maxX, double maxY, int startIdx, int endIdx)> subpaths = null!;
    public List<Aspose.Pdf.Operator>? curOps;
    public Aspose.Pdf.Matrix? curCtm;
    public double minX;
    public double minY;
    public double maxX;
    public double maxY;
    public bool any;
    public double curX;
    public double curY;
    public int curStart;
    public int curEnd;
    // A W/W* seen for the current path: the clip activates at the path-ending
    // op (n or a paint) and stays in force until the enclosing Q.
    public GraphicsClipInfo? pendingClip;
    // The same W/W* as the region the paint that follows the path-ending op is clipped to.
    public ClipRegion? pendingRegion;
    public IReadOnlyList<Aspose.Pdf.Operator> ops = default!;
    public Core.PdfDictionary? resources = null;
    public IO.PdfReader reader = default!;
    public GraphicElementCollection elements = default!;
    public int depth = 0;
    public GraphicsEditState? editState = null;
    public Aspose.Pdf.Matrix? baseCtm = null;
}
}
