using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The entry stage of a block render: breaks, spacing and margin collapse, verbatim; a return that ended the block became return false.</summary>
    private static bool RenderBlockSpacing(ConvertState cv, RenderBlockState rb, HtmlLoadOptions? options, List<byte[]> inlineSvgs, Block block)
    {
        if (ApplyHeightFloors(cv, rb, block) is { } applyHeightFloorsResult) return applyHeightFloorsResult;
        if (TrackBlockSequence(cv, rb, inlineSvgs, block) is { } trackBlockSequenceResult) return trackBlockSequenceResult;
        if (OpenAndCloseFloatScopes(cv, rb, block) is { } openAndCloseFloatScopesResult) return openAndCloseFloatScopesResult;
        if (ApplyRowAndWikiSpacing(cv, rb, block) is { } applyRowAndWikiSpacingResult) return applyRowAndWikiSpacingResult;
        if (ResolveBlockLineMetrics(cv, rb, block) is { } resolveBlockLineMetricsResult) return resolveBlockLineMetricsResult;
        if (BreakPageBeforeBlock(cv, rb, block) is { } breakPageBeforeBlockResult) return breakPageBeforeBlockResult;
        if (LayoutFormAndTableBlocks(cv, rb, options, inlineSvgs, block) is { } layoutFormAndTableBlocksResult) return layoutFormAndTableBlocksResult;
        return true;
    }
}
