using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TableAbsorber
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class TableLinesState
{
    public IO.PdfLexer lexer = null!;
    public List<Aspose.Pdf.Core.PdfObject> operands = null!;
    public Dictionary<int, string>? toUnicode;
    public PdfDictionary? fontDict;
    public FontMetrics? curMetrics;
    public double fontSize;
    public double tx;
    public double ty;
    public double txLine;
    public double tyLine;
    public double tmA;
    public double tmB;
    public double tmC;
    public double tmD;
    public double leading;
    public double curX;
    public double curY;
    public double moveX;
    public double moveY;
    // The live CTM and the q/Q stack it is pushed onto and popped from.
    public double ctmA;
    public double ctmB;
    public double ctmC;
    public double ctmD;
    public double ctmE;
    public double ctmF;
    public Stack<(double a, double b, double c, double d, double e, double f)> ctmStack = null!;
    // Buffer path segments until a paint operator finalizes them
    public List<Aspose.Pdf.Text.TableAbsorber.PendingLine> pendingLines = null!;
    public List<Aspose.Pdf.Text.TableAbsorber.PendingRect> pendingRects = null!;
    // What decides whether a painted path is a rule one can see: the path holds a
    // curve, the fill colour is the paper's, and the backgrounds painted so far.
    public bool pathHasCurve;
    public bool fillIsPaper;
    public bool fillSpaceIsDevice = true;
    public bool strokeIsPaper;
    // The resource colour spaces the stream names, to tell an ICC-based space from others.
    public PdfDictionary? colourSpaces;
    public PdfReader? reader;
    public bool strokeSpaceIsDevice = true;
    public Stack<(bool paper, bool device, bool strokePaper, bool strokeDevice)> fillStack = new();
    public List<Aspose.Pdf.Text.TableAbsorber.PendingRect> backgrounds = new();
    // The strokes of drawings: slanting segments and curve ends, for the figure test.
    public List<Aspose.Pdf.Text.TableAbsorber.PendingLine>? drawn;
    public List<Aspose.Pdf.Text.TableAbsorber.PendingLine> curveEnds = new();
}
}
