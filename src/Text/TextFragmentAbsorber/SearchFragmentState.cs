using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextFragmentAbsorber
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class SearchFragmentState
{
    public int preCountAll;
    // Index the fill rects once so the per-match decoration probes below query a
    // baseline-local slice instead of rescanning the whole (possibly huge) list.
    public Aspose.Pdf.Text.TextFragmentAbsorber.FillRectIndex? fillIndex;
    // Ordering: a page's matches are yielded in the order of its
    // LINE-ORDERED concatenated search text. For almost every document that
    // equals content order — an unconditional position sort misorders far
    // more documents. Only when the page's stream order is majorly scrambled
    // (a >200 pt upward jump between consecutive runs — the same cue the
    // plain-text line sort keys on: rotated column layouts, bottom-up
    // writers) do the matches get ordered top-to-bottom.
    // Scope: only same-phrase match sets reorder (a repeated label found top
    // and bottom); distinct-content matches keep content order — reported
    // match positions and plain-text dumps both preserve it.
    public int newMatches;
    public bool samePhrase;
    public bool anyRtl;
    public List<RawTextRun> rawFragments = null!;
    public int pageIndex = 0;
    public Page? sourcePage = null;
    public XForm? sourceForm = null;
    public List<RawFillRect>? fillRects = null;
    public bool[] laterInk = null!;
    public bool[] clippedAway = null!;
    public double[] runBoxArea = null!;
    public string concatenated = null!;
    public List<int> charToRun = null!;
    public int[] runStartChar = null!;
    public int[]? bidiPerm = null;
}
}
