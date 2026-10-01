namespace Aspose.Pdf.Text;

/// <summary>
/// Represents text formatting options for a <see cref="TextParagraph"/>.
/// </summary>
public sealed class TextFormattingOptions
{
    /// <summary>
    /// Word wrap mode that controls how text wraps within a paragraph rectangle.
    /// </summary>
    public enum WordWrapMode
    {
        /// <summary>No wrapping — text may overflow the rectangle.</summary>
        NoWrap,

        /// <summary>Wrap at word boundaries.</summary>
        ByWords,

        /// <summary>Allow discretionary hyphenation when wrapping.</summary>
        DiscretionaryHyphenation,

        /// <summary>Wrap mode not yet resolved — defer to surrounding context.</summary>
        Undefined,
    }

    /// <summary>How line spacing is interpreted (font-derived vs full glyph extent).</summary>
    public enum LineSpacingMode
    {
        FontSize = 0,
        FullSize = 1,

        /// <summary>The declared spacing makes a LINE BOX: the line is
        /// <c>FontSize + LineSpacing</c> tall and the baseline sits half the box's
        /// surplus leading plus the face's ascent below the box top, so the leading
        /// opens EVENLY above and below the text rather than all of it above.
        ///
        /// This is the line box CSS and the PDF text model both describe, and the
        /// flow already places one for the in-page HTML renderer; this value is
        /// what lets any caller ask for it. <see cref="FontSize"/> and
        /// <see cref="FullSize"/> are untouched, so nothing that does not name this
        /// value changes.
        ///
        /// A caller whose own typography defines the extents declares them with
        /// <see cref="Text.TextState.LineBoxAscentEm"/> and
        /// <see cref="Text.TextState.LineBoxDescentEm"/>; left unset, the face's
        /// own are used.</summary>
        LineBox = 2,
    }

    /// <summary>Creates options with <c>WrapMode</c> <c>Undefined</c> (wrap by width) and <c>LineSpacing</c> <c>FontSize</c>.</summary>
    public TextFormattingOptions() { }

    /// <summary>Construct with an explicit wrap mode.</summary>
    public TextFormattingOptions(WordWrapMode wrapMode) { WrapMode = wrapMode; }

    /// <summary>
    /// Gets or sets the word wrap mode for the paragraph.
    /// Default is <see cref="WordWrapMode.Undefined"/> (the ctor sets the
    /// backing field to Undefined). The
    /// flow layout treats Undefined as "wrap by width" -- callers that want
    /// the no-wrap behaviour have to opt in explicitly.
    /// </summary>
    public WordWrapMode WrapMode { get; set; } = WordWrapMode.Undefined;

    /// <summary>Line-spacing interpretation mode. Default is <see cref="LineSpacingMode.FontSize"/>.</summary>
    public LineSpacingMode LineSpacing { get; set; } = LineSpacingMode.FontSize;

    /// <summary>Line-spacing value in points (internal extension — the public API exposes the mode only).</summary>
    public double LineSpacingPoints { get; set; }

    /// <summary>Indent applied to the first line of the paragraph (points).</summary>
    public float FirstLineIndent { get; set; }

    /// <summary>Whether the space that separated a finished line from the word
    /// that overflowed it HANGS past the measure instead of being dropped.
    ///
    /// Both readings are right for their own typography, which is what makes this
    /// a policy rather than a defect. By default the wrap keeps that space only
    /// while it still fits, so a line never measures wider than its box. A
    /// hanging space is what CSS specifies for a soft wrap -- the space stays
    /// with the line and simply overhangs -- and a caller matching such a system
    /// asks for it here. It is a TEXT difference, not a visible one: the space
    /// renders past the margin where nothing shows.</summary>
    public bool HangingBreakSpace { get; set; }

    /// <summary>The paragraph's lines are not clipped to its band: a glyph that overhangs the
    /// measure (a serif's left bearing, a descender past the last line) is drawn whole, as a
    /// layout that never clips text draws it. Off, the band cuts what runs past it.</summary>
    public bool UnclippedLines { get; set; }

