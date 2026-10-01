using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TableAbsorber
{
/// <summary>The working set of one content-stream filter pass: the graphics and text
/// state the walk tracks so path and text coordinates map to page space, the path and
/// text object being built, and the byte ranges found to remove. One instance per
/// invocation; never shared.</summary>
private sealed class ContentFilterState
{
    public PdfLexer lexer = null!;
    public List<PdfObject> operands = null!;
    public List<(int start, int end)> removals = null!;
    // CTM state - initialized to the page rotation matrix
    public Stack<(double a, double b, double c, double d, double e, double f)> ctmStack = null!;
    public double ctmA;
    public double ctmB;
    public double ctmC;
    public double ctmD;
    public double ctmE;
    public double ctmF;
    // Path construction state - track byte offset of first path operator and all points
    public int pathStart;
    public List<(double x, double y)> pathPoints = null!;
    // Text block state - track BT offset, text positions, and text matrix components
    public int btStart;
    public List<(double x, double y)> textPoints = null!;
    public double tx;
    public double ty;
    public double txLine;
    public double tyLine;
    public double tmA;
    public double tmB;
    public double tmC;
    public double tmD;
    public double leading;
    // The filter inputs, captured from the method parameters.
    public Rectangle tableRect = null!;
    public bool wholeBlocksOnly;
    public bool textOnly;
    public bool decorationOnly;
}
}
