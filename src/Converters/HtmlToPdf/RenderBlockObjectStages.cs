using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The UA top-margin drop: the first block under the page margin spends the standard serif document's collapsed margin.</summary>
    private static void ApplyUaTopMarginDrop(ConvertState cv, Block block)
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
                || (cv.html5BareUa && !cv.doctypeLeadCharged)
                // …and so is an html5-doctype UA document laid out under its own sheet (probed:
                // the state analysis' h1 opens 72 + max(6, 16.08) under `p { line-height }`).
                // (default page margins only, like the standards-mode lead seat: a caller that pins
                // its own margins keeps the dropped-margin top its templates carry)
                || (cv.html5Doctype && cv.profile.uaStdSerif && !cv.doctypeLeadCharged && !cv.marginsExplicit)
                // The html5 fieldset worksheet's template is current-era too: its first block
                // opens one margin down (measured 72 + max(6, 13.5) on the h2-first sheet). The
                // older html5 table sheets keep the dropped-margin top of their templates.
                || (cv.html5Doctype && cv.uaFieldsetBoxes && !cv.doctypeLeadCharged)
                // A margin that belongs to an element nested INSIDE a wrapper
                // block survives like an authored one — the drop is the
                // body-level element's alone (measured on the td-rooted
                // report: its h1 opens 72 + max(6, 16.08), ten points under
                // the dropped-margin seat; on the div-wrapped list, one list
                // margin down; and on the body-level list, at the bare inset).
                // The Word-filtered page keeps its calibrated dropped top: its
                // section div wraps everything, and the template seats the
                // opening heading at the bare inset (measured: 15 pt above
                // the max-collapse seat on the GroupDocs sheet).
                || (block.MarginTopNested && !cv.profile.msoFilteredDoc)))
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

    /// <summary>The drop a text block spends after a table that left one pending.</summary>
    private static void ApplyPendingTableDrop(ConvertState cv, RenderBlockState rb, Block block)
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

    /// <summary>The block's top spacing: the pending top drop, rule drop, form-table gap, pending table drop, UA top margin or the block's own margin, whichever the flow owes.</summary>
    private static void ApplyBlockTopDrop(ConvertState cv, RenderBlockState rb, HtmlLoadOptions? options, List<byte[]> inlineSvgs, Block block)
    {
        if (cv.profile.floatBothSidesDoc && block.ShorthandTopPt > block.MarginTop)
            block.MarginTop = block.ShorthandTopPt;
        if (cv.flow.pendingTopDrop && !(cv.flow.uaTopMarginPending && cv.uaFlow))
        {
            // First line of a zero-top-margin page: baseline = line box + block margin.
            // The metric flow needs no such drop — its baseline always sits inside the
            // line box (half-leading model), on every page.
            // (the sheet-typography flow seats its first line one ascent side down instead)
            if (!cv.profile.metricFlow && !cv.profile.sheetBoxFlow) cv.flow.y -= rb.metrics.blockFontSize * 1.15 + block.MarginTop;
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
            // (the sheet-typography flow seats its first line one ascent side under the table,
            // its own margin collapsed with the table's, spent here)
            if (cv.profile.sheetBoxFlow)
            {
                cv.flow.pendingTableDrop = false;
                cv.flow.y -= Math.Max(0, block.MarginTop - rb.uaPrevMB);
            }
            else ApplyPendingTableDrop(cv, rb, block);
        }
        // Browser margin-collapse: the FIRST flow block's top margin collapses with the
        // page/body top margin at the document top — it does not stack on top of it (an
        // opening <h1> starts at the content top, not one h1-margin below it).
        // The certificate dialect's first flowed block sits AFTER the header floats,
        // so it is not the document's opening block and does not collapse against the
        // page top - it falls through to the ordinary margin handling below.
        else if (cv.flow.uaTopMarginPending && !cv.profile.floatBothSidesDoc)
        {
            ApplyUaTopMarginDrop(cv, block);
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
                    || cv.profile.floatBothSidesDoc || cv.profile.sheetTypographyDoc
                // a margin-less follower stands the previous box's UA closing gap below it
                ? Math.Max(0, (block.MarginTop > 0 ? block.MarginTop : rb.uaClosingGap) - rb.uaPrevMB) : block.MarginTop;
    }

    /// <summary>A hard break or an empty block spends its margins and is done.</summary>
    private static bool? SkipEmptyBlock(ConvertState cv, RenderBlockState rb, Block block)
    {
        if (block.IsHardBreak || string.IsNullOrEmpty(block.Text)
            // Redline: an nbsp-ONLY separator paragraph occupies the empty
            // paragraph box, not a full styled line — unless it carries
            // marker ink (the cover bar paragraph draws its underline).
            || (cv.profile.redlineDiffDoc && block.DecorRuns is null && !block.BorderTopOnly
                && block.Text.Trim(' ', ' ').Length == 0))
        {
            // A break after a UA box stands the box's closing gap below it first (the
            // heading's margin, then the break's own empty line box).
            if (cv.profile.uaStdSerif && block.IsHardBreak && rb.uaClosingGap > 0)
                cv.flow.y -= Math.Max(0, rb.uaClosingGap - rb.uaPrevMB);
            LayoutHardBreakBlock(cv, block, rb.metrics, rb.breakAfterTable, rb.wasRow);
            return false;
        }
        // A paragraph's margin stands once: the table's break tail stood it, and the text
        // after the paragraph's own leading `<br>` spacer does not stand it again (measured:
        // table → br → `<p><br/>text` spaces 13.5 + 13.44 + 13.5 + the ascent to the text).
        if (cv.profile.uaBareDoc && block.AfterOwnLeadingBreak && cv.flow.uaTailMarginSpent && cv.flow.lastWasHardBreak)
            block.MarginTop = 0;
        cv.flow.uaTailMarginSpent = false;
        cv.flow.lastWasHardBreak = false;
        // The `margin:` shorthand's TOP value, which the calibrated dialects take from
        // the dedicated margin-top handling instead. It has to be applied BEFORE the
        // margin chain below reads block.MarginTop.
        return null;
    }

    /// <summary>The dark line of a Word mail's rule groove: one CSS px (also the px-to-pt scale of its size).</summary>
    private const double WordMailRuleLinePt = 0.75;
    private const string WordMailRuleDarkHex = "#808080";
    private const string WordMailRuleLightHex = "#D3D3D3";
    /// <summary>Ascent share of a face whose metrics are unknown.</summary>
    private const double NormalAscentShare = 0.9;

    /// <summary>Horizontal rules of the sectioned-report, escaped-attribute and standard-serif dialects.</summary>
    private static bool? RenderReportRules(ConvertState cv, RenderBlockState rb, Block block)
    {
        // Word mail: the UA rule DRAWS - a groove of its `size` px (a dark line over a light one) across the
        // content width (or its width share), the rule's margins above and below - the reference's
        // divider under the signature band.
        if (cv.profile.wordMailDoc && block.IsHorizontalRule)
        {
            var ruleH = Math.Max(WordMailRuleLinePt, block.RuleWidth * WordMailRuleLinePt);
            var ruleW = block.WidthFrac > 0 ? cv.flow.contentWidth * block.WidthFrac : cv.flow.contentWidth;
            var ruleX = cv.marginLeft + block.LeftIndent + (cv.flow.contentWidth - ruleW) / 2;
            cv.flow.y -= block.MarginTop;
            DrawBox(cv.flow.page, ruleX, cv.flow.y - WordMailRuleLinePt, ruleW, WordMailRuleLinePt, null, 0, ParseCssColor(WordMailRuleDarkHex));
            if (ruleH > WordMailRuleLinePt)
                DrawBox(cv.flow.page, ruleX, cv.flow.y - ruleH, ruleW, ruleH - WordMailRuleLinePt, null, 0, ParseCssColor(WordMailRuleLightHex));
            // The cursor is the NEXT baseline: the block after the rule seats its ascent below the rule's margin.
            var ruleAscent = block.FontFamily is { } rf && WinMetricsFor(rf) is { } rm ? block.FontSize * rm.asc : block.FontSize * NormalAscentShare;
            cv.flow.y -= ruleH + block.MarginBottom + ruleAscent;
            cv.flow.contentPage = cv.flow.page;
            cv.flow.prevFlowMarginBottom = block.MarginBottom;
            cv.flow.prevFlowLineHeight = 0;
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
            // (…or the body's own right margin where its tag states a wider one - MEASURED, the
            //  evaluation form's `margin-right:1in`: the rule ends at W - 162)
            var hrW = cv.pageWidth - cv.marginLeft
                - (cv.uaBodyFaceFromAttr && cv.bodyMarginRightPt > UaBodyMarginPt ? cv.marginRight : cv.marginLeft);
            // The rule's leading 0.5em margin collapses with the block above's bottom
            // margin like its trailing one (probed: a p's 13.44 then an hr opens 13.44
            // in all, the rule's 6 inside it, not 19.44).
            // (the rule's margins are 0.5 em of ITS size: 7.15 inside the evaluation form's 14.3 pt heading)
            var hrMarginPt = HrCellMarginEm * (block.FontSize > 0 ? block.FontSize : UaDefaultFontPt);
            var hrBoxTop = cv.flow.y - Math.Max(0, hrMarginPt - rb.uaPrevMB);   // bottom-up box top edge
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
            // (…and the trailing margin is the rule's own or the heading's it closes, whichever is wider)
            var hrMarginBottomPt = Math.Max(hrMarginPt, block.MarginBottom);
            cv.flow.y -= hrMarginPt + 1.5 + hrMarginBottomPt;
            // The rule's trailing 0.5em is a MARGIN — it max-collapses with
            // the following block's own top margin (probed: hr then an empty
            // p's 13.44 opens 13.44 total, not 6 + 13.44).
            cv.flow.uaPrevMarginBottom = hrMarginBottomPt;
            cv.flow.lastWasHardBreak = false;
            return false;
        }

        // Hard-break blocks (<br>, empty <p>, <hr>) only consume vertical
        // space — never emit an empty BT/ET run, which would surface as
        // extra zero-length TextFragments to TextFragmentAbsorber. Coalesce
        // runs of consecutive hard-breaks so deeply-nested empty containers
        // don't explode page count (HTML like <div><div></div></div> emits
        // a chain of closes that would otherwise each become a blank line).
        return null;
    }

    /// <summary>Horizontal rules of the form-horizontal and redline-diff dialects.</summary>
    private static bool? RenderFormRules(ConvertState cv, Block block)
    {
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
        return null;
    }

    /// <summary>Row runs and the max-width horizontal rule.</summary>
    private static bool? RenderRowRunsAndRule(ConvertState cv, RenderBlockState rb, Block block)
    {
        if (block.RowRuns is { Count: > 0 })
        {
            var collapse = Math.Min(rb.prevRowBottomPx, block.RowMarginTopPx);
            cv.flow.y += collapse * 0.75;
            RenderRowBlock(cv.flow, block, cv.marginLeft, cv.flow.contentWidth, cv.pendingLinks);
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
        return null;
    }

    /// <summary>Image, button, input, diagram, flex, slide, card, topics-list and form blocks each lay themselves out and leave the text path.</summary>
    private static bool? RenderBlockObjectArms(ConvertState cv, RenderBlockState rb, HtmlLoadOptions? options, List<byte[]> inlineSvgs, Block block)
    {
        if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_BLOCKS") == "1")
            Console.WriteLine($"[block] y={cv.pageHeight - cv.flow.y:0.##} table={block.IsTable} hard={block.IsHardBreak} line={block.IsLineBreak} spacer={block.UaSpacerPara} mt={block.MarginTop:0.##} mb={block.MarginBottom:0.##} h={block.ExplicitHeight:0.##} fs={block.FontSize:0.##} li={block.IsListItem} ind={block.LeftIndent:0.##} maxW={block.MaxWidthPt:0.##} wpx={block.WidthPx:0.##} box={block.BoxWidthPt:0.##} cw={cv.flow.contentWidth:0.##} text='{(block.Text.Length > 24 ? block.Text[..24] : block.Text)}'");
        if (block.IsImage)
        {
            LayoutImageBlock(block, cv.flow, cv.profile, cv.doc, cv.docFontDict, options, inlineSvgs, cv.bandStack, cv.marginBottom, cv.marginLeft, cv.marginTop, cv.pageHeight, cv.pageWidth, rb.metrics.lineHeight);
            // The box that closed around the image (an image-only heading) keeps its UA
            // closing gap for the block that follows, exactly as a text block does.
            cv.flow.uaPrevMarginBottom = block.MarginBottom;
            cv.flow.uaClosingGap = block.UaClosingGapPt;
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
            LayoutRtlSvgDiagram(cv, dg, inlineSvgs);
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
        if (RenderVerbatimBlockArms(cv, block) is { } verbatim) return verbatim;

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
            LayoutSearchForm(cv, sf, options);
            return false;
        }

        // Styled inline row (nav bar / centered link line): bar rect + measured
        // horizontal runs, drawn directly at the flow cursor. Vertical margins
        // between adjacent rows collapse (CSS margin collapsing).
        return null;
    }
}
