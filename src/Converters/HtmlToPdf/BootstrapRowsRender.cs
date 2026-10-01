using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    private static Document? TryRenderBootstrapRows(string html,
        IReadOnlyDictionary<string, Dictionary<string, string>> css,
        double pageWidth, double pageHeight)
    {
        var bs = new BootstrapRowsState();
        bs.html = html;
        bs.css = css;
        bs.pageWidth = pageWidth;
        bs.pageHeight = pageHeight;
        // ── fingerprint: the Bootstrap body + panel/row/col markup, no container ──
        if (!bs.css.TryGetValue("body", out var body)
            || !body.TryGetValue("font-size", out var bodyFsV)
            || !bodyFsV.TrimEnd().EndsWith("px", StringComparison.OrdinalIgnoreCase)
            || !bs.css.ContainsKey(".panel")
            || !Regex.IsMatch(bs.html, @"class\s*=\s*['""]panel panel-", RegexOptions.IgnoreCase)
            || !Regex.IsMatch(bs.html, @"class\s*=\s*['""]col-xs-\d", RegexOptions.IgnoreCase)
            || Regex.IsMatch(bs.html, @"class\s*=\s*['""]container['""]", RegexOptions.IgnoreCase))
            return null;
        bs.face = "Arial";
        if (body.TryGetValue("font-family", out var famV))
            foreach (var fam in famV.Split(','))
            {
                var f = fam.Trim().Trim('"', '\'');
                if (f.Length > 0 && !f.Equals("sans-serif", StringComparison.OrdinalIgnoreCase)
                    && WinMetricsFor(f) is not null) { bs.face = f; break; }
            }
        if (WinMetricsFor(bs.face) is not { } fmv) return null;

        bs.doc = new Document();
        bs.page = bs.doc.Pages.Add(bs.pageWidth, bs.pageHeight);
        EnsureFont(bs.page, bs.face.Replace(" ", ""), "FA");
        EnsureFont(bs.page, bs.face.Replace(" ", "") + "-Bold", "FB");
        bs.invc = System.Globalization.CultureInfo.InvariantCulture;

        bs.contentL = BrMarginX + BrBodyPadX;
        bs.contentR = bs.pageWidth - BrMarginX - BrBodyPadX;
        bs.limit = bs.pageHeight - BrMarginY;

        // white body canvas over the content box (the Bootstrap body background)
        bs.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(bs.invc,
            $"q 1 1 1 rg {BrMarginX:F2} {BrMarginY:F2} {bs.pageWidth - 2 * BrMarginX:F2} {bs.pageHeight - 2 * BrMarginY:F2} re f Q\n")));

        bs.drop = MetricBaselineDrop(BrFontPt, BrLineH, fmv);
        bs.dropH3 = MetricBaselineDrop(BrH3FontPt, BrH3LineH, fmv);

        bs.bodyM = Regex.Match(bs.html, @"<body\b[^>]*>", RegexOptions.IgnoreCase);
        bs.pos = bs.bodyM.Success ? bs.bodyM.Index + bs.bodyM.Length : 0;
        bs.yTd = BrMarginY;                       // flow position (top-down)
        bs.pendingMb = 0.0;                       // margin awaiting MAX-collapse
        bs.construct = new Regex(
            @"<h3\b[^>]*>(?<h3>[\s\S]*?)</h3>|<hr\s*/?>|<p\b[^>]*class\s*=\s*['""]text-center['""][^>]*>(?<pc>[\s\S]*?)</p>|<div\b[^>]*class\s*=\s*['""]\s*(?<cls>row|panel panel-[\w-]+)\s*['""][^>]*>",
            RegexOptions.IgnoreCase);
        while (true)
        {
            if (!EmitBootstrapConstruct(bs)) break;
        }
        return bs.doc;
    }
}
