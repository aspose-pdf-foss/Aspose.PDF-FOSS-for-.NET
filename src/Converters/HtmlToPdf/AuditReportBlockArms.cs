using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The title, heading and sub-heading blocks: each an atomic band at its size and indent.</summary>
    private static void LayoutAuditHeadings(AuditReportState ar, string inner, string meta, string cls)
    {
        switch (cls)
        {
            case "auditReportTitleMain":
                ar.y = CtTitleTopPt;
                Atomic(ar, CtTitlePadTopPt + CtLineH(30) + CtTitlePadBotPt + CtTitleMarginPt,
                    CtTitlePadTopPt, ar.left + CtTitleLeftPt, 30, CtFlat(inner), CtHeadInk);
                break;
            case "auditReportTitleSub":
                Atomic(ar, CtLineH(16) + CtTitleSubMarginPt, 0, ar.left + CtTitleLeftPt, 16,
                    CtFlat(inner), CtHeadInk);
                ar.y += CtBreakDivPt;
                break;
            case "auditReportHeading":
                CloseMain(ar);
                Atomic(ar, CtHeadPadTopPt + CtLineH(27) + CtHeadPadBotPt + CtHeadMarginPt,
                    CtHeadPadTopPt, ar.left, 27, CtFlat(inner), CtHeadInk);
                break;
            case "auditReportHeadingMain":
                CloseMain(ar);
                ar.pendingMain = CtMainPadTopPt;
                ar.mainOpen = true;
                break;
            case "auditReportSubHeading":
            {
                ar.y += ar.pendingMain;
                ar.pendingMain = 0;
                var wrapTop = ar.y;
                Atomic(ar, CtSubHeadPadTopPt + CtLineH(19) + CtSubHeadPadBotPt,
                    CtSubHeadPadTopPt,
                    ar.colLeft + (ar.inCols
                        ? ar.colWidth * CtTextIndentFrac + ar.colWidth * CtIconColFrac + CtIconGapPt
                        : CtSubHeadIndentPt),
                    19, CtFlat(inner), CtBlue);
                // the section's own icon float is taller than its heading
                ar.y = Math.Max(ar.y, wrapTop + CtIconBoxPt);
                break;
            }
            case "auditReportSubSubHeading":
            {
                CloseMain(ar);
                var wrapTop = ar.y;
                // a section may shorten its own and its icon's padding inline
                var pad = CtInlinePadTop(meta, "auditReportSubSubHeading", CtSubSubPadTopPt);
                var iconPad = CtInlinePadTop(meta, "auditReportSubSubHeadingImageDiv",
                    CtSubIconPadTopPt);
                Atomic(ar, pad + CtLineH(16), pad, ar.left + CtSubSubIndentPt, 16,
                    CtFlat(inner), CtBlue);
                ar.y = Math.Max(ar.y, wrapTop + iconPad + CtSubIconBoxPt - CtSubIconPadTopPt);
                break;
            }
        }
    }

    /// <summary>A column block or a cost box: key/value pairs seated in the current column.</summary>
    private static void LayoutAuditColumns(AuditReportState ar, string inner, string cls)
    {
        switch (cls)
        {
            case "col":
            {
                var frac = CtColumnFrac(inner);
                if (frac >= 1.0 || frac <= 0)
                {
                    // the row closes on its deepest column, but only when
                    // that column ended on THIS sheet - a column that spilled
                    // has already carried the flow forward
                    if (ar.inCols && ar.colSheet == ar.sheet) ar.y = Math.Max(ar.y, ar.colDeepest);
                    ar.inCols = false;
                    ar.colLeft = ar.left;
                    ar.colWidth = CtContentWPt;
                    break;
                }
                if (!ar.inCols)
                {
                    ar.colRowTop = ar.y;
                    ar.colDeepest = ar.y;
                    ar.colSheet = ar.sheet;
                    ar.inCols = true;
                    ar.colLeft = ar.left;
                }
                else if (ar.colSheet == ar.sheet)
                {
                    ar.colDeepest = Math.Max(ar.colDeepest, ar.y);
                    ar.y = ar.colRowTop;
                    ar.colLeft += ar.colWidth;
                }
                else
                {
                    ar.colRowTop = ar.y;
                    ar.colDeepest = ar.y;
                    ar.colSheet = ar.sheet;
                    ar.colLeft += ar.colWidth;
                }
                ar.colWidth = CtContentWPt * frac;
                break;
            }
            case "costbox":
            {
                var bx = ar.colLeft + ar.colWidth * CtCostMarginFrac;
                var bw = ar.colWidth * CtCostWidthFrac;
                var by = ar.y + CtCostColPadTopPt;
                ar.fills.Add((ar.sheet, bx, by, bw, CtCostHeaderPt, CtBlue));
                ar.fills.Add((ar.sheet, bx, by + CtCostHeaderPt, bw, CtCostBodyPt, CtRowBg));
                var cy = by + CtCostHeaderPadPt;
                var first = true;
                foreach (var (txt, size, padTop) in CtCostLines(inner))
                {
                    if (!first) cy += padTop;
                    var w = Measure(ar, ar.reg, txt, size);
                    ar.items.Add(new CtItem
                    {
                        Sheet = ar.sheet, Y = cy, X = bx + (bw - w) / 2, Size = size,
                        Text = txt, Ink = first ? CtWhite : CtBlack,
                    });
                    cy += CtLineH(size);
                    if (first) { cy = by + CtCostHeaderPt; first = false; }
                }
                ar.y = by + CtCostHeaderPt + CtCostBodyPt;
                break;
            }
        }
    }

    /// <summary>Page breaks, charts and the two-column div: sheet breaks and side-by-side figure layout.</summary>
    private static void LayoutAuditFigures(AuditReportState ar, string inner, string cls)
    {
        switch (cls)
        {
            case "pagebreak":
                // A `page-break-before` is taken only where it would part a
                // finished grid or chart from the next analysis part; one
                // that merely interrupts running prose is passed over, which
                // is the expected treatment of the five inside the
                // narrative half of the report.
                if (ar.sheetHasGrid
                    && (inner.Contains("auditReportHeading\"", StringComparison.Ordinal)
                        || inner.Contains("data-highcharts-chart", StringComparison.Ordinal)))
                {
                    CloseMain(ar);
                    ar.sheet++;
                    ar.y = ar.top;
                    ar.sheetHasGrid = false;
                }
                break;
            case "chartpair":
                break;
            case "chart":
            {
                var h = CtChartHeight(inner);
                if (h > 0)
                {
                    if (ar.y + h > ar.bottom) { ar.sheet++; ar.y = ar.top; ar.sheetHasGrid = false; }
                    ar.y += h;
                    ar.sheetHasGrid = true;
                }
                break;
            }
            case "auditReportTwoColumnDiv":
            {
                CloseMain(ar);
                var h = 2 * CtRowPadPt + CtLineH(14);
                if (ar.y + CtRowMarginPt + h > ar.bottom) { ar.sheet++; ar.y = ar.top; ar.sheetHasGrid = false; }
                ar.y += CtRowMarginPt;
                ar.rows.Add((ar.sheet, ar.y));
                var (lab, val) = CtRowPair(inner);
                var rowL = ar.left + CtContentWPt * CtRowIndentFrac;
                var rowW = CtContentWPt * CtRowWidthFrac;
                ar.items.Add(new CtItem
                {
                    Sheet = ar.sheet, Y = ar.y + CtRowPadPt + CtHalf(14),
                    X = rowL + rowW * CtRowPadLeftFrac, Size = 14, Text = lab, Ink = CtBlack,
                });
                var vw = Measure(ar, ar.reg, val, 14);
                ar.items.Add(new CtItem
                {
                    Sheet = ar.sheet, Y = ar.y + CtRowPadPt + CtHalf(14),
                    X = rowL + rowW * (CtRowLabelFrac + CtRowPadLeftFrac)
                        + (rowW * CtRowValueFrac - vw) / 2,
                    Size = 14, Text = val, Ink = CtWhite,
                });
                ar.y += h;
                break;
            }
        }
    }

    /// <summary>A table block: its header and body rows measured at the column width and booked as banded rows with their cell texts.</summary>
    private static void LayoutAuditTable(AuditReportState ar, string inner)
    {
        var tableLeft = ar.left + CtContentWPt * CtTableMarginFrac;
        var tableW = CtContentWPt * CtTableWidthFrac;
        ar.y += CtTableMarginTopPt;
        foreach (var cells in CtTableRows(inner))
        {
            if (cells.Count == 0) continue;
            // the nine- and three-column grids share the 6px cell
            // padding that sets their row heights
            var nine = cells[0].Cls.Contains("NineColumn", StringComparison.Ordinal)
                || cells[0].Cls.Contains("ThreeColumn", StringComparison.Ordinal);
            var small = cells[0].Cls.Contains("NineColumn", StringComparison.Ordinal);
            // a header row is the one whose cells declare the label
            // classes - the grid's own first cell says "Headerlabel"
            // rather than "Toplabel", so both have to count
            var head = cells.Exists(c =>
                c.Cls.Contains("oplabel", StringComparison.Ordinal)
                || c.Cls.Contains("Headerlabel", StringComparison.Ordinal));
            var rowH = nine
                ? (small && head ? CtNineHeadRowPt : CtNineValueRowPt)
                : (head ? CtHeadRowPt : CtValueRowPt);
            if (ar.y + rowH > ar.bottom) { ar.sheet++; ar.y = ar.top; ar.sheetHasGrid = false; }
            var cx = tableLeft;
            for (var ci = 0; ci < cells.Count; ci++)
            {
                var (ccls, scls, txt) = cells[ci];
                var cw = tableW * CtColFrac(ccls);
                var size = small && head ? 9.0 : 12.0;
                var leftCol = ci == 0;
                // the cell sits half a spacing in from its column
                var bx = cx + CtCellSpacePt / 2;
                // a highlighted row names its own colour on the cell
                var inlineBg = CtInlineBg(ccls);
                if (inlineBg is { } ib)
                    ar.fills.Add((ar.sheet, cx, ar.y, cw + CtCellSpacePt,
                        rowH - CtCellSpacePt, ib));
                else if (leftCol)
                    ar.fills.Add((ar.sheet, cx, ar.y, cw + CtCellSpacePt,
                        rowH - CtCellSpacePt, CtCellBg));
                var drop = nine
                    ? (small && head ? CtNineHeadDropPt : CtNineValueDropPt)
                    : (head ? CtHeadDropPt : CtValueDropPt);
                if (txt.Length > 0)
                {
                    var tw = Measure(ar, ar.reg, txt, size);
                    // the three-column grid's spans are 100% wide and
                    // centre their own text, first column included
                    var centred = !leftCol
                        || ccls.Contains("ThreeColumn", StringComparison.Ordinal);
                    var tx = centred
                        ? bx + (cw - tw) / 2
                        : bx + (nine ? CtNineCellPadLeftPt : CtCellPadLeftPt);
                    ar.items.Add(new CtItem
                    {
                        Sheet = ar.sheet, Y = ar.y + drop, X = tx, Size = size,
                        Text = txt,
                        Ink = head ? (leftCol && !nine ? CtBlack : CtBlue) : CtBlack,
                    });
                }
                // the cell's own borders: a solid blue pair around a
                // header, a dotted grey under a value row
                var ruleC = head ? CtBlue : CtDotRule;
                if (head)
                    ar.rules.Add((ar.sheet, cx, cx + cw,
                        ar.y + CtRulePt / 2,
                        leftCol && !nine ? CtCellBg : CtBlue));
                ar.rules.Add((ar.sheet, cx, cx + cw,
                    ar.y + rowH - CtCellSpacePt - CtRulePt / 2, ruleC));
                cx += cw + CtCellSpacePt;
            }
            ar.y += rowH;
            ar.sheetHasGrid = true;
        }
    }
}
