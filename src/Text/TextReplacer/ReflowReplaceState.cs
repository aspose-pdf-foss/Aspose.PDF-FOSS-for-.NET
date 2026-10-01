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
private sealed class ReflowReplaceState
{
    public int countBefore;
    /// <summary>Whether the walk met an operator the replacer can act on - a text-showing
    /// operator, or a Do into a form that may hold one.</summary>
    public bool sawText;
    public Dictionary<string, Aspose.Pdf.Core.PdfDictionary> fonts = null!;
    public string normalizedSearch = null!;
    public IO.PdfLexer lexer = null!;
    public System.IO.MemoryStream result = null!;
    public List<(Aspose.Pdf.IO.TokenKind kind, Aspose.Pdf.Core.PdfObject obj, int startPos, int endPos)> operands = null!;
    public string? currentFontName;
    public Dictionary<int, string>? currentToUnicode;
    public PdfDictionary? currentFontDict;
    public double currentFontSize;
    public int lastWritePos;
    // CTM (current transformation matrix) and TM (text matrix) tracking. Both
    // are 6-element matrices [a b c d tx ty]. CTM accumulates from `cm`
    // operators, push/pop on `q`/`Q`. TM is only meaningful inside BT/ET;
    // reset on BT, mutated by Td/TD/T*/Tm. Td translates in TEXT SPACE so
    // the dy from Td maps to ty += dy * tm.d (for axis-aligned Tm; full
    // matrix math handles rotation/skew correctly via tm composition).
    // Together CTM and TM let TargetY scope a per-fragment replace to the
    // right text-showing operator (page-space Y ≈ ctm.d × tm.ty + ctm.ty).
    public double ctmA;
    public double ctmB;
    public double ctmC;
    public double ctmD;
    public double ctmTx;
    public double ctmTy;
    public Stack<(double, double, double, double, double, double)> ctmStack = null!;
    public double tmA;
    public double tmB;
    public double tmC;
    public double tmD;
    public double tmTx;
    public double tmTy;
    public double tlLeading;
    // Text render mode (Tr), tracked for RequiredRenderMode scoping. Part of
    // the graphics state, so it saves/restores with q/Q.
    public int renderMode;
    public Stack<int> trStack = null!;
    // Character (Tc) and word (Tw) spacing, tracked so the anchored TJ/Tj
    // splits can reproduce the original pen advance of a partially-kept
    // run (both are per-glyph contributions the font metrics don't know).
    // Text-state parameters, so they save/restore with q/Q.
    public double tcSpacing;
    public double twSpacing;
    public Stack<(double tc, double tw)> spacingStack = null!;
    public byte[] output = null!;
}
}
