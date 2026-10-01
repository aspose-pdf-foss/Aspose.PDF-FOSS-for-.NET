using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The background span the closing block opened is closed and its box drawn.</summary>
    private static void CloseBackgroundSpanBox(ParseBlocksState pb)
    {
        if (pb.bgSpans.Count > 0 && pb.bgSpans.Peek().depth > pb.styleStack.Count)
        {
            var (bgIdx, bgFill, bgW, bgFrac, bgPadT, bgPadB, bgPadL, _) = pb.bgSpans.Pop();
            // "Its own line" means a line carrying THIS fill, not merely any fill: a child
            // with a background of its own paints that child, and says nothing about whether
            // the container ever painted. Compared by component - two Color instances of the
            // same ink are not the same object.
            var ownLine = false;
            for (var bi = bgIdx; bi < pb.blocks.Count && !ownLine; bi++)
                ownLine = pb.blocks[bi].Text.Length > 0
                    && pb.blocks[bi].BackgroundColor is { } bgSeen
                    && bgSeen.R == bgFill.R && bgSeen.G == bgFill.G && bgSeen.B == bgFill.B;
            if (!ownLine && pb.blocks.Count > bgIdx)
            {
                pb.blocks.Insert(bgIdx, new Block
                {
                    Text = "", IsHardBreak = true, BgSpan = 1,
                    BackgroundColor = bgFill, BgBoxWidthPt = bgW, BgSpanWidthFrac = bgFrac,
                    BgPadTopPt = bgPadT, BgPadBottomPt = bgPadB, BgPadLeftPt = bgPadL,
                });
                pb.blocks.Add(new Block { Text = "", IsHardBreak = true, BgSpan = -1 });
            }
        }
    }

    /// <summary>A banded div's background closes over the lines it wrapped, or over its own padding when it flushed none.</summary>
    private static void CloseDivBandBackground(ParseBlocksState pb, string tag)
    {
        if (pb.divBandBg && tag.Equals("div", StringComparison.OrdinalIgnoreCase)
            && pb.emptyDivDepthMark == pb.styleStack.Count + 1
            && pb.blocks.Count == pb.emptyDivBlocksAt
            && pb.currentText.Length == pb.emptyDivTextAt
            && pb.css is not null && pb.css.TryGetValue("div", out var edR)
            && edR.TryGetValue("padding", out var edP)
            && TryParseLength(edP) is { } edPt && edPt > 0)
        {
            pb.blocks.Add(new Block
            {
                Text = "", IsHardBreak = true,
                FontSize = pb.popped.FontSize,
                ExplicitHeight = 2 * edPt,
            });
            pb.emptyDivDepthMark = -1;
        }
        // A CHILDLESS element that closes empty spends its own
        // padding-top longhand as real box space (probed, bench d1). A
        // wrapper whose subtree emitted anything skips this — its pad
        // either went with its direct text under the dialect's own rules
        // or is dropped, which is the expected behaviour for a padded
        // container (a print-grid sheet).
        else if (pb.popped.OwnPadTopPt > 0 && pb.popped.BlocksAtOpen >= 0
            && pb.blocks.Count == pb.popped.BlocksAtOpen)
        {
            pb.blocks.Add(new Block
            {
                Text = "", IsHardBreak = true,
                FontSize = pb.popped.FontSize,
                FontRes = pb.popped.FontRes,
                LeftIndent = pb.popped.LeftIndent,
                ExplicitHeight = pb.popped.OwnPadTopPt,
            });
        }
    }
}
