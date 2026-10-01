using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The object stage of a block render: images, rules and hard breaks, verbatim; a return that ended the block became return false.</summary>
    private static bool RenderBlockObjects(ConvertState cv, RenderBlockState rb, HtmlLoadOptions? options, List<byte[]> inlineSvgs, Block block)
    {
        if (RenderBlockObjectArms(cv, rb, options, inlineSvgs, block) is { } renderBlockObjectArmsResult) return renderBlockObjectArmsResult;
        if (RenderRowRunsAndRule(cv, rb, block) is { } renderRowRunsAndRuleResult) return renderRowRunsAndRuleResult;
        if (RenderFormRules(cv, block) is { } renderFormRulesResult) return renderFormRulesResult;
        if (RenderReportRules(cv, rb, block) is { } renderReportRulesResult) return renderReportRulesResult;
        if (SkipEmptyBlock(cv, rb, block) is { } skipEmptyBlockResult) return skipEmptyBlockResult;
        ApplyBlockTopDrop(cv, rb, options, inlineSvgs, block);
        return true;
    }
}
