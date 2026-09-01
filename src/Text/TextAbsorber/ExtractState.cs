using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextAbsorber
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class ExtractState
{
    public Dictionary<string, Aspose.Pdf.Core.PdfDictionary> fonts = null!;
    public IO.PdfLexer lexer = null!;
    public List<Aspose.Pdf.Core.PdfObject> operands = null!;
    public string? currentFontName;
    public Dictionary<int, string>? currentToUnicode;
    // UseFontEngineEncoding: decode via the font program's encoding/cmap instead of /ToUnicode.
    public bool useFontEngine;
    public PdfDictionary? currentFontDict;
    public string? actualText;
    public bool actualTextUsed;
    // Figma-style Type3 grid reconstruction: the active span's RAW ActualText
    // (no ligature collapse, \n kept) and the per-glyph consumption offset.
    // Each show op inside the span takes the next slice, sized by ITS OWN
    // raw ToUnicode decode length (the ActualText prefix is distributed
    // per glyph), and records it as a grid run — the page then
    // rebuilds on the character grid like an OCR overlay.
    public string? atSpan;
    public int atOffset;
    // True when the span's /ActualText decoded to exactly ONE character
    // (pre-ligature-collapse). When a one-glyph show decodes to the SAME
    // LETTER differing only in case, that's the small-caps styling idiom —
    // the recorded character reflects the STYLED glyph, not the reading —
    // and the font's own decode wins. A single-char
    // ActualText that names a DIFFERENT character (a space over a tab
    // glyph, a reading char over a symbol) is an author correction and is
    // honored, like every multi-character ActualText span.
    public bool actualTextSingleChar;
    public double fontSize;
    public double tmD;
    // X-scale (a component) of the most recent Tm. Td/TD advances and glyph widths
    // are tracked in unscaled text-space units, so multiplying by tmA converts an X
    // delta to page space. Usually equal to tmD (uniform scale).
    public double tmA;
    public double leading;
    public double tlmX;
    // Page-space X origin set by the most recent Tm (operands[4]). Td/TD advance tlmX
    // in unscaled text-space units, so the true page-space pen X is
    // tmOriginX + (tx - tmOriginX) * tmA. Tracked so the search-rectangle filter can
    // clip glyphs in page space (see AppendClippedRun).
    public double tmOriginX;
    public double tx;
    public double lastRunEndX;
    // Device-space end of the previous run for ROTATED text: per-Tm projection
    // scales (n2) make text-space tx values incomparable between runs whose Tm
    // differs, so sideways gap math runs in device coordinates.
    public double lastRunEndDevX;
    // True page-space end of the previous run for UPRIGHT text
    // (tmE + (tx + w − tlmX)·tmAr). tx-space deltas are only meaningful while the
    // Tm is unchanged; a document that re-sets a SCALED Tm per glyph (fontSize 1,
    // size in the matrix) makes tx values Tm-origin page coords while widths stay
    // unscaled — the unscaled subtraction then reads each glyph's own width as an
    // inter-run gap. Page-space endpoints compare correctly across Tm changes and
    // reduce to the tx-space delta exactly when the Tm scale is 1.
    public double lastRunEndPageX;
    // Page-space START of the previous run (upright): distinguishes an
    // out-of-order COLUMN backjump (pen jumps left of the previous run's own
    // start) from an overlapping re-draw at (near-)the-same spot — the
    // duplicate-stack shape the later-ink dedup collapses. Only consulted
    // while lastRunEndX is valid, so it needs no reset bookkeeping.
    public double lastRunStartPageX;
    // A standalone whitespace run at the visual start of a line can be a mid-line word space
    // emitted out of stream order (scrambled RTL/LTR run order — the space is streamed before
    // the run it separates). Hold such a leading space and re-home it before the first RTL run
    // on the same line, so a Hebrew/Arabic word space no longer lands as a leading space.
    public double pendingReorderSpaceY;
    // Raw mode reproduces the stream order without reconstructing visual rows,
    // and SUB/SUPERSCRIPT hops stay inline there: a Td that
    // dips less than ~0.42 em (a TeX subscript is ~0.16 em, a summation-bound
    // or fraction move ~0.6 em+) continues the current output line instead of
    // breaking it ("L" + subscript "DF" extracts as one line "𝐿𝐷𝐹 = …").
    public bool rawInlineScripts;
    public int lastDecodedLength;
    public double lastRunEstWidth;
    public bool lastHadMetrics;
    public double prevTmY;
    public FontMetrics? currentMetrics;
    public bool currentFontNonAgl;
    public double horizScale;
    // Character/word spacing (Tc/Tw): used for the leading-space anchor
    // adjustment (the drawn spaces' true advance includes them).
    public double charSpacing;
    public double wordSpacing;
    public double tmY;
    // Rotated-text line tracking: for a rotated Tm (d ≈ 0, b ≠ 0 — e.g.
    // "0 1 -1 0 e f", 90° text) the visual line coordinate is the origin
    // projected on the Tm up-axis (c,d) — ±e — not f. tmN is |(c,d)|, the
    // per-unit line advance used to scale Td/T*/leading displacements.
    public double tmN;
    public bool tmRotated;
    // Raw text-matrix components + page-space line origin (e,f), maintained
    // through Tm/Td/TD/T*. For sideways text the projected tmY/tlmX no longer
    // ARE page coordinates, so the bounds/rectangle filters need these.
    public double tmAr;
    public double tmBr;
    public double tmCr;
    public double tmDr;
    public double tmE;
    public double tmF;
    public int textRenderMode;
    // Track the Y at which the most recent Tj/TJ/'/" actually rendered so we can
    // distinguish "new logical line" (large Y delta) from "same row, repositioned
    // by Tm for a different column" (small Y delta). Used to suppress false
    // line-breaks from ' and " after an absolute-position Tm.
    public double lastRenderedY;
    // CTM Y-translation in effect when the last text op rendered: a page that steps
    // rows purely with q/cm translations (identical Tm every BT block) changes rows
    // only here, so the Tm row-break test must include this delta.
    public double lastRenderedCmTy;
    // Font size in effect when the last text-showing operator rendered — the
    // blank-line rule measures the vertical gap from the PREVIOUS line's bottom,
    // approximated as its baseline minus ~0.2·fs of descent.
    public double lastRenderedFs;
    // Later-ink duplicate dedup (duplicate-stack scope):
    // a run whose ink covers ≥55% of the IMMEDIATELY-PRECEDING run's glyph box
    // and draws the IDENTICAL text replaces that run in the output — only
    // the last copy of a stacked duplicate draw is reported (a
    // headline drawn gray-then-black 0.6 pt apart extracts once, not
    // interleaved). Box = the baseline-anchored −0.2 em … +0.7 em band the
    // fragment absorber's occlusion pass uses. Upright, unclipped, Pure only.
    public string dedupPrevText = null!;
    public int dedupPrevOffset;
    public double dedupPrevLlx;
    public double dedupPrevLly;
    public double dedupPrevUrx;
    public double dedupPrevUry;
    public bool pageBoundsActive;
    public bool skipText;
    // Verdict of the OPEN line (the one prevTmY/_currentLineY track): a
    // same-row reposition inherits THIS, never the last filtered block's
    // verdict. Updated only when a new-row evaluation KEEPS the line.
    public bool openLineSkip;
    public Rectangle? searchRect;
    // Glyph-clip rectangle: the search rectangle intersected with the page
    // bounds. LimitToPageBounds clips partially off-page runs GLYPH-wise —
    // the on-page tail of a left-overflowing word survives ("…er"), the
    // off-page overflow of a right-overflowing one is cut — using the same
    // machinery as the search rectangle.
    public Rectangle? clipRect;
    // Page-bounds clipping BLANKS the off-page glyphs instead of dropping
    // them: the page keeps its full (uncropped) layout — grid columns,
    // indents, and gaps — with the clipped glyphs read as whitespace
    // ("Bestelbonnummer   /" crops to "…er   /", the columns intact). A
    // search rectangle instead RE-ANCHORS the window (glyphs removed).
    public bool blankClip;
    // CTM tracking for cm operator — accumulates with inherited CTM from parent.
    // localCmD is the composed vertical scale (d): a page whose content is drawn
    // under a flipped CTM ("1 0 0 -1 0 H cm", text-space Y growing downward) needs
    // it to recover the device Y for line ordering.
    public double localCmTx;
    public double localCmTy;
    public double localCmD;
    public Stack<(double tx, double ty, double d)> cmStack = null!;
    // Full CTM (linear part + true composed translation), tracked in parallel
    // with the scalar approximations above. A page that rotates its content via
    // `cm` (deskewed scans, landscape forms) has an IDENTITY Tm — the rotation
    // only shows in the composed Tm×CTM, so direction detection and page-space
    // positions for sideways text must come from here.
    public double cmLa;
    public double cmLb;
    public double cmLc;
    public double cmLd;
    public double cmLe;
    public double cmLf;
    public Stack<(double a, double b, double c, double d, double e, double f)> cmFullStack = null!;
    // Strict font-usage check: track whether a font is set in the current graphics
    // state (Tf sets it; q/Q save/restore it as spec text state). A text-showing
    // operator with no font set means the content stream is malformed (no preceding
    // Tf) — throw IncorrectFontUsageException unless IgnoreResourceFontErrors is set.
    public bool fontSet;
    // The TEXT state is graphics state (PDF 32000-1 Table 52): Tf's font AND size, Tc,
    // Tw, Tz, TL and Tr all live there, so `q`/`Q` save and restore them along with the
    // CTM. Restoring only the font-set flag left a `q /F 1 Tf ... Q` block's size in
    // force for everything that followed it, which is how a 12.48 pt column came back
    // as 1 pt text (a document draws its right-hand column that way).
    public Stack<(bool fontSet, double fontSize, string? fontName, Aspose.Pdf.Core.PdfDictionary? fontDict, Dictionary<int, string>? toUnicode, Aspose.Pdf.Text.FontMetrics? metrics, bool nonAgl, double charSpacing, double wordSpacing, double leading, int renderMode, double horizScale)> gsStack = null!;
    // The render inputs, captured from the method parameters.
    public byte[] streamBytes = null!;
    public PdfDictionary pageDict = null!;
    public PdfReader reader = null!;
    public int depth;
    public double[]? inheritedBounds;
    public double cmTx;
    public double cmTy;
    public bool fontSetOnEntry;
    public double cmD;
    public double cmLinA;
    public double cmLinB;
    public double cmLinC;
    public double cmLinD;
    public double cmLinE;
    public double cmLinF;
    // Use inherited page bounds for Form XObjects (they don't have their own MediaBox)
    public double[]? pageBounds;
}
}
