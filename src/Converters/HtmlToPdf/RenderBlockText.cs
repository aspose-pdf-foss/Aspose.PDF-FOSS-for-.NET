using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The text stage of a block render: padding, line layout and the trailing cursor state, verbatim.</summary>
    private static void RenderBlockText(ConvertState cv, RenderBlockState rb, HtmlLoadOptions? options, List<byte[]> inlineSvgs, Block block)
    {
        // Padding is box space, not margin — it never collapses.
        if (block.PadTop > 0) cv.flow.y -= block.PadTop;
        // An inline broken image on this block's line grows the line box UP by
        // the icon: the baseline lands 29.2 pt lower than a bare text line
        // (measured: rule → icon-bearing label baseline = 41.12, bare = 11.9).
        if (cv.profile.escapedAttrDoc && block.InlineIconAfter) cv.flow.y -= InlineIconLineExtraPt;
        // Plain text directly under the section rule sits 17.9 below it, not the
        // bare 11.9 (headings carry their own margins and skip this).
        else if (cv.flow.afterEscapedRule && block.MarginTop <= 0) cv.flow.y -= RuleToTextExtraPt;
        // A block that OPENS an element marks where that element's box begins - after
        // its own top margin - so a declared height can be measured over the WHOLE
        // element rather than added after its last line.
        if (cv.profile.floatBothSidesDoc && block.ShorthandTopPt > 0) cv.flow.certElementTopY = cv.flow.y;
        cv.flow.afterEscapedRule = false;
        // A painted band/box block: its fill hangs 0.25em below the baseline
        // and one pad above the line top — the seat drops by the same 0.25em
        // so the fill's TOP lands exactly one pad below the reserved margin
        // (else the fill juts up and eats the gap the flow reserved).
        if (block.BandPadPt > 0 && rb.metrics.blockFontSize > 0) cv.flow.y -= rb.metrics.blockFontSize * 0.25;
        rb.coverDrop = 0.0;
        if (block.LineFactor > 0 && rb.metrics.blockFontSize > 0)
        {
            rb.coverDrop = rb.metrics.lineHeight - rb.metrics.blockFontSize * SlideTextDescEm;
            cv.flow.y -= rb.coverDrop;
        }

        // Redline divider bar: a paragraph's solid border-top strokes above
        // its content across its own box (the measured 4.5 pt cover rule
        // spans margin+36 .. the right content edge, top ON the flow cursor).
        if (cv.profile.redlineDiffDoc && block.BorderTopOnly && block.BorderColor is { } rdBarCol
            && block.BorderWidth > 0)
        {
            var barInv = System.Globalization.CultureInfo.InvariantCulture;
            var barY = (cv.flow.y - block.BorderWidth / 2).ToString("0.##", barInv);
            cv.flow.page.AddContentStream(Encoding.ASCII.GetBytes(
                "q " + (rdBarCol.R / 255.0).ToString("0.###", barInv)
                + " " + (rdBarCol.G / 255.0).ToString("0.###", barInv)
                + " " + (rdBarCol.B / 255.0).ToString("0.###", barInv) + " RG "
                + block.BorderWidth.ToString("0.##", barInv) + " w "
                + (cv.marginLeft + block.LeftIndent).ToString("0.##", barInv) + " " + barY + " m "
                + (cv.pageWidth - cv.marginRight).ToString("0.##", barInv) + " " + barY + " l S Q" + (char)10));
            cv.flow.y -= block.BorderWidth;
            cv.flow.contentPage = cv.flow.page;
        }
        // pt-styled fragment: the source keeps the baseline gap across a block
        // boundary at exactly the PREVIOUS block's line height (measured 12.1 /
        // 19.4 / 12.0 / 9.7 across every 10↔16 and 8↔10 transition). Our seat
        // draws the first baseline one FONT SIZE above the entry cursor, which
        // skews that gap by the size delta — repay it here. A distinct leading
        // span sizes the first line box, so it, not the block size, is the seat.
        if (cv.profile.ptStyledFragment && cv.flow.prevFlowFontSize > 0 && rb.metrics.blockFontSize > 0)
            cv.flow.y -= (block.LeadFontSize > 0 ? block.LeadFontSize : rb.metrics.blockFontSize)
                 - cv.flow.prevFlowFontSize;
        // Redline diff document: the cross-paragraph baseline advance is
        // DescLead(prev) + AscLead(next) (probed: 21.0 across 18->18,
        // 13.5 across 18->10) — repay the per-line 1.125 em the loop spent.
        else if (cv.profile.redlineDiffDoc && rb.metrics.blockFontSize > 0
                 && !string.IsNullOrEmpty(block.Text))
            // …and the FIRST baseline (no previous flow line) seats a full
            // AscLead below the cursor (probed: the cover bar paragraph's
            // marker underline at 97.3 = bar bottom + pad + 0.929 em).
            cv.flow.y -= RedlineAscLeadEm * rb.metrics.blockFontSize
                 - (cv.flow.prevFlowFontSize > 0
                    ? (RedlineLineFactor - RedlineDescLeadEm) * cv.flow.prevFlowFontSize : 0);
        // The leading span's taller first line box: the next baseline sits one
        // LEAD line height below the lead baseline; the block's own text seats
        // a size delta below the lead, so the net extra advance after line 1 is
        // the half-leading difference.
        rb.metrics.ptLeadExtraPt = cv.profile.ptStyledFragment && rb.metrics.blockFontSize > 0
            && block.LeadFontSize > rb.metrics.blockFontSize
                ? (PtFragmentLineFactor - 1) * (block.LeadFontSize - rb.metrics.blockFontSize)
                : 0.0;
        rb.availWidth = cv.flow.contentWidth - block.LeftIndent - block.RightInsetPt;
        // FLOAT FLOW: a block keeps the box its own margin and width declare, even
        // when that box overflows the content frame - the certificate heading is a
        // 550px paragraph inset 120px, so it runs 186..598.5 on a sheet whose content
        // ends at 499, so it is let overflow and clipped. Its line box then
        // starts past whichever float it is level with.
        // The certificate page declares no body font-family, so text it does not
        // style falls to the UA default - a SERIF. The heading is expected in
        // Times New Roman Bold Italic where we were drawing Helvetica-Oblique, which
        // measured the line 223.57 pt against the expected 181.09 and threw every
        // centred x off. Naming the family also gets a real bold-italic face, which
        // the Standard-14 resource table has no slot for.
        if (cv.profile.floatBothSidesDoc && string.IsNullOrEmpty(block.FontFamily) && !block.IsImage)
            block.FontFamily = "Times New Roman";
        // …and a family the box has not got falls through its own stack, as CSS says:
        // the certificate heading's `'OpenSansRegular', arial, serif` draws in ARIAL,
        // where taking the first NAMED family dropped it to a Standard-14.
        if (cv.profile.floatBothSidesDoc && !block.IsImage && block.FontFamilyStack is { } famStack
            && block.FontFamily is { Length: > 0 } declaredFam
            && WinMetricsFor(declaredFam) is null)
            foreach (var cand in famStack.Split(','))
                if (cand.Trim().Trim('\'', '"').Trim() is { Length: > 0 } candName
                    && WinMetricsFor(candName) is not null)
                { block.FontFamily = candName; break; }
        // FLOAT FLOW: a block's own `margin-right` insets its wrap box, the mirror of
        // the margin-left that already sets LeftIndent. The certificate's body
        // paragraphs declare 90 px each side, so they wrap at 431.5 on the 96..499
        // content box - without this they ran to the content edge and re-broke every
        // line.
        if (cv.profile.floatBothSidesDoc && block.MarginRightPt > 0 && !block.IsTable && !block.IsImage)
            rb.availWidth = Math.Max(50, rb.availWidth - block.MarginRightPt);
        rb.metrics.floatBoxLeftPt = 0.0;
        rb.metrics.floatBoxWidthPt = 0.0;
        if (cv.profile.floatBothSidesDoc && block.WidthPx > 0 && !block.IsTable && !block.IsImage)
        {
            rb.metrics.floatBoxLeftPt = block.ShorthandLeftPt;
            rb.metrics.floatBoxWidthPt = block.WidthPx * 0.75;
            rb.availWidth = rb.metrics.floatBoxWidthPt;
        }
        // UA-serif flow on the default sheet: the body's right margin mirrors
        // the left (symmetric 96), so the TEXT wrap box ends one UA body
        // margin inside the page's right margin (probed: the expected render
        // wraps flow text at a 403 pt content box on the default A4 sheet —
        // right edge 499 — while tables still reach the 90 pt page margin).
        // (The Word-filtered arm keeps its own measured text column.)
        if (cv.profile.uaStdSerif && !cv.marginsExplicit && !cv.profile.bodyZeroMargin && !block.IsTable
            && !cv.profile.msoFilteredDoc)
            rb.availWidth -= UaBodyMarginPt;
        // Browser-UA flow: an enclosing div's width:N% narrows the wrap box.
        if (cv.profile.uaStdSerif && block.WidthFrac > 0)
            rb.availWidth = Math.Min(rb.availWidth, cv.flow.contentWidth * block.WidthFrac);
        // Form-document dialect: an enclosing div's ABSOLUTE width is the wrap box
        // (the state-notice divs wrap at their width:680 wrapper, not the page).
        if (cv.profile.formDialectTables && block.WidthPx > 0)
            rb.availWidth = Math.Min(rb.availWidth, block.WidthPx * 0.75);
        // Browser-UA flow: so is a PIXEL width, the same way a percentage one is
        // (probed: `<div style="width:150px">` wraps its line to the declared box
        // rather than running to the page edge).
        if (cv.profile.uaStdSerif && block.WidthPx > 0)
            rb.availWidth = Math.Min(rb.availWidth, block.WidthPx * 0.75);
        // Report label/span rows: the column's own box is the wrap box.
        if (block.MaxWidthPt > 0)
            rb.availWidth = Math.Min(rb.availWidth, block.MaxWidthPt);
        // A float:left LABEL with a pixel width: the next in-flow text
        // block sits BESIDE it on the same line, its text at the label's
        // declared box edge (measured: labels at 96, values at
        // 96 + 100px·0.75 = 171, one 13.5 pt line per pair).
        rb.metrics.floatLabelIndent = 0.0;
        if (cv.profile.uaStdSerif && cv.flow.pendingFloatLabelPt > 0 && !block.FloatLeft
            && !block.IsTable && !string.IsNullOrEmpty(block.Text))
        {
            cv.flow.y = cv.flow.pendingFloatLabelY;
            rb.metrics.floatLabelIndent = cv.flow.pendingFloatLabelPt;
            rb.availWidth = Math.Max(50, rb.availWidth - rb.metrics.floatLabelIndent);
        }
        cv.flow.pendingFloatLabelPt = 0;
        if (cv.profile.uaStdSerif && block.FloatLeft && !block.FloatRight
            && block.WidthPx > 0 && !block.IsTable
            && !string.IsNullOrEmpty(block.Text))
        {
            cv.flow.pendingFloatLabelPt = block.WidthPx * 0.75;
            cv.flow.pendingFloatLabelY = cv.flow.y;
        }
        rb.metrics.yBeforeBlockLines = cv.flow.y;
        rb.uaBorderBox = cv.profile.uaStdSerif && block.BorderWidth > 0 && block.BorderColor is not null && !block.IsTable && block.BgBoxHeightPt <= 0 && block.BorderBoxWPt <= 0 && !string.IsNullOrEmpty(block.Text);
        if (rb.uaBorderBox) cv.flow.y -= block.BorderWidth;
        rb.uaDeclBox = cv.profile.uaStdSerif && block.BorderBoxWPt > 0 && block.BorderColor is not null && block.BorderWidth > 0;
        if (rb.uaDeclBox)
        {
            cv.flow.y -= block.BorderWidth;
            rb.availWidth = Math.Min(rb.availWidth, block.BorderBoxWPt);
        }
        // Inside a float-column band (the SEC-filing two-column card), wrap with the
        // block's real font metrics — the crude 0.52-em estimate mis-breaks the narrow
        // column (bold uppercase headings never wrap; body text wraps a line early).
        // Scoped to bands, so the calibrated flat-flow greens keep their 0.52-em breaks.
        // The form-document dialect wraps a family-declaring block the same way — its
        // notice divs (`font: bold 8pt Verdana`) break where the REAL face breaks.
        rb.metrics.bandFace = cv.bandStack.Count > 0 ? BandMeasureFace(block)
            : cv.profile.formDialectTables && !string.IsNullOrEmpty(block.FontFamily) ? BandMeasureFace(block)
            // report label/span columns wrap on real advances too — the crude
            // 0.52-em estimate breaks their narrow boxes a word early
            : block.MaxWidthPt > 0 && !string.IsNullOrEmpty(block.FontFamily) ? BandMeasureFace(block)
            : null;
        rb.floatLines = 0;
        // Lines level with the LEFT float are pushed in from the left; lines level
        // with the RIGHT float lose width on the right. The two spans need not
        // overlap - a dropped right float sits below the left one entirely.
        rb.metrics.besideLeftFloat = cv.flow.floatIndentPt > 0 && cv.flow.y > cv.flow.floatBottomY + 1e-9;
        rb.besideRightFloat = cv.flow.floatRightInsetPt > 0 && cv.flow.y <= cv.flow.floatRightTopY + 1e-9 && cv.flow.y > cv.flow.floatRightBottomY + 1e-9;
        if ((rb.metrics.besideLeftFloat || rb.besideRightFloat) && rb.metrics.lineHeight > 0)
            rb.floatLines = (int)Math.Ceiling(
                (cv.flow.y + (cv.profile.floatBothSidesDoc
                        ? FloatLineBoxRise(block, rb.metrics.blockFontSize, rb.metrics.lineHeight) : 0)
                   - (rb.metrics.besideLeftFloat ? cv.flow.floatBottomY : cv.flow.floatRightBottomY)) / rb.metrics.lineHeight);
        rb.floatBlockL = rb.metrics.floatBoxWidthPt > 0 ? cv.marginLeft + rb.metrics.floatBoxLeftPt : cv.marginLeft + block.LeftIndent;
        rb.floatBlockR = rb.floatBlockL + rb.availWidth;
        rb.floatNarrowW = Math.Max(1, (rb.besideRightFloat ? Math.Min(rb.floatBlockR, cv.marginLeft + cv.flow.contentWidth - cv.flow.floatRightInsetPt) : rb.floatBlockR) - (rb.metrics.besideLeftFloat ? Math.Max(rb.floatBlockL, cv.marginLeft + cv.flow.floatIndentPt) : rb.floatBlockL));
        rb.metrics.lines = block.MaxWidthPt > 0
            ? ReportWordWrap(block.Text, rb.availWidth, rb.metrics.blockFontSize, block.FontRes == "F2")
            : cv.profile.metricFlow && rb.metrics.metricDrop > 0
            ? MeasuredWordWrap(block.Text, rb.availWidth, rb.metrics.metricMeasureFace, rb.metrics.blockFontSize)
            : rb.metrics.bandFace is not null
                ? MeasuredWordWrap(block.Text, rb.availWidth, rb.metrics.bandFace, rb.metrics.blockFontSize)
                // pt-styled fragment: the flow draws real TrueType faces —
                // break lines on their real advances (the 0.52-em estimate
                // over-measures Verdana's short words and wraps a word early).
                // The redline diff document wraps on its Times advances the
                // same way — small-caps blocks on their case-scaled advances.
                : cv.profile.redlineDiffDoc && block.SmallCaps && !string.IsNullOrEmpty(block.FontFamily)
                ? SmallCapsWordWrap(block.Text, rb.availWidth,
                    block.FontFamily + (block.FontRes == "F2" || block.EmBold ? " Bold" : ""),
                    rb.metrics.blockFontSize)
                // …and an indented paragraph wraps its FIRST line short.
                : cv.profile.redlineDiffDoc && block.TextIndentPt > 0 && !string.IsNullOrEmpty(block.FontFamily)
                ? RedlineIndentWrap(block.Text, rb.availWidth - block.TextIndentPt, rb.availWidth,
                    block.FontFamily + (block.FontRes == "F2" || block.EmBold ? " Bold" : ""),
                    rb.metrics.blockFontSize)
                : (cv.profile.ptStyledFragment || cv.profile.redlineDiffDoc) && !string.IsNullOrEmpty(block.FontFamily)
                ? MeasuredWordWrap(block.Text, rb.availWidth,
                    block.FontRes == "F2" || block.EmBold
                        ? block.FontFamily + " Bold"
                        : block.EmItalic ? block.FontFamily + " Italic" : block.FontFamily,
                    rb.metrics.blockFontSize)
                // Dash-overflow wrap: the doc carries a dash-delimited segment
                // wider than the content box — every line then wraps at the
                // widened limit on space/after-dash breakpoints with real face
                // advances (see quirksWrapW above).
                : cv.quirksWrapW > rb.availWidth
                ? DashAwareWordWrap(block.Text, cv.quirksWrapW, cv.dashWrapFace, rb.metrics.blockFontSize)
                // The escaped-attr dialect draws serif — break lines on the real
                // Times advances, not the 0.52-em estimate (17 % narrow).
                : cv.profile.escapedAttrDoc
                    ? MeasuredWordWrap(block.Text, rb.availWidth,
                        block.FontRes == "F2" ? "Times New Roman Bold" : "Times New Roman",
                        rb.metrics.blockFontSize)
                // The pinned-body report wraps on its page face's real advances
                // too — the estimate over-measures narrow-lettered runs — with
                // CSS break-word semantics (its sheet's `* { word-break }`): an
                // over-long token moves to its own line before char-splitting.
                : cv.profile.bodyPinnedW > 0 && cv.bodyPinnedFace is not null
                    ? MeasuredWordWrap(block.Text, rb.availWidth,
                        block.FontRes == "F2" ? cv.bodyPinnedFace + " Bold" : cv.bodyPinnedFace,
                        rb.metrics.blockFontSize, wordFirst: true)
                    // The certificate flow draws real TrueType faces — break its lines
                    // on their real advances. The 0.52-em estimate over-measures Arial
                    // and broke every body paragraph four lines early, which is what
                    // cost the page.
                    : cv.profile.floatBothSidesDoc && !string.IsNullOrEmpty(block.FontFamily)
                    ? MeasuredWordWrapPastFloat(block.Text, rb.floatNarrowW, rb.availWidth,
                        rb.floatLines, FloatFlowMeasureFace(block), rb.metrics.blockFontSize)
                    : rb.floatLines > 0
                        ? WordWrapPastFloat(block.Text, rb.floatNarrowW,
                            rb.availWidth, rb.floatLines, rb.metrics.blockFontSize * 0.52)
                        : WordWrap(block.Text, rb.availWidth, rb.metrics.blockFontSize * 0.52);
        rb.textHeight = rb.metrics.lines.Length * rb.metrics.lineHeight;
        rb.paddingBelow = block.ExplicitHeight > rb.textHeight ? block.ExplicitHeight - rb.textHeight : 0;
        // In the float flow the declared height is the WHOLE element's box, measured
        // from where it opened - the certificate heading reserves 200 px for its four
        // break-separated lines together, not 200 px after the fourth.
        if (cv.profile.floatBothSidesDoc && block.ExplicitHeight > 0 && !double.IsNaN(cv.flow.certElementTopY))
        {
            var spent = cv.flow.certElementTopY - cv.flow.y + rb.textHeight;
            rb.paddingBelow = block.ExplicitHeight > spent ? block.ExplicitHeight - spent : 0;
        }
        // A container-band height (the widget header's class-rule height) spans
        // from the LINE-BOX TOP, but this flow anchors y at the baseline — one
        // line box above the block was already spent getting there. Bill the
        // band's remainder below the baseline only, or the band gains a full
        // extra line (measured: the chart sat 18.8 pt low).
        if (block.BandBoxHeight && rb.paddingBelow > 0)
            rb.paddingBelow = Math.Max(0, rb.paddingBelow - rb.metrics.lineHeight);

        // Approximate per-char advance for mapping inline <a href> char ranges to
        // link-annotation rects (same crude metric WordWrap uses to break lines).
        rb.metrics.charW = rb.metrics.blockFontSize * 0.52;
        rb.metrics.lineX = cv.marginLeft + block.LeftIndent
            + (rb.uaDeclBox ? block.BorderWidth : 0);
        // FLOAT FLOW: the block sits in the box its own margin and width declare,
        // which may overflow the content frame; it is let overflow and clipped.
        if (rb.metrics.floatBoxWidthPt > 0) rb.metrics.lineX = cv.marginLeft + rb.metrics.floatBoxLeftPt;
        rb.metrics.cumChar = 0;          // char offset of the current line's start within block.Text
        rb.metrics.firstLineOfBlock = true;
        WriteBlockTextLines(block, cv.flow, cv.profile, rb.metrics, cv.doc, cv.docFontDict, cv.sb, cv.anchorTargets,
            cv.pendingLinks, cv.embeddedFonts, cv.fontFileCache, cv.bandStack, cv.articleFlow, cv.uaFlow,
            cv.marginBottom, cv.marginLeft, cv.marginRight, cv.marginTop, cv.pageHeight, cv.pageWidth);
        // The inline-css-box block ends at its line-box bottom (half-leading
        // under the descent) plus the element's 1-em default bottom margin.
        // In the float flow the blocks are not separate elements at all — they are
        // one paragraph's `<br><br>`-separated runs — so what stands between them is
        // an empty LINE BOX of the paragraph's own pitch, not a paragraph margin.
        // Measured: that puts the next run's first baseline exactly two pitches below
        // the last one (25.714), where the 1-em charge left it 21.86.
        if (rb.inlineCssBox)
            cv.flow.y -= rb.icbHalfLead + (cv.profile.floatBothSidesDoc ? rb.metrics.lineHeight : rb.metrics.blockFontSize);
        // Close the UA border box: stroke its four edges (centred half a
        // width inside) from the element's box left to the symmetric
        // content right edge, and spend the bottom border width.
        if (rb.uaBorderBox && block.BorderColor is { } ubCol)
        {
            var invc = System.Globalization.CultureInfo.InvariantCulture;
            var ubw = block.BorderWidth;
            var ubLeft = cv.marginLeft + block.LeftIndent;
            var ubRight = cv.pageWidth - cv.marginLeft;
            var ubTop = rb.metrics.yBeforeBlockLines;          // border box top (bottom-up)
            var ubBot = cv.flow.y - ubw;
            // border-top-only divider: one rule above the content, no frame
            // and no bottom width to spend.
            var ubStroke = block.BorderTopOnly
                ? string.Create(invc,
                    $"q {ubCol.R / 255.0:0.###} {ubCol.G / 255.0:0.###} {ubCol.B / 255.0:0.###} RG {ubw:0.##} w ")
                  + string.Create(invc,
                    $"{ubLeft:0.##} {ubTop - ubw / 2:0.##} m {ubRight:0.##} {ubTop - ubw / 2:0.##} l S Q\n")
                : string.Create(invc,
                    $"q {ubCol.R / 255.0:0.###} {ubCol.G / 255.0:0.###} {ubCol.B / 255.0:0.###} RG {ubw:0.##} w ")
                  + string.Create(invc,
                    $"{ubLeft:0.##} {ubTop - ubw / 2:0.##} m {ubRight:0.##} {ubTop - ubw / 2:0.##} l S ")
                  + string.Create(invc,
                    $"{ubLeft:0.##} {ubBot + ubw / 2:0.##} m {ubRight:0.##} {ubBot + ubw / 2:0.##} l S ")
                  + string.Create(invc,
                    $"{ubLeft + ubw / 2:0.##} {ubTop:0.##} m {ubLeft + ubw / 2:0.##} {ubBot:0.##} l S ")
                  + string.Create(invc,
                    $"{ubRight - ubw / 2:0.##} {ubTop:0.##} m {ubRight - ubw / 2:0.##} {ubBot:0.##} l S Q\n");
            cv.flow.page.AddContentStream(Encoding.ASCII.GetBytes(ubStroke));
            if (!block.BorderTopOnly) cv.flow.y = ubBot;
            cv.flow.contentPage = cv.flow.page;
        }
        // Print-grid heading band: the ".cls h4" border-bottom paints as a filled
        // bar across the wrap box, padding-bottom below the last line box.
        if (cv.profile.printGrid && block.BandColor is { } bandC && block.BandPx > 0)
        {
            var bandH = block.BandPx * 0.75;
            var bandPad = block.BandPadPx * 0.75;
            var bandY = cv.flow.y - bandPad - bandH;
            var bandRect = FormattableString.Invariant(
                $"q {bandC.R / 255.0:0.###} {bandC.G / 255.0:0.###} {bandC.B / 255.0:0.###} rg {cv.marginLeft + block.LeftIndent:0.##} {bandY:0.##} {cv.flow.contentWidth - block.LeftIndent:0.##} {bandH:0.##} re f Q\n");
            cv.flow.page.AddContentStream(Encoding.ASCII.GetBytes(bandRect));
            cv.flow.y = bandY;
        }
        if (rb.paddingBelow > 0)
        {
            if (cv.flow.y - rb.paddingBelow < cv.marginBottom)
            {
                cv.flow.page = cv.doc.Pages.Add(cv.pageWidth, cv.pageHeight);
                EnsureFonts(cv.flow.page, cv.docFontDict);
                cv.flow.y = cv.pageHeight - cv.marginTop; cv.flow.pendingTopDrop = cv.profile.hasZeroTopMargin;
            }
            else
            {
                cv.flow.y -= rb.paddingBelow;
            }
        }
        // A painted band/box hands the cursor back at its FILL's bottom edge —
        // the trailing line box the flow advanced overshoots it by a line less
        // the fill's own under-baseline hang (0.25em) and bottom pad; the next
        // element's margin-top is then the whole gap. The
        // cursor now rests on a box edge exactly as after a table, so the next
        // text block owes the same line-box drop.
        if (block.BandPadPt > 0 && rb.metrics.blockFontSize > 0)
        {
            cv.flow.y += rb.metrics.lineHeight - rb.metrics.blockFontSize * 0.25 - block.BandPadPt;
            cv.flow.pendingTableDrop = true;
        }
        // Close the border-only declared box: stroke its (rounded) frame on the
        // centreline — outer edge at the flow position the box opened at, outer
        // size = declared content + a border width each side — then spend the
        // bottom border. Probed: the 200px radius box strokes w=1 on the 151 pt
        // centreline square [96.5,78.5..247.5,229.5].
        if (rb.uaDeclBox && block.BorderColor is { } dbCol)
        {
            var invc = System.Globalization.CultureInfo.InvariantCulture;
            var bw = block.BorderWidth;
            var bL = cv.marginLeft + block.LeftIndent + bw / 2;
            var bT = rb.metrics.yBeforeBlockLines - bw / 2;
            var bR = bL + block.BorderBoxWPt + bw;
            var bB = bT - block.ExplicitHeight - bw;
            // Corner radius on the centreline: the declared radius minus half a
            // border, clamped to half the centreline box.
            var r = Math.Min(Math.Max(0, block.BorderRadiusPt - bw / 2),
                Math.Min((bR - bL) / 2, (bT - bB) / 2));
            var dbSb = new StringBuilder();
            dbSb.Append(string.Create(invc,
                $"q {dbCol.R / 255.0:0.###} {dbCol.G / 255.0:0.###} {dbCol.B / 255.0:0.###} RG {bw:0.##} w "));
            if (r > 0)
            {
                const double k = 0.5522847498; // cubic-bezier circle-arc constant
                var kr = k * r;
                dbSb.Append(string.Create(invc, $"{bL + r:0.##} {bT:0.##} m "));
                dbSb.Append(string.Create(invc, $"{bR - r:0.##} {bT:0.##} l "));
                dbSb.Append(string.Create(invc, $"{bR - r + kr:0.##} {bT:0.##} {bR:0.##} {bT - r + kr:0.##} {bR:0.##} {bT - r:0.##} c "));
                dbSb.Append(string.Create(invc, $"{bR:0.##} {bB + r:0.##} l "));
                dbSb.Append(string.Create(invc, $"{bR:0.##} {bB + r - kr:0.##} {bR - r + kr:0.##} {bB:0.##} {bR - r:0.##} {bB:0.##} c "));
                dbSb.Append(string.Create(invc, $"{bL + r:0.##} {bB:0.##} l "));
                dbSb.Append(string.Create(invc, $"{bL + r - kr:0.##} {bB:0.##} {bL:0.##} {bB + r - kr:0.##} {bL:0.##} {bB + r:0.##} c "));
                dbSb.Append(string.Create(invc, $"{bL:0.##} {bT - r:0.##} l "));
                dbSb.Append(string.Create(invc, $"{bL:0.##} {bT - r + kr:0.##} {bL + r - kr:0.##} {bT:0.##} {bL + r:0.##} {bT:0.##} c s "));
            }
            else
            {
                dbSb.Append(string.Create(invc,
                    $"{bL:0.##} {bB:0.##} {bR - bL:0.##} {bT - bB:0.##} re S "));
            }
            dbSb.Append("Q\n");
            cv.flow.page.AddContentStream(Encoding.ASCII.GetBytes(dbSb.ToString()));
            cv.flow.y = bB - bw / 2;
            cv.flow.contentPage = cv.flow.page;
        }
        // A deferred mid-line broken image: the 32×32 placeholder rides at the
        // last line's END — bottom one point above the baseline, rising over the
        // space above — and consumes no flow height of its own.
        if (cv.profile.escapedAttrDoc && block.InlineIconAfter && rb.metrics.lines.Length > 0)
        {
            var iconBase = cv.flow.y + rb.metrics.lineHeight;
            // The markup's collapsed space between the label and the image
            // survives — the icon starts one space past the text.
            var iconX = cv.marginLeft + block.LeftIndent + MeasureFaceText(
                block.FontRes == "F2" ? "Times New Roman Bold" : "Times New Roman",
                rb.metrics.lines[^1] + " ", rb.metrics.blockFontSize);
            var phName = RegisterPlaceholderIcon(cv.doc, cv.flow.page, ref cv.flow.flowIconRef, masked: true);
            cv.flow.page.AddContentStream(Encoding.ASCII.GetBytes(FormattableString.Invariant(
                $"q 32 0 0 32 {iconX + 1:0.##} {iconBase + 1:0.##} cm /{phName} Do Q\n")));
            var inD = ParseCssColor("#555555");
            var inL = ParseCssColor("#AAAAAA");
            DrawBox(cv.flow.page, iconX, iconBase + 33, 34, 1, null, 0, inD);
            DrawBox(cv.flow.page, iconX, iconBase, 34, 1, null, 0, inL);
            DrawBox(cv.flow.page, iconX, iconBase, 1, 34, null, 0, inD);
            DrawBox(cv.flow.page, iconX + 33, iconBase, 1, 34, null, 0, inL);
        }
        // Repay the cover box-model drop: the next box starts at this box's
        // bottom edge, not one drop below it.
        // …but a block that reserved a DECLARED height already rests on its element's
        // box bottom, which is an absolute edge — repaying the drop on top of it lifts
        // the cursor a whole drop above the box the author declared. Measured on the
        // certificate: its h1 sat 16.42 high, and every block below inherited it.
        if (rb.coverDrop > 0 && !(cv.profile.floatBothSidesDoc && rb.paddingBelow > 0)) cv.flow.y += rb.coverDrop;
        cv.flow.y -= block.MarginBottom;
        // a label gives its row's height back — the span beside it advances
        if (block.NoAdvanceY) cv.flow.y = cv.profile.dwFormDoc ? rb.yAtBlockEntry : rb.metrics.yBeforeBlockLines;
        cv.flow.prevFlowMarginBottom = block.MarginBottom;
        cv.flow.prevFlowLineHeight = rb.metrics.lineHeight;
        cv.flow.prevFlowFontSize = rb.metrics.blockFontSize;
        cv.flow.uaPrevMarginBottom = block.MarginBottom;
    }
}
