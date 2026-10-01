using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Converters;

public sealed partial class PdfToHtmlConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class ShowRunState
{
    public Converters.PdfToHtmlConverter.CtmState dev = null!;
    public double scale;
    public double effSize;
    public double effRise;
    public double posX;
    public double posY;
    // Baseline direction in device space. PDF angles are counter-clockwise
    // with y up; CSS rotation is clockwise with y down, so the CSS angle is
    // the negation (e.g. Tm [0 s -s 0 …] — text running upward — is CSS
    // rotate(-90deg)).
    public double cssAngle;
    // A same-baseline show that lands well PAST where the previous show's
    // pen ended is a COLUMN, not a continuation: keep it as its own group so
    // every column keeps its own x, instead of one concatenated run whose
    // tail drifts by re-measured advances (an invoice's label/value columns
    // fused into single runs). Gaps up to one em of the font still merge —
    // a TOC number→title gap of ~0.9 font-em is bridged with a
    // stretched word space but ~1.06 splits — and so do BACKTRACKS: a
    // zero-leading ' wraps back to the line start at the same y, and
    // those halves join into one flowing line. The text-only
    // overlay's grouping is left as-is.
    // The gap is measured from the last TEXT pen edge: whitespace-only
    // shows are transparent to the split decision (they bridge into the
    // run when text resumes nearby, and never force a split themselves).
    // The stl_ dialects split at 87.5×fs milli-em of pen gap
    // (0.0875·fs² pt); the plain dialect keeps its one-em rule.
    public double divGapPt;
    // The column split concerns a show on the SAME baseline: a show on a
    // different baseline no longer closes the line here — it parks it (below),
    // because the producer may come back to it. The baseline test is the same
    // one the sameLine decision makes, so a same-baseline column still cuts
    // exactly where it always did.
    public double lineYTol;
    // A whitespace-only show continues the line regardless of its own
    // font/colour — a word gap drawn with a different font (a larger
    // space glyph between runs) is coerced to the group's font as its
    // own segment instead of breaking the div chain. stl_ dialects only;
    // the plain span dialect keeps strict font grouping.
    public bool wsOnlyShow;
    // In the stl_ dialects a font/size/colour switch cuts a SPAN, not the
    // line: shows keep merging while the line stays solver-eligible.
    // The stl_ dialects keep the loose 0.3-em baseline merge only for
    // shows WITHIN one marked-content item (or in untagged content):
    // across a BDC/EMC boundary two runs continue one line only on a
    // (near-)identical baseline — a tagged CV's date span and its
    // right-hand subtitle span sat 0.24pt apart and stayed two divs,
    // while an untagged report's footer runs 1–2pt apart still merge.
    public bool sameLine;
    // A run that starts LEFT of the accumulated pen (overlapping/backward
    // draw - e.g. word-gap space glyphs re-drawn over an already-shown line)
    // cannot continue the inline span flow; it opens its own positioned div.
    // (Overlay mode only - the SVG-text dialect keeps the legacy grouping.)
    // The em-compensation dialect tolerates a SQUEEZED inter-span word
    // space: a body span drawn 0.73 pt behind the title's pen (its
    // separator space compressed by justification) still continues the
    // line — the squeeze is solved as negative word-spacing
    // in ONE div. A genuine re-draw starts at least a word further back.
    public double backTolPt;
    // Append the run to the segment chain (one segment per repositioned
    // run, as before). With aligned per-char advances the OVERLAY run is
    // additionally CUT at word boundaries (space-to-nonspace edges), one
    // segment per word, each pinned separately at flush. Anchors accumulate
    // in Tc/Tw-FREE width space (glyph advances only) - the
    // same budget the per-segment letter-spacings solve against - while the
    // pen (Tc/Tw included) is kept for backward-draw detection only.
    public bool aligned;
    public int segIdx;
    public (double X, System.Text.StringBuilder Text, double PenEnd, double GlyphEnd) s0;
    public double penX;
    public double glyphX;
    public (double X, System.Text.StringBuilder Text, double PenEnd, double GlyphEnd) cl;
    // A glyph shown through a GID the embedded program's cmap
    // cannot address renders in the CSS fallback face: it takes a
    // sibling style whose metrics and font class carry the
    // fallback (spaces stay on the base).
    public HtmlFontRecord? fRec;
    public System.Func<int, bool>? glyphMapped;
    public int baseIdx;
    public int fbIdx;
    public double sxRec;
}
}
