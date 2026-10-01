using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The block's declared background fill paints behind the line, over the box the sheet gives it.</summary>
    private static void PaintLineBackgroundBox(BlockTextState bt)
    {
        if (bt.block.BackgroundColor is { } bgc)
        {
            var bgSb = new StringBuilder();
            bgSb.Append("q ");
            bgSb.Append($"{(bgc.R / 255.0).ToString("F5", bt.invc)} {(bgc.G / 255.0).ToString("F5", bt.invc)} {(bgc.B / 255.0).ToString("F5", bt.invc)} rg ");
            // A painted box (tiny background tile × declared CSS size) fills its
            // whole declared rect once, on the block's first line. The element is
            // a body-level container, so its box origin sits one UA body margin
            // inside the content origin on both axes; the fill spans the declared
            // width × height no matter how the text inside wraps. (The Min clamps
            // the first-line-box top back to the content top at a page start,
            // where the flow's entry drop has already been spent.)
            if (bt.block.BgBoxHeightPt > 0 || bt.block.BgBoxHeightVh > 0)
            {
                if (bt.metrics.firstLineOfBlock)
                {
                    var (bbX, bbTop, pbW, pbH, pbBw) = PaintedBoxRect(bt);
                    bgSb.Append($"{bbX.ToString("F2", bt.invc)} {(bbTop - pbH).ToString("F2", bt.invc)} {pbW.ToString("F2", bt.invc)} {pbH.ToString("F2", bt.invc)} re f ");
                    if (pbBw > 0 && bt.block.BorderColor is { } pbc)
                    {
                        bgSb.Append($"{(pbc.R / 255.0).ToString("F5", bt.invc)} {(pbc.G / 255.0).ToString("F5", bt.invc)} {(pbc.B / 255.0).ToString("F5", bt.invc)} RG {pbBw.ToString("F2", bt.invc)} w ");
                        bgSb.Append($"{(bbX + pbBw / 2).ToString("F2", bt.invc)} {(bbTop - pbH + pbBw / 2).ToString("F2", bt.invc)} {(pbW - pbBw).ToString("F2", bt.invc)} {(pbH - pbBw).ToString("F2", bt.invc)} re S ");
                    }
                    bgSb.Append('Q');
                    bt.flow.page.AddContentStream(Encoding.ASCII.GetBytes(bgSb.ToString()));
                }
            }
            // A floated box's background fills its shrink-to-fit box: exactly
            // the measured text advance wide, one line box tall, hanging from
            // the line-box top (metric y).
            else if (bt.uaFloatW > 0)
            {
                bgSb.Append($"{bt.lineXPos.ToString("F2", bt.invc)} {(bt.flow.y - bt.metrics.lineHeight).ToString("F2", bt.invc)} {bt.uaFloatW.ToString("F2", bt.invc)} {bt.metrics.lineHeight.ToString("F2", bt.invc)} re f Q");
                bt.flow.page.AddContentStream(Encoding.ASCII.GetBytes(bgSb.ToString()));
            }
            // Metric flow: y is the LINE BOX TOP (the text draws a drop
            // below it), so the band is the CSS line box exactly — from y
            // down one line height, one UA body margin short of the content
            // right edge (measured: the saved-page title strip fills
            // 96..646.5 x 97.2..110.8 around its 108.0 baseline).
            else if (bt.profile.metricFlow && bt.metrics.metricDrop > 0)
            {
                var bgX = bt.marginLeft + bt.block.LeftIndent;
                var bgW = bt.flow.contentWidth - bt.block.LeftIndent - UaBodyMarginPt;
                var bandUp = bt.metrics.firstLineOfBlock ? bt.block.BandPadPt : 0;
                var bandDn = bt.block.BandPadPt;
                bgSb.Append($"{bgX.ToString("F2", bt.invc)} {(bt.flow.y - bt.metrics.lineHeight - bandDn).ToString("F2", bt.invc)} {bgW.ToString("F2", bt.invc)} {(bt.metrics.lineHeight + bandUp + bandDn).ToString("F2", bt.invc)} re f Q");
                bt.flow.page.AddContentStream(Encoding.ASCII.GetBytes(bgSb.ToString()));
            }
            // The sheet-typography flow: the band is the block's LINE BOX - from the line box top
            // (the baseline less the face's ascent side of the box) down one line height, the
            // content box wide - and the box's border-top / border-bottom rules stroke just
            // outside it, centred on their own widths.
            else if (bt.profile.sheetTypographyDoc && bt.block.SheetBox && bt.metrics.lineHeight > 0)
            {
                var bgX = bt.marginLeft + bt.block.LeftIndent;
                var bgW = bt.flow.contentWidth - bt.block.LeftIndent;
                var face = bt.block.FontFamily is { Length: > 0 } sbFace ? sbFace : "Times New Roman";
                var boxTop = bt.flow.y + LineBoxAbove(face, bt.metrics.blockFontSize, bt.metrics.lineHeight);
                var boxBot = boxTop - bt.metrics.lineHeight;
                bgSb.Append($"{bgX.ToString("F2", bt.invc)} {boxBot.ToString("F2", bt.invc)} {bgW.ToString("F2", bt.invc)} {(boxTop - boxBot).ToString("F2", bt.invc)} re f ");
                if (bt.metrics.firstLineOfBlock && bt.block.BorderTopOnly && bt.block.BorderWidth > 0 && bt.block.BorderColor is { } tc)
                    bgSb.Append($"{(tc.R / 255.0).ToString("F3", bt.invc)} {(tc.G / 255.0).ToString("F3", bt.invc)} {(tc.B / 255.0).ToString("F3", bt.invc)} RG {bt.block.BorderWidth.ToString("F2", bt.invc)} w {bgX.ToString("F2", bt.invc)} {(boxTop + bt.block.BorderWidth / 2).ToString("F2", bt.invc)} m {(bgX + bgW).ToString("F2", bt.invc)} {(boxTop + bt.block.BorderWidth / 2).ToString("F2", bt.invc)} l S ");
                if (bt.block.BorderBottomWidth > 0 && bt.block.BorderBottomColor is { } bc)
                    bgSb.Append($"{(bc.R / 255.0).ToString("F3", bt.invc)} {(bc.G / 255.0).ToString("F3", bt.invc)} {(bc.B / 255.0).ToString("F3", bt.invc)} RG {bt.block.BorderBottomWidth.ToString("F2", bt.invc)} w {bgX.ToString("F2", bt.invc)} {(boxBot - bt.block.BorderBottomWidth / 2).ToString("F2", bt.invc)} m {(bgX + bgW).ToString("F2", bt.invc)} {(boxBot - bt.block.BorderBottomWidth / 2).ToString("F2", bt.invc)} l S ");
                bgSb.Append('Q');
                bt.flow.page.AddContentStream(Encoding.ASCII.GetBytes(bgSb.ToString()));
            }
            else
            {
                var bgX = bt.marginLeft + bt.block.LeftIndent;
                var bgW = bt.flow.contentWidth - bt.block.LeftIndent;
                // A band block's fill extends by the div paddings the flow
                // reserved around the line: up only on the first line (the
                // interior lines' fills already touch), down on every line
                // (interior overlaps merge invisibly, the last line closes
                // the band's bottom pad).
                var bandUp = bt.metrics.firstLineOfBlock ? bt.block.BandPadPt : 0;
                var bandDn = bt.block.BandPadPt;
                bgSb.Append($"{bgX.ToString("F2", bt.invc)} {(bt.flow.y - bt.metrics.blockFontSize * 0.25 - bandDn).ToString("F2", bt.invc)} {bgW.ToString("F2", bt.invc)} {(bt.metrics.blockFontSize * 1.15 + bandUp + bandDn).ToString("F2", bt.invc)} re f Q");
                bt.flow.page.AddContentStream(Encoding.ASCII.GetBytes(bgSb.ToString()));
            }
        }
    }
}
