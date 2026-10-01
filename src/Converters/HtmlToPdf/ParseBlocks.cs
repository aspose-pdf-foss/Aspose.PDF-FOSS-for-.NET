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
    // The UA text input beside an inline-block label column (measured on the
    // label/input form's expected render): a 21px box whose own text baseline sits on
    // the line's baseline, so its bottom edge hangs UaInputBoxBelowBaselinePt under it;
    // one UA-serif space separates it from the column; its border is black.
    private const double UaInputBoxHeightPt = 15.75;
    private const double UaInputBoxBelowBaselinePt = 4.2;
    private const double UaColumnGapPt = 3.0;
    private const double UaInputBorderPt = 1.0;
    // the UA serif face the layout-free flow draws, and its ascent when the face is absent
    private const string UaSerifFaceName = "Times New Roman";
    private const double UaSerifAscentEm = 0.891;

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
    /// <summary>UA fieldset (probed): the frame is a 0.75 pt #808080 box 2px inside the body content box; its top
    /// line runs through the legend's line at half the line box (6.75 at 12 pt), interrupted from the content
    /// left to the legend text plus its 2px padding; the content stands 0.35em under the legend line and the
    /// frame closes 0.75em plus its stroke under the content; the legend keeps the inherited font.</summary>
    private const double UaFieldsetLegendPadPt = 1.5;
    /// <summary>…and its padding-inline, probed at 0.625 em: a 12 pt fieldset stands its
    /// content 8.25 pt inside its frame on each side (the stroke plus 7.5).</summary>
    private const double UaFieldsetSidePadEm = 0.625;
    private const double UaFieldsetLegendGapEm = 0.35;
    private const double UaFieldsetBottomPadEm = 0.75;
    private const double UaFieldsetStrokePt = 0.75;
    private const double UaFieldsetSideInsetPt = 1.5;

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

    /// <summary>In-page fragment flow: remember the enclosing style's face before a
    /// whole-block emphasis promotion, once per emphasis stretch.</summary>
    private static void SaveEmphasisBeforePromotion(ParseBlocksState pb)
    {
        // The sheet-typography flow, like the UA flow, draws emphasis as runs: no promotion, nothing to save.
        if (!pb.trackBoldRuns || pb.browserUa || pb.sheetElementTypography || pb.emphasisSave is not null) return;
        var top = pb.styleStack.Peek();
        pb.emphasisSave = (top.FontRes, top.EmBold, top.EmItalic);
    }

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

    /// <remarks>Dialect switches whose meaning is not obvious from the name:
    /// <c>html5UaHeadings</c> - html5-doctype bare UA document: heading margins are the real
    /// root-em values (see ApplyBlockTagStyle.html5UaHeadings).
    /// <c>spanPtTypography</c> - pt-styled fragment: an inline span's pt typography (font-size,
    /// weight, italic) styles its block - the legacy flow otherwise keeps its calibrated 11 pt
    /// default for span-styled paragraphs.
    /// <c>divBandBg</c> - pinned-body report dialect: a branded band is authored as a wrapper
    /// div carrying the background/colour with an inline-block child holding the text - the
    /// child keeps the wrapper's paint (CSS backgrounds do not inherit, so this is scoped to
    /// the dialect).
    /// <c>dwFlow</c> - DataWorks form flow: value-carrying submit inputs render as push buttons
    /// (honouring the legacy align attribute).
    /// <c>floatFlow</c> - certificate float flow: headings take their UA size and margins in em
    /// of the cascade rather than the legacy flows' flat points.</remarks>
    private static List<Block> ParseBlocks(string html,
        IReadOnlyDictionary<string, Dictionary<string, string>>? css,
        IReadOnlyList<BeforeMarker>? beforeMarkers = null, IReadOnlyList<Block>? rowBlocks = null,
        bool metricLayout = false, bool uaDefaults = false,
        bool browserUa = false, double bodyFontSize = 0, bool bandDialect = false, bool formDialect = false,
        bool brBlankLines = false, bool uaBlockRhythm = false, bool controlBoxes = false, bool uaGridBlocks = false,
        bool inlineEmphasisRuns = false, bool articleRhythm = false, bool bodyBoxRhythm = false,
        bool containerBoxIndents = false, bool coverStyles = false, bool inlineBlockCols = false,
        bool absSpanLedger = false, bool spanClassTypography = false, bool fieldsetBoxes = false,
        bool uaPMargins = false, bool msoParagraphs = false, bool html5UaHeadings = false,
        bool spanPtTypography = false, bool divBandBg = false, bool dwFlow = false, bool floatFlow = false,
        bool sheetElementTypography = false, List<CssChainRule>? chainRules = null, bool wordMail = false, bool wordExport = false,
        bool uaClosingGaps = false, bool uaFieldset = false, double bodyLineBoxPt = 0, double contentWidthPt = 0, bool uaBoxes = false,
        ConvertState? cv = null)
    {
        // Strip script/style/head bodies whole; inline tags inside them are
        // not semantic content.
        html = NormaliseHtmlSource(html);
        var pb = new ParseBlocksState();
        pb.css = css;
        pb.beforeMarkers = beforeMarkers;
        pb.rowBlocks = rowBlocks;
        pb.metricLayout = metricLayout;
        pb.uaDefaults = uaDefaults;
        pb.browserUa = browserUa;
        pb.uaBoxes = uaBoxes;
        pb.sheetElementTypography = sheetElementTypography;
        pb.wordMail = wordMail;
        pb.wordExport = wordExport;
        pb.chainRules = chainRules;
        pb.bodyFontSize = bodyFontSize;
        pb.bodyLineBoxPt = bodyLineBoxPt;
        pb.contentWidthPt = contentWidthPt;
        pb.cv = cv;
        pb.bandDialect = bandDialect;
        pb.formDialect = formDialect;
        pb.brBlankLines = brBlankLines;
        pb.uaGridBlocks = uaGridBlocks;
        pb.uaBlockRhythm = uaBlockRhythm;
        pb.uaClosingGaps = uaClosingGaps;
        pb.uaFieldset = uaFieldset;
        pb.controlBoxes = controlBoxes;
        pb.inlineEmphasisRuns = inlineEmphasisRuns;
        pb.articleRhythm = articleRhythm;
        pb.bodyBoxRhythm = bodyBoxRhythm;
        pb.containerBoxIndents = containerBoxIndents;
        pb.coverStyles = coverStyles;
        pb.inlineBlockCols = inlineBlockCols;
        pb.absSpanLedger = absSpanLedger;
        pb.spanClassTypography = spanClassTypography;
        pb.fieldsetBoxes = fieldsetBoxes;
        pb.uaPMargins = uaPMargins;
        pb.msoParagraphs = msoParagraphs;
        pb.html5UaHeadings = html5UaHeadings;
        pb.spanPtTypography = spanPtTypography;
        pb.divBandBg = divBandBg;
        pb.dwFlow = dwFlow;
        pb.floatFlow = floatFlow;
        pb.tokens = Tokenize(html);

        pb.blocks = new List<Block>();
        pb.currentText = new StringBuilder();
        pb.styleStack = new Stack<BlockStyle>();
        pb.heightFloors = new Stack<(int Marker, double H, int Depth)>();
        SeedRootBlockStyle(pb, css, contentWidthPt);
        ResetParseState(pb);

        pb.closingElement = false;
        foreach (var tok in pb.tokens)
        {
            ParseBlockToken(pb, tok);
            if (pb.wordMail && Environment.GetEnvironmentVariable("ASPOSE_TRACE_TOK") == "1" && tok.Kind == TokenKind.Tag)
                Console.Error.WriteLine($"[tok] {(tok.IsClose ? "/" : "")}{tok.Tag} depth={pb.styleStack.Count} fs={pb.styleStack.Peek().FontSize} fam={pb.styleStack.Peek().FontFamily} spanDepth={pb.spanDepth} blocks={pb.blocks.Count} text={pb.currentText.Length}");
        }
        // Final flush
        FinishBlocks(pb);
        // Control-box dialect: consecutive blocks sharing an inline-run id — the label
        // text and controls of one markup line — merge into a single container the
        // layout lays out with a pen (shared wrapping line boxes). A run that ended up
        // with a single member keeps its ordinary standalone layout.
        if (MergeControlBoxBlocks(pb) is { } mergeControlBoxBlocksResult) return mergeControlBoxBlocksResult;
        return pb.blocks;
    }

    /// <summary>One token of the block parse: skipped and row tags, closes, breaks, rules, images, inputs and block opens dispatch to their handlers; text and inline tags join the current run.</summary>
    private static void ParseBlockToken(ParseBlocksState pb, Token tok)
    {
        if (!HandleHiddenTextAndRowTokens(pb, tok)) return;

        // fieldsetBoxes mode: a <fieldset> opens a bordered box — marker
        // blocks bracket its content (the frame draws at the close) and its
        // padding indents everything inside; a <legend> is its own bold
        // 1.2em block riding the frame's top edge.
        if (!HandleFieldsetAndCenterTags(pb, tok)) return;
        if (pb.tag.Equals("br", StringComparison.OrdinalIgnoreCase)) { HandleBlockBr(pb, tok, pb.tag); return; }
        if (pb.tag.Equals("hr", StringComparison.OrdinalIgnoreCase)) { HandleBlockHr(pb, tok, pb.tag); return; }

        // <img>: emit an in-flow image block (drawn at layout time). A display:none image
        // is not part of the rendering — skip it entirely (no draw, no reserved space).
        if (pb.tag.Equals("img", StringComparison.OrdinalIgnoreCase)) { HandleBlockImg(pb, tok, pb.tag); return; }

        // <button>: its inner text is the caption of a push-button box, not flow
        // content (control-box dialect only; other dialects keep it as text).
        if (!HandleFormControlTags(pb, tok)) return;
        if (pb.tag.Equals("input", StringComparison.OrdinalIgnoreCase)) { HandleBlockInput(pb, tok, pb.tag); return; }

        if (BlockTags.Contains(pb.tag)) { HandleBlockOpen(pb, tok, pb.tag); return; }

        pb.tagCmp = pb.metricLayout ? pb.tag.ToLowerInvariant() : pb.tag;
        // A nested <html>/<body> open (a forwarded email pasted whole inside a
        // paragraph) implicitly closes any open <p> — the browser recovery —
        // so a later stray </p> parses as the empty paragraph it is.
        if (pb.browserUa && pb.tagCmp is "html" or "body") pb.pOpenDepth = 0;
        if (pb.tagCmp is "b" or "strong")
        {
            // Browser-UA flow: bold is an inline RUN (tracked start..end over the
            // raw text), not a whole-block face promotion. The in-page fragment
            // flow records the run AND keeps the promotion — the writer prefers
            // the runs and falls back to the promoted face when they cover the
            // whole block anyway.
            if (pb.trackBoldRuns && pb.inlineBoldDepth++ == 0) pb.inlineBoldStart = pb.currentText.Length;
            SaveEmphasisBeforePromotion(pb);
            if (!pb.browserUa && !pb.sheetElementTypography && !pb.wordMail) MarkInline(pb.styleStack, "F2");
        }
        else if ((pb.inlineEmphasisRuns || pb.browserUa)
            && pb.tag.Equals("u", StringComparison.OrdinalIgnoreCase))
        {
            if (pb.inlineUnderDepth++ == 0) pb.inlineUnderStart = pb.currentText.Length;
        }
        // …and a strike tag opens a strike run the same way
        else if (pb.browserUa && pb.tagCmp is "strike" or "s" or "del")
        {
            if (pb.inlineStrikeDepth++ == 0) pb.inlineStrikeStart = pb.currentText.Length;
        }
        else if (pb.tagCmp is "i" or "em")
        {
            if (pb.controlBoxes) pb.italicOpenTextLen = pb.currentText.Length;
            // Browser-UA flow: italic is an inline RUN like bold — no
            // whole-block promotion (which would stick to the enclosing
            // element's style and bleed past the close tag).
            if (pb.trackBoldRuns && pb.inlineItalicDepth++ == 0)
                pb.inlineItalicStart = pb.currentText.Length;
            SaveEmphasisBeforePromotion(pb);
            if (!pb.browserUa && !pb.sheetElementTypography && !pb.wordMail) MarkInline(pb.styleStack, "F3");
        }
        else ParseInlineAndAnchorToken(pb, tok);
    }
}
