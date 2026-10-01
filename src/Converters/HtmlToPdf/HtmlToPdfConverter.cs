using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

/// <summary>
/// Converts HTML into a PDF document using a minimal block-layout model:
/// block elements (p/div/h1-h6/blockquote/li/tr) stack vertically with
/// per-block top and bottom margins, inline elements flow inside a block,
/// and text wraps to the content width. Not a CSS-complete renderer —
/// enough structure for pagination to match block-level document shape.
/// </summary>
internal static partial class HtmlToPdfConverter
{
    public static Document Convert(string htmlPath, HtmlLoadOptions? options = null)
    {
        // A file-loaded document resolves relative resource refs (<img src>, <link href>)
        // against its own directory, the way a browser resolves them against the page URL.
        if (options is not null && string.IsNullOrEmpty(options.BasePath))
            try
            {
                options.BasePath = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(htmlPath));
                options.BasePathAutoDerived = true;
            }
            catch { }
        var html = DecodeHtmlBytes(File.ReadAllBytes(htmlPath), options);
        return ConvertFromHtml(html, options);
    }

    public static Document Convert(byte[] htmlData, HtmlLoadOptions? options = null)
    {
        var html = DecodeHtmlBytes(htmlData, options);
        return ConvertFromHtml(html, options);
    }

    /// <summary>One rendered block: a run of text with uniform style and
    /// vertical spacing on either side. One Block becomes N wrapped lines
    /// at layout time.</summary>
    internal sealed class Block
    {
        public string Text = "";
        public double FontSize;
        // Point size of the block's LEADING inline span when it differs from the
        // block's final size (a 10 pt marker span opening an 8 pt note): the first
        // LINE BOX takes the leading span's height while the block text keeps its
        // own size. 0 = no distinct leading span.
        public double LeadFontSize;
        // The block's own right margin in points (a paragraph's pt margin
        // shorthand): the wrap box ends this far inside the content edge.
        public double RightInsetPt;
        public string FontRes = "F1";    // F1=Helvetica, F2=Helvetica-Bold, F3=Helvetica-Oblique
        // CSS font-family (first non-generic name, e.g. "Arial"); null = default Helvetica.
        // When set and resolvable through FontRepository, the run is drawn with the
        // embedded TrueType face instead of the Standard-14 FontRes.
        public string? FontFamily;
        // Foreground text color from CSS color: / legacy <font color>. Null = black.
        public Color? ForeColor;
        // Point size from a legacy <font size="N"> attribute (0 = none); inert for the legacy flow.
        public double LegacyFontPt;
        // Set when the size came from a legacy <font size="N"> attribute (dialect marker).
        public bool LegacyFontSized;
        // Inline emphasis seen anywhere in the block (independent of FontRes, which holds
        // only one variant): both true = bold-italic. Read only by the embedded-face
        // page-level path; the legacy flow keeps using FontRes.
        public bool EmBold;
        public bool EmItalic;
        public double MarginTop;
        public double MarginBottom;
        /// <summary>UA flow: the box's closing margin, which a following margin-less line (bare text, a break, a div) stands below; a margined block collapses its own top margin instead.</summary>
        public double UaClosingGapPt;
        /// <summary>The top margin belongs to an element nested inside another block (see BlockStyle.MarginTopNested).</summary>
        public bool MarginTopNested;
        // Apply MarginTop even at the top of a page (the filing dialect's repeated
        // page-header block keeps its CSS top margin below the page margin).
        public bool MarginTopAlways;
        public double LeftIndent;
        // A title column declared as a PERCENT of the content width: resolved onto
        // LeftIndent when the block lays out (the parser has no content width).
        public double LeftIndentFrac;
        public bool IsListItem;
        // CSS page-break-before:always — start this block on a fresh page.
        public bool PageBreakBefore;
        // Browser-UA flow: wrap-box fraction from an enclosing div's width:N%
        // (0 = full content width), and non-collapsing padding-top space.
        public double WidthFrac;
        // Absolute enclosing-div width in CSS px (form-document dialect wrap box).
        public double WidthPx;
        // Report label/span rows: right-align each wrapped line inside a box of
        // this width (the label column), cap the wrap box in points (the span
        // column), and let a label share its row with the span that follows —
        // the row's height is the span's, so the label block gives its y back.
        public double RightAlignBoxPt;
        public double MaxWidthPt;
        public bool NoAdvanceY;
        // The field-list dialect: a label's share of the fields box (its wrap box), or the value's
        // (its indent); the label keeps the row's LAST line for the value beside it
        public double FieldColumnFrac;
        public bool FieldLabel;
        // The element's padding-bottom, spent under its last block (box space, never collapsed)
        public double PadBottomPt;
        // This block's float flag came from an ancestor, not from its own declaration.
        public bool FloatInherited;
        public double PadTop;
        // Unwrapped single-column wrapper-table cell with td align=center: each
        // wrapped line centres over a band this wide starting at the block's own
        // left (the table's attribute width minus the cell chrome both sides).
        public double CenterBandW;
        // text-align:right (honored by the print-grid dialect only).
        public bool AlignRight;
        // Print-grid heading band: a class rule's border-bottom under this block
        // (".blueh4 h4 { border-bottom: 6px solid #C3D5EF }") drawn as a filled bar
        // across the wrap box, BandPadPx below the last line.
        public Color? BandColor;
        public double BandPx;
        public double BandPadPx;
        // Print-grid column scope: narrows the ambient content width to
        // FloatWidthFrac of it (minus ColPadPt each side) for the enclosed blocks —
        // STACKED (no y reset), unlike a float band column.
        public bool ColScopeStart;
        public bool ColScopeEnd;
        public double ColPadPt;
        // List-item marker (e.g. "1." for an <ol> item, "•" for a <ul> bullet).
        // Emitted as a separate text run to the left of the first content line, so it
        // surfaces as its own TextFragment. Null = not a list item / no marker.
        public string? Marker;
        // Emit the marker AFTER the first content line (not before) so it surfaces as the
        // LATER TextFragment on that line. Set for CSS ::before generated markers on RTL
        // lists, where the item text reads first and the marker sits to its right.
        public bool MarkerAfter;
        public bool IsHardBreak;         // hidden spacer (e.g. <br> inside block)
        // A break PARAGRAPH from a text-free stretch between tables (the
        // "</p> <p><br/></p> <p>" sandwich) - it stands a full UA paragraph,
        // margins included, where its table neighbours read none.
        public bool UaSpacerPara;
        // The text continues a paragraph whose first line was a `<br>` spacer: its margin-top is
        // the paragraph's, stood once - by a table's break tail where one stood it already.
        public bool AfterOwnLeadingBreak;
        public bool IsLineBreak;         // a real <br> tag (not a synthetic spacer)
        // Floor on the block's rendered height (from CSS height/min-height).
        // Zero = let the text content alone decide.
        public double ExplicitHeight;
        // ExplicitHeight came from a CONTAINER's class-rule height (the widget
        // header band, containerBoxIndents mode): the band spans from the line-box
        // top, so the baseline-anchored flow bills one line less below it.
        public bool BandBoxHeight;
        // Unitless CSS line-height factor from a class rule (coverStyles mode);
        // 0 = the flow's own default pitch.
        public double LineFactor;
        public bool DeclaredLineFactor;
        // A percent line-height's factor on the UA flow, inherited as a factor and resolved per block (0 = none)
        public double UaLineFactor;
        // A stylesheet's unitless line-height on the calibrated flow: the pitch alone reads
        // it, so the UA flow's declared-factor seating is not engaged by it.
        public double SheetLineFactor;
        // <hr>: draw a horizontal rule line in RuleColor / RuleWidth instead
        // of just consuming vertical space.
        public bool IsHorizontalRule;
        public Color? RuleColor;
        public double RuleWidth;
        // CSS box decoration drawn behind/around the block's content area:
        // background-color fill and a border stroke. Null = none (draw nothing).
        public Color? BackgroundColor;
        // Pinned-body report band: the wrapper + inline-block child's div
        // paddings above and below the band's line — the fill extends by it and
        // the flow reserves it around the line (the branded black bands).
        public double BandPadPt;
        // CSS `padding` on a block that paints a BackgroundColor: the fill covers
        // the block's line boxes plus BgPadTopPt above and BgPadBottomPt below,
        // and its text starts BgPadLeftPt inside the content edge. Read only where
        // a background is actually painted, so an unpainted block is unaffected.
        public double BgPadTopPt;
        public double BgPadBottomPt;
        public double BgPadLeftPt;
        public Color? BorderColor;
        public double BorderWidth;
        // The element declared ONLY border-top (border:none;border-top:solid …):
        // one rule above the content, no box.
        public bool BorderTopOnly;
        // A sheet box's border-bottom rule and width (see BlockStyle.BorderBottomWidth).
        public double BorderBottomWidth;
        public Color? BorderBottomColor;
        // The sheet-typography flow paints this block's line box as a band (BlockStyle.SheetBox).
        public bool SheetBox;
        // A UA-grid heading box: its rules' widths ride the margins after the sheet/UA margins resolve,
        // and its padding-top is space between the top rule and the first line.
        public double UaBoxTopPt;
        public double UaBoxBottomPt;
        public double UaPadTopPt;
        // …and its own top rule (kept apart from the calibrated BorderTopOnly, which a bottom rule overwrites).
        public double UaRuleTopPt;
        public Color? UaRuleTopColor;
        // UA-serif flow: a px line-height LINE BOX and the inline span's own
        // margin-left text inset (see BlockStyle.LineBoxPt / TextInsetPt).
        public double LineBoxPt;
        public double TextInsetPt;
        // margin-top was authored, so it MAX-collapses at the document top.
        public bool MarginTopAuthored;
        // Painted-box dimensions (a tiny repeated background tile over an
        // explicitly sized element): BackgroundColor fills this declared box
        // once, anchored at the element's box origin, instead of banding each
        // text line. Zero = no painted box.
        public double BgBoxWidthPt;
        public double BgBoxHeightPt;
        // The box height as a fraction of the viewport (`height: 100vh`): the page's
        // content height, resolved where the box is painted. Zero = a pt height.
        public double BgBoxHeightVh;
        // A background image drawn once over the declared box, at its top-left, clipped to
        // it and sized by `background-size` (see PaintBackgroundImageBox); the bytes are
        // resolved when the block renders, the natural size read from them.
        public string? BgImageSrc;
        public string BgImageSize = "";
        public byte[]? BgImageBytes;
        public double BgImageNatWPt, BgImageNatHPt;
        // The indent of the element that declared the box (a zero width = the content width).
        public double BgBoxIndentPt;
        // Border-only declared box (UA flow): a border over an inline width ×
        // height with no background strokes its declared box — rounded by
        // BorderRadiusPt — while the content flows inside it. ExplicitHeight
        // carries the content height; zero width = no such box.
        public double BorderBoxWPt;
        public double BorderRadiusPt;
        // text-align:center from a class/type rule — honored by the metric flow only.
        public bool AlignCenter;
        // A CSS text-align:center from anywhere — honored by the sectioned-report flow.
        public bool AlignCenterCss;
        // float:left on this element or one enclosing it — an image inside such a box
        // is taken out of the flow and the text beside it wraps in the space left over.
        public bool FloatLeft;
        // float:right — the UA flow lays the block as a shrink-to-fit box against
        // the right content edge, sharing its line with adjacent floats.
        public bool FloatRight;

        /// <summary>See StyledDoc.ShorthandLeftPt - the float flow's block indent.</summary>
        public double ShorthandLeftPt;

        /// <summary>The `margin:` shorthand's TOP value, recorded for every document and
        /// read only by the float flow.</summary>
        public double ShorthandTopPt;

        /// <summary>See StyledDoc.MarginRightPt - the float flow's block right inset.</summary>
        public double MarginRightPt;

        /// <summary>See StyledDoc.FontFamilyStack - the declared family list.</summary>
        public string? FontFamilyStack;

        /// <summary>A declared `width: Npx`, in points. Recorded for every document and
        /// read only by the float flow, where a block keeps the box it declares even when
        /// that box overflows the content frame.</summary>
        public double DeclaredWidthPt;
        // A page-break-after:always DIV closes with this table (its close tag
        // parses in a later segment) — the flow opens a fresh page after it.
        public bool PageBreakAfterTable;
        // Fieldset box marker: +1 opens a frame, −1 closes (draws) it.
        public int FsBox;
        /// <summary>…a framed wrapper DIV's marker instead when its border is set: the border width
        /// and colour, the content width it declares, and whether its align centres the tables.</summary>
        public double FrameW;
        public Color? FrameCol;
        public double FrameBoxW;
        public bool FrameCentred;
        // Container BACKGROUND box marker: +1 opens, −1 closes (fills) it. Emitted only for
        // a container that paints a background and flushes no line of its own, whose colour
        // would otherwise be carried by nothing; the open marker holds the fill and its width.
        public int BgSpan;
        // A percentage width the container declared, as a fraction; resolved at render.
        public double BgSpanWidthFrac;
        // This block is a <legend> — it rides the fieldset frame's top edge.
        public bool FsLegend;
        // ALIGN="justify" / text-align:justify — stretch word gaps to the content box.
        public bool AlignJustify;
        // Legacy ALIGN="center" attribute — centre measured lines in the content box.
        public bool AlignCenterAttr;
        // Inline <a href> ranges within Text (char offsets into the collapsed Text),
        // each with its target URL. Drives Link-annotation generation at layout time.
        public System.Collections.Generic.List<(int Start, int Length, string Url)>? Anchors;

        // Inline <b>/<strong> ranges within Text (collapsed coordinates) — the
        // browser-UA flow draws these as bold RUNS inside an otherwise regular line
        // (the legacy flow instead promotes the whole block's FontRes).
        public System.Collections.Generic.List<(int Start, int Length)>? BoldRuns;
        // Inline span colour runs (browser-UA flow): a span's own color scopes
        // to the span — collapsed-coordinate ranges with their ink.
        public System.Collections.Generic.List<(int Start, int Length, Color C)>? ColorRuns;
        // Inline span SIZE runs (sheet-typography flow): a span class's font-size scopes
        // to the span - collapsed-coordinate ranges with their size in points. A line
        // holding one takes the CSS line box of its largest run.
        public System.Collections.Generic.List<(int Start, int Length, double Pt)>? SizeRuns;
        // Span-scoped face runs (the Word export's Symbol / Wingdings list label and its Times New
        // Roman filler on an Arial line): the run draws in its own family, the rest in the block's.
        public System.Collections.Generic.List<(int Start, int Length, string Fam)>? FamilyRuns;
        // The block's runs each line at their own face-normal line box (the Word export's 16 pt
        // label beside a 7 pt filler pitches 18.75, the 7 pt box not stretched to the block's).
        public bool RunNormalLines;
        // Decoration runs (redline diff dialect): span-scoped strike/underline ink.
        // Kind: 1 = text-decoration underline, 2 = line-through, 3 = border-bottom
        // solid (the added-marker), 4 = border-bottom dashed/dotted (the
        // changed-marker). C = the border's own colour (kinds 3/4); text-decoration
        // kinds take the text colour at the run.
        public System.Collections.Generic.List<(int Start, int Length, int Kind, Color? C)>? DecorRuns;
        // font-variant: small-caps (redline dialect) — lowercase draws uppercase
        // at RedlineSmallCapsEm of the block size.
        public bool SmallCaps;
        // Positive text-indent (pt) on the paragraph: its FIRST line starts this
        // far right of the content edge (redline dialect).
        public double TextIndentPt;
        // CSS letter-spacing in points (redline dialect: -0.1 on every span).
        public double LetterSpacingPt;

        // Inline <u> ranges within Text (collapsed coordinates) — drawn as an
        // underlined RUN inside an otherwise undecorated line. Populated only for
        // callers that asked for inline emphasis runs.
        public System.Collections.Generic.List<(int Start, int Length)>? UnderlineRuns;
        // <strike>/<s>/<del> runs of the UA flow: a line through the run's text
        public System.Collections.Generic.List<(int Start, int Length)>? StrikeRuns;

        // Inline <i>/<em> ranges within Text (collapsed coordinates) — the
        // browser-UA flow draws these as italic RUNS inside an otherwise regular
        // line (the legacy flow instead promotes the whole block's FontRes).
        public System.Collections.Generic.List<(int Start, int Length)>? ItalicRuns;

        // Anchor-target names declared in this block (an element's `id`, or an
        // `<a name="…">`). A #fragment hyperlink resolves to the page this block
        // renders on, so internal document links land on the right page.
        public System.Collections.Generic.List<string>? AnchorNames;

        // Interactive form input: an <input>/<textarea> becomes an AcroForm
        // TextBoxField at layout time instead of a text run.
        public bool IsInputField;
        public string InputValue = "";
        public string? InputName;     // AcroForm field name from the <input> name/id attribute
        public double InputWidth;     // CSS px (0 = fill content width)
        public double InputWidthFrac; // a class width in percent of the content width, resolved at layout
        public bool InputInColumn;    // seated beside a title column: the UA input box on the label's line
        public double InputHeight;    // CSS px (0 = one text line)
        public bool InputMultiline;
        public bool InputReadOnly;    // HTML disabled / readonly attribute
        public bool InputDrawValue;   // typeset the value inside the drawn box
        public bool InputValueMono;   // …in the typewriter face (textarea)
        public double InputAdvance;   // flow cost, less than the drawn height
        // <select>: a combo box sized by its widest option, the chosen option's text
        // typeset inside. Slightly different box geometry from a text input's.
        public bool IsSelectBox;
        // <button>: a push-button box — caption width + chrome, the page stylesheet's
        // button{} tag rule supplying fill/text colours (escaped-attr dialect).
        public bool IsButton;
        public string ButtonCaption = "";
        // A broken <img> following text on ITS line (escaped-attr dialect): the 32×32
        // placeholder rides INLINE at the line's end — bottom a point above the
        // baseline, rising over the space above — and consumes no flow height.
        public bool InlineIconAfter;
        // Inline run (escaped-attr dialect): consecutive inline content — label text
        // and controls — between block boundaries shares wrapping line boxes with a
        // layout-time pen, so label|input|label|select land on one line. Blocks
        // carrying the same id are merged into one InlineItems container.
        public int InlineRunId;
        public System.Collections.Generic.List<Block>? InlineItems;

        // <input type="checkbox">: emit an AcroForm CheckboxField at layout time.
        // Checked carries the HTML `checked` attribute.
        public bool IsCheckbox;
        public bool Checked;

        // <input type="radio">: collected into a RadioButtonField group (by RadioGroup =
        // the input name) emitted after layout, so each option surfaces as a
        // RadioButtonOptionField on Form.Fields.
        public bool IsRadio;
        public string RadioGroup = "";

        // <img>: draw the referenced image in-flow at layout time. Src is resolved via the
        // load options' custom resource loader (for remote/opaque URIs), a data: URI, or a
        // local file. Width/Height are CSS px (0 = derive from the other / natural size).
        public bool IsImage;
        // the image is INLINE (no display:block, not floated): a <br> right after it ends the
        // image's own line and spends no line box
        public bool ImageInline;
        public string ImageSrc = "";
        public double ImageWidth;
        // An inline `max-width: N%`: the drawn box caps at this share of the
        // content width, and the image never widens the sheet. Zero = none.
        public double ImageMaxWFrac;
        public double ImageHeight;
        // position:absolute image: it leaves the flow entirely (the cursor never advances
        // for it) and seats at these offsets, in points, from the page's content origin -
        // see AbsoluteImageSeat.cs for how the containing block puts them there.
        public bool ImageAbsPos;
        public double ImageAbsLeftPt;
        public double ImageAbsTopPt;
        // The fixed-size container this full-bleed page image fills, in points (0 = the image
        // fills no such band). The raster page export gives every source page one container of
        // a declared em height holding one PNG, and the flow advances by that BAND, not by the
        // image - see AbsoluteImageSeat.cs.
        public double PageBandHeightPt;
        // <img alt="…"> — alternate description, surfaced as a Figure structure
        // element's /Alt when CreateLogicalStructure builds the tag tree.
        public string? ImageAlt;
        // In-flow image alignment/spacing: centered in the content box (inside
        // <center> / text-align:center), with CSS vertical padding (px).
        public bool ImageCentered;
        public double ImagePadTopPx;
        public double ImagePadBottomPx;
        // Horizontal indent (pt) from leading inline whitespace on the image's line
        // (e.g. "&nbsp;&nbsp; <img>"): the whitespace shares the image's line box, so it
        // shifts the image right without reserving a text line of its own above it.
        public double ImageIndentPt;

        /// <summary>A floated image's margin on the side FACING the flow, in points - the
        /// gutter wrapped text keeps off it, so its wrap edge is the float's MARGIN box.
        /// Null when the float declares no side margins at all; those keep the flat
        /// <see cref="FloatGutterPt"/> stand-in.</summary>
        public double? ImageFloatGutterPt;
        // CSS transform: rotate(Ndeg) on the image (inline style or class rule),
        // in CSS degrees (clockwise-positive). The image draws rotated about the
        // centre of its layout box; the layout box itself — and so the flow
        // advance — stays the unrotated one, per CSS transform semantics.
        public double ImageRotateDeg;
        // Width-billing container chrome around the image (containerBoxIndents
        // mode): the padding+border sum of its width:auto ancestors, which the
        // chart-card page-widen adds to the image's natural width.
        public double ImageWidenPadPt;
        // The box-shadow'd CARD enclosing the image (chart-card documents): the
        // shadow colour, and the card's own left chrome (padding + border) so
        // the draw recovers the card box from the image position and frames it.
        public Color? ImageCardShadow;
        public double ImageCardChromePt;
        /// <summary>The card frame around a picture: its border colour and width, and how far
        /// outside the picture the card's outer edge lies (see BlockStyle.CardFrame*).</summary>
        public Color? ImageCardFrame;
        public double ImageCardFrameBorderPt;
        public double ImageCardFrameInsetPt;

        // A real <table> (no form inputs) rendered as a column grid at layout time via
        // BuildTableFromHtml + Table.BuildMultiPage. TableHtml carries the raw <table>…</table>.
        public bool IsTable;
        public string TableHtml = "";
        // The container elements the grid stands INSIDE (the divs open above it, with
        // their ids and classes): the chain a stylesheet addresses its cells through
        // (`#right_column TABLE TD`, `.datagrid table th`). Null = no hooked ancestor.
        public List<CssElem>? CssAncestors;
        // The typography a wrapper's id/class rule declares over the table (`#divGeral { font-family:
        // Verdana; font-size: 14px }`): the UA grid draws its cells in it (null / 0 = the flow's).
        public string? HostFace;
        public double HostFontPt;
        // The box the innermost wrapper's id/class rule declares over the table (0 = none).
        public double HostWidthPt;
        // A floated table (align="left" attribute): float
        // content paints FIRST in the page's content stream (before the normal flow), so its
        // text surfaces as the leading TextFragments. Layout position is unchanged.
        public bool FloatFirst;

        // Float-band structural markers (the SEC-filing two-column card pattern:
        // consecutive <div style="float:left; width:N%"> siblings). A band lays its
        // columns out side by side: each column start rewinds the flow cursor to the
        // band top and narrows the content box to the column; the band end drops the
        // cursor to the lowest column bottom and restores the full content box.
        public bool FloatBandStart;
        public bool FloatBandEnd;
        public bool FloatColStart;
        public double FloatStartFrac;   // column left edge as a fraction of the content width
        public double FloatWidthFrac;   // column width as a fraction of the content width
        public double FloatPadTopPt;    // padding-top of the column div

        // Height-floor structural markers: a container declaring height/min-height
        // is a FLOOR its own content grows DOWN into - probed: the element's own
        // content stays where it was and only what FOLLOWS it moves. The start
        // marker remembers the flow cursor; the end marker drops it to
        // (start - ExplicitHeight) when the content has not reached that far.
        public bool HeightFloorStart;
        public bool HeightFloorEnd;

        // Border-box structural markers: a <div style="border:solid …"> draws a
        // rectangle around the flow content between start and end (same page only).
        public bool BoxStart;
        public bool BoxEnd;
        public double BoxBorderPt;
        public double BoxPadTopPt;
        public double BoxPadBottomPt;
        // The flow cursor is a BASELINE position: the box's top border must sit
        // above the first inner line's ink (≈0.9 em above its baseline), not at
        // the cursor itself.
        public double BoxAscentPt;
        // Print-grid box extras: horizontal padding insets the inner flow, a
        // margin-bottom follows the closed box, and the border strokes in a
        // grey level (0 = black) so a gainsboro frame stays light.
        public double BoxPadSidePt;
        public double BoxMarginBottomPt;
        public double BoxBorderGray;
        // A box that DECLARES its own size: the border box is drawn at exactly this
        // content width and height (borders outside it), the inner flow wraps to the
        // width, and the flow leaves the box at its declared bottom whatever the
        // content measured. Zero = the box takes the ambient width and its content's
        // height, as an undeclared one does.
        public double BoxWidthPt;
        public double BoxHeightPt;

        // A centered search-form (text input + submit buttons + optional side link),
        // extracted from a <form><table> whose flat block layout cannot express the
        // centered fixed-width cell. All geometry in CSS px (×0.75 at draw).
        public SearchForm? Form;

        // A fixed-width RTL diagram table (figure + labels + stretched legend row),
        // right-pinned as one canvas.
        public RtlSvgTable? Diagram;

        // An RTL figure + topics-list table: SVG matrix drawn as graphics, caption and
        // right-aligned bulleted items beside it (see RtlTopicsTable).
        public RtlTopicsTable? TopicsList;

        // A positioned media card: a relative overflow-hidden media box whose
        // bottom-anchored caption bars are position:absolute children, with a
        // float:left text column and a float:right label/value info panel below
        // (see PositionedCard).
        public PositionedCard? Card;
        // A positioned slide drawn at absolute geometry (see PositionedSlide).
        public PositionedSlide? Slide;
        // A flex-row waybill grid drawn at absolute geometry (see FlexGrid).
        public FlexGrid? Flex;
        // A single-line flex row of painted boxes (see FlexRow).
        public FlexRow? FlexRow;
        // A row of percent-width inline-blocks (see InlineRow).
        public InlineRow? InlineRow;
        // A preformatted block of verbatim lines (see PreBlock).
        public PreBlock? Pre;

        // A styled inline row (site nav bar, centered footer-link line): runs laid out
        // horizontally on one line, optionally over a full-content-width background bar.
        // All row geometry is carried in CSS px and converted (×0.75) at draw time.
        public List<RowRun>? RowRuns;
        public double RowHeightPx;        // vertical flow space the row consumes
        public Color? RowBarColor;        // background bar fill (null = none)
        public double RowBarHeightPx;
        public Color? RowBarBorderColor;  // 1px border under the bar
        public bool RowCentered;          // center the run group in the content box
        public double RowLeftPadPx;       // leading pad before the first left run
        public double RowRightPadPx;      // trailing pad after the last right-group run
        public double RowFontPx = 13;     // default run font size
        public double RowMarginTopPx;     // flow gap above the row
        public double RowMarginBottomPx;  // flow gap below the row
    }

    /// <summary>A centered search form: fixed-width center cell holding a text-input
    /// widget (with an optional overlay icon) and a row of push buttons, plus an
    /// optional side link that clips at the content edge. Px units throughout.</summary>
    internal sealed class SearchForm
    {
        public double CellWidthPx = 496;    // centered cell width (widest input class)
        public double InputWidthPx = 478;   // input outer width incl pads/borders/margin
        public double InputContentPx = 458; // input content width (button-row centering)
        public double InputHeightPx = 27;   // input outer height
        public string? InputName;
        public string? IconSrc;             // overlay icon (data URI), right-inset
        public double IconWPx = 27, IconHPx = 23;
        public double IconRightPx = 5, IconTopPx = 4;
        public List<(string Label, string Name)> Buttons = new();
        public double ButtonHeightPx = 32;
        public double ButtonFontPx = 15;
        public double ButtonPadPx = 8;      // horizontal label padding per side
        public double ButtonGapPx = 4;      // gap between adjacent buttons
        public Color ButtonBg = Color.FromArgb(236, 237, 238);
        public Color ButtonFg = Color.FromArgb(31, 31, 31);
        public double GapPx = 12;           // input row → button row gap
        public double MarginTopPx = 25;     // line box + wrapper margins above the input
        public double MarginBottomPx = 20;  // CSS form margin-bottom
        public string? LinkText;
        public string? LinkUrl;
        public Color LinkColor = Color.FromArgb(25, 103, 210);
        public double LinkFontPx = 11;
        public double LinkMarginLeftPx = 13;
    }

    /// <summary>A fixed-width RTL diagram table: a full-width vector figure with a
    /// centered caption, per-column axis labels, and a legend row whose viewBox-only
    /// SVGs stretch to their column width at a common row height. The whole canvas is
    /// right-pinned (right edge on the right content margin), overflowing off the left
    /// page edge. Px units; column split follows the table auto-layout.</summary>
    internal sealed class RtlSvgTable
    {
        public double WidthPx = 936;
        public string? TitleText;
        public double TitleFontPx = 12.5;
        public int MainSvgIdx = -1;
        public double MainSvgWPx, MainSvgHPx;
        public List<(string Text, int Col)> MidLabels = new();
        public List<(int SvgIdx, string Label)> Legend = new();
        // Layout for the 2-label + 3-legend shape; the authored
        // stylesheet is external and unavailable, so the auto-layout result is
        // carried as calibrated canvas fractions (rightmost item first).
        // Legend svg boxes: left edge / width as fractions of the table width.
        public double[] LegendXFrac = { 459.5 / 936, 157.9 / 936, -143.8 / 936 };
        public double[] LegendWFrac = { 466.0 / 936, 269.0 / 936, 269.0 / 936 };
        // Right anchors (fractions of table width from the canvas LEFT edge) for
        // the legend labels and the mid axis-label row.
        public double[] LegendLabelRightFrac = { 0.9886, 0.4886, 0.05 };
        public double[] MidLabelRightFrac = { 0.954, 0.5228, 0.05 };
        public double LabelFontPx = 12.5;
    }

    /// <summary>An RTL topics table: one row pairing an inline-SVG matrix figure cell
    /// with a cell holding a heading caption and a bulleted topics list. The figure
    /// draws as graphics (its text is not part of the flow fragments) and
    /// the caption + list lay out on the left: each item right-aligned on a common pen
    /// edge, in the serif face at the browser default size, with the bullet marker to
    /// the right of the text (marker drawn before its item, so the absorber sees
    /// caption, bullet, item, bullet, item, …). Pen anchors are calibrated for
    /// this shape (see grp/T notes).</summary>
    internal sealed class RtlTopicsTable
    {
        public int SvgIdx = -1;
        public double SvgWPx, SvgHPx;
        public string? CaptionText;
        public List<string> Items = new();
    }

    /// <summary>A positioned media card (the real-estate listing shape): a
    /// position:relative overflow-hidden media box of declared px size whose
    /// bottom-anchored full-width caption bars are position:absolute children
    /// (each a fill + one serif line), plus a float:left prose column clipped
    /// to its declared box and a float:right info panel of label/value
    /// paragraph columns. All geometry in CSS px; converted at draw time.</summary>
    internal sealed class PositionedCard
    {
        public double MediaWPx, MediaHPx;         // the relative media box (545×200)
        public bool HasImg;                        // broken <img> → placeholder icon
        // bottom-anchored bars: height, bottom offset, fill, text colour, text
        public List<(double HPx, double BottomPx, Color Fill, Color TextColor, string Text)> Bars = new();
        public double TextWPx, TextHPx;            // float:left prose box (290×200)
        public string ParaText = "";
        public double InfoWPx, InfoMtPx;           // float:right panel (230, margin-top 7)
        public double ContainerHPx;                // the whole card's flow height (410)
        // label/value paragraph slots, in document order per column:
        // MtPx = declared margin-top; Kind: 0 text, 1 whitespace-only,
        // 2 whitespace-only with an inline child (a full default-size slot)
        public List<(string Text, bool Bold, double MtPx, int Kind)> Labels = new();
        public List<(string Text, bool Bold, double MtPx, int Kind)> Values = new();
    }

    /// <summary>A positioned slide (a slide editor's saved markup): a
    /// position:relative canvas of declared min/max px size whose children are
    /// absolutely positioned boxes — free text runs and background-image divs
    /// (stretched by background-size:100% 100%, else centre-cropped), one
    /// optionally rotated by a CSS transform. All geometry in CSS px.</summary>
    internal sealed class PositionedSlide
    {
        public double MinWPx, MinHPx;
        public List<SlideItem> Items = new();
    }

    internal sealed class SlideItem
    {
        public bool IsImage;
        public string? Src;
        public string Text = "";
        public double LeftPx, TopPx, WPx, HPx, RotDeg;
        public bool Stretch;   // background-size: 100% 100%
    }

    // Positioned-slide text metrics: Arial's Windows line (winAscent 1854,
    // winDescent 434, em 2048) — the sans stack these slide sheets declare.
    private const double SlideTextAscEm = 1854.0 / 2048.0;
    private const double SlideTextDescEm = 434.0 / 2048.0;

    /// <summary>A flex-row waybill grid: a full-width bordered container whose
    /// rows are display:flex divs of percent-width bordered columns, each cell a
    /// dt/dd label-value pair (dd right-aligned), a plain wrapping text, or the
    /// signature composite. All measures from the expected render of the
    /// waybill fixture; geometry in fractions of the wrapper width.</summary>
    internal sealed class FlexGrid
    {
        public string Title = "";
        public List<FlexGridRow> Rows = new();
        // A positioned page wrapper's physical content width (width: 8in) —
        // drives the page-widen; 0 = the authored page is kept.
        public double PageContentPt;
        // The wrapper chain's declared container height (physical × percent
        // factors, e.g. 10in × 107%): the container border runs to this depth,
        // overflowing onto a continuation page. 0 = the border closes at the
        // last row.
        public double PageContentHPt;
        // Rows came from <tr>s: the table flavour adds the UA border-spacing
        // (2px) to the wrapper inset and the row pitch.
        public bool TableFlavor;
    }

    internal sealed class FlexGridRow
    {
        public List<FlexGridCell> Cells = new();
    }

    internal sealed class FlexGridCell
    {
        public double WFrac;                 // resolved width / 100
        public double PadFrac;               // class padding-left, % of the ROW width
        public bool BL, BR, BT, BB;
        public bool Center;
        public string Label = "";            // dt text (or the cell's own single line)
        public string LabelRight = "";       // a float:right span inside the dt
        public double LabelRightMrFrac;      //   …its margin-right, % of the cell
        public string Value = "";            // dd text — right-aligned at the cell edge
        public bool HasDd;                   // a dd exists (an EMPTY dd still keeps its line box)
        public bool ValueWide;               // dd width:100%: float:left + float:right halves
        public string ValueLeft = "", ValueRight = "";
        public double ValueRightMrFrac;
        public double ValuePadPx;            // dd vertical padding (px)
        public bool PlainWrap;               // no dl — the text wraps left-aligned
    }

    // Flex-grid line metrics, measured on the waybill's expected render:
    // in-cell line band 10.5 pt (14px), row height = bands + 0.75 border share;
    // title band = container border to first row top; dd right inset = the 2px
    // margin-right + half the border.
    private const double FlexLineBandPt = 10.5;
    private const double FlexRowBorderPt = 0.75;
    private const double FlexTitleBandPt = 28.5;
    private const double FlexValueInsetPt = 1.9;
    private const double FlexTitleFontPt = 24.0;   // h1 2em of the 16px UA root
    // Times New Roman Windows line (winAscent 1825, winDescent 443, em 2048).
    private const double SerifAscEm = 1825.0 / 2048.0;

    // Positioned-card metrics. The UA quantities derive from the browser box
    // model (8px body margin; the serif Windows line); the pitch quantities are
    // empirical fixed values of this layout dialect — line-height:4px paragraphs
    // pitch on a rhythm the CSS model does not predict, so the measured numbers
    // are carried verbatim.
    private const double CardBodyPadPt = 8 * 0.75;      // UA body margin

    private const double CardIconBoxPt = 34.0;          // broken-image frame (measured 96..130 × 78..112)

    private const double CardInfoPitchPt = 13.08;       // 12px-font p pitch under line-height:4px (measured)

    private const double CardInfoEmptyPt = 3.36;        // a whitespace-only p between two slots (measured)

    private const double CardInfoEmptyFullPt = 30.30;   // a whitespace p WITH an inline child: an unstyled default-size slot (measured)

    private const double CardInfoLineBoxPt = 4 * 0.75;  // the declared line-height:4px box; a margin-top p pitches mt + this

    private const double CardInfoStartPt = 6.6;         // panel content top → first label line-box top (measured)

    private const double CardParaFirstPt = 13.55;       // media box bottom → first prose line-box top (measured)

    /// <summary>One run of a styled inline row: a text span or an image, with its own
    /// face/color/paddings. Px units throughout; converted at draw time.</summary>
    internal sealed class RowRun
    {
        public string Text = "";
        public double FontPx = 13;
        public bool Bold;
        public Color Color = Color.FromArgb(0, 0, 0);
        public double PadLeftPx, PadRightPx;      // padding inside the run box
        public double MarginLeftPx, MarginRightPx; // spacing outside the run box
        public Color? TopStripColor;               // short strip across the run box top
        public double TopStripHeightPx = 2;
        public bool RightGroup;                    // right-aligned cluster (e.g. nav sign-in)
        public string? ImgSrc;                     // image run (data URI / path) instead of text
        public double ImgWPx, ImgHPx;
        public string? Url;                        // link annotation over the run box
    }

    /// <summary>True when the markup carries block-level structure (lists,
    /// paragraphs, headings, tables) that needs vertical/indented block layout
    /// rather than a single flat run of stripped text.</summary>
    internal static bool HasBlockStructure(string html) =>
        Regex.IsMatch(html ?? "", @"<\s*(ul|ol|li|p|div|h[1-6]|table|tr|blockquote|hr|form|input|textarea)\b",
            RegexOptions.IgnoreCase);

    /// <summary>Parse the single-font inline-emphasis dialect: the whole fragment
    /// is one <c>&lt;font&gt;</c> element carrying a face and a legacy size, whose
    /// content is only text and b/strong/u/i/em emphasis. Yields the styled runs
    /// in order; any other structure rejects the parse.</summary>
    internal static (string face, double sizePt, List<(string text, bool bold, bool underline, bool italic)> runs)? TryParseInlineEmphasisFont(string? html)
    {
        string? face = default;
        double sizePt = default;
        List<(string text, bool bold, bool underline, bool italic)>? runs = default;
        face = string.Empty;
        sizePt = 0;
        runs = new List<(string, bool, bool, bool)>();
        var s = (html ?? "").Trim();
        var m = Regex.Match(s, @"^<font\s+([^>]*)>(.*)</font>$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!m.Success) return null;
        var attrs = m.Groups[1].Value;
        var body = m.Groups[2].Value;

        var fm = Regex.Match(attrs, "face\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|(\\S+))",
            RegexOptions.IgnoreCase);
        if (!fm.Success) return null;
        face = fm.Groups[1].Success ? fm.Groups[1].Value
             : fm.Groups[2].Success ? fm.Groups[2].Value : fm.Groups[3].Value;

        var sm = Regex.Match(attrs, "size\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|(\\S+))",
            RegexOptions.IgnoreCase);
        if (!sm.Success) return null;
        var sizeStr = (sm.Groups[1].Success ? sm.Groups[1].Value
                    : sm.Groups[2].Success ? sm.Groups[2].Value : sm.Groups[3].Value).Trim();
        var dEnd = 0;
        while (dEnd < sizeStr.Length && (char.IsDigit(sizeStr[dEnd])
               || (dEnd == 0 && (sizeStr[0] == '+' || sizeStr[0] == '-')))) dEnd++;
        if (dEnd == 0 || !int.TryParse(sizeStr[..dEnd], out var scale)) return null;
        if (sizeStr[0] is '+' or '-') scale = 3 + scale;
        sizePt = HtmlFontSizeToPt(Compat.Clamp(scale, 1, 7));

        var bold = 0; var underline = 0; var italic = 0;
        var pos = 0;
        var textStart = 0;
        void FlushText(int upTo, List<(string, bool, bool, bool)> sink, int b2, int u2, int i2)
        {
            if (upTo > textStart)
                sink.Add((body[textStart..upTo], b2 > 0, u2 > 0, i2 > 0));
        }
        while (pos < body.Length)
        {
            var lt = body.IndexOf('<', pos);
            if (lt < 0) break;
            var gt = body.IndexOf('>', lt);
            if (gt < 0) return null;
            var tag = body[(lt + 1)..gt].Trim();
            var closing = tag.StartsWith('/');
            var name = (closing ? tag[1..] : tag).Trim().ToLowerInvariant();
            var delta = closing ? -1 : 1;
            FlushText(lt, runs, bold, underline, italic);
            switch (name)
            {
                case "b": case "strong": bold += delta; break;
                case "u": underline += delta; break;
                case "i": case "em": italic += delta; break;
                default: return null;
            }
            pos = gt + 1;
            textStart = pos;
        }
        FlushText(body.Length, runs, bold, underline, italic);
        return (runs.Count > 0) ? (face, sizePt, runs) : null;
    }

    /// <summary>Extract the rule colour and width for an &lt;hr&gt; from its
    /// inline style. Reads the CSS border shorthand / border-color / color.</summary>
    private static (Color? color, double width) ParseHrStyle(Dictionary<string, string>? attrs)
    {
        Color? color = default;
        double width = default;
        color = null;
        width = 1;
        if (attrs is null) return (color, width);
        attrs.TryGetValue("style", out var style);
        style ??= "";
        // Width from the first pixel length in a border declaration, else the
        // legacy SIZE attribute (rule thickness in px).
        var wm = Regex.Match(style, @"border[^:]*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
        if (wm.Success && double.TryParse(wm.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var w) && w > 0)
            width = w;
        else if (attrs.TryGetValue("size", out var sizeAttr)
                 && double.TryParse(sizeAttr, System.Globalization.NumberStyles.Float,
                     System.Globalization.CultureInfo.InvariantCulture, out var sz) && sz > 0)
            width = sz;
        // Colour: scan the style string (covers border/border-color/color).
        color = ParseCssColor(style);
        return (color, width);
    }

    /// <summary>Emit a CSS box decoration — an optional <paramref name="fill"/> rectangle
    /// and an optional <paramref name="border"/> stroke — onto <paramref name="page"/> at
    /// the given lower-left origin and size (all in points). No-op when neither is set.</summary>
    private static void DrawBox(Page page, double llx, double lly, double w, double h,
        Color? border, double borderWidth, Color? fill, bool prepend = false)
    {
        if (w <= 0 || h <= 0 || (border is null && fill is null)) return;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        string N(double v) => v.ToString("F2", ci);
        string Rgb(Color c) => $"{N(c.R / 255.0)} {N(c.G / 255.0)} {N(c.B / 255.0)}";
        var sb = new StringBuilder();
        sb.Append("q ");
        if (fill is not null)
            sb.Append($"{Rgb(fill)} rg {N(llx)} {N(lly)} {N(w)} {N(h)} re f ");
        if (border is not null)
        {
            var bw = borderWidth > 0 ? borderWidth : 0.75;
            sb.Append($"{Rgb(border)} RG {N(bw)} w {N(llx)} {N(lly)} {N(w)} {N(h)} re S ");
        }
        sb.Append("Q");
        var ops = Encoding.ASCII.GetBytes(sb.ToString());
        if (prepend) page.PrependContentStream(ops);
        else page.AddContentStream(ops);
    }

    // One fetch per URL per process: conversions repeat a logo/letterhead across
    // pages, and a dead host must not stall every occurrence.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte[]?>
        RemoteImageCache = new(StringComparer.Ordinal);

    /// <summary>Height of the UA <c>&lt;hr&gt;</c> groove: the two 1 css px edges
    /// that make the browser's inset rule.</summary>
    private const double HrGrooveH = 1.5;

    /// <summary>…each edge stroked at one css pixel.</summary>
    private const double HrGrooveW = 0.75;
    /// <summary>The UA fieldset frame's stroke width, read off the reference's content stream
    /// (`0.75 w` beside its #808080 colour), the same on a legend-less fieldset as a legend-bearing one.</summary>
    private const double MetricFieldsetFrameW = 0.75;

    /// <summary>…and the groove's lit edge, the UA's #555555.</summary>
    private const double HrGrooveGrey = 1.0 / 3.0;

    /// <summary>Parse HTML into the flat block list used by the layout pass.
    /// Exposed for the in-page HtmlFragment renderer.</summary>
    internal static List<Block> ParseHtmlBlocks(string html, double bodyFontSize = 0,
        bool inlineEmphasisRuns = false, bool paragraphMargins = false)
        // A standalone <br> — one with no text pending on its line — is an empty
        // line box, not nothing: `</p><br><br><strong>` opens two blank lines
        // before the heading. The in-page fragment renderer flows raw authored
        // markup, where those breaks ARE the vertical rhythm.
        // A fragment that asks for paragraph margins takes the UA block rhythm: a <p>
        // carries 1.12 em above and below (probed against the reference: 13.44 pt at 12 pt,
        // adjacent paragraphs collapsing to one margin, a <div> carrying none).
        => ParseBlocks(html, null, bodyFontSize: bodyFontSize,
            inlineEmphasisRuns: inlineEmphasisRuns, brBlankLines: true, uaBlockRhythm: paragraphMargins);

    /// <summary>Detect the monospace pre-formatted fragment dialect: ONE top-level
    /// <c>&lt;font style="font-family:courier; font-size:Npt"&gt;</c> whose body is only
    /// text, entities, <c>&lt;br/&gt;</c> line breaks and <c>&lt;b&gt;</c> runs. Such HTML
    /// is a column-aligned text report — every <c>&amp;nbsp;</c> is a real fixed-width
    /// column space and every <c>&lt;br/&gt;</c> a hard line box, so it renders as verbatim
    /// Courier lines rather than through the collapsing block flow. A leading
    /// whitespace-only segment (the <c>&amp;nbsp;&lt;br/&gt;</c> lead-in) occupies no line.</summary>
    internal static (double sizePt, List<List<(string text, bool bold)>> lines)? TryParseMonoFontLineBoxes(string? html)
    {
        double sizePt = default;
        List<List<(string text, bool bold)>>? lines = default;
        sizePt = 0;
        lines = new List<List<(string text, bool bold)>>();
        var s = (html ?? "").Trim();
        var m = Regex.Match(s, @"^<font\s+style\s*=\s*(['""])(?<st>[^'""]*)\1\s*>(?<body>[\s\S]*)</font>$",
            RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        var st = m.Groups["st"].Value;
        if (!Regex.IsMatch(st, @"font-family\s*:\s*['""]?\s*courier", RegexOptions.IgnoreCase)) return null;
        var fsM = Regex.Match(st, @"font-size\s*:\s*([\d.]+)\s*pt", RegexOptions.IgnoreCase);
        sizePt = fsM.Success
            ? double.Parse(fsM.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)
            : 10;
        var body = m.Groups["body"].Value;
        foreach (Match tg in Regex.Matches(body, @"<[^>]*>"))
            if (!Regex.IsMatch(tg.Value, @"^<\s*/?\s*(br|b|strong)\s*/?\s*>$", RegexOptions.IgnoreCase))
                return null;

        var segs = Regex.Split(body, @"<br\s*/?>", RegexOptions.IgnoreCase);
        for (var i = 0; i < segs.Length; i++)
        {
            var runs = new List<(string text, bool bold)>();
            var boldDepth = 0;
            var pos = 0;
            var seg = segs[i];
            void Flush(int upTo)
            {
                if (upTo <= pos) return;
                var t = DecodeEntities(seg[pos..upTo]).Replace('\u00A0', ' ');
                if (t.Length > 0) runs.Add((t, boldDepth > 0));
            }
            foreach (Match tg in Regex.Matches(seg, @"<[^>]*>"))
            {
                Flush(tg.Index);
                if (Regex.IsMatch(tg.Value, @"^<\s*(b|strong)\s*>$", RegexOptions.IgnoreCase)) boldDepth++;
                else if (Regex.IsMatch(tg.Value, @"^<\s*/\s*(b|strong)\s*>$", RegexOptions.IgnoreCase)) boldDepth--;
                pos = tg.Index + tg.Length;
            }
            Flush(seg.Length);
            // The lead-in segment before the first <br/> is column padding, not a line.
            if (i == 0 && runs.TrueForAll(r => string.IsNullOrWhiteSpace(r.text))) continue;
            lines.Add(runs);
        }
        // Trailing empty segment after the final <br/> is the end of content, not a blank line.
        while (lines.Count > 0 && lines[^1].Count == 0) lines.RemoveAt(lines.Count - 1);
        return (lines.Count > 1) ? (sizePt, lines) : null;
    }
}
