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
}
}
