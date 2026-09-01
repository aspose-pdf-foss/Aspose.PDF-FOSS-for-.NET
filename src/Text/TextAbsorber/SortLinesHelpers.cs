using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextAbsorber
{
// The line sort's row-tolerance and blank-row helpers.
    private bool EdgeAdjacent(SortLinesState sl, int blankIdx, int textIdx) =>
        !double.IsNaN(sl.lineEdgeX[blankIdx]) && !double.IsNaN(sl.lineEdgeX[textIdx])
        && Math.Abs(sl.lineEdgeX[blankIdx] - sl.lineEdgeX[textIdx]) <= sl.adjacencyTol;

    // Blank-row gaps measure between DESCENT lines (box bottoms), not raw
    // baselines: a deep-descent big-font row reaches further down, closing
    // the gap to the next row (uniform-size rows are unaffected — the
    // descent term cancels).
    // A sideways-DOMINANT page takes the same descent line: its row
    // coordinate is the baseline's device position along the up-axis
    // (RotatedRowY), so the per-line effective sizes scale it just as they
    // do upright. Only a MINORITY rotated line on an upright page keeps its
    // raw anchor - its box points up from there, never down a descender.
    private double DescLineY(SortLinesState sl, double y, int idx) =>
        double.IsNaN(y) || sl.pageRot[idx] ? y
        : y - sl.pageDesc[idx] * (double.IsNaN(sl.pageFs[idx]) || sl.pageFs[idx] <= 0 ? 10.0 : sl.pageFs[idx]);

    private int BlankRowsFor(SortLinesState sl, double prevY, double curY, double curFs = double.NaN)
    {
        if (sl.blankFs <= 0 || double.IsNaN(prevY) || double.IsNaN(curY)) return 0;
        // The line-height that gates a blank is the ARRIVING line's own
        // font size (a 21pt heading 21.5pt below a 10pt line opens no
        // blank; a 6pt fine-print row 28.8pt below a 7.5pt line opens
        // two). Sideways pages included - their effective sizes are device
        // sizes and their row coordinates device positions.
        var f = !double.IsNaN(curFs) && curFs > 0 ? curFs : sl.blankFs;
        var gap = prevY - curY;
        var r = gap <= 2 * f ? 0 : gap > 4 * f ? 2 : 1;
        if (GridDebug && r > 0)
            Console.Error.WriteLine($"[blank] prevY={prevY:F2} curY={curY:F2} f={f:F2} curFs={curFs:F2} -> {r}");
        return r;
    }

    // An IN-ORDER page keeps its stream line structure: only near-identical
    // baselines merge (co-row segments the stream happened to break). The full
    // font-relative row tolerance applies only to the geometric re-sort of an
    // OUT-OF-ORDER block (e.g. flattened field values appended after the page
    // text), where reading-order rows must be reassembled from scratch.
    private double SameRowTol(SortLinesState sl, double y, double fs) => sl.needsSort
        ? RowMergeTol(y, fs)
        : Math.Min(InOrderRowTol, RowMergeTol(y, fs));

    // Row tolerance for a PAIR of lines: the larger font of the two anchors
    // the reach (a 12pt heading merges an 8pt annotation 2.7pt below it).
    private double SameRowTolPair(SortLinesState sl, double y, double fsA, double fsB)
    {
        // Sideways pages take the pair-max reach (a 12pt heading merging an
        // 8pt annotation 2.7pt below). Upright pages keep the anchor-font
        // rule the corpus is calibrated on — EXCEPT for strongly mixed-size
        // pairs (label/value rows: a 6pt caption above its 10pt value,
        // baselines ~4pt apart): the line bands reach by the
        // larger font, so a size ratio ≥ 1.5 takes the full band reach of
        // the larger font (past the in-order cap, which is for equal-size
        // staircases).
        if (!_pageHasRotatedText)
        {
            var lo = Math.Min(fsA, fsB); var hi = Math.Max(fsA, fsB);
            if (!(lo > 0) || double.IsNaN(lo) || hi < 1.5 * lo)
                return SameRowTol(sl, y, fsA);
            // Strongly mixed-size pairs (a 6pt label 3.97pt from its 10pt
            // value) band-merge with the larger font's half-height reach.
            // NOTE: the true band model is LINE BOXES
            // (segBottom/middle vs line top); baseline
            // distance + size ratio cannot reproduce it for mid-ratio
            // pairs — implementing the line-box model is the remaining work.
            return 0.5 * hi;
        }
        var fs = double.IsNaN(fsA) ? fsB : double.IsNaN(fsB) ? fsA : Math.Max(fsA, fsB);
        return SameRowTol(sl, y, fs);
    }

    /// <summary>The x a line anchors at: its recorded x, else where its text starts.</summary>
    private double AnchorX(SortLinesState sl, int idx2) => double.IsNaN(sl.pageXs[idx2]) ? sl.lineStartXs[idx2] : sl.pageXs[idx2];
}
