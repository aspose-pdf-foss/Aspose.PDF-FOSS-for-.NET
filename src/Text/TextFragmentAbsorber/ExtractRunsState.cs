using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextFragmentAbsorber
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class ExtractRunsState
{
    public Dictionary<string, PdfDictionary> fonts = null!;
    // A Type3 font carries no /BaseFont; the FontCollection surfaces a synthesised
    // "T3Font_<n>" handle indexed by /Font enumeration order. Mirror that here so a
    // fragment's TextState.Font.FontName reports the same handle (else it falls back
    // to the "Unknown" BaseFont). Keyed by resource name, same order as the collection.
    public Dictionary<string, string> t3Names = null!;
    public IO.PdfLexer lexer = null!;
    public List<PdfObject> operands = null!;
    public string? currentFontName;
    public string? currentFontNameForGuard;
    // A page-level Tf named a font key the Resources hierarchy does not carry:
    // the text it governs cannot be decoded and produces no runs (the miss is
    // reported through missingFontKeys). Form resources are resolved from the
    // form dict alone, so the guard stays page-level.
    public bool currentFontMissing;
    public Dictionary<int, string>? toUnicode;
    public PdfDictionary? fontDict;
    public FontMetrics? metrics;
    public Font? currentFontInfo;
    public double fontSize;
    public double tx;
    public double ty;
    public double txLine;
    public double tyLine;
    public double leading;
    public double charSpacing;
    public double tmBaseTy;
    public double wordSpacing;
    public double hScaling;
    public double textRise;
    // Text matrix components (a, b, c, d) — updated by Tm.
    // Needed to correctly scale Td/TD/T* displacements (values are in unscaled text space).
    public double tmA;
    public double tmB;
    public double tmC;
    public double tmD;
    // CTM stack for q/Q/cm operators; inherit from parent context if provided
    public Matrix ctm;
    public Stack<Matrix> ctmStack = null!;
    // Graphics state stack — save/restore text state across q/Q.
    // Per PDF spec (32000-1 Table 52) the graphics state carries the WHOLE text state:
    // the spacing scalars AND the font selected by Tf together with its size. A block
    // that selects a font inside `q`/`Q` therefore hands the font back at the `Q`, and
    // a document that leans on that — a document draws its right-hand column with a
    // `q /TT0 1 Tf 12.48 0 0 12.48 ... Tm ... Q` block and then keeps writing at the
    // restored 12.48 pt — reads as 1 pt text if only the scalars come back. Nonstroking
    // color is part of the same state and is saved alongside.
    public Stack<(double leading, double charSpacing, double wordSpacing, double hScaling, double textRise, int renderMode, Color fillColor, Color? strokeColor, string? fontName, string? fontNameGuard, double fontSize, PdfDictionary? fontDict, Dictionary<int, string>? toUnicode, FontMetrics? metrics, Font? fontInfo, bool isBold, bool isItalic, bool fontBold, bool fontMissing)> gsStack = null!;
    // Nonstroking (fill) color tracking for SearchForTextRelatedGraphics.
    // Updated by g/rg/k/sc/scn; saved/restored on q/Q.
    public Color currentFillColor = null!;
    // Stroking color tracking — captured onto each run's StrokingColor so a
    // round-tripped TextState.StrokingColor survives. Updated by G/RG/K/SC/SCN.
    public Color? currentStrokeColor;
    // Pending path fragments since the last path-painting operator.
    // We only classify the path as a "rectangle fill" when it contains
    // at least one re and no other path-construction operator (m/l/c/v/y/h).
    // CTM is captured at the time of re so a subsequent cm doesn't shift the rect.
    public List<(double x, double y, double w, double h, Matrix ctmAtRe)> pendingPathRects = null!;
    public bool currentPathHasNonRect;
    // Device-space bounding box of the CURRENT path (all construction ops since the
    // last paint op), including curve control points (a safe over-approximation).
    // Used for (a) occlusion covers from non-rect filled paths (rounded rects /
    // polygons drawn over text) and (b) the clip rect a `W … n` establishes.
    public double pathMinX;
    public double pathMinY;
    public double pathMaxX;
    public double pathMaxY;
    public int pathSubpaths;
    // Active clip rectangle (device space), tracked through q/Q. Non-rect clip
    // paths contribute their bounding box — an over-approximation of the clip
    // region, so "run outside the clip bbox" (or a degenerate sliver clip)
    // remains a safe invisibility signal. Runs record the clip in effect when
    // they were shown; a fully clipped-away run reads as hidden text.
    public (double Llx, double Lly, double Urx, double Ury)? currentClip;
    public Stack<(double Llx, double Lly, double Urx, double Ury)?> clipStack = null!;
    public bool pendingClip;
    // Stroked-path points (from m/l) + current line width, so a horizontal stroked
    // line — the common way an underline/strikeout is drawn — is captured as a thin
    // decoration rect on the S/s operator.
    public List<(double x, double y, Matrix ctm)> strokePts = null!;
    public double currentLineWidth;
    // Text rendering mode (0=fill, 3=invisible, etc.)
    public int renderMode;
    // Font style flags (resolved from font descriptor or BaseFont name)
    public bool currentIsBold;
    public bool currentIsItalic;
    // Font-intrinsic bold state (from descriptor/name), separate from Tr-based bold
    public bool fontIsBold;
    // Track the Y position of the last actually-emitted text run.
    // Used by the Tm handler to avoid false "\n" sentinels when BT resets ty=0
    // but the next text block is on the same visual line as the previous one.
    public double lastEmittedY;
    // The same position in PAGE space. Some producers draw every run in its own
    // q/cm/BT..ET/Q block with Tm y = 0 and the line position carried entirely by
    // the cm translation — there text-space Y never changes between lines and the
    // sentinel must compare page-space Y instead.
    public double lastEmittedPageY;
    // The font size that set the last emitted baseline. A line-break test compares against
    // THAT baseline, so it has to be scaled by the size that drew it - not by whatever the
    // text state happens to hold now. `q`/`Q` restores a font and its size (PDF 32000-1
    // Table 52), so the current size can belong to an enclosing scope and be far larger
    // than the text on the page: thresholding on it swallowed real line breaks and fused a
    // 109-line page into 6 blobs.
    public double lastEmittedFs;
    // The extraction inputs, captured from the method parameters.
    public byte[] streamBytes = null!;
    public PdfDictionary resourceDict = null!;
    public PdfReader reader = null!;
    public List<RawTextRun> result = null!;
    public int depth;
    public Matrix? inheritedCtm;
    public List<RawFillRect>? fillRects;
    public bool useFontEngineEncoding;
    public bool keepAllFillRects;
    public List<RawCoverRect>? coverRects;
    public (double Llx, double Lly, double Urx, double Ury)? inheritedClip;
    public bool strictFonts;
    public HashSet<object>? seenForms;
    public List<string>? missingFontKeys;
}
}
