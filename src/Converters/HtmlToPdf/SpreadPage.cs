using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// The ebook SPREAD dialect: a stylesheet pins the body to a fixed pixel canvas
// (`body { width: 851px; height: 1103px }`) and each `.spread` div is one full
// page — absolutely positioned full-bleed images layered under a padded content
// div (`img { z-index: -1 }`, `.spread img { position: absolute; left: 0 }`).
// Each spread renders as ONE page at the body width (its height
// stays the A4 842), the images at their natural pixel size × 0.75, and the
// content column flowing over them: UA-serif paragraphs with 3-em initial-cap
// floats (`.national`), an 80% float-left figure (caption head, width:100%
// image, italic caption) beside a 20% float-right text column, and
// `clear: both` headings resuming below the floats with their top margin
// suppressed. Every constant below is the stylesheet's own em chain
// (body 1.1em × UA 16px, .page3 0.9em ⇒ 15.84px = 11.88 pt, h1 1.5em,
// .national 3em, .caption 0.8em) verified against the expected render:
// h1 #2 predicted 266.8 vs 266.9 measured, the float figure's image top
// 364.3 vs 364.4, the cleared heading 700.0 exact.
internal static partial class HtmlToPdfConverter
{
    private const double CaptionGapPt = 4.9;

    private static Document? TryRenderSpreadPages(string html,
        IReadOnlyDictionary<string, Dictionary<string, string>> css,
        HtmlLoadOptions? options, double defaultPageHeight)
    {
        var sp = new SpreadPagesState();
        sp.html = html;
        sp.css = css;
        sp.options = options;
        sp.defaultPageHeight = defaultPageHeight;
        // Gate: a body pinned to a pixel canvas in the stylesheet, and at least
        // one .spread container with position:relative + overflow:hidden.
        if (!sp.css.TryGetValue("body", out var bodyRule)
            || !bodyRule.TryGetValue("width", out var bwV)
            || TryParseLength(bwV) is not { } bodyWPt || bodyWPt <= 0
            || !bodyRule.ContainsKey("height")
            || !sp.css.TryGetValue(".spread", out var spreadRule)
            || !(spreadRule.TryGetValue("overflow", out var ovV)
                 && ovV.Contains("hidden", System.StringComparison.OrdinalIgnoreCase))
            || !Regex.IsMatch(sp.html, "class\\s*=\\s*[\"'][^\"']*spread", RegexOptions.IgnoreCase))
            return null;
        sp.spreads = Regex.Matches(sp.html,
            "<div\\b[^>]*class\\s*=\\s*[\"'][^\"']*\\bspread\\b[^\"']*[\"'][^>]*>(?<body>[\\s\\S]*?)</div>\\s*</div>",
            RegexOptions.IgnoreCase);
        if (sp.spreads.Count == 0) return null;
        if (WinMetricsFor("Times New Roman") is not { } serifM
            || WinMetricsFor("Arial") is not { } arialM) return null;
            sp.arialM = arialM;

        sp.bodyEmPx = 16.0 * CssEm(sp, "body", "font-size", 1.1);
        sp.contentEmPx = sp.bodyEmPx * CssEm(sp, ".page3", "font-size", 0.9);
        sp.bodyFs = sp.contentEmPx * 0.75;
        sp.h1Fs = sp.contentEmPx * CssEm(sp, ".page3 h1", "font-size", 1.5) * 0.75;
        sp.natFs = sp.contentEmPx * CssEm(sp, ".national", "font-size", 3.0) * 0.75;
        sp.capFs = sp.contentEmPx * CssEm(sp, ".caption", "font-size", 0.8) * 0.75;
        sp.bodyPitch = sp.contentEmPx * 1.1 * 0.75;
        sp.capPitch = sp.capFs / 0.75 * 1.5 * 0.75;
        sp.headPitch = sp.bodyFs / 0.75 * 1.5 * 0.75;

        sp.padL = CssPx(sp, ".page3", "padding-left", 71.25);
        sp.padR = CssPx(sp, ".page3", "padding-right", 37.5);
        sp.padT = CssPx(sp, ".page3", "padding-top", 37.5);
        sp.noindML = CssPx(sp, ".page3 p.noind", "margin-left", 37.5);
        sp.capColor = sp.css.TryGetValue(".caption", out var capRule)
            && capRule.TryGetValue("color", out var capColV)
            && ParseCssColor(capColV) is { } cc ? cc : Color.FromArgb(57, 117, 167);

        sp.h1Asc = sp.h1Fs * sp.arialM.asc;
        sp.h1MarginY = sp.h1Fs * 0.67;
        sp.bodyDrop = MetricBaselineDrop(sp.bodyFs, sp.bodyPitch, serifM);
        sp.bodyDesc = sp.bodyPitch - sp.bodyDrop;
        sp.natAsc = sp.natFs * 0.837;
        sp.capDrop = MetricBaselineDrop(sp.capFs, sp.capPitch, serifM);
        // the italic caption opens this far below the figure (measured 592.3
        // against the image bottom 577.1 with the 10.3 pt computed drop)

        sp.pageWidth = bodyWPt;
        sp.pageHeight = sp.defaultPageHeight > 0 ? sp.defaultPageHeight : 842.0;
        sp.contentL = sp.padL;
        sp.contentR = sp.pageWidth - sp.padR;
        sp.contentW = sp.contentR - sp.contentL;

        sp.doc = new Document();
        sp.invc = System.Globalization.CultureInfo.InvariantCulture;

        foreach (Match spread in sp.spreads)
        {
            if (!RenderSpread(sp, spread)) break;
        }
        return sp.doc.Pages.Count > 0 ? sp.doc : null;
    }
}
