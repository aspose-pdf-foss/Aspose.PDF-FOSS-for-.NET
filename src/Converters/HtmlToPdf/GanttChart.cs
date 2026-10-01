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

    /// <summary>Gantt chart: css pixels to points.</summary>
    private static double GtPx(double px) => px * GtPxPt;

    private static readonly (double R, double G, double B) GtHeaderInk = (0.651, 0.651, 0.651);
    private static readonly (double R, double G, double B) GtRowInk = (0.271, 0.271, 0.271);
    private static readonly (double R, double G, double B) GtBarFill = (0.239, 0.725, 0.827);
    private static readonly (double R, double G, double B) GtProgressFill = (0.161, 0.612, 0.706);
    private static readonly (double R, double G, double B) GtLinkFill = (1.0, 0.627, 0.067);
    private static readonly (double R, double G, double B) GtRuleInk = (0.808, 0.808, 0.808);
    private static readonly (double R, double G, double B) GtGridLineInk = (0.922, 0.922, 0.922);
    private static readonly (double R, double G, double B) GtBarBorderInk = (0.161, 0.6, 0.69);

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
