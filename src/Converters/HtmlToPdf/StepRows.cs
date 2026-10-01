using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    internal static List<StepRow>? TryParseProcedureStepRows(string? html,
        bool paraHasMargin = false, HtmlLoadOptions? options = null)
    {
        var rows = new List<StepRow>();
        var s = html ?? "";
        ReadStepHeadingCss(s);
        // A form that links its stylesheet next to itself declares its p line box there
        // (16px/16px on these sheets). ONLY that is taken, and only the full-width
        // step-col-full generation consumes it: the narrow step-col family keeps its
        // fragment rhythm — headings, margins, box pads — even with the same sheet
        // on disk beside it.
        _stepLinkedParaLinePt = 0;
        if (options is not null && _stepParaLinePt <= 0)
        {
            var inlined = InlineLinkedStylesheets(s, options);
            var lpm = Regex.Match(inlined,
                @"(?<![-\w.#])p\s*\{[^}]*line-height\s*:\s*([\d.]+)\s*px[^}]*\}",
                RegexOptions.IgnoreCase);
            if (lpm.Success && double.TryParse(lpm.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var lpx) && lpx > 0)
                _stepLinkedParaLinePt = lpx * 0.75;
        }
        // The fragment's IsParagraphHasMargin honours the paragraph margin even when the
        // document's own p rule is out of reach behind a linked stylesheet: one line box
        // of the body's em, with the 1.12-em margin the sheet family declares. A fragment
        // that leaves the flag off keeps the flush rhythm this family is calibrated to.
        if (paraHasMargin && _stepParaMarginPt is null)
        {
            _stepParaMarginPt = 12.0 * 1.12;
            _stepParaLinePt = 12.0;
        }
        if (s.IndexOf("step-row", StringComparison.OrdinalIgnoreCase) < 0
            || s.IndexOf("sr-content", StringComparison.OrdinalIgnoreCase) < 0
            || s.IndexOf("smart-widget", StringComparison.OrdinalIgnoreCase) < 0) return null;
        // Every table in the document must be one this dialect knows how to place: a
        // smart-widget table of any of its kinds, or an ordinary author table the step
        // walker renders through the generic grid. A table of some other shape means the
        // document is not this form after all, and the generic flow should keep it.
        var tableTags = Regex.Matches(s, @"<table\b[^>]*>", RegexOptions.IgnoreCase);
        var placeable = 0;
        foreach (Match tm in tableTags)
            if (Regex.IsMatch(tm.Value, @"class\s*=\s*['""]sw(dt|mt|l)-table[\s'""]", RegexOptions.IgnoreCase)
                || !Regex.IsMatch(tm.Value, @"class\s*=", RegexOptions.IgnoreCase))
                placeable++;
        if (tableTags.Count != placeable) return null;

        foreach (Match rm in Regex.Matches(s,
            @"<div\b[^>]*class\s*=\s*(['""])[^'""]*step-row[^'""]*\1[^>]*>", RegexOptions.IgnoreCase))
        {
            if (!ParseStepRow(rm, s, rows)) break;
        }
        return rows.Count > 0 ? rows : null;
    }

    /// <summary>Linear walk of step-content HTML into flowed items. Data-entry table
    /// cells recurse through the same walk, keeping only their line items.</summary>
    private static List<StepItem> WalkStepContent(string html)
    {
        var ws = new StepWalkState();
        ws.items = new List<StepItem>();
        ws.line = new StepLine();
        ws.boldDepth = 0;
        ws.inDetable = false;
        ws.pSawText = false;
        ws.pHadContent = false;
        ws.pendingPad = 0;
        ws.gapNext = 0;
        ws.headingPt = 0;
        ws.headingLinePt = 0;
        ws.inChoice = false;
        ws.inPara = false;

        ws.i = 0;
        ws.n = html.Length;
        while (ws.i < ws.n)
        {
            if (!WalkStepToken(ws, html)) break;
        }
        FlushStepLine(ws);
        return ws.items;
    }

    /// <summary>The line a multiple-choice widget paces its label and options on -
    /// wider than the form's own pitch. Measured across six consecutive gaps of a
    /// label->option->option->option run.</summary>
    internal const double SwmOptionPitch = 15.85;

    /// <summary>The layout side reads the parsed document's paragraph margin back when it
    /// seats the acknowledge table under the content.</summary>
    internal static double? StepParaMargin => _stepParaMarginPt;

    /// <summary>Every fill-in blank the form draws is an inline-block 15 css px tall with a
    /// 3 px margin under it, so a paragraph carrying one sets on an 18 px line box.</summary>
    private const double SwElementLinePt = 18 * 0.75;

    /// <summary>Read <c>hN { font-size: Npx; line-height: Npx }</c> out of the document's
    /// style blocks. A form that sizes its headings this way also resets their margin
    /// (<c>h1,…,h6 { margin: 0 }</c>) and their weight, so headings set at their declared
    /// size, on their declared line, with nothing above or below.</summary>
    private static void ReadStepHeadingCss(string html)
    {
        _stepHeadingCss = null;
        _stepBoxPadPt = null;
        foreach (Match bm in Regex.Matches(html,
            @"\.step-(note|caution|warning|alara)\b[^{]*\{(?<body>[^}]*)\}", RegexOptions.IgnoreCase))
        {
            var pt = Regex.Match(bm.Groups["body"].Value,
                @"padding-top\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
            if (pt.Success)
                (_stepBoxPadPt ??= new())[bm.Groups[1].Value.ToLowerInvariant()] =
                    0.75 * double.Parse(pt.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        }
        Dictionary<string, (double, double, double)>? map = null;
        foreach (Match m in Regex.Matches(html,
            @"(?<tag>h[1-6])\s*\{(?<body>[^}]*)\}", RegexOptions.IgnoreCase))
        {
            var body = m.Groups["body"].Value;
            var fs = Regex.Match(body, @"font-size\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
            if (!fs.Success) continue;
            var lh = Regex.Match(body, @"line-height\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
            var size = double.Parse(fs.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * 0.75;
            var line = lh.Success
                ? double.Parse(lh.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * 0.75
                : size;
            (map ??= new())[m.Groups["tag"].Value.ToLowerInvariant()] = (size, line, 0.0);
        }
        if (map is not null) _stepHeadingCss = map;

        // A paragraph's margin is one em of the size the document gives it. A form that
        // declares nothing for `p` is left alone: those documents lay flush, and the
        // no-`p`-rule family is calibrated that way.
        _stepParaMarginPt = null;
        _stepParaLinePt = 0;
        var pm = Regex.Match(html,
            @"(?<![-\w])p\s*\{(?<body>[^}]*font-size\s*:\s*(?<px>[\d.]+)\s*px[^}]*)\}",
            RegexOptions.IgnoreCase);
        if (pm.Success && double.TryParse(pm.Groups["px"].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var pxv) && pxv > 0)
        {
            var paraPt = pxv * 0.75;
            _stepParaMarginPt = paraPt * 1.12;
            var plh = Regex.Match(pm.Groups["body"].Value,
                @"line-height\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
            _stepParaLinePt = plh.Success
                ? double.Parse(plh.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * 0.75
                : paraPt;
        }
    }

    /// <summary>Parse a data-entry <c>swdt-table</c>: fixed th widths, header texts,
    /// and td cells re-walked into line stacks.</summary>
    /// <summary>Where a table wrap seats its grid in the content column. The form says so
    /// either in the wrap's own class (<c>swmt-tablewrap right</c>) or inline
    /// (<c>style='text-align:right;'</c>); both spellings appear in one document.</summary>
    private static int StepWrapAlign(string cls, string style)
    {
        var m = Regex.Match(style, @"text-align\s*:\s*(left|center|right)", RegexOptions.IgnoreCase);
        if (!m.Success) m = Regex.Match(cls, @"(?:^|\s)(left|center|right)(?:\s|$)", RegexOptions.IgnoreCase);
        return m.Success
            ? m.Groups[1].Value.ToLowerInvariant() switch { "center" => 1, "right" => 2, _ => 0 }
            : 0;
    }

    private static StepTable? ParseStepTable(string wrapHtml, int align)
    {
        var tm = Regex.Match(wrapHtml, @"<table\b[^>]*>", RegexOptions.IgnoreCase);
        if (!tm.Success) return null;
        var t = new StepTable { Align = align };
        // An author's own table sets at the size the form gives its tables — a step
        // below the body — but a table whose cells carry the form's own widgets keeps
        // the widgets' size, because it is their label runs that set the text.
        if (!Regex.IsMatch(tm.Value, @"class\s*=\s*['""]sw", RegexOptions.IgnoreCase))
        {
            t.FormRhythm = true;
            if (wrapHtml.IndexOf("smart-widget", StringComparison.OrdinalIgnoreCase) < 0)
                t.CellFontPt = 10.5;
        }
        var csm = Regex.Match(tm.Value, @"cellspacing\s*=\s*[""']?([\d.]+)",
            RegexOptions.IgnoreCase);
        if (csm.Success)
            t.CellSpacingPt = double.Parse(csm.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture) * 0.75;
        // ⚠ the declared width, NOT the max-width that may sit in front of it in the
        // same style - an unbounded `width` matches inside `max-width` and the grid then
        // fills the whole column
        var wm = Regex.Match(tm.Value, @"(?<![-\w])width\s*:\s*([\d.]+)\s*px",
            RegexOptions.IgnoreCase);
        if (wm.Success)
        {
            t.WidthPt = double.Parse(wm.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * 0.75;
            t.WidthDeclared = true;
        }

        foreach (Match trm in Regex.Matches(wrapHtml, @"<tr\b[^>]*>([\s\S]*?)</tr\s*>", RegexOptions.IgnoreCase))
        {
            var tr = trm.Groups[1].Value;
            var ths = Regex.Matches(tr, @"<th\b([^>]*)>([\s\S]*?)</th\s*>", RegexOptions.IgnoreCase);
            if (ths.Count > 0 && t.Header.Count == 0)
            {
                foreach (Match th in ths)
                {
                    var wa = Regex.Match(th.Groups[1].Value, @"width\s*=\s*['""]?([\d.]+)px", RegexOptions.IgnoreCase);
                    t.ColPts.Add(wa.Success
                        ? double.Parse(wa.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * 0.75
                        : 48.75);
                    t.Header.Add(Regex.Replace(
                        DecodeEntities(HtmlFragment.StripHtmlTags(th.Groups[2].Value)), @"\s+", " ").Trim());
                }
                continue;
            }
            var tds = Regex.Matches(tr, @"<td\b([^>]*)>([\s\S]*?)</td\s*>", RegexOptions.IgnoreCase);
            if (tds.Count == 0) continue;
            // A table that heads its columns with plain cells rather than <th> declares
            // the grid on its first row: take the widths from there.
            if (t.ColPts.Count == 0 && t.Header.Count == 0)
                foreach (Match td in tds)
                {
                    var cw = Regex.Match(td.Groups[1].Value,
                        @"width\s*[:=]\s*['""]?\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
                    t.ColPts.Add(cw.Success
                        ? double.Parse(cw.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * 0.75
                        : 48.75);
                }
            var rowCells = new List<List<StepLine>>();
            var rowBg = new List<Color?>();
            foreach (Match td in tds)
            {
                var cellLines = new List<StepLine>();
                foreach (var it in WalkStepContent(td.Groups[2].Value))
                    if (it.Line is not null) cellLines.Add(it.Line);
                rowCells.Add(cellLines);
                var bgm = Regex.Match(td.Groups[1].Value,
                    @"background(?:-color)?\s*:\s*([^;'""]+)", RegexOptions.IgnoreCase);
                rowBg.Add(bgm.Success ? ParseCssColor(bgm.Groups[1].Value) : null);
            }
            t.Rows.Add(rowCells);
            t.RowBg.Add(rowBg);
            // the row is at least as tall as the tallest min-height its cells declare
            var minH = 0.0;
            foreach (Match mh in Regex.Matches(tr,
                @"min-height\s*:\s*([\d.]+)\s*(pt|px)", RegexOptions.IgnoreCase))
                if (double.TryParse(mh.Groups[1].Value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var mhv))
                    minH = Math.Max(minH, mh.Groups[2].Value.Equals("px", StringComparison.OrdinalIgnoreCase)
                        ? mhv * 0.75 : mhv);
            t.RowMinPt.Add(minH);
        }
        if (t.ColPts.Count == 0 || t.Rows.Count == 0) return null;
        if (t.WidthPt <= 0) foreach (var c in t.ColPts) t.WidthPt += c;
        return t;
    }

    /// <summary>Index just past the matching close of the element opening at
    /// <paramref name="openIdx"/> (same-tag nesting honored).</summary>
    private static int SkipElement(string html, int openIdx, string tag)
    {
        var d = 0;
        foreach (Match m in Regex.Matches(html[openIdx..], @"<(/?)" + tag + @"\b[^>]*>", RegexOptions.IgnoreCase))
        {
            if (m.Value.EndsWith("/>", StringComparison.Ordinal))
            {
                if (d == 0) return openIdx + m.Index + m.Length;
                continue;
            }
            d += m.Groups[1].Value.Length > 0 ? -1 : 1;
            if (d == 0) return openIdx + m.Index + m.Length;
        }
        return html.Length;
    }

    /// <summary>The inner HTML of the first <c>&lt;div&gt;</c> whose class contains
    /// <paramref name="classToken"/>, honoring nested div nesting.</summary>
    private static string? ExtractBalancedDivInner(string html, string classToken)
    {
        var open = Regex.Match(html,
            @"<div\b[^>]*class\s*=\s*(['""])[^'""]*" + Regex.Escape(classToken) + @"[^'""]*\1[^>]*>",
            RegexOptions.IgnoreCase);
        return open.Success ? ExtractBalancedInnerAt(html, open.Index) : null;
    }

    /// <summary>Balanced inner HTML of the div opening at <paramref name="openIdx"/>;
    /// <see cref="ExtractBalancedInnerSpanAt"/> also reports the index just past
    /// the close tag (the end of the input when there is no balanced div).</summary>
    private static string? ExtractBalancedInnerAt(string html, int openIdx)
        => ExtractBalancedInnerSpanAt(html, openIdx).inner;

    private static (string? inner, int pastEnd) ExtractBalancedInnerSpanAt(string html, int openIdx)
    {
        var open = Regex.Match(html[openIdx..], @"^<div\b[^>]*>", RegexOptions.IgnoreCase);
        if (!open.Success) return (null, html.Length);
        var i = openIdx + open.Length;
        var d = 1;
        foreach (Match t in Regex.Matches(html[i..], @"<(/?)div\b[^>]*>", RegexOptions.IgnoreCase))
        {
            if (t.Value.EndsWith("/>", StringComparison.Ordinal)) continue;
            d += t.Groups[1].Value.Length > 0 ? -1 : 1;
            if (d == 0) return (html.Substring(i, t.Index), i + t.Index + t.Length);
        }
        return (null, html.Length);
    }

    /// <summary>Resolve knockout <c>data-bind="text: name"</c> spans against observable
    /// literals declared in the document's own scripts (<c>name = ko.observable('…')</c>,
    /// applied via <c>ko.applyBindings</c>): the bound span renders its observable's text.
    /// The enclosing heading splits at the span so the bound text keeps its own DOM-node
    /// run (and takes the browser heading size), matching how a scripted engine draws
    /// it. HTML without both binding halves passes through untouched.</summary>
    internal static string ApplyKnockoutTextBindings(string html)
    {
        if (string.IsNullOrEmpty(html)
            || html.IndexOf("data-bind", StringComparison.OrdinalIgnoreCase) < 0
            || html.IndexOf("ko.observable", StringComparison.Ordinal) < 0
            || !Regex.IsMatch(html, @"ko\.applyBindings\s*\(")) return html ?? "";

        var lits = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match sm in Regex.Matches(html, @"<script\b[^>]*>([\s\S]*?)</script\s*>",
                     RegexOptions.IgnoreCase))
            foreach (Match om in Regex.Matches(sm.Groups[1].Value,
                         @"(?:this\s*\.\s*)?(\w+)\s*=\s*ko\.observable\(\s*(['""])(.*?)\2\s*\)"))
                lits[om.Groups[1].Value] = om.Groups[3].Value;
        if (lits.Count == 0) return html;

        return Regex.Replace(html,
            @"<(h[1-6])([^>]*)>((?:(?!</\1>|<span)[\s\S])*?)<span[^>]*data-bind\s*=\s*(['""])\s*text\s*:\s*(\w+)\s*\4[^>]*>[\s\S]*?</span>\s*</\1>",
            m =>
            {
                if (!lits.TryGetValue(m.Groups[5].Value, out var lit)) return m.Value;
                // The browser heading size (h1 = 2 em of the 12 pt UA base, h2 = 1.5 em, …)
                // applies to both halves, so the bound run wraps where the scripted
                // engine's does.
                var uaPt = m.Groups[1].Value.ToLowerInvariant() switch
                {
                    "h1" => 24, "h2" => 18, "h3" => 14, "h4" => 12, "h5" => 10, _ => 9,
                };
                var open = $"<{m.Groups[1].Value} style=\"font-size:{uaPt}pt\"{m.Groups[2].Value}>";
                return $"{open}{m.Groups[3].Value}</{m.Groups[1].Value}>{open}{lit}</{m.Groups[1].Value}>";
            }, RegexOptions.IgnoreCase);
    }
}
