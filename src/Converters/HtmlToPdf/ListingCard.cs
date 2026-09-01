using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// The listing-card dialect: a rounded-bordered `.container` card of fixed-height
// `.item` rows (an archive-listing export — header band, alternating row fills,
// a floated inline-SVG icon per row). The card draws on SYMMETRIC
// 96 pt side margins one UA body margin below the 72 pt top, sizes it at the
// stylesheet's 80% width plus its own padding and border box, centres each row's
// text on the row's 48px line-height, and renders the item text in the UA serif
// while a script the substitute face lacks (the Devanagari file names) leaves
// invisible notdefs that still advance three quarters of an em apiece. All other
// geometry comes from the stylesheet's own values.
internal static partial class HtmlToPdfConverter
{
    // The card placement: symmetric 96 pt side margins, the card one
    // UA body margin (6 pt) below the 72 pt top margin.
    private const double LcMarginX = 96.0;
    private const double LcCardTop = 78.0;
    // An uncovered script's glyph advances 0.75 em (measured: the latin tail
    // starts 21 pt past two Devanagari code points at 14 pt).
    private const double LcNotdefAdvEm = 0.75;

    /// <summary>Render a rounded-container listing card, or null when the page
    /// does not carry the dialect's fingerprint.</summary>
    private static Document? TryRenderListingCard(string html,
        double pageWidth, double pageHeight)
    {
        if (!Regex.IsMatch(html, @"class\s*=\s*[""']container-header[""']", RegexOptions.IgnoreCase))
            return null;
        var css = ParseStyleSheet(html);
        if (!css.TryGetValue(".container", out var cont)
            || !cont.ContainsKey("border-radius")
            || !cont.TryGetValue("border", out var borderV)
            || !cont.TryGetValue("width", out var contWidthV)
            || !contWidthV.Trim().EndsWith('%')
            || !css.TryGetValue(".item", out var item)
            || !item.TryGetValue("line-height", out var itemLhV)
            || !TryParseLength(itemLhV, out var itemH)) return null;
        if (WinMetricsFor("Arial") is not { } fm) return null;

        double.TryParse(contWidthV.Trim().TrimEnd('%'),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var contPct);
        var borderW = 1.5;                                      // 2px
        var bm = Regex.Match(borderV, @"([\d.]+)\s*px");
        if (bm.Success) borderW = double.Parse(bm.Groups[1].Value,
            System.Globalization.CultureInfo.InvariantCulture) * 0.75;
        var borderCol = ParseCssColor(borderV) ?? Color.FromArgb(149, 151, 153);
        var radius = cont.TryGetValue("border-radius", out var radV)
            && TryParseLength(radV, out var radPt) ? radPt : 18.75;
        var pad = cont.TryGetValue("padding", out var padV)
            && TryParseLength(padV, out var padPt) ? padPt : 15.0;
        var itemFs = item.TryGetValue("font-size", out var ifsV)
            && TryParseLength(ifsV, out var ifs) ? ifs : 10.5;
        var headerFs = 16.5;
        Color headerBg = Color.FromArgb(228, 228, 228);
        if (css.TryGetValue(".container-header", out var hdr))
        {
            if (hdr.TryGetValue("font-size", out var hfsV)
                && TryParseLength(hfsV, out var hfs)) headerFs = hfs;
            if (hdr.TryGetValue("background-color", out var hbgV)
                && ParseCssColor(hbgV) is { } hbg) headerBg = hbg;
        }
        var headerPad = css.TryGetValue(".container-header", out var hdr2)
            && hdr2.TryGetValue("padding", out var hpV)
            && TryParseLength(hpV, out var hp) ? hp : 7.5;
        var folderFs = css.TryGetValue(".folder-name", out var fn)
            && fn.TryGetValue("font-size", out var ffsV)
            && TryParseLength(ffsV, out var ffs) ? ffs : 9.0;
        var evenBg = Color.FromArgb(240, 240, 240);              // .item:nth-child(even)
        var oddBg = Color.FromArgb(252, 252, 252);               // .item:nth-child(odd)
        foreach (var (sel, decls) in css)
            if (sel.Contains(":nth-child(even)") && decls.TryGetValue("background", out var ev)
                && ParseCssColor(ev) is { } evc) evenBg = evc;
            else if (sel.Contains(":nth-child(odd)") && decls.TryGetValue("background", out var od)
                && ParseCssColor(od) is { } odc) oddBg = odc;

        // ── parse: the header text and the item rows (icon svg + text) ──
        var headM = Regex.Match(html,
            @"class\s*=\s*[""']container-header[""'][^>]*>(?<body>[\s\S]*?)</div>",
            RegexOptions.IgnoreCase);
        if (!headM.Success) return null;
        var headHtml = headM.Groups["body"].Value;
        var folderM = Regex.Match(headHtml,
            @"<span\b[^>]*class\s*=\s*[""']folder-name[""'][^>]*>([\s\S]*?)</span>",
            RegexOptions.IgnoreCase);
        var folderText = folderM.Success
            ? Regex.Replace(DecodeEntities(folderM.Groups[1].Value), @"\s+", " ").Trim() : "";
        var headText = Regex.Replace(DecodeEntities(
            Regex.Replace(Regex.Replace(headHtml, @"<span[\s\S]*?</span>", ""), @"<[^>]+>", "")),
            @"\s+", " ").Trim();

        var items = new List<(string SvgXml, string Text)>();
        foreach (Match im in Regex.Matches(html,
            @"<div\b[^>]*class\s*=\s*[""']item[""'][^>]*>(?<body>[\s\S]*?)</div>",
            RegexOptions.IgnoreCase))
        {
            var bodyHtml = im.Groups["body"].Value;
            var svgM = Regex.Match(bodyHtml, @"<svg\b[\s\S]*?</svg>", RegexOptions.IgnoreCase);
            var text = Regex.Replace(DecodeEntities(
                Regex.Replace(Regex.Replace(bodyHtml, @"<svg\b[\s\S]*?</svg>", ""), @"<[^>]+>", "")),
                @"\s+", " ").Trim();
            items.Add((svgM.Success ? svgM.Value : "", text));
        }
        if (items.Count == 0 || headText.Length == 0) return null;

        // ── layout ──
        var doc = new Document();
        var page = doc.Pages.Add(pageWidth, pageHeight);
        EnsureFonts(page);
        var invc = System.Globalization.CultureInfo.InvariantCulture;

        var contentW = contPct / 100.0 * (pageWidth - 2 * LcMarginX);
        var cardW = contentW + 2 * pad + 2 * borderW;
        var contentX = LcMarginX + borderW + pad;
        // the header's line box paces on the face's hhea line (34px at 22pt)
        var headLineSum = HheaLineSumFor("Arial") ?? 1.15;
        var headLineH = MetricLineHeight(headerFs, headLineSum);
        var headerH = 2 * headerPad + headLineH;
        var cardH = 2 * borderW + 2 * pad + headerH + items.Count * itemH;

        // rounded card border: four edges joined by quarter-turn beziers, the
        // stroke centred half a border inside the outer box
        var k = 0.5523 * radius;                                // circle-approx control offset
        double x0 = LcMarginX + borderW / 2, y0 = LcCardTop + borderW / 2;
        double x1 = LcMarginX + cardW - borderW / 2, y1 = LcCardTop + cardH - borderW / 2;
        string P(double v) => v.ToString("F2", invc);
        var sbr = new StringBuilder();
        sbr.Append(string.Create(invc,
            $"q {borderCol.R / 255.0:0.###} {borderCol.G / 255.0:0.###} {borderCol.B / 255.0:0.###} RG {borderW:0.##} w "));
        double Y(double td) => pageHeight - td;
        sbr.Append($"{P(x0 + radius)} {P(Y(y0))} m {P(x1 - radius)} {P(Y(y0))} l ");
        sbr.Append($"{P(x1 - radius + k)} {P(Y(y0))} {P(x1)} {P(Y(y0 + radius - k))} {P(x1)} {P(Y(y0 + radius))} c ");
        sbr.Append($"{P(x1)} {P(Y(y1 - radius))} l ");
        sbr.Append($"{P(x1)} {P(Y(y1 - radius + k))} {P(x1 - radius + k)} {P(Y(y1))} {P(x1 - radius)} {P(Y(y1))} c ");
        sbr.Append($"{P(x0 + radius)} {P(Y(y1))} l ");
        sbr.Append($"{P(x0 + radius - k)} {P(Y(y1))} {P(x0)} {P(Y(y1 - radius + k))} {P(x0)} {P(Y(y1 - radius))} c ");
        sbr.Append($"{P(x0)} {P(Y(y0 + radius))} l ");
        sbr.Append($"{P(x0)} {P(Y(y0 + radius - k))} {P(x0 + radius - k)} {P(Y(y0))} {P(x0 + radius)} {P(Y(y0))} c ");
        sbr.Append("S Q\n");
        page.AddContentStream(Encoding.ASCII.GetBytes(sbr.ToString()));

        void Fill(double x, double yTd, double w, double h, Color c)
            => page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
                $"q {c.R / 255.0:0.###} {c.G / 255.0:0.###} {c.B / 255.0:0.###} rg " +
                $"{x:F2} {pageHeight - yTd - h:F2} {w:F2} {h:F2} re f Q\n")));

        // header band over the content box, its text at the header padding
        var headTop = LcCardTop + borderW + pad;
        Fill(contentX, headTop, contentW, headerH, headerBg);
        var headDrop = MetricBaselineDrop(headerFs, headLineH, fm);
        var headBase = headTop + headerPad + headDrop;
        EmitPositionedRun(page, "F1", headerFs, contentX + headerPad,
            pageHeight - headBase, headText + " ");
        var headW = MeasureFaceText("Arial", headText + " ", headerFs);
        if (folderText.Length > 0)
            EmitPositionedRun(page, "F1", folderFs, contentX + headerPad + headW,
                pageHeight - headBase, folderText);

        // item rows: alternating fills, the floated icon, the row-centred text
        var rowTop = headTop + headerH;
        // the row's line box centres the text on the 48px line-height
        var itemDrop = (itemH - itemFs * fm.sum) / 2 + itemFs * fm.asc;
        for (var i = 0; i < items.Count; i++)
        {
            // CSS nth-child is 1-based: the first row is odd
            Fill(contentX, rowTop, contentW, itemH, i % 2 == 0 ? oddBg : evenBg);
            var (svgXml, text) = items[i];
            var textX = contentX;
            if (svgXml.Length > 0)
            {
                var png = ImageRasterizer.RasterizeSvg(Encoding.UTF8.GetBytes(svgXml),
                    out var natWpx, out var natHpx);
                var iconW = natWpx > 0 ? natWpx * 0.75 : itemH;
                var iconH = natHpx > 0 ? natHpx * 0.75 : itemH;
                if (png is not null)
                    page.AddImage(png, new Rectangle(contentX, pageHeight - rowTop - iconH,
                        contentX + iconW, pageHeight - rowTop));
                textX += iconW;
            }
            // the UA serif draws the latin; a code point outside Latin leaves an
            // invisible notdef that still advances 0.75 em
            var pen = textX;
            var latin = new StringBuilder();
            void FlushLatin()
            {
                if (latin.Length == 0) return;
                EmitPositionedRun(page, "F5", itemFs, pen, pageHeight - (rowTop + itemDrop),
                    latin.ToString());
                pen += MeasureFaceText("Times New Roman", latin.ToString(), itemFs);
                latin.Clear();
            }
            foreach (var ch in text)
            {
                if (ch < 0x0250) latin.Append(ch);
                else { FlushLatin(); pen += LcNotdefAdvEm * itemFs; }
            }
            FlushLatin();
            rowTop += itemH;
        }
        return doc;
    }
}
