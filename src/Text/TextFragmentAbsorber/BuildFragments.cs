using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextFragmentAbsorber
{
    private void BuildAllFragmentsFromRuns(List<RawTextRun> rawFragments, Rectangle? searchRect,
        Page? sourcePage, XForm? sourceForm, int pageIndex, List<RawFillRect>? fillRects = null,
        List<RawCoverRect>? coverRects = null)
    {
        var bf = new BuildFragmentsState();
        bf.rawFragments = rawFragments;
        bf.searchRect = searchRect;
        bf.sourcePage = sourcePage;
        bf.sourceForm = sourceForm;
        bf.pageIndex = pageIndex;
        bf.fillRects = fillRects;
        bf.coverRects = coverRects;
        SplitRunsAtCharGaps(bf.rawFragments);
        if (_textSearchOptions?.ExcludeRectangles is { Length: > 0 } excludeRects)
            SplitRunsByExcludeRects(bf.rawFragments, excludeRects);
        // Blank lines BELOW the last painted glyph leave no fragment behind — only
        // trailing newline sentinels (textless line advances). Count them so the
        // Text getter can reproduce the document's trailing blank
        // lines; the last visited page's tail is the document's tail.
        _trailingLineBreaks = 0;
        for (var t = bf.rawFragments.Count - 1; t >= 0 && bf.rawFragments[t].Text == "\r\n"; t--)
            _trailingLineBreaks++;

        // Index into rawFragments — cover rects record how many runs painted before
        // them, so run i is occluded only by covers with RunsBefore > i.
        var (laterInk, _, _) = ComputeLaterInkOcclusion(bf.rawFragments);
        bf.fillIndex = bf.fillRects is { Count: > 0 } ? new FillRectIndex(bf.fillRects) : null;
        bf.runIndex = -1;
        foreach (var run in bf.rawFragments)
        {
            if (!BuildFragmentFromRun(bf, run, laterInk)) break;
        }

        DetectSuperSubscript(_fragments);
    }
}
