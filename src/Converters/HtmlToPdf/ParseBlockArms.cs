using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>One arm of ParseBlocks' token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void HandleBlockClose(ParseBlocksState pb, Token tok, string tag, IReadOnlyDictionary<string, Dictionary<string, string>>? css, bool articleRhythm, bool bodyBoxRhythm, bool browserUa, bool controlBoxes, bool divBandBg, bool inlineEmphasisRuns, bool metricLayout, bool msoParagraphs, bool spanPtTypography, bool uaBlockRhythm, bool uaPMargins)
    {
        // A stray </p> with no open <p> quirks-parses as an EMPTY
        // paragraph: it contributes its UA margin (max-collapsed onto
        // the next block) and must NOT pop the enclosing element's
        // style. Browser-UA flow only.
        if (browserUa && tag.Equals("p", StringComparison.OrdinalIgnoreCase))
        {
            if (pb.pOpenDepth > 0) pb.pOpenDepth--;
            else
            {
                Flush(pb, controlBoxes, uaBlockRhythm, articleRhythm, spanPtTypography, false, pb.styleStack.Peek());
                // Word-filtered idiom: the stray </p> is an EMPTY
                // MsoNormal paragraph — one full base-size line box
                // (the sheet reset its margins to zero), not a
                // collapsible UA margin.
                if (msoParagraphs)
                    pb.blocks.Add(new Block
                    {
                        Text = "", IsHardBreak = true, IsLineBreak = true,
                        FontSize = 12,
                    });
                else
                    pb.pendingEmptyPMarginPt = Math.Max(pb.pendingEmptyPMarginPt,
                        UaBlockMarginEm * pb.styleStack.Peek().FontSize);
                return;
            }
        }
        // Metric flow: a closed body-level <p> leaves its UA 1.12 em
        // bottom margin to collapse onto whatever follows it — carried
        // both as the next block's pending top margin and on the flushed
        // block itself (a following TABLE reads only the latter).
        else if (metricLayout && uaPMargins && tag.Equals("p", StringComparison.OrdinalIgnoreCase))
        {
            // only the flushed block's bottom margin — the NEXT
            // paragraph's own open-margin covers text-to-text collapse;
            // a following TABLE reads MarginBottom alone. The margin is
            // 1.12 em of the p's INHERITED size (the block style's own
            // FontSize carries the legacy flow default, not the CSS one).
            var pMb = UaBlockMarginEm * (pb.styleStack.Peek().ParentFontSize > 0
                ? pb.styleStack.Peek().ParentFontSize : pb.styleStack.Peek().FontSize);
            Flush(pb, controlBoxes, uaBlockRhythm, articleRhythm, spanPtTypography, false, pb.styleStack.Peek());
            if (pb.blocks.Count > 0)
                pb.blocks[^1].MarginBottom = Math.Max(pb.blocks[^1].MarginBottom, pMb);
        }
        if (pb.trackBoldRuns && tag.ToLowerInvariant() is "b" or "strong")
        {
            if (pb.inlineBoldDepth > 0 && --pb.inlineBoldDepth == 0
                && pb.currentText.Length > pb.inlineBoldStart)
                pb.rawBolds.Add((pb.inlineBoldStart, pb.currentText.Length));
            // The browser-UA flow draws bold purely as a run; the in-page
            // fragment flow keeps the historical whole-block promotion as
            // its fallback, so let the close reach the generic handling.
            if (browserUa) return;
        }
        if ((inlineEmphasisRuns || browserUa)
            && tag.Equals("u", StringComparison.OrdinalIgnoreCase))
        {
            if (pb.inlineUnderDepth > 0 && --pb.inlineUnderDepth == 0
                && pb.currentText.Length > pb.inlineUnderStart)
                pb.rawUnders.Add((pb.inlineUnderStart, pb.currentText.Length));
            return;
        }
        if (pb.trackBoldRuns && tag.ToLowerInvariant() is "i" or "em")
        {
            if (pb.inlineItalicDepth > 0 && --pb.inlineItalicDepth == 0
                && pb.currentText.Length > pb.inlineItalicStart)
                pb.rawItalics.Add((pb.inlineItalicStart, pb.currentText.Length));
            // Browser-UA flow draws italic purely as a run; other flows
            // keep the historical whole-block promotion.
            if (browserUa) return;
        }
        if (tag.Equals("center", StringComparison.OrdinalIgnoreCase))
        {
            Flush(pb, controlBoxes, uaBlockRhythm, articleRhythm, spanPtTypography, false, pb.styleStack.Peek());
            if (pb.centerDepth > 0) pb.centerDepth--;
            return;
        }
        if (tag.Equals("textarea", StringComparison.OrdinalIgnoreCase))
        {
            pb.inTextarea = false;
            if (pb.textareaBlock is not null)
                pb.textareaBlock.InputValue = CollapseWs(pb.textareaText.ToString());
            pb.textareaBlock = null; pb.textareaText.Clear();
            return;
        }
        if (tag.Equals("select", StringComparison.OrdinalIgnoreCase))
        {
            pb.inSelect = false; pb.inSelectedOption = false;
            var chosen = CollapseWs(pb.selectedText.ToString());
            pb.selectedText.Clear();
            if (pb.curOptionText.Length > 0)
            {
                pb.selectOptions.Add(CollapseWs(pb.curOptionText.ToString()));
                pb.curOptionText.Clear();
            }
            if (controlBoxes)
            {
                // The combo box occupies its own control box on the line: as
                // wide as its widest option in the 10 pt UI face plus the
                // dropdown chrome, the chosen entry typeset inside.
                double maxOpt = 0;
                foreach (var opt in pb.selectOptions)
                {
                    var ow = MeasureStd14("Helvetica", opt, 10);
                    if (ow > maxOpt) maxOpt = ow;
                }
                var st = pb.styleStack.Peek();
                pb.blocks.Add(new Block
                {
                    IsInputField = true,
                    IsSelectBox = true,
                    InputValue = chosen,
                    InputName = string.IsNullOrEmpty(pb.selectName) ? null : pb.selectName,
                    InputWidth = maxOpt + SelectChromePt,
                    InputHeight = SelectBoxHeightPt,
                    InputAdvance = ControlFirstRowAdvancePt,
                    InputDrawValue = true,
                    FontSize = st.FontSize,
                    FontRes = st.FontRes,
                    LeftIndent = st.LeftIndent,
                    InlineRunId = pb.inlineRunId,
                });
                pb.runPrevWasControl = true;
            }
            else if (chosen.Length > 0) { pb.currentText.Append(chosen); Flush(pb, controlBoxes, uaBlockRhythm, articleRhythm, spanPtTypography, true, pb.styleStack.Peek()); }
            pb.selectOptions.Clear();
            pb.selectName = null;
            return;
        }
        if (tag.Equals("option", StringComparison.OrdinalIgnoreCase))
        {
            if (pb.curOptionText.Length > 0)
            {
                pb.selectOptions.Add(CollapseWs(pb.curOptionText.ToString()));
                pb.curOptionText.Clear();
            }
            pb.inSelectedOption = false;
            return;
        }
        if (controlBoxes && tag.Equals("button", StringComparison.OrdinalIgnoreCase))
        {
            pb.inButton = false;
            pb.blocks.Add(new Block
            {
                IsButton = true,
                ButtonCaption = CollapseWs(pb.buttonText.ToString()),
                FontSize = pb.styleStack.Peek().FontSize,
            });
            pb.buttonText.Clear();
            return;
        }
        if (tag.Equals("a", StringComparison.OrdinalIgnoreCase) && pb.openAnchors.Count > 0)
        {
            var (st, url) = pb.openAnchors.Pop();
            if (pb.currentText.Length > st) pb.rawAnchors.Add((st, pb.currentText.Length, url));
        }
        // An empty <i>/<em> (icon placeholder) reverts its promotion — the
        // text that follows the close is not emphasised.
        if (controlBoxes && tag.ToLowerInvariant() is "i" or "em"
            && pb.italicOpenTextLen >= 0 && pb.currentText.Length == pb.italicOpenTextLen)
        {
            var itop = pb.styleStack.Peek();
            if (itop.FontRes == "F3") itop.FontRes = "F1";
            itop.EmItalic = false;
            pb.italicOpenTextLen = -1;
        }
        // A UA-flow </font> restores the typography its open saved —
        // flushing the styled run it closes first.
        if (browserUa && tag.Equals("font", StringComparison.OrdinalIgnoreCase)
            && pb.uaFontSaves.Count > 0)
        {
            Flush(pb, controlBoxes, uaBlockRhythm, articleRhythm, spanPtTypography, false, pb.styleStack.Peek());
            var (sFs, sFore, sFam) = pb.uaFontSaves.Pop();
            var fTop = pb.styleStack.Peek();
            fTop.FontSize = sFs;
            fTop.ForeColor = sFore;
            fTop.FontFamily = sFam;
        }
        // A block-span's close breaks its line like its open did.
        if (tag.Equals("span", StringComparison.OrdinalIgnoreCase))
        {
            // Close an inline-style underline run opened by this span.
            if (pb.uaUnderSpanDepths.Count > 0 && pb.uaUnderSpanDepths.Peek() == pb.spanDepth)
            {
                pb.uaUnderSpanDepths.Pop();
                if (pb.inlineUnderDepth > 0 && --pb.inlineUnderDepth == 0
                    && pb.currentText.Length > pb.inlineUnderStart)
                    pb.rawUnders.Add((pb.inlineUnderStart, pb.currentText.Length));
            }
            // …and the bold / italic runs its inline style opened.
            if (pb.styleBoldSpanDepths.Count > 0 && pb.styleBoldSpanDepths.Peek() == pb.spanDepth)
            {
                pb.styleBoldSpanDepths.Pop();
                if (pb.inlineBoldDepth > 0 && --pb.inlineBoldDepth == 0
                    && pb.currentText.Length > pb.inlineBoldStart)
                    pb.rawBolds.Add((pb.inlineBoldStart, pb.currentText.Length));
            }
            if (pb.styleItalicSpanDepths.Count > 0 && pb.styleItalicSpanDepths.Peek() == pb.spanDepth)
            {
                pb.styleItalicSpanDepths.Pop();
                if (pb.inlineItalicDepth > 0 && --pb.inlineItalicDepth == 0
                    && pb.currentText.Length > pb.inlineItalicStart)
                    pb.rawItalics.Add((pb.inlineItalicStart, pb.currentText.Length));
            }
            if (pb.blockSpanDepths.Count > 0 && pb.blockSpanDepths.Peek() == pb.spanDepth)
            {
                pb.blockSpanDepths.Pop();
                Flush(pb, controlBoxes, uaBlockRhythm, articleRhythm, spanPtTypography, false, pb.styleStack.Peek());
            }
            // The ledger's absolute column closes: its text flushes as its
            // own block at the column x. position:absolute anchors at the
            // page margin box — one UA body margin OUTSIDE the flow's
            // content origin (probed: label 96 + margin-left, value
            // 90 + left, on one line).
            if (pb.absSpanLeftPt >= 0)
            {
                var lgTop = pb.styleStack.Peek();
                var lgSavLi = lgTop.LeftIndent;
                var lgSavTi = lgTop.TextInsetPt;
                var lgBefore = pb.blocks.Count;
                lgTop.LeftIndent = Math.Max(0, pb.absSpanLeftPt - UaBodyMarginPt);
                lgTop.TextInsetPt = 0;
                Flush(pb, controlBoxes, uaBlockRhythm, articleRhythm, spanPtTypography, false, lgTop);
                lgTop.LeftIndent = lgSavLi;
                lgTop.TextInsetPt = lgSavTi;
                // An EMPTY column emitted nothing — the label must advance
                // the row itself, or the next row overprints it.
                if (pb.blocks.Count == lgBefore && pb.absSpanLabelIdx >= 0
                    && pb.absSpanLabelIdx < pb.blocks.Count)
                    pb.blocks[pb.absSpanLabelIdx].NoAdvanceY = false;
                pb.absSpanLeftPt = -1;
                pb.absSpanLabelIdx = -1;
            }
            // The title column closes: its text is its own run that gives the
            // row back — the value that follows seats at the column's edge.
            if (pb.titleColSpanDepth == pb.spanDepth && pb.openTitleColW > 0)
            {
                Flush(pb, controlBoxes, uaBlockRhythm, articleRhythm, spanPtTypography, false, pb.styleStack.Peek());
                if (pb.blocks.Count > 0 && !pb.blocks[^1].IsHardBreak
                    && pb.blocks[^1].Text.Length > 0)
                {
                    pb.blocks[^1].NoAdvanceY = true;
                    pb.pendingColIndent = pb.openTitleColW;
                }
                pb.openTitleColW = 0;
                pb.titleColSpanDepth = -1;
            }
            // Close this span's colour run and restore the frame's ink.
            while (pb.openDecorRuns.Count > 0 && pb.openDecorRuns.Peek().depth == pb.spanDepth)
            {
                var (_, drs, drk, drc) = pb.openDecorRuns.Pop();
                if (pb.currentText.Length > drs)
                    pb.rawDecorRuns.Add((drs, pb.currentText.Length, drk, drc));
            }
            if (pb.openColorRuns.Count > 0 && pb.openColorRuns.Peek().depth == pb.spanDepth)
            {
                var (_, crs, crc, crPrev) = pb.openColorRuns.Pop();
                if (pb.currentText.Length > crs)
                    pb.rawColorRuns.Add((crs, pb.currentText.Length, crc));
                pb.styleStack.Peek().ForeColor = crPrev;
            }
            if (pb.spanDepth > 0) pb.spanDepth--;
        }
        // An inline div's close is as inline as its open — nothing to pop.
        if (pb.inlineDivDepth > 0 && tag.Equals("div", StringComparison.OrdinalIgnoreCase))
        {
            pb.inlineDivDepth--;
            return;
        }
        if (BlockTags.Contains(tag))
        {
            var popped = pb.styleStack.Count > 1 ? pb.styleStack.Pop() : pb.styleStack.Peek();
            if (pb.divClassStack.Count > 0) pb.divClassStack.RemoveAt(pb.divClassStack.Count - 1);
            // A closing block element leaves its own bottom margin as real
            // space below its last line — the in-page fragment flow reads
            // authored markup, where that margin IS the vertical rhythm.
            var closingMarginBottom = uaBlockRhythm || articleRhythm || bodyBoxRhythm
                || inlineEmphasisRuns
                ? popped.MarginBottom : 0;
            pb.closingElement = true;
            Flush(pb, controlBoxes, uaBlockRhythm, articleRhythm, spanPtTypography, true,popped);
            pb.closingElement = false;
            if (pb.heightFloors.Count > 0 && pb.heightFloors.Peek().Depth == pb.styleStack.Count + 1)
            {
                var fl = pb.heightFloors.Pop();
                popped.HeightFloorDeferred = false;
                if (pb.blocks.Count > fl.Marker + 1)
                    pb.blocks.Add(new Block
                    {
                        Text = "",
                        HeightFloorEnd = true,
                        ExplicitHeight = fl.H,
                        FontSize = popped.FontSize,
                        FontRes = popped.FontRes,
                        LeftIndent = popped.LeftIndent,
                    });
                else
                {
                    // Nothing grew into the floor: an empty sized element is
                    // the plain reserved-space spacer it always was.
                    pb.blocks[fl.Marker].HeightFloorStart = false;
                    pb.blocks[fl.Marker].IsHardBreak = true;
                    pb.blocks[fl.Marker].ExplicitHeight = fl.H;
                }
            }
            // A border-only declared box whose element closed without any
            // block flushing inside it still strokes its box: emit the
            // reserved-height spacer carrying it.
            if (pb.pendingBorderBox is { } cbb && pb.styleStack.Count == pb.pendingBorderBoxDepth - 1)
            {
                pb.blocks.Add(new Block
                {
                    Text = "",
                    IsHardBreak = true,
                    FontSize = popped.FontSize,
                    FontRes = popped.FontRes,
                    LeftIndent = popped.LeftIndent,
                    ExplicitHeight = cbb.h,
                    BorderBoxWPt = cbb.w,
                    BorderRadiusPt = cbb.r,
                    BorderWidth = cbb.bw,
                    BorderColor = cbb.c,
                });
                pb.pendingBorderBox = null;
            }
            // The pinned-body report's authored spacer: an EMPTY div
            // renders as its padding box — the sheet's `div{padding:4px}`
            // gives "<div></div>" 4px above + 4px below of real height.
            if (divBandBg && tag.Equals("div", StringComparison.OrdinalIgnoreCase)
                && pb.emptyDivDepthMark == pb.styleStack.Count + 1
                && pb.blocks.Count == pb.emptyDivBlocksAt
                && pb.currentText.Length == pb.emptyDivTextAt
                && css is not null && css.TryGetValue("div", out var edR)
                && edR.TryGetValue("padding", out var edP)
                && TryParseLength(edP, out var edPt) && edPt > 0)
            {
                pb.blocks.Add(new Block
                {
                    Text = "", IsHardBreak = true,
                    FontSize = popped.FontSize,
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
            else if (popped.OwnPadTopPt > 0 && popped.BlocksAtOpen >= 0
                && pb.blocks.Count == popped.BlocksAtOpen)
            {
                pb.blocks.Add(new Block
                {
                    Text = "", IsHardBreak = true,
                    FontSize = popped.FontSize,
                    FontRes = popped.FontRes,
                    LeftIndent = popped.LeftIndent,
                    ExplicitHeight = popped.OwnPadTopPt,
                });
            }
            // page-break-after breaks AFTER this element's content — even when
            // it emitted nothing (the empty cover-separator <p>): the break
            // carries to whatever block flushes next.
            if (popped.PageBreakAfter) pb.pendingPageBreak = true;
            pb.inlineRunId = 0; pb.runPrevWasControl = false;
            // The box's bottom margin lands on its LAST line, whatever the
            // <br>s inside it split off (see the flush).
            if (closingMarginBottom > 0 && pb.blocks.Count > 0)
                pb.blocks[^1].MarginBottom = Math.Max(pb.blocks[^1].MarginBottom, closingMarginBottom);
        }
        // Inline close tags are no-ops for block layout.
    }

    /// <summary>One arm of ParseBlocks' token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void HandleBlockBr(ParseBlocksState pb, Token tok, string tag, bool articleRhythm, bool brBlankLines, bool browserUa, bool controlBoxes, bool inlineBlockCols, bool metricLayout, bool spanPtTypography, bool uaBlockRhythm)
    {
        // A <br> directly after a styled row block ends the full default-size
        // line box the row's markup opened (the browser's ~16px body line) —
        // there is no pending text for the usual flush-based break to space.
        if (pb.currentText.Length == 0 && pb.blocks.Count > 0 && pb.blocks[^1].RowRuns is not null)
        {
            pb.blocks.Add(new Block
            {
                Text = "", IsHardBreak = true, IsLineBreak = true, ExplicitHeight = 13.5,
                // The enclosing style's size: the metric flow sizes the <br>'s line
                // box from it (a <br> between 9pt paragraphs is still an 11pt line
                // when the paragraph closed before it).
                FontSize = pb.styleStack.Peek().FontSize,
            });
            return;
        }
        // Metric flow: a standalone <br> (no pending text — it sits between
        // blocks) is one full empty line box at the enclosing style's size.
        // A <br> after text just ends the line (the flush below), same as legacy.
        if (metricLayout && pb.currentText.ToString().Trim().Length == 0)
        {
            pb.currentText.Clear();
            pb.blocks.Add(new Block
            {
                Text = "", IsHardBreak = true, IsLineBreak = true, ExplicitHeight = 13.5,
                FontSize = pb.styleStack.Peek().FontSize,
            });
            return;
        }
        // Form-document dialect: a standalone <br> between flow runs (the
        // notice divs' <br><br> rhythm) keeps one empty line box at the
        // enclosing style's size instead of collapsing.
        if (brBlankLines && pb.currentText.ToString().Trim().Length == 0)
        {
            pb.currentText.Clear();
            var fbk = pb.styleStack.Peek();
            pb.blocks.Add(new Block
            {
                Text = "", IsHardBreak = true, IsLineBreak = true,
                FontSize = fbk.FontSize, FontFamily = fbk.FontFamily,
                WidthPx = fbk.WidthPx,
            });
            return;
        }
        // Sectioned-report rhythm: N consecutive <br> in block context are an
        // anonymous block of exactly N line boxes, carrying no margin of their own.
        if (uaBlockRhythm && pb.currentText.ToString().Trim().Length == 0)
        {
            pb.currentText.Clear();
            var brk = pb.styleStack.Peek();
            pb.blocks.Add(new Block
            {
                Text = "", IsHardBreak = true, IsLineBreak = true,
                FontSize = brk.FontSize, FontFamily = brk.FontFamily,
                // The empty line box occupies one full line of the enclosing
                // style — without a height of its own it would take no space.
                ExplicitHeight = NormalLineHeightPt(brk.FontSize),
            });
            return;
        }
        // Legacy-font dialect: a <br> with no pending text (an empty
        // <p><…><br></…></p>) is a full blank line on the 1.25×em grid, like
        // the metric flow above. Gated on LegacyFontSized so no other legacy
        // HTML gains blank lines where it previously collapsed them.
        if (pb.currentText.ToString().Trim().Length == 0 && pb.styleStack.Peek().LegacyFontSized)
        {
            pb.currentText.Clear();
            var pk = pb.styleStack.Peek();
            pb.blocks.Add(new Block
            {
                Text = "", IsHardBreak = true, IsLineBreak = true,
                FontFamily = pk.FontFamily, ForeColor = pk.ForeColor,
                LegacyFontPt = pk.LegacyFontPt, LegacyFontSized = true,
            });
            return;
        }
        // <br> inserts a newline *within* the current block. We
        // flush as an empty forced-break block so the next text
        // starts on a new line at the same style. Quirks CSS-run docs
        // keep a collapsed newline before the <br> as a trailing space.
        pb.keepTrailingSpace = inlineBlockCols;
        Flush(pb, controlBoxes, uaBlockRhythm, articleRhythm, spanPtTypography, true,pb.styleStack.Peek());
        pb.keepTrailingSpace = false;
        // An element's top margin belongs to the line that OPENS it: the segments
        // a <br> cuts are further lines of the same paragraph, not new paragraphs.
        // (Inert outside the float flow, which is the only reader of this value.)
        pb.styleStack.Peek().ShorthandTopPt = 0;
        // Browser-UA flow: a <br> breaks the LINE, not the paragraph —
        // the segments it cuts share the paragraph's margins (top on the
        // first, bottom on the last) instead of re-charging them per line.
        if (browserUa)
        {
            var brStyle = pb.styleStack.Peek();
            brStyle.MarginTop = 0;
            if (pb.blocks.Count > 0 && !pb.blocks[^1].IsHardBreak)
                pb.blocks[^1].MarginBottom = 0;
        }
        pb.pendingColIndent = 0;
        pb.inlineRunId = 0; pb.runPrevWasControl = false;
    }

    /// <summary>One arm of ParseBlocks' token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void HandleBlockHr(ParseBlocksState pb, Token tok, string tag, bool articleRhythm, bool controlBoxes, bool formDialect, bool spanPtTypography, bool uaBlockRhythm)
    {
        Flush(pb, controlBoxes, uaBlockRhythm, articleRhythm, spanPtTypography, true,pb.styleStack.Peek());
        pb.inlineRunId = 0; pb.runPrevWasControl = false;
        // Draw <hr> as a horizontal rule. The line colour/width come
        // from the CSS border (e.g. "border: 1px solid red"); default
        // to a thin grey line when unspecified.
        ParseHrStyle(tok.Attributes, out var hrColor, out var hrWidth);
        // Form dialect: the rule's own CSS margins (carried over from the
        // divider div it replaced) set the section rhythm around it.
        // The UA rule is `hr { margin: 0.5em 0 }` — smaller than a paragraph's,
        // so beside one it collapses away entirely.
        double hrMarginTop = 6, hrMarginBottom = 6;
        if (uaBlockRhythm)
            hrMarginTop = hrMarginBottom = 0.5 * pb.styleStack.Peek().FontSize;
        if (formDialect && tok.Attributes is not null
            && tok.Attributes.TryGetValue("style", out var hrStyle) && hrStyle is not null)
        {
            var hmt = Regex.Match(hrStyle, @"margin-top\s*:\s*(\d+(?:\.\d+)?)px", RegexOptions.IgnoreCase);
            if (hmt.Success) hrMarginTop = double.Parse(hmt.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture) * 0.75;
            var hmb = Regex.Match(hrStyle, @"margin-bottom\s*:\s*(\d+(?:\.\d+)?)px", RegexOptions.IgnoreCase);
            if (hmb.Success) hrMarginBottom = double.Parse(hmb.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture) * 0.75;
        }
        // The rule's declared width fraction (`width: 25%`) survives to
        // the drawn stroke (the redline groove rule centres at 25%).
        var hrWidthFrac = 0.0;
        if (tok.Attributes is not null
            && tok.Attributes.TryGetValue("style", out var hrWSt) && hrWSt is not null
            && Regex.Match(hrWSt, @"(?<![-\w])width\s*:\s*([\d.]+)\s*%",
                RegexOptions.IgnoreCase) is { Success: true } hrWm
            && double.TryParse(hrWm.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var hrWv))
            hrWidthFrac = hrWv / 100.0;
        pb.blocks.Add(new Block
        {
            Text = "",
            FontSize = pb.styleStack.Peek().FontSize,
            FontRes = "F1",
            WidthFrac = hrWidthFrac,
            MarginTop = hrMarginTop,
            MarginBottom = hrMarginBottom,
            // Not IsHardBreak: a rule is drawn content, so it must
            // survive the trailing-spacer trim and be rendered.
            IsHorizontalRule = true,
            RuleColor = hrColor,
            RuleWidth = hrWidth,
            // A pending page-break (a break-only <p> right before the rule)
            // belongs to the rule itself — otherwise the rule stays at the
            // old page's tail and the break jumps past it.
            PageBreakBefore = pb.pendingPageBreak,
        });
        pb.pendingPageBreak = false;
        // The rule's OWN page-break-after (the `<hr style="page-break-after:
        // always">` section-divider idiom): the rule closes the page it sits
        // on, and whatever block flushes next opens a fresh one.
        if (tok.Attributes is not null
            && tok.Attributes.TryGetValue("style", out var hrBreakStyle) && hrBreakStyle is not null
            && Regex.IsMatch(hrBreakStyle, @"(page-)?break-after\s*:\s*(always|page)",
                RegexOptions.IgnoreCase))
            pb.pendingPageBreak = true;
    }

    /// <summary>One arm of ParseBlocks' token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void HandleBlockImg(ParseBlocksState pb, Token tok, string tag, IReadOnlyDictionary<string, Dictionary<string, string>>? css, bool articleRhythm, bool containerBoxIndents, bool controlBoxes, bool formDialect, bool spanPtTypography, bool uaBlockRhythm)
    {
        string? src = null;
        tok.Attributes?.TryGetValue("src", out src);
        bool imgHidden = tok.Attributes is not null && tok.Attributes.TryGetValue("style", out var istyle)
            && Regex.IsMatch(istyle, @"display\s*:\s*none", RegexOptions.IgnoreCase);
        // A src-less image renders as its alt TEXT in a browser (the broken-image
        // placeholder line) — it still occupies a line box in the flow.
        if (string.IsNullOrEmpty(src) && !imgHidden
            && tok.Attributes is not null && tok.Attributes.TryGetValue("alt", out var altText)
            && !string.IsNullOrWhiteSpace(altText))
        {
            pb.currentText.Append(DecodeEntities(altText));
        }
        if (!string.IsNullOrEmpty(src) && !imgHidden)
        {
            // Control-box dialect: an image arriving MID-LINE (after label
            // text) rides inline at that line's end — defer it onto the text
            // block the pending run will flush into.
            if (controlBoxes && pb.currentText.ToString().Trim().Length > 0)
            {
                pb.pendingInlineIcon = true;
                return;
            }
            // Leading inline whitespace before an image (e.g. "&nbsp;&nbsp; <img>")
            // shares the image's line box in a browser — it is not a line of its
            // own. Keep its horizontal advance as the image's indent, but drop the
            // run so it doesn't reserve a phantom text line above the image (which
            // would push the image down a line).
            double imgIndentPt = 0;
            if (IsAllWhitespace(pb.currentText) && pb.currentText.Length > 0)
            {
                var (leadTxt, _) = CollapseWhitespaceWithMap(pb.currentText.ToString());
                if (leadTxt.Length > 0)
                {
                    var lst = pb.styleStack.Peek();
                    var leadFace = !string.IsNullOrEmpty(lst.FontFamily)
                        && PosFace(lst.FontFamily!).ttf is not null ? lst.FontFamily! : "Arial";
                    imgIndentPt = MeasureFaceText(leadFace, leadTxt, lst.FontSize);
                }
                pb.currentText.Clear();
            }
            // A sized container is FILLED by its image content — the
            // declared height must not also bill as an empty spacer at
            // this flush (the 600×400 chart div held exactly its svg).
            pb.styleStack.Peek().ExplicitHeight = 0;
            Flush(pb, controlBoxes, uaBlockRhythm, articleRhythm, spanPtTypography, false, pb.styleStack.Peek());
            double iw = 0, ih = 0;
            if (tok.Attributes is not null)
            {
                if (tok.Attributes.TryGetValue("width", out var ws)) double.TryParse(
                    Regex.Match(ws, @"[\d.]+").Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out iw);
                if (tok.Attributes.TryGetValue("height", out var hs)) double.TryParse(
                    Regex.Match(hs, @"[\d.]+").Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out ih);
                if (tok.Attributes.TryGetValue("style", out var st2) && !string.IsNullOrEmpty(st2))
                {
                    // Property-name anchored: "border-width: 0px" must not
                    // satisfy the width lookup (nor min-height the height one).
                    // A unitless value is CSS quirks px ("width:500;").
                    var wm = Regex.Match(st2, @"(?<![-\w])width\s*:\s*([\d.]+)\s*(?:px)?\s*(?:;|$)", RegexOptions.IgnoreCase);
                    if (wm.Success) double.TryParse(wm.Groups[1].Value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out iw);
                    var hm = Regex.Match(st2, @"(?<![-\w])height\s*:\s*([\d.]+)\s*(?:px)?\s*(?:;|$)", RegexOptions.IgnoreCase);
                    if (hm.Success) double.TryParse(hm.Groups[1].Value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out ih);
                }
                // A CLASS rule sizing the image (".auto-style1 { width: 453px;
                // height: 271px }") beats the width/height attributes, as CSS
                // beats presentational hints (the licensing letter's broken
                // photo box is the CLASS size).
                if (css is not null
                    && tok.Attributes.TryGetValue("class", out var imgCls)
                    && !string.IsNullOrWhiteSpace(imgCls))
                    foreach (var cn in imgCls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (!css.TryGetValue("." + cn, out var imgClsRule)) continue;
                        if (imgClsRule.TryGetValue("width", out var cwv))
                        {
                            var cwm = Regex.Match(cwv, @"([\d.]+)\s*px", RegexOptions.IgnoreCase);
                            if (cwm.Success) double.TryParse(cwm.Groups[1].Value,
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out iw);
                        }
                        if (imgClsRule.TryGetValue("height", out var chv))
                        {
                            var chm = Regex.Match(chv, @"([\d.]+)\s*px", RegexOptions.IgnoreCase);
                            if (chm.Success) double.TryParse(chm.Groups[1].Value,
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out ih);
                        }
                    }
            }
            // position:absolute + left/top on the image's inline style: the
            // image seats at the page margins + left/top (CSS px) and leaves
            // the flow entirely (the cursor never moves for it).
            var imgAbsPos = false;
            double imgAbsLeftPx = 0, imgAbsTopPx = 0;
            if (tok.Attributes is not null && tok.Attributes.TryGetValue("style", out var apSt)
                && !string.IsNullOrEmpty(apSt)
                && Regex.IsMatch(apSt, @"position\s*:\s*absolute", RegexOptions.IgnoreCase))
            {
                var alM = Regex.Match(apSt, @"(?<![-\w])left\s*:\s*(-?[\d.]+)\s*(?:px)?\s*(?:;|$)", RegexOptions.IgnoreCase);
                var atM = Regex.Match(apSt, @"(?<![-\w])top\s*:\s*(-?[\d.]+)\s*(?:px)?\s*(?:;|$)", RegexOptions.IgnoreCase);
                if (alM.Success && atM.Success
                    && double.TryParse(alM.Groups[1].Value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out imgAbsLeftPx)
                    && double.TryParse(atM.Groups[1].Value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out imgAbsTopPx))
                    imgAbsPos = true;
            }
            string? alt = null;
            tok.Attributes?.TryGetValue("alt", out alt);
            // Form dialect: the image's CSS margin-left indents it within the
            // flow (a browser applies it to the image box; the legacy flow's
            // calibrated conversions keep ignoring it).
            if (formDialect && tok.Attributes is not null
                && tok.Attributes.TryGetValue("style", out var imStyle) && imStyle is not null)
            {
                var iml = Regex.Match(imStyle, @"(?<![-\w])margin-left\s*:\s*(\d+(?:\.\d+)?)px", RegexOptions.IgnoreCase);
                if (iml.Success) imgIndentPt += double.Parse(iml.Groups[1].Value,
                    System.Globalization.CultureInfo.InvariantCulture) * 0.75;
            }
            // CSS vertical padding on the image (style="padding:28px 0 14px").
            double padT = 0, padB = 0;
            if (tok.Attributes is not null && tok.Attributes.TryGetValue("style", out var ist)
                && !string.IsNullOrEmpty(ist))
            {
                var pm = Regex.Match(ist, @"padding\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
                if (pm.Success)
                {
                    var parts = pm.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 1) padT = padB = ParsePxValue(parts[0]);
                    if (parts.Length >= 3) padB = ParsePxValue(parts[2]);
                }
            }
            // CSS transform: rotate(Ndeg) — from the img's inline style or a class
            // rule. Only the UNPREFIXED property qualifies (a vendor-mangled
            // "-webkit - transform" parses under a different property name and a
            // real vendor prefix would shadow the standard one anyway).
            double imgRotDeg = 0;
            string? rotSrc = null;
            if (tok.Attributes is not null && tok.Attributes.TryGetValue("style", out var rst)
                && !string.IsNullOrEmpty(rst)
                && Regex.Match(rst, @"(?<![-\w])transform\s*:\s*([^;]+)", RegexOptions.IgnoreCase)
                    is { Success: true } rim)
                rotSrc = rim.Groups[1].Value;
            else if (css is not null && tok.Attributes is not null
                     && tok.Attributes.TryGetValue("class", out var rcls) && rcls is not null)
                foreach (var rc in rcls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                    if (css.TryGetValue("." + rc, out var rrules)
                        && rrules.TryGetValue("transform", out var rv))
                    { rotSrc = rv; break; }
            if (rotSrc is not null)
            {
                var rm = Regex.Match(rotSrc, @"rotate\(\s*(-?[\d.]+)\s*deg\s*\)", RegexOptions.IgnoreCase);
                if (rm.Success) double.TryParse(rm.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out imgRotDeg);
            }
            // An inline percentage max-width caps the drawn box at that
            // share of the content width and keeps the sheet from widening.
            double imgMaxWFrac = 0;
            if (tok.Attributes is not null && tok.Attributes.TryGetValue("style", out var mwSt)
                && mwSt is not null
                && Regex.Match(mwSt, @"max-width\s*:\s*([\d.]+)\s*%", RegexOptions.IgnoreCase)
                    is { Success: true } mwM
                && double.TryParse(mwM.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var mwPct)
                && mwPct > 0)
                imgMaxWFrac = mwPct / 100.0;
            // An image floats by its OWN style attribute as often as by its
            // container's - `<img style="float:left">` is the common form - so
            // read both and take either. Only the container was consulted before,
            // which left such an image in the flow and stacked it.
            var imgFloatLeft = pb.styleStack.Peek().FloatLeft;
            var imgFloatRight = pb.styleStack.Peek().FloatRight;
            if (tok.Attributes is not null && tok.Attributes.TryGetValue("style", out var flSt)
                && flSt is not null)
            {
                if (Regex.IsMatch(flSt, @"float\s*:\s*left", RegexOptions.IgnoreCase))
                    imgFloatLeft = true;
                else if (Regex.IsMatch(flSt, @"float\s*:\s*right", RegexOptions.IgnoreCase))
                    imgFloatRight = true;
            }
            // A FLOATED image is inset from the edge it floats to by its own
            // side margin - a logo with margin-left:70px lands at
            // 148.50 on a 96 pt content edge, and one with margin-right:70px at
            // the right edge less the same 52.5 pt. Declarations repeat in this
            // dialect ("margin-right:60px;margin-right:70px"), so the LAST wins
            // as CSS says. Only floated images take it; the calibrated flows
            // keep ignoring an in-flow image's margins.
            // …and the margin on the side FACING the flow is the gutter the
            // wrapped text keeps off it: a float's wrap edge is its MARGIN box
            // edge. Measured on the certificate: the left logo's margin-right:34px
            // puts the heading's box at 342.75, and the right logo declares no
            // margin-left so text wraps flush against 315.25 - which the flat
            // FloatGutterPt stand-in got wrong on both sides. A float that
            // declares no margins at all keeps that stand-in.
            double? imgGutterPt = null;
            if ((imgFloatLeft || imgFloatRight) && tok.Attributes is not null
                && tok.Attributes.TryGetValue("style", out var fmSt) && fmSt is not null)
            {
                // Declarations repeat in this dialect, so the LAST wins as CSS says.
                static double SideMarginPt(string style, string prop)
                {
                    var ms = Regex.Matches(style,
                        @"(?<![-\w])" + prop + @"\s*:\s*(\d+(?:\.\d+)?)px",
                        RegexOptions.IgnoreCase);
                    return ms.Count > 0 && double.TryParse(ms[ms.Count - 1].Groups[1].Value,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var px)
                        ? px * 0.75 : 0;
                }
                var hugPt = SideMarginPt(fmSt, imgFloatRight ? "margin-right" : "margin-left");
                if (hugPt > 0)
                {
                    imgIndentPt += hugPt;
                    imgGutterPt = SideMarginPt(fmSt,
                        imgFloatRight ? "margin-left" : "margin-right");
                }
            }
            pb.blocks.Add(new Block { IsImage = true, ImageSrc = src, ImageWidth = iw, ImageHeight = ih,
                ImageMaxWFrac = imgMaxWFrac,
                ImageAbsPos = imgAbsPos, ImageAbsLeftPx = imgAbsLeftPx, ImageAbsTopPx = imgAbsTopPx,
                ImageAlt = alt, PageBreakBefore = pb.pendingPageBreak,
                // Centered inside <center> or an ALIGN="center" block (a legacy
                // <P ALIGN="center"><IMG> centers the image line).
                ImageCentered = pb.centerDepth > 0 || pb.styleStack.Peek().AlignCenterAttr,
                ImagePadTopPx = padT, ImagePadBottomPx = padB,
                // Container chrome (containerBoxIndents mode): the enclosing
                // divs' padding+border chain indents the image like any block,
                // and the width-billing part sizes the page-widen.
                ImageIndentPt = imgIndentPt
                    + (containerBoxIndents ? pb.styleStack.Peek().LeftIndent : 0),
                ImageFloatGutterPt = imgGutterPt,
                ImageWidenPadPt = containerBoxIndents ? pb.styleStack.Peek().BillPadPt : 0,
                ImageCardShadow = containerBoxIndents ? pb.styleStack.Peek().CardShadowColor : null,
                ImageCardChromePt = containerBoxIndents ? pb.styleStack.Peek().CardChromePt : 0,
                ImageRotateDeg = imgRotDeg,
                FloatLeft = imgFloatLeft, FloatRight = imgFloatRight });
            pb.pendingPageBreak = false;
        }
    }

    /// <summary>One arm of ParseBlocks' token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void HandleBlockInput(ParseBlocksState pb, Token tok, string tag, bool articleRhythm, bool controlBoxes, bool dwFlow, bool spanPtTypography, bool uaBlockRhythm)
    {
        string? type = null;
        tok.Attributes?.TryGetValue("type", out type);
        type = UnescapeAttrValue(type);
        type = string.IsNullOrEmpty(type) ? "text" : type.ToLowerInvariant();
        if (type is "text" or "password" or "email" or "tel" or "url"
            or "number" or "search" or "date" or "datetime-local" or "month" or "week" or "time"
            // The control-box dialect has no radio/checkbox/hidden widgets:
            // they ALL render as ordinary text boxes — the
            // intrinsic 20-column box with the value (wrappers and all)
            // typeset inside (an escaped type never reaches its handler).
            || (controlBoxes && type is "radio" or "checkbox" or "hidden"))
        {
            if (controlBoxes && pb.inlineRunId == 0) pb.inlineRunId = pb.nextInlineRunId++;
            var inTrailWs = pb.currentText.Length > 0 && char.IsWhiteSpace(pb.currentText[^1]);
            var inBefore = pb.blocks.Count;
            Flush(pb, controlBoxes, uaBlockRhythm, articleRhythm, spanPtTypography, false, pb.styleStack.Peek());
            if (controlBoxes && inTrailWs && pb.blocks.Count > inBefore)
                pb.blocks[^1].Text += " ";
            pb.blocks.Add(BuildInputBlock(tok.Attributes, pb.styleStack.Peek(), controlBoxes));
            if (controlBoxes)
            {
                pb.blocks[^1].InlineRunId = pb.inlineRunId;
                pb.runPrevWasControl = true;
            }
        }
        else if (type == "checkbox")
        {
            Flush(pb, controlBoxes, uaBlockRhythm, articleRhythm, spanPtTypography, false, pb.styleStack.Peek());
            var st = pb.styleStack.Peek();
            pb.blocks.Add(new Block
            {
                IsCheckbox = true,
                Checked = tok.Attributes?.ContainsKey("checked") == true,
                FontSize = st.FontSize,
                FontRes = st.FontRes,
                LeftIndent = st.LeftIndent,
            });
        }
        else if (type == "radio")
        {
            Flush(pb, controlBoxes, uaBlockRhythm, articleRhythm, spanPtTypography, false, pb.styleStack.Peek());
            var st = pb.styleStack.Peek();
            string? grp = null;
            tok.Attributes?.TryGetValue("name", out grp);
            pb.blocks.Add(new Block
            {
                IsRadio = true,
                RadioGroup = grp ?? "",
                Checked = tok.Attributes?.ContainsKey("checked") == true,
                FontSize = st.FontSize,
                FontRes = st.FontRes,
                LeftIndent = st.LeftIndent,
            });
        }
        else if (dwFlow && type == "submit" && tok.Attributes is not null
            && tok.Attributes.TryGetValue("value", out var dwBtnVal)
            && !string.IsNullOrEmpty(dwBtnVal))
        {
            Flush(pb, controlBoxes, uaBlockRhythm, articleRhythm, spanPtTypography, false, pb.styleStack.Peek());
            pb.blocks.Add(new Block
            {
                IsButton = true,
                ButtonCaption = DecodeEntities(dwBtnVal),
                AlignRight = tok.Attributes.TryGetValue("align", out var dwBtnAl)
                    && dwBtnAl?.Equals("right", StringComparison.OrdinalIgnoreCase) == true,
                FontSize = pb.styleStack.Peek().FontSize,
            });
        }
    }
}
