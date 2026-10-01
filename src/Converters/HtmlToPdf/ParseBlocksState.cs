using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class ParseBlocksState
{
    // Decode entities once at the text layer.
    public List<Token> tokens = null!;
    // The flow's content width (the sheet less its page margins), the base a percent box
    // width resolves against in the UA-serif flow; zero where the caller supplies none.
    public double contentWidthPt;
    public List<Block> blocks = null!;
    public StringBuilder currentText = null!;
    public Stack<BlockStyle> styleStack = null!;
    // Open height floors: the marker block's index, the floor, and the style
    // depth that owns it (see Block.HeightFloorStart).
    public Stack<(int Marker, double H, int Depth)> heightFloors = null!;
    // Inline <a href> spans accumulated for the block currently being built, in
    // currentText (raw, pre-collapse) coordinates. Flushed (and translated to the
    // collapsed Text's coordinates) when the block is emitted.
    public List<(int start, int end, string url)> rawAnchors = null!;
    public Stack<(int start, string url)> openAnchors = null!;
    // Anchor-target names (id / <a name>) seen since the last flush; attached to
    // the block being emitted so #fragment links can resolve to its page. If the
    // block is empty they carry forward to the next non-empty block.
    public List<string> pendingAnchorNames = null!;
    // A list-item marker ("5." / "•") set when an <li> opens; attaches to the FIRST
    // non-empty block emitted inside that <li> (its text may be nested in child divs,
    // e.g. EditorJS markup), then clears so only the item's first line is marked.
    public string? pendingMarker;
    // UA-serif flow <font> scoping: each open saves the enclosing style's
    // typography so the matching close restores it.
    public Stack<(double Fs, Color? Fore, string? Fam)> uaFontSaves = null!;
    // UA-serif flow <span style="font-size"> scoping: each sized span saves the size it
    // replaced, keyed by its depth; its close queues that size, and the flush that emits
    // the span's own line puts it back for the lines that follow.
    public Stack<(int Depth, double Fs)> uaSpanSizeSaves = null!;
    public double uaSpanSizeRestorePt;
    // True when pendingMarker is CSS ::before generated content on an RTL list: it renders
    // after the item text (to its right) rather than before, so the item text is the earlier
    // fragment on the line.
    public bool pendingMarkerAfter;
    // Open `display:inline` divs (styled-article) — their closes must not pop.
    public int inlineDivDepth;
    // Span nesting depth, and the depths at which `display:block` spans opened
    // (class-rule block-spans break their line at open AND close; metric flow).
    public int spanDepth;
    public Stack<int> blockSpanDepths = null!;
    // The right-floated inline spans open (their depths): each flushes as its own block at its close.
    public Stack<int> floatRightSpanDepths = new Stack<int>();
    // ...and the `unicode-bidi: isolate` spans: (depth, start offset) while open, then the raw text
    // ranges whose spaces the flush makes non-breaking (an isolated run is one unbreakable unit).
    public Stack<(int Depth, int Start)> isolateSpanOpen = new Stack<(int Depth, int Start)>();
    public List<(int Start, int End)> isolateRanges = new List<(int Start, int End)>();
    public bool floatRightFlush;
    // Ledger column state: a position:absolute+left span class opened — its
    // text flushes as its OWN block at the column x when the span closes.
    public double absSpanLeftPt;
    public int absSpanLabelIdx;
    // Browser-UA flow: an EMPTY paragraph (a self-closed <p/> or a stray
    // </p> with no open <p> — both quirks-parse as an empty p element)
    // contributes its UA margin to the next block by max-collapse.
    public double pendingEmptyPMarginPt;
    // pt-styled fragment: size of the first pt-sized span in the block being
    // accumulated (it sets the first LINE BOX height when later spans shrink).
    public double ptyLeadFs;
    public int pOpenDepth;
    // fieldsetBoxes state: the open <legend>'s saved typography.
    public (double, string) fsLegendSave;
    public bool fsInLegend;
    // Inline-block title columns (quirks CSS-run docs): a span whose class rule
    // declares display:inline-block with a width is a TITLE column — its text
    // becomes its own run and the text that follows seats at the column's
    // right edge on the same line. State: the open column's width + span depth,
    // the pending indent for the value run, and a keep-trailing-space marker
    // for the flush a <br> triggers (a collapsed newline before the <br>
    // survives as the fragment's trailing space).
    public double openTitleColW;
    public double openTitleColFrac;      // the column's percent share of the content width
    public double pendingColIndentFrac;
    public int titleColSpanDepth;
    public double pendingColIndent;
    // the field-list dialect: a label span is open (its suffix and bold apply at its close)
    public bool fieldLabelOpen;
    public bool keepTrailingSpace;
    // Empty-div spacer tracking (pinned-body report, see divBandBg): the open
    // records where it stood; a close with nothing in between is the spacer.
    public int emptyDivDepthMark;
    public int emptyDivBlocksAt;
    public int emptyDivTextAt;
    // Container box chrome (containerBoxIndents mode): the vertical border+padding
    // of divs opened since the last content block lands on the NEXT block's top
    // margin (the card's chrome above its first line), and a class-rule HEIGHT on
    // a container (the widget header band) floors that block's height.
    public double pendingBoxPadTop;
    public double pendingBoxHeight;
    public int pendingBorderBoxDepth;
    // True between <textarea> and </textarea>: the element becomes an AcroForm field,
    // so its inner text is the field's default value, not body content — suppress it.
    public bool inTextarea;
    // Inside a <select>: its <option> list is the control's VALUE SET, not flow content
    // — a closed dropdown shows exactly one entry. The chosen one is captured here and
    // drawn where the control sits when the tag closes.
    public bool inSelect;
    public bool inSelectedOption;
    public Block? textareaBlock;
    public StringBuilder textareaText = null!;
    public StringBuilder selectedText = null!;
    // Control-box dialect: every option's text is kept — the combo box is sized
    // by its widest entry — and the select's name carries to the AcroForm field.
    public List<string> selectOptions = null!;
    public StringBuilder curOptionText = null!;
    public string? selectName;
    // Inline-run bookkeeping (control-box dialect): a run opens at the first
    // control after a block boundary; text flushed while it is open joins it, and
    // any block boundary closes it. runPrevWasControl preserves the single
    // collapsed space between a control and the label text that follows it.
    public int inlineRunId;
    public int nextInlineRunId;
    public bool runPrevWasControl;
    // Control-box dialect: an <i>/<em> that closes without enclosing any text (an
    // icon placeholder) must not leave the whole rest of its block italic.
    public int italicOpenTextLen;
    // Between <button> and </button>: the inner text is the push-button's caption,
    // not flow content.
    public bool inButton;
    public StringBuilder buttonText = null!;
    // A mid-line broken <img> waiting to ride the end of the text block the
    // pending run flushes into (control-box dialect).
    public bool pendingInlineIcon;
    // A page-break-before seen on an element that emitted no block of its own; the
    // next emitted block (text or image) starts the fresh page instead.
    public bool pendingPageBreak;
    // The page NAME the flow is currently on (CSS3 paged media `page: <name>`). An element
    // that wants a differently named page starts a fresh one; an empty page just adopts the
    // name, which the break rule itself already honours.
    public string? pageName;
    // Suppression of display:none / visibility:hidden subtrees: while hiddenTag is
    // set, every token is dropped until the matching close tag (same-name depth
    // count) is reached. Hidden content is not part of the rendering — no text,
    // no fields, no reserved space.
    public string? hiddenTag;
    public int hiddenDepth;
    // <center> nesting depth — content inside is horizontally centered.
    public int centerDepth;
    // Inline <b>/<strong> run tracking (browser-UA flow and the in-page
    // HtmlFragment flow): raw-coordinate ranges over currentText, re-mapped to
    // collapsed coordinates at Flush.
    public List<(int start, int end)> rawBolds = null!;
    public int inlineBoldDepth;
    // In-page fragment flow: <b>/<i> promote the WHOLE enclosing style (its face is what
    // the flush and the embedded bold-italic face read), so the style the promotion
    // replaced is saved at the first open and put back by the flush after the last
    // close - the list after a bold lead-in draws regular again.
    public (string FontRes, bool EmBold, bool EmItalic)? emphasisSave;
    public bool emphasisRestorePending;
    public int inlineBoldStart;
    // The same bookkeeping for <u>: an underlined run inside the block's line.
    public List<(int start, int end)> rawUnders = null!;
    // Spans whose inline style opened an underline run (text-decoration:
    // underline) — keyed by span depth so the matching </span> closes it.
    public Stack<int> uaUnderSpanDepths = null!;
    // Spans whose own inline style declares font-weight:bold / font-style:italic:
    // the run they opened closes with THAT span, exactly as the <b>/<i> tags do.
    public Stack<int> styleBoldSpanDepths = null!;
    public Stack<int> styleItalicSpanDepths = null!;
    public int inlineUnderDepth;
    public int inlineUnderStart;
    public int inlineStrikeDepth;
    public int inlineStrikeStart;
    public List<(int start, int end)> rawStrikes = new List<(int start, int end)>();
    // A block whose TAG rule declares text-decoration: underline (`h2 { text-decoration:
    // underline }`): the run covers the block's whole text from this raw offset (-1 = none).
    public int blockUnderStart = -1;
    // Anchors that pushed a font-style frame for the sheet's `a { color; font-family }` rule;
    // their close restores it the way a </font> does.
    public int uaAnchorFrames;
    // And for <i>/<em>: an italic run inside the block's line (browser-UA flow).
    public List<(int start, int end)> rawItalics = null!;
    public int inlineItalicDepth;
    public int inlineItalicStart;
    // Both flows track bold RANGES; only the browser-UA flow suppresses the
    // whole-block face promotion that <b> otherwise performs.
    public bool trackBoldRuns;
    // Coloured inline spans (browser-UA flow): a span's own color is a RUN
    // over its content — it is scoped to the span, while the
    // legacy block-level styling would bleed it to the block end (the
    // saved email's red bold phrases). Keyed by span depth; the matching
    // </span> closes the run and restores the frame's colour.
    public List<(int start, int end, Color c)> rawColorRuns = null!;
    public Stack<(int depth, int start, Color c, Color? prev)> openColorRuns = null!;
    /// <summary>Span-scoped font sizes (sheet-typography flow), raw offsets with their size.</summary>
    public List<(int start, int end, double pt)> rawSizeRuns = null!;
    public Stack<(int depth, int start, double pt)> openSizeRuns = null!;
    public List<(int start, int end, string fam)> rawFamilyRuns = null!;
    public Stack<(int depth, int start, string fam)> openFamilyRuns = null!;
    /// <summary>Open sub/sup runs: the raw offset each opened at, closed by its own end tag.
    /// Kept apart from <see cref="openSizeRuns"/>, which a closing SPAN pops by depth.</summary>
    public Stack<int> openSubSupRuns = null!;
    /// <summary>Open container backgrounds: where the container's first child block would go,
    /// its fill, the width it declared, and the style depth that closes it.</summary>
    public Stack<(int idx, Color c, double w, double frac, double padT, double padB, double padL, int depth)> bgSpans = null!;
    // Redline decoration runs (see Block.DecorRuns): strike/underline ink
    // scoped to spans, kinds per the Block field's comment.
    public List<(int start, int end, int kind, Color? c)> rawDecorRuns = null!;
    public Stack<(int depth, int start, int kind, Color? c)> openDecorRuns = null!;
    // Open block elements' class attributes (pushed per BlockTags open) — lets a
    // descendant rule like ".blueh4 h4 { border-bottom: … }" resolve its ancestor.
    public List<string> divClassStack = null!;
    /// <summary>The open blocks' tags, one per divClassStack entry, for descendant-rule matching.</summary>
    public List<string> divTagStack = null!;
    /// <summary>The open blocks' id attributes, one per divClassStack entry ("" = none) — an
    /// id-scoped descendant rule ("#right_column H2 { … }") reaches its blocks through it.</summary>
    public List<string> divIdStack = null!;
    /// <summary>The open blocks' inline style attributes, one per divClassStack entry ("" = none) -
    /// the box chrome an absolutely positioned descendant is seated against.</summary>
    public List<string> divStyleStack = null!;
    /// <summary>The sheet's descendant (chain) rules, read by the calibrated flow's typography.</summary>
    public List<CssChainRule>? chainRules;
    // Set only around the flush that CLOSES a block element (see the </p> path):
    // an element's declared height reserves space for the whole element, so it
    // belongs to the line that closes it, not to a <br> inside it.
    public bool closingElement;
    // Border-only declared box (browser-UA flow): a block element with inline
    // width+height+border and no background strokes its declared box while its
    // content flows INSIDE it — the box travels to the first block that flushes
    // within the element (usually a bare wrapper child's text), and to a
    // text-less spacer at the element's close if nothing flushed.
    public (double w, double h, double bw, Color c, double r)? pendingBorderBox;
    // The enclosing conversion's state, when the caller has one (nested/table-cell
    // fragment parses may not) - only consulted by ApplyAbsolutePositionResolve to
    // publish the document-wide furthest position:absolute overflow.
    public ConvertState? cv;
    // The extraction inputs, captured from the method parameters.
    public IReadOnlyDictionary<string, Dictionary<string, string>>? css;
    public IReadOnlyList<BeforeMarker>? beforeMarkers;
    public IReadOnlyList<Block>? rowBlocks;
    public bool metricLayout;
    public bool uaDefaults;
    public bool browserUa;
    public bool uaBoxes;
    /// <summary>The sheet's element and class rules size and face the blocks (their typography
    /// only) on the calibrated flow, where the UA flow's whole rule application does not run.</summary>
    public bool sheetElementTypography;
    // Word mail: header paragraphs hang their label in a negative text-indent and tab the value; bold is a run.
    public bool wordMail;
    public bool wordExport;
    // Word mail: the typography each sized span replaced, with the text length at its open - a span that
    // closes having held no text (a trailing `<span style=…><o:p></o:p></span>`) styles nothing and puts it back.
    public Stack<(int Depth, int TextLen, double Fs, string? Fam, Color? Fore, bool Bold, bool Ital)> wmSpanSaves = new();
    /// <summary>The body rule's drawable face on the sheet-typography flow (the blocks' fallback face).</summary>
    public string? sheetBodyFace;
    public double bodyFontSize;
    public double bodyLineBoxPt;
    public bool bandDialect;
    public bool formDialect;
    public bool brBlankLines;
    /// <summary>A UA-grid document: its sheet rules box the headings the browser way.</summary>
    public bool uaGridBlocks;
    public bool uaBlockRhythm;
    // UA flow: a closing block tag leaves its box's UA margin as a gap for a margin-less follower.
    public bool uaClosingGaps;
    // UA fieldset boxes: the legend keeps the inherited font and stands its 0.35em gap.
    public bool uaFieldset;
    public double closingGapPt;
    public bool controlBoxes;
    public bool inlineEmphasisRuns;
    public bool articleRhythm;
    public bool bodyBoxRhythm;
    public bool containerBoxIndents;
    public bool coverStyles;
    public bool inlineBlockCols;
    public bool absSpanLedger;
    public bool spanClassTypography;
    public bool fieldsetBoxes;
    public bool uaPMargins;
    public bool msoParagraphs;
    public bool html5UaHeadings;
    public bool spanPtTypography;
    public bool divBandBg;
    public bool dwFlow;
    public bool floatFlow;
    public Converters.HtmlToPdfConverter.BlockStyle parent = null!;
    public Converters.HtmlToPdfConverter.BlockStyle style = null!;
    public string tag = null!;
    // Inline tags: mutate the top-of-stack style for <b>/<i>/<strong>/<em>.
    // <span style="font-size:..."> also adjusts size for the inner run.
    // Metric flow: MSHTML-saved documents write UPPERCASE tags, and bold drives
    // the metric wrap width — match case-insensitively there. Legacy keeps the
    // historical ordinal match so no existing conversion changes face.
    public string tagCmp = null!;
    public string? src;
    public bool imgHidden;
    // Leading inline whitespace before an image (e.g. "&nbsp;&nbsp; <img>")
    // shares the image's line box in a browser — it is not a line of its
    // own. Keep its horizontal advance as the image's indent, but drop the
    // run so it doesn't reserve a phantom text line above the image (which
    // would push the image down a line).
    public double imgIndentPt;
    public double iw;
    public double ih;
    // position:absolute + left/top on the image (its class rules or its own inline
    // style): the image seats at these offsets, in points, from the page's content
    // origin and leaves the flow entirely (the cursor never moves for it).
    public bool imgAbsPos;
    public double imgAbsLeftPt;
    public double imgAbsTopPt;
    // the declared height of the fixed-size container a full-bleed page image fills, in points
    public double imgPageBandPt;
    public string? alt;
    // CSS vertical padding on the image (style="padding:28px 0 14px").
    public double padT;
    public double padB;
    // CSS transform: rotate(Ndeg) — from the img's inline style or a class
    // rule. Only the UNPREFIXED property qualifies (a vendor-mangled
    // "-webkit - transform" parses under a different property name and a
    // real vendor prefix would shadow the standard one anyway).
    public double imgRotDeg;
    public string? rotSrc;
    // An inline percentage max-width caps the drawn box at that
    // share of the content width and keeps the sheet from widening.
    public double imgMaxWFrac;
    // An image floats by its OWN style attribute as often as by its
    // container's - `<img style="float:left">` is the common form - so
    // read both and take either. Only the container was consulted before,
    // which left such an image in the flow and stacked it.
    public bool imgFloatLeft;
    public bool imgFloatRight;
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
    public double? imgGutterPt;
    public Converters.HtmlToPdfConverter.BlockStyle popped = null!;
    // A closing block element leaves its own bottom margin as real
    // space below its last line — the in-page fragment flow reads
    // authored markup, where that margin IS the vertical rhythm.
    public double closingMarginBottom;
}
}
