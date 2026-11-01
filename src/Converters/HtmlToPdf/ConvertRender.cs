using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>One block of the conversion render loop, verbatim; the
    /// loop-level continue became a return.</summary>
    private static void RenderBlock(ConvertState cv, HtmlLoadOptions? options, List<byte[]> inlineSvgs, Block block)
    {
        // A control line lays out whole at its first block; its other blocks are done by then.
        if (block.InCtlLine) return;
        if (block.CtlLine is { } line) { RenderControlLine(cv, options, inlineSvgs, line); return; }
        RenderFlowBlock(cv, options, inlineSvgs, block);
    }

    private static void RenderFlowBlock(ConvertState cv, HtmlLoadOptions? options, List<byte[]> inlineSvgs, Block block)
    {
        var rb = new RenderBlockState();
        if (!RenderBlockSpacing(cv, rb, options, inlineSvgs, block)) return;
        if (!RenderBlockObjects(cv, rb, options, inlineSvgs, block)) return;
        RenderBlockText(cv, rb, options, block);
    }
}
