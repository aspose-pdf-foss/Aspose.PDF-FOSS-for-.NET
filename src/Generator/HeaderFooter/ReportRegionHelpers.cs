using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Stamps;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public sealed partial class HeaderFooter
{
// The report region's measuring helper and its pending-column flush.
    private static double Measure(ReportRegionState rr, string t, bool bold) => MeasureReportText(t, rr.fs, bold);

    private static void FlushCols(ReportRegionState rr)
    {
        if (rr.pendingCols.Count == 0) return;
        // sibling columns bottom-align: the tallest sets the group's rows
        var heights = new List<double>();
        foreach (var (inner, frac) in rr.pendingCols)
            heights.Add(RenderReportRegion(null, null, inner, 0, frac * rr.w, 0,
                rr.inFieldset, null, null));
        var groupH = 0.0;
        foreach (var h in heights) if (h > groupH) groupH = h;
        var cx = rr.x;
        for (var ci = 0; ci < rr.pendingCols.Count; ci++)
        {
            var (inner, frac) = rr.pendingCols[ci];
            RenderReportRegion(rr.page, rr.b, inner, cx, frac * rr.w,
                rr.yBase + (groupH - heights[ci]), rr.inFieldset, rr.boldRes, rr.plainRes);
            cx += frac * rr.w + RptSpaceEm * rr.fs;
        }
        rr.yBase += groupH;
        rr.pendingCols.Clear();
    }
}
