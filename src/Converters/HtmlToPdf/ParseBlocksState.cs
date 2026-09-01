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
    public int titleColSpanDepth;
    public double pendingColIndent;
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
    // Redline decoration runs (see Block.DecorRuns): strike/underline ink
    // scoped to spans, kinds per the Block field's comment.
    public List<(int start, int end, int kind, Color? c)> rawDecorRuns = null!;
    public Stack<(int depth, int start, int kind, Color? c)> openDecorRuns = null!;
    // Open block elements' class attributes (pushed per BlockTags open) — lets a
    // descendant rule like ".blueh4 h4 { border-bottom: … }" resolve its ancestor.
    public List<string> divClassStack = null!;
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
}
}
