using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>One block of the conversion render loop, verbatim; the
    /// loop-level continue became a return.</summary>
    private static void RenderBlock(ConvertState cv, HtmlLoadOptions? options, List<byte[]> inlineSvgs, Block block)
    {
        var rb = new RenderBlockState();
        if (!RenderBlockSpacing(cv, rb, options, inlineSvgs, block)) return;
        if (!RenderBlockObjects(cv, rb, options, inlineSvgs, block)) return;
        RenderBlockText(cv, rb, options, inlineSvgs, block);
    }
}
