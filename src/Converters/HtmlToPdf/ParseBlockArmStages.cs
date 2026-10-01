using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>A closing block tag pops its style, flushes the run it held open, closes lists, fieldsets and pending borders, and records the margins the next block inherits.</summary>
    private static void CloseBlockTag(ParseBlocksState pb, string tag)
    {
        pb.popped = pb.styleStack.Count > 1 ? pb.styleStack.Pop() : pb.styleStack.Peek();
        if (pb.divClassStack.Count > 0) pb.divClassStack.RemoveAt(pb.divClassStack.Count - 1);
        if (pb.divTagStack.Count > 0) pb.divTagStack.RemoveAt(pb.divTagStack.Count - 1);
        if (pb.divIdStack.Count > 0) pb.divIdStack.RemoveAt(pb.divIdStack.Count - 1);
        if (pb.divStyleStack.Count > 0) pb.divStyleStack.RemoveAt(pb.divStyleStack.Count - 1);
        pb.closingMarginBottom = pb.uaBlockRhythm || pb.articleRhythm || pb.bodyBoxRhythm
            || pb.inlineEmphasisRuns
            ? pb.popped.MarginBottom : 0;
        // The UA flow's box keeps its closing margin for a margin-less follower: the
        // pairwise constants give a following P its share, a bare line, break or
        // div after a heading or list adds nothing of its own.
        // An authored margin-bottom replaced the UA default, and a NESTED list's box
        // margins are zero (the UA sheet's `ul ul { margin: 0 }`).
        pb.closingGapPt = pb.uaClosingGaps && !pb.popped.MarginBottomAuthored
            && !(pb.popped.ListKind != 0 && InsideList(pb))
            ? UaBlockMarginEmOf(tag) * pb.popped.FontSize : 0;
        pb.closingElement = true;
        Flush(pb, pb.controlBoxes, pb.uaBlockRhythm, pb.articleRhythm, pb.spanPtTypography, true,pb.popped);
        pb.closingElement = false;
        if (pb.cv?.profile.fieldListDoc == true)
        {
            // (a label whose row holds no value keeps its own row)
            if (pb.pendingColIndentFrac > 0)
            {
                pb.pendingColIndentFrac = 0; pb.pendingColIndent = 0;
                if (pb.blocks.Count > 0) pb.blocks[^1].NoAdvanceY = false;
            }
            // (the element's padding-bottom stands under its last block)
            if (pb.popped.PadBottomPt > 0 && pb.blocks.Count > 0 && pb.blocks.Count > pb.popped.BlocksAtOpen)
                pb.blocks[^1].PadBottomPt += pb.popped.PadBottomPt;
        }
        // The body-line-box sheet flow steps by CSS boxes: a text element that closes EMPTY (the
        // `<P align=center><A></A> </P>` spacer) has no box of its own and its margins collapse
        // through - the larger of the two joins the margin under the block before it, where the
        // flow's pairwise collapse reads it (measured: an empty 13 px P stands 9.75 between an
        // h3 and the h2 band, and again between the two grids).
        if (_quirksChainSheet && pb.popped.LineBoxPt > 0 && IsTextElementTag(tag)
            && pb.popped.BlocksAtOpen >= 0 && pb.blocks.Count == pb.popped.BlocksAtOpen
            && pb.blocks.Count > 0 && !pb.blocks[^1].IsHardBreak)
            pb.blocks[^1].MarginBottom = Math.Max(pb.blocks[^1].MarginBottom,
                Math.Max(pb.popped.MarginTop, pb.popped.MarginBottom));
        CloseHeightFloorAndBorderBox(pb);
        // The pinned-body report's authored spacer: an EMPTY div
        // renders as its padding box — the sheet's `div{padding:4px}`
        // gives "<div></div>" 4px above + 4px below of real height.
        CloseDivBandBackground(pb, tag);
        // ...and the container background box: if the element flushed no line of its own, the
        // colour reached no block and nothing painted it, so bracket the children it did emit.
        // A container that DID flush its own line already paints per line and is left alone.
        CloseBackgroundSpanBox(pb);
        // page-break-after breaks AFTER this element's content — even when
        // it emitted nothing (the empty cover-separator <p>): the break
        // carries to whatever block flushes next.
        if (pb.popped.PageBreakAfter) pb.pendingPageBreak = true;
        pb.inlineRunId = 0; pb.runPrevWasControl = false;
        // The box's bottom margin lands on its LAST line, whatever the
        // <br>s inside it split off (see the flush).
        if (pb.closingMarginBottom > 0 && pb.blocks.Count > 0)
            pb.blocks[^1].MarginBottom = Math.Max(pb.blocks[^1].MarginBottom, pb.closingMarginBottom);
        if (pb.closingGapPt > 0 && pb.blocks.Count > 0)
            pb.blocks[^1].UaClosingGapPt = Math.Max(pb.blocks[^1].UaClosingGapPt, pb.closingGapPt);
    }

    /// <summary>True while an open list encloses the current style stack.</summary>
    private static bool InsideList(ParseBlocksState pb)
    {
        foreach (var anc in pb.styleStack)
            if (anc.ListKind != 0) return true;
        return false;
    }

    /// <summary>The last emphasis close of a stretch queues the saved style for the flush
    /// that emits the promoted block; the blocks after it draw in the style it replaced.</summary>
    private static void ArmEmphasisRestore(ParseBlocksState pb)
    {
        if (pb.inlineBoldDepth == 0 && pb.inlineItalicDepth == 0 && pb.emphasisSave is not null)
            pb.emphasisRestorePending = true;
    }

    /// <summary>A closing span closes the underline, bold and italic runs its inline style opened.</summary>
    private static void CloseSpanEmphasisRuns(ParseBlocksState pb)
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
    }

    /// <summary>Word mail: a sized span that held no text styles nothing - the typography it replaced comes back
    /// (a paragraph's trailing empty 12 pt span must not restyle its 7.5 pt text).</summary>
    private static void RestoreEmptyWordMailSpan(ParseBlocksState pb)
    {
        if (!pb.wordMail || pb.wmSpanSaves.Count == 0 || pb.wmSpanSaves.Peek().Depth != pb.spanDepth) return;
        var save = pb.wmSpanSaves.Pop();
        // (the Word export scopes a span's typography to the span, text or no text: an 8 pt bullet
        // span closes and the h3's 16 pt comes back for its unsized text)
        if (pb.currentText.Length != save.TextLen && !pb.wordExport) return;
        var top = pb.styleStack.Peek();
        top.FontSize = save.Fs; top.FontFamily = save.Fam; top.ForeColor = save.Fore;
        top.EmBold = save.Bold; top.EmItalic = save.Ital;
    }

    /// <summary>A closing font tag restores the UA font save; a closing span closes its class, colour, decoration and emphasis runs and its ledger entry.</summary>
    private static void CloseFontAndSpanTags(ParseBlocksState pb, string tag)
    {
        var anchorFrame = tag.Equals("a", StringComparison.OrdinalIgnoreCase) && pb.uaAnchorFrames > 0;
        if (anchorFrame) pb.uaAnchorFrames--;
        if (pb.browserUa && (tag.Equals("font", StringComparison.OrdinalIgnoreCase) || anchorFrame)
            && pb.uaFontSaves.Count > 0)
        {
            var (sFs, sFore, sFam) = pb.uaFontSaves.Pop();
            var fTop = pb.styleStack.Peek();
            // A font tag that changed nothing the flow draws (an unknown face, no size,
            // no colour) closes without breaking its line: the text after it continues
            // the run (probed: the literal line break after `</FONT>` sits on the last
            // line of the font's text, not on a line of its own).
            var restores = fTop.FontSize != sFs || !Equals(fTop.ForeColor, sFore)
                || !string.Equals(fTop.FontFamily, sFam, StringComparison.Ordinal);
            if (restores)
                Flush(pb, pb.controlBoxes, pb.uaBlockRhythm, pb.articleRhythm, pb.spanPtTypography, false, fTop);
            if (pb.wordMail && Environment.GetEnvironmentVariable("ASPOSE_TRACE_SEAT") == "1") Console.Error.WriteLine($"[restore] tag={tag} depth={pb.spanDepth} was={fTop.FontSize}/{fTop.FontFamily} -> {sFs}/{sFam} text='{(pb.currentText.Length > 20 ? pb.currentText.ToString(0, 20) : pb.currentText.ToString())}'");
            fTop.FontSize = sFs;
            fTop.ForeColor = sFore;
            fTop.FontFamily = sFam;
        }
        // A label's close ends the runs its class opened, and nothing else.
        if (tag.Equals("label", StringComparison.OrdinalIgnoreCase))
        {
            CloseSpanEmphasisRuns(pb);
            if (pb.openSizeRuns.Count > 0 && pb.openSizeRuns.Peek().depth == pb.spanDepth)
            {
                var (_, lbs, lbPt) = pb.openSizeRuns.Pop();
                if (pb.currentText.Length > lbs)
                    pb.rawSizeRuns.Add((lbs, pb.currentText.Length, lbPt));
            }
            if (pb.spanDepth > 0) pb.spanDepth--;
        }
        // A block-span's close breaks its line like its open did.
        CloseSpanTag(pb, tag);
    }

    /// <summary>Closing center, textarea, select, option and button tags flush the control they collected; false when the close is done.</summary>
    private static bool CloseFormControlTags(ParseBlocksState pb, string tag)
    {
        if (tag.Equals("center", StringComparison.OrdinalIgnoreCase))
        {
            Flush(pb, pb.controlBoxes, pb.uaBlockRhythm, pb.articleRhythm, pb.spanPtTypography, false, pb.styleStack.Peek());
            if (pb.centerDepth > 0) pb.centerDepth--;
            return false;
        }
        if (tag.Equals("textarea", StringComparison.OrdinalIgnoreCase))
        {
            pb.inTextarea = false;
            if (pb.textareaBlock is not null)
                pb.textareaBlock.InputValue = CollapseWs(pb.textareaText.ToString());
            pb.textareaBlock = null; pb.textareaText.Clear();
            return false;
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
            if (pb.controlBoxes)
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
            else if (chosen.Length > 0) { pb.currentText.Append(chosen); Flush(pb, pb.controlBoxes, pb.uaBlockRhythm, pb.articleRhythm, pb.spanPtTypography, true, pb.styleStack.Peek()); }
            pb.selectOptions.Clear();
            pb.selectName = null;
            return false;
        }
        if (tag.Equals("option", StringComparison.OrdinalIgnoreCase))
        {
            if (pb.curOptionText.Length > 0)
            {
                pb.selectOptions.Add(CollapseWs(pb.curOptionText.ToString()));
                pb.curOptionText.Clear();
            }
            pb.inSelectedOption = false;
            return false;
        }
        if (pb.controlBoxes && tag.Equals("button", StringComparison.OrdinalIgnoreCase))
        {
            pb.inButton = false;
            pb.blocks.Add(new Block
            {
                IsButton = true,
                ButtonCaption = CollapseWs(pb.buttonText.ToString()),
                FontSize = pb.styleStack.Peek().FontSize,
            });
            pb.buttonText.Clear();
            return false;
        }
        return true;
    }

    /// <summary>A closing p under the browser UA or the metric dialect, and closing bold or emphasis tags, settle their runs; false when the close is done.</summary>
    private static bool CloseParagraphAndEmphasisTags(ParseBlocksState pb, string tag)
    {
        if (pb.browserUa && tag.Equals("p", StringComparison.OrdinalIgnoreCase))
        {
            if (pb.pOpenDepth > 0) pb.pOpenDepth--;
            else
            {
                Flush(pb, pb.controlBoxes, pb.uaBlockRhythm, pb.articleRhythm, pb.spanPtTypography, false, pb.styleStack.Peek());
                // Word-filtered idiom: the stray </p> is an EMPTY
                // MsoNormal paragraph — one full base-size line box
                // (the sheet reset its margins to zero), not a
                // collapsible UA margin.
                if (pb.msoParagraphs)
                    pb.blocks.Add(new Block
                    {
                        Text = "", IsHardBreak = true, IsLineBreak = true,
                        FontSize = 12,
                    });
                else
                    pb.pendingEmptyPMarginPt = Math.Max(pb.pendingEmptyPMarginPt,
                        UaBlockMarginEm * pb.styleStack.Peek().FontSize);
                return false;
            }
        }
        // Metric flow: a closed body-level <p> leaves its UA 1.12 em
        // bottom margin to collapse onto whatever follows it — carried
        // both as the next block's pending top margin and on the flushed
        // block itself (a following TABLE reads only the latter).
        else if (pb.metricLayout && pb.uaPMargins && tag.Equals("p", StringComparison.OrdinalIgnoreCase))
        {
            // only the flushed block's bottom margin — the NEXT
            // paragraph's own open-margin covers text-to-text collapse;
            // a following TABLE reads MarginBottom alone. The margin is
            // 1.12 em of the p's INHERITED size (the block style's own
            // FontSize carries the legacy flow default, not the CSS one).
            var pMb = UaBlockMarginEm * (pb.styleStack.Peek().ParentFontSize > 0
                ? pb.styleStack.Peek().ParentFontSize : pb.styleStack.Peek().FontSize);
            Flush(pb, pb.controlBoxes, pb.uaBlockRhythm, pb.articleRhythm, pb.spanPtTypography, false, pb.styleStack.Peek());
            if (pb.blocks.Count > 0)
                pb.blocks[^1].MarginBottom = Math.Max(pb.blocks[^1].MarginBottom, pMb);
        }
        if (pb.trackBoldRuns && tag.ToLowerInvariant() is "b" or "strong")
        {
            if (pb.inlineBoldDepth > 0 && --pb.inlineBoldDepth == 0
                && pb.currentText.Length > pb.inlineBoldStart)
                pb.rawBolds.Add((pb.inlineBoldStart, pb.currentText.Length));
            // The browser-UA and sheet-typography flows draw bold purely as a run; the
            // in-page fragment flow keeps the historical whole-block promotion as
            // its fallback, so let the close reach the generic handling.
            if (pb.browserUa || pb.sheetElementTypography || pb.wordMail) return false;
            ArmEmphasisRestore(pb);
        }
        if (pb.trackBoldRuns && !pb.browserUa && tag.ToLowerInvariant() is "i" or "em")
        {
            if (pb.inlineItalicDepth > 0 && --pb.inlineItalicDepth == 0
                && pb.currentText.Length > pb.inlineItalicStart)
                pb.rawItalics.Add((pb.inlineItalicStart, pb.currentText.Length));
            ArmEmphasisRestore(pb);
        }
        if ((pb.inlineEmphasisRuns || pb.browserUa)
            && tag.Equals("u", StringComparison.OrdinalIgnoreCase))
        {
            if (pb.inlineUnderDepth > 0 && --pb.inlineUnderDepth == 0
                && pb.currentText.Length > pb.inlineUnderStart)
                pb.rawUnders.Add((pb.inlineUnderStart, pb.currentText.Length));
            return false;
        }
        if (pb.browserUa && tag.ToLowerInvariant() is "strike" or "s" or "del")
        {
            if (pb.inlineStrikeDepth > 0 && --pb.inlineStrikeDepth == 0
                && pb.currentText.Length > pb.inlineStrikeStart)
                pb.rawStrikes.Add((pb.inlineStrikeStart, pb.currentText.Length));
            return false;
        }
        if (pb.openSubSupRuns.Count > 0 && tag.ToLowerInvariant() is "sub" or "sup")
        {
            var ssStart = pb.openSubSupRuns.Pop();
            if (pb.currentText.Length > ssStart)
                pb.rawSizeRuns.Add((ssStart, pb.currentText.Length,
                    pb.styleStack.Peek().FontSize * SubSupFontScale));
            return false;
        }
        return true;
    }

    /// <summary>A floated image joins the float ledger, otherwise the image becomes its own block; false when the float consumed it.</summary>
    private static bool PlaceImageBlock(ParseBlocksState pb, Token tok)
    {
        if ((pb.imgFloatLeft || pb.imgFloatRight) && tok.Attributes is not null
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
            var hugPt = SideMarginPt(fmSt, pb.imgFloatRight ? "margin-right" : "margin-left");
            if (hugPt > 0)
            {
                pb.imgIndentPt += hugPt;
                pb.imgGutterPt = SideMarginPt(fmSt,
                    pb.imgFloatRight ? "margin-left" : "margin-right");
            }
        }
        pb.blocks.Add(new Block { IsImage = true, ImageSrc = pb.src!, ImageWidth = pb.iw, ImageHeight = pb.ih,
            ImageMaxWFrac = pb.imgMaxWFrac,
            // An image inside an absolute chain seats where the chain resolved it, from the PAGE's own
            // margin box (the options' 90 / 72), not the calibrated flow's inset - the reference lays
            // an absolute box against the page margins (probed: 90 + 273.75 / 72 + 6 for a dashboard tile).
            ImageAbsPos = pb.imgAbsPos || AbsChainSeated(pb),
            ImageAbsLeftPt = pb.imgAbsPos ? pb.imgAbsLeftPt
                : AbsChainSeated(pb) ? pb.styleStack.Peek().AbsOriginLeftPt - (pb.cv!.marginLeft - PageMarginLeftPt(pb.cv)) : 0,
            ImageAbsTopPt = pb.imgAbsPos ? pb.imgAbsTopPt
                : AbsChainSeated(pb) ? pb.styleStack.Peek().AbsOriginTopPt - (pb.cv!.marginTop - PageMarginTopPt(pb.cv)) : 0,
            PageBandHeightPt = pb.imgPageBandPt,
            ImageAlt = pb.alt, PageBreakBefore = pb.pendingPageBreak,
            // Centered inside <center> or an ALIGN="center" block (a legacy
            // <P ALIGN="center"><IMG> centers the image line).
            ImageCentered = pb.centerDepth > 0 || pb.styleStack.Peek().AlignCenterAttr,
            ImagePadTopPx = pb.padT, ImagePadBottomPx = pb.padB,
            // Container chrome (containerBoxIndents mode): the enclosing
            // divs' padding+border chain indents the image like any block,
            // and the width-billing part sizes the page-widen.
            // A position:absolute ancestor chain (see AbsOriginLeftPt) seats the image at its
            // own resolved offset, exactly as an in-flow container's padding/border chain
            // would - the two are independent, additive CSS box-model concepts, and
            // AbsOriginLeftPt is 0 (a no-op) for the vast majority of documents that never
            // resolve one.
            ImageIndentPt = pb.imgIndentPt
                + (pb.containerBoxIndents ? pb.styleStack.Peek().LeftIndent : 0)
                + pb.styleStack.Peek().AbsOriginLeftPt,
            // The sheet-typography flow keeps the float's OWN margin as its gutter (a
            // margin-less float lets the text start at its edge; probed: a 231 pt float
            // seats the column at 90 + 231).
            ImageFloatGutterPt = pb.sheetElementTypography ? pb.imgGutterPt ?? 0 : pb.imgGutterPt,
            ImageWidenPadPt = pb.containerBoxIndents ? pb.styleStack.Peek().BillPadPt : 0,
            ImageCardShadow = pb.containerBoxIndents ? pb.styleStack.Peek().CardShadowColor : null,
            ImageCardChromePt = pb.containerBoxIndents ? pb.styleStack.Peek().CardChromePt : 0,
            ImageCardFrame = pb.styleStack.Peek().CardFrameColor,
            ImageCardFrameBorderPt = pb.styleStack.Peek().CardFrameBorderPt,
            ImageCardFrameInsetPt = pb.styleStack.Peek().CardFrameInsetPt,
            ImageRotateDeg = pb.imgRotDeg,
            ImageInline = !pb.imgFloatLeft && !pb.imgFloatRight && !ImageDisplaysAsBlock(pb, tok),
            FloatLeft = pb.imgFloatLeft, FloatRight = pb.imgFloatRight });
        return true;
    }

    /// <summary>Whether the image sits inside a resolved absolute containing box (an ancestor chain that
    /// AbsOriginWidthPt records) without being absolutely positioned itself.</summary>
    private static bool AbsChainSeated(ParseBlocksState pb)
        => !pb.imgAbsPos && pb.cv is not null && pb.styleStack.Peek().InAbsoluteChain;

    /// <summary>True when the image is a block of its own: its style or the sheet's img rule says
    /// display: block (the newsletter idiom `img { display: block }`).</summary>
    private static bool ImageDisplaysAsBlock(ParseBlocksState pb, Token tok)
    {
        if (tok.Attributes is { } ia && ia.TryGetValue("style", out var ist) && ist is not null
            && Regex.IsMatch(ist, @"(?<![-\w])display\s*:\s*block", RegexOptions.IgnoreCase))
            return true;
        return pb.css is not null && pb.css.TryGetValue("img", out var imgRule)
            && imgRule.TryGetValue("display", out var idisp) && idisp is not null
            && idisp.Trim().StartsWith("block", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The inline size, class and max-width rules and the float attribute settle the image box.</summary>
    private static void ReadImageSizingAttributes(ParseBlocksState pb, Token tok)
    {
        if (tok.Attributes is not null && tok.Attributes.TryGetValue("style", out var ist)
            && !string.IsNullOrEmpty(ist))
        {
            var pm = Regex.Match(ist, @"padding\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
            if (pm.Success)
            {
                var parts = pm.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 1) pb.padT = pb.padB = ParsePxValue(parts[0]);
                if (parts.Length >= 3) pb.padB = ParsePxValue(parts[2]);
            }
        }
        pb.imgRotDeg = 0;
        pb.rotSrc = null;
        if (tok.Attributes is not null && tok.Attributes.TryGetValue("style", out var rst)
            && !string.IsNullOrEmpty(rst)
            && Regex.Match(rst, @"(?<![-\w])transform\s*:\s*([^;]+)", RegexOptions.IgnoreCase)
                is { Success: true } rim)
            pb.rotSrc = rim.Groups[1].Value;
        else if (pb.css is not null && tok.Attributes is not null
                 && tok.Attributes.TryGetValue("class", out var rcls) && rcls is not null)
            foreach (var rc in rcls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                if (pb.css.TryGetValue("." + rc, out var rrules)
                    && rrules.TryGetValue("transform", out var rv))
                { pb.rotSrc = rv; break; }
        if (pb.rotSrc is not null)
        {
            var rm = Regex.Match(pb.rotSrc, @"rotate\(\s*(-?[\d.]+)\s*deg\s*\)", RegexOptions.IgnoreCase);
            if (rm.Success) double.TryParse(rm.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out pb.imgRotDeg);
        }
        pb.imgMaxWFrac = 0;
        if (tok.Attributes is not null && tok.Attributes.TryGetValue("style", out var mwSt)
            && mwSt is not null
            && Regex.Match(mwSt, @"max-width\s*:\s*([\d.]+)\s*%", RegexOptions.IgnoreCase)
                is { Success: true } mwM
            && double.TryParse(mwM.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var mwPct)
            && mwPct > 0)
            pb.imgMaxWFrac = mwPct / 100.0;
        pb.imgFloatLeft = pb.styleStack.Peek().FloatLeft;
        pb.imgFloatRight = pb.styleStack.Peek().FloatRight;
        if (tok.Attributes is not null && tok.Attributes.TryGetValue("style", out var flSt)
            && flSt is not null)
        {
            if (Regex.IsMatch(flSt, @"float\s*:\s*left", RegexOptions.IgnoreCase))
                pb.imgFloatLeft = true;
            else if (Regex.IsMatch(flSt, @"float\s*:\s*right", RegexOptions.IgnoreCase))
                pb.imgFloatRight = true;
        }
    }

    /// <summary>The image's width, height, alt, align and positioning style attributes.</summary>
    private static void ReadImageAttributes(ParseBlocksState pb, Token tok)
    {
        if (tok.Attributes is not null)
        {
            if (tok.Attributes.TryGetValue("width", out var ws)) double.TryParse(
                Regex.Match(ws, @"[\d.]+").Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out pb.iw);
            if (tok.Attributes.TryGetValue("height", out var hs)) double.TryParse(
                Regex.Match(hs, @"[\d.]+").Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out pb.ih);
            if (tok.Attributes.TryGetValue("style", out var st2) && !string.IsNullOrEmpty(st2))
            {
                // Property-name anchored: "border-width: 0px" must not
                // satisfy the width lookup (nor min-height the height one).
                // A unitless value is CSS quirks px ("width:500;").
                var wm = Regex.Match(st2, @"(?<![-\w])width\s*:\s*([\d.]+)\s*(?:px)?\s*(?:;|$)", RegexOptions.IgnoreCase);
                if (wm.Success) double.TryParse(wm.Groups[1].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out pb.iw);
                var hm = Regex.Match(st2, @"(?<![-\w])height\s*:\s*([\d.]+)\s*(?:px)?\s*(?:;|$)", RegexOptions.IgnoreCase);
                if (hm.Success) double.TryParse(hm.Groups[1].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out pb.ih);
            }
            // A CLASS rule sizing the image (".auto-style1 { width: 453px;
            // height: 271px }") beats the width/height attributes, as CSS
            // beats presentational hints (the licensing letter's broken
            // photo box is the CLASS size).
            if (pb.css is not null
                && tok.Attributes.TryGetValue("class", out var imgCls)
                && !string.IsNullOrWhiteSpace(imgCls))
                foreach (var cn in imgCls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!pb.css.TryGetValue("." + cn, out var imgClsRule)) continue;
                    if (imgClsRule.TryGetValue("width", out var cwv))
                    {
                        var cwm = Regex.Match(cwv, @"([\d.]+)\s*px", RegexOptions.IgnoreCase);
                        if (cwm.Success) double.TryParse(cwm.Groups[1].Value,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out pb.iw);
                    }
                    if (imgClsRule.TryGetValue("height", out var chv))
                    {
                        var chm = Regex.Match(chv, @"([\d.]+)\s*px", RegexOptions.IgnoreCase);
                        if (chm.Success) double.TryParse(chm.Groups[1].Value,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out pb.ih);
                    }
                }
        }
        ReadAbsoluteImageSeat(pb, tok);
    }

    /// <summary>Text pending before the image flushes or, when it is only whitespace, drops; false when a control box took the image.</summary>
    private static bool FlushTextBeforeImage(ParseBlocksState pb)
    {
        if (pb.controlBoxes && pb.currentText.ToString().Trim().Length > 0)
        {
            pb.pendingInlineIcon = true;
            return false;
        }
        pb.imgIndentPt = 0;
        if (IsAllWhitespace(pb.currentText) && pb.currentText.Length > 0)
        {
            var (leadTxt, _) = CollapseWhitespaceWithMap(pb.currentText.ToString());
            if (leadTxt.Length > 0)
            {
                var lst = pb.styleStack.Peek();
                var leadFace = !string.IsNullOrEmpty(lst.FontFamily)
                    && PosFace(lst.FontFamily!).ttf is not null ? lst.FontFamily! : "Arial";
                pb.imgIndentPt = MeasureFaceText(leadFace, leadTxt, lst.FontSize);
            }
            pb.currentText.Clear();
        }
        return true;
    }

    /// <summary>Under the UA block rhythm an empty-line break becomes a spacer block; false when it did.</summary>
    private static bool BreakAsUaRhythmSpacer(ParseBlocksState pb)
    {
        if (pb.uaBlockRhythm && pb.currentText.ToString().Trim().Length == 0)
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
            return false;
        }
        return true;
    }

    /// <summary>A closing block settles the height floor it opened and the pending border box that ends with it.</summary>
    private static void CloseHeightFloorAndBorderBox(ParseBlocksState pb)
    {
        if (pb.heightFloors.Count > 0 && pb.heightFloors.Peek().Depth == pb.styleStack.Count + 1)
        {
            var fl = pb.heightFloors.Pop();
            pb.popped.HeightFloorDeferred = false;
            if (pb.blocks.Count > fl.Marker + 1)
                pb.blocks.Add(new Block
                {
                    Text = "",
                    HeightFloorEnd = true,
                    ExplicitHeight = fl.H,
                    FontSize = pb.popped.FontSize,
                    FontRes = pb.popped.FontRes,
                    LeftIndent = pb.popped.LeftIndent,
                });
            else
            {
                // Nothing grew into the floor: an empty sized element is
                // the plain reserved-space spacer it always was.
                pb.blocks[fl.Marker].HeightFloorStart = false;
                pb.blocks[fl.Marker].IsHardBreak = true;
                pb.blocks[fl.Marker].ExplicitHeight = fl.H;
                // ...and in the UA-serif flow a painted one fills its box (the reward letter's
                // white 60px strip under the banner picture)
                if (pb.popped.UaBoxes && pb.popped.BackgroundColor is { } spacerFill)
                {
                    pb.blocks[fl.Marker].BackgroundColor = spacerFill;
                    pb.blocks[fl.Marker].BgBoxHeightPt = fl.H;
                    pb.blocks[fl.Marker].BgBoxIndentPt = pb.popped.LeftIndent;
                    pb.blocks[fl.Marker].RightInsetPt = pb.popped.RightInsetPt;
                }
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
                FontSize = pb.popped.FontSize,
                FontRes = pb.popped.FontRes,
                LeftIndent = pb.popped.LeftIndent,
                ExplicitHeight = cbb.h,
                BorderBoxWPt = cbb.w,
                BorderRadiusPt = cbb.r,
                BorderWidth = cbb.bw,
                BorderColor = cbb.c,
            });
            pb.pendingBorderBox = null;
        }
    }
}
