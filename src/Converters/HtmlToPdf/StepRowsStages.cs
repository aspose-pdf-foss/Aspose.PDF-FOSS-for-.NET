using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Renders one step row: the bullet, then a detail table, a heading with an optional underline, a bordered attribute table or a paragraph in its content column; a row that does not fit the space left opens the next page.</summary>
    private static bool RenderStepRow(StepRowsState sw, Match srM)
    {
        sw.srEnd = DivClose(sw.html, srM.Index + srM.Length);
        sw.srBody = sw.html[(srM.Index + srM.Length)..sw.srEnd];
        sw.bulletM = SafeRegex.Match(sw.srBody,
            @"class\s*=\s*['""]sr-bullet['""][^>]*>(?<t>[\s\S]*?)</div>", RegexOptions.IgnoreCase);
        sw.bullet = sw.bulletM.Success ? Flat(sw.bulletM.Groups["t"].Value) : "";
        sw.scM = SafeRegex.Match(sw.srBody,
            @"<div\b[^>]*class\s*=\s*['""]sr-content['""][^>]*>", RegexOptions.IgnoreCase);
        if (!sw.scM.Success) return true;
        sw.scEnd = DivClose(sw.srBody, sw.scM.Index + sw.scM.Length);
        sw.content = sw.srBody[(sw.scM.Index + sw.scM.Length)..sw.scEnd];

        if (sw.yTd > sw.contentTop + SrPageTopTolerancePt) sw.yTd += SrRowMarginPt;
        sw.rowTop = sw.yTd;
        if (IsProcedurePage(sw)) { RenderProcedureStep(sw); return true; }

        sw.detM = SafeRegex.Match(sw.content,
            @"<table\b[^>]*class\s*=\s*['""]swdt-table['""][^>]*>", RegexOptions.IgnoreCase);
        sw.headM = SafeRegex.Match(sw.content,
            @"<h(?<n>\d)\b[^>]*>(?<b>[\s\S]*?)</h\k<n>>", RegexOptions.IgnoreCase);
        sw.atM = SafeRegex.Match(sw.content, @"<table\b[^>]*border\s*=\s*[""']1[""'][^>]*>",
            RegexOptions.IgnoreCase);
        sw.consumedHead = false;
        var flow = !sw.detM.Success && !sw.headM.Success && !sw.atM.Success ? StepFlowItems(sw) : null;
        if (!sw.detM.Success) FitStepRowOnPage(sw, StepRowHeightPt(sw, flow));
        if (sw.bullet.Length > 0)
            Run(sw, "FA", SrBodyFsPt, sw.bulletX, sw.rowTop, sw.bullet);

        if (sw.detM.Success)
        {
            // step 1: the widget caption and label lines, then the centred detable
            var capH = RenderDetableCaption(sw);
            var lblM = SafeRegex.Match(sw.content,
                @"class\s*=\s*['""]swdt-label['""][^>]*>(?<t>[^<]*)", RegexOptions.IgnoreCase);
            if (lblM.Success && Flat(lblM.Groups["t"].Value).Length > 0)
                Run(sw, "FA", SrBodyFsPt, sw.contentX, sw.rowTop + capH, Flat(lblM.Groups["t"].Value));
            sw.yTd = sw.rowTop + capH + SrLinePt;
            var tEnd = sw.content.IndexOf("</table>",
                sw.detM.Index, StringComparison.OrdinalIgnoreCase);
            var tableHtml = sw.content[sw.detM.Index..(tEnd < 0 ? sw.content.Length : tEnd)];
            sw.yTd = RenderDetable(sw, tableHtml, sw.yTd, sw.limit);
            return true;
        }
        sw.consumedHead = false;
        if (sw.headM.Success)
        {
            RenderStepHeading(sw);
        }
        if (sw.atM.Success)
        {
            sw.yTd = RenderStepAttributeTable(sw, sw.consumedHead ? sw.yTd : sw.rowTop);
        }
        else if (flow is not null) RenderStepFlow(sw, flow);
        else if (!sw.consumedHead)
        {
            // a bare text step: its paragraph lines at the content column
            var txt = Flat(sw.content);
            if (txt.Length > 0)
            {
                var li = 0;
                foreach (var ln in MeasuredWordWrap(txt, SrContentWPt, "Arial", SrBodyFsPt))
                    Run(sw, "FA", SrBodyFsPt, sw.contentX, sw.rowTop + li++ * SrLinePt, ln);
                sw.yTd = sw.rowTop + Math.Max(1, li) * SrLinePt;
            }
            else sw.yTd = sw.rowTop + SrLinePt;
        }
        return true;
    }

    /// <summary>The height the row will take from its top: its heading, caption and attribute table, its
    /// pictures and text runs, or its wrapped paragraph.</summary>
    private static double StepRowHeightPt(StepRowsState sw, List<StepFlowItem>? flow)
    {
        if (flow is not null) return StepFlowHeightPt(flow);
        var h = sw.headM.Success ? SrHeadSeatPt + StepHeadingFsPt(sw.headM.Groups["n"].Value) * StepHeadingLineEm + StepHeadingGapPt : 0;
        sw.consumedHead = sw.headM.Success;
        if (sw.atM.Success) return RenderStepAttributeTable(sw, sw.rowTop + h, draw: false) - sw.rowTop;
        return sw.headM.Success ? h : StepBareTextHeightPt(sw);
    }

    /// <summary>A bordered attribute table in the content column from <paramref name="top"/>, under its
    /// caption: each row's cells at their percent widths, word-wrapped, with the cell rules; a row that runs
    /// past the page foot opens the next page. Returns the y under the table.</summary>
    private static double RenderStepAttributeTable(StepRowsState sw, double top, bool draw = true)
    {
        var tEnd = sw.content.IndexOf("</table>", sw.atM.Index, StringComparison.OrdinalIgnoreCase);
        var tableHtml = sw.content[sw.atM.Index..(tEnd < 0 ? sw.content.Length : tEnd)];
        var tTop = top;
        tTop = RenderStepCaption(sw, StepTableCaptionHtml(sw), tTop, draw);
        foreach (Match trM in SafeRegex.Matches(tableHtml, @"<tr\b[^>]*>(?<r>[\s\S]*?)</tr>",
            RegexOptions.IgnoreCase))
        {
            var cells = new List<(double frac, string[] lines)>();
            foreach (var groups in SafeRegex.Matches(trM.Groups["r"].Value,
                @"<td\b(?<a>[^>]*)>(?<c>[\s\S]*?)</td>", RegexOptions.IgnoreCase).Select(tdM => tdM.Groups))
            {
                var fr = SafeRegex.Match(groups["a"].Value, @"width:\s*([\d.]+)%")
                    is { Success: true } fM
                    ? double.Parse(fM.Groups[1].Value, sw.invc) / 100.0
                    : 0.5;
                cells.Add((fr, MeasuredWordWrap(Flat(groups["c"].Value), SrContentWPt * fr - 2 * AttrCellInsetPt, "Arial", SrBodyFsPt)));
            }
            if (cells.Count == 0) continue;
            var rowLines = 1;
            foreach (var (_, lines) in cells) rowLines = Math.Max(rowLines, lines.Length);
            var rowH = rowLines * SrLinePt + AttrRowGapPt;
            if (draw && tTop + rowH > sw.limit && tTop > sw.contentTop + SrPageTopTolerancePt)
            {
                NewPage(sw);
                tTop = sw.contentTop;
            }
            var cellX = sw.contentX + AttrCellInsetPt;
            foreach (var (frac, lines) in cells)
            {
                for (var li = 0; draw && li < lines.Length; li++)
                    if (lines[li].Trim().Length > 0)
                        Run(sw, "FA", SrBodyFsPt, cellX + (li > 0 ? 6 : 0),
                            tTop + AttrCellSeatPt + li * SrLinePt, lines[li].Trim());
                cellX += SrContentWPt * frac;
            }
            tTop += rowH;
        }
        return tTop;
    }

    // the engine's own heading scale on the 12pt body (measured), and its line: the first
    // attribute-table row's glyph opens fs·1.15 + 0.6 below the heading's top
    private const double StepHeadingLineEm = 1.15;
    private const double StepHeadingGapPt = 0.6;

    private static double StepHeadingFsPt(string level)
        => level switch { "3" => 19.95, "4" => 16.12, "6" => 10.95, _ => 12.0 };

    /// <summary>A heading in the content column, centred at its level's size, bold and underlined as marked.</summary>
    private static void RenderStepHeading(StepRowsState sw)
    {
        var hText = Flat(sw.headM.Groups["b"].Value);
        var bold = SafeRegex.IsMatch(sw.headM.Groups["b"].Value, @"<strong\b", RegexOptions.IgnoreCase);
        var under = SafeRegex.IsMatch(sw.headM.Groups["b"].Value, @"<u\b", RegexOptions.IgnoreCase);
        var hFs = StepHeadingFsPt(sw.headM.Groups["n"].Value);
        var hW = MeasureFaceText(bold ? "Arial-Bold" : "Arial", hText, hFs);
        var hX = sw.contentX + (SrContentWPt - hW) / 2;
        var hTop = sw.rowTop + SrHeadSeatPt;
        Run(sw, bold ? "FB" : "FA", hFs, hX, hTop, hText);
        if (under)
            HLine(sw, hX, hX + hW, hTop + hFs * (ArialAscentEm + 0.11));
        sw.yTd = hTop + hFs * StepHeadingLineEm + StepHeadingGapPt;
        sw.consumedHead = true;
    }
}
