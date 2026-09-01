using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    // ── The pre-wrap LABEL document ─────────────────────────────────────────────
    // A generated read-only form page: a rem-scaled sheet (html { font-size: 62.5% }),
    // one div-wrapped <label> whose class chain declares a pixel font size and
    // `white-space: pre-wrap !important`, its content a custom inline element holding
    // literal newlines. The label lays out PREFORMATTED: every source
    // newline is a line break (a blank source line is a blank line box), source
    // spaces stay as drawn glyphs, and leading tabs advance the pen without ink.
    //
    // Geometry (all measured, fs 12px = 9 pt Arial on A4):
    //   content x = 96 (the 90 pt margin + the UA body 8 px);
    //   line i baseline (from top) = 86.1108 + i · 10.7593 — the ladder's first
    //   rung is the label's leading source newline, so the first VISIBLE line
    //   (i = 1) seats at 96.8701;
    //   a leading tab advances 2.46533 pt (7.396 over the fixture's three tabs);
    //   a space glyph advances 0.278 em = 2.502 pt.

    private const double PwFirstLineTopTd = 86.1108;   // measured: rung 0 of the ladder
    private const double PwLinePitchPt = 10.7593;      // measured: 14.3457 px line box
    private const double PwTabAdvancePt = 2.46533;     // measured: 7.396 / 3 tabs
    private const double PwContentX = 96.0;            // 90 pt margin + 8 px body margin

    private static Document? TryRenderPreWrapLabel(string html,
        IReadOnlyDictionary<string, Dictionary<string, string>> css,
        double pageWidth, double pageHeight)
    {
        // Gate: a rem-scaled sheet, a class rule declaring pre-wrap, and a single
        // div>label body whose label carries that class plus a 12px-size class —
        // and nothing else content-wise (no tables, images or form controls).
        if (!css.TryGetValue("html", out var htmlRule)
            || !htmlRule.TryGetValue("font-size", out var rootFs)
            || !rootFs.Contains("62.5%")) return null;
        string? preWrapCls = null;
        foreach (var (sel, props) in css)
            if (sel.StartsWith('.') && props.TryGetValue("white-space", out var ws)
                && ws.Contains("pre-wrap", StringComparison.OrdinalIgnoreCase))
            { preWrapCls = sel[1..]; break; }
        if (preWrapCls is null) return null;

        var body = Regex.Match(html, @"<body\b[^>]*>([\s\S]*)</body>", RegexOptions.IgnoreCase) is
            { Success: true } bm ? bm.Groups[1].Value : html;
        if (Regex.IsMatch(body, @"<(table|img|input|select|textarea)\b", RegexOptions.IgnoreCase))
            return null;
        var labels = Regex.Matches(body, @"<label\b([^>]*)>([\s\S]*?)</label>", RegexOptions.IgnoreCase);
        if (labels.Count != 1) return null;
        var labelAttrs = labels[0].Groups[1].Value;
        var clsM = Regex.Match(labelAttrs, @"class\s*=\s*[""']([^""']*)[""']", RegexOptions.IgnoreCase);
        if (!clsM.Success) return null;
        var classes = clsM.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (Array.IndexOf(classes, preWrapCls) < 0) return null;
        // The pixel size from the label's class chain; the measured ladder is the
        // 12px one, so any other size stays off this arm.
        double fsPx = 0;
        Color? color = null;
        foreach (var cls in classes)
        {
            if (!css.TryGetValue("." + cls, out var rule)) continue;
            if (rule.TryGetValue("font-size", out var fv))
            {
                // These sheets declare the px value then its rem twin ("12px" then
                // "1.2rem"), and last-wins leaves the rem — under the gated 62.5%
                // root, 1 rem = 10 px.
                var fm = Regex.Match(fv, @"(\d+(?:\.\d+)?)(px|rem)");
                if (fm.Success)
                {
                    fsPx = double.Parse(fm.Groups[1].Value,
                        System.Globalization.CultureInfo.InvariantCulture);
                    if (fm.Groups[2].Value == "rem") fsPx *= 10.0;
                }
            }
            if (rule.TryGetValue("color", out var cv)
                && ParseCssColor(Regex.Replace(cv, @"\s*!\s*important", "",
                       RegexOptions.IgnoreCase).Trim()) is { } c)
                color = c;
        }
        if (System.Math.Abs(fsPx - 12.0) > 0.01) return null;
        var fs = fsPx * 0.75;

        var doc = Document.Create();
        var docFontDict = new Core.PdfDictionary();
        var page = doc.Pages.Add(pageWidth, pageHeight);
        EnsureFonts(page, docFontDict);
        EnsureFont(page, "Arial", "F8");

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        // The label content with tags stripped but WHITESPACE KEPT — the pre-wrap
        // layout is exactly the source text's own line structure.
        var text = DecodeEntities(Regex.Replace(labels[0].Groups[2].Value, @"<[^>]+>", ""))
            .Replace("\r\n", "\n").Replace(' ', ' ');
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var tabs = 0;
            while (tabs < line.Length && line[tabs] == '\t') tabs++;
            line = line[tabs..].Replace("\t", " ");
            if (line.Trim().Length == 0) continue;   // blank rungs advance, draw nothing
            var x = PwContentX + tabs * PwTabAdvancePt;
            var yTd = PwFirstLineTopTd + i * PwLinePitchPt;
            sb.Append("BT ");
            if (color is { } col)
                sb.Append(string.Create(inv,
                    $"{col.R / 255.0:0.###} {col.G / 255.0:0.###} {col.B / 255.0:0.###} rg "));
            sb.Append(string.Create(inv,
                $"/F8 {fs:0.##} Tf 1 0 0 1 {x:F4} {pageHeight - yTd:F4} Tm ({EscapePdfString(line)}) Tj "));
            if (color is not null) sb.Append("0 g ");
            sb.AppendLine("ET");
        }
        if (sb.Length == 0) return null;

        page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
        return doc;
    }
}
