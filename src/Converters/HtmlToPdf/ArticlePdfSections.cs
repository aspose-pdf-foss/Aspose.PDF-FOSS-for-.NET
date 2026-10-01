using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Article PDF sections: title band, description column, content flow, the absolute-date layout and the footer.</summary>
    private static bool TryRenderDateAbsolute(ArticlePdfState ap, bool dateAbsolute)
    {
        if (dateAbsolute)
        {
            if (ap.dateText.Length > 0)
            {
                var dwAbs = MeasureFaceText("Times New Roman", ap.dateText, ApContentFs);
                EmitPositionedRun(ap.page1, "F5", ApContentFs,
                    ap.pageWidth - ap.marginRight - ap.wrapperPadX - dwAbs,
                    ap.marginBottom + ap.datePadBottom + ApContentLineH - ap.contentDrop, ap.dateText);
            }
            var contTop = 0.0;
            if (ap.descIdx < ap.descLines.Length)
            {
                NewPage(ap);
                EnsureFont(ap.page, "Times-BoldItalic", "F8");
                DescInk(ap);
                var cont = 0;
                for (; ap.descIdx < ap.descLines.Length; ap.descIdx++, cont++)
                    Emit(ap, "F8", ApDescFs, ap.colLeftX, ap.marginTop + cont * ApDescLineH + ap.descDrop,
                        ap.descLines[ap.descIdx]);
                BlackInk(ap);
                contTop = ap.marginTop + cont * ApDescLineH;
            }
            var fyTd = contTop + ap.wrapperPadB;
            var disclaimerW = ap.pageWidth - ap.marginLeft - ap.marginRight;
            foreach (var blk in ap.footer)
            {
                var drop = MetricBaselineDrop(ApContentFs, ApContentLineH, ap.fm);
                foreach (var ln in MeasuredWordWrap(blk.Text, disclaimerW, "Times New Roman", ApContentFs))
                {
                    if (fyTd + ApContentLineH > ap.limit) { NewPage(ap); fyTd = ap.marginTop; }
                    Emit(ap, "F5", ApContentFs, ap.marginLeft, fyTd + drop, ln);
                    fyTd += ApContentLineH;
                }
                fyTd += ApBlockGapPt;
            }
            return true;
        }
        return false;
    }

    /// <summary></summary>
    private static void RenderFooter(ArticlePdfState ap)
    {
        ap.yTd -= ApBlockGapPt;                       // the wrapper's padding follows the last block directly
        ap.yTd += ap.wrapperPadB;

        // date band, right-aligned
        if (ap.dateText.Length > 0)
        {
            if (ap.yTd + ap.datePadTop + ApContentLineH + ap.datePadBottom > ap.limit)
            {
                NewPage(ap);
                ap.yTd = ap.marginTop + ap.datePadTop + ApContentLineH;
            }
            var dw = MeasureFaceText("Times New Roman", ap.dateText, ApContentFs);
            Emit(ap, "F5", ApContentFs, ap.pageWidth - ap.marginRight - ap.wrapperPadX - dw,
                ap.yTd + ap.datePadTop + ap.contentDrop, ap.dateText);
            ap.yTd += ap.datePadTop + ApContentLineH + ap.datePadBottom;
        }

        // footer: its 40px padded box of heading + disclaimer text
        ap.yTd += ap.disclaimerPad;
        foreach (var blk in ap.footer)
        {
            var (fs, lineH, res) = blk.Kind is "h2" or "h3"
                ? (ApH3Fs, ApH3LineH, "F2") : (ApContentFs, ApContentLineH, "F1");
            var drop = MetricBaselineDrop(fs, lineH, ap.fm);
            var lines = MeasuredWordWrap(blk.Text, ap.fullW,
                blk.Kind is "h2" or "h3" ? "Arial-Bold" : "Arial", fs);
            foreach (var ln in lines)
            {
                if (ap.yTd + lineH > ap.limit) { NewPage(ap); ap.yTd = ap.marginTop; }
                Emit(ap, res, fs, ap.colLeftX, ap.yTd + drop, ln);
                ap.yTd += lineH;
            }
            ap.yTd += ApBlockGapPt;
        }
    }

    /// <summary></summary>
    private static void RenderContent(ArticlePdfState ap, bool dateAbsolute)
    {
        ap.page1 = ap.page;

        ap.col1X = ap.colLeftX + ap.colLeftShare * ap.innerW;
        ap.col1W = ap.pageWidth - ap.marginRight - ap.wrapperPadX - ap.col1X;
        ap.fullW = ap.pageWidth - ap.marginRight - ap.wrapperPadX - ap.colLeftX;
        ap.yTd = ap.colsTop;
        // The absolute-date variant honours the first <p>'s UA top margin; the
        // in-flow variant's pagination was calibrated without it and keeps its seat.
        if (dateAbsolute) ap.yTd += ApContentTopMarginPt;
        ap.firstPage = true;
        foreach (var blk in ap.content)
        {
            var (fs, lineH, res) = blk.Kind switch
            {
                "h2" => (ApH2Fs, ApH2LineH, "F2"),
                "h3" => (ApH3Fs, ApH3LineH, "F2"),
                _ => (ApContentFs, ApContentLineH, "F1"),
            };
            var drop = MetricBaselineDrop(fs, lineH, ap.fm);
            var face = blk.Kind is "h2" or "h3" ? "Arial-Bold" : "Arial";
            var bullet = blk.Kind == "li" ? "• " : "";
            var lines = MeasuredWordWrap(bullet + blk.Text, ap.col1W, face, fs);
            foreach (var ln in lines)
            {
                if (ap.yTd + lineH > ap.limit)
                {
                    NewPage(ap);
                    ap.firstPage = false;
                    ap.yTd = ap.marginTop + ApContentLineH;   // measured: content resumes one line below the margin
                    // re-wrap the remaining block at the full width? — the split
                    // line keeps its wrap; the NEXT block re-wraps (page-count
                    // accuracy holds well within a page)
                }
                Emit(ap, res, fs, ap.firstPage ? ap.col1X : ap.colLeftX, ap.yTd + drop, ln);
                ap.yTd += lineH;
            }
            ap.yTd += ApBlockGapPt - (blk.Kind == "li" ? ApBlockGapPt - 3.75 : 0);
        }
        // Absolute-date variant: the date pins to page 1's bottom edge (its 55px
        // padding-bottom above the margin), the description column resumes at the
        // top margin of a fresh page, and the disclaimer — an unpadded width:100%
        // block outside the wrapper — sets in the UA serif at the page margin one
    }

    /// <summary></summary>
    private static void RenderDescription(ArticlePdfState ap)
    {
        ap.colsTop = ap.bandTop + ap.bandH + ap.titleMarB;
        ap.colLeftX = ap.marginLeft + ap.wrapperPadX;
        ap.innerW = ap.pageWidth - ap.marginLeft - ap.marginRight - 2 * ap.wrapperPadX;
        ap.colLeftW = ap.colLeftShare * ap.innerW - ap.colLeftPadR;
        ap.descLines = MeasuredWordWrap(ap.descText, ap.colLeftW, "Times New Roman Bold Italic", ApDescFs);
        ap.descDrop = MetricBaselineDrop(ApDescFs, ApDescLineH, ap.fm);
        ap.descColor = ap.css.TryGetValue(".article-pdf__description", out var descRule)
            && descRule.TryGetValue("color", out var descColV)
            && ParseCssColor(descColV) is { } dcv ? dcv : Color.FromArgb(23, 54, 93);
        EnsureFont(ap.page, "Times-BoldItalic", "F8");
        ap.descIdx = 0;
        DescInk(ap);
        for (; ap.descIdx < ap.descLines.Length; ap.descIdx++)
        {
            var yb = ap.colsTop + ap.colLeftPadT + ap.descIdx * ApDescLineH + ap.descDrop;
            if (yb > ap.limit) break;                 // continues on page 2 (absolute-date
                                                   // variant); clipped otherwise
            Emit(ap, "F8", ApDescFs, ap.colLeftX, yb, ap.descLines[ap.descIdx]);
        }
        BlackInk(ap);
    }

    /// <summary></summary>
    private static void RenderTitleBand(ArticlePdfState ap)
    {
        ap.doc = new Document();
        ap.page = ap.doc.Pages.Add(ap.pageWidth, ap.pageHeight);
        EnsureFonts(ap.page);
        ap.invc = System.Globalization.CultureInfo.InvariantCulture;
        ap.limit = ap.pageHeight - ap.marginBottom;
        ap.contentDrop = MetricBaselineDrop(ApContentFs, ApContentLineH, ap.fm);

        ap.bandTop = ap.marginTop + ap.articlePadTop;
        ap.titlePadL = 30.0;
        ap.titlePadR = 22.5;
        ap.titlePadY = 7.5;
        ap.titleLines = MeasuredWordWrap(ap.titleText, ap.titleMaxW - ap.titlePadL - ap.titlePadR,
            "Arial-Bold", ApTitleFs);
        ap.bandH = 2 * ap.titlePadY + ap.titleLines.Length * ApTitleLineH;
        var bandColor = ap.css.TryGetValue(".article-pdf__title", out var titleRule)
            && titleRule.TryGetValue("background-color", out var bandV)
            && ParseCssColor(bandV) is { } bv ? bv : Color.FromArgb(226, 23, 31);
        ap.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(ap.invc,
            $"q {bandColor.R / 255.0:0.###} {bandColor.G / 255.0:0.###} {bandColor.B / 255.0:0.###} rg " +
            $"{ap.marginLeft:F2} {ap.pageHeight - ap.bandTop - ap.bandH:F2} {ap.titleMaxW:F2} {ap.bandH:F2} re f Q\n")));
        ap.titleDrop = MetricBaselineDrop(ApTitleFs, ApTitleLineH, ap.fm);
        for (var i = 0; i < ap.titleLines.Length; i++)
            Emit(ap, "F2", ApTitleFs, ap.marginLeft + ap.titlePadL,
                ap.bandTop + ap.titlePadY + i * ApTitleLineH + ap.titleDrop, ap.titleLines[i]);

        ap.holderW = Px(ap, ".article-pdf__logo-holder", "width", 165.0);
        ap.holderPadT = Px(ap, ".article-pdf__logo-holder", "padding-top", 30.0);
        if (ap.logoBytes is not null)
        {
            var lw = ap.holderW;
            var lh = lw * ap.logoNatH / ap.logoNatW;
            var lRight = ap.pageWidth - ap.marginRight - 30.0;
            var lTopTd = ap.marginTop + ap.holderPadT;
            try
            {
                ap.page.AddImage(ap.logoBytes, new Rectangle(lRight - lw,
                    ap.pageHeight - lTopTd - lh, lRight, ap.pageHeight - lTopTd));
            }
            catch { /* undecodable image: the holder stays empty like the browser's broken frame */ }
        }
        else if (ap.logoAlt.Length > 0)
            Emit(ap, "F1", ApContentFs, ap.pageWidth - ap.marginRight - 30.0 - 165.0,
                ap.marginTop + 30.0 + ap.contentDrop, ap.logoAlt);

        // The date's position drives the variant: `position:absolute; bottom:0`
        // pins the date band to the FIRST page's bottom edge, lets the
        // description column continue onto page 2, and drops the disclaimer's
        // padded box (measured; the in-flow variant
    }

    /// <summary></summary>
    private static bool ParseArticle(ArticlePdfState ap)
    {
        ap.logoM = Regex.Match(ap.html, @"class\s*=\s*[""'][^""']*article-pdf__logo-holder[\s\S]*?<img\b[^>]*alt\s*=\s*[""']([^""']*)",
            RegexOptions.IgnoreCase);
        if (ap.logoM.Success) ap.logoAlt = ap.logoM.Groups[1].Value;
        ap.logoBytes = null;
        ap.logoNatW = 0; ap.logoNatH = 0;
        ap.logoSrcM = Regex.Match(ap.html,
            @"class\s*=\s*[""'][^""']*article-pdf__logo-holder[\s\S]*?<img\b[^>]*src\s*=\s*[""']([^""']*)",
            RegexOptions.IgnoreCase);
        if (ap.logoSrcM.Success && !string.IsNullOrEmpty(ap.basePath))
        {
            var data = LoadConverterImage(ap.logoSrcM.Groups[1].Value,
                new HtmlLoadOptions(ap.basePath!));
            if (data is not null && TryReadImagePixelSize(data) is (var logoW, var logoH)
                && logoW > 0 && logoH > 0)
            {
                ap.logoNatW = logoW;
                ap.logoNatH = logoH;
                ap.logoBytes = data;
            }
        }
        ap.leftM = Regex.Match(ap.html,
            @"class\s*=\s*[""'][^""']*article-pdf__col--left[""'][^>]*>(?<body>[\s\S]*?)<div\b[^>]*article-pdf__col--right",
            RegexOptions.IgnoreCase);
        if (ap.leftM.Success) ap.descText = Flat(ap, ap.leftM.Groups["body"].Value);

        ap.rightM = Regex.Match(ap.html,
            @"class\s*=\s*[""'][^""']*article-pdf__col--right[""'][^>]*>(?<body>[\s\S]*?)<div\b[^>]*article-pdf__date",
            RegexOptions.IgnoreCase);
        if (!ap.rightM.Success) return false;
        ParseBlocksInto(ap, ap.rightM.Groups["body"].Value, ap.content);
        ap.footM = Regex.Match(ap.html, @"<footer\b[^>]*>(?<body>[\s\S]*)</footer>", RegexOptions.IgnoreCase);
        if (ap.footM.Success)
        {
            ParseBlocksInto(ap, ap.footM.Groups["body"].Value, ap.footer);
            // the footer's loose text after its heading is a paragraph of its own
            var loose = Flat(ap, Regex.Replace(ap.footM.Groups["body"].Value,
                @"<(p|h2|h3|li)\b[^>]*>[\s\S]*?</\1>", " ", RegexOptions.IgnoreCase));
            if (loose.Length > 0) ap.footer.Add(new ApBlock { Kind = "p", Text = loose });
        }
        if (ap.content.Count == 0 || ap.titleText.Length == 0) return false;
        return true;
    }
}
