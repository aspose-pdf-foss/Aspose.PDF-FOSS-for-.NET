using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// Task report drawing helpers: flattening, numbers, pages, text, rules, bands, pills, cards and page breaks.
    private static string Flat(TaskReportState tq, string s) => CollapseWs(DecodeEntities(
        Regex.Replace(s, "<[^>]+>", " "))).Trim();

    private static string N(TaskReportState tq, double v) => v.ToString("0.###", tq.inv);

    private static void OpenPage(TaskReportState tq)
    {
        var p = tq.doc.Pages.Add(tq.pageWidth, tq.pageHeight);
        EnsureFonts(p);
        var chrome = new StringBuilder();
        chrome.AppendLine(
            $"1 1 1 rg {N(tq, tq.left)} {N(tq, tq.pageHeight - tq.bottom)} {N(tq, tq.right - tq.left)} {N(tq, tq.bottom - tq.top)} re f");
        tq.pages.Add((p, chrome, new StringBuilder()));
    }

    private static double Y(TaskReportState tq, double yTd) => tq.pageHeight - yTd;

    private static void TrText(TaskReportState tq, double fs, double x, double baseTd, string t)
    {
        if (t.Length == 0) return;
        tq.pages[^1].Text.AppendLine($"BT 0.133 0.180 0.196 rg /F1 {fs.ToString("F2", tq.inv)} Tf "
            + $"1 0 0 1 {N(tq, x)} {N(tq, Y(tq, baseTd))} Tm ({EscapePdfString(t)}) Tj ET");
    }

    private static void HRule(TaskReportState tq, double x0, double x1, double yTd) => tq.pages[^1].Chrome.AppendLine(
        $"0.816 0.839 0.855 RG 0.75 w {N(tq, x0)} {N(tq, Y(tq, yTd))} m {N(tq, x1)} {N(tq, Y(tq, yTd))} l S");

    private static void VRule(TaskReportState tq, double x, double y0Td, double y1Td) => tq.pages[^1].Chrome.AppendLine(
        $"0.816 0.839 0.855 RG 0.75 w {N(tq, x)} {N(tq, Y(tq, y1Td))} m {N(tq, x)} {N(tq, Y(tq, y0Td))} l S");

    private static void Band(TaskReportState tq, double x0, double x1, double topTd) => tq.pages[^1].Chrome.AppendLine(
        $"0.906 0.918 0.925 rg {N(tq, x0)} {N(tq, Y(tq, topTd + TrBandHPt))} {N(tq, x1 - x0)} {N(tq, TrBandHPt)} re f");

    private static void Pill(TaskReportState tq, double x, double topTd) => tq.pages[^1].Chrome.AppendLine(
        $"0.816 0.839 0.855 RG 0.75 w {N(tq, x)} {N(tq, Y(tq, topTd + TrPillHPt))} {N(tq, TrPillWPt)} {N(tq, TrPillHPt)} re S");

    private static void CloseCardSides(TaskReportState tq, bool isTask, double? botTd)
    {
        var (l, r) = isTask ? (tq.taskL, tq.taskR) : (tq.stageL, tq.stageR);
        var from = (isTask ? tq.taskTopOnPage : tq.stageTopOnPage) ?? tq.top;
        var to = botTd is { } b ? b + TrHalfRulePt : tq.bottom;
        VRule(tq, l + TrHalfRulePt, from, to);
        VRule(tq, r - TrHalfRulePt, from, to);
        if (isTask) tq.taskTopOnPage = null; else tq.stageTopOnPage = null;
    }

    private static void PageBreak(TaskReportState tq, double carry)
    {
        if (tq.taskOpen) CloseCardSides(tq, isTask: true, botTd: null);
        if (tq.stageOpen) CloseCardSides(tq, isTask: false, botTd: null);
        OpenPage(tq);
        tq.y = tq.top + carry;
    }

    private static void OpenCard(TaskReportState tq, bool isTask, string title)
    {
        var (l, r) = isTask ? (tq.taskL, tq.taskR) : (tq.stageL, tq.stageR);
        if (tq.y + TrBandDropPt + TrBandHPt > tq.bottom) PageBreak(tq, 0);
        HRule(tq, l - TrHalfRulePt, r + TrHalfRulePt, tq.y);
        Band(tq, l + TrHalfRulePt, r - TrHalfRulePt, tq.y + TrBandDropPt);
        if (isTask) HRule(tq, l + TrHalfRulePt, r - TrHalfRulePt, tq.y + TrBandRulePt);
        TrText(tq, TrTitleFs, l + TrTitleInsetPt, tq.y + TrTitleDropPt, title);
        if (isTask) { tq.taskTopOnPage = tq.y - TrHalfRulePt; tq.taskOpen = true; }
        else { tq.stageTopOnPage = tq.y - TrHalfRulePt; tq.stageOpen = true; }
        tq.y += isTask ? TrTaskFirstLabelPt : TrStageFirstLabelPt;
    }

    private static void EmitRow(TaskReportState tq, List<TrCol> cols, double[] colX)
    {
        // the row carries onto the next page when its label line cannot seat
        if (tq.y + TrLabelFs * TrLineDescFrac > tq.bottom)
            PageBreak(tq, tq.y - tq.bottom);
        var labelBase = tq.y;
        var anyPill = false;
        var extra = 0;
        for (var c = 0; c < cols.Count && c < colX.Length; c++)
        {
            var col = cols[c];
            var x = tq.left + colX[c];
            TrText(tq, TrLabelFs, x, labelBase, col.Label);
            if (col.PillText.Length > 0)
            {
                anyPill = true;
                Pill(tq, x + TrPillInsetPt, labelBase + TrPillDropPt);
                TrText(tq, TrLabelFs, x + TrPillInsetPt + TrPillPadX,
                    labelBase + TrPillTextDropPt, col.PillText);
                continue;
            }
            var vb = labelBase + TrLabelValueGap;
            foreach (var line in col.Lines)
            {
                // a value line that cannot seat opens the next page alone
                if (vb + TrValueFs * TrLineDescFrac > tq.bottom)
                {
                    PageBreak(tq, 0);
                    vb = tq.top + TrLoneValueSeatPt;
                    labelBase = vb - TrLabelValueGap; // keep the pitch chain
                }
                TrText(tq, TrValueFs, x, vb, line);
                vb += TrValueLinePitch;
            }
            extra = Math.Max(extra, Math.Max(1, col.Lines.Count) - 1);
        }
        tq.lastValueBase = labelBase + TrLabelValueGap + extra * TrValueLinePitch;
        tq.y = labelBase + (anyPill ? TrPillRowPitch : TrRowPitch) + extra * TrValueLinePitch;
    }

    // wrap the long stage guidance at the measured width
    private static List<string> Wrap(TaskReportState tq, string text, double width, double fs)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var cur = "";
        foreach (var w in words)
        {
            var probe = cur.Length == 0 ? w : cur + " " + w;
            if (cur.Length > 0 && MeasureFaceText("Helvetica", probe, fs) > width)
            {
                lines.Add(cur);
                cur = w;
            }
            else cur = probe;
        }
        if (cur.Length > 0) lines.Add(cur);
        return lines;
    }
}
