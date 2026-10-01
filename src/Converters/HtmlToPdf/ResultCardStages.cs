using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The box's trailing spacers advance the cursor; a broken footer image draws its alt text and a rule.</summary>
    private static void EmitResultCardFooter(ResultCardState rc)
    {
        foreach (Match dm in Regex.Matches(rc.boxHtml, @"<div\b[^>]*style\s*=\s*(['""])[^'""]*height\s*:\s*(\d+(?:\.\d+)?)px[^'""]*\1[^>]*>\s*</div>", RegexOptions.IgnoreCase))
            rc.y += double.Parse(dm.Groups[2].Value, rc.inv) * PxPt;
        rc.footerAlt = Regex.Match(rc.boxHtml, @"<img\b[^>]*alt\s*=\s*[""']([^""']+)[""'][^>]*>(?![\s\S]*<img)", RegexOptions.IgnoreCase);
        if (rc.footerAlt.Success)
        {
            Run(rc, "F5", 12, rc.contentX, rc.y + RcBrokenImgBaselinePt, rc.footerAlt.Groups[1].Value.Trim());
            HLine(rc, 96.0, 96.0 + rc.contentW, rc.y + RcBrokenImgBaselinePt + 3.83, Color.FromArgb(255, 255, 255));
        }
    }

    /// <summary>Paints the result box: its fill, then each label/value row at the row pitch with the rules under it.</summary>
    private static void EmitResultCardBox(ResultCardState rc)
    {
        rc.rowLine = ArialLine(rc, rc.labelFs);
        rc.rowPitch = rc.padTop + rc.padBottom + rc.rowLine;
        rc.boxH = rc.rows.Count * rc.rowPitch + 2 * rc.boxPad;
        rc.sb.AppendLine(Compat.Format(rc.inv,
            $"q {rc.boxCol.R / 255.0:0.###} {rc.boxCol.G / 255.0:0.###} {rc.boxCol.B / 255.0:0.###} rg " +
            $"{rc.contentX:F2} {rc.pageHeight - rc.y - rc.boxH:F2} {rc.boxPx + 2 * rc.boxPad:F2} {rc.boxH:F2} re f Q"));

        rc.tableX = rc.contentX + rc.boxPad;
        rc.labelW = 0;
        foreach (var (label, _) in rc.rows)
            rc.labelW = Math.Max(rc.labelW, MeasureFaceText("Arial Bold", label, rc.labelFs));
        rc.labelBoxW = rc.labelW + rc.padRight + 12.0;
        rc.valueX = rc.tableX + rc.labelBoxW + 0.75;
        rc.rowTop = rc.y + rc.boxPad;
        rc.innerW = rc.boxPx - 2 * 0.75;
        foreach (var (label, value) in rc.rows)
        {
            var baseTd = rc.rowTop + rc.padTop + Drop(rc, rc.labelFs, rc.rowLine, rc.arial);
            Run(rc, "F9", rc.labelFs, rc.tableX + 0.75, baseTd, label);
            if (value.Length > 0) Run(rc, "F8", rc.labelFs, rc.valueX, baseTd, value);
            var rowBot = rc.rowTop + rc.rowPitch;
            HLine(rc, rc.tableX, rc.tableX + rc.labelBoxW, rowBot, Color.FromArgb(255, 255, 255));
            HLine(rc, rc.tableX + rc.labelBoxW, rc.tableX + rc.innerW, rowBot, Color.FromArgb(255, 255, 255));
            rc.rowTop = rowBot;
        }
    }

    /// <summary>The stacked divs before the box: pixel-height spacers advance the cursor, text divs draw at their declared Arial size with bold spans.</summary>
    private static void EmitResultCardParagraphs(ResultCardState rc)
    {
        foreach (Match dm in Regex.Matches(rc.body[..rc.boxStart],
            @"<div\b([^>]*)>([\s\S]*?)</div>", RegexOptions.IgnoreCase))
        {
            var attrs = dm.Groups[1].Value;
            var inner = dm.Groups[2].Value;
            var st = Regex.Match(attrs, @"style\s*=\s*(['""])([^'""]*)\1").Groups[2].Value;
            var hm = Regex.Match(st, @"height\s*:\s*(\d+(?:\.\d+)?)px", RegexOptions.IgnoreCase);
            if (hm.Success && Regex.Replace(inner, @"<[^>]+>", "").Trim().Length == 0)
            {
                rc.y += double.Parse(hm.Groups[1].Value, rc.inv) * PxPt;
                continue;
            }
            if (Regex.Replace(inner, @"<[^>]+>", "").Trim().Length == 0) continue;
            // line-height:N% pitches the div's lines; each span carries its size/weight
            var lhPct = Regex.Match(st, @"line-height\s*:\s*(\d+)\s*%", RegexOptions.IgnoreCase) is
                { Success: true } lm ? double.Parse(lm.Groups[1].Value, rc.inv) / 100.0 : 0;
            var divFsM = Regex.Match(st, @"font-size\s*:\s*(\d+(?:\.\d+)?)px", RegexOptions.IgnoreCase);
            var divFs = divFsM.Success ? double.Parse(divFsM.Groups[1].Value, rc.inv) * PxPt : 12.0;
            var divBold = st.Contains("font-weight:bold", StringComparison.OrdinalIgnoreCase)
                || Regex.IsMatch(st, @"font-weight\s*:\s*bold", RegexOptions.IgnoreCase);
            var divCol = Regex.Match(st, @"(?<![-\w])color\s*:\s*(#[0-9a-fA-F]{3,6})") is
                { Success: true } cm ? ParseCssColor(cm.Groups[1].Value) : null;
            // segment the div into lines at <br> boundaries; spans style their line
            foreach (var seg in Regex.Split(inner, @"<br\s*/?>", RegexOptions.IgnoreCase))
            {
                var segFs = divFs; var segBold = divBold;
                var spanFsM = Regex.Match(seg, @"font-size\s*:\s*(\d+(?:\.\d+)?)px", RegexOptions.IgnoreCase);
                if (spanFsM.Success) segFs = double.Parse(spanFsM.Groups[1].Value, rc.inv) * PxPt;
                if (Regex.IsMatch(seg, @"font-weight\s*:\s*bold", RegexOptions.IgnoreCase)) segBold = true;
                var textSeg = CollapseWs(DecodeEntities(Regex.Replace(seg, @"<[^>]+>", " ")));
                if (textSeg.Length == 0) continue;
                var box = lhPct > 0 ? segFs * lhPct : ArialLine(rc, segFs);
                var drop = Drop(rc, segFs, box, rc.arial);
                Run(rc, segBold ? "F9" : "F8", segFs, rc.contentX, rc.y + drop, textSeg, divCol);
                rc.y += box;
            }
        }
    }
}
