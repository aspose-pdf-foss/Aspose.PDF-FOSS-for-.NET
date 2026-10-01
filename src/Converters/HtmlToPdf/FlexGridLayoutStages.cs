using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The stages of the flex-grid layout: one row (measure, then draw) and the container box.</summary>
    private static bool LayoutFlexRow(FlexGridLayoutState xg, FlexGridRow frow)
    {
        // Row height: the tallest cell's line bands + the border share.
        double rowBands = 0;
        var wraps = new List<string[]?>();
        double cx0 = xg.wrapL;
        foreach (var fc in frow.Cells)
        {
            var cw = fc.WFrac * xg.wrapW;
            double bands;
            string[]? wl = null;
            if (fc.PlainWrap)
            {
                var availF = cw - fc.PadFrac * xg.wrapW - 4;
                wl = MeasuredWordWrap(fc.Label, Math.Max(20, availF),
                    "Times New Roman Bold", FlexCellFontPt);
                // Table flavour: only a CENTRED wrapping cell grows its
                // row — a left-aligned prose cell OVERFLOWS it (both
                // measured on the table-flavoured waybill: the wrapped
                // header row is two bands tall, the certify row one).
                bands = (xg.fg.TableFlavor && !fc.Center ? 1 : wl.Length)
                        * FlexLineBandPt;
            }
            else if (fc.ValueWide)
                bands = 2 * FlexLineBandPt + 2 * fc.ValuePadPx * 0.75;
            else
                // An EMPTY dd collapses its line box; a filled one keeps it.
                bands = (fc.HasDd && fc.Value.Trim().Length > 0 ? 2 : 1)
                        * FlexLineBandPt;
            rowBands = Math.Max(rowBands, bands);
            wraps.Add(wl);
            cx0 += cw;
        }
        var rowH = rowBands + FlexRowBorderPt;
        var rowBottom = xg.fy + rowH;
        var nextRowTop = rowBottom + xg.rowGap;
        // Draw the cells.
        var cx = xg.wrapL;
        for (var ci = 0; ci < frow.Cells.Count; ci++)
        {
            var fc = frow.Cells[ci];
            var cw = fc.WFrac * xg.wrapW;
            var cellR = cx + cw;
            if (fc.BL) FLine(xg, cx + 0.38, xg.fy - 0.38, cx + 0.38, rowBottom + 0.38);
            if (fc.BR) FLine(xg, cellR - 0.38, xg.fy - 0.38, cellR - 0.38, rowBottom + 0.38);
            if (fc.BT) FLine(xg, cx, xg.fy - 0.38, cellR, xg.fy - 0.38);
            if (fc.BB) FLine(xg, cx, rowBottom - 0.38, cellR, rowBottom - 0.38);
            var textX = cx + fc.PadFrac * xg.wrapW + FlexRowBorderPt;
            if (fc.PlainWrap)
            {
                var wl = wraps[ci] ?? Array.Empty<string>();
                for (var li = 0; li < wl.Length; li++)
                {
                    var lx = fc.Center
                        ? cx + (cw - FWidth(wl[li], FlexCellFontPt)) / 2
                        : textX;
                    FDraw(xg, wl[li], lx, xg.fy + xg.labelDy + li * FlexLineBandPt, FlexCellFontPt);
                }
            }
            else if (fc.ValueWide)
            {
                FDraw(xg, fc.Label, textX, xg.fy + xg.labelDy, FlexCellFontPt);
                if (fc.LabelRight.Length > 0)
                    FDraw(xg, fc.LabelRight,
                        cellR - fc.LabelRightMrFrac * cw
                              - FWidth(fc.LabelRight, FlexCellFontPt),
                        xg.fy + xg.labelDy, FlexCellFontPt);
                var vTop = xg.fy + FlexLineBandPt + fc.ValuePadPx * 0.75 + xg.labelDy;
                if (fc.ValueLeft.Length > 0)
                    FDraw(xg, fc.ValueLeft, textX, vTop, FlexCellFontPt);
                if (fc.ValueRight.Length > 0)
                    FDraw(xg, fc.ValueRight,
                        cellR - fc.ValueRightMrFrac * cw
                              - FWidth(fc.ValueRight, FlexCellFontPt),
                        vTop, FlexCellFontPt);
            }
            else
            {
                if (fc.Label.Length > 0)
                    FDraw(xg, fc.Label, fc.Center
                            ? cx + (cw - FWidth(fc.Label, FlexCellFontPt)) / 2
                            : textX,
                        xg.fy + xg.labelDy, FlexCellFontPt);
                if (fc.Value.Length > 0)
                    FDraw(xg, fc.Value,
                        cellR - xg.valueInset - FWidth(fc.Value, FlexCellFontPt),
                        xg.fy + FlexLineBandPt + xg.labelDy, FlexCellFontPt);
            }
            cx = cellR;
        }
        xg.fy = nextRowTop;
        return true;
    }
}
