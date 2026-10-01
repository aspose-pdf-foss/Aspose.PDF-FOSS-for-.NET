using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextAbsorber
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class ShowTextArrayState
{
    public double tjWidth;
    public int tjDecodedLen;
    // Buffer the TJ text so we can apply per-operator RTL reversal
    // after collecting all sub-strings (mirrors TypeScript applyRtl on TJ).
    public System.Text.StringBuilder tjBuf = null!;
    // When a search rectangle is active, clip each glyph to it in
    // page space; the pen advances over the whole array (strings and
    // numeric adjustments) regardless of visibility.
    // Sideways text clips along its advance axis (page Y).
    public bool clipRot;
    public bool clipping;
    public System.Text.StringBuilder? clipBuf;
    public double clipPen;
    public bool hadString;
    // Track this run for the Pure-mode grid (line-start X for
    // leading columns) — the TJ path must mirror the Tj path or
    // TJ-drawn documents get no grid anchoring at all.
    public double tjRunPageX;
    // The inter-word space before the run depends only on pre-run state.
    public int leadingSpaces;
    public double tjRunDevX;
    public bool tjUseDev;
    public bool tjUsePage;
    public double tjStartPageX;
    public double tjGapPre;
    // Pen start offsets (text-space, one per tjBuf char) for the run
    // span's per-character X map; invalidated when the code↔char
    // mapping is not 1:1 for some sub-string.
    public List<double> tjRel = null!;
    public bool tjRelValid;
    // Synthetic-space eligibility (validated over a
    // 1231-run corpus; same rule as the fragment
    // absorber): one space per adjustment ≤ −130/1000 em iff the
    // array is "armed" — any ≥2-glyph piece, or any glyph that is
    // NOT an uppercase letter or punctuation (font type is
    // irrelevant; tracked caps-only display text collapses) — and
    // is not the letter-tracking shape (>10 pieces, ALL
    // single-glyph → collapse; word-piece prose arrays keep their
    // kern-encoded word gaps).
    public bool tjIsType0;
    public int tjPieceCount;
    public bool tjMultiGlyph;
    public List<double> tjAdjs = null!;
    public bool tjSynthArmed;
    // Letter-tracked single-glyph arrays (the disarmed shape) can still
    // encode WORD gaps — as kern OUTLIERS against the array's uniform
    // tracking baseline, not as absolute-threshold kerns: a newspaper
    // headline tracks letters at +20..+58 and words at −135..−169
    // (never reaching the classic −190). Break where the adjustment
    // falls ≥130/1000 em BELOW the array's median; a uniformly tracked
    // display run (every kern ≈ the median) still collapses.
    public double tjMedian;
    public double tjLtrackMedian;
    // Per-glyph POSITIONING arrays: in an all-single-glyph array
    // where word-depth kerns are the NORM rather than the exception
    // (half or more of the adjustments reach −130), the kerns place
    // glyphs, they don't separate words — synthesizing a space at
    // each would shred the run into single-char confetti. "Page:1/1"
    // (4 of 7 kerns at −264…−284) collapses even though lowercase
    // letters arm it; "Date : 26/05/2022 03:53:42 PM" (3 word kerns
    // among 24 small tracking values) keeps its word gaps.
    public bool tjPositioningArray;
    public double tjDeepMedian;
    public StringBuilder? tjDbg;
    // Apply per-operator RTL reversal: if all decoded TJ chars are RTL/neutral,
    // reverse to convert visual order to logical order (Hebrew, Arabic).
    public string tjText = null!;
    public bool tjIsLeadingPos;
    public bool tjAllSpace;
}
}
