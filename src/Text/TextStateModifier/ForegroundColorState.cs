using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

internal sealed partial class TextStateModifier
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class ForegroundColorState
{
    public Dictionary<string, Aspose.Pdf.Core.PdfDictionary> fonts = null!;
    public IO.PdfLexer lexer = null!;
    public List<(Aspose.Pdf.IO.TokenKind kind, Aspose.Pdf.Core.PdfObject obj, int startPos, int endPos)> operands = null!;
    public Dictionary<int, string>? currentToUnicode;
    public string? currentFontName;
    public FontMetrics? currentMetrics;
    public double fontSize;
    public double charSpacing;
    public double wordSpacing;
    public double hScaling;
    // Raw components of the pending TJ array (strings + kern adjustments), kept
    // for the pen-advance computation below.
    public List<object>? tjItems;
    // CTM/TM tracking — same approach as TextReplacer.ReplaceInContentStream so
    // targetY scopes the color injection to the right text-showing op when the
    // same text occurs at multiple positions on the page.
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
    public double yTolerance;
    // Pen X in text space: the line matrix origin (tmTx) plus the glyph advances
    // of the show operators already drawn on the line. tmTx itself stays the LINE
    // matrix (Td/TD/Tm/T* semantics unchanged); penTx is what a show operator's
    // real start X is, so X-scoping can tell apart same-text runs on one line.
    public double penTx;
    // Track the active fill colour so a substring recolour can restore the surrounding
    // glyphs to whatever colour was in effect (default black) when splitting a run.
    public double fillR;
    public double fillG;
    public double fillB;
    // ...and the VERBATIM source text of the operator that set it, so the restore
    // re-emits the producer's own form (`0 0 0 rg` stays `0 0 0 rg`, never
    // collapsing to `0 g`). Null until a fill-colour operator has been seen.
    public string? fillOpText;
    // Active text rendering mode (Tr). A replacement carrying a TextState writes
    // its own mode before the run and restores this one after it.
    public int trMode;
    // X scoping (same formula/tolerance as TextReplacer.IsAtTargetX): lets a
    // short segment (e.g. a lone space) recolour ITS OWN show operator instead
    // of the first operator on the line whose decoded text merely contains it.
    public double xTolerance;
    // Nearest-X fallback state: the best candidate rewrite seen so far and how far
    // its occurrence sits from the recorded X.
    public byte[]? bestResult;
    public double bestGap;
    // Distance from targetX of the occurrence PickOccurrence last chose; the
    // nearest-X fallback ranks candidate operators by it.
    public double lastOccurrenceGap;
    /// <summary>The call's inputs the helpers read: the text sought, its target position and the nearest-X mode.</summary>
    public string text = null!;
    public double? targetY;
    public double? targetX;
    public bool nearestX;
}
}
