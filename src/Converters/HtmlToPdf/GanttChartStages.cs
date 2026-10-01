using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Gantt chart: the dependency links drawn between task bars.</summary>
    private static void DrawGanttLinks(GanttChartState gc)
    {
        gc.linksArea = Regex.Match(gc.html,
            @"<div class=""gantt_links_area""[^>]*>(?<b>[\s\S]*)", RegexOptions.IgnoreCase);
        if (gc.linksArea.Success)
        {
            foreach (Match wrap in Regex.Matches(gc.linksArea.Groups["b"].Value,
                         @"<div class=""gantt_line_wrapper""[^>]*style=""(?<s>[^""]*)""[^>]*>\s*<div class=""gantt_link_line_(?<dir>\w+)""[^>]*style=""(?<ls>[^""]*)""",
                         RegexOptions.IgnoreCase))
            {
                double W(string style, string prop)
                {
                    var pm = Regex.Match(style, @"(?<![-\w])" + prop + @"\s*:\s*(-?[\d.]+)px", RegexOptions.IgnoreCase);
                    return pm.Success ? double.Parse(pm.Groups[1].Value, gc.inv) : 0;
                }
                var s0 = wrap.Groups["s"].Value;
                var ls = wrap.Groups["ls"].Value;
                var x0 = gc.timelineX + GtPx(W(s0, "left") + W(ls, "margin-left"));
                var y0 = gc.dataTop + GtPx(W(s0, "top") + W(ls, "margin-top"));
                var lw = GtPx(W(ls, "width"));
                var lh = GtPx(W(ls, "height"));
                if (lw > 0 && lh > 0) Fill(gc, GtLinkFill, x0, y0, lw, lh);
            }
        }
    }

    /// <summary>Gantt chart: the timeline scale labels drawn, clipped at the viewport.</summary>
    private static void DrawGanttTimelineScale(GanttChartState gc)
    {
        gc.taskScale = Regex.Match(gc.html,
            @"<div class=""gantt_scale_line""[^>]*>(?<b>[\s\S]*?)</div>\s*</div>",
            RegexOptions.IgnoreCase);
        {
            var cx = gc.timelineX;
            foreach (Match sc in Regex.Matches(gc.taskScale.Success ? gc.taskScale.Groups["b"].Value : "",
                         @"<div class=""gantt_scale_cell""[^>]*style=""[^""]*width:\s*(?<w>[\d.]+)px[^""]*""[^>]*>(?<t>[^<]*)</div\s*>",
                         RegexOptions.IgnoreCase))
            {
                var wpx = double.Parse(sc.Groups["w"].Value, gc.inv);
                var text = DecodeEntities(sc.Groups["t"].Value).Trim();
                // The scale clips at the timeline viewport, like the bars do — the
                // last visible label is 21:00, not the declared 22:00.
                if (cx >= gc.timelineX + GtPx(1044.66674804688)) break;
                if (text.Length > 0)
                {
                    var tw = MeasureFaceText("Arial", text, GtHeaderPt);
                    GcText(gc, GtHeaderPt, cx + (GtPx(wpx) - tw) / 2,
                        gc.pageHeight - (GtHeaderTopPt + GtHeaderTextDropPt + GtHeaderPt * ArialAscentEm),
                        text, "Arial", GtHeaderInk);
                }
                cx += GtPx(wpx);
            }
        }
    }

    /// <summary>Gantt chart: the widget frame, the timeline rules and the row separators.</summary>
    private static void DrawGanttFrame(GanttChartState gc)
    {
        gc.areaBottom = GtHeaderTopPt + GtPx(681.0) - 0.75;   // the .gantt_task declared height
        gc.frameRight = gc.timelineX + GtPx(1190.0) + 0.75;
        gc.frameBottom = 616.1;                            // the widget section's own box
        Stroke(gc, GtRuleInk, 0.75, gc.gridX - 1.1, GtHeaderTopPt - 1.1, gc.frameRight, GtHeaderTopPt - 1.1);
        Stroke(gc, GtRuleInk, 0.75, gc.gridX - 1.1, gc.frameBottom, gc.frameRight, gc.frameBottom);
        Stroke(gc, GtRuleInk, 0.75, gc.gridX - 1.1, GtHeaderTopPt - 1.1, gc.gridX - 1.1, gc.frameBottom);
        Stroke(gc, GtRuleInk, 0.75, gc.frameRight, GtHeaderTopPt - 1.1, gc.frameRight, gc.frameBottom);
        Stroke(gc, GtRuleInk, 0.75, gc.timelineX - 0.35, GtHeaderTopPt - 0.8, gc.timelineX - 0.35, gc.areaBottom);
        Stroke(gc, GtRuleInk, 0.75, gc.gridX - 0.7, gc.dataTop - 0.4, gc.timelineX - 0.75, gc.dataTop - 0.4);
        for (var k = 0; k <= 16; k++)
        {
            var lx = gc.timelineX + GtPx(k * 70.0);
            if (lx > gc.frameRight) break;
            Stroke(gc, GtGridLineInk, 0.75, lx, GtHeaderTopPt - 0.8, lx, gc.areaBottom);
        }
        for (var ri2 = 1; ri2 <= gc.rows.Count; ri2++)
        {
            var ly = gc.dataTop + GtPx(ri2 * GtRowHeightPx) - 0.85;
            Stroke(gc, GtGridLineInk, 0.75, gc.gridX - 0.7, ly, gc.timelineX - 0.75, ly);
            Stroke(gc, GtGridLineInk, 0.75, gc.timelineX, ly, gc.frameRight, ly);
        }
    }

    /// <summary>Gantt chart: the left grid - its head cells and one row per task.</summary>
    private static void DrawGanttGrid(GanttChartState gc)
    {
        gc.headCells = ParseGanttCells(gc.gridScale.Success ? gc.gridScale.Groups["b"].Value : "");
        gc.colX = new List<double>();
        gc.colW = new List<double>();
        {
            var cx = gc.gridX;
            foreach (var (wpx, _, _, _) in gc.headCells)
            {
                gc.colX.Add(cx);
                gc.colW.Add(GtPx(wpx));
                cx += GtPx(wpx);
            }
        }
        for (var i = 0; i < gc.headCells.Count; i++)
        {
            var text = gc.headCells[i].Text;
            if (text.Length == 0) continue;
            // Header cells CENTRE their label (probed: "Task name" at 133.49 on the
            // 156 px column, "Duration" at 290.49 on the 70 px one).
            var w = MeasureFaceText("Arial", text, GtHeaderPt);
            GcText(gc, GtHeaderPt, gc.colX[i] + (gc.colW[i] - w) / 2,
                gc.pageHeight - (GtHeaderTopPt + GtHeaderTextDropPt + GtHeaderPt * ArialAscentEm),
                text, "Arial", GtHeaderInk);
        }

        gc.rows = ParseGanttRows(gc.html);
        for (var ri = 0; ri < gc.rows.Count; ri++)
        {
            var bandTop = gc.dataTop + GtPx(ri * GtRowHeightPx);
            var cells = gc.rows[ri];
            for (var ci = 0; ci < cells.Count && ci < gc.colX.Count; ci++)
            {
                var (_, align, text, _) = cells[ci];
                if (text.Length == 0) continue;
                var w = MeasureFaceText("Arial", text, GtRowPt);
                double x;
                if (align == "center") x = gc.colX[ci] + (gc.colW[ci] - w) / 2;
                else x = gc.colX[ci] + GtPx(GtCellPadPx + cells[ci].IndentPx);
                // The cell clips its own text at its right edge.
                var clipR = gc.colX[ci] + gc.colW[ci];
                while (text.Length > 0 && x + MeasureFaceText("Arial", text, GtRowPt) > clipR)
                    text = text[..^1];
                if (text.Length == 0) continue;
                GcText(gc, GtRowPt, x, gc.pageHeight - (bandTop + GtRowTextDropPt + GtRowPt * ArialAscentEm),
                    text, "Arial", GtRowInk);
            }
        }
    }
}
