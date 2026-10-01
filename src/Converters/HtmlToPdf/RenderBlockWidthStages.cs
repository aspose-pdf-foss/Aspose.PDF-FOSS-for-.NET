using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>A float-both-sides document measures the block against its own declared width and the face stack it names.</summary>
    private static void ResolveFloatBothSidesWidth(RenderBlockState rb, ConvertState cv, Block block)
    {
        if (cv.profile.floatBothSidesDoc && !block.IsImage && block.FontFamilyStack is { } famStack
            && block.FontFamily is { Length: > 0 } declaredFam
            && WinMetricsFor(declaredFam) is null)
            foreach (var cand in famStack.Split(','))
                if (cand.Trim().Trim('\'', '"').Trim() is { Length: > 0 } candName
                    && WinMetricsFor(candName) is not null)
                { block.FontFamily = candName; break; }
        // FLOAT FLOW: a block's own `margin-right` insets its wrap box, the mirror of
        // the margin-left that already sets LeftIndent. The certificate's body
        // paragraphs declare 90 px each side, so they wrap at 431.5 on the 96..499
        // content box - without this they ran to the content edge and re-broke every
        // line.
        if (cv.profile.floatBothSidesDoc && block.MarginRightPt > 0 && !block.IsTable && !block.IsImage)
            rb.availWidth = Math.Max(50, rb.availWidth - block.MarginRightPt);
        rb.metrics.floatBoxLeftPt = 0.0;
        rb.metrics.floatBoxWidthPt = 0.0;
        if (cv.profile.floatBothSidesDoc && block.WidthPx > 0 && !block.IsTable && !block.IsImage)
        {
            rb.metrics.floatBoxLeftPt = block.ShorthandLeftPt;
            rb.metrics.floatBoxWidthPt = block.WidthPx * 0.75;
            rb.availWidth = rb.metrics.floatBoxWidthPt;
        }
    }
}
