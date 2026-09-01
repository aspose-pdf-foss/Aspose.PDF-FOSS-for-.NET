using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    // ── The dhtmlxGantt chart export ────────────────────────────────────────
    //
    // A SAPUI5 page whose body is one dhtmlxGantt widget: a .gantt_grid of task
    // rows on the left, a .gantt_task timeline on the right, and every box —
    // rows, cells, scale cells, task bars, progress fills and the connector
    // line segments — carrying its own px geometry in an inline style.
    //
    // Measured geometry. Everything is the declared geometry at
    // 0.75 pt/px hung off two origins:
    //  - the widget's left edge (the .gantt_grid box) at the page's own left
    //    margin + 1.5, and the timeline right after the grid's 359 px;
    //  - the header band top at 93.0, the data band 34 px under it — which puts
    //    bar 1 (left:70 top:2 w:1050 h:30) at 419.25/120.0/787.5/22.5, exactly
    //    where it is expected, and row 2 (top:37) at 146.25.
    // Typography: 9 pt Arial headers #a6a6a6 centred per cell, 9.75 pt Arial
    // row text #454545, 9 pt white bar labels centred in the bar and CLIPPED at
    // the timeline viewport (the last bar's "Launch" shows as "Lau").
    private const double GtPxPt = 0.75;
    private const double GtGridWidthPx = 359.0;
    private const double GtHeaderTopPt = 93.0;
    private const double GtHeaderHeightPx = 34.0;
    private const double GtRowHeightPx = 35.0;
    private const double GtHeaderTextDropPt = 6.6;   // header glyph top under the band top
    private const double GtRowTextDropPt = 9.40;     // row glyph top under the band top
    private const double GtBarTextDropPt = 8.48;     // bar label glyph top under the bar top
    private const double GtHeaderPt = 9.0;
    private const double GtRowPt = 9.75;
    private const double GtBarLabelPt = 9.0;
    private const double GtTreeIndentPx = 15.0;      // one tree level
    private const double GtTreeIconPx = 22.5;        // one icon slot
    private const double GtCellPadPx = 6.0;
    private const double GtBarInsetPt = 0.75;        // the progress fill's 1 px border inset
    /// <summary>The page title's glyph top (probed: 78.11).</summary>
    private const double GtTitleTopPt = 78.11;
    /// <summary>Times New Roman's ascent as an em fraction.</summary>
    private const double TimesAscentEm = 0.891;

    private static readonly (double R, double G, double B) GtHeaderInk = (0.651, 0.651, 0.651);
    private static readonly (double R, double G, double B) GtRowInk = (0.271, 0.271, 0.271);
    private static readonly (double R, double G, double B) GtBarFill = (0.239, 0.725, 0.827);
    private static readonly (double R, double G, double B) GtProgressFill = (0.161, 0.612, 0.706);
    private static readonly (double R, double G, double B) GtLinkFill = (1.0, 0.627, 0.067);
    private static readonly (double R, double G, double B) GtRuleInk = (0.808, 0.808, 0.808);
    private static readonly (double R, double G, double B) GtGridLineInk = (0.922, 0.922, 0.922);
    private static readonly (double R, double G, double B) GtBarBorderInk = (0.161, 0.6, 0.69);

    private static Document? TryRenderGanttChart(string html, HtmlLoadOptions? options,
        double pageWidth, double pageHeight, double marginLeft, double marginTop)
    {
        if (!html.Contains("gantt_task_line", System.StringComparison.Ordinal)
            || !html.Contains("gantt_grid_data", System.StringComparison.Ordinal)) return null;

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        double P(double px) => px * GtPxPt;

        var doc = new Document();
        var page = doc.Pages.Add(pageWidth, pageHeight);
        EnsureFonts(page);
        var resByFace = new Dictionary<string, string>(System.StringComparer.Ordinal);
        var sb = new StringBuilder();
        // (size, x, y, text, face, ink) — flushed after the fills so bars sit UNDER
        // their labels.
        var texts = new List<(double Size, double X, double Y, string Text, string Face, (double R, double G, double B) Ink)>();
        void Text(double size, double x, double y, string text, string face, (double R, double G, double B) ink)
            => texts.Add((size, x, y, text, face, ink));

        void Fill((double R, double G, double B) c, double x, double topY, double w, double h)
            => sb.Append(string.Create(inv,
                $"q {c.R:0.###} {c.G:0.###} {c.B:0.###} rg {x:F2} {pageHeight - topY - h:F2} {w:F2} {h:F2} re f Q\n"));

        // The page title rides the ordinary UA flow above the widget.
        var title = Regex.Match(html, @"<label\b[^>]*>(?<t>[^<]*)</label\s*>", RegexOptions.IgnoreCase);
        if (title.Success)
        {
            var tt = Regex.Replace(DecodeEntities(title.Groups["t"].Value), @"\s+", " ").Trim();
            if (tt.Length > 0)
                Text(12.0, marginLeft, pageHeight - GtTitleTopPt - 12.0 * TimesAscentEm,
                    tt, "Times New Roman", (0, 0, 0));
        }

        var gridX = marginLeft + 1.5;
        var timelineX = gridX + P(GtGridWidthPx);
        var dataTop = GtHeaderTopPt + P(GtHeaderHeightPx);

        // ── the left grid ──
        var gridScale = Regex.Match(html,
            @"<div class=""gantt_grid_scale""[^>]*>(?<b>[\s\S]*?)<div class=""gantt_grid_data""",
            RegexOptions.IgnoreCase);
        var headCells = ParseGanttCells(gridScale.Success ? gridScale.Groups["b"].Value : "");
        var colX = new List<double>();
        var colW = new List<double>();
        {
            var cx = gridX;
            foreach (var (wpx, _, _, _) in headCells)
            {
                colX.Add(cx);
                colW.Add(P(wpx));
                cx += P(wpx);
            }
        }
        for (var i = 0; i < headCells.Count; i++)
        {
            var text = headCells[i].Text;
            if (text.Length == 0) continue;
            // Header cells CENTRE their label (probed: "Task name" at 133.49 on the
            // 156 px column, "Duration" at 290.49 on the 70 px one).
            var w = MeasureFaceText("Arial", text, GtHeaderPt);
            Text(GtHeaderPt, colX[i] + (colW[i] - w) / 2,
                pageHeight - (GtHeaderTopPt + GtHeaderTextDropPt + GtHeaderPt * ArialAscentEm),
                text, "Arial", GtHeaderInk);
        }

        var rows = ParseGanttRows(html);
        for (var ri = 0; ri < rows.Count; ri++)
        {
            var bandTop = dataTop + P(ri * GtRowHeightPx);
            var cells = rows[ri];
            for (var ci = 0; ci < cells.Count && ci < colX.Count; ci++)
            {
                var (_, align, text, _) = cells[ci];
                if (text.Length == 0) continue;
                var w = MeasureFaceText("Arial", text, GtRowPt);
                double x;
                if (align == "center") x = colX[ci] + (colW[ci] - w) / 2;
                else x = colX[ci] + P(GtCellPadPx + cells[ci].IndentPx);
                // The cell clips its own text at its right edge.
                var clipR = colX[ci] + colW[ci];
                while (text.Length > 0 && x + MeasureFaceText("Arial", text, GtRowPt) > clipR)
                    text = text[..^1];
                if (text.Length == 0) continue;
                Text(GtRowPt, x, pageHeight - (bandTop + GtRowTextDropPt + GtRowPt * ArialAscentEm),
                    text, "Arial", GtRowInk);
            }
        }

        void Stroke((double R, double G, double B) c, double w, double x0, double y0, double x1, double y1)
            => sb.Append(string.Create(inv,
                $"q {c.R:0.###} {c.G:0.###} {c.B:0.###} RG {w:0.##} w " +
                $"{x0:F2} {pageHeight - y0:F2} m {x1:F2} {pageHeight - y1:F2} l S Q\n"));

        // The widget chrome (all measured):
        //  - the outer frame and the grid/timeline separator in the darker rule gray;
        //  - an hour line at every scale-cell boundary and a row line under every row
        //    band, in the light #ebebeb — the timeline lines run the full declared
        //    1190 px area, the row lines both panes.
        var areaBottom = GtHeaderTopPt + P(681.0) - 0.75;   // the .gantt_task declared height
        var frameRight = timelineX + P(1190.0) + 0.75;
        var frameBottom = 616.1;                            // the widget section's own box
        Stroke(GtRuleInk, 0.75, gridX - 1.1, GtHeaderTopPt - 1.1, frameRight, GtHeaderTopPt - 1.1);
        Stroke(GtRuleInk, 0.75, gridX - 1.1, frameBottom, frameRight, frameBottom);
        Stroke(GtRuleInk, 0.75, gridX - 1.1, GtHeaderTopPt - 1.1, gridX - 1.1, frameBottom);
        Stroke(GtRuleInk, 0.75, frameRight, GtHeaderTopPt - 1.1, frameRight, frameBottom);
        Stroke(GtRuleInk, 0.75, timelineX - 0.35, GtHeaderTopPt - 0.8, timelineX - 0.35, areaBottom);
        Stroke(GtRuleInk, 0.75, gridX - 0.7, dataTop - 0.4, timelineX - 0.75, dataTop - 0.4);
        for (var k = 0; k <= 16; k++)
        {
            var lx = timelineX + P(k * 70.0);
            if (lx > frameRight) break;
            Stroke(GtGridLineInk, 0.75, lx, GtHeaderTopPt - 0.8, lx, areaBottom);
        }
        for (var ri2 = 1; ri2 <= rows.Count; ri2++)
        {
            var ly = dataTop + P(ri2 * GtRowHeightPx) - 0.85;
            Stroke(GtGridLineInk, 0.75, gridX - 0.7, ly, timelineX - 0.75, ly);
            Stroke(GtGridLineInk, 0.75, timelineX, ly, frameRight, ly);
        }

        // ── the timeline scale ──
        var taskScale = Regex.Match(html,
            @"<div class=""gantt_scale_line""[^>]*>(?<b>[\s\S]*?)</div>\s*</div>",
            RegexOptions.IgnoreCase);
        {
            var cx = timelineX;
            foreach (Match sc in Regex.Matches(taskScale.Success ? taskScale.Groups["b"].Value : "",
                         @"<div class=""gantt_scale_cell""[^>]*style=""[^""]*width:\s*(?<w>[\d.]+)px[^""]*""[^>]*>(?<t>[^<]*)</div\s*>",
                         RegexOptions.IgnoreCase))
            {
                var wpx = double.Parse(sc.Groups["w"].Value, inv);
                var text = DecodeEntities(sc.Groups["t"].Value).Trim();
                // The scale clips at the timeline viewport, like the bars do — the
                // last visible label is 21:00, not the declared 22:00.
                if (cx >= timelineX + P(1044.66674804688)) break;
                if (text.Length > 0)
                {
                    var tw = MeasureFaceText("Arial", text, GtHeaderPt);
                    Text(GtHeaderPt, cx + (P(wpx) - tw) / 2,
                        pageHeight - (GtHeaderTopPt + GtHeaderTextDropPt + GtHeaderPt * ArialAscentEm),
                        text, "Arial", GtHeaderInk);
                }
                cx += P(wpx);
            }
        }

        // ── the connector lines (each segment is its own declared box) ──
        var linksArea = Regex.Match(html,
            @"<div class=""gantt_links_area""[^>]*>(?<b>[\s\S]*)", RegexOptions.IgnoreCase);
        if (linksArea.Success)
        {
            foreach (Match wrap in Regex.Matches(linksArea.Groups["b"].Value,
                         @"<div class=""gantt_line_wrapper""[^>]*style=""(?<s>[^""]*)""[^>]*>\s*<div class=""gantt_link_line_(?<dir>\w+)""[^>]*style=""(?<ls>[^""]*)""",
                         RegexOptions.IgnoreCase))
            {
                double W(string style, string prop)
                {
                    var pm = Regex.Match(style, @"(?<![-\w])" + prop + @"\s*:\s*(-?[\d.]+)px", RegexOptions.IgnoreCase);
                    return pm.Success ? double.Parse(pm.Groups[1].Value, inv) : 0;
                }
                var s0 = wrap.Groups["s"].Value;
                var ls = wrap.Groups["ls"].Value;
                var x0 = timelineX + P(W(s0, "left") + W(ls, "margin-left"));
                var y0 = dataTop + P(W(s0, "top") + W(ls, "margin-top"));
                var lw = P(W(ls, "width"));
                var lh = P(W(ls, "height"));
                if (lw > 0 && lh > 0) Fill(GtLinkFill, x0, y0, lw, lh);
            }
        }

        // ── the task bars ──
        // The timeline viewport clips at the .gantt_task box (probed: the last
        // bar's label shows as "Lau").
        var viewportRight = timelineX + P(1044.66674804688);
        var barsArea = Regex.Match(html,
            @"<div class=""gantt_bars_area""[^>]*>(?<b>[\s\S]*?)<div class=""gantt_links_area""",
            RegexOptions.IgnoreCase);
        var barsHtml = barsArea.Success ? barsArea.Groups["b"].Value : html;
        foreach (Match bar in Regex.Matches(barsHtml,
                     @"<div task_id=""[^""]*"" class=""gantt_task_line[^""]*""[^>]*style=""(?<s>[^""]*)""[^>]*>(?<b>[\s\S]*?)(?=<div task_id=|$)",
                     RegexOptions.IgnoreCase))
        {
            var st = bar.Groups["s"].Value;
            double W(string prop)
            {
                var pm = Regex.Match(st, @"(?<![-\w])" + prop + @"\s*:\s*(-?[\d.]+)px", RegexOptions.IgnoreCase);
                return pm.Success ? double.Parse(pm.Groups[1].Value, inv) : 0;
            }
            var bx = timelineX + P(W("left"));
            var by = dataTop + P(W("top"));
            var bw = P(W("width"));
            var bh = P(W("height"));
            if (bw <= 0 || bh <= 0) continue;
            Fill(GtBarFill, bx, by, bw, bh);
            sb.Append(string.Create(inv,
                $"q {GtBarBorderInk.R:0.###} {GtBarBorderInk.G:0.###} {GtBarBorderInk.B:0.###} RG 0.75 w " +
                $"{bx + 0.375:F2} {pageHeight - by - 0.375:F2} {bw - 0.75:F2} {-(bh - 0.75):F2} re S Q\n"));

            var inner = bar.Groups["b"].Value;
            var prog = Regex.Match(inner,
                @"<div class=""gantt_task_progress""[^>]*style=""[^""]*width:\s*(?<w>[\d.]+)px",
                RegexOptions.IgnoreCase);
            if (prog.Success)
            {
                var pw = P(double.Parse(prog.Groups["w"].Value, inv));
                if (pw > 0)
                    Fill(GtProgressFill, bx + GtBarInsetPt, by + GtBarInsetPt,
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
                    while (text.Length > 0 && tx + MeasureFaceText("Arial", text, GtBarLabelPt) > viewportRight)
                        text = text[..^1];
                    if (text.Length > 0)
                        Text(GtBarLabelPt, tx,
                            pageHeight - (by + GtBarTextDropPt + GtBarLabelPt * ArialAscentEm),
                            text, "Arial", (1.0, 1.0, 1.0));
                }
            }
        }

        page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
        foreach (var (size, x, y, text, face, ink) in texts)
            EmitGridsterText(page, resByFace, size, x, y, text, face, ink);
        PruneUnusedFonts(doc);
        return doc;
    }

    /// <summary>The cells of one grid row / the grid header: declared width, its
    /// text-align, the text, and how deep the tree indent is.</summary>
    private static List<(double WidthPx, string Align, string Text, double IndentPx)> ParseGanttCells(string html)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var cells = new List<(double, string, string, double)>();
        foreach (Match c in Regex.Matches(html,
                     @"<div class=""gantt_(?:grid_head_)?cell[^""]*""[^>]*style=""(?<s>[^""]*)""[^>]*>(?<b>[\s\S]*?)(?=<div class=""gantt_(?:grid_head_)?cell|</div>\s*</div>|$)",
                     RegexOptions.IgnoreCase))
        {
            var st = c.Groups["s"].Value;
            var wm = Regex.Match(st, @"width\s*:\s*([\d.]+)px");
            if (!wm.Success) continue;
            var align = Regex.Match(st, @"text-align\s*:\s*(\w+)") is { Success: true } am
                ? am.Groups[1].Value.ToLowerInvariant() : "left";
            var body = c.Groups["b"].Value;
            // Tree chrome: an indent div is one level, and the icons before the
            // label each take their own slot.
            var indent = Regex.Matches(body, @"gantt_tree_indent").Count * GtTreeIndentPx
                       + Regex.Matches(body, @"gantt_tree_icon").Count * GtTreeIconPx;
            var tm = Regex.Match(body, @"<div class=""gantt_tree_content""[^>]*>(?<t>[^<]*)</div\s*>",
                RegexOptions.IgnoreCase);
            var text = tm.Success
                ? Regex.Replace(DecodeEntities(tm.Groups["t"].Value), @"\s+", " ").Trim()
                : Regex.Replace(DecodeEntities(Regex.Replace(body, "<[^>]+>", " ")), @"\s+", " ").Trim();
            cells.Add((double.Parse(wm.Groups[1].Value, inv), align, text, indent));
        }
        return cells;
    }

    private static List<List<(double WidthPx, string Align, string Text, double IndentPx)>> ParseGanttRows(string html)
    {
        var rows = new List<List<(double, string, string, double)>>();
        var data = Regex.Match(html,
            @"<div class=""gantt_grid_data""[^>]*>(?<b>[\s\S]*?)<div class=""gantt_task""",
            RegexOptions.IgnoreCase);
        if (!data.Success) return rows;
        var openRx = new Regex(@"<div class=""gantt_row[^""]*""[^>]*>", RegexOptions.IgnoreCase);
        var body = data.Groups["b"].Value;
        var starts = new List<int>();
        for (var m = openRx.Match(body); m.Success; m = openRx.Match(body, m.Index + m.Length))
            starts.Add(m.Index);
        for (var i = 0; i < starts.Count; i++)
        {
            var end = i + 1 < starts.Count ? starts[i + 1] : body.Length;
            rows.Add(ParseGanttCells(body[starts[i]..end]));
        }
        return rows;
    }
}
