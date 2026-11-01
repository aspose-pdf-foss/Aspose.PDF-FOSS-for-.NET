using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    private static Document? TryRenderGanttChart(string html,
        double pageWidth, double pageHeight, double marginLeft)
    {
        var gc = new GanttChartState();
        gc.html = html;
        gc.pageWidth = pageWidth;
        gc.pageHeight = pageHeight;
        gc.marginLeft = marginLeft;
        if (!gc.html.Contains("gantt_task_line", System.StringComparison.Ordinal)
            || !gc.html.Contains("gantt_grid_data", System.StringComparison.Ordinal)) return null;

        gc.inv = System.Globalization.CultureInfo.InvariantCulture;

        gc.doc = new Document();
        gc.page = gc.doc.Pages.Add(gc.pageWidth, gc.pageHeight);
        EnsureFonts(gc.page);
        gc.resByFace = new Dictionary<string, string>(System.StringComparer.Ordinal);
        gc.sb = new StringBuilder();
        gc.texts = new List<(double Size, double X, double Y, string Text, string Face, (double R, double G, double B) Ink)>();
        gc.title = SafeRegex.Match(gc.html, @"<label\b[^>]*>(?<t>[^<]*)</label\s*>", RegexOptions.IgnoreCase);
        if (gc.title.Success)
        {
            var tt = SafeRegex.Replace(DecodeEntities(gc.title.Groups["t"].Value), @"\s+", " ").Trim();
            if (tt.Length > 0)
                GcText(gc, 12.0, gc.marginLeft, gc.pageHeight - GtTitleTopPt - 12.0 * TimesAscentEm,
                    tt, "Times New Roman", (0, 0, 0));
        }

        gc.gridX = gc.marginLeft + 1.5;
        gc.timelineX = gc.gridX + GtPx(GtGridWidthPx);
        gc.dataTop = GtHeaderTopPt + GtPx(GtHeaderHeightPx);

        gc.gridScale = SafeRegex.Match(gc.html,
            @"<div class=""gantt_grid_scale""[^>]*>(?<b>[\s\S]*?)<div class=""gantt_grid_data""",
            RegexOptions.IgnoreCase);
        DrawGanttGrid(gc);

        DrawGanttFrame(gc);

        DrawGanttTimelineScale(gc);

        DrawGanttLinks(gc);

        gc.viewportRight = gc.timelineX + GtPx(1044.66674804688);
        gc.barsArea = SafeRegex.Match(gc.html,
            @"<div class=""gantt_bars_area""[^>]*>(?<b>[\s\S]*?)<div class=""gantt_links_area""",
            RegexOptions.IgnoreCase);
        gc.barsHtml = gc.barsArea.Success ? gc.barsArea.Groups["b"].Value : gc.html;
#pragma warning disable S3267 // a step loop: each call does the work and returns false to stop - as a LINQ predicate the side effects would hide in a query
        foreach (Match bar in SafeRegex.Matches(gc.barsHtml,
#pragma warning restore S3267
                     @"<div task_id=""[^""]*"" class=""gantt_task_line[^""]*""[^>]*style=""(?<s>[^""]*)""[^>]*>(?<b>[\s\S]*?)(?=<div task_id=|$)",
                     RegexOptions.IgnoreCase))
        {
            if (!DrawGanttBar(gc, bar)) break;
        }

        gc.page.AddContentStream(Encoding.ASCII.GetBytes(gc.sb.ToString()));
        foreach (var (size, x, y, text, face, ink) in gc.texts)
            EmitGridsterText(gc.page, gc.resByFace, size, x, y, text, face, ink);
        PruneUnusedFonts(gc.doc);
        return gc.doc;
    }
}
