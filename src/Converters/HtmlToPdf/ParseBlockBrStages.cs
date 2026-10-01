using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>A break on an empty line in the metric flow closes the block rather than spacing it; false means the caller is done with this token.</summary>
    private static bool CloseMetricLayoutBlankBr(ParseBlocksState pb)
    {
        if (pb.metricLayout && pb.currentText.ToString().Trim().Length == 0)
        {
            pb.currentText.Clear();
            // A <br> right after an INLINE image ends the image's own line box and opens none
            // (probed: `<a><img></a> <br><h1>` seats the heading 16.08 under the image line).
            if (pb.blocks.Count > 0 && pb.blocks[^1].IsImage && pb.blocks[^1].ImageInline) return false;
            // (the element's FIRST content: the text that follows continues its paragraph)
            var mbk = pb.styleStack.Peek();
            if (pb.blocks.Count == mbk.BlocksAtOpen) mbk.LeadingBreakSpacer = true;
            pb.blocks.Add(new Block
            {
                Text = "", IsHardBreak = true, IsLineBreak = true, ExplicitHeight = 13.5,
                LineBoxPt = mbk.LineBoxPt,
                FontSize = mbk.FontSize,
            });
            return false;
        }
        return true;
    }
}
