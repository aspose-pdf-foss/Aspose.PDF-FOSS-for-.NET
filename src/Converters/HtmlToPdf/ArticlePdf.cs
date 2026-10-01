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
        var ap = new ArticlePdfState();
        ap.html = html;
        ap.css = css;
        ap.marginLeft = marginLeft;
        ap.marginRight = marginRight;
        ap.marginTop = marginTop;
        ap.marginBottom = marginBottom;
        ap.pageWidth = pageWidth;
        ap.pageHeight = pageHeight;
        ap.basePath = basePath;
        if (!ap.css.ContainsKey(".article-pdf__col-wrapper")
            || !ap.css.ContainsKey(".article-pdf__title")
            || !Regex.IsMatch(ap.html, @"class\s*=\s*[""']article-pdf[""']", RegexOptions.IgnoreCase))
            return null;
        if (WinMetricsFor("Arial") is not { } fm) return null;
        ap.fm = fm;

        ap.articlePadTop = Px(ap, ".article-pdf", "padding-top", 111.0);
        ap.titleMaxW = 0.65 * (ap.pageWidth - ap.marginLeft - ap.marginRight);
        ap.titleMarB = 24.0;
        ap.wrapperPadX = 30.0;
        ap.wrapperPadB = 165.0;
        if (ap.css.TryGetValue(".article-pdf__col-wrapper", out var wrapRule)
            && wrapRule.TryGetValue("padding", out var wrapPadV))
        {
            var box = ParseInlineMarginBox("margin:" + wrapPadV, ApContentFs);
            if (box.bottom > 0) ap.wrapperPadB = box.bottom;
            if (box.right > 0) ap.wrapperPadX = box.right;
        }
        ap.colLeftShare = 0.30;
        ap.colLeftPadR = 15.0;
        ap.colLeftPadT = 7.5;
        ap.datePadTop = 6.0;
        ap.datePadBottom = 41.25;
        ap.disclaimerPad = 30.0;

        ap.titleText = "";
        ap.logoAlt = "";
        ap.descText = "";
        ap.dateText = "";
        ap.content = new List<ApBlock>();
        ap.footer = new List<ApBlock>();

        ap.titleText = Flat(ap, Inner(ap, "article-pdf__title"));
        ap.dateText = Flat(ap, Inner(ap, "article-pdf__date"));
        if (!ParseArticle(ap)) return null;

        RenderTitleBand(ap);
        // keeps the original tail order and clips the description at page 1).
        var dateAbsolute = ap.css.TryGetValue(".article-pdf__date", out var dateRuleV)
            && dateRuleV.TryGetValue("position", out var datePosV)
            && datePosV.Contains("absolute", StringComparison.OrdinalIgnoreCase);

        RenderDescription(ap);
        RenderContent(ap, dateAbsolute);
        // wrapper padding-bottom below the column's end.
        if (TryRenderDateAbsolute(ap, dateAbsolute)) return ap.doc;

        RenderFooter(ap);
        return ap.doc;
    }
}
