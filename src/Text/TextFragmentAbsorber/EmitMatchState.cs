using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextFragmentAbsorber
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class EmitMatchState
{
    // Map match indices back through bidi permutation if reordering was applied
    public int startCharIdx;
    public int endCharIdx;
    public int firstRunIdx;
    public int lastRunIdx;
    // Compute bounding rectangle spanning all involved runs
    public Rectangle rect = null!;
    public Aspose.Pdf.Text.TextFragmentAbsorber.RawTextRun firstRun;
    public Aspose.Pdf.Text.TextState textState = null!;
    // A match is hidden when the HIDDEN AREA of its spanned runs — covered
    // by later ink or clipped away — carries the majority of the glyph
    // area. Area-weighted, not all-runs: a word straddling two clip
    // strips is hidden in the pass that shows only its short tail, but
    // visible in the pass that shows most of it.
    public double hiddenArea;
    public double totalArea;
    public double trailingTc;
    // Text direction in page space
    public double sTdx;
    public double sTdy;
    public double? sRot;
    // The text a match REPORTS is in logical (reading) order. Which conversion
    // gets it there depends on the frame `match.Value` came from: when the page
    // carried RTL and the concatenation was bidi-reordered (bidiPerm non-null)
    // the value is already logical; otherwise it is still in DRAWN order — the
    // regex path deliberately searches drawn order — and the run reverses.
    public string absorbedText = null!;
    public RawFillRect? capturedUl;
    public RawFillRect? capturedBg;
    // A regex match can span a line break: the matched text carries the
    // \r\n sentinel, but segments cover only glyph runs, so the segment
    // join (which each Segments.Add refreshed _text to) loses it. Keep
    // the matched text — the break belongs in Text — but
    // only for an INTERIOR break: a match that merely ends (or starts)
    // on the sentinel (e.g. pattern "RTF\s[\r\n]") reads back without it.
    public string matchTrimmed = null!;
    public TextFragment fragment = null!;
    public double posX;
    public double posY;
}
}
