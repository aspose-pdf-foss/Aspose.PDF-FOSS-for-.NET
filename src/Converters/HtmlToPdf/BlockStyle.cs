using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    private sealed class BlockStyle
    {
        public double FontSize;
        // font-variant: small-caps seen on a span of this block (redline dialect):
        // lowercase draws as uppercase at the small-caps ratio.
        public bool SmallCaps;
        public double TextIndentPt;
        public double LetterSpacingPt;
        // The paragraph's own pt right margin: its wrap box ends this far
        // inside the content edge (pt-styled fragment dialect only).
        public double RightInsetPt;
        // An explicit font-size:0 (the "clear:both;height:0;font-size:0" float
        // terminator idiom): a whitespace-only block at size 0 occupies NO line.
        public bool ZeroFontSize;
        public string FontRes = "F1";
        public string? FontFamily;
        // Foreground text color from an inline color: declaration or a legacy <font color>.
        // Null = default black.
        public Color? ForeColor;
        // Point size from a legacy <font size="N"> attribute (0 = none). Kept separate from
        // FontSize so it is inert for the legacy flow and read only by the gated dialect path.
        public double LegacyFontPt;
        // Set when this block's size came from a legacy <font size="N"> attribute — the
        // marker for the legacy-font dialect (summernote / Word-paste HTML).
        public bool LegacyFontSized;
        // Inline emphasis seen anywhere in the block (both true = bold-italic). Read only
        // by the embedded-face page-level path; the legacy flow keeps using FontRes.
        public bool EmBold;
        public bool EmItalic;
        public double MarginTop;
        public double MarginBottom;
        // Apply MarginTop even at the top of a page (the filing dialect's repeated
        // page-header block keeps its CSS top margin below the page margin).
        public bool MarginTopAlways;
        public double LeftIndent;
        // position:absolute/fixed containing-block chain: the nearest such ancestor's own
        // resolved box, in points from the page's baseline content origin (left edge = 0),
        // and its own width. Composed forward through nested absolute ancestors by
        // ApplyAbsolutePositionResolve; seeded at the root to (0, the page's baseline
        // content width) so a top-level absolute element resolves against the page itself.
        public double AbsOriginLeftPt;
        /// <summary>The top of the absolute containing box this element resolved, in points from
        /// the page's content top: ancestors' `top` offsets, their translate Y (applied once) and
        /// the chrome inside the box - the vertical twin of AbsOriginLeftPt.</summary>
        public double AbsOriginTopPt;
        /// <summary>The translate X of every transformed absolute ancestor-or-self, summed: the
        /// reference counts it a second time toward the page width (see the widen accumulator).</summary>
        public double AbsTranslateSumPt;
        /// <summary>True once an absolutely positioned ancestor-or-self has resolved its box: the
        /// root seeds AbsOriginWidthPt with the content width, so that alone cannot say so.</summary>
        public bool InAbsoluteChain;
        public double AbsOriginWidthPt;
        // Sum of the width-BILLING container chrome (padding + borders of width:auto
        // ancestors) on this style's chain — containerBoxIndents mode. A width:100%
        // ancestor's chrome indents but overflows its parent, so it does not bill
        // the page-widen; a width:auto ancestor's chrome does both.
        public double BillPadPt;
        // A box-shadow'd container (the widget CARD) on this style's chain: the
        // shadow colour, and the card's own left chrome (padding + border) so the
        // draw can recover the card box from the content position.
        public Color? CardShadowColor;
        public double CardChromePt;
        /// <summary>The border of the nearest positioned ancestor that draws one - the widget
        /// card around a chart - carried down to the picture that sits inside it, with the
        /// chrome (border plus padding) between the card's outer edge and its descendants.</summary>
        public Color? CardFrameColor;
        public double CardFrameBorderPt;
        public double CardFrameInsetPt;
        public bool IsListItem;
        public bool PageBreakBefore; // CSS page-break-before:always on this element
        public bool PageBreakAfter;  // CSS page-break-after:always — break at the close
        public string? PageName;     // CSS3 paged-media `page: <name>` — the named page this element wants
        // Unitless CSS line-height factor from a class rule (coverStyles mode);
        // 0 = the flow's own default pitch.
        /// <summary>A stylesheet's unitless line-height on the calibrated flow (pitch only).</summary>
        public double SheetLineFactor;
        public double LineFactor;
        // A percent line-height's factor on the UA flow (inherited as a factor; see Block.UaLineFactor)
        public double UaLineFactor;
        // The element this style opened for (lower case; "" for the root)
        public string Tag = "";
        // The field-list dialect: this element is the fields box; this element is a direct child of it;
        // an element has already opened inside this one (a `:first-child` test)
        public bool FieldsBox;
        public bool InFieldsBox;
        public bool ChildOpened;
        // The element's padding-bottom from its sheet rule (the field-list dialect), for its last block
        public double PadBottomPt;
        // True when LineFactor came from a PARSED unitless line-height
        // declaration (inline style or stylesheet rule) — the CSS-box flow
        // seat/margins apply only then, never to a dialect-assigned factor.
        public bool DeclaredLineFactor;
        // style="width:N%" on an enclosing div (browser-UA flow only): the block's
        // wrap box narrows to that fraction of the content width — the source
        // renderer stacks such divs but still wraps their text at the declared width.
        public double WidthFrac;
        // Absolute width (style="width:680" / "width:680px") on an enclosing div —
        // recorded always, honored as the wrap box only by the form-document dialect.
        public double WidthPx;
        // style="padding-top:Npx" on the enclosing div (browser-UA flow only):
        // non-collapsing vertical space above the block.
        public double PadTop;
        // The element's OWN padding-top longhand (any flow) — spent only by the
        // childless-empty close spacer, never carried onto text blocks (a content
        // block's padding stays with the dialect's own PadTop rules above).
        public double OwnPadTopPt;
        // blocks.Count when this element opened; -1 until a block-tag open sets it.
        // At close, equality means the element's whole subtree emitted nothing.
        public int BlocksAtOpen = -1;
        // text-align:right (honored by the print-grid dialect only).
        public bool AlignRight;
        // Print-grid heading band (a ".cls h4" rule's border-bottom).
        public Color? BandColor;
        public double BandPx;
        public double BandPadPx;
        // List context carried on an <ol>/<ul> style so its <li> children can be
        // numbered/bulleted. ListKind: 0 = not a list, 1 = ordered, 2 = unordered.
        // ListCounter holds the last-used ordinal (incremented per <li>); the first
        // <li> renders ListCounter+1, so `start="5"`/`counter-set: item 4` sets it to 4.
        public int ListKind;
        public int ListCounter;
        // CSS list-style-type carried on the <ol> (inline style or attribute):
        // "" = decimal; otherwise upper-alpha / lower-alpha / upper-roman /
        // lower-roman markers, formatted per item from ListCounter.
        public string ListStyleType = "";
        // Styled-article panel list (`.td-toc`): the block-link's padding-bottom,
        // carried on the list style so each item pitches one line box + this pad.
        public double TocLinkPadPt;
        // Styled-article dialect marker, inherited down the style stack so the
        // declaration applier can honour the box-model cases the calibrated
        // dialects never see (e.g. negative gutter margins).
        public bool ArticleRhythm;
        // CSS `li:nth-child(An+B)::before { content: … }` generated markers active for this
        // list (matched to the <ol>/<ul>'s class when it opens); ChildIndex counts the list's
        // children so each <li> can pick the matching rule. Null = no ::before markers → the
        // numeric/bullet default applies.
        public List<BeforeMarker>? BeforeRules;
        public int ChildIndex;
        // Explicit CSS height / min-height in points. When >0 the block's
        // own rendered area must be at least this tall, so empty-body
        // styled divs (common in CMS template HTML) still contribute
        // vertical space to pagination.
        public double ExplicitHeight;
        // CSS box decoration (background-color / border) carried to the emitted Block.
        public Color? BackgroundColor;
        // Pinned-body report band pad (see Block.BandPadPt).
        public double BandPadPt;
        // The CSS `padding` of a block that paints a background (see
        // Block.BgPadTopPt): the fill covers the line boxes plus this much above
        // and below, and the text starts BgPadLeftPt inside the content edge.
        public double BgPadTopPt;
        public double BgPadBottomPt;
        public double BgPadLeftPt;
        public Color? BorderColor;
        public double BorderWidth;
        // Only border-top declared (the `border:none; border-top: solid …` divider).
        public bool BorderTopOnly;
        // A sheet box's border-bottom (the chain dialect's banded heading): its rule is drawn
        // under the band and its width is box space the flow steps over.
        public double BorderBottomWidth;
        public Color? BorderBottomColor;
        // The band paints the block's LINE BOX (the sheet-typography flow): true when the sheet
        // gave the block a background or a rule, so the draw uses the box, not the legacy strip.
        public bool SheetBox;
        public double UaBoxTopPt;
        public double UaBoxBottomPt;
        public double UaPadTopPt;
        public double UaRuleTopPt;
        public Color? UaRuleTopColor;
        // border-radius corner rounding (first shorthand value), px→pt.
        public double BorderRadiusPt;
        // UA-serif flow inline-span typography: a px line-height fixes the LINE
        // BOX; the span's own margin-left insets its text within the element box.
        public double LineBoxPt;
        public double TextInsetPt;
        // Declared height/min-height as a FLOOR (see Block.HeightFloorStart): the
        // element's content grows down into it and only what FOLLOWS moves.
        public double HeightFloorPt;
        // True between this element's open and close: its own ExplicitHeight is
        // being spent by the floor markers, so an inner flush must not also emit
        // it as a spacer ahead of the content.
        public bool HeightFloorDeferred;
        // UA-serif flow marker: negative inline margins are real here (the
        // calibrated dialects never met one).
        public bool UaSerif;
        // The UA flow proper (never the print grid, whose body-level container padding the page
        // margins already carry): a block's padding and margin shorthands, a percent inline-block's
        // width and a white background are real box geometry here.
        public bool UaBoxes;
        // margin-top came from an AUTHORED declaration (inline/stylesheet),
        // not a UA element default - it MAX-collapses with the body margin.
        public bool MarginTopAuthored;
        /// <summary>An authored margin-bottom (longhand or shorthand) replaced the UA default; the UA closing gap does not apply.</summary>
        public bool MarginBottomAuthored;
        // Painted-box dimensions (a tiny repeated background tile over an
        // explicitly sized element): the fill spans this declared box rather
        // than each text line. Zero = no painted box.
        public double BgBoxWidthPt;
        public double BgBoxHeightPt;
        // ...or the box height as a fraction of the viewport (`height: 100vh`), resolved
        // against the page's content height where the box is painted.
        public double BgBoxHeightVh;
        // A background IMAGE over the declared box (`background-image: url(...)` with a width x
        // height): the source as written and the `background-size` value, resolved where the
        // box is painted. Null = no image box.
        public string? BgImageSrc;
        public string BgImageSize = "";
        // The indent of the element that declared the painted box - its child blocks may stand
        // further in (their own padding), the box does not.
        public double BgBoxIndentPt;
        // The block belongs to an in-page HtmlFragment (probed against the reference): a
        // unitless `font-size: 13` is 13 pt there (13px is 9.75 pt), where the page
        // converter keeps the CSS quirk of a unitless length being pixels, and a list
        // indents its items 30 pt with the marker right-aligned 4.5 pt before them.
        public bool InPageFragment;
        // Form-report dialect (control-group + label documents): opts style parsing
        // into the CSS the expected render honours there — the `margin:` shorthand,
        // padding-bottom, and font-weight:normal undoing a heading's default bold.
        // Off everywhere else so calibrated conversions keep their spacing.
        public bool FormDialect;
        // The enclosing element's resolved font size — the base an em font-size
        // resolves against (1.75em on a 12pt body = 21pt, regardless of the tag's
        // legacy default size). Form dialect only.
        public double ParentFontSize;
        // text-align:center from a class rule — honored by the metric flow only.
        public bool AlignCenter;
        // A CSS text-align:center from anywhere (inline style included) — honored by
        // the sectioned-report flow.
        public bool AlignCenterCss;
        // float:left on this element or one enclosing it — an image inside such a box
        // is taken out of the flow and the text beside it wraps in the space left over.
        public bool FloatLeft;
        /// <summary>The float flag reached this element from an ANCESTOR rather than its own
        /// declaration. CSS does not inherit `float`; the flag is carried down so a nested image
        /// still leaves the flow, but such a block is NOT a float of its own.</summary>
        public bool FloatInherited;
        // float:right — the UA flow lays such an element as a shrink-to-fit box
        // against the right content edge, sharing its line with adjacent floats.
        public bool FloatRight;

        /// <summary>Left inset from a `margin:` SHORTHAND's fourth value, recorded for
        /// every document but read only by the float flow - the calibrated dialects take
        /// their horizontal margins from the dedicated margin-left handling.</summary>
        public double ShorthandLeftPt;

        /// <summary>The `margin:` shorthand's TOP value, recorded for every document and
        /// read only by the float flow.</summary>
        public double ShorthandTopPt;

        /// <summary>The whole declared `font-family` list, recorded for every document but
        /// read only by the float flow: CSS falls through the stack to the first family
        /// that is actually installed, where FontFamily keeps the first NAMED one.</summary>
        public string? FontFamilyStack;

        /// <summary>A `margin-right` longhand, recorded for every document but read only
        /// by the float flow - the calibrated dialects wrap on their own measured text
        /// columns, where honouring the declaration everywhere would re-break them.</summary>
        public double MarginRightPt;

        /// <summary>A declared `width: Npx`, in points. Recorded for every document and
        /// read only by the float flow, where a block keeps the box it declares even when
        /// that box overflows the content frame.</summary>
        public double DeclaredWidthPt;
        /// <summary>A width declared as a PERCENTAGE, as a fraction. Kept apart from the point
        /// width because it resolves against a box that is not known until the sheet is final.</summary>
        public double DeclaredWidthFrac;
        /// <summary>Padding a CONTAINER declared, read only by its background box. Deliberately
        /// not BgPadLeftPt, which also insets the TEXT: these must move nothing but the fill.</summary>
        public double BgSpanPadTopPt;
        public double BgSpanPadBottomPt;
        public double BgSpanPadLeftPt;
        // ALIGN="justify" / text-align:justify — flow lines stretch word gaps to the
        // content box (except a paragraph's last line).
        public bool AlignJustify;
        // The top margin belongs to an element that opened INSIDE another block
        // element (it travels with the margin when it collapses into a child). A
        // body-level element's top margin vanishes at the document top, but a
        // nested element's survives like an authored margin (max-collapsed with
        // the UA body margin).
        public bool MarginTopNested;
        // The legacy ALIGN="center" ATTRIBUTE (not CSS classes, which stay
        // metric-flow-only): centre each measured line in the content box.
        public bool AlignCenterAttr;
        // The element's first content was a `<br>` spacer (see BlocksAtOpen): the text that
        // follows continues the same paragraph.
        public bool LeadingBreakSpacer;
    }
}
