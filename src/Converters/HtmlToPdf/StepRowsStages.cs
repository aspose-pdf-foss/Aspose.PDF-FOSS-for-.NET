using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Renders one step row: the bullet, then a detail table, a heading with an optional underline, a bordered attribute table or a paragraph in its content column, breaking the page when the row runs past the limit.</summary>
    private static bool RenderStepRow(StepRowsState sw, Match srM)
    {
        sw.srEnd = DivClose(sw.html, srM.Index + srM.Length);
        sw.srBody = sw.html[(srM.Index + srM.Length)..sw.srEnd];
        sw.bulletM = Regex.Match(sw.srBody,
            @"class\s*=\s*['""]sr-bullet['""][^>]*>(?<t>[\s\S]*?)</div>", RegexOptions.IgnoreCase);
        sw.bullet = sw.bulletM.Success ? Flat(sw, sw.bulletM.Groups["t"].Value) : "";
        sw.scM = Regex.Match(sw.srBody,
            @"<div\b[^>]*class\s*=\s*['""]sr-content['""][^>]*>", RegexOptions.IgnoreCase);
        if (!sw.scM.Success) return true;
        sw.scEnd = DivClose(sw.srBody, sw.scM.Index + sw.scM.Length);
        sw.content = sw.srBody[(sw.scM.Index + sw.scM.Length)..sw.scEnd];

        if (sw.yTd > sw.contentTop + 0.1) sw.yTd += SrRowMarginPt;
        sw.rowTop = sw.yTd;
        if (IsProcedurePage(sw)) { RenderProcedureStep(sw); return true; }
        if (sw.bullet.Length > 0)
            Run(sw, "FA", 12, sw.bulletX, sw.rowTop, sw.bullet);

        sw.detM = Regex.Match(sw.content,
            @"<table\b[^>]*class\s*=\s*['""]swdt-table['""][^>]*>", RegexOptions.IgnoreCase);
        if (sw.detM.Success)
        {
            // step 1: the widget label line, then the centred detable
            var lblM = Regex.Match(sw.content,
                @"class\s*=\s*['""]swdt-label['""][^>]*>(?<t>[^<]*)", RegexOptions.IgnoreCase);
            if (lblM.Success && Flat(sw, lblM.Groups["t"].Value).Length > 0)
                Run(sw, "FA", 12, sw.contentX, sw.rowTop, Flat(sw, lblM.Groups["t"].Value));
            sw.yTd = sw.rowTop + SrLinePt;
            var tEnd = sw.content.IndexOf("</table>",
                sw.detM.Index, StringComparison.OrdinalIgnoreCase);
            var tableHtml = sw.content[sw.detM.Index..(tEnd < 0 ? sw.content.Length : tEnd)];
            sw.yTd = RenderDetable(sw, tableHtml, sw.yTd, sw.limit);
            return true;
        }
        sw.headM = Regex.Match(sw.content,
            @"<h(?<n>\d)\b[^>]*>(?<b>[\s\S]*?)</h\k<n>>", RegexOptions.IgnoreCase);
        sw.consumedHead = false;
        if (sw.headM.Success)
        {
            RenderStepHeading(sw);
        }
        sw.atM = Regex.Match(sw.content, @"<table\b[^>]*border\s*=\s*[""']1[""'][^>]*>",
            RegexOptions.IgnoreCase);
        if (sw.atM.Success)
        {
            RenderStepAttributeTable(sw);
        }
        else if (!sw.consumedHead)
        {
            // a bare text step: its paragraph lines at the content column
            var txt = Flat(sw, sw.content);
            if (txt.Length > 0)
            {
                var li = 0;
                foreach (var ln in MeasuredWordWrap(txt, SrContentWPt, "Arial", 12))
                    Run(sw, "FA", 12, sw.contentX, sw.rowTop + li++ * SrLinePt, ln);
                sw.yTd = sw.rowTop + Math.Max(1, li) * SrLinePt;
            }
            else sw.yTd = sw.rowTop + SrLinePt;
        }
        return true;
    }

    /// <summary>A bordered attribute table in the content column: each row's cells at their percent widths, word-wrapped, with the cell rules.</summary>
    private static void RenderStepAttributeTable(StepRowsState sw)
    {
        var tEnd = sw.content.IndexOf("</table>", sw.atM.Index, StringComparison.OrdinalIgnoreCase);
        var tableHtml = sw.content[sw.atM.Index..(tEnd < 0 ? sw.content.Length : tEnd)];
        var tTop = sw.consumedHead ? sw.yTd : sw.rowTop;
        foreach (Match trM in Regex.Matches(tableHtml, @"<tr\b[^>]*>(?<r>[\s\S]*?)</tr>",
            RegexOptions.IgnoreCase))
        {
            var cells = new List<(double frac, string text)>();
            foreach (Match tdM in Regex.Matches(trM.Groups["r"].Value,
                @"<td\b(?<a>[^>]*)>(?<c>[\s\S]*?)</td>", RegexOptions.IgnoreCase))
            {
                var fr = Regex.Match(tdM.Groups["a"].Value, @"width:\s*([\d.]+)%")
                    is { Success: true } fM
                    ? double.Parse(fM.Groups[1].Value, sw.invc) / 100.0
                    : 0.5;
                cells.Add((fr, Flat(sw, tdM.Groups["c"].Value)));
            }
            if (cells.Count == 0) continue;
            var cellX = sw.contentX + AttrCellInsetPt;
            var rowLines = 1;
            foreach (var (frac, text) in cells)
            {
                var cw = SrContentWPt * frac - 2 * AttrCellInsetPt;
                var lines = MeasuredWordWrap(text, cw, "Arial", 12);
                for (var li = 0; li < lines.Length; li++)
                    if (lines[li].Trim().Length > 0)
                        Run(sw, "FA", 12, cellX + (li > 0 ? 6 : 0),
                            tTop + AttrCellSeatPt + li * SrLinePt, lines[li].Trim());
                rowLines = Math.Max(rowLines, lines.Length);
                cellX += SrContentWPt * frac;
            }
            tTop += rowLines * SrLinePt + AttrRowGapPt;
        }
        sw.yTd = tTop;
    }

    /// <summary>A heading in the content column, centred at its level's size, bold and underlined as marked.</summary>
    private static void RenderStepHeading(StepRowsState sw)
    {
        var hn = sw.headM.Groups["n"].Value;
        var hText = Flat(sw, sw.headM.Groups["b"].Value);
        var bold = Regex.IsMatch(sw.headM.Groups["b"].Value, @"<strong\b", RegexOptions.IgnoreCase);
        var under = Regex.IsMatch(sw.headM.Groups["b"].Value, @"<u\b", RegexOptions.IgnoreCase);
        // the engine's own heading scale on the 12pt body (measured)
        var hFs = hn switch { "3" => 19.95, "4" => 16.12, "6" => 10.95, _ => 12.0 };
        var hW = MeasureFaceText(bold ? "Arial-Bold" : "Arial", hText, hFs);
        var hX = sw.contentX + (SrContentWPt - hW) / 2;
        var hTop = sw.rowTop + SrHeadSeatPt;
        Run(sw, bold ? "FB" : "FA", hFs, hX, hTop, hText);
        if (under)
            HLine(sw, hX, hX + hW, hTop + hFs * (ArialAscentEm + 0.11));
        // the first attribute-table row's glyph opens fs·1.15 + 0.6 below
        sw.yTd = hTop + hFs * 1.15 + 0.6;
        sw.consumedHead = true;
    }
}
