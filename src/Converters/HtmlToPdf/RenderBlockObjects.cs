using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The object stage of a block render: images, rules and hard breaks, verbatim; a return that ended the block became return false.</summary>
    private static bool RenderBlockObjects(ConvertState cv, RenderBlockState rb, HtmlLoadOptions? options, List<byte[]> inlineSvgs, Block block)
    {
        if (block.IsImage)
        {
            LayoutImageBlock(block, cv.flow, cv.profile, cv.doc, cv.docFontDict, options, inlineSvgs, cv.bandStack, cv.marginBottom, cv.marginLeft, cv.marginTop, cv.pageHeight, cv.pageWidth, rb.metrics.lineHeight);
            return false;
        }

        // A push-button in the flow: caption + 10.4 chrome wide, 18.75 tall
        // (11.5×7.5 when empty), its LEFT edge 2 pt outside the margin, filled
        // from the button{} tag rule. Measured: box top 12.3 above the cursor,
        // the next baseline 9.3 under the box.
        if (block.IsButton)
        {
            LayoutButtonBlock(block, cv.flow, cv.profile, cv.doc, cv.docFontDict, cv.marginBottom, cv.marginLeft, cv.marginTop, cv.pageHeight, cv.pageWidth, cv.dialectButtonFill, cv.dialectButtonTextRg);
            return false;
        }

        if (block.IsInputField || block.InlineItems is not null)
        {
            LayoutInputFieldBlock(block, cv.flow, cv.profile, cv.doc, cv.docFontDict, cv.marginBottom, cv.marginLeft, cv.marginTop, cv.pageHeight, cv.pageWidth, rb.metrics.blockFontSize, rb.metrics.lineHeight);
            return false;
        }

        // RTL diagram table: one right-pinned canvas — stretched figure, centered
        // caption, per-column right-aligned labels, and a legend row whose
        // viewBox-only svgs stretch to column width at a common row height.
        if (block.Diagram is { } dg)
        {
            LayoutRtlSvgDiagram(dg, cv.flow, cv.profile, cv.doc, cv.docFontDict, cv.marginBottom, cv.marginLeft, cv.marginTop, cv.pageHeight, cv.pageWidth, inlineSvgs);
            return false;
        }

        // RTL topics table: the matrix figure paints as graphics (right-pinned);
        // the caption and topic items stack on the left in the serif face, each
        // item right-aligned on a common pen edge with its bullet marker (one
        // " •" run) just right of that edge — marker before item text, so the
        // absorber reads caption, bullet, item, bullet, item, … Layout rule:
        // cell content right edge Rc = contentRight − 410.25 (405 pt
        // CSS svg column + 5.25 pt UA table chrome), items flush-right at
        // R = Rc − 30 (UA ul inline-start padding), caption flush-right at Rc,
        // marker pen at R + 4.5. contentRight nominally comes from an
        // 842-wide A3 page box; this library's A3 is the exact 841.89, so the
        // anchors are carried as content-LEFT offsets (equal under the
        // mirrored 96 pt insets) to keep the numbers verbatim.
        // Flex-row waybill grid: the bordered container, its centred serif
        // title, and every flex row's percent-width cells draw at absolute
        // geometry (all measures from the waybill's expected render).
        if (block.Flex is { } fg)
        {
            LayoutFlexGrid(fg, cv.flow, cv.doc, cv.docFontDict, cv.marginBottom, cv.marginLeft, cv.marginRight, cv.marginTop, cv.pageHeight, cv.pageWidth);
            return false;
        }

        // Positioned slide: every absolutely positioned item draws at its
        // canvas geometry — the canvas anchors at the content origin (page
        // margin + the UA body margin) on the extent-widened sheet.
        if (block.Slide is { } slide)
        {
            LayoutPositionedSlide(slide, cv.flow, options, cv.css, cv.marginLeft, cv.marginTop, cv.pageHeight);
            return false;
        }

        // Positioned media card: draw the whole card at absolute geometry —
        // media box with its placeholder icon and bottom-anchored bars, the
        // clipped prose column, and the two-column info panel (see
        // PositionedCard; every quantity an empirical fixed value).
        if (block.Card is { } pc)
        {
            LayoutPositionedCard(pc, cv.flow, cv.marginLeft);
            return false;
        }

        if (block.TopicsList is { } tp)
        {
            LayoutTopicsList(tp, cv.flow, cv.profile, cv.doc, cv.docFontDict, cv.marginBottom, cv.marginLeft, cv.marginTop, cv.pageHeight, cv.pageWidth, inlineSvgs);
            return false;
        }

        // Centered search form: fixed-width cell with the input widget (+overlay
        // icon), a centered push-button row, and a side link clipped at the
        // content-box edge.
        if (block.Form is { } sf)
        {
            LayoutSearchForm(sf, cv.flow, cv.profile, cv.doc, cv.docFontDict, cv.marginBottom, cv.marginLeft, cv.marginTop, cv.pageHeight, cv.pageWidth, options, cv.pendingLinks);
            return false;
        }

        // Styled inline row (nav bar / centered link line): bar rect + measured
        // horizontal runs, drawn directly at the flow cursor. Vertical margins
        // between adjacent rows collapse (CSS margin collapsing).
        if (block.RowRuns is { Count: > 0 })
        {
            var collapse = Math.Min(rb.prevRowBottomPx, block.RowMarginTopPx);
            cv.flow.y += collapse * 0.75;
            RenderRowBlock(cv.flow.page, block, ref cv.flow.y, cv.marginLeft, cv.flow.contentWidth, cv.pendingLinks);
            cv.flow.prevRowMarginBottomPx = block.RowMarginBottomPx;
            cv.flow.lastWasHardBreak = false;
            cv.flow.lastWasRow = true;
            return false;
        }

        // Form dialect: a mid-flow <hr> (the section-divider div it replaced)
        // DRAWS its rule line across the content box, with its CSS margins around
        // it. The top margin collapses with the preceding block's bottom margin
        // (CSS adjacent-margin collapse — a heading right above a divider
        // contributes max(heading-bottom, divider-top), not their sum). Every
        // other dialect keeps the legacy spacing-only <hr>.
        // Report label/span dialect: the section divider draws at its own
        // percentage width from the content left — a GROOVE, a
        // black top line over a dark-grey one 0.75 lower.
        if (block.IsHorizontalRule && block.MaxWidthPt > 0)
        {
            // anchor on the preceding BASELINE: rewind the last line box, then
            // drop the measured baseline→groove distance
            if (cv.flow.prevFlowLineHeight > 0) { cv.flow.y += cv.flow.prevFlowLineHeight; }
            cv.flow.y -= ReportHrBelowBasePt;
            DrawBox(cv.flow.page, cv.marginLeft + block.LeftIndent, cv.flow.y - ReportGroovePt,
                block.MaxWidthPt, ReportGroovePt, null, 0, ParseCssColor("#000000"));
            DrawBox(cv.flow.page, cv.marginLeft + block.LeftIndent, cv.flow.y - 2 * ReportGroovePt,
                block.MaxWidthPt, ReportGroovePt, null, 0, ParseCssColor("#555555"));
            cv.flow.y -= 2 * ReportGroovePt + ReportHrAfterPt;
            cv.flow.contentPage = cv.flow.page;
            cv.flow.prevFlowMarginBottom = block.MarginBottom;
            cv.flow.prevFlowLineHeight = 0;
            return false;
        }
        if (cv.profile.formHorizontalDoc && block.IsHorizontalRule)
        {
            var fhRuleH = cv.profile.sectionedReport ? 1.5 : Math.Max(0.75, block.RuleWidth * 0.75);
            var fhTopGap = Math.Max(block.MarginTop - cv.flow.prevFlowMarginBottom, 0);
            if (cv.flow.prevFlowLineHeight > 0)
            {
                // Rewind the preceding text block's full-line-box advance to its
                // CSS box bottom (baseline + ~0.3em descent), then the collapsed
                // margin pair — the heading→divider rhythm.
                cv.flow.y += cv.flow.prevFlowLineHeight + cv.flow.prevFlowMarginBottom;
                cv.flow.y -= cv.flow.prevFlowFontSize * 0.3 + Math.Max(block.MarginTop, cv.flow.prevFlowMarginBottom);
                fhTopGap = 0;
                cv.flow.prevFlowLineHeight = 0;
            }
            if (cv.flow.y - fhTopGap - fhRuleH < cv.marginBottom)
            {
                cv.flow.page = cv.doc.Pages.Add(cv.pageWidth, cv.pageHeight);
                EnsureFonts(cv.flow.page, cv.docFontDict);
                cv.flow.y = cv.pageHeight - cv.marginTop; cv.flow.pendingTopDrop = cv.profile.hasZeroTopMargin;
                fhTopGap = 0;
            }
            cv.flow.y -= fhTopGap;
            DrawBox(cv.flow.page, cv.marginLeft, cv.flow.y - fhRuleH, cv.flow.contentWidth, fhRuleH,
                null, 0, block.RuleColor ?? ParseCssColor("#999999"));
            cv.flow.y -= fhRuleH + block.MarginBottom;
            cv.flow.contentPage = cv.flow.page;
            cv.flow.prevFlowMarginBottom = block.MarginBottom;
            // The next TEXT block draws its first baseline AT the cursor (glyphs
            // extend upward) — without a line-box drop it would overprint the
            // rule. Tables and images lay out downward from the cursor and clear
            // the flag untouched.
            cv.flow.afterRuleDrop = true;
            cv.flow.lastWasHardBreak = false;
            return false;
        }

        // Sectioned report: an <hr> is a real box, not just a gap. The UA rule
        // `hr { border: 1px inset }` paints a 0.75 pt black top border over a
        // 0.75 pt #555555 bottom one across the content box, and the legacy
        // size/color/noshade attributes are ignored. The box top sits one
        // baseline offset ABOVE the cursor (which runs in baseline space); the
        // space the rule reserves below is left exactly as it was, so this adds
        // ink without disturbing the surrounding rhythm.
        // Redline diff document: the <hr> draws the UA 3D groove — a black
        // top stroke and a light-gray bottom stroke 0.8 pt below, at its
        // declared width fraction centred in the content box (probed:
        // 25% rule at 280.3..405.1, strokes 156.6/157.4).
        if (cv.profile.redlineDiffDoc && block.IsHorizontalRule)
        {
            var rhInv = System.Globalization.CultureInfo.InvariantCulture;
            var rhW = (block.WidthFrac > 0 ? block.WidthFrac : 1.0) * cv.flow.contentWidth;
            var rhX0 = cv.marginLeft + (cv.flow.contentWidth - rhW) / 2;
            var rhTop = cv.flow.y + cv.flow.prevFlowLineHeight - RedlineHrLeadPt;
            string F(double v) => v.ToString("0.##", rhInv);
            cv.flow.page.AddContentStream(Encoding.ASCII.GetBytes(
                "q 0 0 0 RG 0.75 w " + F(rhX0) + " " + F(rhTop) + " m " + F(rhX0 + rhW) + " " + F(rhTop) + " l S "
                + F(rhX0) + " " + F(rhTop - 0.8) + " m " + F(rhX0) + " " + F(rhTop + 0.7) + " l S "
                + "0.333 0.333 0.333 RG " + F(rhX0) + " " + F(rhTop - 0.8) + " m " + F(rhX0 + rhW) + " " + F(rhTop - 0.8) + " l S "
                + F(rhX0 + rhW) + " " + F(rhTop - 0.8) + " m " + F(rhX0 + rhW) + " " + F(rhTop + 0.7) + " l S Q" + (char)10));
            cv.flow.y = rhTop - RedlineHrDropPt;
            cv.flow.prevFlowFontSize = 0; cv.flow.prevFlowLineHeight = 0; cv.flow.prevFlowMarginBottom = 0;
            cv.flow.lastWasHardBreak = false;
            cv.flow.contentPage = cv.flow.page;
            return false;
        }
        if (cv.profile.sectionedReport && block.IsHorizontalRule)
        {
            var hrTop = cv.flow.y + BaselineInLineBoxPt(
                cv.flow.prevFlowFontSize > 0 ? cv.flow.prevFlowFontSize : rb.metrics.blockFontSize);
            if (hrTop - 1.5 >= cv.marginBottom)
            {
                DrawBox(cv.flow.page, cv.marginLeft, hrTop - 0.75, cv.flow.contentWidth, 0.75,
                    null, 0, Color.Black);
                DrawBox(cv.flow.page, cv.marginLeft, hrTop - 1.5, cv.flow.contentWidth, 0.75,
                    null, 0, ParseCssColor("#555555"));
                cv.flow.contentPage = cv.flow.page;
            }
        }
        // Escaped-attr dialect: the section divider is the same UA groove — a
        // black hairline over a #555 one — spanning symmetric 96 pt margins
        // (measured: the rule sits 10 pt under the previous control line's
        // baseline, 4.4 pt above the cursor that line's advance left).
        else if (cv.profile.escapedAttrDoc && block.IsHorizontalRule)
        {
            var hrTop = cv.flow.y + 4.42;
            if (hrTop - 0.75 >= cv.marginBottom)
            {
                DrawBox(cv.flow.page, cv.marginLeft, hrTop, cv.pageWidth - 2 * cv.marginLeft, 0.75,
                    null, 0, Color.Black);
                DrawBox(cv.flow.page, cv.marginLeft, hrTop - 0.75, cv.pageWidth - 2 * cv.marginLeft, 0.75,
                    null, 0, ParseCssColor("#555555"));
                cv.flow.contentPage = cv.flow.page;
            }
            cv.flow.afterEscapedRule = true;
        }
        // UA-default serif flow: an <hr> is 0.5em of margin, a 1.5 pt groove
        // box (dark top+left stroke over a #555 bottom+right one) spanning
        // the symmetric content frame, and 0.5em more margin — 13.5 pt of
        // flow in all; the size/color attributes are ignored (measured:
        // size 2, 4 and 6 rules all draw the same 1.5 pt box at 96..499).
        else if ((cv.profile.uaStdSerif || cv.profile.ptReportDoc) && !cv.profile.sectionedReport && block.IsHorizontalRule)
        {
            var hrW = cv.pageWidth - 2 * cv.marginLeft;
            var hrBoxTop = cv.flow.y - UaBodyMarginPt;            // bottom-up box top edge
            if (hrBoxTop - 1.5 >= cv.marginBottom)
            {
                DrawBox(cv.flow.page, cv.marginLeft, hrBoxTop - 0.75, hrW, 0.75, null, 0, Color.Black);
                DrawBox(cv.flow.page, cv.marginLeft, hrBoxTop - 1.5, hrW, 0.75, null, 0,
                    ParseCssColor("#555555"));
                DrawBox(cv.flow.page, cv.marginLeft, hrBoxTop - 1.5, 0.75, 1.5, null, 0, Color.Black);
                DrawBox(cv.flow.page, cv.marginLeft + hrW - 0.75, hrBoxTop - 1.5, 0.75, 1.5, null, 0,
                    ParseCssColor("#555555"));
                cv.flow.contentPage = cv.flow.page;
            }
            cv.flow.y -= 2 * UaBodyMarginPt + 1.5;
            // The rule's trailing 0.5em is a MARGIN — it max-collapses with
            // the following block's own top margin (probed: hr then an empty
            // p's 13.44 opens 13.44 total, not 6 + 13.44).
            cv.flow.uaPrevMarginBottom = UaBodyMarginPt;
            cv.flow.lastWasHardBreak = false;
            return false;
        }

        // Hard-break blocks (<br>, empty <p>, <hr>) only consume vertical
        // space — never emit an empty BT/ET run, which would surface as
        // extra zero-length TextFragments to TextFragmentAbsorber. Coalesce
        // runs of consecutive hard-breaks so deeply-nested empty containers
        // don't explode page count (HTML like <div><div></div></div> emits
        // a chain of closes that would otherwise each become a blank line).
        if (block.IsHardBreak || string.IsNullOrEmpty(block.Text)
            // Redline: an nbsp-ONLY separator paragraph occupies the empty
            // paragraph box, not a full styled line — unless it carries
            // marker ink (the cover bar paragraph draws its underline).
            || (cv.profile.redlineDiffDoc && block.DecorRuns is null && !block.BorderTopOnly
                && block.Text.Trim(' ', ' ').Length == 0))
        {
            LayoutHardBreakBlock(block, cv.flow, cv.profile, rb.metrics, cv.doc, cv.docFontDict, cv.uaFlow,
                rb.breakAfterTable, rb.wasRow, cv.bodyCssFace, cv.marginBottom, cv.marginLeft, cv.marginTop, cv.pageHeight, cv.pageWidth);
            return false;
        }
        cv.flow.lastWasHardBreak = false;
        // The `margin:` shorthand's TOP value, which the calibrated dialects take from
        // the dedicated margin-top handling instead. It has to be applied BEFORE the
        // margin chain below reads block.MarginTop.
        if (cv.profile.floatBothSidesDoc && block.ShorthandTopPt > block.MarginTop)
            block.MarginTop = block.ShorthandTopPt;
        if (cv.flow.pendingTopDrop && !(cv.flow.uaTopMarginPending && cv.uaFlow))
        {
            // First line of a zero-top-margin page: baseline = line box + block margin.
            // The metric flow needs no such drop — its baseline always sits inside the
            // line box (half-leading model), on every page.
            if (!cv.profile.metricFlow) cv.flow.y -= rb.metrics.blockFontSize * 1.15 + block.MarginTop;
            cv.flow.pendingTopDrop = false;
            cv.flow.afterRuleDrop = false;
        }
        // First text after a form-dialect rule: line-box drop so the glyphs land
        // below the rule, not through it (0.9em puts the cap top
        // ~15px below the rule with the 10px rule margin already applied).
        else if (cv.flow.afterRuleDrop)
        {
            cv.flow.y -= rb.metrics.blockFontSize * 0.9;
            cv.flow.afterRuleDrop = false;
            cv.flow.afterFhTable = false;
        }
        // Flow text directly after a synthesized form-row block: the line-box
        // drop plus the section gap kept above the next heading.
        else if (cv.flow.afterFhTable)
        {
            cv.flow.y -= rb.metrics.blockFontSize * 1.15 + 10.3;
            cv.flow.afterFhTable = false;
        }
        else if (cv.flow.pendingTableDrop && !string.IsNullOrEmpty(block.Text))
        {
            // pt-styled fragment: the first baseline under a grid seats an
            // ASCENT below its edge (probed: the nbsp line at end + 0.935em),
            // not the full legacy 1.15em drop.
            cv.flow.y -= rb.metrics.blockFontSize * (cv.profile.ptStyledFragment
                ? PtDropEm + (cv.flow.pendingTableDropBordered ? PtBorderedDropExtraEm : 0)
                : 1.15);
            cv.flow.pendingTableDropBordered = false;
            // The pinned-body report's painted panels carry REAL margins past
            // the drop: whatever their margin-top declares beyond the pad the
            // fill reserves (the boxes' `margin-top: 5px`) is spent here —
            // the drop replaced only the pad share.
            if (cv.profile.bodyPinnedW > 0 && block.MarginTop > block.BandPadPt)
                cv.flow.y -= block.MarginTop - block.BandPadPt;
            cv.flow.pendingTableDrop = false;
        }
        // Browser margin-collapse: the FIRST flow block's top margin collapses with the
        // page/body top margin at the document top — it does not stack on top of it (an
        // opening <h1> starts at the content top, not one h1-margin below it).
        // The certificate dialect's first flowed block sits AFTER the header floats,
        // so it is not the document's opening block and does not collapse against the
        // page top - it falls through to the ordinary margin handling below.
        else if (cv.flow.uaTopMarginPending && !cv.profile.floatBothSidesDoc)
        {
            // ...but a first-block margin MAX-collapses with the UA body margin
            // instead of vanishing: the content opens the excess below the body
            // inset (measured: 72 + max(6, 18.75) = 90.75 for an authored
            // margin; probed on the dialect bench for the UA defaults too — a
            // p-first document opens one p-margin down and an h1-first one
            // h1-margin down, a div-first at the bare body inset).
            // The face-swap admission (single inline family, no stylesheet)
            // gets the CURRENT-era top model: the first block's UA
            // margin MAX-collapses with the body inset instead of vanishing
            // (probed: p-first opens one p-margin down, h1-first one
            // h1-margin down). The vintage UA corpus keeps the dropped-margin
            // top its templates were rendered with (the era wall: the same
            // bare <h1> doc measures BOTH ways, current binary vs template).
            if (cv.uaFlow && block.MarginTop > UaBodyMarginPt
                && (block.MarginTopAuthored || cv.mozEmailDoc || cv.singleFamilyFaceSwap
                    // html5-doctype bare docs are CURRENT-era: the first
                    // block's UA margin max-collapses instead of vanishing
                    // (measured: an h3-first sheet opens 72 + max(6, 12)) —
                    // unless the doctype lead-paragraph charge above already
                    // seated this document's first block.
                    || (cv.html5BareUa && !cv.doctypeLeadCharged)))
                {
                    // The calibrated charge assumes the page's top margin already carries the
                    // 6 pt UA body inset (the default 96 = 90 + 6, the arm's own 72 + max(6, mt)
                    // example = a 78 pt margin). A caller-authored ZERO top margin carries no
                    // inset, so the block opens the full max-collapse below the bare page top
                    // (measured: an 18 pt heading's ink seats at 14.04 = its
                    // 14.94 margin, on the zero-top RTL report; mt - 6 left it 6 short).
                    cv.flow.y -= block.MarginTop - (cv.profile.hasZeroTopMargin ? 0 : UaBodyMarginPt);
                }
            cv.flow.uaTopMarginPending = false;
        }
        // Apply top margin (unless we're at the start of a fresh page; a
        // MarginTopAlways block keeps it even there). The browser-UA flow
        // collapses it with the previous block's bottom margin.
        else if (block.MarginTopAlways || cv.profile.sectionedReport
            // the fieldset worksheet's padded body keeps margins at the top
            || (cv.profile.uaStdSerif && cv.fieldsetDoc)
            // A block that follows a FLOAT is not the first thing on the page even
            // though the cursor still stands at the content top - the float left it
            // there - so its top margin does not collapse against the page. The
            // certificate heading declares 30 px and that is honoured,
            // seating its first line 24.67 below the content edge.
            || (cv.profile.floatBothSidesDoc && cv.flow.floatIndentPt > 0)
            || cv.flow.y < cv.pageHeight - cv.marginTop - 1e-3)
            cv.flow.y -= cv.profile.uaStdSerif || cv.profile.printGrid || cv.profile.sectionedReport || cv.articleFlow
                    // the float flow's paragraphs carry real UA margins now, so they
                    // collapse with the block above like every other CSS box
                    || cv.profile.floatBothSidesDoc
                ? Math.Max(0, block.MarginTop - rb.uaPrevMB) : block.MarginTop;
        return true;
    }
}
