using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The fieldset frame markers: the open records the frame's top and indents; the close pads the box bottom and strokes the frame.</summary>
    private static bool? LayoutFieldsetMarker(ConvertState cv, Block block)
    {
        if (block.FrameW > 0) return LayoutFramedDivMarker(cv, block);
        if (block.FsBox == 1)
        {
            cv.fsStack.Push((cv.flow.page, cv.flow.y, 0, 0));
            cv.flow.fsIndentLive += FsPadLeftPt;
            cv.flow.lastWasHardBreak = false;
            return false;
        }
        if (block.FsBox == -1)
        {
            cv.flow.fsIndentLive = Math.Max(0, cv.flow.fsIndentLive - FsPadLeftPt);
            if (cv.fsStack.Count > 0)
            {
                var (fsPage, fsTopY, fsGapX0, fsGapX1) = cv.fsStack.Pop();
                cv.flow.y -= cv.uaFieldsetBoxes ? UaFieldsetBottomPadEm * DefaultBodyFontPt + UaFieldsetStrokePt : FsBoxBottomPadPt;
                if (cv.uaFieldsetBoxes && ReferenceEquals(fsPage, cv.flow.page) && cv.profile.fsBoxW > 0)
                    DrawUaFieldsetFrame(cv, fsPage, fsTopY, fsGapX0, fsGapX1);
                else if (ReferenceEquals(fsPage, cv.flow.page) && cv.profile.fsBoxW > 0)
                {
                    var fsX = cv.marginLeft + 1.5;
                    fsPage.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        $"q {FsFrameGray:0.###} {FsFrameGray:0.###} {FsFrameGray:0.###} RG 0.75 w " +
                        $"{fsX:F2} {cv.flow.y:F2} {cv.profile.fsBoxW:F2} {fsTopY - cv.flow.y:F2} re S Q\n")));
                    cv.flow.contentPage = cv.flow.page;
                }
            }
            cv.flow.lastWasHardBreak = false;
            return false;
        }
        return false;
    }

    /// <summary>A container background box: the open marker records its top at the flow cursor,
    /// the close fills from that top down to the cursor. The fill is PREPENDED to the page so the
    /// blocks it brackets draw over it, which is what a background is; it spans the width the
    /// container declared, falling back to the content box when it declared none.</summary>
    private static bool? LayoutBackgroundSpanMarker(ConvertState cv, Block block)
    {
        if (block.BgSpan == 1)
        {
            if (block.BackgroundColor is { } fill)
            {
                if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_BGSPAN") == "1")
                    Console.Error.WriteLine($"[bgspan] RENDER OPEN flow.y={cv.flow.y:0.##} rasterTop={cv.pageHeight - cv.flow.y:0.##} pageH={cv.pageHeight:0.##} mTop={cv.marginTop:0.##}");
                cv.bgSpanStack.Push((cv.flow.page, cv.flow.y, fill, block.BgBoxWidthPt,
                    block.BgSpanWidthFrac, block.BgPadTopPt, block.BgPadBottomPt, block.BgPadLeftPt));
            }
            cv.flow.lastWasHardBreak = false;
            return false;
        }
        if (cv.bgSpanStack.Count > 0)
        {
            var (bgPage, bgTop, bgFill, bgW, bgFrac, bgPadT, bgPadB, bgPadL) = cv.bgSpanStack.Pop();
            if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_BGSPAN") == "1")
                Console.Error.WriteLine($"[bgspan] RENDER CLOSE flow.y={cv.flow.y:0.##} rasterBottom={cv.pageHeight - cv.flow.y:0.##} prevMB={cv.flow.prevFlowMarginBottom:0.##} uaPrevMB={cv.flow.uaPrevMarginBottom:0.##} uaTopPending={cv.flow.uaTopMarginPending}");
            // A box the flow carried onto another page is not one rectangle any more; the
            // per-page split is not modelled, so it is left unpainted rather than drawn wrong.
            if (ReferenceEquals(bgPage, cv.flow.page) && bgTop > cv.flow.y)
            {
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                var bgX = cv.marginLeft;
                // A percentage resolves against the BODY's content box - the sheet inside its
                // page margins, inset by the UA body margin on BOTH sides - which is the box the
                // container is a child of. A point width is taken as declared; with neither, the
                // box spans the content width.
                // The fill covers the PADDING box: the declared content width plus the padding
                // the container keeps on each side, and the same padding above and below the
                // children it wraps.
                // The percentage is of the BODY's content box: the sheet inside the content
                // origin on BOTH sides. cv.marginLeft already carries the UA body inset, so the
                // box is the page less twice that - written from the page rather than from
                // cv.marginRight, which the widening branches leave holding their own band.
                var bgBase = cv.pageWidth - 2 * cv.marginLeft;
                var bgWidth = (bgFrac > 0 ? bgFrac * bgBase
                    : bgW > 0 ? bgW : cv.pageWidth - cv.marginLeft - cv.marginRight) + bgPadL;
                bgPage.PrependContentStream(Encoding.ASCII.GetBytes(Compat.Format(inv,
                    $"q {bgFill.R / 255.0:F5} {bgFill.G / 255.0:F5} {bgFill.B / 255.0:F5} rg " +
                    $"{bgX:F2} {cv.flow.y - bgPadB:F2} {bgWidth:F2} {bgTop - cv.flow.y + bgPadT + bgPadB:F2} re f Q\n")));
                cv.flow.contentPage = cv.flow.page;
            }
        }
        cv.flow.lastWasHardBreak = false;
        return false;
    }

    /// <summary>A framed wrapper div: the open spends its border-top and stands the tables inside its
    /// border at its declared content width (centred there when its align says so); the close
    /// spends the border-bottom and strokes the box - its declared width plus the borders, at the
    /// content left (measured on the valuation report: 96..582.75 round a 645 px div).</summary>
    private static bool? LayoutFramedDivMarker(ConvertState cv, Block block)
    {
        if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_BLOCKS") == "1")
            Console.WriteLine($"[frame] box={block.FsBox} w={block.FrameW} col={block.FrameCol} contentW={block.FrameBoxW} live={cv.flow.frameContentW} stack={cv.fsStack.Count} y={cv.flow.y:0.##} cw={cv.flow.contentWidth:0.##}");
        if (block.FsBox == 1 && cv.flow.frameContentW > 0) return false;
        if (block.FsBox == 1)
        {
            // (the border width rides the stack for the close, whose marker knows none)
            cv.fsStack.Push((cv.flow.page, cv.flow.y, block.FrameW, 0));
            cv.flow.y -= block.FrameW;
            cv.flow.frameInset = block.FrameW;
            // a class-bordered frame (the quirks chain sheet) that declares no width spans the content
            // box: its content is the box less both borders; an inline-bordered div with a width the
            // parser could not read keeps standing for no frame at all, as it always did
            cv.flow.frameContentW = block.FrameBoxW > 0 ? block.FrameBoxW
                : _quirksChainSheet ? cv.flow.contentWidth - 2 * block.FrameW : 0;
            cv.flow.frameCentred = block.FrameCentred;
            cv.flow.frameCol = block.FrameCol;
            cv.flow.lastWasHardBreak = false;
            return false;
        }
        var inset = cv.flow.frameInset;
        var contentW = cv.flow.frameContentW;
        var frameCol = block.FrameCol ?? cv.flow.frameCol;
        cv.flow.frameInset = 0; cv.flow.frameContentW = 0; cv.flow.frameCentred = false; cv.flow.frameCol = null;
        if (cv.fsStack.Count == 0) return false;
        var (fsPage, fsTopY, frameW, _) = cv.fsStack.Pop();
        cv.flow.y -= frameW;
        if (ReferenceEquals(fsPage, cv.flow.page) && contentW > 0)
        {
            var col = frameCol ?? Color.Black;
            var half = frameW / 2;
            var outerW = contentW + 2 * inset;
            fsPage.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                $"q {col.R / 255.0:0.###} {col.G / 255.0:0.###} {col.B / 255.0:0.###} RG {frameW:0.##} w " +
                $"{cv.marginLeft + half:F2} {cv.flow.y + half:F2} {outerW - frameW:F2} {fsTopY - cv.flow.y - frameW:F2} re S Q\n")));
            cv.flow.contentPage = cv.flow.page;
        }
        cv.flow.lastWasHardBreak = false;
        return false;
    }

    /// <summary>The UA fieldset frame: four 0.75 pt #808080 lines just inside the box, the top one broken around the legend.</summary>
    private static void DrawUaFieldsetFrame(ConvertState cv, Page fsPage, double fsTopY, double gapX0, double gapX1)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var x0 = cv.marginLeft + UaFieldsetSideInsetPt;
        var x1 = x0 + cv.profile.fsBoxW;
        var h = UaFieldsetStrokePt / 2;
        var yTop = fsTopY;
        var yBot = cv.flow.y;
        var sb = new StringBuilder(Compat.Format(inv, $"q {FsFrameGray:0.###} {FsFrameGray:0.###} {FsFrameGray:0.###} RG {UaFieldsetStrokePt:0.##} w "));
        if (gapX1 > gapX0)
            sb.Append(Compat.Format(inv, $"{x0:F2} {yTop - h:F2} m {gapX0:F2} {yTop - h:F2} l {gapX1:F2} {yTop - h:F2} m {x1:F2} {yTop - h:F2} l S "));
        else
            sb.Append(Compat.Format(inv, $"{x0:F2} {yTop - h:F2} m {x1:F2} {yTop - h:F2} l S "));
        sb.Append(Compat.Format(inv, $"{x0:F2} {yBot + h:F2} m {x1:F2} {yBot + h:F2} l S "));
        sb.Append(Compat.Format(inv, $"{x0 + h:F2} {yTop:F2} m {x0 + h:F2} {yBot:F2} l S "));
        sb.Append(Compat.Format(inv, $"{x1 - h:F2} {yTop:F2} m {x1 - h:F2} {yBot:F2} l S Q\n"));
        fsPage.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
        cv.flow.contentPage = cv.flow.page;
    }

    /// <summary>The fieldset frame, legend, table, checkbox and radio blocks lay themselves out and leave the text path.</summary>
    private static bool? LayoutFormAndTableBlocks(ConvertState cv, RenderBlockState rb, HtmlLoadOptions? options, List<byte[]> inlineSvgs, Block block)
    {

        // Fieldset frame markers: the open records the frame's top at the
        // cursor (a following legend re-pins it under its baseline); the
        // close pads the box bottom and strokes the gray frame.
        if (block.FsBox != 0) return LayoutFieldsetMarker(cv, block);
        // Container background markers: the open records the box top at the cursor, the close
        // fills from there to the cursor. Prepended, so the content it wraps draws over it.
        if (block.BgSpan != 0) return LayoutBackgroundSpanMarker(cv, block);
        // A legend re-pins its frame's top: the border runs under the
        // legend's baseline (probed: baseline drop + 4.86 below the line top).
        if (block.FsLegend && cv.fsStack.Count > 0 && !string.IsNullOrEmpty(block.Text))
        {
            var fsTop = cv.fsStack.Pop();
            if (cv.uaFieldsetBoxes)
            {
                // the UA frame's top line crosses the legend line at half its box, and stops
                // around the legend text: content left .. text right + 2px
                var lgdFace = cv.profile.metricFace + (block.FontRes == "F2" || block.EmBold ? " Bold" : "");
                var lgdX0 = cv.marginLeft + FsPadLeftPt;
                var lgdX1 = lgdX0 + UaFieldsetLegendPadPt + MeasureFaceText(lgdFace, block.Text.Trim(), block.FontSize) + UaFieldsetLegendPadPt;
                cv.fsStack.Push((fsTop.page, cv.flow.y - Table.CssLineBoxPt(block.FontSize) / 2, lgdX0, lgdX1));
            }
            else
                cv.fsStack.Push((fsTop.page, cv.flow.y - FsLegendFrameAdjPt, 0, 0));
        }

        if (block.IsTable)
        {
            // The sheet-typography box flow: the table's sheet margins collapse with the box
            // above (never at the page top) and stand below it.
            // (a UA-flow table carries a margin only when a bare <p> opened it - see TableSegmentOne)
            // (…and the pt form's own inline margin-top stands above its grid)
            var tableBoxMargins = (cv.profile.sheetBoxFlow || ((cv.profile.uaStdSerif || cv.profile.ptFormDoc) && block.MarginTop > 0)) && !string.IsNullOrEmpty(block.TableHtml);
            if (tableBoxMargins && cv.flow.y < cv.pageHeight - cv.marginTop - 1e-3)
            {
                // Still inside the body's own opening margin (nothing else spent yet): the
                // table's top margin COLLAPSES with it through the border-less body rather
                // than standing under it - the whole gap is the larger of the two.
                var spentFromPageTop = cv.pageHeight - cv.marginTop - cv.flow.y;
                cv.flow.y -= cv.profile.uaStdSerif && spentFromPageTop <= UaBodyMarginPt + 1e-3
                    ? Math.Max(0, block.MarginTop - spentFromPageTop)
                    : Math.Max(0, block.MarginTop - rb.uaPrevMB);
            }
            // A table after a UA heading or list stands that box's closing gap too (the
            // table carries no top margin of its own to collapse it against).
            if (cv.profile.uaStdSerif && rb.uaClosingGap > 0 && cv.flow.y < cv.pageHeight - cv.marginTop - 1e-3)
                cv.flow.y -= Math.Max(0, rb.uaClosingGap - rb.uaPrevMB);
            LayoutTableBlock(block, cv.flow, cv.profile, cv.doc, cv.docFontDict, cv.css, options, inlineSvgs, cv.floatFirstOps, cv.bandStack, cv.bodyCssFace, cv.profile.dwFormDoc, cv.marginBottom, cv.marginLeft, cv.marginTop, cv.pageHeight, cv.pageWidth, rb.tableAfterSpacer, rb.tableAfterText);
            if (tableBoxMargins)
            {
                cv.flow.y -= block.MarginBottom;
                cv.flow.uaPrevMarginBottom = block.MarginBottom;
            }
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

        return null;
    }

    /// <summary>A declared page break opens a new page; a float-band rule and an escaped-attribute table draw themselves and leave.</summary>
    private static bool? BreakPageBeforeBlock(ConvertState cv, RenderBlockState rb, Block block)
    {
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
        return null;
    }

    /// <summary>The block's font size, line pitch, extra leads, inline-css box and metric-flow face.</summary>
    private static bool? ResolveBlockLineMetrics(ConvertState cv, RenderBlockState rb, Block block)
    {
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
        if ((cv.profile.sectionedReport || cv.profile.escapedAttrDoc || cv.profile.bodyPinnedW > 0 || (cv.profile.uaGridSheet)) && rb.metrics.blockFontSize > 0)
            rb.metrics.lineHeight = NormalLineHeightPt(rb.metrics.blockFontSize);
        // A class rule's unitless line-height (coverStyles mode): the cover
        // title's line-height:1 pitches at the font size, the date's 3 leaves
        // its authored air below.
        // A Word mail paces on its face's normal line (Calibri 11 steps 13.4, Times 12 steps 13.8):
        // the legacy 1.3 em opened every header row and blank paragraph a point too far.
        if (cv.profile.wordMailDoc && rb.metrics.blockFontSize > 0 && block.FontFamily is { } wmFam
            && WinMetricsFor(wmFam) is { } wmFm)
            rb.metrics.lineHeight = MetricLineHeight(rb.metrics.blockFontSize, HheaLineSumFor(wmFam) ?? wmFm.sum);
        if (block.LineFactor > 0 && rb.metrics.blockFontSize > 0)
            rb.metrics.lineHeight = rb.metrics.blockFontSize * block.LineFactor;
        SeatWordMailSizeChange(cv, rb, block);
        // …and the sheet's line-height on the calibrated flow (the UA flow reads its own
        // below): a factor of the block's size, or the absolute box - a 21 px body
        // line-height paces every line at 15.75, a `p { line-height: 1.6 }` at 1.6 em.
        if (cv.profile.sheetTypographyDoc)
        {
            if (block.SheetLineFactor > 0 && rb.metrics.blockFontSize > 0)
                rb.metrics.lineHeight = rb.metrics.blockFontSize * block.SheetLineFactor;
            if (block.LineBoxPt > 0) rb.metrics.lineHeight = block.LineBoxPt;
        }
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
            // (…and a percent line-height's factor paces the block's lines at its own size)
            else if (cv.uaFlow && block.UaLineFactor > 0) rb.metrics.lineHeight = rb.metrics.blockFontSize * block.UaLineFactor;
            rb.metrics.metricDrop = MetricBaselineDrop(rb.metrics.blockFontSize, rb.metrics.lineHeight, blockFaceFm);
            if (block.FontRes == "F2") rb.metrics.metricMeasureFace = cv.profile.metricFace + "-Bold";
        }
        return null;
    }

    /// <summary>The gap after a table row, the body line-factor drop and the wiki export's list and heading spacing.</summary>
    private static bool? ApplyRowAndWikiSpacing(ConvertState cv, RenderBlockState rb, Block block)
    {
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
        // (…and the body TAG's own percent line-height, where its attribute is the UA flow's
        //  typography: every block that states none of its own paces on it)
        else if (cv.uaBodyFaceFromAttr && cv.bodyLineFactor > 0 && block.UaLineFactor <= 0)
            block.UaLineFactor = cv.bodyLineFactor;
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
                cv.flow.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(
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
        return null;
    }

    /// <summary>Float bands, float columns, column scopes and boxes open and close on their marker blocks; a clipped column drops the rest.</summary>
    private static bool? OpenAndCloseFloatScopes(ConvertState cv, RenderBlockState rb, Block block)
    {
        rb.uaPrevMB = cv.flow.uaPrevMarginBottom;
        cv.flow.uaPrevMarginBottom = 0;
        rb.uaClosingGap = cv.flow.uaClosingGap;
        cv.flow.uaClosingGap = 0;
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
        if (block.BoxStart || (block.BoxEnd && cv.boxStack.Count > 0))
            return OpenOrCloseBorderBox(cv, block);
        // Remaining blocks of a clipped float column are dropped (overflow:hidden).
        if (cv.flow.bandColClipped && cv.bandStack.Count > 0) return false;
        return null;
    }

    /// <summary>A border box opens on its start marker and closes on its end one: the
    /// open records the frame's corner and holds the flow to the box, the close strokes
    /// the frame and returns the flow to the ambient content box.</summary>
    private static bool? OpenOrCloseBorderBox(ConvertState cv, Block block)
    {
        if (block.BoxStart)
        {
            // A box that declares its own width holds its content to it, and the frame is
            // drawn at that width wherever the ambient content box ends.
            var boxWidth = block.BoxWidthPt > 0 ? block.BoxWidthPt : cv.flow.contentWidth;
            cv.boxStack.Push((cv.marginLeft, cv.flow.y + block.BoxAscentPt, boxWidth, block.BoxBorderPt, cv.flow.page,
                block.BoxBorderGray, block.BoxPadSidePt, cv.marginLeft, cv.flow.contentWidth));
            cv.flow.y -= block.BoxPadTopPt;
            if (block.BoxWidthPt > 0)
            {
                // The border rides outside the declared content box, so the content opens
                // one stroke inside the frame's own top-left corner.
                cv.marginLeft += block.BoxBorderPt;
                cv.flow.y -= block.BoxBorderPt;
                cv.flow.contentWidth = block.BoxWidthPt;
                cv.flow.declaredBoxDepth++;
            }
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
            if (block.BoxWidthPt > 0 && cv.flow.declaredBoxDepth > 0) cv.flow.declaredBoxDepth--;
            cv.flow.y -= block.BoxPadBottomPt;
            if (block.BoxHeightPt > 0 && ReferenceEquals(cv.flow.page, box.Page))
            {
                DrawDeclaredBox(cv, box.XLeft, box.TopY, box.Width, block.BoxHeightPt, box.BorderPt, box.Gray);
                // The declared height is what the box occupies, whatever its content
                // measured: the flow resumes under the frame's outer bottom edge.
                cv.flow.y = box.TopY - (block.BoxHeightPt + 2 * box.BorderPt);
                if (block.BoxMarginBottomPt > 0) cv.flow.y -= block.BoxMarginBottomPt;
                return false;
            }
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
        return null;
    }

    /// <summary>Remember where the block entered, the table/text order flags, and lay out the keep-with-image and SharePoint matrix sections.</summary>
    private static bool? TrackBlockSequence(ConvertState cv, RenderBlockState rb, List<byte[]> inlineSvgs, Block block)
    {
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
        return null;
    }

    /// <summary>A declared height or min-height is a floor the content grows down into: the start marker remembers, the end marker spends.</summary>
    private static bool? ApplyHeightFloors(ConvertState cv, RenderBlockState rb, Block block)
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
                        cv.flow.y = FreshPageTopY(cv.profile, cv.pageHeight, cv.marginTop); cv.flow.pendingTopDrop = cv.profile.hasZeroTopMargin;
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
        return null;
    }

    /// <summary>Word mail: a text block of another size or face than the text block before it seats its first
    /// baseline the browser's way - under the previous line box's bottom by its own half-leading and ascent -
    /// not one previous pitch down (a 2 pt spacer paragraph after a 12 pt one steps 4.6, not 13.5).</summary>
    private static void SeatWordMailSizeChange(ConvertState cv, RenderBlockState rb, Block block)
    {
        // (the sheet-typography flow steps by line boxes and seats every block's first line one
        // ascent side under the previous box already - the Word export takes that seat)
        if (cv.profile.sheetTypographyDoc) return;
        if (!cv.profile.wordMailDoc || !cv.flow.prevBlockWasText || cv.flow.prevFlowLineHeight <= 0
            || cv.flow.prevFlowFontSize <= 0 || rb.metrics.blockFontSize <= 0 || rb.metrics.lineHeight <= 0
            || block.FontFamily is not { } curFam || cv.flow.prevFlowFace is not { } prevFam) return;
        if (Math.Abs(cv.flow.prevFlowFontSize - rb.metrics.blockFontSize) < 0.01
            && string.Equals(prevFam, curFam, StringComparison.OrdinalIgnoreCase)) return;
        if (WinMetricsFor(prevFam) is not { } pm || WinMetricsFor(curFam) is not { } cm) return;
        var prevDescentPart = cv.flow.prevFlowLineHeight - LineAscentPart(cv.flow.prevFlowFontSize, cv.flow.prevFlowLineHeight, pm);
        cv.flow.y += cv.flow.prevFlowLineHeight - prevDescentPart - LineAscentPart(rb.metrics.blockFontSize, rb.metrics.lineHeight, cm);
    }

    /// <summary>The part of a line box above its baseline: half the leading plus the face's ascent.</summary>
    private static double LineAscentPart(double fs, double lineHeight, (double asc, double sum) m)
        => (lineHeight - fs * m.sum) / 2 + fs * m.asc;
}
