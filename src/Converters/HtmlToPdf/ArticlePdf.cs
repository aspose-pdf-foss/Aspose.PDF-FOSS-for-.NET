using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// The article-PDF export dialect (the `.article-pdf__*` class namespace): a red
// title band inset by the sheet's 148px article padding, an absolutely
// positioned logo holder whose missing image leaves its alt text, a float
// column pair — a serif description at 30% beside the content at 70% — whose
// SIDE-BY-SIDE layout holds for the first page only (the content resumes
// full-width at the wrapper's left padding after a page break), the
// wrapper's 220px bottom padding, and the date/footer tail. Content is
// paced at the UA 18px line on the sheet's 16px font with a uniform
// 17.25 pt block gap (measured between every p/ul/h2/h3 pair). All other
// geometry comes from the stylesheet's own pixel values.
internal static partial class HtmlToPdfConverter
{
    private sealed class ApBlock
    {
        public string Kind = "p";                  // p | li | h2 | h3
        public string Text = "";
    }

    // Block gap (measured): the white band between any two content
    // blocks (p→p, p→ul, p→h2, h3→p all pace on it).
    private const double ApBlockGapPt = 17.25;
    // UA heading scale on the sheet's 16px content font: h2 = 1.5em (24px = 18 pt,
    // 28px line = 21 pt), h3 = 1.17em (18.72px = 14.04 pt, 22px line = 16.5 pt).
    private const double ApH2Fs = 18.0;
    private const double ApH2LineH = 21.0;
    private const double ApH3Fs = 14.04;
    private const double ApH3LineH = 16.5;
    // The content font: 16px = 12 pt on the UA-normal 18px = 13.5 pt line.
    private const double ApContentFs = 12.0;
    private const double ApContentLineH = 13.5;
    // The content column's first <p> keeps its UA 1em top margin at the 16px
    // base (measured: the first content line box opens 12 pt
    // below the columns' top).
    private const double ApContentTopMarginPt = 12.0;
    // The description column's serif: 24px = 18 pt on a 28px = 21 pt line.
    private const double ApDescFs = 18.0;
    private const double ApDescLineH = 21.0;
    // The title: 28px = 21 pt on a 32px = 24 pt line.
    private const double ApTitleFs = 21.0;
    private const double ApTitleLineH = 24.0;

