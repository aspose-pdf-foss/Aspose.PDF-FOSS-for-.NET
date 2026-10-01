using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Contract invoice: one matched card rendered.</summary>
    private static bool RenderInvoiceCard(ContractInvoiceState cv, Match cM)
    {
        var ic = new InvoiceCardState();
        ic.inner = BalancedInner(cv.html, cM.Index + cM.Length, "div") ?? "";
        if (TryRenderInvoiceHeading(cv, ic, cM)) return true;

        ic.rows = new List<List<string>>();
        foreach (Match trM in Regex.Matches(ic.inner, @"<tr[^>]*>(.*?)</tr>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var cells = new List<string>();
            foreach (Match tdM in Regex.Matches(trM.Groups[1].Value, @"<td[^>]*>(.*?)</td>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline))
                cells.Add(CollapseWs(DecodeEntities(tdM.Groups[1].Value)).Trim());
            if (cells.Count > 0) ic.rows.Add(cells);
        }
        ic.ths = new List<string>();
        foreach (Match thM in Regex.Matches(ic.inner, @"<th(?![a-z])[^>]*>(.*?)</th>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
            ic.ths.Add(CollapseWs(DecodeEntities(thM.Groups[1].Value)).Trim());

        ic.nPlain = Math.Max(0, ic.rows.Count - 1);
        ic.lastRowH = CiRowHPt + 18.0;
        ic.blockH = CiH2AfterDividerPt + 38.7 + CiTheadHPt
            + ic.nPlain * CiRowHPt + ic.lastRowH + CiDividerGapPt;
        if (cv.first) { ic.h2Base = cv.y + CiH2AfterDividerPt; cv.first = false; }
        else
        {
            Line(cv, CiContentXPt, cv.y, CiContentXPt + CiContentWPt, cv.y, cv.blue, 1.5, true);
            if (cv.y + ic.blockH > cv.pageHeight - 90.0)
            {
                NewPage(cv);
                ic.h2Base = CiFreshTopBasePt;
            }
            else ic.h2Base = cv.y + CiH2AfterDividerPt;
        }

        ic.spans = Regex.Matches(ic.inner, @"<span(?<a>[^>]*)>(?<b>.*?)</span>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        ic.lbl1 = "";
        ic.val1 = "";
        ic.lbl2 = "";
        ic.val2 = "";
        foreach (Match sM in ic.spans)
        {
            var isDesc = sM.Groups["a"].Value.Contains("desc", StringComparison.OrdinalIgnoreCase);
            var lM = Regex.Match(sM.Groups["b"].Value, @"<label[^>]*>(.*?)</label>(.*)$",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!lM.Success) continue;
            var l = DecodeEntities(lM.Groups[1].Value);
            var v = CollapseWs(DecodeEntities(Regex.Replace(lM.Groups[2].Value, "<[^>]+>", " "))).Trim();
            if (isDesc) { ic.lbl2 = l; ic.val2 = v; } else { ic.lbl1 = l; ic.val1 = v; }
        }
        EmitRun(cv, ic.lbl1, 16, cv.regFace, cv.tX, ic.h2Base, cv.ink);
        EmitRun(cv, ic.val1, 16, cv.lightFace, cv.tX + W(cv, ic.lbl1, cv.regFace, 16), ic.h2Base, cv.ink);
        ic.line1W = W(cv, ic.lbl1, cv.regFace, 16) + W(cv, ic.val1, cv.lightFace, 16);
        ic.descW = W(cv, ic.lbl2, cv.regFace, 16) + W(cv, ic.val2, cv.lightFace, 16);
        ic.descSameLine = ic.line1W + ic.descW <= cv.tW;
        if (ic.lbl2.Length > 0 || ic.val2.Length > 0)
        {
            var dx = CiContentXPt + CiContentWPt - 22.5 - ic.descW;
            var dy2 = ic.descSameLine ? ic.h2Base : ic.h2Base + 19.5;
            EmitRun(cv, ic.lbl2, 16, cv.regFace, dx, dy2, cv.ink);
            EmitRun(cv, ic.val2, 16, cv.lightFace, dx + W(cv, ic.lbl2, cv.regFace, 16), dy2, cv.ink);
        }

        ic.thTop = ic.h2Base + (ic.descSameLine ? 16.0 : CiTableAfterH2Pt);
        FillRect(cv, cv.tX, ic.thTop, cv.tW, CiTheadHPt, cv.orange);
        DrawInvoiceTable(cv, ic);
        cv.y = ic.rTop + CiDividerGapPt;
        return true;
    }
}
