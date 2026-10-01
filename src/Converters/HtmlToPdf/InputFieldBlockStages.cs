using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The stages of the input-field block layout: an inline label-and-control run.</summary>
    private static void LayoutInlineFieldRun(InputFieldBlockState ib, List<Block> runItems)
    {
        // Directly under a section rule the run drops extra so its
        // control boxes clear the rule (baseline rule+17.2, not +11.9).
        if (ib.flow.afterEscapedRule) { ib.flow.y -= RuleToRunExtraPt; ib.flow.afterEscapedRule = false; }
        ib.lineLeft = ib.marginLeft;
        ib.lineRight = ib.marginLeft + ib.flow.contentWidth;
        ib.runLines = new List<(List<(Block? Ctl, string? Txt, double X, double FontPt, string Res)> Items, bool HasText, double MaxAdv, double MaxAbove)>();
        ib.curItems = new List<(Block? Ctl, string? Txt, double X, double FontPt, string Res)>();
        ib.pen = ib.lineLeft;
        ib.curHasText = false;
        ib.curMaxAdv = 0;
        ib.curMaxAbove = 0;
        foreach (var it in runItems)
        {
            if (!PlaceInlineFieldItem(ib, it)) break;
        }
        EndRunLine(ib);
        ib.runTotalAdv = 0;
        for (var rl = 0; rl < ib.runLines.Count; rl++)
        {
            var (_, rlHasText, rlMaxAdv, rlMaxAbove) = ib.runLines[rl];
            var rlAdv = rlMaxAdv > 0
                ? rlMaxAdv + (rlHasText ? InlineMixedExtraPt : 0)
                : NormalLineHeightPt(ib.blockFontSize > 0 ? ib.blockFontSize : EscapedBodyFontPt);
            ib.runTotalAdv += System.Math.Max(0, rlMaxAbove - InputBoxAboveBaselinePt)
                + (rl == ib.runLines.Count - 1 && rlMaxAbove > InputBoxAboveBaselinePt
                    ? SerifDescentRoomPt : rlAdv);
        }
        if (ib.flow.y - ib.runTotalAdv < ib.marginBottom
            && ib.runTotalAdv <= FreshPageTopY(ib.profile, ib.pageHeight, ib.marginTop) - ib.marginBottom
            && ib.flow.y < FreshPageTopY(ib.profile, ib.pageHeight, ib.marginTop) - 1e-3)
        {
            ib.flow.page = ib.doc.Pages.Add(ib.pageWidth, ib.pageHeight);
            EnsureFonts(ib.flow.page, ib.docFontDict);
            ib.flow.y = FreshPageTopY(ib.profile, ib.pageHeight, ib.marginTop); ib.flow.pendingTopDrop = ib.profile.hasZeroTopMargin;
        }
        foreach (var (items, hasText, maxAdv, maxAbove) in ib.runLines)
        {
            if (!EmitInlineFieldLine(ib, items, hasText, maxAdv, maxAbove)) break;
        }
        ib.flow.lastWasHardBreak = false;
        ib.flow.prevFlowMarginBottom = 0;
        ib.flow.prevFlowLineHeight = 0;
    }
}
