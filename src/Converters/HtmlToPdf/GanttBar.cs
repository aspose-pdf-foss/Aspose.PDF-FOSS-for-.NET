using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Gantt chart: one task bar drawn with its label.</summary>
    private static bool DrawGanttBar(GanttChartState gc, Match bar)
    {
        var st = bar.Groups["s"].Value;
        double W(string prop)
        {
            var pm = Regex.Match(st, @"(?<![-\w])" + prop + @"\s*:\s*(-?[\d.]+)px", RegexOptions.IgnoreCase);
            return pm.Success ? double.Parse(pm.Groups[1].Value, gc.inv) : 0;
        }
        var bx = gc.timelineX + GtPx(W("left"));
        var by = gc.dataTop + GtPx(W("top"));
        var bw = GtPx(W("width"));
        var bh = GtPx(W("height"));
        if (bw <= 0 || bh <= 0) return true;
        Fill(gc, GtBarFill, bx, by, bw, bh);
        gc.sb.Append(Compat.Format(gc.inv,
            $"q {GtBarBorderInk.R:0.###} {GtBarBorderInk.G:0.###} {GtBarBorderInk.B:0.###} RG 0.75 w " +
            $"{bx + 0.375:F2} {gc.pageHeight - by - 0.375:F2} {bw - 0.75:F2} {-(bh - 0.75):F2} re S Q\n"));

        var inner = bar.Groups["b"].Value;
        var prog = Regex.Match(inner,
            @"<div class=""gantt_task_progress""[^>]*style=""[^""]*width:\s*(?<w>[\d.]+)px",
            RegexOptions.IgnoreCase);
        if (prog.Success)
        {
            var pw = GtPx(double.Parse(prog.Groups["w"].Value, gc.inv));
            if (pw > 0)
                Fill(gc, GtProgressFill, bx + GtBarInsetPt, by + GtBarInsetPt,
                    System.Math.Min(pw, bw - 2 * GtBarInsetPt), bh - 2 * GtBarInsetPt);
        }

        var content = Regex.Match(inner,
            @"<div class=""gantt_task_content""[^>]*>(?<t>[^<]*)</div\s*>", RegexOptions.IgnoreCase);
        if (content.Success)
        {
            var text = Regex.Replace(DecodeEntities(content.Groups["t"].Value), @"\s+", " ").Trim();
            if (text.Length > 0)
            {
                var tw = MeasureFaceText("Arial", text, GtBarLabelPt);
                var tx = bx + (bw - tw) / 2;
                // Clip at the viewport: drop whole characters that fall past it.
                while (text.Length > 0 && tx + MeasureFaceText("Arial", text, GtBarLabelPt) > gc.viewportRight)
                    text = text[..^1];
                if (text.Length > 0)
                    GcText(gc, GtBarLabelPt, tx,
                        gc.pageHeight - (by + GtBarTextDropPt + GtBarLabelPt * ArialAscentEm),
                        text, "Arial", (1.0, 1.0, 1.0));
            }
        }
        return true;
    }
}
