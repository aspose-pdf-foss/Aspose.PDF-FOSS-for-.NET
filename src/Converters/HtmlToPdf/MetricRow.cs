using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Metric rows: one row of the grid measured and drawn.</summary>
    private static void RenderMetricRow(MetricRowsState mr, int ri)
    {
        mr.r = mr.rows[ri];
        MeasureMetricRowContent(mr, ri);
        MeasureMetricRowBox(mr, ri);
        if (TrySplitMetricRowByLines(mr, ri))
        {
            MeasureMetricRowContent(mr, ri);
            MeasureMetricRowBox(mr, ri);
        }

        BreakMetricRowPage(mr);

        FillMetricRowBand(mr, ri);

        mr.contentTop = mr.cursor.y - mr.s - mr.p - mr.bandCenterPad - (mr.mps.collapsedGrid ? 0.75 : 0);
        DrawCollapsedGridRules(mr);
        mr.colX = mr.tableX + mr.s + (mr.mps.collapsedGrid ? 0.75 : 0)
            + (mr.collapseBoxW > 0 ? mr.collapseBoxW : 0)
            // the table's own left padding is box space before its first column
            + mr.mps.tablePadLeftPt;
        mr.rowSubBottom = double.MaxValue;
        mr.rowRealBottom = double.MaxValue;
        mr.flatSkip = 0;
        var rowPage = mr.cursor.page;
        // Every cell flows from the row's top on the page the row opened on, and the row ends
        // on the furthest page any of them reached, under the lowest of the cells that ended
        // there (probed on the Words letter: the right-hand block stands beside the address
        // grid on page 1 and paginates on its own; laid out after the grid it opened where the
        // grid ENDED, a page later).
        // (the UA flow's law; the legacy flows keep their calibrated hand-off, where a later cell
        // follows a nested grid that moved whole to the next page)
        var rowPages = mr.stdSerif;
        var endPage = rowPage;
        var endIndex = mr.doc.Pages.IndexOf(rowPage);
        var endSubBottom = double.MaxValue;
        for (var c = 0; c < mr.nCols; c++)
        {
            if (rowPages) { mr.cursor.page = rowPage; mr.rowSubBottom = double.MaxValue; }
            RenderMetricRowColumn(mr, c);
            if (!rowPages) continue;
            var cellIndex = mr.doc.Pages.IndexOf(mr.cursor.page);
            // (a cell whose content moved WHOLE to a later page - all of its measured height stands
            // there - takes the cells after it along, as the calibrated hand-off did; a cell that
            // paginates leaves them at the row top)
            if (cellIndex > endIndex && c < mr.r.Count && mr.rowSubBottom < double.MaxValue
                && mr.pageHeight - mr.marginTop - mr.rowSubBottom >= mr.r[c].ContentH - 1)
                rowPage = mr.cursor.page;
            if (cellIndex > endIndex) { endPage = mr.cursor.page; endIndex = cellIndex; endSubBottom = mr.rowSubBottom; }
            else if (cellIndex == endIndex) endSubBottom = Math.Min(endSubBottom, mr.rowSubBottom);
        }
        if (rowPages) { mr.cursor.page = endPage; mr.rowSubBottom = endSubBottom; }
        mr.rowAdvance = mr.rowBoxH;
        // A nested grid that paginated leaves the row on the NEW page: the row ends where
        // the grid ended there, whatever its box measured on the page it opened on (the
        // report's comment section broke to page 2 and its footer followed a page later,
        // seated off the old page's bottom).
        if (mr.rowSubBottom < double.MaxValue && !ReferenceEquals(mr.cursor.page, rowPage))
        {
            mr.cursor.y = mr.rowSubBottom - mr.p;
            return;
        }
        if (mr.rowSubBottom < double.MaxValue)
        {
            var subAdv = (mr.cursor.y - mr.s) - mr.rowSubBottom + mr.p;
            var textAdv = mr.rowRealBottom < double.MaxValue
                ? (mr.cursor.y - mr.s) - mr.rowRealBottom + mr.p : 0;
            // A row of nested grids advances by what they really drew, not by the
            // pre-layout estimate its box was measured with; a declared row height
            // still floors it.
            mr.rowAdvance = (!mr.stdSerif && mr.wrapperStacks) || mr.rowCellClassH <= 0
                ? Math.Max(subAdv, textAdv)
                : Math.Max(mr.rowAdvance, subAdv);
        }
        mr.cursor.y -= mr.s + mr.rowAdvance;
    }
}
