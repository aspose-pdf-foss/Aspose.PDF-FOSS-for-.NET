using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The UA sheet's block sizes and margins for a tag (scaled to a non-16px body base): heading sizes and their em margins, paragraph, list, blockquote and table rhythm, the browser and MSO variants.</summary>
    private static void ApplyUaDefaultBlockStyle(string tag, BlockStyle s, bool browserUa, bool msoParagraphs, bool html5UaHeadings)
    {
        // A non-16px body base (the print-grid dialect's CSS body size) scales the
        // 16px-base UA sizes and margins below proportionally; the 12pt default
        // keeps them byte-identical.
        var uaParentSize = s.FontSize;
        var uaScale = browserUa && uaParentSize > 0 ? uaParentSize / 12.0 : 1.0;
        switch (tag.ToLowerInvariant())
        {
            case "h1": s.FontSize = 24; s.FontRes = "F2"; s.MarginTop = 16.455; s.MarginBottom = 3.015; break;
            case "h2": s.FontSize = 18; s.FontRes = "F2"; s.MarginTop = 13.875; s.MarginBottom = 0.435; break;
            case "h3": s.FontSize = 14.039; s.FontRes = "F2"; s.MarginTop = 13.793; s.MarginBottom = 0.353; break;
            case "h4": s.FontSize = 12; s.FontRes = "F2"; s.MarginTop = 13.44; s.MarginBottom = 0; break;
            case "h5": s.FontSize = 9.96; s.FontRes = "F2"; s.MarginTop = 14.9625; s.MarginBottom = 1.5225; break;
            case "h6": s.FontSize = 9; s.FontRes = "F2"; s.MarginTop = 15.2175; s.MarginBottom = 1.7775; break;
            case "p": s.MarginTop = 13.44; s.MarginBottom = 0; break;
            case "blockquote": s.MarginTop = 13.44; s.MarginBottom = 0; s.LeftIndent += 30; break;
            case "ul":
            case "ol": s.LeftIndent += 30; s.MarginTop = 13.44; s.MarginBottom = 0; break;
            case "li": s.IsListItem = true; break;
            case "pre": s.FontRes = "F4"; break;
        }
        // Full-document flow: the pairwise-gap constants above assume the FOLLOWING
        // box supplies its own top margin to complete the gap — true for P↔P/P↔H
        // runs, but a bare <div> or text node adds nothing, so a heading before one
        // would sit too close. Use the browser's real per-element margins (0.67em on
        // h1 rising to 2.33em on h6, 1em on p), symmetric top and bottom.
        if (uaScale != 1.0)
        {
            if (s.FontSize != uaParentSize) s.FontSize *= uaScale;
            s.MarginTop *= uaScale;
            s.MarginBottom *= uaScale;
        }
        // (a UA pre is 0.87 of the size it inherits - the pre law's factor, probed against the reference)
        if (browserUa && tag.Equals("pre", StringComparison.OrdinalIgnoreCase)) s.FontSize *= PreFaceSizeFactor;
        // An html5-doctype bare UA document (see Convert's html5BareUa): the
        // heading margins are the browser's real per-element em values resolved
        // against the PARENT size, symmetric — measured on the
        // h3-over-inline sheet: the h3 opens 72 + max(6, 12) from the page top
        // and stands 12 above the following bare inline (1.00 em of the 12 pt
        // root, NOT of its own 14.04). The mid-document pairwise constants
        // assume a following block completes the gap, which a bare inline
        // never does.
        if (html5UaHeadings && tag.Length == 2 && (tag[0] is 'h' or 'H')
            && tag[1] is >= '1' and <= '6')
        {
            // (the CSS 2.1 sample sheet, em of the heading's OWN size: h2 0.75em = 13.5 at 18 pt, probed on the
            // html5 worksheet's h2-to-table gap; the h3-over-inline sheet's 12 = 0.83 x 14.04 within 0.35)
            s.MarginTop = s.MarginBottom = UaBlockMarginEmOf(tag) * s.FontSize;
        }
        // (an html5-doctype document's headings keep the CSS 2.1 margins set above)
        var html5Heading = html5UaHeadings && tag.Length == 2 && (tag[0] is 'h' or 'H') && tag[1] is >= '1' and <= '6';
        if (browserUa && !html5Heading)
            switch (tag.ToLowerInvariant())
            {
                case "h1": s.MarginTop = s.MarginBottom = 0.67 * s.FontSize; break;
                case "h2": s.MarginTop = s.MarginBottom = 0.83 * s.FontSize; break;
                case "h3": s.MarginTop = s.MarginBottom = UaBlockMarginEmOf(tag) * s.FontSize; break;
                case "h4": s.MarginTop = s.MarginBottom = 1.33 * s.FontSize; break;
                // h5/h6: the UA margins measure ~15pt (20px)
                // symmetric — NOT 1.67/2.33em of the pt sizes above.
                case "h5": s.MarginTop = s.MarginBottom = 15.0 * uaScale; break;
                case "h6": s.MarginTop = s.MarginBottom = 15.0 * uaScale; break;
                // The UA paragraph margin is 1.12em (probed: a text→p / p→p / p→text
                // ladder gaps uniformly at 13.44 on the 12 pt base) - on a PLAIN
                // Word-filtered page too, whose paragraphs pitch 26.94 = the 13.5 line
                // box plus this margin, collapsed (MEASURED, the filtered e-mail export's
                // prologue: baselines 88.7988 / 115.7388 / 142.6788 / 169.6188 /
                // 196.5588 down from the page top). The filtered arm's GROWN sheet keeps
                // the 1.00em its calibrated constants compose with.
                case "p":
                    s.MarginTop = s.MarginBottom = (msoParagraphs ? 1.00 : 1.12) * s.FontSize;
                    break;
            }
    }

    /// <summary>The styled-article rhythm: the docs-site sheet's own block margins per tag (a Bootstrap-reboot model). True when the tag took its style here and the later rhythms must not touch it.</summary>
    private static bool ApplyArticleRhythmBlockStyle(string tag, BlockStyle s)
    {
        switch (tag.ToLowerInvariant())
        {
            case "p":
                s.MarginTop = 0; s.MarginBottom = CssRootFontPt; return true;
            case "ul": case "ol":
                s.MarginTop = 0; s.MarginBottom = CssRootFontPt;
                s.LeftIndent += ArticleListIndentPt; return true;
            case "li":
                s.IsListItem = true; s.MarginBottom = ArticleLiGapPt; return true;
            case "h1":
                s.FontSize = CssRootFontPt * 2; s.FontRes = "F2";
                // The page opens 31.5 below the top
                // margin (≈ the sheet's h1 margin-top 2rem + the content row's
                // .5rem padding + the body's 2px top border).
                s.MarginTop = ArticleH1TopPt; s.MarginBottom = CssRootFontPt * 0.5; return true;
            case "h2":
                s.FontSize = CssRootFontPt * 1.6; s.FontRes = "F2";
                s.MarginTop = CssRootFontPt * 2; s.MarginBottom = CssRootFontPt; return true;
            case "h3":
                s.FontSize = CssRootFontPt * 1.4; s.FontRes = "F2";
                s.MarginTop = CssRootFontPt * 1.5; s.MarginBottom = CssRootFontPt; return true;
            case "h4":
                s.FontSize = CssRootFontPt * 1.2; s.FontRes = "F2";
                s.MarginTop = CssRootFontPt * 1.5; s.MarginBottom = CssRootFontPt; return true;
        }
        return false;
    }
}
