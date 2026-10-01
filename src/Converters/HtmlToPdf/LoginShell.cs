using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// The SIGNCHART LOGIN SHELL: a saved application login page whose external stylesheet
// sets every td, a, div and input in 11px Tahoma #444f57. The body is one full-width
// table: the first-level nav row (the black logo cell and the right-aligned application
// logo on a 75px nested table), the 24px #666666 #ContentTitle band between white 1px
// rules, the #Content row whose 500px box (corner gifs round a #F7F7F7 body cell under
// 1px #EEEEEE rules) carries the welcome text, the 90% login grid (two 50% label + input
// cells and a nowrap link cell on a 16px line) and the notice, and the #Copyright footer
// band. The box's 12% bottom margin is a share of the row's width and the box centres in
// the row that margin tallens; the login grid's percent columns share what its nowrap
// column leaves in proportion to their shortfall; a text input is the metric line plus
// the UA chrome, framed by the 1pt rect the reference converter strokes inset 0.5.
// Constants not read from the stylesheet are measured on the licensed render.
internal static partial class HtmlToPdfConverter
{
    private const double LsFs = 8.25;                     // a, td, div, input { font-size: 11px }
    private const double LsBodyMargin = 6.0;              // the UA body margin (8px)
    private const string LsTextRgb = "0.267 0.31 0.341";  // … color: #444f57
    private const string LsNavFillRgb = "0.137 0.122 0.125";  // the logo td's bgcolor #231f20
    private const double LsLogoMarginBottom = 3.75;       // the logo img's margin-bottom: 5px
    private const double LsTitleH = 18.0;                 // #ContentTitle { height: 24px }
    private const string LsTitleRgb = "0.4 0.4 0.4";      // … background: #666666
    private const string LsTitleRuleRgb = "1 1 1";        // … border-top/bottom: 1px solid #FFFFFF
    private const double LsRule = 0.75;                   // every 1px rule
    private const string LsBoxFillRgb = "0.969 0.969 0.969";  // the box cells' bgcolor #F7F7F7
    private const string LsBoxRuleRgb = "0.933 0.933 0.933";  // … border: 1px solid #EEEEEE
    private const double LsBodyPadSide = 10.5;            // the body cell's padding-left/right: 14px
    private const double LsBodyPadV = 3.0;                // … padding-top/bottom: 4px
    private const double LsGridColPct = 0.5;              // the login grid's two width=50% cells
    private const double LsPassPadLeft = 9.0;             // the password cell's padding-left: 12px
    private const double LsLinkPadLeft = 9.0;             // the link cell's padding-left: 12px
    private const double LsLinkPadRight = 6.0;            // … padding-right: 8px
    private const double LsLinkLine = 12.0;               // … line-height: 16px
    private const double LsInputBorderPx = 2;             // the UA text input: 2px inset border
    private const double LsInputPadPx = 1;                // … 1px vertical padding
    private const double LsWidgetOverhang = 0.75;         // the widget rect's 1px overhang above and below the box
    private const double LsWidgetInset = 0.5;             // the 1pt black rect is stroked inset 0.5
    private const double LsWidgetStroke = 1.0;
    private const double LsUnderlineEm = 0.1;             // an underline's offset below the baseline and its thickness
    private const double LsDescGapLeftEm = 0.0376;        // the skip-ink gap around a descender, measured on the g
    private const double LsDescGapRightEm = 0.0194;
    private const string LsDescenders = "gjpqy";
    private const string LsFooterRgb = "0.349 0.349 0.349";      // #Copyright { color: #595959 }
    private const string LsFooterFillRgb = "0.91 0.922 0.914";   // … background: #e8ebe9
    private const double LsFooterPadT = 3.0;              // … padding: 4px 3px 3px 15px
    private const double LsFooterPadR = 2.25;
    private const double LsFooterPadB = 2.25;
    private const double LsFooterPadL = 11.25;
    private const double LsPxPt = 0.75;

