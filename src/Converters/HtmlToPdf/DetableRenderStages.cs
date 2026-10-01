using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The stages of the detable render: the grid close and one row.
    private static void CloseDetableGrid(DetableRenderState de, double bottom)
    {
        // verticals at the column edges over this page's rows, plus the frame
        var x = de.tableX;
        VLine(de.sw, x, de.gridTop, bottom);
        foreach (var w in de.colW) { x += w; VLine(de.sw, x, de.gridTop, bottom); }
        HLine(de.sw, de.tableX, de.tableX + SrDetableWPt, bottom, 0.75);
    }

    /// <summary></summary>
    private static bool RenderDetableRow(DetableRenderState de, List<(bool el, string lbl)> row)
    {
        if (de.y + SrDataRowHPt > de.limit)
        {
            CloseDetableGrid(de, de.y);
            NewPage(de.sw);
            de.gridTop = de.contentTop + SrTableTopPadPt;
            de.y = de.gridTop;
            HLine(de.sw, de.tableX, de.tableX + SrDetableWPt, de.y, 0.75);
        }
        for (var ci = 0; ci < row.Count && ci < de.colW.Count; ci++)
        {
            var cx = de.tableX; for (var k = 0; k < ci; k++) cx += de.colW[k];
            var (el, lbl) = row[ci];
            var avail = de.colW[ci] - 6;
            var lblW = lbl.Length > 0 ? MeasureFaceText("Arial", lbl, SrCellFsPt) : 0;
            if (el && lbl.Length > 0 && SrElementWPt + lblW <= avail)
            {
                // element + label share the first line (template: label glyph
                // 13.4 under the row top, the underline at 18.2)
                HLine(de.sw, cx + 2, cx + 2 + SrElementWPt, de.y + 18.2, 0.75);
                Run(de.sw, "FA", SrCellFsPt, cx + 3 + SrElementWPt + 2, de.y + 13.4, lbl);
            }
            else if (el && SrElementWPt <= avail)
            {
                // element line, labels below (template: 17.4 / 22.0)
                HLine(de.sw, cx + 1.5, cx + 1.5 + SrElementWPt, de.y + 17.4, 0.75);
                var lines = MeasuredWordWrap(lbl, avail, "Arial", SrCellFsPt);
                for (var li = 0; li < lines.Length; li++)
                    Run(de.sw, "FA", SrCellFsPt, cx + 1.5, de.y + 22.0 + li * SrCellLinePt, lines[li]);
            }
            else if (el)
            {
                // the element itself overflows the column: it draws unclipped
                // on its own tight line, the labels flow under it
                // (template: 11.5 / 16.3)
                HLine(de.sw, cx + 2, cx + 2 + SrElementWPt, de.y + 11.5, 0.75);
                var lines = MeasuredWordWrap(lbl, avail, "Arial", SrCellFsPt);
                for (var li = 0; li < lines.Length; li++)
                    Run(de.sw, "FA", SrCellFsPt, cx + 2, de.y + 16.3 + li * SrCellLinePt, lines[li]);
            }
            else if (lbl.Length > 0)
            {
                var lines = MeasuredWordWrap(lbl, avail, "Arial", SrCellFsPt);
                for (var li = 0; li < lines.Length; li++)
                    Run(de.sw, "FA", SrCellFsPt, cx + 3, de.y + 13.4 + li * SrCellLinePt, lines[li]);
            }
        }
        de.y += SrDataRowHPt;
        HLine(de.sw, de.tableX, de.tableX + SrDetableWPt, de.y, 0.75);
        return true;
    }
}
