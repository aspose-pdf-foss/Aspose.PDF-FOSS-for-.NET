using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextReplacer
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class CollectTextOpsState
{
    public List<Aspose.Pdf.Text.TextReplacer.CrossTextOp> textOps = null!;
    public IO.PdfLexer lexer2 = null!;
    public List<(Aspose.Pdf.IO.TokenKind kind, Aspose.Pdf.Core.PdfObject obj, int startPos, int endPos)> ops2 = null!;
    public Dictionary<int, string>? curToUnicode;
    public PdfDictionary? curFontDict;
    public string? curFontName;
    public double curFontSize;
    public double curTc;
    public int curBtStart;
    public double tmA;
    public double tmB;
    public double tmC;
    public double tmD;
    public double tmTx;
    public double tmTy;
    public double tlLeading;
    // The LINE matrix's translation. Td/TD/T*/' move the LINE, and the text matrix is then
    // reset to it; only showing text moves the text matrix away from the line. Tracking one
    // translation for both is right only while shows never advance it — once they do, a Td
    // measured from the advanced position doubles the run's width into the next line.
    public double tlmTx;
    public double tlmTy;
    // Seed the CTM with the caller's context (the Do-site CTM when this stream is a
    // recursed Form XObject) so TargetY/TargetX scoping sees page-space positions.
    public double ctmA;
    public double ctmB;
    public double ctmC;
    public double ctmD;
    public double ctmTx;
    public double ctmTy;
    public Stack<(double, double, double, double, double, double)> ctmStack = null!;
    public Stack<(double size, string? name, Aspose.Pdf.Core.PdfDictionary? dict, Dictionary<int, string>? toUni, double tc, double tw, double leading)> tsStack = null!;
    public double curTw;
    // Pending positioning-Tm record, consumed by the next text-showing op.
    public (bool has, int xStart, int xEnd, double xVal) pendingTm;
    public (bool has, int xStart, int xEnd, double xVal) pendingTd;
    // Showing text ADVANCES the text matrix by what it drew (PDF 32000-1 §9.4.4). Without
    // that, consecutive shows inside one BT block all report the first one's origin: a
    // producer that lays a line out as `(word) Tj (word) Tj …`, advancing on the glyphs
    // rather than re-positioning, gives every run of the line the same X. Every consumer
    // that asks where a run sits then reads the line's start for all of them.
    public Dictionary<Aspose.Pdf.Core.PdfDictionary, Aspose.Pdf.Text.FontMetrics?> metricsByDict = null!;
    public byte[] streamBytes = default!;
    public Dictionary<string, PdfDictionary> fonts = default!;
    public PdfReader reader = default!;
    public double initCtmA = 0;
    public double initCtmB = 0;
    public double initCtmC = 0;
    public double initCtmD = 0;
    public double initCtmTx = 0;
    public double initCtmTy = 0;
}
}
