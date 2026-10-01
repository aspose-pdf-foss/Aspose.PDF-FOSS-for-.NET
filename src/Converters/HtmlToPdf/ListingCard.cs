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
        var lc = new ListingCardState();
        lc.html = html;
        lc.pageWidth = pageWidth;
        lc.pageHeight = pageHeight;
        if (!Regex.IsMatch(lc.html, @"class\s*=\s*[""']container-header[""']", RegexOptions.IgnoreCase))
            return null;
        if (!ReadListingCardStyle(lc)) return null;

        if (!ReadListingCardMarkup(lc)) return null;

        lc.doc = new Document();
        lc.page = lc.doc.Pages.Add(lc.pageWidth, lc.pageHeight);
        EnsureFonts(lc.page);
        lc.invc = System.Globalization.CultureInfo.InvariantCulture;

        lc.contentW = lc.contPct / 100.0 * (lc.pageWidth - 2 * LcMarginX);
        lc.cardW = lc.contentW + 2 * lc.pad + 2 * lc.borderW;
        lc.contentX = LcMarginX + lc.borderW + lc.pad;
        lc.headLineSum = HheaLineSumFor("Arial") ?? 1.15;
        lc.headLineH = MetricLineHeight(lc.headerFs, lc.headLineSum);
        lc.headerH = 2 * lc.headerPad + lc.headLineH;
        lc.cardH = 2 * lc.borderW + 2 * lc.pad + lc.headerH + lc.items.Count * lc.itemH;

        DrawListingCardFrame(lc);

        DrawListingCardHeader(lc);

        DrawListingCardItems(lc);
        return lc.doc;
    }
}
