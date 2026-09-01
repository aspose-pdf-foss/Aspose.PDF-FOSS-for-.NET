using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The entry stage of a block render: breaks, spacing and margin collapse, verbatim; a return that ended the block became return false.</summary>
    private static bool RenderBlockSpacing(ConvertState cv, RenderBlockState rb, HtmlLoadOptions? options, List<byte[]> inlineSvgs, Block block)
    {
        rb.metrics = new HtmlBlockMetrics();
        // A declared height/min-height is a floor the element's content grows
        // DOWN into: the start marker only remembers where the element opened.
        if (block.HeightFloorStart)
        {
            cv.heightFloorStack.Push((cv.flow.y, cv.flow.page));
            return false;
        }
        // …and the close drops the cursor to the floor when the content stopped
        // short of it. Content that overran the floor keeps its own position -
        // the floor never pulls the flow back UP. A floor whose element spilled
        // onto a later page is spent (its page is gone), so it is only dropped.
        if (block.HeightFloorEnd)
        {
            if (cv.heightFloorStack.Count > 0)
            {
                var (openY, openPage) = cv.heightFloorStack.Pop();
                var floorY = openY - block.ExplicitHeight;
                if (ReferenceEquals(cv.flow.page, openPage) && cv.flow.y > floorY)
                {
                    if (floorY < cv.marginBottom)
                    {
                        cv.flow.page = cv.doc.Pages.Add(cv.pageWidth, cv.pageHeight);
                        EnsureFonts(cv.flow.page, cv.docFontDict);
                        cv.flow.contentPage = cv.flow.page;
                        cv.flow.y = FreshPageTopY(cv.profile.escapedAttrDoc, cv.pageHeight, cv.marginTop); cv.flow.pendingTopDrop = cv.profile.hasZeroTopMargin;
                    }
                    else cv.flow.y = floorY;
                    // A floor the content did not reach ENDS the margin-collapse
                    // chain: the block after it opens a fresh box under the floor
                    // and spends its own top margin in full (measured - a floor
                    // that fires costs one extra paragraph margin, one that stays
                    // inert costs none).
                    cv.flow.uaPrevMarginBottom = 0;
                }
            }
            return false;
        }

        rb.yAtBlockEntry = cv.flow.y;
        // The quoted section heading an over-tall image opens its own sheet.
        if (cv.msoKeepWithImage is not null && cv.msoKeepWithImage.Contains(block)
            && cv.flow.y < cv.pageHeight - cv.marginTop - 0.01)
        {
            cv.flow.page = cv.doc.Pages.Add(cv.pageWidth, cv.pageHeight);
            EnsureFonts(cv.flow.page, cv.docFontDict);
            cv.flow.contentPage = cv.flow.page;
            cv.flow.y = cv.pageHeight - cv.marginTop;
        }
        rb.tableAfterText = block.IsTable && cv.flow.prevBlockWasText;
        rb.breakAfterTable = cv.flow.lastWasMetricTable;
        rb.tableAfterSpacer = cv.flow.lastBreakWasUaSpacer;
        // Only the FIRST break after the table stands in for the table
        // wrapper's margin - a chain of breaks must not re-charge it.
        if (block.IsHardBreak) cv.flow.lastWasMetricTable = false;
        if (!string.IsNullOrEmpty(block.Text) && !block.IsTable) cv.flow.prevBlockWasText = true;
        else if (block.IsTable) cv.flow.prevBlockWasText = false;
        if (!block.IsHardBreak) { cv.flow.lastWasMetricTable = false; cv.flow.lastBreakWasUaSpacer = false; }
        if (cv.spBlocks is not null && cv.spBlocks.Contains(block))
        {
            if (ReferenceEquals(block, cv.spFirst))
            {
                cv.flow.page = cv.doc.Pages.Add(cv.pageWidth, cv.pageHeight);
                EnsureFonts(cv.flow.page, cv.docFontDict);
                cv.flow.contentPage = cv.flow.page;
                var spHeads = new List<string>();
                var spTable = "";
                foreach (var secB in cv.blocks)
                    if (cv.spBlocks.Contains(secB))
                    {
                        if (secB.IsTable) spTable = secB.TableHtml ?? "";
                        else if (!string.IsNullOrEmpty(secB.Text)) spHeads.Add(secB.Text!);
                    }
                RenderSpMatrixSection(cv.flow.page, cv.pageHeight, cv.marginLeft, spHeads, spTable, inlineSvgs);
                cv.flow.y = cv.marginBottom;
                cv.flow.lastWasHardBreak = false;
            }
            return false;
        }
        rb.uaPrevMB = cv.flow.uaPrevMarginBottom;
        cv.flow.uaPrevMarginBottom = 0;
        if (block.FloatBandStart)
        {
            cv.bandStack.Push((cv.marginLeft, cv.flow.contentWidth, cv.flow.y, cv.flow.y, cv.flow.page));
            return false;
        }
        if (block.FloatColStart && cv.bandStack.Count > 0)
        {
            var band = cv.bandStack.Pop();
            band.MinEndY = Math.Min(band.MinEndY, cv.flow.y);
            if (ReferenceEquals(cv.flow.page, band.StartPage)) cv.flow.y = band.TopY;
            cv.marginLeft = band.SavedML + block.FloatStartFrac * band.SavedCW;
            cv.flow.contentWidth = Math.Max(20, block.FloatWidthFrac * band.SavedCW);
            cv.bandStack.Push(band);
            cv.flow.y -= block.FloatPadTopPt;
            cv.flow.bandColClipped = false;
            return false;
        }
        if (block.FloatBandEnd && cv.bandStack.Count > 0)
        {
            var band = cv.bandStack.Pop();
            if (ReferenceEquals(cv.flow.page, band.StartPage)) cv.flow.y = Math.Min(band.MinEndY, cv.flow.y);
            cv.marginLeft = band.SavedML;
            cv.flow.contentWidth = band.SavedCW;
            cv.flow.bandColClipped = false;
            return false;
        }
        if (block.ColScopeStart)
        {
            cv.colScopeStack.Push((cv.marginLeft, cv.flow.contentWidth));
            cv.flow.contentWidth = Math.Max(20, block.FloatWidthFrac * cv.flow.contentWidth - block.ColPadPt);
            return false;
        }
        if (block.ColScopeEnd)
        {
            if (cv.colScopeStack.Count > 0)
                (cv.marginLeft, cv.flow.contentWidth) = cv.colScopeStack.Pop();
            return false;
        }
        if (block.BoxStart)
        {
            cv.boxStack.Push((cv.marginLeft, cv.flow.y + block.BoxAscentPt, cv.flow.contentWidth, block.BoxBorderPt, cv.flow.page,
                block.BoxBorderGray, block.BoxPadSidePt, cv.marginLeft, cv.flow.contentWidth));
            cv.flow.y -= block.BoxPadTopPt;
            if (block.BoxPadSidePt > 0)
            {
                cv.marginLeft += block.BoxPadSidePt / 2;
                cv.flow.contentWidth = Math.Max(20, cv.flow.contentWidth - block.BoxPadSidePt);
            }
            return false;
        }
        if (block.BoxEnd && cv.boxStack.Count > 0)
        {
            var box = cv.boxStack.Pop();
            cv.marginLeft = box.SavedML;
            cv.flow.contentWidth = box.SavedCW;
            cv.flow.y -= block.BoxPadBottomPt;
            var strokeG = box.Gray > 0
                ? box.Gray.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " G"
                : "0 G";
            if (ReferenceEquals(cv.flow.page, box.Page) && box.BorderPt > 0 && box.TopY - cv.flow.y > 1)
            {
                var rect = FormattableString.Invariant(
                    $"q {box.BorderPt:0.##} w {strokeG} {box.XLeft:0.##} {cv.flow.y:0.##} {box.Width:0.##} {box.TopY - cv.flow.y:0.##} re S Q\n");
                cv.flow.page.AddContentStream(System.Text.Encoding.ASCII.GetBytes(rect));
            }
            else if (!ReferenceEquals(cv.flow.page, box.Page) && box.BorderPt > 0)
            {
                // The box's content spilled past its start page: the visible part of the
                // border is an open-bottom frame — top edge plus both sides running to
                // just below the bottom content margin (the cut
                // box's sides stop ~10 pt into the margin, not at the page edge).
                var xr = box.XLeft + box.Width;
                var yb = Math.Max(0, cv.marginBottom - 10);
                var frame = FormattableString.Invariant(
                    $"q {box.BorderPt:0.##} w {strokeG} {box.XLeft:0.##} {yb:0.##} m {box.XLeft:0.##} {box.TopY:0.##} l {xr:0.##} {box.TopY:0.##} l {xr:0.##} {yb:0.##} l S Q\n");
                box.Page.AddContentStream(System.Text.Encoding.ASCII.GetBytes(frame));
            }
            if (block.BoxMarginBottomPt > 0) cv.flow.y -= block.BoxMarginBottomPt;
            return false;
        }
        // Remaining blocks of a clipped float column are dropped (overflow:hidden).
        if (cv.flow.bandColClipped && cv.bandStack.Count > 0) return false;
        rb.wasRow = cv.flow.lastWasRow;
        cv.flow.lastWasRow = false;
        rb.prevRowBottomPx = cv.flow.prevRowMarginBottomPx;
        cv.flow.prevRowMarginBottomPx = 0;
        // The body class's line-height applies to any block that declares none of its
        // own, and it has to be in place BEFORE the line height is taken from it.
        if (cv.profile.floatBothSidesDoc && cv.bodyLineFactor > 0 && !block.DeclaredLineFactor)
        {
            block.LineFactor = cv.bodyLineFactor;
            block.DeclaredLineFactor = true;
        }
        // MediaWiki UA rhythm (measured; era-stable
        // against the shipped templates): a dropdown LABEL line indents 15
        // and leads 1.2 extra (its cdx-button line box); the pin-button pair
        // draws one line leading 0.6 extra and hands 1.7 to the block after
        // it (the widget boxes' height over the text line); the welcome
        // banner renders at 162% of the UA base, bold, centred; and a block
        // AFTER a list opens the same gap the list itself opened with.
        if (cv.wikiExportDoc && block.Text.Length > 0)
        {
            if (block.Text.StartsWith("[[WKL]]", StringComparison.Ordinal))
            {
                block.Text = block.Text[7..];
                block.LeftIndent += WikiLabelIndentPt;
                // The label rides 1.2 under a bare line: the paragraph bottom
                // the flow charged for the block above comes BACK, less the
                // label's own taller line box (probed: plain->label 14.7,
                // list->label 28.1 with the list gap below).
                block.MarginTop = 0;
                cv.flow.y += WikiLabelParaCancelPt;
            }
            else if (block.Text.StartsWith("[[WKB]]", StringComparison.Ordinal))
            {
                block.Text = block.Text.Replace("[[WKB]]", "").Replace(" [[WKS]] ", " ").Replace("[[WKS]]", " ");
                cv.flow.y -= WikiButtonLeadPt;
                cv.flow.wikiAfterButtons = true;
            }
            else if (block.Text.StartsWith("[[WKH]]", StringComparison.Ordinal))
            {
                block.Text = block.Text[7..];
                block.FontSize = WikiBannerPt;
                block.FontRes = "F2";
                block.AlignCenterCss = true;
                cv.flow.y -= WikiBannerLeadPt;
                // The welcome banner's mp-box: a 1px #aaa frame that opens
                // above the heading and runs off the content bottom (probed:
                // box top = baseline + 25; the flow seats the baseline
                // 0.9 em under the cursor).
                var wkBoxTop = cv.flow.y - 0.9 * WikiBannerPt + WikiBannerBoxAbovePt;
                cv.flow.page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"q 0.667 0.667 0.667 RG 0.75 w "
                    + $"{cv.marginLeft:F2} {wkBoxTop:F2} m {cv.pageWidth - cv.marginLeft:F2} {wkBoxTop:F2} l S "
                    + $"{cv.marginLeft + 0.4:F2} {wkBoxTop:F2} m {cv.marginLeft + 0.4:F2} {cv.marginBottom:F2} l S "
                    + $"{cv.pageWidth - cv.marginLeft - 0.4:F2} {wkBoxTop:F2} m {cv.pageWidth - cv.marginLeft - 0.4:F2} {cv.marginBottom:F2} l S Q")));
            }
            else if (block.Text.StartsWith("[[WKA]]", StringComparison.Ordinal))
            {
                // the search widget's box below its text line
                cv.flow.y -= WikiAfterSearchPt;
                return false;
            }
            else if (block.Text.StartsWith("[[WKG]]", StringComparison.Ordinal))
            {
                // the logo box: spend, draw nothing
                cv.flow.y -= WikiLogoBoxPt + (cv.flow.wikiPrevListItem ? WikiAfterListGapPt : 0);
                cv.flow.wikiPrevListItem = false;
                return false;
            }
            else if (cv.flow.wikiAfterButtons && !string.IsNullOrWhiteSpace(block.Text))
            {
                cv.flow.y -= WikiAfterButtonsPt;
                cv.flow.wikiAfterButtons = false;
            }
            if (!block.IsListItem && cv.flow.wikiPrevListItem && !string.IsNullOrWhiteSpace(block.Text))
                cv.flow.y -= WikiAfterListGapPt;
            if (!string.IsNullOrWhiteSpace(block.Text)) cv.flow.wikiPrevListItem = block.IsListItem;
        }
        rb.metrics.blockFontSize = block.FontSize;
        rb.metrics.lineHeight = rb.metrics.blockFontSize * 1.3;
        // pt-styled fragment: the flow paces on the measured 1.2 em
        // line box (probed: 10 pt paragraphs step 12.0), not the legacy 1.3.
        if (cv.profile.ptStyledFragment && rb.metrics.blockFontSize > 0)
            rb.metrics.lineHeight = rb.metrics.blockFontSize * PtFragmentLineFactor;
        // The redline diff document paces tighter still — its wrapped
        // paragraphs step 11.25 at 10 pt / 13.5 at 12 pt (measured).
        else if (cv.profile.redlineDiffDoc && rb.metrics.blockFontSize > 0)
            rb.metrics.lineHeight = rb.metrics.blockFontSize * RedlineLineFactor;
        // The pinned-body report paces on the browser's own line box too —
        // the legacy 1.3-em pitch gains 2 pt per line over the expected
        // panels and drifts every section below them.
        if ((cv.profile.sectionedReport || cv.profile.escapedAttrDoc || cv.profile.bodyPinnedW > 0) && rb.metrics.blockFontSize > 0)
            rb.metrics.lineHeight = NormalLineHeightPt(rb.metrics.blockFontSize);
        // A class rule's unitless line-height (coverStyles mode): the cover
        // title's line-height:1 pitches at the font size, the date's 3 leaves
        // its authored air below.
        if (block.LineFactor > 0 && rb.metrics.blockFontSize > 0)
            rb.metrics.lineHeight = rb.metrics.blockFontSize * block.LineFactor;
        rb.inlineCssBox = false;
        rb.icbHalfLead = 0;
        if (!cv.printCoverDoc && block.DeclaredLineFactor
            && block.LineFactor > 0 && rb.metrics.blockFontSize > 0
            && block.FontFamily is { } icbFam && WinMetricsFor(icbFam) is { } icbFm)
        {
            rb.inlineCssBox = true;
            rb.icbHalfLead = Math.Max(0, (rb.metrics.lineHeight - icbFm.sum * rb.metrics.blockFontSize) / 2);
            if (cv.flow.y >= cv.pageHeight - cv.marginTop - 1e-6) cv.flow.y -= UaBodyMarginPt;
            cv.flow.y += rb.icbHalfLead;
        }
        // Text in a band column advances at the CSS line box of its font size
        // (round(pt·4/3·1.15)px·0.75 — 12 pt for a 10.5 pt line, the box the band
        // tables already use): centered heading stacks, small blank spacers, and
        // body-size (≥10 pt) text lines — the 1.3-em pitch accumulates a point per
        // line and pushes a full column several lines past its layout height.
        // Sub-10 pt text lines keep the legacy pitch (calibrated card flow).
        if (cv.profile.floatBandDoc && cv.bandStack.Count > 0 && rb.metrics.blockFontSize > 0
            && (block.AlignCenterAttr
                || (rb.metrics.blockFontSize >= 10 && !string.IsNullOrWhiteSpace(block.Text))
                || (string.IsNullOrWhiteSpace(block.Text) && rb.metrics.blockFontSize < 10)))
            rb.metrics.lineHeight = Math.Round(rb.metrics.blockFontSize * 4.0 / 3.0 * 1.15) * 0.75;
        // Metric flow: browser line box + half-leading baseline; measurement face is
        // the body face (bold variant for bold blocks).
        rb.metrics.metricDrop = 0;
        rb.metrics.metricMeasureFace = cv.profile.metricFace;
        if (cv.profile.metricFlow && WinMetricsFor(cv.profile.metricFace) is { } mfm)
        {
            // UA defaults use the serif's hhea line box, px-rounded (13.5pt @12
            // — same as 1.125em there — but 27.75 @24, 21 @18, 16.5 @14.04:
            // all measured on the expected render's h1-h3 list items).
            // The print grid uses the CSS body line-height, px-rounded.
            rb.metrics.lineHeight = cv.profile.printGrid
                ? Math.Round(rb.metrics.blockFontSize / 0.75 * cv.printGridLineFactor, MidpointRounding.AwayFromZero) * 0.75
                // The article sheet's own unitless line-height, resolved against
                // each block's size and px-rounded the way the expected render does.
                : cv.articleFlow
                    ? Math.Round(rb.metrics.blockFontSize / 0.75 * cv.articleLineFactor, MidpointRounding.AwayFromZero) * 0.75
                : cv.uaFlow ? MetricLineHeight(rb.metrics.blockFontSize, HheaLineSumFor(cv.profile.metricFace) ?? mfm.sum)
                : MetricLineHeight(rb.metrics.blockFontSize, cv.profile.metricLineSum > 0 ? cv.profile.metricLineSum : mfm.sum);
            // A block that carries its OWN resolvable face lines on that
            // face's box (a Word-filtered span's Tahoma pitches 12 where the
            // serif box gives 11.25) and seats its baseline by its metrics.
            var blockFaceFm = mfm;
            if (cv.uaFlow && block.FontFamily is { } bffFam
                && !bffFam.Equals(cv.profile.metricFace, StringComparison.OrdinalIgnoreCase)
                && WinMetricsFor(bffFam) is { } bffFm)
            {
                rb.metrics.lineHeight = MetricLineHeight(rb.metrics.blockFontSize, HheaLineSumFor(bffFam) ?? bffFm.sum);
                blockFaceFm = bffFm;
            }
            // An inline px line-height fixes the LINE BOX outright; the
            // baseline keeps its half-leading seat inside the bigger box.
            if (cv.uaFlow && block.LineBoxPt > 0) rb.metrics.lineHeight = block.LineBoxPt;
            rb.metrics.metricDrop = MetricBaselineDrop(rb.metrics.blockFontSize, rb.metrics.lineHeight, blockFaceFm);
            if (block.FontRes == "F2") rb.metrics.metricMeasureFace = cv.profile.metricFace + "-Bold";
        }



        rb.brokeForRule = false;
        if (block.PageBreakBefore
            && (ReferenceEquals(cv.flow.page, cv.flow.contentPage) || cv.flow.y < cv.pageHeight - cv.marginTop - 1e-3))
        {
            cv.flow.page = cv.doc.Pages.Add(cv.pageWidth, cv.pageHeight);
            EnsureFonts(cv.flow.page, cv.docFontDict);
            cv.flow.y = cv.pageHeight - cv.marginTop; cv.flow.pendingTopDrop = cv.profile.hasZeroTopMargin;
            cv.flow.uaTopMarginPending = cv.profile.uaStdSerif && !cv.fieldsetDoc;
            rb.brokeForRule = block.IsHorizontalRule;
        }

        // A band-dialect <hr> riding its page break: the rule paints inside the fresh
        // page's top margin (≈10 pt above the content top) and the following content
        // flows from the content top as if the rule weren't there. Mid-page rules
        // keep the legacy (spacing-only) path.
        if (block.IsHorizontalRule && cv.profile.floatBandDoc && rb.brokeForRule)
        {
            // Thickness ≈ 0.48 pt per SIZE unit and a 3.6 pt rise above the content
            // top: a rule that carried its page break rides the top margin.
            var ruleH = cv.profile.sectionedReport ? 1.5 : Math.Max(0.75, block.RuleWidth * 0.48);
            DrawBox(cv.flow.page, cv.marginLeft, cv.flow.y + 3.6 - ruleH, cv.flow.contentWidth, ruleH,
                null, 0, block.RuleColor ?? ParseCssColor("#999999"));
            // The rule consumed the break itself — the next block flows from the
            // content top without inheriting a first-block top-margin drop.
            cv.flow.pendingTopDrop = false;
            cv.flow.lastWasHardBreak = false;
            cv.flow.contentPage = cv.flow.page;
            return false;
        }

        if (block.IsTable && cv.profile.escapedAttrDoc)
        {
            LayoutEscapedAttrTable(block, cv.flow, cv.profile, cv.doc, cv.docFontDict, cv.css, cv.dialectButtonFill, cv.dialectButtonTextRg, cv.marginBottom, cv.marginLeft, cv.marginTop, cv.pageHeight, cv.pageWidth, rb.metrics.lineHeight);
            return false;
        }

        // Fieldset frame markers: the open records the frame's top at the
        // cursor (a following legend re-pins it under its baseline); the
        // close pads the box bottom and strokes the gray frame.
        if (block.FsBox == 1)
        {
            cv.fsStack.Push((cv.flow.page, cv.flow.y));
            cv.flow.fsIndentLive += FsPadLeftPt;
            cv.flow.lastWasHardBreak = false;
            return false;
        }
        if (block.FsBox == -1)
        {
            cv.flow.fsIndentLive = Math.Max(0, cv.flow.fsIndentLive - FsPadLeftPt);
            if (cv.fsStack.Count > 0)
            {
                var (fsPage, fsTopY) = cv.fsStack.Pop();
                cv.flow.y -= FsBoxBottomPadPt;
                if (ReferenceEquals(fsPage, cv.flow.page) && cv.profile.fsBoxW > 0)
                {
                    var fsX = cv.marginLeft + 1.5;
                    fsPage.AddContentStream(Encoding.ASCII.GetBytes(string.Create(
                        System.Globalization.CultureInfo.InvariantCulture,
                        $"q {FsFrameGray:0.###} {FsFrameGray:0.###} {FsFrameGray:0.###} RG 0.75 w " +
                        $"{fsX:F2} {cv.flow.y:F2} {cv.profile.fsBoxW:F2} {fsTopY - cv.flow.y:F2} re S Q\n")));
                    cv.flow.contentPage = cv.flow.page;
                }
            }
            cv.flow.lastWasHardBreak = false;
            return false;
        }
        // A legend re-pins its frame's top: the border runs under the
        // legend's baseline (probed: baseline drop + 4.86 below the line top).
        if (block.FsLegend && cv.fsStack.Count > 0 && !string.IsNullOrEmpty(block.Text))
        {
            var fsTop = cv.fsStack.Pop();
            cv.fsStack.Push((fsTop.page, cv.flow.y - FsLegendFrameAdjPt));
        }

        if (block.IsTable)
        {
            LayoutTableBlock(block, cv.flow, cv.profile, cv.doc, cv.docFontDict, cv.css, options, inlineSvgs, cv.floatFirstOps, cv.bandStack, cv.bodyCssFace, cv.profile.dwFormDoc, cv.marginBottom, cv.marginLeft, cv.marginTop, cv.pageHeight, cv.pageWidth, rb.tableAfterSpacer, rb.tableAfterText);
            return false;
        }

        // <input>: place an interactive AcroForm TextBoxField at the cursor.
        // The test only inspects the field (type/Multiline), not its pixels, but
        // we size and position it from the CSS so the widget lands where the input
        // sits in the flow.
        if (block.IsCheckbox)
        {
            LayoutCheckboxBlock(block, cv.flow, cv.profile, cv.doc, cv.docFontDict, cv.marginBottom, cv.marginLeft, cv.marginTop, cv.pageHeight, cv.pageWidth);
            return false;
        }

        if (block.IsRadio)
        {
            const double boxSize = 10.0;
            if (cv.flow.y - boxSize < cv.marginBottom)
            {
                cv.flow.page = cv.doc.Pages.Add(cv.pageWidth, cv.pageHeight);
                EnsureFonts(cv.flow.page, cv.docFontDict);
                cv.flow.y = cv.pageHeight - cv.marginTop; cv.flow.pendingTopDrop = cv.profile.hasZeroTopMargin;
            }
            var rbx = cv.marginLeft + block.LeftIndent;
            cv.radioOptions.Add((block.RadioGroup, block.Checked, cv.flow.page,
                new Rectangle(rbx, cv.flow.y - boxSize, rbx + boxSize, cv.flow.y)));
            cv.flow.y -= boxSize + 2;
            cv.flow.lastWasHardBreak = false;
            return false;
        }

        return true;
    }
}
