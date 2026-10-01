using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextFragmentAbsorber
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class ShowTextArrayState
{
    public System.Text.StringBuilder sb = null!;
    public double tjWidth;
    public double tjWidthUnscaled;
    // Segment origin + consumed advance: a huge intra-TJ kern
    // (> ~1.5 em) SPLITS the array into separate runs at their
    // drawn positions (the Flatten tokenization rule).
    public double segTx;
    public double segTy;
    public double consumedW;
    public int lastStrLen;
    /// <summary>The decoded char range covered by the array's glyph-bearing pieces.</summary>
    public int inkStart = -1;
    public int inkEnd = -1;
    // Track per-character cumulative advance widths WITHOUT hScaling.
    // Rectangle width should not include Tz scaling — CTM handles
    // the visual scaling. This matches .NET behavior.
    public List<double> charCumWidthsList = null!;
    // Parallel list: position just AFTER each character's own glyph
    // advance, BEFORE any TJ kerning that follows.  Fragment-width
    // computation uses this for the match's final character so that
    // compensation kernings sitting between the matched region and
    // subsequent runs don't inflate the fragment's rectangle.
    public List<double> charEndPositionsList = null!;
    // Synthetic-space eligibility (validated over a
    // 1231-run corpus with zero mismatches): a TJ
    // run inserts ONE space per numeric adjustment ≤ −130/1000 em
    // iff it is "armed" — any piece of ≥2 glyphs, or any glyph
    // that is NOT an uppercase letter or punctuation (lowercase,
    // digits, spaces and symbols arm; tracked caps-only display
    // text like "(A)-417(R)-416(K)" collapses in EVERY font type) —
    // AND it is not the letter-tracking shape: an array of MORE
    // than 10 pieces that are ALL single-glyph collapses with no
    // synthetic spaces; word-piece prose arrays of any length keep
    // their kern-encoded word gaps.
    public bool tjIsType0;
    public int tjPieceCount;
    public bool tjMultiGlyphPiece;
    public List<double> tjAdjList = null!;
    public bool tjArmed;
    public bool tjSynthSpaces;
    // Letter-tracked single-glyph arrays (the disarmed shape) can still
    // encode WORD gaps — as kern OUTLIERS against the array's uniform
    // tracking baseline rather than absolute-threshold kerns (letters
    // tracked at +20..+58, words at −135..−169). Break where the
    // adjustment falls ≥130/1000 em BELOW the array's median; a
    // uniformly tracked display run (every kern ≈ the median) still
    // collapses. Mirrors the TextAbsorber rule.
    public double tjLtrackMedian;
    public double[]? charCumWidths;
    public double[]? charEndPositions;
    public ExtractRunsState xr = default!;
    public PdfArray arr = default!;
}
}
