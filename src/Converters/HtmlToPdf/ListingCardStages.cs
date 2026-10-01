using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>A filled rectangle at top-down y on the listing card page.</summary>
    private static void FillListingRect(ListingCardState lc, double x, double yTd, double w, double h, Color c)
        => lc.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(lc.invc,
            $"q {c.R / 255.0:0.###} {c.G / 255.0:0.###} {c.B / 255.0:0.###} rg " +
            $"{x:F2} {lc.pageHeight - yTd - h:F2} {w:F2} {h:F2} re f Q\n")));

    /// <summary>Draw the header band: its fill, the header text and the folder name after it.</summary>
    private static void DrawListingCardHeader(ListingCardState lc)
    {
        lc.headTop = LcCardTop + lc.borderW + lc.pad;
        FillListingRect(lc, lc.contentX, lc.headTop, lc.contentW, lc.headerH, lc.headerBg);
        lc.headDrop = MetricBaselineDrop(lc.headerFs, lc.headLineH, lc.fm);
        lc.headBase = lc.headTop + lc.headerPad + lc.headDrop;
        EmitPositionedRun(lc.page, "F1", lc.headerFs, lc.contentX + lc.headerPad,
            lc.pageHeight - lc.headBase, lc.headText + " ");
        lc.headW = MeasureFaceText("Arial", lc.headText + " ", lc.headerFs);
        if (lc.folderText.Length > 0)
            EmitPositionedRun(lc.page, "F1", lc.folderFs, lc.contentX + lc.headerPad + lc.headW,
                lc.pageHeight - lc.headBase, lc.folderText);
    }

    /// <summary>Stroke the rounded card border: four Bezier corners at the circle-approximation control offset.</summary>
    private static void DrawListingCardFrame(ListingCardState lc)
    {
        lc.k = 0.5523 * lc.radius;                                // circle-approx control offset
        lc.x0 = LcMarginX + lc.borderW / 2;
        lc.y0 = LcCardTop + lc.borderW / 2;
        lc.x1 = LcMarginX + lc.cardW - lc.borderW / 2;
        lc.y1 = LcCardTop + lc.cardH - lc.borderW / 2;
        string P(double v) => v.ToString("F2", lc.invc);
        lc.sbr = new StringBuilder();
        lc.sbr.Append(Compat.Format(lc.invc,
            $"q {lc.borderCol.R / 255.0:0.###} {lc.borderCol.G / 255.0:0.###} {lc.borderCol.B / 255.0:0.###} RG {lc.borderW:0.##} w "));
        double Y(double td) => lc.pageHeight - td;
        lc.sbr.Append($"{P(lc.x0 + lc.radius)} {P(Y(lc.y0))} m {P(lc.x1 - lc.radius)} {P(Y(lc.y0))} l ");
        lc.sbr.Append($"{P(lc.x1 - lc.radius + lc.k)} {P(Y(lc.y0))} {P(lc.x1)} {P(Y(lc.y0 + lc.radius - lc.k))} {P(lc.x1)} {P(Y(lc.y0 + lc.radius))} c ");
        lc.sbr.Append($"{P(lc.x1)} {P(Y(lc.y1 - lc.radius))} l ");
        lc.sbr.Append($"{P(lc.x1)} {P(Y(lc.y1 - lc.radius + lc.k))} {P(lc.x1 - lc.radius + lc.k)} {P(Y(lc.y1))} {P(lc.x1 - lc.radius)} {P(Y(lc.y1))} c ");
        lc.sbr.Append($"{P(lc.x0 + lc.radius)} {P(Y(lc.y1))} l ");
        lc.sbr.Append($"{P(lc.x0 + lc.radius - lc.k)} {P(Y(lc.y1))} {P(lc.x0)} {P(Y(lc.y1 - lc.radius + lc.k))} {P(lc.x0)} {P(Y(lc.y1 - lc.radius))} c ");
        lc.sbr.Append($"{P(lc.x0)} {P(Y(lc.y0 + lc.radius))} l ");
        lc.sbr.Append($"{P(lc.x0)} {P(Y(lc.y0 + lc.radius - lc.k))} {P(lc.x0 + lc.radius - lc.k)} {P(Y(lc.y0))} {P(lc.x0 + lc.radius)} {P(Y(lc.y0))} c ");
        lc.sbr.Append("S Q\n");
        lc.page.AddContentStream(Encoding.ASCII.GetBytes(lc.sbr.ToString()));
    }

    /// <summary>Read the header, folder name and item bodies out of the markup. False when the card has no header text or no items.</summary>
    private static bool ReadListingCardMarkup(ListingCardState lc)
    {
        lc.headM = Regex.Match(lc.html,
            @"class\s*=\s*[""']container-header[""'][^>]*>(?<body>[\s\S]*?)</div>",
            RegexOptions.IgnoreCase);
        if (!lc.headM.Success) return false;
        lc.headHtml = lc.headM.Groups["body"].Value;
        lc.folderM = Regex.Match(lc.headHtml,
            @"<span\b[^>]*class\s*=\s*[""']folder-name[""'][^>]*>([\s\S]*?)</span>",
            RegexOptions.IgnoreCase);
        lc.folderText = lc.folderM.Success
            ? Regex.Replace(DecodeEntities(lc.folderM.Groups[1].Value), @"\s+", " ").Trim() : "";
        lc.headText = Regex.Replace(DecodeEntities(
            Regex.Replace(Regex.Replace(lc.headHtml, @"<span[\s\S]*?</span>", ""), @"<[^>]+>", "")),
            @"\s+", " ").Trim();

        lc.items = new List<(string SvgXml, string Text)>();
        foreach (Match im in Regex.Matches(lc.html,
            @"<div\b[^>]*class\s*=\s*[""']item[""'][^>]*>(?<body>[\s\S]*?)</div>",
            RegexOptions.IgnoreCase))
        {
            var bodyHtml = im.Groups["body"].Value;
            var svgM = Regex.Match(bodyHtml, @"<svg\b[\s\S]*?</svg>", RegexOptions.IgnoreCase);
            var text = Regex.Replace(DecodeEntities(
                Regex.Replace(Regex.Replace(bodyHtml, @"<svg\b[\s\S]*?</svg>", ""), @"<[^>]+>", "")),
                @"\s+", " ").Trim();
            lc.items.Add((svgM.Success ? svgM.Value : "", text));
        }
        if (lc.items.Count == 0 || lc.headText.Length == 0) return false;
        return true;
    }

    /// <summary>Read the stylesheet: container border and width, item line height and font, header font and background, row stripes. False when the idiom rules are missing.</summary>
    private static bool ReadListingCardStyle(ListingCardState lc)
    {
        lc.css = ParseStyleSheet(lc.html);
        if (!lc.css.TryGetValue(".container", out var cont)
            || !cont.ContainsKey("border-radius")
            || !cont.TryGetValue("border", out var borderV)
            || !cont.TryGetValue("width", out var contWidthV)
            || !contWidthV.Trim().EndsWith('%')
            || !lc.css.TryGetValue(".item", out var item)
            || !item.TryGetValue("line-height", out var itemLhV)
            || TryParseLength(itemLhV) is not { } itemH) return false;
        if (WinMetricsFor("Arial") is not { } fm) return false;
        lc.itemH = itemH;
        lc.fm = fm;

        double.TryParse(contWidthV.Trim().TrimEnd('%'),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var contPct);
        lc.contPct = contPct;
        lc.borderW = 1.5;                                      // 2px
        lc.bm = Regex.Match(borderV, @"([\d.]+)\s*px");
        if (lc.bm.Success) lc.borderW = double.Parse(lc.bm.Groups[1].Value,
            System.Globalization.CultureInfo.InvariantCulture) * 0.75;
        lc.borderCol = ParseCssColor(borderV) ?? Color.FromArgb(149, 151, 153);
        lc.radius = cont.TryGetValue("border-radius", out var radV)
            && TryParseLength(radV) is { } radPt ? radPt : 18.75;
        lc.pad = cont.TryGetValue("padding", out var padV)
            && TryParseLength(padV) is { } padPt ? padPt : 15.0;
        lc.itemFs = item.TryGetValue("font-size", out var ifsV)
            && TryParseLength(ifsV) is { } ifs ? ifs : 10.5;
        lc.headerFs = 16.5;
        lc.headerBg = Color.FromArgb(228, 228, 228);
        if (lc.css.TryGetValue(".container-header", out var hdr))
        {
            if (hdr.TryGetValue("font-size", out var hfsV)
                && TryParseLength(hfsV) is { } hfs) lc.headerFs = hfs;
            if (hdr.TryGetValue("background-color", out var hbgV)
                && ParseCssColor(hbgV) is { } hbg) lc.headerBg = hbg;
        }
        lc.headerPad = lc.css.TryGetValue(".container-header", out var hdr2)
            && hdr2.TryGetValue("padding", out var hpV)
            && TryParseLength(hpV) is { } hp ? hp : 7.5;
        lc.folderFs = lc.css.TryGetValue(".folder-name", out var fn)
            && fn.TryGetValue("font-size", out var ffsV)
            && TryParseLength(ffsV) is { } ffs ? ffs : 9.0;
        lc.evenBg = Color.FromArgb(240, 240, 240);              // .item:nth-child(even)
        lc.oddBg = Color.FromArgb(252, 252, 252);               // .item:nth-child(odd)
        foreach (var (sel, decls) in lc.css)
            if (sel.Contains(":nth-child(even)") && decls.TryGetValue("background", out var ev)
                && ParseCssColor(ev) is { } evc) lc.evenBg = evc;
            else if (sel.Contains(":nth-child(odd)") && decls.TryGetValue("background", out var od)
                && ParseCssColor(od) is { } odc) lc.oddBg = odc;
        return true;
    }

    /// <summary>Draw the item rows: alternating fills, the rasterized svg icon, then the text as Latin runs with notdef advances for the rest.</summary>
    private static void DrawListingCardItems(ListingCardState lc)
    {
        lc.rowTop = lc.headTop + lc.headerH;
        lc.itemDrop = (lc.itemH - lc.itemFs * lc.fm.sum) / 2 + lc.itemFs * lc.fm.asc;
        for (var i = 0; i < lc.items.Count; i++)
        {
            // CSS nth-child is 1-based: the first row is odd
            FillListingRect(lc, lc.contentX, lc.rowTop, lc.contentW, lc.itemH, i % 2 == 0 ? lc.oddBg : lc.evenBg);
            var (svgXml, text) = lc.items[i];
            var textX = lc.contentX;
            if (svgXml.Length > 0)
            {
                var (png, natWpx, natHpx) = ImageRasterizer.RasterizeSvgWithSize(Encoding.UTF8.GetBytes(svgXml));
                var iconW = natWpx > 0 ? natWpx * 0.75 : lc.itemH;
                var iconH = natHpx > 0 ? natHpx * 0.75 : lc.itemH;
                if (png is not null)
                    lc.page.AddImage(png, new Rectangle(lc.contentX, lc.pageHeight - lc.rowTop - iconH,
                        lc.contentX + iconW, lc.pageHeight - lc.rowTop));
                textX += iconW;
            }
            // the UA serif draws the latin; a code point outside Latin leaves an
            // invisible notdef that still advances 0.75 em
            var pen = textX;
            var latin = new StringBuilder();
            void FlushLatin()
            {
                if (latin.Length == 0) return;
                EmitPositionedRun(lc.page, "F5", lc.itemFs, pen, lc.pageHeight - (lc.rowTop + lc.itemDrop),
                    latin.ToString());
                pen += MeasureFaceText("Times New Roman", latin.ToString(), lc.itemFs);
                latin.Clear();
            }
            foreach (var ch in text)
            {
                if (ch < 0x0250) latin.Append(ch);
                else { FlushLatin(); pen += LcNotdefAdvEm * lc.itemFs; }
            }
            FlushLatin();
            lc.rowTop += lc.itemH;
        }
    }
}