    private static readonly Regex LsTagOrTextRx = new("<(/?)([a-zA-Z][a-zA-Z0-9]*)\\b[^>]*>|[^<]+", RegexOptions.Compiled);
    private static readonly Regex LsBrRx = new("<br\\s*/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Render the login shell, or null when the page does not carry the idiom's markup.</summary>
    private static Document? TryRenderLoginShell(ConvertState cv, HtmlLoadOptions? options)
    {
        var html = cv.html;
        if (!Regex.IsMatch(html, "<table\\b[^>]*id\\s*=\\s*['\"]ContentTitle['\"]", RegexOptions.IgnoreCase)
            || !Regex.IsMatch(html, "<tr\\b[^>]*id\\s*=\\s*['\"]Content['\"]", RegexOptions.IgnoreCase)
            || !Regex.IsMatch(html, "<td\\b[^>]*id\\s*=\\s*['\"]Copyright['\"]", RegexOptions.IgnoreCase)
            || !Regex.IsMatch(html, "<td\\b[^>]*colspan[^>]*class\\s*=\\s*['\"]Tahoma11DarkGray['\"]", RegexOptions.IgnoreCase))
            return null;
        if (WinMetricsFor(LpFace) is not { } tm) return null;
        var ls = new LoginShellState
        {
            html = html,
            options = options,
            invc = System.Globalization.CultureInfo.InvariantCulture,
            pageW = cv.pageWidth,
            pageH = cv.pageHeight,
            lp = new LetterPreviewState
            {
                tm = tm,
                invc = System.Globalization.CultureInfo.InvariantCulture,
                pageHeight = cv.pageHeight,
                sb = new System.Text.StringBuilder(),
            },
        };
        ls.contentL = LpUaMarginX + LsBodyMargin;
        ls.contentR = cv.pageWidth - LpUaMarginX - LsBodyMargin;
        ls.top = LpUaMarginY + LsBodyMargin;
        if (!LsParse(ls)) return null;
        return LsRender(ls);
    }

    private static bool LsParse(LoginShellState ls)
    {
        var s = ls.html;
        // the nav row: the logo in the dark cell, the application logo in the user-menu cell
        var logoTd = Regex.Match(s, "<td\\b[^>]*bgcolor\\s*=\\s*['\"]?#231f20['\"]?[^>]*>([\\s\\S]*?)</td>", RegexOptions.IgnoreCase);
        var menuTd = Regex.Match(s, "<td\\b[^>]*id\\s*=\\s*['\"]UserMenu1['\"][^>]*>([\\s\\S]*?)</table>", RegexOptions.IgnoreCase);
        if (!logoTd.Success || !menuTd.Success) return false;
        if (LsImage(ls, logoTd.Groups[1].Value) is not { } logo || LsImage(ls, menuTd.Groups[1].Value) is not { } app) return false;
        ls.logo = logo;
        ls.appLogo = app;
        var navH = Regex.Match(menuTd.Groups[1].Value, "<table\\b[^>]*height\\s*=\\s*['\"]?(\\d+)", RegexOptions.IgnoreCase);
        ls.navTableH = navH.Success ? double.Parse(navH.Groups[1].Value, ls.invc) * LsPxPt : 0;
        // the box: the 500px table whose bottom margin is a percent of the row
        var box = Regex.Match(s, "<table\\b[^>]*width\\s*=\\s*['\"]?(\\d+)['\"]?[^>]*margin-bottom\\s*:\\s*(\\d+(?:\\.\\d+)?)%", RegexOptions.IgnoreCase);
        if (!box.Success) return false;
        ls.boxW = double.Parse(box.Groups[1].Value, ls.invc) * LsPxPt;
        ls.boxMarginPct = double.Parse(box.Groups[2].Value, ls.invc) / 100.0;
        var boxEnd = LsElementClose(s, box.Index + box.Length, "table");
        var boxHtml = s[(box.Index + box.Length)..boxEnd];
        var rows = new List<string>();
        foreach (Match tr in Regex.Matches(boxHtml, "<tr\\b[^>]*>", RegexOptions.IgnoreCase))
            rows.Add(boxHtml[(tr.Index + tr.Length)..LsElementClose(boxHtml, tr.Index + tr.Length, "tr")]);
        if (rows.Count < 3) return false;
        ls.topCorners = LsImages(ls, rows[0]);
        ls.bottomCorners = LsImages(ls, rows[^1]);
        if (ls.topCorners.Count < 2 || ls.bottomCorners.Count < 2) return false;
        var body = Regex.Match(rows[1], "<td\\b[^>]*colspan[^>]*class\\s*=\\s*['\"]Tahoma11DarkGray['\"][^>]*>", RegexOptions.IgnoreCase);
        if (!body.Success) return false;
        var bodyStart = body.Index + body.Length;
        if (!LsParseBody(ls, rows[1][bodyStart..LsElementClose(rows[1], bodyStart, "td")])) return false;
        var foot = Regex.Match(s, "<td\\b[^>]*id\\s*=\\s*['\"]Copyright['\"][^>]*>([\\s\\S]*?)</td>", RegexOptions.IgnoreCase);
        ls.footer = foot.Success ? LpFlat(foot.Groups[1].Value) : "";
        return true;
    }

    /// <summary>The body cell: the welcome segments, the login grid and the notice segments.</summary>
    private static bool LsParseBody(LoginShellState ls, string body)
    {
        body = Regex.Replace(body, "<!--[\\s\\S]*?-->", "");
        var grid = Regex.Match(body, "<table\\b[^>]*width\\s*=\\s*['\"]?(\\d+)%", RegexOptions.IgnoreCase);
        if (!grid.Success) return false;
        ls.gridPct = double.Parse(grid.Groups[1].Value, ls.invc) / 100.0;
        var gridEnd = LsElementClose(body, grid.Index + grid.Length, "table");
        ls.welcome = LsSegments(body[..grid.Index]);
        ls.notice = LsSegments(body[Math.Min(body.Length, gridEnd + "</table>".Length)..]);
        var cells = new List<string>();
        foreach (Match td in Regex.Matches(body[(grid.Index + grid.Length)..gridEnd], "<td\\b[^>]*>([\\s\\S]*?)</td>", RegexOptions.IgnoreCase))
            cells.Add(td.Groups[1].Value);
        if (cells.Count < 3) return false;
        ls.userLabel = LsInlineRuns(LsBrRx.Split(cells[0])[0]);
        ls.passLabel = LsInlineRuns(LsBrRx.Split(cells[1])[0]);
        ls.links = LsSegments(cells[2]);
        return ls.userLabel.Count > 0 && ls.passLabel.Count > 0 && ls.links.Count > 0;
    }

    /// <summary>The br-separated segments of a fragment as inline runs; the last br ends its
    /// line without opening another; a hidden div contributes nothing.</summary>
    private static List<List<LpRun>> LsSegments(string frag)
    {
        frag = Regex.Replace(frag, "<!--[\\s\\S]*?-->", "");
        var hidden = Regex.Match(frag, "<div\\b[^>]*display\\s*:\\s*none[^>]*>", RegexOptions.IgnoreCase);
        while (hidden.Success)
        {
            var start = hidden.Index + hidden.Length;
            var end = Math.Min(frag.Length, DivClose(frag, start) + "</div>".Length);
            frag = frag[..hidden.Index] + frag[end..];
            hidden = Regex.Match(frag, "<div\\b[^>]*display\\s*:\\s*none[^>]*>", RegexOptions.IgnoreCase);
        }
        var segs = new List<List<LpRun>>();
        foreach (var part in LsBrRx.Split(frag)) segs.Add(LsInlineRuns(part));
        if (segs.Count > 1 && segs[^1].Count == 0) segs.RemoveAt(segs.Count - 1);
        return segs;
    }

    /// <summary>Inline runs of a fragment: strong/b bold, u underlined, whitespace collapsed
    /// across tags, the block's leading and trailing spaces dropped (an nbsp stays).</summary>
    private static List<LpRun> LsInlineRuns(string frag)
    {
        var runs = new List<LpRun>();
        int bold = 0, under = 0;
        foreach (Match m in LsTagOrTextRx.Matches(Regex.Replace(frag, "[ \\t\\r\\n]+", " ")))
        {
            if (m.Value[0] == '<')
            {
                var tag = m.Groups[2].Value.ToLowerInvariant();
                var close = m.Groups[1].Value.Length > 0;
                if (tag is "strong" or "b") bold += close ? -1 : 1;
                else if (tag == "u") under += close ? -1 : 1;
                continue;
            }
            var text = DecodeEntities(m.Value);
            if (text.Length == 0) continue;
            var run = LpMakeRun(text, bold > 0, LsFs);
            run.Underline = under > 0;
            if (runs.Count == 0) run.Text = run.Text.TrimStart(' ');
            if (run.Text.Length > 0) runs.Add(run);
        }
        // a space between runs belongs to the run it precedes (the line model trims a run's tail)
        for (var i = 0; i + 1 < runs.Count; i++)
            if (runs[i].Text.EndsWith(' '))
            {
                runs[i].Text = runs[i].Text.TrimEnd(' ');
                runs[i + 1].Text = " " + runs[i + 1].Text.TrimStart(' ');
            }
        if (runs.Count > 0)
        {
            runs[^1].Text = runs[^1].Text.TrimEnd(' ');
            if (runs[^1].Text.Length == 0) runs.RemoveAt(runs.Count - 1);
        }
        return runs;
    }

    private static List<LsPicture> LsImages(LoginShellState ls, string frag)
    {
        var list = new List<LsPicture>();
        foreach (Match img in Regex.Matches(frag, "<img\\b[^>]*>", RegexOptions.IgnoreCase))
            if (LsImage(ls, img.Value) is { } im) list.Add(im);
        return list;
    }

    /// <summary>The first img of a fragment, loaded from the page's base directory: its
    /// width and height attributes in px, else its natural size; a 1px spacer is skipped.</summary>
    private static LsPicture? LsImage(LoginShellState ls, string frag)
    {
        var img = Regex.Match(frag, "<img\\b[^>]*>", RegexOptions.IgnoreCase);
        if (!img.Success) return null;
        var src = Regex.Match(img.Value, "src\\s*=\\s*['\"]([^'\"]+)", RegexOptions.IgnoreCase);
        if (!src.Success || LoadConverterImage(src.Groups[1].Value, ls.options) is not { } bytes) return null;
        if (TryReadImagePixelSize(bytes) is not { } nat) return null;
        var w = Regex.Match(img.Value, "\\bwidth\\s*=\\s*['\"]?(\\d+)", RegexOptions.IgnoreCase);
        var h = Regex.Match(img.Value, "\\bheight\\s*=\\s*['\"]?(\\d+)", RegexOptions.IgnoreCase);
        var im = new LsPicture
        {
            Bytes = bytes,
            W = (w.Success ? double.Parse(w.Groups[1].Value, ls.invc) : nat.w) * LsPxPt,
            H = (h.Success ? double.Parse(h.Groups[1].Value, ls.invc) : nat.h) * LsPxPt,
        };
        return nat.w <= 1 && nat.h <= 1 ? null : im;
    }

    /// <summary>Index of the close tag matching the element whose open tag ends at <paramref name="start"/>.</summary>
    private static int LsElementClose(string s, int start, string tag)
    {
        var rx = new Regex("<(/?)" + tag + "\\b[^>]*>", RegexOptions.IgnoreCase);
        var depth = 1;
        for (var m = rx.Match(s, start); m.Success; m = m.NextMatch())
        {
            depth += m.Groups[1].Value.Length > 0 ? -1 : 1;
            if (depth == 0) return m.Index;
        }
        return s.Length;
    }
}