    /// <summary>The fragment's segments flow as RUNS of one paragraph: the text wraps
    /// at spaces across them (a word split between two segments stays one word),
    /// every line is drawn run by run in each segment's own face, size and colour,
    /// and a line is as tall as the tallest line box among the runs on it, seated
    /// on the largest run's ascent -- a small run beside a large one shares the
    /// large one's baseline. Off by default: segments of differing styles then draw
    /// as one unwrapped line when they fit, and take the fixed-position writer when
    /// they do not.</summary>
    public bool SegmentsFlowAsRuns { get; set; }

    /// <summary>For segments that flow as runs (<see cref="SegmentsFlowAsRuns"/>) in a
    /// declared line box: a line is this multiple of its HIGHEST ascent plus its DEEPEST
    /// descent tall, whichever runs reach them, and seats its baseline half the surplus
    /// under its top -- the rule of a typesetter whose leading multiplies the line's own
    /// extent, so runs in faces of different proportions box together. Unset, a line
    /// takes the tallest line box among its runs.</summary>
    public double? RunLineBoxMultiplier { get; set; }

    /// <summary>For segments that flow as runs (<see cref="SegmentsFlowAsRuns"/>): the stops a tab
    /// segment (<see cref="TextSegment.IsTab"/>) goes to, measured from where each line starts.
    /// A tab reaches the first stop PAST where its line has got to. What follows a
    /// <see cref="TabAlignmentType.Left"/> stop starts at it; what follows any other, up to the next
    /// tab or the line's end, is laid as though the tab took no room and then moved so that it
    /// ends at a <see cref="TabAlignmentType.Right"/> stop, is centred on a
    /// <see cref="TabAlignmentType.Center"/> one, or has its first
    /// <see cref="TabStop.AnchorCharacter"/> on a <see cref="TabAlignmentType.Decimal"/> one (ending
    /// there without it) - never moved back past the tab, and pulled back as far as it must be to
    /// end within the line. A stop past the line's width is still gone to; what follows then
    /// starts the next line.</summary>
    public TabStops? RunTabStops { get; set; }

    /// <summary>For segments that flow as runs: how far apart the default tab positions stand for
    /// a tab with no stop past it - the next multiple of this from where its line starts, and
    /// never past the line's end. Unset, every 36 points (half an inch).</summary>
    public double? RunTabInterval { get; set; }

    /// <summary>The fewest lines of the paragraph that may stand at the bottom of a page when it
    /// breaks there; with fewer room left the whole paragraph starts the next page, unless it
    /// already stands at the top of one. 0 (the default) keeps any number.</summary>
    public int MinOrphanLines { get; set; }

    /// <summary>The fewest lines of the paragraph that may be carried to the next page when it
    /// breaks; with fewer, lines are moved over to make them up - at most
    /// <see cref="MaxWidowLinesMoved"/>, and never so many that the lines left behind fall under
    /// <see cref="MinOrphanLines"/> (or one). 0 (the default) carries any number.</summary>
    public int MinWidowLines { get; set; }

    /// <summary>The most lines <see cref="MinWidowLines"/> may move over to the next page.</summary>
    public int MaxWidowLinesMoved { get; set; }

    /// <summary>When too few lines would be carried over and moving lines cannot make them up
    /// (<see cref="MinWidowLines"/>), the whole paragraph starts the next page instead of breaking
    /// as it would; off (the default), it breaks as it would.</summary>
    public bool MoveWholeOnWidowViolation { get; set; }

    /// <summary>The geometry of the underline drawn when <see cref="TextState.IsUnderline"/>
    /// is set; null (the default) keeps the flow's own rule.</summary>
    public TextDecorationStyle? UnderlineStyle { get; set; }

    /// <summary>The geometry of the strike-through drawn when <see cref="TextState.IsStrikeOut"/>
    /// is set; null (the default) keeps the flow's own rule.</summary>
    public TextDecorationStyle? StrikeoutStyle { get; set; }

    /// <summary>A horizontal shear of the glyphs -- the text matrix's <c>c</c>
    /// component, the tangent of the angle the verticals lean by. Zero (the
    /// default) is upright. A shear is text state alone: the run advances by its
    /// glyphs, nothing else moves.</summary>
    public double Skew { get; set; }

