using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    private static bool HasDescendant(HtmlNode node, string tag)
    {
        foreach (var d in node.Descendants())
            if (d.Tag == tag) return true;
        return false;
    }

    // A control with no usable CSS size still occupies its INTRINSIC box — the character
    // grid its size/cols attribute declares (20 columns by default), one row per `rows`:
    // 5.852 pt per column, a first row of 15.75 pt (21 px) and
    // 11.25 pt for each further row; a textarea's box is one column wider than its `cols`
    // (the scrollbar gutter).
    private const double ControlColWidthPt = 5.852;

    private const double ControlFirstRowPt = 15.75;

    private const double ControlNextRowPt = 11.25;

    // …and it ADVANCES the flow by less than it draws: the box hangs 1.5 pt over the line
    // that follows it, so a label/control pair costs 13.5 + 14.25, not 13.5 + 15.75.
    private const double ControlFirstRowAdvancePt = 14.25;

    // Where the control box sits on its line: the top edge rides above the text
    // baseline, so the box straddles the line rather than hanging under it.
    private const double InputBoxAboveBaselinePt = 11.34;

    private const double SelectBoxAboveBaselinePt = 11.55;

    private const double SelectBoxHeightPt = 16.17;

    // A combo box is as wide as its WIDEST option (10 pt UI face) plus the dropdown
    // arrow and padding; the selected text alone does not size it.
    private const double SelectChromePt = 18.25;

    // …and it keeps a hairline side bearing on both sides of the pen.
    private const double SelectSideBearingPt = 0.25;

    // A control line that also carries body text advances a touch further than a
    // control alone: the text descent clears the box bottom.
    private const double InlineMixedExtraPt = 0.2;

    // The escaped-attr dialect's UA base font size (16 px = 12 pt).
    private const double EscapedBodyFontPt = 12;

    // The UA default body margin: 8px at 0.75 pt/px. A body-level element's box
    // sits this far inside the page content origin on both axes.
    private const double UaBodyMarginPt = 6.0;

    // What a DOCTYPE adds to the UA-serif flow's first seat when the document
    // OPENS with a default-size paragraph: standards mode charges the leading
    // <p>'s UA top margin at the canvas where the quirks calibration collapses
    // it (measured A/B: first baseline 96.24 with the
    // doctype vs the calibrated 88.80 without, same body).
    private const double UaDoctypeLeadParagraphPt = 96.24 - 88.80;

    // MediaWiki export rhythm, measured on the expected render of
    // the saved Main Page (era-stable against the shipped templates):
    // the dropdown label indents 15 (line at x 111 on the 96 origin) and its
    // cdx-button line box leads 1.2 over the bare 13.5 line; the pin-button
    // widget line leads 0.6 and hands 1.7 to the next block; the welcome
    // banner is 162% of the 12 pt UA base; a block after a list opens the
    // same 26.9 paragraph gap the list opened with (13.4 over the bare line).
    private const double WikiLabelIndentPt = 15.0;
    /// <summary>What a dropdown label hands back of the previous paragraph's
    /// bottom margin, net of its own taller line box (probed: 12 − 1.2).</summary>
    private const double WikiLabelParaCancelPt = 10.8;
    private const double WikiButtonLeadPt = 0.6;
    private const double WikiAfterButtonsPt = 1.7;
    private const double WikiBannerPt = 12.0 * 1.62;
    private const double WikiAfterListGapPt = 26.9 - 13.5;
    /// <summary>The sidebar logo's undrawn box (probed: the list above it to the
    /// Search heading spans 43.1 = the 26.9 gap + this).</summary>
    private const double WikiLogoBoxPt = 16.2;
    /// <summary>The search input widget's box below its text line (probed:
    /// the input line hands 29.9 to the next heading, a bare line 14.7).</summary>
    private const double WikiAfterSearchPt = 15.2;
    /// <summary>The welcome banner's lead over a bare line (probed: the button
    /// row hands 32.7 to the 162% heading where a text line takes 13.5).</summary>
    private const double WikiBannerLeadPt = 12.3;
    /// <summary>The mp-box frame's top over the heading baseline (probed).</summary>
    private const double WikiBannerBoxAbovePt = 25.0;
    /// <summary>The UA default link ink (probed: pure blue).</summary>
    private static readonly Color WikiLinkInk = Color.FromArgb(0, 0, 255);

    /// <summary>The over-declared grid document's host chrome: its filing shell
    /// wraps every grid in a cellpadding-5 (7.5 pt pair) cellspacing-1 (1.5 pt
    /// pair) cell, and ALL of the document's tables resolve inside
    /// that box (measured: the standard box is pageW − 201 at every page width —
    /// margins 96+90, the UA body gutter 6, and this pair).</summary>
    private const double OverDeclaredHostChromePt = 7.5 + 1.5;

    /// <summary>What a full-bled host hands its nested width:100% fixed-layout
    /// grid BEYOND the standard box: the right margin plus the UA body gutter
    /// (the host's band runs to the page edge and the grid fills it).</summary>
    private const double OverDeclaredBleedRightPt = 90.0 + UaBodyMarginPt;

    // A top-level table opening after a body text line sits this far below
    // the line's box (measured: table top 43.0 = line bottom 39.5
    // + 3.5, identical in the shipped template and the current render).
    private const double TableAfterTextGapPt = 3.5;

    // Gap between a UA list marker's right edge and the item's text indent,
    // in em of the item's font (probed: 4.5 at 12pt, 9 at 24pt — bullet pen
    // 117.3 = 126 − 4.2 advance − 4.5 at 12pt).
    private const double UaMarkerGapEm = 0.375;

    // Default cell chrome of a chrome-less table: border-spacing 2px +
    // cellpadding 1px = 3px at 0.75 pt/px. A single-column wrapper table's
    // flowed content sits this far inside the content origin on both axes
    // (measured: text x 98.25 = 96 + 2.25, first line top 80.25 = 78 + 2.25).
    private const double UaCellChromePt = 2.25;

    // The boundary between two ROWS of an unwrapped single-column wrapper table:
    // the closing cell's padding + the UA 2px border-spacing + the next cell's
    // padding = (0.75 + 1.5 + 0.75) pt. Probed on the licensing letter's header
    // grid: single-line rows pitch 16.5 = the 13.5 serif line + this chrome.
    private const double UaWrapperRowChromePt = 3.0;

    // The UA block margin (p/ul/…): 1.12 em of the element's
    // font (probed: nested-list offsets 7.44/9.72/14.16/20.88 = 1.12·fs − the
    // 6 pt body margin across 12/14/18/24 pt, and the mid-flow p↔ul gap 13.44).
    private const double UaBlockMarginEm = 1.12;

    // Fieldset chrome (probed on the worksheet reference): content sits 9.75 pt
    // inside the frame (margin 2px + border + 0.75em padding), the frame's
    // right pad is 8.25, and its box closes 12.82 under the last baseline.
    private const double FsPadLeftPt = 9.75;
    private const double FsPadRightPt = 8.25;
    private const double FsBoxBottomPadPt = 12.82;
    private const double FsWidenRightPt = 90.75;      // frame right edge → page edge
    private const double FsFrameGray = 0.502;         // the UA fieldset border ink
    // Frame top below a leading legend's LINE TOP: the legend's 14.4pt baseline
    // drop (13.11) + the probed 4.86 baseline→border seat.
    private const double FsLegendFrameAdjPt = 17.97;

    // Room a line needs under its baseline at the page bottom — the serif descent
    // (a line may keep its baseline as little as 2.7 pt over the margin).
    private const double SerifDescentRoomPt = 2.7;

    // A line carrying an inline broken image grows its box by the icon: its baseline
    // lands this much lower than a bare text line (rule → icon-label baseline 41.12,
    // bare 11.9, both measured).
    private const double InlineIconLineExtraPt = 29.2;

    // The first line under a section rule sits 17.9 (text) / 17.2 (inline run) below
    // it, not the bare 11.9 — headings carry their own margins instead.
    private const double RuleToTextExtraPt = 6.0;

    private const double RuleToRunExtraPt = 5.3;

    // A mid-line textarea anchors its box BOTTOM this far under the baseline and
    // grows upward.
    private const double TextareaBottomHangPt = 0.75;

    // The multiline pitch that seats a textarea's first value line 10.11 under the
    // box top (2 pt inset + the Courier ascent).
    private const double TextareaValuePitchPt = 8.11;

    // Push-button chrome: caption width + 10.4, 18.75 tall (11.5×7.5 when empty),
    // caption 5.75 in from the left edge with its baseline 12.84 under the top.
    private const double ButtonChromeWPt = 10.4;

    private const double ButtonHeightPt = 18.75;

    private const double EmptyButtonWPt = 11.5;

    private const double EmptyButtonHPt = 7.5;

    private const double ButtonCaptionInsetXPt = 5.75;

    private const double ButtonCaptionDropPt = 12.84;

    /// <summary>Advance of a run in a Standard-14 face (AFM widths).</summary>
    private static double MeasureStd14(string baseFont, string s, double pt)
    {
        double total = 0;
        foreach (var ch in s) total += Text.Standard14Fonts.GetWidth(baseFont, ch);
        return total / 1000.0 * pt;
    }

    private static (double w, double h, double adv) IntrinsicControlBox(
        Dictionary<string, string>? attrs, bool multiline)
    {
        int Attr(string n, int dflt) =>
            attrs is not null && attrs.TryGetValue(n, out var raw)
            && int.TryParse(UnescapeAttrValue(raw), out var v) && v > 0 ? v : dflt;
        var cols = multiline ? Attr("cols", 20) + 1 : Attr("size", 20);
        var rows = multiline ? Attr("rows", 2) : 1;
        var extra = (rows - 1) * ControlNextRowPt;
        return (cols * ControlColWidthPt, ControlFirstRowPt + extra,
                ControlFirstRowAdvancePt + extra);
    }

    /// <summary>Read width:/height: pixel lengths from an inline style string.</summary>
    private static (double w, double h) ParseInputSize(string? styleAttr)
    {
        double w = 0, h = 0;
        if (string.IsNullOrEmpty(styleAttr)) return (w, h);
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var wm = Regex.Match(styleAttr, @"(?:^|[;\s])width\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
        if (wm.Success) double.TryParse(wm.Groups[1].Value, System.Globalization.NumberStyles.Float, ci, out w);
        var hm = Regex.Match(styleAttr, @"(?:^|[;\s])height\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
        if (hm.Success) double.TryParse(hm.Groups[1].Value, System.Globalization.NumberStyles.Float, ci, out h);
        return (w, h);
    }

    private static List<Block> ParseBlocks(string html,
        IReadOnlyDictionary<string, Dictionary<string, string>>? css,
        IReadOnlyList<BeforeMarker>? beforeMarkers = null,
        IReadOnlyList<Block>? rowBlocks = null,
        bool metricLayout = false,
        bool uaDefaults = false,
        bool browserUa = false,
        double bodyFontSize = 0,
        bool bandDialect = false,
        bool formDialect = false,
        bool brBlankLines = false,
        bool uaBlockRhythm = false,
        bool controlBoxes = false,
        bool inlineEmphasisRuns = false,
        bool articleRhythm = false,
        bool bodyBoxRhythm = false,
        bool containerBoxIndents = false,
        bool coverStyles = false,
        bool inlineBlockCols = false,
        bool absSpanLedger = false,
        bool spanClassTypography = false,
        bool fieldsetBoxes = false,
        bool uaPMargins = false,
        bool msoParagraphs = false,
        // html5-doctype bare UA document: heading margins are the real root-em
        // values (see ApplyBlockTagStyle.html5UaHeadings).
        bool html5UaHeadings = false,
        // pt-styled fragment: an inline span's pt typography (font-size,
        // weight, italic) styles its block — the legacy flow otherwise keeps
        // its calibrated 11 pt default for span-styled paragraphs.
        bool spanPtTypography = false,
        // Pinned-body report dialect: a branded band is authored as a wrapper
        // div carrying the background/colour with an inline-block child holding
        // the text — the child keeps the wrapper's paint (CSS backgrounds do
        // not inherit, so this is scoped to the dialect).
        bool divBandBg = false,
        // DataWorks form flow: value-carrying submit inputs render as push
        // buttons (honouring the legacy align attribute).
        bool dwFlow = false,
        // Certificate float flow: headings take their UA size and margins in em of
        // the cascade rather than the legacy flows' flat points.
        bool floatFlow = false)
    {
        // Strip script/style/head bodies whole; inline tags inside them are
        // not semantic content.
        html = Regex.Replace(html, @"<(script|style|head)[^>]*>[\s\S]*?</\1>", "", RegexOptions.IgnoreCase);
        // Strip DOCTYPE, comments and CDATA sections — the tag tokenizer
        // below only recognises <Name …> shapes, so these would otherwise
        // surface as literal text content.
        html = Regex.Replace(html, @"<!DOCTYPE[^>]*>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<!--[\s\S]*?-->", "");
        html = Regex.Replace(html, @"<!\[CDATA\[[\s\S]*?\]\]>", "");
        // XML processing instructions (<?xml …?> prolog of an XHTML file) are markup,
        // not text — a browser never renders them.
        html = Regex.Replace(html, @"<\?[\s\S]*?\?>", "");
        // Strip leading BOM if present — UTF-8 HTMLs often ship with one.
        if (html.Length > 0 && html[0] == '\uFEFF') html = html.Substring(1);
        var pb = new ParseBlocksState();
        pb.tokens = Tokenize(html);

        pb.blocks = new List<Block>();
        pb.currentText = new StringBuilder();
        pb.styleStack = new Stack<BlockStyle>();
        pb.heightFloors = new Stack<(int Marker, double H, int Depth)>();
        pb.styleStack.Push(new BlockStyle
        {
            // UA base = 16px serif (12pt); a caller-set body size replaces the default.
            FontSize = bodyFontSize > 0 ? bodyFontSize : uaDefaults ? 12 : 11,
            FontRes = "F1", MarginTop = 0, MarginBottom = 0, LeftIndent = 0,
            FormDialect = formDialect,
            ArticleRhythm = articleRhythm,
            UaSerif = browserUa,
        });
        pb.rawAnchors = new List<(int start, int end, string url)>();
        pb.openAnchors = new Stack<(int start, string url)>();
        pb.pendingAnchorNames = new List<string>();
        pb.pendingMarker = null;
        pb.uaFontSaves = new Stack<(double Fs, Color? Fore, string? Fam)>();
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
        pb.rawItalics = new List<(int start, int end)>();
        pb.inlineItalicDepth = 0;
        pb.inlineItalicStart = -1;
        pb.trackBoldRuns = browserUa || inlineEmphasisRuns;
        pb.rawColorRuns = new List<(int start, int end, Color c)>();
        pb.openColorRuns = new Stack<(int depth, int start, Color c, Color? prev)>();
        pb.rawDecorRuns = new List<(int start, int end, int kind, Color? c)>();
        pb.openDecorRuns = new Stack<(int depth, int start, int kind, Color? c)>();
        pb.divClassStack = new List<string>();

        pb.closingElement = false;
        foreach (var tok in pb.tokens)
        {
            if (pb.hiddenTag is not null)
            {
                if (tok.Kind == TokenKind.Tag && !tok.IsSelfClosing
                    && tok.Tag!.Equals(pb.hiddenTag, StringComparison.OrdinalIgnoreCase))
                {
                    if (tok.IsClose) { if (--pb.hiddenDepth == 0) pb.hiddenTag = null; }
                    else pb.hiddenDepth++;
                }
                continue;
            }
            if (tok.Kind == TokenKind.Text)
            {
                // Text inside a <textarea> is the field's value, not flow content.
                if (pb.inTextarea || pb.inSelect)
                {
                    if (pb.inSelectedOption) pb.selectedText.Append(DecodeEntities(tok.Value));
                    else if (pb.inTextarea) pb.textareaText.Append(DecodeEntities(tok.Value));
                    if (pb.inSelect && !pb.inTextarea) pb.curOptionText.Append(DecodeEntities(tok.Value));
                    continue;
                }
                if (pb.inButton) { pb.buttonText.Append(DecodeEntities(tok.Value)); continue; }
                pb.currentText.Append(DecodeEntities(tok.Value));
                continue;
            }
            var tag = tok.Tag!;
            if (SkipTags.Contains(tag)) continue;
            if (tag.Equals("rowmark", StringComparison.OrdinalIgnoreCase))
            {
                // Placeholder for a prebuilt styled-run row (see ExtractRowBlocks).
                if (!tok.IsClose && rowBlocks is not null && tok.Attributes is not null
                    && tok.Attributes.TryGetValue("i", out var riStr)
                    && int.TryParse(riStr, out var ri) && ri >= 0 && ri < rowBlocks.Count)
                {
                    Flush(pb, controlBoxes, uaBlockRhythm, articleRhythm, spanPtTypography, false, pb.styleStack.Peek());
                    pb.blocks.Add(rowBlocks[ri]);
                }
                continue;
            }
            if (!tok.IsClose && IsHiddenElement(tag, tok.Attributes, css))
            {
                if (!tok.IsSelfClosing && !VoidTags.Contains(tag))
                {
                    pb.hiddenTag = tag;
                    pb.hiddenDepth = 1;
                }
                continue;
            }

            // fieldsetBoxes mode: a <fieldset> opens a bordered box — marker
            // blocks bracket its content (the frame draws at the close) and its
            // padding indents everything inside; a <legend> is its own bold
            // 1.2em block riding the frame's top edge.
            if (fieldsetBoxes && tag.Equals("fieldset", StringComparison.OrdinalIgnoreCase))
            {
                Flush(pb, controlBoxes, uaBlockRhythm, articleRhythm, spanPtTypography, false, pb.styleStack.Peek());
                pb.blocks.Add(new Block { Text = "", IsHardBreak = true, FsBox = tok.IsClose ? -1 : 1 });
                // clamped at zero: the close tag often parses in a LATER segment
                // (its table split the parse) whose fresh root never saw the open
                pb.styleStack.Peek().LeftIndent = tok.IsClose
                    ? Math.Max(0, pb.styleStack.Peek().LeftIndent - FsPadLeftPt)
                    : pb.styleStack.Peek().LeftIndent + FsPadLeftPt;
                continue;
            }
            if (fieldsetBoxes && tag.Equals("legend", StringComparison.OrdinalIgnoreCase))
            {
                Flush(pb, controlBoxes, uaBlockRhythm, articleRhythm, spanPtTypography, false, pb.styleStack.Peek());
                var lgdTop = pb.styleStack.Peek();
                if (!tok.IsClose)
                {
                    pb.fsLegendSave = (lgdTop.FontSize, lgdTop.FontRes);
                    var lgdFactor = 1.2;
                    if (css is not null && css.TryGetValue("legend", out var lgdRule)
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
                        pb.blocks[^1].FsLegend = true;
                    (lgdTop.FontSize, lgdTop.FontRes) = pb.fsLegendSave;
                    pb.fsInLegend = false;
                }
                continue;
            }

            if (tok.IsClose) { HandleBlockClose(pb, tok, tag, css, articleRhythm, bodyBoxRhythm, browserUa, controlBoxes, divBandBg, inlineEmphasisRuns, metricLayout, msoParagraphs, spanPtTypography, uaBlockRhythm, uaPMargins); continue; }

            // Opening tag (or self-closing).
            // Anchor targets: an `id` on any element, or a `name` on <a>, marks a
            // destination that a #fragment hyperlink can jump to. Record it against
            // the block currently being built.
            if (tok.Attributes is not null)
            {
                if (tok.Attributes.TryGetValue("id", out var idName) && !string.IsNullOrEmpty(idName))
                    pb.pendingAnchorNames.Add(idName);
                if (tag.Equals("a", StringComparison.OrdinalIgnoreCase)
                    && tok.Attributes.TryGetValue("name", out var aName) && !string.IsNullOrEmpty(aName))
                    pb.pendingAnchorNames.Add(aName);
            }
            if (tag.Equals("center", StringComparison.OrdinalIgnoreCase))
            {
                Flush(pb, controlBoxes, uaBlockRhythm, articleRhythm, spanPtTypography, false, pb.styleStack.Peek());
                if (!tok.IsSelfClosing) pb.centerDepth++;
                continue;
            }
            if (tag.Equals("br", StringComparison.OrdinalIgnoreCase)) { HandleBlockBr(pb, tok, tag, articleRhythm, brBlankLines, browserUa, controlBoxes, inlineBlockCols, metricLayout, spanPtTypography, uaBlockRhythm); continue; }
            if (tag.Equals("hr", StringComparison.OrdinalIgnoreCase)) { HandleBlockHr(pb, tok, tag, articleRhythm, controlBoxes, formDialect, spanPtTypography, uaBlockRhythm); continue; }

            // <img>: emit an in-flow image block (drawn at layout time). A display:none image
            // is not part of the rendering — skip it entirely (no draw, no reserved space).
            if (tag.Equals("img", StringComparison.OrdinalIgnoreCase)) { HandleBlockImg(pb, tok, tag, css, articleRhythm, containerBoxIndents, controlBoxes, formDialect, spanPtTypography, uaBlockRhythm); continue; }

            // <button>: its inner text is the caption of a push-button box, not flow
            // content (control-box dialect only; other dialects keep it as text).
            if (controlBoxes && tag.Equals("button", StringComparison.OrdinalIgnoreCase))
            {
                Flush(pb, controlBoxes, uaBlockRhythm, articleRhythm, spanPtTypography, false, pb.styleStack.Peek());
                pb.inlineRunId = 0; pb.runPrevWasControl = false;
                pb.inButton = true; pb.buttonText.Clear();
                continue;
            }

            // <input> / <textarea>: emit an interactive AcroForm field.
            // Text-like inputs become a TextBoxField; a checkbox becomes a CheckboxField
            // (its `checked` attribute → Checked); a radio becomes a RadioButtonOptionField
            // grouped by name. hidden/submit/button/image are skipped.
            if (tag.Equals("textarea", StringComparison.OrdinalIgnoreCase))
            {
                // <textarea> → a multi-line AcroForm text field. Its inner text is the
                // default value (suppressed via inTextarea), not flow content.
                if (controlBoxes && pb.inlineRunId == 0) pb.inlineRunId = pb.nextInlineRunId++;
                var taTrailWs = pb.currentText.Length > 0 && char.IsWhiteSpace(pb.currentText[^1]);
                var taBefore = pb.blocks.Count;
                Flush(pb, controlBoxes, uaBlockRhythm, articleRhythm, spanPtTypography, false, pb.styleStack.Peek());
                if (controlBoxes && taTrailWs && pb.blocks.Count > taBefore)
                    pb.blocks[^1].Text += " ";
                pb.blocks.Add(BuildInputBlock(tok.Attributes, pb.styleStack.Peek(),
                    controlBoxes, multiline: true));
                if (controlBoxes)
                {
                    pb.blocks[^1].InlineRunId = pb.inlineRunId;
                    pb.runPrevWasControl = true;
                }
                pb.textareaBlock = pb.blocks[^1];
                pb.textareaText.Clear();
                pb.inTextarea = true;
                continue;
            }
            // <select>: the control occupies its box; only its chosen entry is text.
            if (tag.Equals("select", StringComparison.OrdinalIgnoreCase))
            {
                // The label text on this line joins the control's inline run, keeping
                // the collapsed space the markup left before the control.
                if (controlBoxes && pb.inlineRunId == 0) pb.inlineRunId = pb.nextInlineRunId++;
                var trailWs = pb.currentText.Length > 0 && char.IsWhiteSpace(pb.currentText[^1]);
                var nBefore = pb.blocks.Count;
                Flush(pb, controlBoxes, uaBlockRhythm, articleRhythm, spanPtTypography, false, pb.styleStack.Peek());
                if (controlBoxes && trailWs && pb.blocks.Count > nBefore)
                    pb.blocks[^1].Text += " ";
                pb.inSelect = true; pb.inSelectedOption = false; pb.selectedText.Clear();
                pb.selectOptions.Clear(); pb.curOptionText.Clear();
                string? selNm = null, selId = null;
                tok.Attributes?.TryGetValue("name", out selNm);
                tok.Attributes?.TryGetValue("id", out selId);
                pb.selectName = !string.IsNullOrEmpty(selNm) ? selNm : selId;
                continue;
            }
            if (pb.inSelect && tag.Equals("option", StringComparison.OrdinalIgnoreCase))
            {
                if (pb.curOptionText.Length > 0)
                {
                    pb.selectOptions.Add(CollapseWs(pb.curOptionText.ToString()));
                    pb.curOptionText.Clear();
                }
                pb.inSelectedOption = tok.Attributes is not null
                    && tok.Attributes.ContainsKey("selected") && pb.selectedText.Length == 0;
                continue;
            }
            if (tag.Equals("input", StringComparison.OrdinalIgnoreCase)) { HandleBlockInput(pb, tok, tag, articleRhythm, controlBoxes, dwFlow, spanPtTypography, uaBlockRhythm); continue; }

            if (BlockTags.Contains(tag)) { HandleBlockOpen(pb, tok, tag, css, beforeMarkers, floatFlow, absSpanLedger, articleRhythm, bandDialect, browserUa, containerBoxIndents, controlBoxes, coverStyles, divBandBg, html5UaHeadings, metricLayout, msoParagraphs, spanPtTypography, uaBlockRhythm, uaDefaults, uaPMargins); continue; }

            // Inline tags: mutate the top-of-stack style for <b>/<i>/<strong>/<em>.
            // <span style="font-size:..."> also adjusts size for the inner run.
            // Metric flow: MSHTML-saved documents write UPPERCASE tags, and bold drives
            // the metric wrap width — match case-insensitively there. Legacy keeps the
            // historical ordinal match so no existing conversion changes face.
            var tagCmp = metricLayout ? tag.ToLowerInvariant() : tag;
            // A nested <html>/<body> open (a forwarded email pasted whole inside a
            // paragraph) implicitly closes any open <p> — the browser recovery —
            // so a later stray </p> parses as the empty paragraph it is.
            if (browserUa && tagCmp is "html" or "body") pb.pOpenDepth = 0;
            if (tagCmp is "b" or "strong")
            {
                // Browser-UA flow: bold is an inline RUN (tracked start..end over the
                // raw text), not a whole-block face promotion. The in-page fragment
                // flow records the run AND keeps the promotion — the writer prefers
                // the runs and falls back to the promoted face when they cover the
                // whole block anyway.
                if (pb.trackBoldRuns && pb.inlineBoldDepth++ == 0) pb.inlineBoldStart = pb.currentText.Length;
                if (!browserUa) MarkInline(pb.styleStack, "F2");
            }
            else if ((inlineEmphasisRuns || browserUa)
                && tag.Equals("u", StringComparison.OrdinalIgnoreCase))
            {
                if (pb.inlineUnderDepth++ == 0) pb.inlineUnderStart = pb.currentText.Length;
            }
            else if (tagCmp is "i" or "em")
            {
                if (controlBoxes) pb.italicOpenTextLen = pb.currentText.Length;
                // Browser-UA flow: italic is an inline RUN like bold — no
                // whole-block promotion (which would stick to the enclosing
                // element's style and bleed past the close tag).
                if (pb.trackBoldRuns && pb.inlineItalicDepth++ == 0)
                    pb.inlineItalicStart = pb.currentText.Length;
                if (!browserUa) MarkInline(pb.styleStack, "F3");
            }
            else if (tagCmp == "small")
                MarkInlineSize(pb.styleStack, factor: 0.85);
            else if (tagCmp is "span" or "font")
            {
                // A span a class rule sets `display:block` is a block box: it breaks
                // the line before its content and again at its close (metric flow
                // only — the `.year { display:block }` date-stamp idiom). Vendor-
                // mangled transform debris in the same rule stays inert.
                if (tagCmp == "span" && !tok.IsSelfClosing)
                {
                    pb.spanDepth++;
                    if (metricLayout && css is not null && tok.Attributes is not null
                        && tok.Attributes.TryGetValue("class", out var spCls) && spCls is not null)
                        foreach (var sc in spCls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                            if (css.TryGetValue("." + sc, out var spr)
                                && spr.TryGetValue("display", out var spd)
                                && spd.Trim().Equals("block", StringComparison.OrdinalIgnoreCase))
                            {
                                Flush(pb, controlBoxes, uaBlockRhythm, articleRhythm, spanPtTypography, false, pb.styleStack.Peek());
                                pb.blockSpanDepths.Push(pb.spanDepth);
                                break;
                            }
                    // Inline-block title column (quirks CSS-run docs): the span's
                    // class rule declares display:inline-block with a width — its
                    // text becomes its own run, closed off from what preceded it.
                    if (inlineBlockCols && pb.titleColSpanDepth < 0 && css is not null
                        && tok.Attributes is not null
                        && tok.Attributes.TryGetValue("class", out var ibCls) && ibCls is not null)
                        foreach (var sc in ibCls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                            if (css.TryGetValue("." + sc, out var ibr)
                                && ibr.TryGetValue("display", out var ibd)
                                && ibd.Trim().Equals("inline-block", StringComparison.OrdinalIgnoreCase)
                                && ibr.TryGetValue("width", out var ibw)
                                && TryParseLength(ibw, out var ibwPt) && ibwPt > 0)
                            {
                                Flush(pb, controlBoxes, uaBlockRhythm, articleRhythm, spanPtTypography, false, pb.styleStack.Peek());
                                pb.openTitleColW = ibwPt;
                                pb.titleColSpanDepth = pb.spanDepth;
                                break;
                            }
                }
                // Ledger span classes (browser-UA flow): a margin-left class insets
                // the label run's line; a position:absolute+left class makes the
                // span its OWN column block, seated on the SAME line as the label
                // that precedes it.
                if (absSpanLedger && browserUa && tagCmp == "span" && !tok.IsSelfClosing
                    && css is not null && tok.Attributes is not null
                    && tok.Attributes.TryGetValue("class", out var lgCls) && lgCls is not null)
                    foreach (var sc in lgCls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (!css.TryGetValue("." + sc, out var scr)) continue;
                        if (scr.TryGetValue("position", out var scPos)
                            && scPos.Contains("absolute", StringComparison.OrdinalIgnoreCase)
                            && scr.TryGetValue("left", out var scLeft)
                            && TryParseLength(scLeft, out var scLeftPt))
                        {
                            Flush(pb, controlBoxes, uaBlockRhythm, articleRhythm, spanPtTypography, false, pb.styleStack.Peek());
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
                                 && TryParseLength(scMl, out var scMlPt))
                            pb.styleStack.Peek().TextInsetPt += scMlPt;
                    }
                // The pt-report flow: a span CLASS's typography (font-size,
                // weight) styles the rest of its block — the report's .title
                // span (rules resolve bare and tag-prefixed).
                if (spanClassTypography && tagCmp == "span" && !tok.IsSelfClosing
                    && css is not null && tok.Attributes is not null
                    && tok.Attributes.TryGetValue("class", out var tySpCls) && tySpCls is not null)
                    foreach (var sc in tySpCls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (!css.TryGetValue("." + sc, out var tyScr))
                            css.TryGetValue("span." + sc, out tyScr);
                        if (tyScr is null) continue;
                        var tyTop = pb.styleStack.Peek();
                        if (tyScr.TryGetValue("font-size", out var tyFs)
                            && TryParseCssFontSize(tyFs.Trim(), out var tyFsPt))
                            tyTop.FontSize = tyFsPt;
                        if (tyScr.TryGetValue("font-weight", out var tyFw)
                            && tyFw.Contains("bold", StringComparison.OrdinalIgnoreCase))
                        { tyTop.FontRes = "F2"; tyTop.EmBold = true; }
                    }
                // A span CLASS's stylesheet colour opens a colour run too (the
                // redline markers' `span.diff-html-added { color: red }`); its
                // own inline colour below wins when both are present.
                if (pb.trackBoldRuns && spanPtTypography && tagCmp == "span" && !tok.IsSelfClosing
                    && css is not null && tok.Attributes is { } clsColAttrs
                    && !(clsColAttrs.TryGetValue("style", out var clsInlSt) && clsInlSt is not null
                        && Regex.IsMatch(clsInlSt, @"(?<![-\w])color\s*:", RegexOptions.IgnoreCase))
                    && clsColAttrs.TryGetValue("class", out var clsColCls) && clsColCls is not null)
                    foreach (var sc in clsColCls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (!css.TryGetValue("." + sc, out var ccr))
                            css.TryGetValue("span." + sc, out ccr);
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
                if (pb.trackBoldRuns && spanPtTypography && tagCmp == "span" && !tok.IsSelfClosing
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
                    if (css is not null && decAttrs.TryGetValue("class", out var decCls) && decCls is not null)
                        foreach (var sc in decCls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                        {
                            if (!css.TryGetValue("." + sc, out var dcr))
                                css.TryGetValue("span." + sc, out dcr);
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
                if (pb.trackBoldRuns && tagCmp == "span" && !tok.IsSelfClosing
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
                if (pb.trackBoldRuns && tagCmp == "span" && !tok.IsSelfClosing
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
                // pt-styled fragment: the span's own pt typography styles the
                // block (16 pt bold title, 10 pt paragraphs, 8 pt italic note).
                if (spanPtTypography && tagCmp is "span" && !tok.IsSelfClosing
                    && tok.Attributes is { } ptyAttrs
                    && ptyAttrs.TryGetValue("style", out var ptySt) && ptySt is not null)
                {
                    var ptyTop = pb.styleStack.Peek();
                    if (Regex.Match(ptySt, @"font-size\s*:\s*([\d.]+)\s*pt",
                            RegexOptions.IgnoreCase) is { Success: true } ptyFs
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
                    }
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
                if (browserUa && tagCmp == "font" && tok.Attributes is { } uaFa)
                {
                    var uaFTop = pb.styleStack.Peek();
                    if (!tok.IsSelfClosing)
                        pb.uaFontSaves.Push((uaFTop.FontSize, uaFTop.ForeColor, uaFTop.FontFamily));
                    if (uaFa.TryGetValue("size", out var uaFsAttr)
                        && TryParseHtmlFontSize(uaFsAttr, out var uaFsPt))
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
                // UA-serif flow: an inline span's typography styles its element's
                // block — pt/px font-size, a px line-height LINE BOX, and the
                // span's own margin-left insetting its text (the legacy corpus
                // wraps whole lines in one styled span).
                if (browserUa && tok.Attributes is not null
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
                    if (fsM.Success && TryParseCssFontSize(fsM.Groups[1].Value.Trim(), out var uaSpFs))
                        uaTop.FontSize = uaSpFs;
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
                    if (tagCmp == "span" && !tok.IsSelfClosing
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
            else if (tag.Equals("a", StringComparison.OrdinalIgnoreCase))
            {
                // <a href> opens an inline hyperlink span; record the start so the
                // text up to the matching </a> becomes a Link annotation.
                string? href = null;
                tok.Attributes?.TryGetValue("href", out href);
                if (!string.IsNullOrEmpty(href))
                    pb.openAnchors.Push((pb.currentText.Length, href));
            }
        }
        // Final flush
        Flush(pb, controlBoxes, uaBlockRhythm, articleRhythm, spanPtTypography, false,pb.styleStack.Peek());
        // Drop trailing hard-break spacers so the doc doesn't grow a blank
        // tail page for HTML that ends with close-tags.
        // Drop trailing spacer-only hardbreaks so HTML that ends with close-tags
        // doesn't grow a blank tail page. Hardbreaks with an explicit CSS
        // height are intentional layout spacers — keep those.
        while (pb.blocks.Count > 0 && pb.blocks[^1].IsHardBreak && pb.blocks[^1].ExplicitHeight <= 0)
            pb.blocks.RemoveAt(pb.blocks.Count - 1);
        // A page-break still pending at the segment boundary (the following content is a
        // <table> segment parsed separately): emit a break-carrier block so the table
        // starts on the fresh page.
        if (pb.pendingPageBreak)
            pb.blocks.Add(new Block { Text = "", IsHardBreak = true, PageBreakBefore = true });
        // Control-box dialect: consecutive blocks sharing an inline-run id — the label
        // text and controls of one markup line — merge into a single container the
        // layout lays out with a pen (shared wrapping line boxes). A run that ended up
        // with a single member keeps its ordinary standalone layout.
        if (controlBoxes)
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
        return pb.blocks;
    }
}
