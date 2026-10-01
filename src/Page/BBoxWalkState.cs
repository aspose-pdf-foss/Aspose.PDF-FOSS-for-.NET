using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public sealed partial class Page
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class BBoxWalkState
{
    public IO.PdfLexer lexer = null!;
    public List<Aspose.Pdf.Core.PdfObject> operands = null!;
    public Page.Cm ctm;
    public Stack<Aspose.Pdf.Page.Cm> ctmStack = null!;
    // Active clip in page space — the bbox of the union of clip paths
    // accumulated by W/W* operators. Painted content is intersected with
    // this on emit. Saved/restored by q/Q (graphics state).
    public double clipMinX;
    public double clipMinY;
    public double clipMaxX;
    public double clipMaxY;
    public Stack<(double, double, double, double)> clipStack = null!;
    // Current path bbox (user-space pre-CTM, so we can apply the active CTM
    // at paint time). Reset on n/S/s/f/F/f*/B/B*/b/b*.
    public bool pathStarted;
    public double pminX;
    public double pminY;
    public double pmaxX;
    public double pmaxY;
    public double curX;
    public double curY;
    public bool inText;
    public bool clipPending;
}
}