    /// <summary>A vertical shear of the glyphs -- the text matrix's <c>b</c>
    /// component, the tangent of the angle the baseline climbs by along the run.
    /// Zero (the default) is level. Text state alone, like <see cref="Skew"/>.</summary>
    public double Slope { get; set; }

    /// <summary>A synthetic slant: the glyphs are sheared by this tangent (added
    /// to <see cref="Skew"/>), the way an italic is simulated from an upright
    /// face, and the run OCCUPIES <c>lean × size</c> more than its glyphs advance
    /// -- the top of its em box leans that far past the last glyph -- which the
    /// run after it, the wrap and the alignment take in; a rule under it reaches
    /// half of it. Zero (the default) is none.</summary>
    public double SyntheticItalicLean { get; set; }

    /// <summary>A synthetic weight: the glyphs are filled AND stroked with a pen
    /// this wide (<c>2 Tr</c>, <c>w</c>), the way a bold is simulated from a
    /// regular face, and the run OCCUPIES the pen's width more than its glyphs
    /// advance -- the stroke's overhang -- which the run after it, the wrap, the
    /// alignment and a rule under it all take in. Zero (the default) is none.
    /// An explicit <see cref="TextState.RenderingMode"/> is text state alone and
    /// wins over this when set.</summary>
    public double SyntheticBoldPen { get; set; }

    /// <summary>The opacity of everything the fragment paints -- fill AND stroke,
    /// <c>ca</c> and <c>CA</c> of one graphics state -- as a real in 0..1. Null
    /// (the default) is opaque. A colour's own alpha is a byte; this keeps the
    /// exact value a caller gave.</summary>
    public double? Opacity { get; set; }

    /// <summary>The fill opacity (<c>ca</c>) of the text alone, as a real in 0..1,
    /// used instead of the foreground colour's byte alpha when set.</summary>
    public double? FillOpacity { get; set; }

    /// <summary>The fill opacity (<c>ca</c>) of the background, as a real in
    /// 0..1, used instead of the background colour's byte alpha when set.</summary>
    public double? BackgroundOpacity { get; set; }

    /// <summary>How a line aligned <see cref="HorizontalAlignment.Justify"/> is
    /// stretched to the measure: by character AND word spacing (<c>Tc</c>, <c>Tw</c>)
    /// rather than by moving its words. The slack <c>S</c> of a line of <c>n</c>
    /// glyphs and <c>s</c> spaces is shared as <c>Tw = r·B</c>, <c>Tc = (1−r)·B</c>
    /// with <c>B = S / ((1−r)(n−1) + r·s)</c>: this value is <c>r</c>, the word
    /// share (1 = words only, 0 = glyphs only). Null (the default) keeps the
    /// flow's own justification. The last line is left as it is unless
    /// <see cref="JustifyLastLine"/> asks for it too.</summary>
    public double? JustifySpacingRatio { get; set; }

    /// <summary>Whether the paragraph's LAST line is stretched too (the way a
    /// "justify all" alignment does). Off by default.</summary>
    public bool JustifyLastLine { get; set; }

    /// <summary>Whether the fragment's top margin is applied AGAIN where its lines
    /// continue on a new page or column. Off (the default) the continuation
    /// starts flush at the region's top, the way a fragmented box does; a caller
    /// whose typesetter gives the continued part its own margin asks for it here.
    /// Each part then keeps its margins: the part left behind keeps the bottom
    /// margin under it, which a <see cref="BoxBlock"/> around the fragment closes
    /// under.</summary>
    public bool TopMarginAfterBreak { get; set; }

    /// <summary>Whether the fragment's LAST line must leave room for its bottom margin
    /// above the region's bottom edge: when it would not, that line goes on to the
    /// next page or column (a one-line fragment moves whole). The lines before it
    /// still fill the region to its edge - only the line the margin follows needs
    /// the margin's room. Off (the default) the last line may end on the edge and
    /// the margin runs past it.</summary>
    public bool BottomMarginInsideRegion { get; set; }

