using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>One arm of ParseBlocks' token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void HandleBlockClose(ParseBlocksState pb, string tag)
    {
        // A stray </p> with no open <p> quirks-parses as an EMPTY
        // paragraph: it contributes its UA margin (max-collapsed onto
        // the next block) and must NOT pop the enclosing element's
        // style. Browser-UA flow only.
        if (tag.Equals("bdo", StringComparison.OrdinalIgnoreCase)) CloseBdoRun(pb);
        if (!CloseParagraphAndEmphasisTags(pb, tag)) return;
        if (pb.trackBoldRuns && tag.ToLowerInvariant() is "i" or "em")
        {
            if (pb.inlineItalicDepth > 0 && --pb.inlineItalicDepth == 0
                && pb.currentText.Length > pb.inlineItalicStart)
                pb.rawItalics.Add((pb.inlineItalicStart, pb.currentText.Length));
            // Browser-UA flow draws italic purely as a run; other flows
            // keep the historical whole-block promotion.
            if (pb.browserUa) return;
        }
        if (!CloseFormControlTags(pb, tag)) return;
        if (tag.Equals("a", StringComparison.OrdinalIgnoreCase) && pb.openAnchors.Count > 0)
        {
            var (st, url) = pb.openAnchors.Pop();
            if (pb.currentText.Length > st) pb.rawAnchors.Add((st, pb.currentText.Length, url));
        }
        // An empty <i>/<em> (icon placeholder) reverts its promotion — the
        // text that follows the close is not emphasised.
        if (pb.controlBoxes && tag.ToLowerInvariant() is "i" or "em"
            && pb.italicOpenTextLen >= 0 && pb.currentText.Length == pb.italicOpenTextLen)
        {
            var itop = pb.styleStack.Peek();
            if (itop.FontRes == "F3") itop.FontRes = "F1";
            itop.EmItalic = false;
            pb.italicOpenTextLen = -1;
        }
        // A UA-flow </font> restores the typography its open saved —
        // flushing the styled run it closes first.
        CloseFontAndSpanTags(pb, tag);
        // An inline div's close is as inline as its open — nothing to pop.
        if (pb.inlineDivDepth > 0 && tag.Equals("div", StringComparison.OrdinalIgnoreCase))
        {
            pb.inlineDivDepth--;
            return;
        }
        if (BlockTags.Contains(tag))
        {
            CloseBlockTag(pb, tag);
        }
        // Inline close tags are no-ops for block layout.
    }

    /// <summary>One arm of ParseBlocks' token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void HandleBlockBr(ParseBlocksState pb)
    {
        // A <br> directly after a styled row block ends the full default-size
        // line box the row's markup opened (the browser's ~16px body line) —
        // there is no pending text for the usual flush-based break to space.
        if (pb.currentText.Length == 0 && pb.blocks.Count > 0 && pb.blocks[^1].RowRuns is not null)
        {
            pb.blocks.Add(new Block
            {
                Text = "", IsHardBreak = true, IsLineBreak = true, ExplicitHeight = 13.5,
                LineBoxPt = pb.styleStack.Peek().LineBoxPt,
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
        if (!CloseMetricLayoutBlankBr(pb)) return;
        // The sheet-typography flow steps by CSS boxes: a standalone <br> between blocks is one
        // empty line box of the enclosing style - its line-height box when the sheet declared
        // one, the face's normal line otherwise (measured: seven <br/> under a UA body open the
        // grid 94.5 below the content top, 13.5 each).
        if (_quirksChainSheet && pb.currentText.ToString().Trim().Length == 0)
        {
            pb.currentText.Clear();
            var sbk = pb.styleStack.Peek();
            pb.blocks.Add(new Block
            {
                Text = "", IsHardBreak = true, IsLineBreak = true,
                FontSize = sbk.FontSize, FontFamily = sbk.FontFamily,
                ExplicitHeight = sbk.LineBoxPt > 0 ? sbk.LineBoxPt : NormalLineHeightPt(sbk.FontSize),
            });
            return;
        }
        // Form-document dialect: a standalone <br> between flow runs (the
        // notice divs' <br><br> rhythm) keeps one empty line box at the
        // enclosing style's size instead of collapsing.
        if (pb.brBlankLines && pb.currentText.ToString().Trim().Length == 0)
        {
            pb.currentText.Clear();
            var fbk = pb.styleStack.Peek();
            var breakFontPt = fbk.FontSize > 0 ? fbk.FontSize : DefaultBodyFontPt;
            pb.blocks.Add(new Block
            {
                Text = "", IsHardBreak = true, IsLineBreak = true,
                FontSize = fbk.FontSize, FontFamily = fbk.FontFamily,
                WidthPx = fbk.WidthPx,
                // (a UA-grid document's blank break is one line box of its font)
                ExplicitHeight = pb.uaGridBlocks ? NormalLineHeightPt(breakFontPt) : 0,
            });
            return;
        }
        // Sectioned-report rhythm: N consecutive <br> in block context are an
        // anonymous block of exactly N line boxes, carrying no margin of their own.
        if (!BreakAsUaRhythmSpacer(pb)) return;
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
        pb.keepTrailingSpace = pb.inlineBlockCols;
        Flush(pb, pb.controlBoxes, pb.uaBlockRhythm, pb.articleRhythm, pb.spanPtTypography,pb.styleStack.Peek());
        pb.keepTrailingSpace = false;
        // An element's top margin belongs to the line that OPENS it: the segments
        // a <br> cuts are further lines of the same paragraph, not new paragraphs.
        // (Inert outside the float flow, which is the only reader of this value.)
        pb.styleStack.Peek().ShorthandTopPt = 0;
        // Browser-UA flow: a <br> breaks the LINE, not the paragraph —
        // the segments it cuts share the paragraph's margins (top on the
        // first, bottom on the last) instead of re-charging them per line.
        if (pb.browserUa)
        {
            var brStyle = pb.styleStack.Peek();
            brStyle.MarginTop = 0;
            if (pb.blocks.Count > 0 && !pb.blocks[^1].IsHardBreak)
                pb.blocks[^1].MarginBottom = 0;
        }
        pb.pendingColIndent = 0;
        pb.pendingColIndentFrac = 0;
        pb.inlineRunId = 0; pb.runPrevWasControl = false; pb.ctlLineId = 0;
    }

    /// <summary>One arm of ParseBlocks' token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void HandleBlockHr(ParseBlocksState pb, Token tok)
    {
        Flush(pb, pb.controlBoxes, pb.uaBlockRhythm, pb.articleRhythm, pb.spanPtTypography,pb.styleStack.Peek());
        pb.inlineRunId = 0; pb.runPrevWasControl = false; pb.ctlLineId = 0;
        // Draw <hr> as a horizontal rule. The line colour/width come
        // from the CSS border (e.g. "border: 1px solid red"); default
        // to a thin grey line when unspecified.
        var (hrColor, hrWidth) = ParseHrStyle(tok.Attributes);
        // Form dialect: the rule's own CSS margins (carried over from the
        // divider div it replaced) set the section rhythm around it.
        // The UA rule is `hr { margin: 0.5em 0 }` — smaller than a paragraph's,
        // so beside one it collapses away entirely.
        double hrMarginTop = 6, hrMarginBottom = 6;
        if (pb.uaBlockRhythm)
            hrMarginTop = hrMarginBottom = 0.5 * pb.styleStack.Peek().FontSize;
        // A rule INSIDE a UA heading (`<H1>…<HR></H1>`): its margins are 0.5 em of the heading's
        // size, and the heading's own bottom margin stands after the rule, not before it - the rule
        // is the heading's last child, its bottom margin collapsing with the heading's (MEASURED, the
        // evaluation form: the rule 7.15 under the title's second line, the h2 9.58 = 0.67 x 14.3
        // under the rule).
        if (pb.browserUa && pb.styleStack.Peek() is { Tag: { Length: 2 } hrHost } hrHostStyle
            && (hrHost[0] is 'h' or 'H') && hrHost[1] is >= '1' and <= '6' && hrHostStyle.FontSize > 0)
        {
            hrMarginTop = hrMarginBottom = HrCellMarginEm * hrHostStyle.FontSize;
            if (pb.blocks.Count > 0 && !pb.blocks[^1].IsHardBreak && pb.blocks[^1].MarginBottom > 0)
            {
                hrMarginBottom = Math.Max(hrMarginBottom, pb.blocks[^1].MarginBottom);
                pb.blocks[^1].MarginBottom = 0;
            }
        }
        if (pb.formDialect && tok.Attributes is not null
            && tok.Attributes.TryGetValue("style", out var hrStyle) && hrStyle is not null)
        {
            var hmt = SafeRegex.Match(hrStyle, @"margin-top\s*:\s*(\d+(?:\.\d+)?)px", RegexOptions.IgnoreCase);
            if (hmt.Success) hrMarginTop = double.Parse(hmt.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture) * 0.75;
            var hmb = SafeRegex.Match(hrStyle, @"margin-bottom\s*:\s*(\d+(?:\.\d+)?)px", RegexOptions.IgnoreCase);
            if (hmb.Success) hrMarginBottom = double.Parse(hmb.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture) * 0.75;
        }
        // The rule's declared width fraction (`width: 25%`) survives to
        // the drawn stroke (the redline groove rule centres at 25%).
        var hrWidthFrac = 0.0;
        if (tok.Attributes is not null
            && tok.Attributes.TryGetValue("style", out var hrWSt) && hrWSt is not null
            && SafeRegex.Match(hrWSt, @"(?<![-\w])width\s*:\s*([\d.]+)\s*%",
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
            && SafeRegex.IsMatch(hrBreakStyle, @"(page-)?break-after\s*:\s*(always|page)",
                RegexOptions.IgnoreCase))
            pb.pendingPageBreak = true;
    }

    /// <summary>One arm of ParseBlocks' token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void HandleBlockImg(ParseBlocksState pb, Token tok)
    {
        pb.src = null;
        tok.Attributes?.TryGetValue("src", out pb.src);
        pb.imgHidden = tok.Attributes is not null && tok.Attributes.TryGetValue("style", out var istyle)
            && SafeRegex.IsMatch(istyle, @"display\s*:\s*none", RegexOptions.IgnoreCase);
        // A src-less image renders as its alt TEXT in a browser (the broken-image
        // placeholder line) — it still occupies a line box in the flow.
        if (string.IsNullOrEmpty(pb.src) && !pb.imgHidden
            && tok.Attributes is not null && tok.Attributes.TryGetValue("alt", out var altText)
            && !string.IsNullOrWhiteSpace(altText))
        {
            pb.currentText.Append(DecodeEntities(altText));
        }
        // Without a source there is no picture, but a declared size and background still paint the box.
        if (string.IsNullOrEmpty(pb.src) && !pb.imgHidden && SourcelessImageBox(tok) is { } fillBox)
        {
            Flush(pb, pb.controlBoxes, pb.uaBlockRhythm, pb.articleRhythm, pb.spanPtTypography, pb.styleStack.Peek());
            pb.blocks.Add(new Block { SizedFillBox = fillBox, LeftIndent = pb.styleStack.Peek().LeftIndent });
            return;
        }
        if (!(!string.IsNullOrEmpty(pb.src) && !pb.imgHidden)) return;
        // Control-box dialect: an image arriving MID-LINE (after label
        // text) rides inline at that line's end — defer it onto the text
        // block the pending run will flush into.
        if (!FlushTextBeforeImage(pb)) return;
        // A sized container is FILLED by its image content — the
        // declared height must not also bill as an empty spacer at
        // this flush (the 600×400 chart div held exactly its svg).
        pb.styleStack.Peek().ExplicitHeight = 0;
        Flush(pb, pb.controlBoxes, pb.uaBlockRhythm, pb.articleRhythm, pb.spanPtTypography, pb.styleStack.Peek());
        pb.iw = 0;
        pb.ih = 0;
        pb.imgWFrac = 0;
        ReadImageAttributes(pb, tok);
        pb.alt = null;
        tok.Attributes?.TryGetValue("alt", out pb.alt);
        // Form dialect: the image's CSS margin-left indents it within the
        // flow (a browser applies it to the image box; the legacy flow's
        // calibrated conversions keep ignoring it).
        if (pb.formDialect && tok.Attributes is not null
            && tok.Attributes.TryGetValue("style", out var imStyle) && imStyle is not null)
        {
            var iml = SafeRegex.Match(imStyle, @"(?<![-\w])margin-left\s*:\s*(\d+(?:\.\d+)?)px", RegexOptions.IgnoreCase);
            if (iml.Success) pb.imgIndentPt += double.Parse(iml.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture) * 0.75;
        }
        pb.padT = 0;
        pb.padB = 0;
        ReadImageSizingAttributes(pb, tok);
        pb.imgGutterPt = null;
        if (!PlaceImageBlock(pb, tok)) return;
        var placed = pb.blocks[^1];
        placed.ImageJoinsRow = placed.ImageInline && pb.ctlLineId == 0 && pb.blocks.Count > 1
            && pb.blocks[^2] is { IsImage: true, ImageInline: true } && ReferenceEquals(pb.lastInlineImageStyle, pb.styleStack.Peek());
        pb.lastInlineImageStyle = placed.ImageInline ? pb.styleStack.Peek() : null;
        if (pb.ctlLineId != 0) JoinControlLineImage(pb, pb.blocks[^1], tok);
        pb.pendingPageBreak = false;
    }

    /// <summary>The box a source-less picture element paints: the width, height and background colour of
    /// its own style attribute, all three declared; null otherwise.</summary>
    private static (double W, double H, Color Fill)? SourcelessImageBox(Token tok)
    {
        if (tok.Attributes is null || !tok.Attributes.TryGetValue("style", out var style) || string.IsNullOrEmpty(style))
            return null;
        string? Declared(string prop)
        {
            var m = SafeRegex.Match(style, @"(?<![-\w])" + prop + @"\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }
        if (Declared("width") is not { } w || TryParseLength(w) is not { } widthPt || widthPt <= 0) return null;
        if (Declared("height") is not { } h || TryParseLength(h) is not { } heightPt || heightPt <= 0) return null;
        if (Declared("background-color") is not { } bg || ParseCssColor(bg) is not { } fill) return null;
        return (widthPt, heightPt, fill);
    }

    /// <summary>One arm of ParseBlocks' token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    /// <summary>Inline-block column form: the control after a title column seats at the
    /// column's edge on the label's line, and its own inline-block class gives it its
    /// width (a percent of the content width) and the row's margin-bottom.</summary>
    private static void SeatInputInTitleColumns(ParseBlocksState pb, Token tok, Block input)
    {
        if (pb.pendingColIndent > 0 || pb.pendingColIndentFrac > 0)
        {
            input.LeftIndent += pb.pendingColIndent;
            input.LeftIndentFrac += pb.pendingColIndentFrac;
            pb.pendingColIndent = 0;
            pb.pendingColIndentFrac = 0;
            input.InputInColumn = true;
            input.MarginTop = 0;
            // the UA text input box beside the column (measured 17.1 pt from the line top)
            if (input.InputHeight <= 0) input.InputHeight = UaInputBoxHeightPt;
        }
        if (pb.css is null || tok.Attributes is null
            || !tok.Attributes.TryGetValue("class", out var cls) || cls is null) return;
        foreach (var sc in cls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!pb.css.TryGetValue("." + sc, out var rule)) continue;
            if (rule.TryGetValue("width", out var w))
            {
                if (TryParseLength(w) is { } wPt && wPt > 0) input.InputWidth = wPt;
                else if (PercentFraction(w) is var wf && wf > 0) input.InputWidthFrac = wf;
            }
            if (rule.TryGetValue("margin-bottom", out var mb) && TryParseLength(mb) is { } mbPt && mbPt > 0)
                input.MarginBottom = mbPt;
        }
    }

    /// <summary>The input types that are not text boxes: buttons, choices, the hidden,
    /// file and image kinds, the slider and the colour well.</summary>
    private static readonly HashSet<string> NonTextInputTypes = new(StringComparer.Ordinal)
    {
        "checkbox", "radio", "submit", "button", "reset", "hidden", "image", "file", "range", "color",
    };

    private static void HandleBlockInput(ParseBlocksState pb, Token tok)
    {
        string? type = null;
        tok.Attributes?.TryGetValue("type", out type);
        type = UnescapeAttrValue(type);
        type = string.IsNullOrEmpty(type) ? "text" : type.ToLowerInvariant();
        // An input whose type is not one of the button, choice, hidden or file kinds is a
        // text box, an unknown or misspelt type included (the HTML rule: an invalid type
        // falls back to the text state, so `type=“number”` with curly quotes is a text box).
        if (!NonTextInputTypes.Contains(type)
            // The control-box dialect has no radio/checkbox/hidden widgets:
            // they ALL render as ordinary text boxes — the
            // intrinsic 20-column box with the value (wrappers and all)
            // typeset inside (an escaped type never reaches its handler).
            || (pb.controlBoxes && type is "radio" or "checkbox" or "hidden"))
        {
            if (pb.controlBoxes && pb.inlineRunId == 0) pb.inlineRunId = pb.nextInlineRunId++;
            var inTrailWs = pb.currentText.Length > 0 && char.IsWhiteSpace(pb.currentText[^1]);
            var inBefore = pb.blocks.Count;
            OpenControlLine(pb);
            Flush(pb, pb.controlBoxes, pb.uaBlockRhythm, pb.articleRhythm, pb.spanPtTypography, pb.styleStack.Peek());
            if (pb.controlBoxes && inTrailWs && pb.blocks.Count > inBefore)
                pb.blocks[^1].Text += " ";
            pb.blocks.Add(BuildInputBlock(tok.Attributes, pb.styleStack.Peek(), pb.controlBoxes,
                sheet: pb.css, tag: "input"));
            pb.blocks[^1].CtlLineId = pb.ctlLineId;
            if (pb.ctlLineId != 0) pb.blocks[^1].InputColumns = InputSizeAttribute(tok.Attributes);
            if (pb.controlBoxes)
            {
                pb.blocks[^1].InlineRunId = pb.inlineRunId;
                pb.runPrevWasControl = true;
            }
            if (pb.inlineBlockCols) SeatInputInTitleColumns(pb, tok, pb.blocks[^1]);
        }
        else if (type == "checkbox")
        {
            OpenControlLine(pb);
            Flush(pb, pb.controlBoxes, pb.uaBlockRhythm, pb.articleRhythm, pb.spanPtTypography, pb.styleStack.Peek());
            var st = pb.styleStack.Peek();
            pb.blocks.Add(new Block
            {
                IsCheckbox = true,
                Checked = tok.Attributes?.ContainsKey("checked") == true,
                InputName = ControlFieldName(tok.Attributes),
                InputReadOnly = ControlLocked(tok.Attributes),
                FontSize = st.FontSize,
                FontRes = st.FontRes,
                LeftIndent = st.LeftIndent,
                CtlLineId = pb.ctlLineId,
            });
        }
        else if (type == "radio")
        {
            OpenControlLine(pb);
            Flush(pb, pb.controlBoxes, pb.uaBlockRhythm, pb.articleRhythm, pb.spanPtTypography, pb.styleStack.Peek());
            var st = pb.styleStack.Peek();
            string? grp = null;
            tok.Attributes?.TryGetValue("name", out grp);
            string? radioValue = null;
            tok.Attributes?.TryGetValue("value", out radioValue);
            pb.blocks.Add(new Block
            {
                IsRadio = true,
                RadioGroup = grp ?? "",
                RadioValue = radioValue ?? "",
                Checked = tok.Attributes?.ContainsKey("checked") == true,
                InputReadOnly = ControlLocked(tok.Attributes),
                FontSize = st.FontSize,
                FontRes = st.FontRes,
                LeftIndent = st.LeftIndent,
                CtlLineId = pb.ctlLineId,
            });
        }
        else if (pb.controlLines && type is "submit" or "button" or "reset")
        {
            OpenControlLine(pb);
            Flush(pb, pb.controlBoxes, pb.uaBlockRhythm, pb.articleRhythm, pb.spanPtTypography, pb.styleStack.Peek());
            string? caption = null;
            tok.Attributes?.TryGetValue("value", out caption);
            pb.blocks.Add(PushButtonBlock(pb, tok.Attributes, "input", caption ?? UaButtonCaption(type)));
        }
        else if (pb.dwFlow && type == "submit" && tok.Attributes is not null
            && tok.Attributes.TryGetValue("value", out var dwBtnVal)
            && !string.IsNullOrEmpty(dwBtnVal))
        {
            Flush(pb, pb.controlBoxes, pb.uaBlockRhythm, pb.articleRhythm, pb.spanPtTypography, pb.styleStack.Peek());
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
