using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The opening block's style starts as its parent's, tag and all.</summary>
    private static void SeedBlockStyleFromParent(ParseBlocksState pb, string tag)
    {
        pb.style = new BlockStyle
        {
            BlocksAtOpen = pb.blocks.Count,
            FontSize = pb.parent.FontSize,
            FontRes = pb.parent.FontRes,
            FontFamily = pb.parent.FontFamily,
            // The stack travels with the family it resolved, so a child can fall
            // through it too when the named face is not installed.
            FontFamilyStack = pb.parent.FontFamilyStack,
            MarginTop = 0,
            MarginBottom = 0,
            LeftIndent = pb.parent.LeftIndent,
            AbsOriginLeftPt = pb.parent.AbsOriginLeftPt,
            AbsOriginWidthPt = pb.parent.AbsOriginWidthPt,
            AbsOriginTopPt = pb.parent.AbsOriginTopPt,
            AbsTranslateSumPt = pb.parent.AbsTranslateSumPt,
            InAbsoluteChain = pb.parent.InAbsoluteChain,
            RightInsetPt = pb.parent.RightInsetPt,
            BillPadPt = pb.parent.BillPadPt,
            CardShadowColor = pb.parent.CardShadowColor,
            CardChromePt = pb.parent.CardChromePt,
            CardFrameColor = pb.parent.CardFrameColor,
            CardFrameBorderPt = pb.parent.CardFrameBorderPt,
            CardFrameInsetPt = pb.parent.CardFrameInsetPt,
            FormDialect = pb.parent.FormDialect,
            ParentFontSize = pb.parent.FontSize,
            WidthFrac = pb.parent.WidthFrac,
            WidthPx = pb.parent.WidthPx,
            AlignRight = pb.parent.AlignRight,
            // …and so does an inline text-align: center (a centred div centres the
            // heading inside it) - and the align=center ATTRIBUTE the same way (probed: the
            // h1 and h2 inside `<div align=center>` centre at 197.16 / 214.74).
            AlignCenterCss = pb.parent.AlignCenterCss,
            AlignCenterAttr = pb.parent.AlignCenterAttr,
            // A float is inherited by the boxes inside it: the image that
            // actually gets taken out of the flow is usually nested a few
            // wrappers below the element the rule names.
            FloatLeft = pb.parent.FloatLeft,
            FloatRight = pb.parent.FloatRight,
            FloatInherited = pb.parent.FloatLeft || pb.parent.FloatRight,
            ArticleRhythm = pb.parent.ArticleRhythm,
            UaSerif = pb.parent.UaSerif,
            UaBoxes = pb.parent.UaBoxes,
            InPageFragment = pb.parent.InPageFragment,
            // The sheet's line-height inherits (calibrated flow): a factor as a factor of
            // each block's own size, a length as the box it names.
            SheetLineFactor = pb.sheetElementTypography ? pb.parent.SheetLineFactor : 0,
            // …and the UA flow inherits the box the same way: CSS line-height is inherited.
            LineBoxPt = pb.sheetElementTypography || pb.browserUa ? pb.parent.LineBoxPt : 0,
            // (…and a percent line-height's factor, the UA flow's inherited-as-factor reading)
            UaLineFactor = pb.browserUa ? pb.parent.UaLineFactor : 0,
            Tag = tag.ToLowerInvariant(),
            InFieldsBox = pb.parent.FieldsBox,
        };
    }
}
