using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The title and section-title items.</summary>
    private static void RenderIpdHeadings(IpdClaimLetterState ic, IpdItem it, string kind)
    {
        switch (kind)
        {
            case "title":
            {
                var w = MW(ic, it.Text, 18, true);
                Run(ic, 18, IpdLeft + (IpdSheetW - w) / 2, IpdTitleBaseTd, it.Text, true);
                Line(ic, IpdLeft, IpdTitleRuleTd, IpdLeft + IpdSheetW, IpdTitleRuleTd, 1.5, "0 0 0");
                ic.y = IpdTitleRuleTd + 0.75;
                Advance(ic, 7.5);
                break;
            }
            case "sectitle":
            {
                var lines = Wrap(ic, it.Text, 12, IpdSheetW, true);
                var boxH = 2 * IpdSecBorder + 2 * IpdSecPad + lines.Count * IpdSecLinePitch;
                if (ic.y + Math.Max(ic.pendingMargin, IpdBlockMargin) + boxH > IpdContentBottom)
                    BreakPage(ic);
                var t = Open(ic, IpdBlockMargin);
                var bx1 = IpdLeft + IpdSheetW + 2 * IpdSecPad + 2 * IpdSecBorder;
                // the 1 pt "double" border draws as two ⅓ pt strokes
                foreach (var off in new[] { 0.17, 0.83 })
                {
                    Line(ic, IpdLeft, t + off, bx1, t + off, 0.333, "0 0 0");
                    Line(ic, IpdLeft, t + boxH - 1 + off, bx1, t + boxH - 1 + off, 0.333, "0 0 0");
                    Line(ic, IpdLeft + off, t, IpdLeft + off, t + boxH, 0.333, "0 0 0");
                    Line(ic, bx1 - 1 + off, t, bx1 - 1 + off, t + boxH, 0.333, "0 0 0");
                }
                for (var i = 0; i < lines.Count; i++)
                    Run(ic, 12, IpdLeft + IpdSecBorder + IpdSecPad,
                        t + IpdSecBorder + IpdSecPad + IpdSecAscent + i * IpdSecLinePitch,
                        lines[i], true);
                ic.y = t + boxH;
                Advance(ic, IpdBlockMargin);
                break;
            }
        }
    }

    /// <summary>A grid item: its rows on the grey band.</summary>
    private static void RenderIpdGrid(IpdClaimLetterState ic, IpdItem it, string kind)
    {
        switch (kind)
        {
            case "grid":
            {
                var t = Open(ic, IpdBlockMargin);
                // the 25 % split resolves on the border-inset content width
                // (96.38 + 0.25 · 575.25 = 240.19, the measured divider)
                var xSplit = IpdLeft + 0.38 + (IpdSheetW - 0.75) * IpdGridLabelFrac;
                var xRight = IpdLeft + IpdSheetW;            // 672
                if (t + IpdGridRowH > IpdContentBottom) { BreakPage(ic); t = IpdContentTop; }
                ic.pageHasGrid[^1] = true;
                Line(ic, IpdLeft, t + 0.38, xSplit + 0.37, t + 0.38, 0.75);
                Line(ic, xSplit - 0.38, t + 0.38, xRight, t + 0.38, 0.75);
                foreach (var row in it.Rows)
                {
                    if (t + IpdGridRowH > IpdContentBottom)
                    {
                        BreakPage(ic);
                        t = IpdContentTop;
                        ic.pageHasGrid[^1] = true;
                        Line(ic, IpdLeft, t + 0.38, xSplit + 0.37, t + 0.38, 0.75);
                        Line(ic, xSplit - 0.38, t + 0.38, xRight, t + 0.38, 0.75);
                    }
                    Line(ic, IpdLeft + 0.38, t, IpdLeft + 0.38, t + IpdGridRowH + 0.75, 0.75);
                    Line(ic, xSplit + 0.19, t, xSplit + 0.19, t + IpdGridRowH + 0.75, 0.75);
                    Line(ic, xRight - 0.38, t, xRight - 0.38, t + IpdGridRowH + 0.75, 0.75);
                    if (row.Count > 0 && row[0].Length > 0)
                        Run(ic, 10, IpdLeft + 0.75 + IpdCellPad, t + IpdGridBaseOff, row[0]);
                    if (row.Count > 1 && row[1].Length > 0)
                        Run(ic, 10, xSplit + 0.37 + IpdCellPad, t + IpdGridBaseOff, row[1]);
                    t += IpdGridRowH;
                    Line(ic, IpdLeft, t + 0.38, xSplit + 0.37, t + 0.38, 0.75);
                    Line(ic, xSplit - 0.38, t + 0.38, xRight, t + 0.38, 0.75);
                }
                ic.y = t + 0.75;
                Advance(ic, IpdBlockMargin);
                break;
            }
        }
    }

    /// <summary>A sub-box opens at the pending margin and closes with its frame.</summary>
    private static void RenderIpdSubBox(IpdClaimLetterState ic, IpdItem it, string kind)
    {
        switch (kind)
        {
            case "subbegin":
            {
                var t = Open(ic, IpdBlockMargin);
                if (t + IpdSubBarH + 30 > IpdContentBottom) { BreakPage(ic); t = IpdContentTop; }
                ic.inSub = true;
                ic.subTop = t;
                Line(ic, IpdLeft, t + 0.38, IpdLeft + IpdSheetW, t + 0.38, 0.75);
                FillRect(ic, 96.75, t + 0.76, IpdSubBarW, IpdSubBarH, "0.945 0.945 0.945");
                if (it.Text.Length > 0)
                    Run(ic, 11, 96.75 + IpdSubBarPad, t + 0.76 + IpdSubBarBase, it.Text, true);
                Line(ic, 96.75, t + 0.76 + IpdSubBarH - 0.38, 96.75 + IpdSubBarW,
                    t + 0.76 + IpdSubBarH - 0.38, 0.75);
                ic.y = t + 0.76 + IpdSubBarH + IpdSubPad;
                ic.pendingMargin = 0;
                break;
            }
            case "subend":
            {
                var b = ic.y + IpdSubPad;
                Line(ic, 96.38, ic.subTop, 96.38, b + 0.38, 0.75);
                Line(ic, 671.62, ic.subTop, 671.62, b + 0.38, 0.75);
                Line(ic, IpdLeft, b + 0.38, IpdLeft + IpdSheetW, b + 0.38, 0.75);
                ic.inSub = false;
                ic.subTop = -1;
                ic.y = b + 0.75;
                Advance(ic, IpdBlockMargin);
                break;
            }
        }
    }

    /// <summary>Form, coverage and text items: wrapped paragraphs with their labels.</summary>
    private static void RenderIpdFormBlocks(IpdClaimLetterState ic, IpdItem it, string kind)
    {
        switch (kind)
        {
            case "form":
            case "coverage":
            {
                var x0 = ic.inSub ? 96.75 + IpdSubPad : IpdLeft + 0.75;
                var tw = ic.inSub ? (IpdSheetW - 2 * 0.75 - 2 * IpdSubPad) * IpdSubTableFrac
                               : IpdSheetW;
                var t = Open(ic, 0);
                // column left edges
                var fr = it.Cols.Length > 0 ? it.Cols : IpdCol4;
                var colX = new double[fr.Length + 1];
                colX[0] = x0;
                for (var c = 0; c < fr.Length; c++) colX[c + 1] = colX[c] + tw * fr[c];
                double frameTop = t;
                foreach (var row in it.Rows)
                {
                    var wraps = new List<List<string>>();
                    var maxLines = 1;
                    for (var c = 0; c < row.Count && c < fr.Length; c++)
                    {
                        var budget = tw * fr[c] - 2 * IpdCellPad;
                        var wl = row[c].Length > 0 ? Wrap(ic, row[c], 10, budget) : new List<string> { "" };
                        wraps.Add(wl);
                        if (wl.Count > maxLines) maxLines = wl.Count;
                    }
                    var rowH = maxLines * IpdLinePitch10 + IpdFormRowPad;
                    if (t + rowH > IpdContentBottom) { BreakPage(ic); t = IpdContentTop; frameTop = t; }
                    for (var c = 0; c < wraps.Count; c++)
                        for (var li = 0; li < wraps[c].Count; li++)
                            if (wraps[c][li].Length > 0)
                                Run(ic, 10, colX[c] + IpdCellPad,
                                    t + IpdCellPad + IpdAscent10 + li * IpdLinePitch10,
                                    wraps[c][li]);
                    t += rowH;
                }
                if (it.Framed)
                {
                    Line(ic, x0 - 0.38, frameTop, x0 - 0.38, t, 0.75);
                    Line(ic, x0 + tw + 0.38, frameTop, x0 + tw + 0.38, t, 0.75);
                    Line(ic, x0, frameTop + 0.38, x0 + tw, frameTop + 0.38, 0.75);
                    Line(ic, x0, t - 0.38, x0 + tw, t - 0.38, 0.75);
                }
                ic.y = t;
                break;
            }
            case "text":
            {
                var x0 = ic.inSub ? 96.75 + IpdSubPad : IpdLeft + 0.75;
                var t = Open(ic, 0);
                var lines = Wrap(ic, it.Text, it.Fs, IpdSheetW - 2 * IpdSubPad);
                foreach (var ln in lines)
                {
                    var pitch = it.Fs * 1.15;
                    if (t + pitch > IpdContentBottom) { BreakPage(ic); t = IpdContentTop; }
                    Run(ic, it.Fs, x0, t + 0.905 * it.Fs + 0.66, ln);
                    t += pitch;
                }
                ic.y = t;
                break;
            }
        }
    }
}
