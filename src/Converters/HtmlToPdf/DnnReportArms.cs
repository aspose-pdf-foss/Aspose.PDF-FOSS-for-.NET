using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Bands, field rows, paragraph lines and gaps.</summary>
    private static void RenderDnnTextBlocks(DnnReportState dn, DnnBlock b)
    {
        switch (b)
        {
            case DnnBand band:
            {
                BreakPage(dn, DnnBandPt);
                var top = dn.y + 0.375;              // outer border line
                var h = DnnBandPt - 0.375 * 2;
                if (band.Header)
                {
                    Line(dn, dn.page, dn.moduleL - 0.375, dn.y, dn.moduleR + 0.375, dn.y, dn.teal);
                    FillRect(dn, dn.page, dn.moduleL + 0.375, top, dn.moduleR - 0.375, top + h, dn.teal);
                    Line(dn, dn.page, dn.moduleL + 0.375, top + 0.375, dn.moduleR - 0.375, top + 0.375, dn.teal);
                    Line(dn, dn.page, dn.moduleL + 0.375, top + h - 0.375, dn.moduleR - 0.375, top + h - 0.375, dn.teal);
                }
                else
                {
                    FillRect(dn, dn.page, dn.moduleL + 0.375, top, dn.moduleR - 0.375, top + h, dn.subBg);
                    Line(dn, dn.page, dn.moduleL + 0.375, top + 0.375, dn.moduleR - 0.375, top + 0.375, dn.teal);
                    Line(dn, dn.page, dn.moduleL + 0.375, top + h - 0.375, dn.moduleR - 0.375, top + h - 0.375, dn.teal);
                }
                var textTop = top + (band.Header ? DnnHeaderTextSeatPt : DnnSubTextSeatPt);
                DrawText(dn, dn.page, dn.bodyL + (band.Header ? 0.75 : 0.0),
                    textTop + dn.asc8, band.Text, true, DnnCellFontPt,
                    band.Header ? dn.white : dn.black);
                dn.y += DnnBandPt;
                Touch(dn, dn.page, dn.y);
                break;
            }
            case DnnFieldRow row:
            {
                var rowH = DnnRowLinePt + (row.Bare ? 5 * 0.75 : 0);
                BreakPage(dn, rowH);
                var baseline = dn.y + DnnRowSeatPt + dn.asc8;
                var rowL = dn.bodyL + row.IndentPx * 0.75;
                foreach (var (label, labelW, value, pairX) in row.Pairs)
                {
                    var boxRight = rowL + pairX + labelW;
                    var lw = MeasureFaceText("Verdana Bold", label, DnnCellFontPt);
                    DrawText(dn, dn.page, boxRight - DnnBodyPadPt - lw, baseline, label, true,
                        DnnCellFontPt, dn.black);
                    DrawText(dn, dn.page, boxRight, baseline, value, false, DnnCellFontPt, dn.black);
                }
                dn.y += rowH;
                Touch(dn, dn.page, dn.y);
                break;
            }
            case DnnParaLine para:
            {
                BreakPage(dn, para.Pitch);
                DrawText(dn, dn.page, dn.bodyL, dn.y + para.Pitch - 3.75 - DnnDescEm * DnnCellFontPt,
                    para.Text, false, DnnCellFontPt, dn.black);
                dn.y += para.Pitch;
                Touch(dn, dn.page, dn.y);
                break;
            }
            case DnnGap gap:
            {
                dn.y += gap.H;
                if (dn.y > dn.bandBottom) { BreakPage(dn, dn.bandBottom); }
                else Touch(dn, dn.page, dn.y);
                break;
            }
        }
    }

    /// <summary>Grid headers, rows and fills.</summary>
    private static void RenderDnnGridBlocks(DnnReportState dn, DnnBlock b)
    {
        switch (b)
        {
            case DnnGridHeader gh:
            {
                BreakPage(dn, DnnGridHeaderHPt);
                var gl = dn.bodyL;
                var gr = dn.moduleR - 0.375 - DnnBodyPadPt;
                FillRect(dn, dn.page, gl, dn.y, gr, dn.y + DnnGridHeaderHPt, dn.gridHdrBg);
                Line(dn, dn.page, gl - 0.375, dn.y, gr + 0.375, dn.y, dn.teal);
                Line(dn, dn.page, gl, dn.y, gl, dn.y + DnnGridHeaderHPt, dn.teal);
                Line(dn, dn.page, gr, dn.y, gr, dn.y + DnnGridHeaderHPt, dn.teal);
                Line(dn, dn.page, gl, dn.y + DnnGridHeaderHPt + 0.3, gr, dn.y + DnnGridHeaderHPt + 0.3, dn.teal);
                double total = 0;
                foreach (var c in gh.Cells) total += c.Wpx + DnnGridCellPadPx;
                double cum = 0;
                var hb = dn.y + DnnGridTextSeatPt + dn.asc8;
                foreach (var (text, wpx) in gh.Cells)
                {
                    var x0 = gl + (gr - gl) * cum / total;
                    cum += wpx + DnnGridCellPadPx;
                    var x1 = gl + (gr - gl) * cum / total;
                    if (text.Length > 0)
                        DrawText(dn, dn.page, x0 + DnnGridCellTextPadPt, hb, text, true, DnnCellFontPt, dn.black);
                    if (cum < total - 0.1)
                        Line(dn, dn.page, x1, dn.y + 0.3, x1, dn.y + DnnGridHeaderHPt, dn.teal);
                }
                dn.y += DnnGridHeaderHPt;
                Touch(dn, dn.page, dn.y);
                break;
            }
            case DnnGridRow grow:
            {
                BreakPage(dn, DnnGridSummaryHPt);
                var gl = dn.bodyL;
                var gr = dn.moduleR - 0.375 - DnnBodyPadPt;
                double total = 0;
                foreach (var c in grow.Cells) total += c.Wpx + DnnGridCellPadPx;
                double cum = 0;
                var sb = dn.y + DnnGridTextSeatPt + dn.asc8;
                foreach (var (text, wpx) in grow.Cells)
                {
                    var x0 = gl + (gr - gl) * cum / total;
                    cum += wpx + DnnGridCellPadPx;
                    var x1 = gl + (gr - gl) * cum / total;
                    if (text.Length > 0)
                        DrawText(dn, dn.page, x0 + DnnGridCellTextPadPt, sb, text, false, DnnCellFontPt, dn.black);
                    if (cum < total - 0.1)
                        Line(dn, dn.page, x1, dn.y, x1, dn.y + DnnGridSummaryHPt, dn.teal);
                }
                Line(dn, dn.page, gl, dn.y + DnnGridSummaryHPt + 0.3, gr, dn.y + DnnGridSummaryHPt + 0.3, dn.teal);
                dn.y += DnnGridSummaryHPt;
                Touch(dn, dn.page, dn.y);
                break;
            }
        }
    }
}
