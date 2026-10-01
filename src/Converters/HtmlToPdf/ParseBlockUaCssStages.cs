using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The browser-UA sheet's rules for the opening tag land on the block style: its own element reset, the calibrated flow's margins and the sheet box's typography.</summary>
    private static void ApplyBrowserUaTagCss(ParseBlocksState pb, Token tok, string tag)
    {
        if ((pb.browserUa || pb.sheetElementTypography) && pb.css is not null)
        {
            var bmRule = SheetRuleFor(pb, tok, tag);
            // The rule's margins ride the block on the UA flow and on the sheet-typography
            // flow (whose blocks step by CSS boxes); the other calibrated flows keep their
            // measured heading and paragraph margins and take only the rule's typography.
            var bmHasMargin = bmRule is not null && (bmRule.ContainsKey("margin")
                || bmRule.ContainsKey("margin-top") || bmRule.ContainsKey("margin-bottom"));
            // The calibrated flow's own margins, where it set any, already read this dialect's
            // sheet (a container's padding folded onto its first heading, the tag rule a
            // pseudo-class chain would override); on the sheet-typography flow the rule's or
            // the UA's margin fills in only the sides the calibration left at zero.
            var (calibratedMt, calibratedMb) = (pb.style.MarginTop, pb.style.MarginBottom);
            // (the rule's typography FIRST: an em margin is of the element's own resolved size - the
            //  change-control page's `h1 { font-size: 15pt; margin: 0.75em 0 }` stands 11.25, not
            //  0.75 x the UA 2 em heading)
            if (bmRule is not null) ApplySheetRuleTypography(pb, bmRule);
            // (the field-list dialect's rows carry their rules' paddings as box space: the group's
            //  `padding-top: 1em` above its first block, the category's `padding-bottom` under its last)
            if (pb.cv?.profile.fieldListDoc == true && bmRule is not null && pb.style.FontSize > 0)
            {
                if (bmRule.TryGetValue("padding-top", out var fpT) && StatedMarginPt(fpT, pb.style.FontSize) is > 0 and var fpTop)
                    pb.style.OwnPadTopPt += fpTop;
                if (bmRule.TryGetValue("padding-bottom", out var fpB) && StatedMarginPt(fpB, pb.style.FontSize) is > 0 and var fpBot)
                    pb.style.PadBottomPt += fpBot;
            }
            if ((pb.browserUa || pb.sheetElementTypography) && bmHasMargin && bmRule is not null)
            {
                var bmSb = new StringBuilder();
                foreach (var kv in bmRule)
                    bmSb.Append(kv.Key).Append(':').Append(kv.Value).Append(';');
                var bmDecl = bmSb.ToString();
                var bmBox = ParseInlineMarginBox(bmDecl, pb.style.FontSize);
                if (bmRule.ContainsKey("margin") || bmRule.ContainsKey("margin-top"))
                    pb.style.MarginTop = bmBox.top;
                if (bmRule.ContainsKey("margin") || bmRule.ContainsKey("margin-bottom"))
                    pb.style.MarginBottom = bmBox.bottom;
                // …and a class rule's margin-left indents the block.
                if (bmBox.left > 0) pb.style.LeftIndent += bmBox.left;
            }
            // (a quirks UA heading the sheet re-sizes carries the UA em margins of its NEW size, the
            //  CSS 2.1 sample sheet's ems - MEASURED, the evaluation form: `H1 { font-size: 1.3em }`
            //  stands 0.67 x 14.3 = 9.58 under its rule, `H2 { 1.15em }` 0.75 x 12.65 = 9.49 over its grid)
            if (pb.browserUa && _quirksRowStrut && !bmHasMargin && bmRule is not null && bmRule.ContainsKey("font-size")
                && tag.Length == 2 && (tag[0] is 'h' or 'H') && tag[1] is >= '1' and <= '6' && pb.style.FontSize > 0)
                pb.style.MarginTop = pb.style.MarginBottom = UaBlockMarginEmOf(tag) * pb.style.FontSize;
            // …and a UA-grid block's own inline text-align aligns it (`<p style="text-align: center">`).
            if (pb.uaGridBlocks && tok.Attributes is not null && tok.Attributes.TryGetValue("style", out var uaAlignSt) && uaAlignSt is not null)
            {
                if (Regex.IsMatch(uaAlignSt, "text-align *: *center", RegexOptions.IgnoreCase)) pb.style.AlignCenterAttr = true;
                else if (Regex.IsMatch(uaAlignSt, "text-align *: *right", RegexOptions.IgnoreCase)) pb.style.AlignRight = true;
            }
            // …and where the sheet says nothing about the box, the UA's own em margins of
            // the block's (sheet-sized) font (probed: a sheet-less p steps one em).
            if (pb.sheetElementTypography && !bmHasMargin && IsTextElementTag(tag))
                pb.style.MarginTop = pb.style.MarginBottom = (pb.uaGridBlocks ? UaGridElementMarginEm(tag) : UaElementMarginEm(tag)) * pb.style.FontSize;
            // A sheet that paces its lines by a BODY line box steps its blocks by CSS boxes too:
            // the side the sheet declares rides the block and the side it leaves out is the UA
            // em margin, pairwise collapsed by the flow (measured on the 21 px body: an h1 with
            // MARGIN-BOTTOM: 10px seats 72 + its 0.67 em UA margin-top below the page top, and
            // the h3 after it opens max(7.5, 1 em) below its box).
            var sheetBoxMargins = (_quirksChainSheet && pb.style.LineBoxPt > 0 && IsTextElementTag(tag))
                // (a UA-grid document's text blocks step by the browser's margins: the sheet's where declared,
                //  the UA em where not - measured on the quotation's h3 `margin-top: 0` and its 0.83em below)
                || (pb.uaGridBlocks && IsTextElementTag(tag));
            if (sheetBoxMargins && bmHasMargin && bmRule is not null)
            {
                if (!bmRule.ContainsKey("margin") && !bmRule.ContainsKey("margin-top"))
                    pb.style.MarginTop = (pb.uaGridBlocks ? UaGridElementMarginEm(tag) : UaElementMarginEm(tag)) * pb.style.FontSize;
                if (!bmRule.ContainsKey("margin") && !bmRule.ContainsKey("margin-bottom"))
                    pb.style.MarginBottom = (pb.uaGridBlocks ? UaGridElementMarginEm(tag) : UaElementMarginEm(tag)) * pb.style.FontSize;
            }
            if (pb.sheetElementTypography)
            {
                // …and a CONTAINER's sheet margin is the calibrated flow's to fold at its close.
                // (a block the sheet BOXES - background, rules - steps by the sheet's own margins
                // and its rules' widths; the calibrated heading margins are for bare text, and a
                // body-line-box sheet's blocks keep the CSS margins resolved above)
                if (!pb.style.SheetBox && !sheetBoxMargins)
                {
                    if (calibratedMt > 0 || !IsTextElementTag(tag)) pb.style.MarginTop = calibratedMt;
                    if (calibratedMb > 0 || !IsTextElementTag(tag)) pb.style.MarginBottom = calibratedMb;
                }
            }
            if (pb.uaGridBlocks)
            {
                pb.style.MarginTop += pb.style.UaBoxTopPt;
                pb.style.MarginBottom += pb.style.UaBoxBottomPt;
            }
            if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_SEAT") == "1")
                Console.Error.WriteLine($"[sheetmargin] tag={tag} hasMargin={bmHasMargin} boxMargins={sheetBoxMargins} bodyLine={pb.bodyLineBoxPt:0.##} sheetTypo={pb.sheetElementTypography} calibrated={calibratedMt:0.##}/{calibratedMb:0.##} -> mt={pb.style.MarginTop:0.##} mb={pb.style.MarginBottom:0.##} rule=[{(bmRule is null ? "-" : string.Join(";", bmRule.Keys))}]");
        }
    }
}
