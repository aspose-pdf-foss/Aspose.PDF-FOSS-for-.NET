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
    private static Document? TryRenderSpreadPages(string html,
        IReadOnlyDictionary<string, Dictionary<string, string>> css,
        HtmlLoadOptions? options, double defaultPageHeight)
    {
        // Gate: a body pinned to a pixel canvas in the stylesheet, and at least
        // one .spread container with position:relative + overflow:hidden.
        if (!css.TryGetValue("body", out var bodyRule)
            || !bodyRule.TryGetValue("width", out var bwV)
            || !TryParseLength(bwV, out var bodyWPt) || bodyWPt <= 0
            || !bodyRule.ContainsKey("height")
            || !css.TryGetValue(".spread", out var spreadRule)
            || !(spreadRule.TryGetValue("overflow", out var ovV)
                 && ovV.Contains("hidden", System.StringComparison.OrdinalIgnoreCase))
            || !Regex.IsMatch(html, "class\\s*=\\s*[\"'][^\"']*spread", RegexOptions.IgnoreCase))
            return null;
        var spreads = Regex.Matches(html,
            "<div\\b[^>]*class\\s*=\\s*[\"'][^\"']*\\bspread\\b[^\"']*[\"'][^>]*>(?<body>[\\s\\S]*?)</div>\\s*</div>",
            RegexOptions.IgnoreCase);
        if (spreads.Count == 0) return null;
        if (WinMetricsFor("Times New Roman") is not { } serifM
            || WinMetricsFor("Arial") is not { } arialM) return null;

        // ── the stylesheet's em chain ──
        double CssEm(string sel, string prop, double fallbackEm)
        {
            if (css.TryGetValue(sel, out var r) && r.TryGetValue(prop, out var v))
            {
                var m = Regex.Match(v, "([\\d.]+)\\s*em");
                if (m.Success && double.TryParse(m.Groups[1].Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var em))
                    return em;
            }
            return fallbackEm;
        }
        double CssPx(string sel, string prop, double fallbackPt)
            => css.TryGetValue(sel, out var r) && r.TryGetValue(prop, out var v)
               && TryParseLength(v, out var pt) && pt > 0 ? pt : fallbackPt;

        var bodyEmPx = 16.0 * CssEm("body", "font-size", 1.1);      // 17.6 px
        var contentEmPx = bodyEmPx * CssEm(".page3", "font-size", 0.9); // 15.84 px
        var bodyFs = contentEmPx * 0.75;                            // 11.88 pt
        var h1Fs = contentEmPx * CssEm(".page3 h1", "font-size", 1.5) * 0.75;   // 17.82
        var natFs = contentEmPx * CssEm(".national", "font-size", 3.0) * 0.75;  // 35.64
        var capFs = contentEmPx * CssEm(".caption", "font-size", 0.8) * 0.75;   // 9.5
        var bodyPitch = contentEmPx * 1.1 * 0.75;                   // line-height 1.1em = 13.07
        var capPitch = capFs / 0.75 * 1.5 * 0.75;                   // p line-height 1.5em = 14.25
        var headPitch = bodyFs / 0.75 * 1.5 * 0.75;                 // caption head on the p 1.5em line

        // .page3 box: padding around the content column, the noind paragraphs'
        // own left margin, the figure/caption colour.
        var padL = CssPx(".page3", "padding-left", 71.25);
        var padR = CssPx(".page3", "padding-right", 37.5);
        var padT = CssPx(".page3", "padding-top", 37.5);
        var noindML = CssPx(".page3 p.noind", "margin-left", 37.5);
        var capColor = css.TryGetValue(".caption", out var capRule)
            && capRule.TryGetValue("color", out var capColV)
            && ParseCssColor(capColV) is { } cc ? cc : Color.FromArgb(57, 117, 167);

        // Metrics: ascent-only baseline seat for headings/floats (line-height
        // normal), the half-leading drop for the 1.1em body lines.
        var h1Asc = h1Fs * arialM.asc;
        var h1MarginY = h1Fs * 0.67;                                // UA h1 margin
        var bodyDrop = MetricBaselineDrop(bodyFs, bodyPitch, serifM);
        var bodyDesc = bodyPitch - bodyDrop;
        var natAsc = natFs * 0.837;                                 // measured seat of the 3em initcap
        var capDrop = MetricBaselineDrop(capFs, capPitch, serifM);
        // the italic caption opens this far below the figure (measured 592.3
        // against the image bottom 577.1 with the 10.3 pt computed drop)
        const double CaptionGapPt = 4.9;

        var pageWidth = bodyWPt;
        var pageHeight = defaultPageHeight > 0 ? defaultPageHeight : 842.0;
        var contentL = padL;
        var contentR = pageWidth - padR;
        var contentW = contentR - contentL;

        var doc = new Document();
        var invc = System.Globalization.CultureInfo.InvariantCulture;

        foreach (Match spread in spreads)
        {
            var body = spread.Groups["body"].Value;
            var page = doc.Pages.Add(pageWidth, pageHeight);
            EnsureFonts(page);

            // 1. The spread's own absolutely positioned images: natural pixel
            // size × 0.75, anchored at the page TOP-left in source order.
            foreach (Match im in Regex.Matches(body,
                         "<img\\b[^>]*src\\s*=\\s*[\"']([^\"']+)[\"'][^>]*/?>",
                         RegexOptions.IgnoreCase))
            {
                // images inside the content div are the flow's own (the figure) —
                // only the ones before it are spread layers
                var contentOpen = Regex.Match(body, "<div\\b[^>]*class\\s*=", RegexOptions.IgnoreCase);
                if (contentOpen.Success && im.Index > contentOpen.Index) break;
                var data = LoadConverterImage(im.Groups[1].Value, options);
                if (data is null || !TryReadImagePixelSize(data, out var iw, out var ih)
                    || iw <= 0 || ih <= 0) continue;
                var w = iw * 0.75;
                var h = ih * 0.75;
                try
                {
                    page.AddImage(data, new Rectangle(0, pageHeight - h, w, pageHeight));
                }
                catch { /* undecodable layer: the canvas stays blank behind the text */ }
            }

            // 2. The padded content column, walked child by child with a
            // float-aware cursor. y runs TOP-DOWN in pt.
            var contentM = Regex.Match(body,
                "<div\\b[^>]*class\\s*=\\s*[\"'][^\"']*[\"'][^>]*>(?<inner>[\\s\\S]*)$",
                RegexOptions.IgnoreCase);
            var inner = contentM.Success ? contentM.Groups["inner"].Value : body;
            var y = padT;
            double floatBottom = 0;      // cleared headings resume below this
            var pendingRightTop = 0.0;   // float-right column opens at the flow position

            void EmitLine(string res, double fs, double x, double baselineTd, string text)
                => EmitPositionedRun(page, res, fs, x, pageHeight - baselineTd, text);

            static string Flat(string frag)
                => Regex.Replace(DecodeEntities(Regex.Replace(frag, "<[^>]+>", " ")), "\\s+", " ").Trim();

            foreach (Match el in Regex.Matches(inner,
                         "<(?<tag>h1|p|div)\\b(?<attrs>[^>]*)>(?<body>[\\s\\S]*?)</\\k<tag>>",
                         RegexOptions.IgnoreCase))
            {
                var tag = el.Groups["tag"].Value.ToLowerInvariant();
                var attrs = el.Groups["attrs"].Value;
                var elBody = el.Groups["body"].Value;
                // an element nested inside a float div is drawn by the float walk
                if (Regex.IsMatch(attrs, "class\\s*=\\s*[\"'][^\"']*float", RegexOptions.IgnoreCase))
                    tag = "float:" + (attrs.Contains("floatright", System.StringComparison.OrdinalIgnoreCase)
                        ? "right" : "left");
                else if (el.Index > 0 && Regex.IsMatch(
                             inner[..el.Index], "<div\\b[^>]*float[^>]*>(?:(?!</div>)[\\s\\S])*$",
                             RegexOptions.IgnoreCase))
                    continue;

                switch (tag)
                {
                    case "h1":
                    {
                        var cleared = Regex.IsMatch(attrs, "clear\\s*:\\s*both", RegexOptions.IgnoreCase);
                        var top = Regex.IsMatch(attrs, "class\\s*=\\s*[\"'][^\"']*top", RegexOptions.IgnoreCase);
                        var padTopEm = top ? CssEm("h1.top", "padding-top", 1.0)
                            : CssEm(".page3 h1", "padding-top", 2.0);
                        if (cleared && floatBottom > y)
                        {
                            // clear: both — the heading's box opens AT the float
                            // bottom, its own top margin suppressed (measured 700.0
                            // = floats' 648.4 + the 2em padding + the ascent).
                            y = floatBottom;
                        }
                        else
                        {
                            y += h1MarginY;
                        }
                        y += padTopEm * h1Fs;
                        var baseline = y + h1Asc;
                        EmitLine("F2", h1Fs, contentL, baseline, Flat(elBody));
                        y = baseline + (h1Fs * 1.15 - h1Asc) + h1MarginY;   // desc + margin-bottom
                        break;
                    }
                    case "p":
                    {
                        // a noind paragraph with a .national initial-cap float
                        var natM = Regex.Match(elBody,
                            "<span\\b[^>]*class\\s*=\\s*[\"'][^\"']*national[^\"']*[\"'][^>]*>(?<cap>[\\s\\S]*?)</span>",
                            RegexOptions.IgnoreCase);
                        var text = Flat(natM.Success
                            ? elBody.Remove(natM.Index, natM.Length) : elBody);
                        var pX = contentL + noindML;
                        var pW = contentR - pX;
                        if (natM.Success)
                        {
                            var cap = Flat(natM.Groups["cap"].Value);
                            var capW = MeasureFaceText("Times New Roman", cap, natFs)
                                + 0.1 * natFs;                        // margin-right 0.1em
                            var narrowLines = (int)System.Math.Ceiling(natFs / bodyPitch);
                            EmitLine("F5", natFs, pX, y + natAsc, cap);
                            var lines = MeasuredWordWrapPastFloat(text, pW - capW, pW,
                                narrowLines, "Times New Roman", bodyFs);
                            for (var i = 0; i < lines.Length; i++)
                                EmitLine("F5", bodyFs,
                                    i < narrowLines ? pX + capW : pX,
                                    y + bodyDrop + i * bodyPitch, lines[i]);
                            y += lines.Length * bodyPitch;
                        }
                        else
                        {
                            var lines = MeasuredWordWrap(text, pW, "Times New Roman", bodyFs);
                            foreach (var t in lines)
                            {
                                EmitLine("F5", bodyFs, pX, y + bodyDrop, t);
                                y += bodyPitch;
                            }
                        }
                        pendingRightTop = y;                          // a float-right column
                        break;                                        // opens on this grid
                    }
                    case "float:right":
                    {
                        // 20% column against the content right edge, its text on
                        // the body grid continuing from the paragraph above.
                        var share = 0.20;
                        var fm2 = Regex.Match(css.TryGetValue(".page3 div.floatright", out var frRule)
                            && frRule.TryGetValue("width", out var frW) ? frW : "20%", "([\\d.]+)\\s*%");
                        if (fm2.Success) share = double.Parse(fm2.Groups[1].Value, invc) / 100.0;
                        var colW = share * contentW;
                        var colX = contentR - colW;
                        var fy = pendingRightTop > 0 ? pendingRightTop : y;
                        var lines = MeasuredWordWrap(Flat(elBody), colW, "Times New Roman", bodyFs);
                        foreach (var t in lines)
                        {
                            EmitLine("F5", bodyFs, colX, fy + bodyDrop, t);
                            fy += bodyPitch;
                        }
                        if (fy > floatBottom) floatBottom = fy;
                        break;
                    }
                    case "float:left":
                    {
                        // 80% figure: caption head (bold, centred), the image at
                        // width:100% keeping its aspect, the italic caption.
                        var share = 0.80;
                        var flm = Regex.Match(css.TryGetValue(".floatleft", out var flRule)
                            && flRule.TryGetValue("width", out var flW) ? flW : "80%", "([\\d.]+)\\s*%");
                        if (flm.Success) share = double.Parse(flm.Groups[1].Value, invc) / 100.0;
                        var boxW = share * contentW;
                        var fy = y + contentEmPx * 0.75;              // margin: 1em 0
                        var headM = Regex.Match(elBody,
                            "<p\\b[^>]*captionhead[^>]*>(?<t>[\\s\\S]*?)</p>", RegexOptions.IgnoreCase);
                        if (headM.Success)
                        {
                            var t = Flat(headM.Groups["t"].Value);
                            var tw = MeasureFaceText("Arial-Bold", t, bodyFs);
                            var drop = MetricBaselineDrop(bodyFs, headPitch, arialM);
                            page.AddContentStream(System.Text.Encoding.ASCII.GetBytes(
                                string.Create(invc, $"{capColor.R / 255.0:0.###} {capColor.G / 255.0:0.###} {capColor.B / 255.0:0.###} rg\n")));
                            EmitLine("F2", bodyFs, contentL + (boxW - tw) / 2, fy + drop, t);
                            page.AddContentStream(System.Text.Encoding.ASCII.GetBytes("0 g\n"));
                            fy += headPitch;
                        }
                        var imM = Regex.Match(elBody, "<img\\b[^>]*src\\s*=\\s*[\"']([^\"']+)[\"']",
                            RegexOptions.IgnoreCase);
                        if (imM.Success)
                        {
                            var data = LoadConverterImage(imM.Groups[1].Value, options);
                            if (data is not null && TryReadImagePixelSize(data, out var iw, out var ih)
                                && iw > 0 && ih > 0)
                            {
                                var h = boxW * ih / iw;
                                try
                                {
                                    page.AddImage(data, new Rectangle(contentL,
                                        pageHeight - fy - h, contentL + boxW, pageHeight - fy));
                                }
                                catch { }
                                fy += h;
                            }
                        }
                        var capM = Regex.Match(elBody,
                            "<p\\b[^>]*class\\s*=\\s*[\"']caption[\"'][^>]*>(?<t>[\\s\\S]*?)</p>",
                            RegexOptions.IgnoreCase);
                        if (capM.Success)
                        {
                            fy += CaptionGapPt;
                            page.AddContentStream(System.Text.Encoding.ASCII.GetBytes(
                                string.Create(invc, $"{capColor.R / 255.0:0.###} {capColor.G / 255.0:0.###} {capColor.B / 255.0:0.###} rg\n")));
                            foreach (var t in MeasuredWordWrap(Flat(capM.Groups["t"].Value),
                                         boxW, "Arial-Italic", capFs))
                            {
                                EmitLine("F3", capFs, contentL, fy + capDrop, t);
                                fy += capPitch;
                            }
                            page.AddContentStream(System.Text.Encoding.ASCII.GetBytes("0 g\n"));
                        }
                        fy += contentEmPx * 0.75;                     // margin-bottom 1em
                        if (fy > floatBottom) floatBottom = fy;
                        break;
                    }
                }
            }
        }
        return doc.Pages.Count > 0 ? doc : null;
    }
}
