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
private sealed class TjReplaceState
{
    // First, concatenate all string parts to see if search text spans them.
    // Large negative kernings are treated as synthetic word-space, mirroring
    // the TextFragmentAbsorber reader — but only when the next PdfString
    // doesn't already begin with ' ', so we don't double-up the space.
    public System.Text.StringBuilder fullText = null!;
    public List<(int index, string text, bool isHex)> parts = null!;
    public Aspose.Pdf.Text.TextReplacer.TjBreakRule tjRule;
    public string combinedText = null!;
    public string normalizedCombined = null!;
    public string normalizedSearch = null!;
    // Locate the match span so we can rewrite only the matched region and
    // keep everything after it intact. Preserving the suffix structure keeps
    // downstream glyph positions aligned with the original layout instead of
    // flattening the whole TJ (which shifts after-match glyphs when the
    // replacement width differs from the matched region width).
    public int matchStart;
    public int matchLen;
    // Replace-all across multiple occurrences: the structured single-match
    // path below only rewrites the first match (keeping the suffix intact),
    // so when every match must be replaced and more than one is present,
    // fall back to a flat replacement that substitutes them all.
    public bool multipleMatches;
    // Build a per-character map (combinedText char index → arr element index).
    // Must use the SAME rule as the concatenation loop above — keep in sync.
    public List<int> charMap = null!;
    public char lastMapCh;
    // Prefix/suffix text (unchanged portions on either side of the match).
    public string prefixText = null!;
    public int suffixStart;
    public string suffixText = null!;
    // Map match boundaries back to the TJ-array coordinates (arrIdx + byte
    // offset inside that string) so the width-compensation helper can
    // identify the matched slice of each PdfString.
    public int startArrIdx;
    public int endArrIdx;
    public int startOffset;
    public int endOffset;
    // Emit:  [ (prefix + replacement)  <compensation-kerning>  (suffix) ]
    //
    // Two sub-strings for the unchanged + replaced portion and the tail, with
    // an optional integer kerning between them that compensates for the width
    // change caused by the replacement. This keeps the post-match glyph row
    // at its original X — the behaviour that tests using ReplaceAdjustment.None
    // depend on.  When the replacement width matches the original matched
    // region (including any within-match kerning) the compensation is zero
    // and the kerning element is omitted.
    public bool useHex2;
    // Compute the width change the replacement introduces, in PDF
    // text-space (1/1000 em) units, so we can emit it as a TJ kerning.
    public int kernCompensation;
    // Build the prefix-and-replacement bytes from the matched string's
    // leading slice + the replacement text.
    public byte[] preRepBytes = null!;
    // Emit the suffix by COPYING the original TJ-array elements after the
    // match end, rather than collapsing them into a single PdfString. This
    // preserves the original kerning values (including big-negative kerns
    // that were synthesized into spaces in `combinedText` for matching
    // purposes) so subsequent text stays at its original X position. The
    // first PdfString after the match needs its leading bytes trimmed
    // when the match ended partway through it.
    public bool firstSuffixString;
    // the replaced array the caller receives through its out parameter
    public PdfArray newArr = null!;
    public PdfArray arr = default!;
    public string search = default!;
    public string replacement = default!;
    public Dictionary<int, string>? toUnicode = null;
    public PdfDictionary? fontDict = null;
    public PdfReader reader = default!;
    public double fontSize = 0;
}
}
