using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// The FORM-AREA SHEET: a jQuery-print export whose px-wide wrapper holds nothing in flow
// but absolutely positioned div.formArea panels at declared px tops, each carrying either
// positioned div.newControl labels (left/top in px) or one px-wide tblDynamicContent table
// of px-width th columns, in the UA's 12pt Times New Roman. Nothing flows: a panel stands
// at its declared top in a document space the reference converter cuts into slices as tall
// as the sheet's content box, a label wrapping at its min-content width (word by word), a
// table's rows following one another and a row that cannot end above the slice's bottom
// opening the next page under the repeated header row. A declared th width is the cell's
// content box; the spacing the columns owe over the table's width is taken back from each
// in proportion to its slack over its min-content. Constants not read from the markup are
// measured on the licensed render.
internal static partial class HtmlToPdfConverter
{
    private const double FaFs = 12.0;                       // the UA body size
    private const double FaPxPt = 0.75;
    private const string FaFace = "Times New Roman";        // the measuring faces
    private const string FaBoldFace = "Times New Roman-Bold";
    private const string FaFontName = "TimesNewRoman";      // the page resources
    private const string FaBoldFontName = "TimesNewRoman-Bold";
    private const string FaRes = "F10";
    private const string FaBoldRes = "F11";
    private const double FaCellPad = 0.75;                  // the UA 1px td/th padding
    private const double FaSpacing = 1.5;                   // the UA 2px border-spacing
    private const double FaBoundary = 1e-6;

    private static readonly Regex FaAreaOpenRx = new("<div\\b[^>]*class\\s*=\\s*['\"]formArea['\"][^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex FaControlOpenRx = new("<div\\b[^>]*class\\s*=\\s*['\"]newControl['\"][^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex FaPxRx = new("(?<![a-z-])(top|left|width)\\s*:\\s*(-?\\d+(?:\\.\\d+)?)\\s*px", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex FaRowRx = new("<tr\\b[^>]*>([\\s\\S]*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex FaCellRx = new("<(t[hd])\\b([^>]*)>([\\s\\S]*?)</t[hd]>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private sealed class FaArea
    {
        public double Top;                                  // the panel's declared top, pt
        public List<FaControl> Controls = new();
    }

    private sealed class FaControl
    {
        public double Left, Top;                            // the control's declared offsets, pt
        public string Text = "";                            // a label's text
        public FaTable? Table;                              // …or the panel's table
    }

    private sealed class FaTable
    {
        public double W;                                    // the declared width, pt
        public List<string> Head = new();                   // the th texts
        public List<double> HeadW = new();                  // …and their declared content widths, pt
        public List<List<string>> Rows = new();
        public double[] ColW = Array.Empty<double>();
    }

    /// <summary>Render the form-area sheet, or null when the page does not carry the idiom's markup.</summary>
    private static Document? TryRenderFormAreas(ConvertState cv)
    {
        var html = cv.html;
        if (!FaAreaOpenRx.IsMatch(html) || !FaControlOpenRx.IsMatch(html)
            || !Regex.IsMatch(html, "<table\\b[^>]*class\\s*=\\s*['\"]tblDynamicContent['\"]", RegexOptions.IgnoreCase))
            return null;
        if (WinMetricsFor(FaFace) is not { } tm) return null;
        var areas = FaParse(html);
        if (areas is null || areas.Count == 0) return null;
        var fa = new FormAreasState
        {
            invc = System.Globalization.CultureInfo.InvariantCulture,
            tm = tm,
            pageW = cv.pageWidth,
            pageH = cv.pageHeight,
            marginT = LpUaMarginY,
            contentL = LpUaMarginX + UaBodyMarginPt,
            areas = areas,
        };
        fa.sliceH = cv.pageHeight - 2 * LpUaMarginY;
        return FaRender(fa);
    }

    /// <summary>Every formArea panel in order with its controls; null when a panel or a
    /// control lacks the idiom's declared geometry.</summary>
    private static List<FaArea>? FaParse(string html)
    {
        var areas = new List<FaArea>();
        var invc = System.Globalization.CultureInfo.InvariantCulture;
        for (var m = FaAreaOpenRx.Match(html); m.Success; m = m.NextMatch())
        {
            var px = FaPxValues(m.Value, invc);
            if (!px.TryGetValue("top", out var top)) return null;
            var start = m.Index + m.Length;
            var body = html[start..DivClose(html, start)];
            var area = new FaArea { Top = top * FaPxPt };
            for (var c = FaControlOpenRx.Match(body); c.Success; c = c.NextMatch())
            {
                var cpx = FaPxValues(c.Value, invc);
                var cs = c.Index + c.Length;
                var inner = body[cs..DivClose(body, cs)];
                var ctl = new FaControl
                {
                    Left = cpx.TryGetValue("left", out var l) ? l * FaPxPt : 0,
                    Top = cpx.TryGetValue("top", out var t) ? t * FaPxPt : 0,
                };
                var tbl = Regex.Match(inner, "<table\\b[^>]*>", RegexOptions.IgnoreCase);
                if (tbl.Success)
                {
                    if (FaParseTable(inner, tbl, invc) is not { } table) return null;
                    ctl.Table = table;
                }
                else ctl.Text = LpFlat(inner);
                area.Controls.Add(ctl);
            }
            areas.Add(area);
        }
        return areas;
    }

    /// <summary>The px lengths (top, left, width) declared in an open tag's style.</summary>
    private static Dictionary<string, double> FaPxValues(string tag, System.Globalization.CultureInfo invc)
    {
        var d = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in FaPxRx.Matches(tag))
            d[m.Groups[1].Value.ToLowerInvariant()] = double.Parse(m.Groups[2].Value, invc);
        return d;
    }

    /// <summary>The table: its declared width, the th row (each with its declared content
    /// width) and the td rows; null without a width or a th row.</summary>
    private static FaTable? FaParseTable(string frag, Match open, System.Globalization.CultureInfo invc)
    {
        var px = FaPxValues(open.Value, invc);
        if (!px.TryGetValue("width", out var w)) return null;
        var t = new FaTable { W = w * FaPxPt };
        var end = LpTableClose(frag, open.Index);
        foreach (Match row in FaRowRx.Matches(frag[open.Index..end]))
        {
            var cells = new List<string>();
            var head = false;
            foreach (Match cell in FaCellRx.Matches(row.Groups[1].Value))
            {
                cells.Add(LpFlat(cell.Groups[3].Value));
                if (!cell.Groups[1].Value.Equals("th", StringComparison.OrdinalIgnoreCase)) continue;
                head = true;
                var cpx = FaPxValues(cell.Groups[2].Value, invc);
                t.HeadW.Add(cpx.TryGetValue("width", out var cw) ? cw * FaPxPt : 0);
            }
            if (cells.Count == 0) continue;
            if (head && t.Head.Count == 0) t.Head = cells;
            else if (!head) t.Rows.Add(cells);
        }
        return t.Head.Count == 0 ? null : t;
    }
}