    /// <summary>Where a block background (<see cref="BlockBackground"/>) ENDS on the
    /// page a split paragraph leaves: this many em below its last baseline there,
    /// instead of at that line's box bottom. Zero (the default) keeps the line box.
    /// A caller whose typesetter closes a broken box at the face's descent (its
    /// leading below the line not painted) gives that descent here.</summary>
    public double BreakBoxDescentEm { get; set; }

    /// <summary>Whether a line fits the region when its text reaches no lower than the
    /// region's bottom (or the room its paragraph keeps for its bottom margin): the half
    /// of the surplus leading below a line's descent may fall past it. Off (the default)
    /// a line needs its whole line box. Lines of differing heights always need theirs.</summary>
    public bool LineFitsToDescent { get; set; }

    /// <summary>The decimals each wrapped line's origin is written with: every line lands
    /// on its own origin rounded to them, halves away from zero, rather than one leading
    /// below the last. A caller whose own writer states positions to a fixed precision
    /// gives it here so the glyphs land where that writer's would. Null (the default)
    /// writes the origins as laid out. Rotated lines keep their exact origins.</summary>
    public int? LinePositionDecimals { get; set; }

    /// <summary>How far a block background (<see cref="BlockBackground"/>) reaches
    /// OUTSIDE the block's box on each side, painting only -- nothing else moves.
    /// A negative side keeps the background that far INSIDE the box instead (off a
    /// border band or the padding). Null (the default) is the box itself.</summary>
    public MarginInfo? BlockBackgroundOutset { get; set; }

    /// <summary>The corners of a RUN's background (a segment flowing as a run, see
    /// <see cref="SegmentsFlowAsRuns"/>, with a <see cref="TextState.BackgroundColor"/>):
    /// the box behind the run's text on each line it reaches - its advance wide, its line
    /// box's ascent and descent tall - is rounded at these corners, each line's piece a
    /// whole rounded box. Null (the default) leaves it square.</summary>
    public CornerRadii? BackgroundCornerRadii { get; set; }

    /// <summary>A border around a RUN's box (a segment flowing as a run, see
    /// <see cref="SegmentsFlowAsRuns"/>): the box behind its text - its advance wide, its
    /// line box's ascent and descent tall - grown by the border's bands, painted as a
    /// block's border is (rounded with <see cref="BackgroundCornerRadii"/>), a background
    /// filling it out to the bands' outer edge. The run occupies both side bands and its
    /// text moves in by the left one; each line's piece is a whole bordered box. The line's
    /// height is the caller's: a caller whose leading follows the grown box states it in
    /// the run's line spacing. Null (the default) draws none.</summary>
    public BorderInfo? RunBorder { get; set; }

    /// <summary>Whether a background colour covers the paragraph's BLOCK box --
    /// one rectangle over its line boxes, the full content width -- instead of
    /// highlighting each line's own ink.
    ///
    /// The two are different things that happen to look alike on a single full
    /// line. A run highlight follows the text: it is as wide as the ink and as
    /// tall as the em box, so a short line gets a short patch. A block
    /// background is the box of a block element: full width whatever the line
    /// holds, and exactly as tall as the line boxes it contains, so it grows
    /// with a declared leading where a highlight does not. The engine had only
    /// the first.</summary>
    public bool BlockBackground { get; set; }

    /// <summary>Whether a caller-positioned fragment belongs to the page the FLOW
    /// HAS REACHED rather than the page it was added to.
    ///
    /// The two are the same until the flow moves on. After that they are different
    /// requests and only the caller knows which it means: content that names its
    /// page wants that page, and content that means "here, beside the text I have
    /// just added" wants wherever the flow now is. Without this the second lands on
    /// the flow's start page, stranded on page one while the text it belonged
    /// beside has moved. It does not consume flow space either way.</summary>
    public bool SeatOnFlowPage { get; set; }

    /// <summary>Indent applied to lines after the first (points).</summary>
    public float SubsequentLinesIndent { get; set; }

    /// <summary>Symbol used at line breaks when hyphenation is in effect (default "-").</summary>
    public string HyphenSymbol { get; set; } = "-";
}