    /// <summary>Render an article-PDF export, or null when the page does not
    /// carry the dialect's class namespace.</summary>
    private static Document? TryRenderArticlePdf(string html,
        IReadOnlyDictionary<string, Dictionary<string, string>> css,
        double marginLeft, double marginRight, double marginTop, double marginBottom,
        double pageWidth, double pageHeight, string? basePath = null)
    {
        if (!css.ContainsKey(".article-pdf__col-wrapper")
            || !css.ContainsKey(".article-pdf__title")
            || !Regex.IsMatch(html, @"class\s*=\s*[""']article-pdf[""']", RegexOptions.IgnoreCase))
            return null;
        if (WinMetricsFor("Arial") is not { } fm) return null;

        double Px(string sel, string prop, double fallback)
            => css.TryGetValue(sel, out var r) && r.TryGetValue(prop, out var v)
               && TryParseLength(v, out var pt) ? pt : fallback;

        // the sheet's own pixel geometry
        var articlePadTop = Px(".article-pdf", "padding-top", 111.0);          // 148px
        var titleMaxW = 0.65 * (pageWidth - marginLeft - marginRight);         // max-width 65%
        var titleMarB = 24.0;                                                  // 32px
        var wrapperPadX = 30.0;                                                // 40px
        var wrapperPadB = 165.0;                                               // 220px
        if (css.TryGetValue(".article-pdf__col-wrapper", out var wrapRule)
            && wrapRule.TryGetValue("padding", out var wrapPadV))
        {
            var box = ParseInlineMarginBox("margin:" + wrapPadV, ApContentFs);
            if (box.bottom > 0) wrapperPadB = box.bottom;
            if (box.right > 0) wrapperPadX = box.right;
        }
        var colLeftShare = 0.30;                                               // .article-pdf__col--left width
        var colLeftPadR = 15.0;                                                // 20px
        var colLeftPadT = 7.5;                                                 // 10px
        var datePadTop = 6.0;                                                  // 8px
        var datePadBottom = 41.25;                                             // 55px
        var disclaimerPad = 30.0;                                              // 40px

        // ── parse the article ──
        string titleText = "", logoAlt = "", descText = "", dateText = "";
        var content = new List<ApBlock>();
        var footer = new List<ApBlock>();

        string Inner(string cls)
            => Regex.Match(html,
                @"<(?<tag>h1|div|footer)\b[^>]*class\s*=\s*[""'][^""']*" + Regex.Escape(cls) +
                @"[^""']*[""'][^>]*>(?<body>[\s\S]*?)</\k<tag>>",
                RegexOptions.IgnoreCase) is { Success: true } m ? m.Groups["body"].Value : "";
        static string Flat(string frag)
            => Regex.Replace(DecodeEntities(Regex.Replace(frag, @"<[^>]+>", " ")), @"\s+", " ").Trim();

        titleText = Flat(Inner("article-pdf__title"));
        dateText = Flat(Inner("article-pdf__date"));
        var logoM = Regex.Match(html, @"class\s*=\s*[""'][^""']*article-pdf__logo-holder[\s\S]*?<img\b[^>]*alt\s*=\s*[""']([^""']*)",
            RegexOptions.IgnoreCase);
        if (logoM.Success) logoAlt = logoM.Groups[1].Value;
        // The logo image itself, when its file resolves — through the shared
        // resolver, whose parent-directory fallback recovers the expected file
        // (a base pointing INTO the img folder doubles the folder on the naive
        // join; the parent-dir retry finds the file).
        byte[]? logoBytes = null;
        var logoNatW = 0; var logoNatH = 0;
        var logoSrcM = Regex.Match(html,
            @"class\s*=\s*[""'][^""']*article-pdf__logo-holder[\s\S]*?<img\b[^>]*src\s*=\s*[""']([^""']*)",
            RegexOptions.IgnoreCase);
        if (logoSrcM.Success && !string.IsNullOrEmpty(basePath))
        {
            var data = LoadConverterImage(logoSrcM.Groups[1].Value,
                new HtmlLoadOptions(basePath!));
            if (data is not null && TryReadImagePixelSize(data, out logoNatW, out logoNatH)
                && logoNatW > 0 && logoNatH > 0)
                logoBytes = data;
        }
        // the left column runs to the right column's opening div
        var leftM = Regex.Match(html,
            @"class\s*=\s*[""'][^""']*article-pdf__col--left[""'][^>]*>(?<body>[\s\S]*?)<div\b[^>]*article-pdf__col--right",
            RegexOptions.IgnoreCase);
        if (leftM.Success) descText = Flat(leftM.Groups["body"].Value);

        void ParseBlocksInto(string frag, List<ApBlock> into)
        {
            foreach (Match bm in Regex.Matches(frag,
                @"<(?<tag>p|h2|h3|li)\b[^>]*>(?<body>[\s\S]*?)</\k<tag>>", RegexOptions.IgnoreCase))
            {
                var txt = Flat(bm.Groups["body"].Value);
                if (txt.Length > 0)
                    into.Add(new ApBlock { Kind = bm.Groups["tag"].Value.ToLowerInvariant(), Text = txt });
            }
        }
        var rightM = Regex.Match(html,
            @"class\s*=\s*[""'][^""']*article-pdf__col--right[""'][^>]*>(?<body>[\s\S]*?)<div\b[^>]*article-pdf__date",
            RegexOptions.IgnoreCase);
        if (!rightM.Success) return null;
        ParseBlocksInto(rightM.Groups["body"].Value, content);
        var footM = Regex.Match(html, @"<footer\b[^>]*>(?<body>[\s\S]*)</footer>", RegexOptions.IgnoreCase);
        if (footM.Success)
        {
            ParseBlocksInto(footM.Groups["body"].Value, footer);
            // the footer's loose text after its heading is a paragraph of its own
            var loose = Flat(Regex.Replace(footM.Groups["body"].Value,
                @"<(p|h2|h3|li)\b[^>]*>[\s\S]*?</\1>", " ", RegexOptions.IgnoreCase));
            if (loose.Length > 0) footer.Add(new ApBlock { Kind = "p", Text = loose });
        }
        if (content.Count == 0 || titleText.Length == 0) return null;

        // ── layout ──
        var doc = new Document();
        var page = doc.Pages.Add(pageWidth, pageHeight);
        EnsureFonts(page);
        var invc = System.Globalization.CultureInfo.InvariantCulture;
        var limit = pageHeight - marginBottom;
        var contentDrop = MetricBaselineDrop(ApContentFs, ApContentLineH, fm);

        void Emit(string res, double fs, double x, double yTd, string text)
            => EmitPositionedRun(page, res, fs, x, pageHeight - yTd, text);
        void NewPage()
        {
            page = doc.Pages.Add(pageWidth, pageHeight);
            EnsureFonts(page);
        }

        // title band: article padding-top below the margin, the band the title's
        // padded box at 65% max-width, the text wrapped inside its padding
        var bandTop = marginTop + articlePadTop;
        var titlePadL = 30.0;                                                  // 40px
        var titlePadR = 22.5;                                                  // 30px
        var titlePadY = 7.5;                                                   // 10px
        var titleLines = MeasuredWordWrap(titleText, titleMaxW - titlePadL - titlePadR,
            "Arial-Bold", ApTitleFs);
        var bandH = 2 * titlePadY + titleLines.Length * ApTitleLineH;
        var bandColor = css.TryGetValue(".article-pdf__title", out var titleRule)
            && titleRule.TryGetValue("background-color", out var bandV)
            && ParseCssColor(bandV) is { } bv ? bv : Color.FromArgb(226, 23, 31);
        page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
            $"q {bandColor.R / 255.0:0.###} {bandColor.G / 255.0:0.###} {bandColor.B / 255.0:0.###} rg " +
            $"{marginLeft:F2} {pageHeight - bandTop - bandH:F2} {titleMaxW:F2} {bandH:F2} re f Q\n")));
        var titleDrop = MetricBaselineDrop(ApTitleFs, ApTitleLineH, fm);
        for (var i = 0; i < titleLines.Length; i++)
            Emit("F2", ApTitleFs, marginLeft + titlePadL,
                bandTop + titlePadY + i * ApTitleLineH + titleDrop, titleLines[i]);

        // the absolute logo holder: the image at the holder's 220px width ×
        // natural aspect (width:100%; height:auto), right-inset by the sheet's
        // 40px, one 40px pad below the page top — or its alt text when the
        // image file does not resolve
        var holderW = Px(".article-pdf__logo-holder", "width", 165.0);
        var holderPadT = Px(".article-pdf__logo-holder", "padding-top", 30.0);
        if (logoBytes is not null)
        {
            var lw = holderW;
            var lh = lw * logoNatH / logoNatW;
            var lRight = pageWidth - marginRight - 30.0;
            var lTopTd = marginTop + holderPadT;
            try
            {
                page.AddImage(logoBytes, new Rectangle(lRight - lw,
                    pageHeight - lTopTd - lh, lRight, pageHeight - lTopTd));
            }
            catch { /* undecodable image: the holder stays empty like the browser's broken frame */ }
        }
        else if (logoAlt.Length > 0)
            Emit("F1", ApContentFs, pageWidth - marginRight - 30.0 - 165.0,
                marginTop + 30.0 + contentDrop, logoAlt);

        // The date's position drives the variant: `position:absolute; bottom:0`
        // pins the date band to the FIRST page's bottom edge, lets the
        // description column continue onto page 2, and drops the disclaimer's
        // padded box (measured; the in-flow variant
        // keeps the original tail order and clips the description at page 1).
        var dateAbsolute = css.TryGetValue(".article-pdf__date", out var dateRuleV)
            && dateRuleV.TryGetValue("position", out var datePosV)
            && datePosV.Contains("absolute", StringComparison.OrdinalIgnoreCase);

        // the description column beside the first page of content — the
        // stylesheet's serif bold italic in its declared colour, wrapped on the
        // face that draws it
        var colsTop = bandTop + bandH + titleMarB;
        var colLeftX = marginLeft + wrapperPadX;
        var innerW = pageWidth - marginLeft - marginRight - 2 * wrapperPadX;
        var colLeftW = colLeftShare * innerW - colLeftPadR;
        var descLines = MeasuredWordWrap(descText, colLeftW, "Times New Roman Bold Italic", ApDescFs);
        var descDrop = MetricBaselineDrop(ApDescFs, ApDescLineH, fm);
        var descColor = css.TryGetValue(".article-pdf__description", out var descRule)
            && descRule.TryGetValue("color", out var descColV)
            && ParseCssColor(descColV) is { } dcv ? dcv : Color.FromArgb(23, 54, 93);
        EnsureFont(page, "Times-BoldItalic", "F8");
        void DescInk() => page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
            $"{descColor.R / 255.0:0.###} {descColor.G / 255.0:0.###} {descColor.B / 255.0:0.###} rg\n")));
        void BlackInk() => page.AddContentStream(Encoding.ASCII.GetBytes("0 0 0 rg\n"));
        var descIdx = 0;
        DescInk();
        for (; descIdx < descLines.Length; descIdx++)
        {
            var yb = colsTop + colLeftPadT + descIdx * ApDescLineH + descDrop;
            if (yb > limit) break;                 // continues on page 2 (absolute-date
                                                   // variant); clipped otherwise
            Emit("F8", ApDescFs, colLeftX, yb, descLines[descIdx]);
        }
        BlackInk();
        var page1 = page;

        // The content column: beside the description on page 1; after a page
        // break it moves to the wrapper's left edge but KEEPS the column width
        // (measured: continuation lines end at x≈399, the same 360 pt measure).
        var col1X = colLeftX + colLeftShare * innerW;
        var col1W = pageWidth - marginRight - wrapperPadX - col1X;
        var fullW = pageWidth - marginRight - wrapperPadX - colLeftX;
        var yTd = colsTop;
        // The absolute-date variant honours the first <p>'s UA top margin; the
        // in-flow variant's pagination was calibrated without it and keeps its seat.
        if (dateAbsolute) yTd += ApContentTopMarginPt;
        var firstPage = true;
        foreach (var blk in content)
        {
            var (fs, lineH, res) = blk.Kind switch
            {
                "h2" => (ApH2Fs, ApH2LineH, "F2"),
                "h3" => (ApH3Fs, ApH3LineH, "F2"),
                _ => (ApContentFs, ApContentLineH, "F1"),
            };
            var drop = MetricBaselineDrop(fs, lineH, fm);
            var face = blk.Kind is "h2" or "h3" ? "Arial-Bold" : "Arial";
            var bullet = blk.Kind == "li" ? "• " : "";
            var lines = MeasuredWordWrap(bullet + blk.Text, col1W, face, fs);
            foreach (var ln in lines)
            {
                if (yTd + lineH > limit)
                {
                    NewPage();
                    firstPage = false;
                    yTd = marginTop + ApContentLineH;   // measured: content resumes one line below the margin
                    // re-wrap the remaining block at the full width? — the split
                    // line keeps its wrap; the NEXT block re-wraps (page-count
                    // accuracy holds well within a page)
                }
                Emit(res, fs, firstPage ? col1X : colLeftX, yTd + drop, ln);
                yTd += lineH;
            }
            yTd += ApBlockGapPt - (blk.Kind == "li" ? ApBlockGapPt - 3.75 : 0);
        }
        // Absolute-date variant: the date pins to page 1's bottom edge (its 55px
        // padding-bottom above the margin), the description column resumes at the
        // top margin of a fresh page, and the disclaimer — an unpadded width:100%
        // block outside the wrapper — sets in the UA serif at the page margin one
        // wrapper padding-bottom below the column's end.
        if (dateAbsolute)
        {
            if (dateText.Length > 0)
            {
                var dwAbs = MeasureFaceText("Times New Roman", dateText, ApContentFs);
                EmitPositionedRun(page1, "F5", ApContentFs,
                    pageWidth - marginRight - wrapperPadX - dwAbs,
                    marginBottom + datePadBottom + ApContentLineH - contentDrop, dateText);
            }
            var contTop = 0.0;
            if (descIdx < descLines.Length)
            {
                NewPage();
                EnsureFont(page, "Times-BoldItalic", "F8");
                DescInk();
                var cont = 0;
                for (; descIdx < descLines.Length; descIdx++, cont++)
                    Emit("F8", ApDescFs, colLeftX, marginTop + cont * ApDescLineH + descDrop,
                        descLines[descIdx]);
                BlackInk();
                contTop = marginTop + cont * ApDescLineH;
            }
            var fyTd = contTop + wrapperPadB;
            var disclaimerW = pageWidth - marginLeft - marginRight;
            foreach (var blk in footer)
            {
                var drop = MetricBaselineDrop(ApContentFs, ApContentLineH, fm);
                foreach (var ln in MeasuredWordWrap(blk.Text, disclaimerW, "Times New Roman", ApContentFs))
                {
                    if (fyTd + ApContentLineH > limit) { NewPage(); fyTd = marginTop; }
                    Emit("F5", ApContentFs, marginLeft, fyTd + drop, ln);
                    fyTd += ApContentLineH;
                }
                fyTd += ApBlockGapPt;
            }
            return doc;
        }

        yTd -= ApBlockGapPt;                       // the wrapper's padding follows the last block directly
        yTd += wrapperPadB;

        // date band, right-aligned
        if (dateText.Length > 0)
        {
            if (yTd + datePadTop + ApContentLineH + datePadBottom > limit)
            {
                NewPage();
                yTd = marginTop + datePadTop + ApContentLineH;
            }
            var dw = MeasureFaceText("Times New Roman", dateText, ApContentFs);
            Emit("F5", ApContentFs, pageWidth - marginRight - wrapperPadX - dw,
                yTd + datePadTop + contentDrop, dateText);
            yTd += datePadTop + ApContentLineH + datePadBottom;
        }

        // footer: its 40px padded box of heading + disclaimer text
        yTd += disclaimerPad;
        foreach (var blk in footer)
        {
            var (fs, lineH, res) = blk.Kind is "h2" or "h3"
                ? (ApH3Fs, ApH3LineH, "F2") : (ApContentFs, ApContentLineH, "F1");
            var drop = MetricBaselineDrop(fs, lineH, fm);
            var lines = MeasuredWordWrap(blk.Text, fullW,
                blk.Kind is "h2" or "h3" ? "Arial-Bold" : "Arial", fs);
            foreach (var ln in lines)
            {
                if (yTd + lineH > limit) { NewPage(); yTd = marginTop; }
                Emit(res, fs, colLeftX, yTd + drop, ln);
                yTd += lineH;
            }
            yTd += ApBlockGapPt;
        }
        return doc;
    }
}
