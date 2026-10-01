using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>A span or font tag opens or closes an inline run: its class and style rules, colour, size, face, bold and italic flags, absolute-span ledger, decoration runs and UA font saves.</summary>
    private static void ParseSpanOrFontTag(ParseBlocksState pb, Token tok)
    {
        // A span a class rule sets `display:block` is a block box: it breaks
        // the line before its content and again at its close (metric flow
        // only — the `.year { display:block }` date-stamp idiom). Vendor-
        // mangled transform debris in the same rule stays inert.
        OpenSpanClassAndLedger(pb, tok);
        // A span CLASS's stylesheet colour opens a colour run too (the
        // redline markers' `span.diff-html-added { color: red }`); its
        // own inline colour below wins when both are present.
        TrackSpanColourAndEmphasisRuns(pb, tok);
        // pt-styled fragment: the span's own pt typography styles the
        // block (16 pt bold title, 10 pt paragraphs, 8 pt italic note).
        ApplySpanPtTypographyAndFontTag(pb, tok);
        // UA-serif flow: an inline span's typography styles its element's
        // block — pt/px font-size, a px line-height LINE BOX, and the
        // span's own margin-left insetting its text (the legacy corpus
        // wraps whole lines in one styled span).
        ApplyUaSpanStyleAttribute(pb, tok);
    }

    /// <summary>Button, textarea, select and option tags collect their text into the pending control; false when the token is done.</summary>
    private static bool HandleFormControlTags(ParseBlocksState pb, Token tok)
    {
        if (pb.controlBoxes && pb.tag.Equals("button", StringComparison.OrdinalIgnoreCase))
        {
            Flush(pb, pb.controlBoxes, pb.uaBlockRhythm, pb.articleRhythm, pb.spanPtTypography, false, pb.styleStack.Peek());
            pb.inlineRunId = 0; pb.runPrevWasControl = false;
            pb.inButton = true; pb.buttonText.Clear();
            return false;
        }

        // <input> / <textarea>: emit an interactive AcroForm field.
        // Text-like inputs become a TextBoxField; a checkbox becomes a CheckboxField
        // (its `checked` attribute → Checked); a radio becomes a RadioButtonOptionField
        // grouped by name. hidden/submit/button/image are skipped.
        if (pb.tag.Equals("textarea", StringComparison.OrdinalIgnoreCase))
        {
            // <textarea> → a multi-line AcroForm text field. Its inner text is the
            // default value (suppressed via inTextarea), not flow content.
            if (pb.controlBoxes && pb.inlineRunId == 0) pb.inlineRunId = pb.nextInlineRunId++;
            var taTrailWs = pb.currentText.Length > 0 && char.IsWhiteSpace(pb.currentText[^1]);
            var taBefore = pb.blocks.Count;
            Flush(pb, pb.controlBoxes, pb.uaBlockRhythm, pb.articleRhythm, pb.spanPtTypography, false, pb.styleStack.Peek());
            if (pb.controlBoxes && taTrailWs && pb.blocks.Count > taBefore)
                pb.blocks[^1].Text += " ";
            pb.blocks.Add(BuildInputBlock(tok.Attributes, pb.styleStack.Peek(),
                pb.controlBoxes, multiline: true, sheet: pb.css, tag: "textarea"));
            if (pb.controlBoxes)
            {
                pb.blocks[^1].InlineRunId = pb.inlineRunId;
                pb.runPrevWasControl = true;
            }
            pb.textareaBlock = pb.blocks[^1];
            pb.textareaText.Clear();
            pb.inTextarea = true;
            return false;
        }
        // <select>: the control occupies its box; only its chosen entry is text.
        if (pb.tag.Equals("select", StringComparison.OrdinalIgnoreCase))
        {
            // The label text on this line joins the control's inline run, keeping
            // the collapsed space the markup left before the control.
            if (pb.controlBoxes && pb.inlineRunId == 0) pb.inlineRunId = pb.nextInlineRunId++;
            var trailWs = pb.currentText.Length > 0 && char.IsWhiteSpace(pb.currentText[^1]);
            var nBefore = pb.blocks.Count;
            Flush(pb, pb.controlBoxes, pb.uaBlockRhythm, pb.articleRhythm, pb.spanPtTypography, false, pb.styleStack.Peek());
            if (pb.controlBoxes && trailWs && pb.blocks.Count > nBefore)
                pb.blocks[^1].Text += " ";
            pb.inSelect = true; pb.inSelectedOption = false; pb.selectedText.Clear();
            pb.selectOptions.Clear(); pb.curOptionText.Clear();
            string? selNm = null, selId = null;
            tok.Attributes?.TryGetValue("name", out selNm);
            tok.Attributes?.TryGetValue("id", out selId);
            pb.selectName = !string.IsNullOrEmpty(selNm) ? selNm : selId;
            return false;
        }
        if (pb.inSelect && pb.tag.Equals("option", StringComparison.OrdinalIgnoreCase))
        {
            if (pb.curOptionText.Length > 0)
            {
                pb.selectOptions.Add(CollapseWs(pb.curOptionText.ToString()));
                pb.curOptionText.Clear();
            }
            pb.inSelectedOption = tok.Attributes is not null
                && tok.Attributes.ContainsKey("selected") && pb.selectedText.Length == 0;
            return false;
        }
        return true;
    }

    /// <summary>Fieldset and legend boxes, the token's attributes and the center tag are handled; false when the token is done.</summary>
    private static bool HandleFieldsetAndCenterTags(ParseBlocksState pb, Token tok)
    {
        if (pb.tag.Equals(FrameDivTag, StringComparison.OrdinalIgnoreCase))
        {
            Flush(pb, pb.controlBoxes, pb.uaBlockRhythm, pb.articleRhythm, pb.spanPtTypography, false, pb.styleStack.Peek());
            var marker = new Block { Text = "", IsHardBreak = true, FsBox = tok.IsClose ? -1 : 1, FrameW = CssBorderMediumPt };
            if (!tok.IsClose && tok.Attributes is { } fa)
            {
                var st = fa.TryGetValue("style", out var fst) && fst is not null ? fst : "";
                var sides = CssBorderSides(st);
                marker.FrameW = sides[0].W > 0 ? sides[0].W : CssBorderMediumPt;
                marker.FrameCol = sides[0].Col;
                if (Regex.Match(st, @"(?<![-\w])width\s*:\s*([^;]+)", RegexOptions.IgnoreCase) is { Success: true } wm
                    && TryParseLength(wm.Groups[1].Value.Trim()) is { } wPt) marker.FrameBoxW = wPt;
                marker.FrameCentred = fa.TryGetValue("align", out var al) && al is not null
                    && al.Trim().Equals("center", StringComparison.OrdinalIgnoreCase);
            }
            pb.blocks.Add(marker);
            return false;
        }
        if (pb.fieldsetBoxes && pb.tag.Equals("fieldset", StringComparison.OrdinalIgnoreCase))
        {
            Flush(pb, pb.controlBoxes, pb.uaBlockRhythm, pb.articleRhythm, pb.spanPtTypography, false, pb.styleStack.Peek());
            pb.blocks.Add(new Block { Text = "", IsHardBreak = true, FsBox = tok.IsClose ? -1 : 1 });
            // clamped at zero: the close tag often parses in a LATER segment
            // (its table split the parse) whose fresh root never saw the open
            pb.styleStack.Peek().LeftIndent = tok.IsClose
                ? Math.Max(0, pb.styleStack.Peek().LeftIndent - FsPadLeftPt)
                : pb.styleStack.Peek().LeftIndent + FsPadLeftPt;
            return false;
        }
        if (pb.fieldsetBoxes && pb.tag.Equals("legend", StringComparison.OrdinalIgnoreCase))
        {
            Flush(pb, pb.controlBoxes, pb.uaBlockRhythm, pb.articleRhythm, pb.spanPtTypography, false, pb.styleStack.Peek());
            var lgdTop = pb.styleStack.Peek();
            if (!tok.IsClose)
            {
                pb.fsLegendSave = (lgdTop.FontSize, lgdTop.FontRes);
                var lgdFactor = pb.uaFieldset ? 1.0 : 1.2;
                if (pb.css is not null && pb.css.TryGetValue("legend", out var lgdRule)
                    && lgdRule.TryGetValue("font-size", out var lgdFs)
                    && Regex.Match(lgdFs, @"([\d.]+)\s*em") is { Success: true } lgdEm)
                    lgdFactor = double.Parse(lgdEm.Groups[1].Value,
                        System.Globalization.CultureInfo.InvariantCulture);
                lgdTop.FontSize *= lgdFactor;
                lgdTop.FontRes = "F2";
                pb.fsInLegend = true;
            }
            else if (pb.fsInLegend)
            {
                if (pb.blocks.Count > 0 && pb.blocks[^1].Text.Length > 0)
                {
                    pb.blocks[^1].FsLegend = true;
                    if (pb.uaFieldset)
                    {
                        pb.blocks[^1].LeftIndent += UaFieldsetLegendPadPt;
                        pb.blocks[^1].MarginBottom = Math.Max(pb.blocks[^1].MarginBottom, UaFieldsetLegendGapEm * pb.blocks[^1].FontSize);
                    }
                }
                (lgdTop.FontSize, lgdTop.FontRes) = pb.fsLegendSave;
                pb.fsInLegend = false;
            }
            return false;
        }

        if (tok.IsClose) { HandleBlockClose(pb, tok, pb.tag); return false; }

        // Opening tag (or self-closing).
        // Anchor targets: an `id` on any element, or a `name` on <a>, marks a
        // destination that a #fragment hyperlink can jump to. Record it against
        // the block currently being built.
        if (tok.Attributes is not null)
        {
            if (tok.Attributes.TryGetValue("id", out var idName) && !string.IsNullOrEmpty(idName))
                pb.pendingAnchorNames.Add(idName);
            if (pb.tag.Equals("a", StringComparison.OrdinalIgnoreCase)
                && tok.Attributes.TryGetValue("name", out var aName) && !string.IsNullOrEmpty(aName))
                pb.pendingAnchorNames.Add(aName);
        }
        if (pb.tag.Equals("center", StringComparison.OrdinalIgnoreCase))
        {
            Flush(pb, pb.controlBoxes, pb.uaBlockRhythm, pb.articleRhythm, pb.spanPtTypography, false, pb.styleStack.Peek());
            if (!tok.IsSelfClosing) pb.centerDepth++;
            return false;
        }
        return true;
    }

    /// <summary>Tokens inside a hidden element, text tokens, row markers and hidden-element opens are consumed here; false when the token is done.</summary>
    private static bool HandleHiddenTextAndRowTokens(ParseBlocksState pb, Token tok)
    {
        if (pb.hiddenTag is not null)
        {
            if (tok.Kind == TokenKind.Tag && !tok.IsSelfClosing
                && tok.Tag!.Equals(pb.hiddenTag, StringComparison.OrdinalIgnoreCase))
            {
                if (tok.IsClose) { if (--pb.hiddenDepth == 0) pb.hiddenTag = null; }
                else pb.hiddenDepth++;
            }
            return false;
        }
        if (tok.Kind == TokenKind.Text)
        {
            // Text inside a <textarea> is the field's value, not flow content.
            if (pb.inTextarea || pb.inSelect)
            {
                if (pb.inSelectedOption) pb.selectedText.Append(DecodeEntities(tok.Value));
                else if (pb.inTextarea) pb.textareaText.Append(DecodeEntities(tok.Value));
                if (pb.inSelect && !pb.inTextarea) pb.curOptionText.Append(DecodeEntities(tok.Value));
                return false;
            }
            if (pb.inButton) { pb.buttonText.Append(DecodeEntities(tok.Value)); return false; }
            pb.currentText.Append(DecodeEntities(tok.Value));
            return false;
        }
        pb.tag = tok.Tag!;
        if (SkipTags.Contains(pb.tag)) return false;
        if (pb.tag.Equals("rowmark", StringComparison.OrdinalIgnoreCase))
        {
            // Placeholder for a prebuilt styled-run row (see ExtractRowBlocks).
            if (!tok.IsClose && pb.rowBlocks is not null && tok.Attributes is not null
                && tok.Attributes.TryGetValue("i", out var riStr)
                && int.TryParse(riStr, out var ri) && ri >= 0 && ri < pb.rowBlocks.Count)
            {
                Flush(pb, pb.controlBoxes, pb.uaBlockRhythm, pb.articleRhythm, pb.spanPtTypography, false, pb.styleStack.Peek());
                // (an inline-block row lays out inside its container's box: the insets the
                // open styles carry travel with the placeholder)
                var rowBlock = pb.rowBlocks[ri];
                if (rowBlock.InlineRow is not null)
                {
                    var rowStyle = pb.styleStack.Peek();
                    rowBlock.LeftIndent = rowStyle.LeftIndent;
                    rowBlock.RightInsetPt = rowStyle.RightInsetPt;
                    // the container's pending padding-top spaces the row, as it would a block
                    if (rowStyle.PadTop > 0)
                    {
                        rowBlock.InlineRow!.PadTopPt += rowStyle.PadTop;
                        rowStyle.PadTop = 0;
                    }
                }
                pb.blocks.Add(rowBlock);
            }
            return false;
        }
        if (!tok.IsClose && IsHiddenElement(pb.tag, tok.Attributes, pb.css))
        {
            if (!tok.IsSelfClosing && !VoidTags.Contains(pb.tag))
            {
                pb.hiddenTag = pb.tag;
                pb.hiddenDepth = 1;
            }
            return false;
        }
        return true;
    }

    /// <summary>The parse's collections, run trackers and depth marks start empty.</summary>
    private static void ResetParseState(ParseBlocksState pb)
    {
        pb.rawAnchors = new List<(int start, int end, string url)>();
        pb.openAnchors = new Stack<(int start, string url)>();
        pb.pendingAnchorNames = new List<string>();
        pb.pendingMarker = null;
        pb.uaFontSaves = new Stack<(double Fs, Color? Fore, string? Fam)>();
        pb.uaSpanSizeSaves = new Stack<(int Depth, double Fs)>();
        pb.uaSpanSizeRestorePt = 0;
        pb.emphasisSave = null;
        pb.emphasisRestorePending = false;
        pb.pendingMarkerAfter = false;
        pb.inlineDivDepth = 0;
        pb.spanDepth = 0;
        pb.blockSpanDepths = new Stack<int>();
        pb.absSpanLeftPt = -1;
        pb.absSpanLabelIdx = -1;
        pb.pendingEmptyPMarginPt = 0;
        pb.ptyLeadFs = 0;
        pb.pOpenDepth = 0;
        pb.fsLegendSave = (0.0, "F1");
        pb.fsInLegend = false;
        pb.openTitleColW = 0;
        pb.openTitleColFrac = 0;
        pb.pendingColIndentFrac = 0;
        pb.titleColSpanDepth = -1;
        pb.pendingColIndent = 0;
        pb.keepTrailingSpace = false;
        pb.emptyDivDepthMark = -1;
        pb.emptyDivBlocksAt = 0;
        pb.emptyDivTextAt = 0;
        pb.pendingBoxPadTop = 0;
        pb.pendingBoxHeight = 0;
        pb.pendingBorderBox = null;
        pb.pendingBorderBoxDepth = 0;
        pb.inTextarea = false;
        pb.inSelect = false;
        pb.inSelectedOption = false;
        pb.textareaBlock = null;
        pb.textareaText = new StringBuilder();
        pb.selectedText = new StringBuilder();
        pb.selectOptions = new List<string>();
        pb.curOptionText = new StringBuilder();
        pb.selectName = null;
        pb.inlineRunId = 0;
        pb.nextInlineRunId = 1;
        pb.runPrevWasControl = false;
        pb.italicOpenTextLen = -1;
        pb.inButton = false;
        pb.buttonText = new StringBuilder();
        pb.pendingInlineIcon = false;
        pb.pendingPageBreak = false;
        pb.hiddenTag = null;
        pb.hiddenDepth = 0;
        pb.centerDepth = 0;
        pb.rawBolds = new List<(int start, int end)>();
        pb.inlineBoldDepth = 0;
        pb.inlineBoldStart = -1;
        pb.rawUnders = new List<(int start, int end)>();
        pb.uaUnderSpanDepths = new Stack<int>();
        pb.styleBoldSpanDepths = new Stack<int>();
        pb.styleItalicSpanDepths = new Stack<int>();
        pb.inlineUnderDepth = 0;
        pb.inlineUnderStart = -1;
        pb.inlineStrikeDepth = 0;
        pb.inlineStrikeStart = -1;
        pb.rawItalics = new List<(int start, int end)>();
        pb.inlineItalicDepth = 0;
        pb.inlineItalicStart = -1;
        // …and the sheet-typography flow draws its emphasis as runs in the sheet's face.
        pb.trackBoldRuns = pb.browserUa || pb.inlineEmphasisRuns || pb.sheetElementTypography || pb.wordMail;
        pb.rawColorRuns = new List<(int start, int end, Color c)>();
        pb.openColorRuns = new Stack<(int depth, int start, Color c, Color? prev)>();
        pb.rawSizeRuns = new List<(int start, int end, double pt)>();
        pb.openSizeRuns = new Stack<(int depth, int start, double pt)>();
        pb.rawFamilyRuns = new List<(int start, int end, string fam)>();
        pb.openFamilyRuns = new Stack<(int depth, int start, string fam)>();
        pb.openSubSupRuns = new Stack<int>();
        pb.bgSpans = new Stack<(int idx, Color c, double w, double frac, double padT, double padB, double padL, int depth)>();
        pb.rawDecorRuns = new List<(int start, int end, int kind, Color? c)>();
        pb.openDecorRuns = new Stack<(int depth, int start, int kind, Color? c)>();
        pb.divClassStack = new List<string>();
        pb.divTagStack = new List<string>();
        pb.divIdStack = new List<string>();
        pb.divStyleStack = new List<string>();
    }

    /// <summary>Comments, conditional comments, scripts, styles and a leading byte-order mark leave the markup before it is tokenised.</summary>
    private static string NormaliseHtmlSource(string html)
    {
        // (a title is never content, wherever a headless document leaves it)
        html = Regex.Replace(html, @"<(script|style|head|title)[^>]*>[\s\S]*?</\1>", "", RegexOptions.IgnoreCase);
        // Strip DOCTYPE, comments and CDATA sections — the tag tokenizer
        // below only recognises <Name …> shapes, so these would otherwise
        // surface as literal text content.
        html = Regex.Replace(html, @"<!DOCTYPE[^>]*>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<!--[\s\S]*?-->", "");
        html = Regex.Replace(html, @"<!\[CDATA\[[\s\S]*?\]\]>", "");
        // Word's downlevel-revealed conditional comments (`<![if !supportLists]>` …
        // `<![endif]>`) are markup around content a browser shows; the markers
        // themselves are never text.
        html = Regex.Replace(html, ConditionalCommentMarker, "", RegexOptions.IgnoreCase);
        // XML processing instructions (<?xml …?> prolog of an XHTML file) are markup,
        // not text — a browser never renders them.
        html = Regex.Replace(html, @"<\?[\s\S]*?\?>", "");
        // Strip leading BOM if present — UTF-8 HTMLs often ship with one.
        if (html.Length > 0 && html[0] == '\uFEFF') html = html.Substring(1);
        return html;
    }

    /// <summary>With control boxes on, adjacent control blocks merge into one; the merged list when that happened, otherwise null.</summary>
    private static List<Block>? MergeControlBoxBlocks(ParseBlocksState pb)
    {
        if (pb.controlBoxes)
        {
            var merged = new List<Block>(pb.blocks.Count);
            for (int i = 0; i < pb.blocks.Count; i++)
            {
                var b = pb.blocks[i];
                if (b.InlineRunId > 0)
                {
                    int j = i;
                    while (j + 1 < pb.blocks.Count && pb.blocks[j + 1].InlineRunId == b.InlineRunId) j++;
                    if (j > i)
                    {
                        var items = new List<Block>();
                        for (int k = i; k <= j; k++) items.Add(pb.blocks[k]);
                        merged.Add(new Block { InlineItems = items, FontSize = b.FontSize });
                        i = j;
                        continue;
                    }
                }
                merged.Add(b);
            }
            return merged;
        }
        return null;
    }

    /// <summary>The last run flushes, trailing hard breaks drop, and a pending page break lands on the last block.</summary>
    private static void FinishBlocks(ParseBlocksState pb)
    {
        Flush(pb, pb.controlBoxes, pb.uaBlockRhythm, pb.articleRhythm, pb.spanPtTypography, false,pb.styleStack.Peek());
        // Drop trailing hard-break spacers so the doc doesn't grow a blank
        // tail page for HTML that ends with close-tags.
        // Drop trailing spacer-only hardbreaks so HTML that ends with close-tags
        // doesn't grow a blank tail page. Hardbreaks with an explicit CSS
        // height are intentional layout spacers — keep those.
        // (a fieldset frame marker is not a break: it stays so the frame can close)
        while (pb.blocks.Count > 0 && pb.blocks[^1].IsHardBreak && pb.blocks[^1].ExplicitHeight <= 0 && pb.blocks[^1].FsBox == 0
               && pb.blocks[^1].BgSpan == 0)
            pb.blocks.RemoveAt(pb.blocks.Count - 1);
        // A page-break still pending at the segment boundary (the following content is a
        // <table> segment parsed separately): emit a break-carrier block so the table
        // starts on the fresh page.
        if (pb.pendingPageBreak)
            pb.blocks.Add(new Block { Text = "", IsHardBreak = true, PageBreakBefore = true });
    }

    /// <summary>A UA span's own font-size: an em or percent of the size it opens in, else the CSS size as it stands.</summary>
    private static double? UaSpanFontSizePt(string value, double enclosingPt)
    {
        var rel = Regex.Match(value, @"^(\d+(?:\.\d+)?|\.\d+)\s*(em|%)$", RegexOptions.IgnoreCase);
        if (rel.Success && enclosingPt > 0)
        {
            var n = double.Parse(rel.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            return rel.Groups[2].Value == "%" ? n / 100.0 * enclosingPt : n * enclosingPt;
        }
        return TryParseCssFontSize(value);
    }

    /// <summary>A browser-UA span's inline style attribute: colour, size, face, weight and decoration saved and restored around the span.</summary>
    private static void ApplyUaSpanStyleAttribute(ParseBlocksState pb, Token tok)
    {
        if (pb.browserUa && tok.Attributes is not null
            && tok.Attributes.TryGetValue("style", out var uaSpSt) && uaSpSt is not null)
        {
            // quote entities decode BEFORE the property scan — the ';'
            // inside &quot; would otherwise truncate a value mid-entity
            // (font-family: &quot;Tahoma&quot; parsed as '&quot')
            if (uaSpSt.IndexOf('&') >= 0)
                uaSpSt = uaSpSt.Replace("&quot;", "\"").Replace("&#34;", "\"")
                               .Replace("&apos;", "'").Replace("&#39;", "'");
            var uaTop = pb.styleStack.Peek();
            var fsM = Regex.Match(uaSpSt, @"font-size\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
            // (an em or percent resolves against the size the span opens in - MEASURED, the evaluation
            //  form: the H1's `font-size: .8em` span is 11.44 = .8 x the 14.3 heading, not .8 x the body)
            if (fsM.Success && UaSpanFontSizePt(fsM.Groups[1].Value.Trim(), uaTop.FontSize) is { } uaSpFs)
            {
                // The size styles the whole line the span is on; its close puts the
                // replaced size back for the lines after (probed: a 24px heading span
                // followed by <br><hr> leaves the next paragraph at the body's 12 pt).
                // A sized span opening while a closed one's restore still waits
                // (adjacent spans on one line) takes that waiting size as its own
                // base, so the line between them keeps the size it was set.
                if (pb.tagCmp == "span" && !tok.IsSelfClosing)
                {
                    pb.uaSpanSizeSaves.Push((pb.spanDepth,
                        pb.uaSpanSizeRestorePt > 0 ? pb.uaSpanSizeRestorePt : uaTop.FontSize));
                    pb.uaSpanSizeRestorePt = 0;
                }
                uaTop.FontSize = uaSpFs;
            }
            var lhM = Regex.Match(uaSpSt, @"line-height\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
            if (lhM.Success && double.TryParse(lhM.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var uaLhPx))
                uaTop.LineBoxPt = uaLhPx * 0.75;
            // …and a percentage one resolves against the span's own size
            // (which the same style attribute set just above).
            var lhPctM = Regex.Match(uaSpSt, @"line-height\s*:\s*([\d.]+)\s*%", RegexOptions.IgnoreCase);
            if (lhPctM.Success && double.TryParse(lhPctM.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var uaLhPct)
                && uaLhPct > 0 && uaTop.FontSize > 0)
                uaTop.LineBoxPt = uaLhPct / 100.0 * uaTop.FontSize;
            // A span's RESOLVABLE font-family styles its element's runs,
            // exactly like a <font face> (Word-filtered markup carries the
            // face on spans).
            var famM = Regex.Match(uaSpSt, @"font-family\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
            if (famM.Success && FirstFontFamily(famM.Groups[1].Value) is { Length: > 0 } uaSpFam
                && WinMetricsFor(uaSpFam) is not null)
                uaTop.FontFamily = uaSpFam;
            // text-decoration: underline on an inline span opens an
            // underline run over the span's extent (its </span> closes it).
            if (pb.tagCmp == "span" && !tok.IsSelfClosing
                && Regex.IsMatch(uaSpSt, @"text-decoration\s*:\s*[^;]*\bunderline",
                    RegexOptions.IgnoreCase))
            {
                if (pb.inlineUnderDepth++ == 0) pb.inlineUnderStart = pb.currentText.Length;
                pb.uaUnderSpanDepths.Push(pb.spanDepth);
            }
            var mlM = Regex.Match(uaSpSt, @"margin-left\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
            if (mlM.Success && double.TryParse(mlM.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var uaMlPx))
                uaTop.TextInsetPt += uaMlPx * 0.75;
        }
    }

    /// <summary>The span-pt typography dialect's span sizing and the browser-UA font tag's face, size and colour.</summary>
    private static void ApplySpanPtTypographyAndFontTag(ParseBlocksState pb, Token tok)
    {
        if (pb.spanPtTypography && pb.tagCmp is "span" && !tok.IsSelfClosing
            && tok.Attributes is { } ptyAttrs
            && ptyAttrs.TryGetValue("style", out var ptySt) && ptySt is not null)
        {
            var ptyTop = pb.styleStack.Peek();
            if (pb.wordMail)
                pb.wmSpanSaves.Push((pb.spanDepth, pb.currentText.Length, ptyTop.FontSize, ptyTop.FontFamily, ptyTop.ForeColor, ptyTop.EmBold, ptyTop.EmItalic));
            // (the `font:` shorthand sizes a span as font-size does - the Word export's
            // 7 pt nbsp filler between a list label and its text)
            var ptyFs = Regex.Match(ptySt, @"font-size\s*:\s*([\d.]+)\s*pt", RegexOptions.IgnoreCase);
            if (!ptyFs.Success)
                ptyFs = Regex.Match(ptySt, @"(?<![-\w])font\s*:\s*([\d.]+)\s*pt", RegexOptions.IgnoreCase);
            if (ptyFs.Success
                && double.TryParse(ptyFs.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var ptyPt)
                && ptyPt > 0)
            {
                ptyTop.FontSize = ptyPt;
                // First sized span of the block-in-progress: its size owns
                // the first line box (a leading 10 pt marker over 8 pt text).
                if (pb.currentText.ToString().Trim().Length == 0)
                    pb.ptyLeadFs = ptyPt;
                // The Word export draws a sized span as a run of its own size inside the
                // element's block (an 8 pt bullet and a 7 pt filler before 16 pt text).
                if (pb.wordExport)
                    pb.openSizeRuns.Push((pb.spanDepth, pb.currentText.Length, ptyPt));
            }
            // …and a span's face (font-family, or the family of its `font:` shorthand) as a run
            // of that face: the list label in Symbol / Wingdings, the filler in Times New Roman.
            if (pb.wordExport && SpanRunFamily(ptySt) is { } ptyFam)
                pb.openFamilyRuns.Push((pb.spanDepth, pb.currentText.Length, ptyFam));
            if (Regex.IsMatch(ptySt, @"font-weight\s*:\s*(bold|[7-9]00)",
                    RegexOptions.IgnoreCase))
                ptyTop.EmBold = true;
            if (Regex.IsMatch(ptySt, @"font-style\s*:\s*italic",
                    RegexOptions.IgnoreCase))
                ptyTop.EmItalic = true;
            if (Regex.IsMatch(ptySt, @"font-variant\s*:\s*small-caps",
                    RegexOptions.IgnoreCase))
                ptyTop.SmallCaps = true;
            if (Regex.Match(ptySt, @"letter-spacing\s*:\s*(-?[\d.]+)\s*pt",
                    RegexOptions.IgnoreCase) is { Success: true } ptyLs
                && double.TryParse(ptyLs.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var ptyLsv))
                ptyTop.LetterSpacingPt = ptyLsv;
        }
        // Inline <span style="font-family:…"> / <font face="…"> selects a
        // custom face for the enclosed run (resolved+embedded at layout).
        MarkInlineFontFamily(pb.styleStack, tok.Attributes);
        // UA-serif flow: a <font size=N> sizes the rest of its block
        // through the legacy 1..7 ladder (measured: size2 draws 9.75,
        // size3 12, size4 13.5); its color attribute tints the run.
        if (pb.browserUa && pb.tagCmp == "font" && tok.Attributes is { } uaFa)
        {
            var uaFTop = pb.styleStack.Peek();
            if (!tok.IsSelfClosing)
                pb.uaFontSaves.Push((uaFTop.FontSize, uaFTop.ForeColor, uaFTop.FontFamily));
            if (uaFa.TryGetValue("size", out var uaFsAttr)
                && TryParseHtmlFontSize(uaFsAttr) is { } uaFsPt)
                uaFTop.FontSize = uaFsPt;
            if (uaFa.TryGetValue("color", out var uaFcAttr)
                && ParseCssColor(uaFcAttr.Trim()) is { } uaFCol)
                uaFTop.ForeColor = uaFCol;
            // A RESOLVABLE face draws its runs in the named family
            // (embedded at layout); unknown faces keep the UA serif.
            if (uaFa.TryGetValue("face", out var uaFfAttr)
                && FirstFontFamily(uaFfAttr) is { Length: > 0 } uaFfName
                && WinMetricsFor(uaFfName) is not null)
                uaFTop.FontFamily = uaFfName;
        }
    }

    /// <summary>A span's class colour, decoration, inline colour and emphasis attributes open the runs the text extraction reads back.</summary>
    private static void TrackSpanColourAndEmphasisRuns(ParseBlocksState pb, Token tok)
    {
        OpenSpanClassSizeRun(pb, tok);
        OpenSpanChainSizeRun(pb, tok);
        OpenInlineClassTypographyRuns(pb, tok);
        if (pb.trackBoldRuns && (pb.spanPtTypography || pb.sheetElementTypography) && pb.tagCmp == "span" && !tok.IsSelfClosing
            && pb.css is not null && tok.Attributes is { } clsColAttrs
            && !(clsColAttrs.TryGetValue("style", out var clsInlSt) && clsInlSt is not null
                && Regex.IsMatch(clsInlSt, @"(?<![-\w])color\s*:", RegexOptions.IgnoreCase))
            && clsColAttrs.TryGetValue("class", out var clsColCls) && clsColCls is not null)
            foreach (var sc in clsColCls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!pb.css.TryGetValue("." + sc, out var ccr))
                    pb.css.TryGetValue("span." + sc, out ccr);
                if (ccr is not null && ccr.TryGetValue("color", out var ccv)
                    && ParseCssColor(ccv.Trim()) is { } ccc)
                {
                    pb.openColorRuns.Push((pb.spanDepth, pb.currentText.Length, ccc,
                        pb.styleStack.Peek().ForeColor));
                    break;
                }
            }
        // Redline decoration runs: a span's text-decoration and its
        // diff-marker class's border-bottom underline are ink runs
        // scoped to the span (strike bars, solid red and dotted blue
        // underlines of the review markers).
        if (pb.trackBoldRuns && pb.spanPtTypography && pb.tagCmp == "span" && !tok.IsSelfClosing
            && tok.Attributes is { } decAttrs)
        {
            void OpenDecor(int kind, Color? dcol) =>
                pb.openDecorRuns.Push((pb.spanDepth, pb.currentText.Length, kind, dcol));
            if (decAttrs.TryGetValue("style", out var decSt) && decSt is not null
                && Regex.Match(decSt, @"text-decoration\s*:\s*([^;]+)",
                    RegexOptions.IgnoreCase) is { Success: true } tdm)
            {
                var tdv = tdm.Groups[1].Value;
                if (tdv.Contains("underline", StringComparison.OrdinalIgnoreCase)) OpenDecor(1, null);
                if (tdv.Contains("line-through", StringComparison.OrdinalIgnoreCase)) OpenDecor(2, null);
            }
            if (pb.css is not null && decAttrs.TryGetValue("class", out var decCls) && decCls is not null)
                foreach (var sc in decCls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!pb.css.TryGetValue("." + sc, out var dcr))
                        pb.css.TryGetValue("span." + sc, out dcr);
                    if (dcr is null) continue;
                    if (dcr.TryGetValue("text-decoration", out var dtd))
                    {
                        if (dtd.Contains("underline", StringComparison.OrdinalIgnoreCase)) OpenDecor(1, null);
                        if (dtd.Contains("line-through", StringComparison.OrdinalIgnoreCase)) OpenDecor(2, null);
                    }
                    if (dcr.TryGetValue("border-bottom", out var dbb)
                        && !dbb.Contains("none", StringComparison.OrdinalIgnoreCase))
                        OpenDecor(dbb.Contains("dashed", StringComparison.OrdinalIgnoreCase)
                            || dbb.Contains("dotted", StringComparison.OrdinalIgnoreCase) ? 4 : 3,
                            ParseCssColor(dbb));
                }
        }
        // A span's own inline color opens a COLOUR RUN in the run-tracked
        // flows — scoped to the span, not styled onto
        // the rest of the block.
        if (pb.trackBoldRuns && pb.tagCmp == "span" && !tok.IsSelfClosing
            && tok.Attributes is { } spanColAttrs
            && spanColAttrs.TryGetValue("style", out var spanColSt)
            && spanColSt is not null
            && Regex.Match(spanColSt, @"(?<![-\w])color\s*:\s*([^;]+)",
                RegexOptions.IgnoreCase) is { Success: true } spanColM
            && ParseCssColor(spanColM.Groups[1].Value.Trim()) is { } spanRunCol)
            pb.openColorRuns.Push((pb.spanDepth, pb.currentText.Length, spanRunCol,
                pb.styleStack.Peek().ForeColor));
        // …and its own font-weight / font-style open an emphasis run over
        // exactly the span's extent — the same model as <b> and <i>, which is
        // what a browser applies. Without this the declaration is lost: the
        // whole-block promotion cannot express "these words only".
        if (pb.trackBoldRuns && pb.tagCmp == "span" && !tok.IsSelfClosing
            && tok.Attributes is { } spanEmAttrs
            && spanEmAttrs.TryGetValue("style", out var spanEmSt)
            && spanEmSt is not null)
        {
            if (Regex.IsMatch(spanEmSt, @"(?<![-\w])font-weight\s*:\s*(bold|bolder|[7-9]00)",
                    RegexOptions.IgnoreCase))
            {
                if (pb.inlineBoldDepth++ == 0) pb.inlineBoldStart = pb.currentText.Length;
                pb.styleBoldSpanDepths.Push(pb.spanDepth);
            }
            if (Regex.IsMatch(spanEmSt, @"(?<![-\w])font-style\s*:\s*(italic|oblique)",
                    RegexOptions.IgnoreCase))
            {
                if (pb.inlineItalicDepth++ == 0) pb.inlineItalicStart = pb.currentText.Length;
                pb.styleItalicSpanDepths.Push(pb.spanDepth);
            }
        }
    }

    /// <summary>A span open registers its class rules, its absolute-span ledger entry and its class typography.</summary>
    /// <summary>Whether the span's own style or one of its classes' sheet rules declares `unicode-bidi: isolate`.</summary>
    private static bool SpanIsBidiIsolate(ParseBlocksState pb, Token tok)
    {
        if (tok.Attributes!.TryGetValue("style", out var biSt) && biSt is not null
            && Regex.IsMatch(biSt, @"unicode-bidi\s*:\s*isolate", RegexOptions.IgnoreCase)) return true;
        if (pb.css is null || !tok.Attributes.TryGetValue("class", out var biCls) || biCls is null) return false;
        foreach (var c in biCls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            if (pb.css.TryGetValue("." + c, out var rule) && rule.TryGetValue("unicode-bidi", out var ub)
                && ub.Trim().Equals("isolate", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static void OpenSpanClassAndLedger(ParseBlocksState pb, Token tok)
    {
        OpenSpanTypography(pb, tok);
        // Ledger span classes (browser-UA flow): a margin-left class insets
        // the label run's line; a position:absolute+left class makes the
        // span its OWN column block, seated on the SAME line as the label
        // that precedes it.
        if (pb.absSpanLedger && pb.browserUa && pb.tagCmp == "span" && !tok.IsSelfClosing
            && pb.css is not null && tok.Attributes is not null
            && tok.Attributes.TryGetValue("class", out var lgCls) && lgCls is not null)
            foreach (var sc in lgCls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!pb.css.TryGetValue("." + sc, out var scr)) continue;
                if (scr.TryGetValue("position", out var scPos)
                    && scPos.Contains("absolute", StringComparison.OrdinalIgnoreCase)
                    && scr.TryGetValue("left", out var scLeft)
                    && TryParseLength(scLeft) is { } scLeftPt)
                {
                    Flush(pb, pb.controlBoxes, pb.uaBlockRhythm, pb.articleRhythm, pb.spanPtTypography, false, pb.styleStack.Peek());
                    pb.absSpanLabelIdx = -1;
                    if (pb.blocks.Count > 0 && !pb.blocks[^1].IsHardBreak
                        && pb.blocks[^1].Text.Length > 0)
                    {
                        pb.blocks[^1].NoAdvanceY = true;
                        pb.absSpanLabelIdx = pb.blocks.Count - 1;
                    }
                    pb.absSpanLeftPt = scLeftPt;
                }
                else if (scr.TryGetValue("margin-left", out var scMl)
                         && TryParseLength(scMl) is { } scMlPt)
                    pb.styleStack.Peek().TextInsetPt += scMlPt;
            }
        // The pt-report flow: a span CLASS's typography (font-size,
        // weight) styles the rest of its block — the report's .title
        // span (rules resolve bare and tag-prefixed).
        if (pb.spanClassTypography && pb.tagCmp == "span" && !tok.IsSelfClosing
            && pb.css is not null && tok.Attributes is not null
            && tok.Attributes.TryGetValue("class", out var tySpCls) && tySpCls is not null)
            foreach (var sc in tySpCls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!pb.css.TryGetValue("." + sc, out var tyScr))
                    pb.css.TryGetValue("span." + sc, out tyScr);
                if (tyScr is null) continue;
                var tyTop = pb.styleStack.Peek();
                if (tyScr.TryGetValue("font-size", out var tyFs)
                    && TryParseCssFontSize(tyFs.Trim()) is { } tyFsPt)
                    tyTop.FontSize = tyFsPt;
                if (tyScr.TryGetValue("font-weight", out var tyFw)
                    && tyFw.Contains("bold", StringComparison.OrdinalIgnoreCase))
                { tyTop.FontRes = "F2"; tyTop.EmBold = true; }
            }
    }

    /// <summary>Sheet-typography flow: a span CLASS's font-size opens a SIZE RUN over the span's
    /// extent (probed: `.ms-rteFontSize-4 { font-size: 18pt }` draws its words at 18 inside a
    /// 9.75 pt paragraph, the line box growing to the run).</summary>
    /// <summary>UA flow: a sheet's descendant chain ending in the span (`ul li span { font-size: 10pt }`)
    /// sizes the span's own extent as a run, the way its class would (probed on the state analysis:
    /// the list items' spans draw at 10 pt under a 12 pt list).</summary>
    private static void OpenSpanChainSizeRun(ParseBlocksState pb, Token tok)
    {
        if (!pb.browserUa || pb.tagCmp != "span" || tok.IsSelfClosing || pb.chainRules is null) return;
        if (tok.Attributes is { } a && a.TryGetValue("class", out var cls) && !string.IsNullOrWhiteSpace(cls)) return;
        if (MatchChainDecls(pb.chainRules, OpenElementChain(pb, tok, "span")) is { } chained
            && chained.TryGetValue("font-size", out var chFs) && TryParseCssFontSize(chFs.Trim()) is { } chPt && chPt > 0)
            pb.openSizeRuns.Push((pb.spanDepth, pb.currentText.Length, chPt));
    }

    private static void OpenSpanClassSizeRun(ParseBlocksState pb, Token tok)
    {
        if (!pb.sheetElementTypography || pb.tagCmp != "span" || tok.IsSelfClosing
            || pb.css is null || tok.Attributes is not { } szAttrs
            || !szAttrs.TryGetValue("class", out var szCls) || szCls is null) return;
        foreach (var sc in szCls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!pb.css.TryGetValue("." + sc, out var szr))
                pb.css.TryGetValue("span." + sc, out szr);
            if (szr is not null && szr.TryGetValue("font-size", out var szv)
                && TryParseCssFontSize(szv.Trim()) is { } szPt && szPt > 0)
            {
                pb.openSizeRuns.Push((pb.spanDepth, pb.currentText.Length, szPt));
                break;
            }
        }
    }

    /// <summary>UA flow: an inline element's CLASS typography styles exactly its own extent,
    /// as RUNS, leaving the text either side of it at the block's own size and weight
    /// (probed: `.w { font-weight: bold }` on `Alpha <label class="w">Bravo</label> Charlie`
    /// draws one bold fragment between two regular ones; `font-size: 15px` draws Bravo at
    /// 11.25 pt; a numeric 900 counts as bold exactly as the keyword does; and `!important`
    /// changes nothing, being a cascade rank rather than a value). A `label` behaves as a
    /// `span` does - the reference makes no distinction between the two.</summary>
    private static void OpenInlineClassTypographyRuns(ParseBlocksState pb, Token tok)
    {
        if (!pb.browserUa || tok.IsSelfClosing || pb.css is null
            || pb.tagCmp is not ("span" or "label")
            || tok.Attributes is not { } clsAttrs
            || !clsAttrs.TryGetValue("class", out var clsNames) || clsNames is null) return;
        double sizePt = 0;
        var bold = false;
        string? family = null;
        foreach (var sc in clsNames.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!pb.css.TryGetValue("." + sc, out var rule))
                pb.css.TryGetValue(pb.tagCmp + "." + sc, out rule);
            if (rule is null) continue;
            // (…and its resolvable family: the enterprise summary's `.title { Arial }` lines draw
            // Arial bold in the UA serif flow)
            if (family is null && rule.TryGetValue("font-family", out var ffv)
                && FirstFontFamily(StripImportant(ffv)) is { Length: > 0 } fam && WinMetricsFor(fam) is not null)
                family = fam;
            if (sizePt <= 0 && rule.TryGetValue("font-size", out var fsv)
                && TryParseCssFontSize(StripImportant(fsv)) is { } fsPt && fsPt > 0)
                sizePt = fsPt;
            if (!bold && rule.TryGetValue("font-weight", out var fwv))
                bold = IsBoldFontWeight(StripImportant(fwv));
        }
        // At most one run of each kind per element: the close pops one entry at this
        // depth, so a second push would outlive its element.
        if (sizePt > 0)
            pb.openSizeRuns.Push((pb.spanDepth, pb.currentText.Length, sizePt));
        if (family is not null)
            pb.openFamilyRuns.Push((pb.spanDepth, pb.currentText.Length, family));
        if (bold && pb.trackBoldRuns)
        {
            if (pb.inlineBoldDepth++ == 0) pb.inlineBoldStart = pb.currentText.Length;
            pb.styleBoldSpanDepths.Push(pb.spanDepth);
        }
    }

    /// <summary>A declaration's value without its `!important` cascade suffix.</summary>
    private static string StripImportant(string v)
    {
        var ix = v.IndexOf("!important", StringComparison.OrdinalIgnoreCase);
        return (ix >= 0 ? v.Substring(0, ix) : v).Trim();
    }

    /// <summary>CSS font-weight values a browser draws in the face's bold cut.</summary>
    private static bool IsBoldFontWeight(string v)
    {
        v = v.Trim();
        return v.Equals("bold", StringComparison.OrdinalIgnoreCase)
            || v.Equals("bolder", StringComparison.OrdinalIgnoreCase)
            || (int.TryParse(v, out var numeric) && numeric >= 600);
    }
}
