using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>A line with no room left under the flow cursor opens the next page and re-seats on it; inside a float column it is clipped instead, and the caller stops. False means the caller writes no line.</summary>
    private static bool SeatLineOnPage(BlockTextState bt, double needRoom)
    {
        if (bt.flow.y - needRoom < bt.marginBottom)
        {
            // Inside a float column the overflow is clipped, not paginated.
            if (bt.profile.floatBandDoc && bt.bandStack.Count > 0) { bt.flow.bandColClipped = true; return false; }
            bt.flow.page = bt.doc.Pages.Add(bt.pageWidth, bt.pageHeight);
            EnsureFonts(bt.flow.page, bt.docFontDict);
            bt.flow.y = FreshPageTopY(bt.profile, bt.pageHeight, bt.marginTop); bt.flow.pendingTopDrop = bt.profile.hasZeroTopMargin;
            // UA flow: a block pushed to a fresh page re-applies its margin-top at
            // the new page top (a continuation page's first paragraph baseline
            // = topMargin + p-gap + ascent, not topMargin + ascent).
            if (bt.uaFlow && bt.metrics.firstLineOfBlock) bt.flow.y -= bt.block.MarginTop;
            // The sheet-typography flow's continuation line hangs its box from the content top.
            if (bt.profile.sheetBoxFlow && bt.block.FontFamily is { Length: > 0 } contFace
                && WinMetricsFor(contFace) is not null && bt.metrics.blockFontSize > 0)
                bt.flow.y -= LineBoxAbove(contFace, bt.metrics.blockFontSize, bt.metrics.lineHeight);
        }
        return true;
    }
}
