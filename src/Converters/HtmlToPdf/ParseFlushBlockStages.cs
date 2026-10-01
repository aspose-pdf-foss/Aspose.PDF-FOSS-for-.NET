using System.Text;
using System.Text.RegularExpressions;
namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The block the flush emits, built from the style in force and the text the run collapsed to.</summary>
    private static Block NewFlushedBlock(ParseBlocksState pb, BlockStyle styleUsed, string collapsed)
    {
        return new Block
        {
            Text = collapsed,
            FontSize = styleUsed.FontSize,
            LeadFontSize = pb.ptyLeadFs > 0 && pb.ptyLeadFs != styleUsed.FontSize
                ? pb.ptyLeadFs : 0,
            RightInsetPt = styleUsed.RightInsetPt,
            SmallCaps = styleUsed.SmallCaps,
            TextIndentPt = styleUsed.TextIndentPt,
            LetterSpacingPt = styleUsed.LetterSpacingPt,
            FontRes = styleUsed.FontRes,
            FontFamily = styleUsed.FontFamily,
            ForeColor = styleUsed.ForeColor,
            LegacyFontPt = styleUsed.LegacyFontPt,
            LegacyFontSized = styleUsed.LegacyFontSized,
            EmBold = styleUsed.EmBold,
            EmItalic = styleUsed.EmItalic,
            MarginTop = styleUsed.MarginTop,
            AfterOwnLeadingBreak = styleUsed.LeadingBreakSpacer,
            MarginBottom = styleUsed.MarginBottom,
            MarginTopAlways = styleUsed.MarginTopAlways,
            MarginTopAuthored = styleUsed.MarginTopAuthored,
            MarginTopNested = styleUsed.MarginTopNested,
            LeftIndent = styleUsed.LeftIndent,
            IsListItem = styleUsed.IsListItem,
            PageBreakBefore = styleUsed.PageBreakBefore || pb.pendingPageBreak,
            // An element's declared HEIGHT reserves space for the WHOLE
            // element, so it belongs to the line that CLOSES it: a <p> 200px
            // tall holding four <br>-separated lines pads once, after the
            // fourth, where charging the first spread them a box apart.
            ExplicitHeight = pb.closingElement ? styleUsed.ExplicitHeight : 0,
            ShorthandLeftPt = styleUsed.ShorthandLeftPt,
            ShorthandTopPt = styleUsed.ShorthandTopPt,
            MarginRightPt = styleUsed.MarginRightPt,
            FontFamilyStack = styleUsed.FontFamilyStack,
            DeclaredWidthPt = styleUsed.DeclaredWidthPt,
            LineFactor = styleUsed.LineFactor,
            UaLineFactor = styleUsed.UaLineFactor,
            SheetLineFactor = styleUsed.SheetLineFactor,
            DeclaredLineFactor = styleUsed.DeclaredLineFactor,
            BackgroundColor = styleUsed.BackgroundColor,
            BandPadPt = styleUsed.BandPadPt,
            BgPadTopPt = styleUsed.BgPadTopPt,
            BgPadBottomPt = styleUsed.BgPadBottomPt,
            BgPadLeftPt = styleUsed.BgPadLeftPt,
            BgBoxWidthPt = styleUsed.BgBoxWidthPt,
            BgBoxHeightPt = styleUsed.BgBoxHeightPt,
            BgBoxHeightVh = styleUsed.BgBoxHeightVh,
            BgImageSrc = styleUsed.BgImageSrc,
            BgImageSize = styleUsed.BgImageSize,
            BgBoxIndentPt = styleUsed.BgBoxIndentPt,
            BorderColor = styleUsed.BorderColor,
            BorderTopOnly = styleUsed.BorderTopOnly,
            BorderBottomWidth = styleUsed.BorderBottomWidth, BorderBottomColor = styleUsed.BorderBottomColor,
            SheetBox = styleUsed.SheetBox,
            UaBoxTopPt = styleUsed.UaBoxTopPt, UaBoxBottomPt = styleUsed.UaBoxBottomPt, UaPadTopPt = styleUsed.UaPadTopPt, UaRuleTopPt = styleUsed.UaRuleTopPt, UaRuleTopColor = styleUsed.UaRuleTopColor,
            BorderWidth = styleUsed.BorderWidth,
            LineBoxPt = styleUsed.LineBoxPt,
            TextInsetPt = styleUsed.TextInsetPt,
            AlignCenter = styleUsed.AlignCenter,
            // (<center> centres its text under the UA flow like a text-align:center block)
            AlignCenterCss = styleUsed.AlignCenterCss || pb.centerDepth > 0,
            AlignJustify = styleUsed.AlignJustify,
            AlignCenterAttr = styleUsed.AlignCenterAttr || pb.centerDepth > 0,
            WidthFrac = styleUsed.WidthFrac,
            WidthPx = styleUsed.WidthPx,
            PadTop = styleUsed.PadTop,
            AlignRight = styleUsed.AlignRight,
            BandColor = styleUsed.BandColor,
            BandPx = styleUsed.BandPx,
            BandPadPx = styleUsed.BandPadPx,
            FloatLeft = styleUsed.FloatLeft,
            FloatInherited = styleUsed.FloatInherited,
            FloatRight = styleUsed.FloatRight || pb.floatRightFlush,
        };
    }
}
