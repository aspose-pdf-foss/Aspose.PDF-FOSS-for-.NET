using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of <c>BuildTableFromHtml</c>: the column
/// and width model the parse loop fills and the width solver consumes. One instance
/// per invocation; never shared.</summary>
private sealed class TableParseState
{
    // <strong>/<b> nesting; a line is Bold only when EVERY text run on it arrived
    // while bold was active (mixed lines keep the regular face).
    public int boldDepth = 0;
    /// <summary>Word mail: cell runs face from their paragraph class, bold runs split, td borders read per side.</summary>
    public bool wordMailCells = false;
    /// <summary>Word mail: the open paragraph's `line-height: N%`, and the percent each pushed line was paced at.</summary>
    public double curLineHeightPct = 0;
    public Dictionary<int, double>? lineHeightPctByIdx = null;
    // `font-style: italic` nesting (style attributes on the run tags or the td
    // itself — the form-grid band titles); same all-runs rule as bold.
    public int italicDepth = 0;
    // <u> tracking: lines whose text sat inside a <u> element stroke an
    // underline at draw (over-declared grid dialect).
    public int uDepth = 0;
    // <pre> nesting: newlines are HARD line breaks and each source line is
    // one unbreakable line box. The longest line is unwrappable content —
    // it grows the grid (and the sheet) past every declared width, the way
    // the expected render lays the case-comment box out (probed).
    public int preDepth = 0;
    // <table> nesting depth: 1 inside the outer table, >1 inside a table
    // nested in a cell (whose structure tags must not drive the outer grid).
    public int tableDepth = 0;
    public int hiddenSubDepth = 0;
    public string? hiddenSubTag = null;
    public int dwSelectDepth = 0;
    public bool dwTextareaOpen = false;
    public StringBuilder dwOptBuf = new StringBuilder();
    public bool dwOptSelected = false;
    public bool dwSawSelected = false;
    public StringBuilder dwTaBuf = new StringBuilder();
    public double dwTaW = 0;
    public double dwTaH = 0;
    public string? dwSelectedOpt = null;
    public string? dwFirstOpt = null;
    // Colour declared by an enclosing run (`<p style="color:#004178">`): it belongs
    // to the LINE, not the cell — a coloured heading and a black paragraph are two
    // <p>s in one cell.
    public Color? curColor = null;
    public double curFontPt = 0;
    public string? curFamily = null;
    public Color? lineColor = null;
    // A webridge `<span class="htmlPage">` is the source generator's block
    // container: a run it opens starts on a FRESH line (a
    // trailing `&nbsp;` wrapper sets on its own line box between a control group
    // and its <BR><BR>). Deferred until the span shows VISIBLE content — the
    // ubiquitous EMPTY wrappers are inert — and cancelled by any structural tag.
    public bool htmlPageBreakPending = false;
    public double liStandingIndentPt = 0;
    public int anchorBoldDepth = 0;      // anchors weighted by the sheet's a rule, open
    // UA-boxed cells lay their blocks out on the browser's collapsed margins: the margin a
    // closed or opened block leaves pending, taken by the next line that is pushed.
    public bool uaCellBoxes = false;
    // ...whose grid declared its cellpadding ATTRIBUTE (so no UA 1px pad pair rides its cells).
    public bool cellPadAttrDeclared = false;
    // ...or whose document sheet zeroes every cell's padding (`td { padding: 0 }`).
    public bool sheetTdPadZero = false;
    /// <summary>The sheet declares its cells' own box through a descendant cell rule
    /// (see <see cref="SheetDeclaresCellBox"/>): the grids measure on that box.</summary>
    public bool sheetTdBoxRule = false;
    /// <summary>The first installed face the SHEET names for the document body, on the declared-cell-box
    /// dialect only; null otherwise. A quirks grid draws and pitches in it.</summary>
    public string? sheetFace = null;
    /// <summary>This grid lays out at its own min-content (see BlockRowGridAttr).</summary>
    public bool minContentGrid = false;
    /// <summary>The cell's own style attribute states this side's padding - a ZERO declaration is a
    /// declaration, and it beats the sheet's cell rule.</summary>
    public bool cellPadLeftInline = false;
    public bool cellPadRightInline = false;
    public double uaPendingMarginPt = 0;
    // ...the bottom margins of the UA blocks still open, taken when each closes, and the sheets the
    // block margins resolve against.
    public List<(string Tag, double Pt)> uaBlockBottoms = new List<(string Tag, double Pt)>();
    public IReadOnlyDictionary<string, Dictionary<string, string>>? uaSheet;
    public IReadOnlyDictionary<string, Dictionary<string, string>>? uaDocSheet;
    // ...the style-stack depths of the runs declaring text-transform: uppercase, and the cell's own.
    public List<int> uaUpperDepths = new List<int>();
    // ...a list item's marker (its own font, drawn hanging before the item's first line) and the
    // slack a one-line item leaves under itself when its marker's box is taller than its line.
    public (string Text, double Fs, string? Family, double BoxPt)? lineMarker = null;
    public Dictionary<int, (string Text, double Fs, string? Family, double BoxPt)>? lineMarkerByIdx = null;
    // ...the collapsed rule under a row (the tr's or the td's bottom border) drawn as a band
    // line at the cell's foot, and the band the previous row already drew.
    public Dictionary<int, (double W, Color C)>? uaRuleBands = null;
    public double rowRuleBelowPt = 0;
    public Color? rowRuleColor = null;
    public double cellRuleBelowPt = 0;
    public double cellRuleAbovePt = 0;
    public Color? cellRuleAboveColor = null;
    public Color? cellRuleColor = null;
    public double uaPrevRowRuleBelowPt = 0;
    public bool cellUpper = false;
    // ...the largest run size the current line holds (its box is the largest run's).
    public double lineMaxRunPt = 0;
    // ...the classes the table itself carries (the sheet scopes cell rules by them).
    public string[] uaTableClasses = Array.Empty<string>();
    // ...the background the current row declares (class rule or style), painted by its cells.
    public Color? uaRowBg = null;
    // ...and the alignment an open UA block (a centred heading) gives the lines it pushes.
    public HorizontalAlignment? uaLineAlign = null;
    public Dictionary<int, HorizontalAlignment>? lineAlignByIdx = null;
    // ...and the <hr> whose rule the next pushed line draws across the cell.
    public bool uaHrRulePending = false;
    public HashSet<int>? hrRuleLines = null;

    public double uaBaseFontPt = 0;      // the em base when no run style is active
    // A pre/pre-wrap box was just opened: the next text token still carries the
    // newline that followed the tag, and that newline is content.
    public bool preWrapPending = false;
    // Inline style context inside the current cell: <p>/<span>/<font> tags carrying a
    // font-size / font-family set the style of the lines they enclose. A line binds the
    // style active when its first text arrived (or when an explicit <br> created it),
    // so a close tag after the text doesn't retroactively restyle the line.
    // Positioned: the run opened a `position: absolute` (or `fixed`) wrapper, whose content is out of
    // flow - a picture inside one draws at its own seat and lends the cell none of its width.
    public List<(string Tag, double PrevPt, string? PrevFamily, bool BoldBump, Color? PrevColor, bool ItalicBump, bool Positioned)> styleStack = new List<(string Tag, double PrevPt, string? PrevFamily, bool BoldBump, Color? PrevColor, bool ItalicBump, bool Positioned)>();
    // Open <ol>/<ul> nesting in the cell walk: (ordered?, items seen). An <li>
    // breaks the line, prepends its marker ("1." / "•") and hangs it left of the
    // UA list indent; every line inside the item seats ON the indent.
    public List<(bool Ordered, int Count)> listNesting = new List<(bool Ordered, int Count)>();
    public Cell? cell = null;
    public HorizontalAlignment cellAlign = HorizontalAlignment.Left;
    public bool alignSet = false;
    // the cell's own weight and size, from its style or one of its classes
    public bool cellBold = false;
    // The weight the TABLE itself declares, inline or through one of its classes.
    // font-weight inherits, so a bold table hands every cell that starts out bold -
    // a cell rule of its own still wins on top.
    public bool tableBold = false;
    // The caller's document base face — the grid inherits the page's own `body { }`
    // family the same way it inherits `defaultCellFontPt`. Any rule the table or its
    // cells declare still wins below.
    public Text.Font? measureFont;
    // Form-grid: a line whose bold state TOGGLES mid-run (the owner band's
    // 'Owner Team: <b>bv Designers</b>') carries per-run segments, keyed by
    // the line's index at push — the render draws each in its own face.
    public Dictionary<int, List<(string Text, bool Bold)>>? lineRunsByIdx = null;
    public Dictionary<int, List<(int Kind, Color? C)>>? lineDecorsByIdx = null;
    public Dictionary<int, List<(int S, int L, Color C)>>? lineColorRunsByIdx = null;
    // Inline <a href> tracking: the open anchor's start offset in `line`, and the
    // anchors already closed on the current line (inner text + URL).
    public (int Start, string Url)? openAnchor = null;
    // A rounded-capsule div (bg + border-radius) wrapping the NEXT lifted
    // table: (fill, corner radius, horizontal pad, vertical pad).
    public (Color Fill, double RadiusPt, double PadHPt, double PadVPt, double MarginPt)? pendingCapsule = null;
    // Probe measure resolves each run's own face (family + real bold metrics) —
    // the min-content the page-widen matches is computed from the styles the runs
    // actually render with, not the table's base face. Cached per (family, bold).
    public Dictionary<(string fam, bool bold), Text.Font?>? probeFonts = null;
    public string? cellFamily;
    public Color? cellTextColor = null;
    public double cellClassPt = 0.0;
    /// <summary>The family a cell's class `font:` shorthand names, when its metrics are installed
    /// (reset per cell; the table-level cellFamily stays the flow's own face).</summary>
    public string? cellClassFamily;
    /// <summary>The bare min-content the current row's cells have asked for so far (spans included).</summary>
    public double rowMinSum;
    public Color? cellChainColor = null;
    public double cellChainPadTopPt = 0;
    public double cellChainPadBotPt = 0;
    // Per-cell CSS padding (pt) and the widest fixed-width inner <div> (pt) — see
    // the CloseCell measurement: the div box, not its wrapped token, sizes the column.
    // The left half is kept apart because it also INDENTS the drawn text.
    public double cellCssPadPt = 0;
    public double cellFixedDivPt = 0;
    public double cellPadLeftPt = 0;
    // Redline decoration state (kinds per Block.DecorRuns): the active
    // span-scoped decorations, the union seen on the line being built,
    // and the per-line snapshots consumed at fragment build.
    public List<(int Depth, int Kind, Color? C)>? cellDecorActive = null;
    // …and the FIRST paragraph's margin-top: the redline cells bill it as
    // cell padding (the address rows separate on it).
    public double cellFirstPMarginTopPt = 0;
    // Form-grid: a td styling its OWN font-size carries that size's strut
    // (the Description band's 10pt td rows at the 16px box, not the ambient),
    // and the strut's own baseline drop follows that size too.
    public double cellFgStrutPt = 0.0;
    public double cellFgStrutFontPt = 0.0;
    // Widest image the cell draws (pt). An image is replaced content: it never
    // wraps, so its box is the cell's min- AND max-content width. Email templates
    // build their whole column grid out of `<img width="15" height="1">` spacer
    // GIFs, and a text-only measure leaves every one of those columns empty.
    public double cellImgWidthPt = 0;
    // The widest UA text control the cell holds (pt): its intrinsic box plus the advance its
    // chrome adds - the UA grid seats the control at that width whatever the column was asked,
    // so the probe floors the column with it (see OpenInputControl).
    public double cellControlBoxPt = 0;
    // Radio options collected for the CURRENT cell, one per inline marker char
    // appended to its line text, in document order; attached to the cell's
    // fragments at CloseCell (each fragment takes as many options as it holds
    // markers).
    public List<Aspose.Pdf.Forms.RadioButtonOptionField>? cellInlineOptions = null;
    // DataWorks form grid control state (see dwFormCells).
    public List<(double W, double H, string Value, bool Mono, double Lift)>? cellInputBoxes = null;
    // True when the cell declares an explicit height (attr or style): its box is
    // already fixed, and its internal pitch stays on the legacy model — an
    // embedded document's own line-height cascade lives inside such cells.
    public bool cellOwnHeightDecl = false;
    // The cell's own inline `line-height` (pt) — pitches its text lines.
    public double cellOwnLineHPt = 0;
    // The row's own line-height (`<tr style="line-height: 30px">`) a UA-boxed cell's lines inherit, in points.
    public double rowLineHPt = 0;
    // The open cell's class names (a nested grid's ancestor chain runs through them).
    public string[]? uaCellClasses;
    // The body line-height factor a UA-boxed cell's lines stand on (see TableStyleConfig.uaLineFactor).
    public double uaLineFactor = 0;
    public bool uaSheetGrid = false;
    // pt-styled fragment: the cell paragraphs' margin-RIGHT — an inset on
    // the wrap box only (the column box keeps width + pads).
    public double cellPMarginRightPt = 0;
    // A <br> closed the cell's last line and no real text followed: the
    // break still opens a line box, which the row must be tall enough for.
    public bool cellPendingBrBlank = false;
    // The previous paragraph's margin-bottom in this cell (pt) — CSS-collapsed
    // into the next paragraph's gap.
    public double cellPrevPBottomPt = 0;
    // ROWSPAN occupancy for the widen probe's column mapping: a row-spanning cell
    // keeps its column(s) occupied in the following rows, so their cells shift
    // right the way the HTML grid algorithm places them. (Probe-only: the render
    // grid has its own rowspan handling, and the calibrated legacy mapping is
    // left untouched.)
    public int cellRowSpan = 1;
    // Declared-ZERO bottom padding on a cell (overrides the table's
    // cellpadding — the filing shell's `padding-bottom: 0px` host cells;
    // the top half of the pair stays, matching the expected row seats).
    public bool cellVPadZeroBot = false;
    // Legacy WIDTH="N%" cell attributes (the classic empty sizing row of filing
    // HTML) declare the column split as fractions of the table width. Tracked
    // per column from single-span cells; honoured only when EVERY column got one.
    public double cellWidthPct = 0;
    // Explicit per-column widths (points): captured from the first row whose cells are all
    // single-span and each carry an explicit CSS width (inline `width:Npx` or a class rule),
    // so a label : value table keeps its narrow ":" column instead of equal thirds.
    public double cellWidthPt = 0;
    public List<(int LineIdx, ChainBoxRun Run, string Prefix, string Text)>? cellBoxSegs = null;
    // Chain-selector state for the current cell: its own element node, the
    // div/span elements open inside it, and the vertical padding / text colour
    // a matched rule contributed (consumed at CloseCell).
    public CssElem? chainTdElem = null;
    // While a TrafficLight subtree is open, its text is the circle's letter,
    // not line content.
    public CssElem? chainTrafficElem = null;
    public ChainBoxRun? chainTrafficRun = null;
    public List<CssElem>? chainOpenElems;
    // Open inline-box runs (title plates / status pills) and the per-line
    // segments they resolve to (consumed at CloseCell into InlineBoxDecorations).
    public List<ChainBoxRun>? chainBoxOpen = null;
    public StringBuilder line = new StringBuilder();
    public List<(string Text, double FontPt, string? Family, bool Keep, bool JoinNext, List<(string Text, string Url)>? Anchors, bool Bold, double MarginTopPt, double MarginLeftPt, Color? Color, bool Italic)> lines = new List<(string Text, double FontPt, string? Family, bool Keep, bool JoinNext, List<(string Text, string Url)>? Anchors, bool Bold, double MarginTopPt, double MarginLeftPt, Color? Color, bool Italic)>();
    public bool lineAllBold = true;
    public bool lineAllItalic = true;
    public List<(string Text, string Url)>? lineAnchors = null;
    public List<(int S, int L, Color C)>? lineColorRuns = null;
    public List<(int Kind, Color? C)>? lineDecorUnion = null;
    public double lineFontPt = 0;
    public string? lineFamily = null;
    public bool lineStyleSet = false;
    public bool lineHadText = false;
    public bool lineHadU = false;
    public double lineMarginLeft = 0;
    // margin-top / margin-left of the <p> that opened the current line (band
    // dialect: they become the fragment's margins so a spacer gap above — and
    // the indent beside — a styled cell paragraph survive into the generator's
    // cell layout).
    public double lineMarginTop = 0;
    /// <summary>The page sheet's `p { margin }` top value: every paragraph in a cell opens that far below the line before it, and the paragraphs stay separate lines.</summary>
    public double sheetPMarginTopPt = 0;
    /// <summary>The reset-sheet grid dialect: a page whose sheet zeroes every margin (`* { margin: 0 }`) and boxes its cells by rule (`table td { border }`) - its cell rules, header rules, paragraph margins and one-sided picture boxes all read.</summary>
    public bool resetSheetGrid = false;
    /// <summary>The UA serif flow's control grid (checkbox/radio cells): an empty cell keeps one line box, as the browser's does.</summary>
    public bool uaControlGrid;
    /// <summary>The UA control grid's checkbox fields of the open cell, in document order (they join the cell as paragraphs at its close).</summary>
    public List<Aspose.Pdf.Forms.CheckboxField>? cellCheckboxes;
    /// <summary>The page sheet's `th` rule: header cells fill with its background and seat their text by its text-align.</summary>
    public Color? thBackground = null;
    public HorizontalAlignment? thAlign = null;
    public List<(int Pos, bool Bold)>? lineRunMarks = null;
    public HashSet<int>? underlinedLines = null;
    // Line indices (per cell) of blanks pushed by a LONE <br> — see the br case.
    public HashSet<int>? loneBrBlankLines = null;
    public Row? row = null;
    // ALIGN declared on the current <tr> — the fallback for its cells.
    public HorizontalAlignment? rowAlign = null;
    // Row-level VALIGN (`<tr valign="bottom">`): the default vertical seat for
    // every cell in the row that declares none of its own.
    public VerticalAlignment rowVAlign = VerticalAlignment.None;
    // Row-level defaults from the <tr>: its font-size cascades to cells that declare
    // none (so a `<tr style="font-size:1pt">` spacer row measures thin), and the tallest
    // cell HEIGHT="N" in the row floors the row height (an HTML row-height minimum).
    public double rowFontPt = 0;
    /// <summary>The font-size a row GROUP declared inline (<c>&lt;tbody style="font-size: 10px"&gt;</c>):
    /// every row it holds opens at it, the way a row's own declaration cascades to its cells.</summary>
    public double sectionFontPt = 0;
    public double rowMinHeightPt = 0;
    public bool rowAllSingleExplicit = true;
    // Widest per-row sum of DECLARED percent widths, colspan cells included
    // (colPctW skips those) — an attribute row declaring more than 100% is
    // what makes a fixed-layout grid demand a wider sheet than its box.
    public double rowPctSum = 0;
    public double rowPctDeclMax = 0;
    public double rowPxSum = 0;
    public double rowPxAtMax = 0;
    public int rowPxCells = 0;
    public int rowPxCellsAtMax = 0;
    public bool rowMinHeightIsContent = false;
    public List<double> rowWidths = new List<double>();
    public List<(int col, int span, int remaining)> rowspanOcc = new List<(int col, int span, int remaining)>();
    public bool isHeader = false;
    public int colSpan = 1;
    // Leading rows whose cells are all <th> are the table header; count them so they can be
    // repeated at the top of every page the table spans (RepeatingRowsCount).
    public int headerRows = 0;
    public bool countingHeaderRows = true;
    public bool rowHasTd = false;
    public bool rowHasCell = false;
    public HorizontalAlignment? headerAlign = null;
    public BorderInfo? headerBorder = null;
    // Cell images seen AFTER text on the same cell — appended to the cell's
    // paragraphs at CloseCell, after the text lines flush, to keep markup order.
    public List<Image>? pendingCellImgs = null;
    /// <summary>Word mail: the line count each deferred picture was met at, so it draws in markup order.</summary>
    public List<int>? pendingCellImgAt = null;
    // Tables lifted out of the current cell, added when it closes — each with the
    // LINE INDEX it anchored at, so the cell's paragraph order interleaves text
    // and grids the way the markup does (text after a grid stays below it).
    public List<(Table T, int AnchorLine)>? pendingCellTables = null;
    public double pendingCellTablesNatW = 0;
    public double pendingCellTablesPrefW = 0;
    // …and the widest DECLARED box among them: the hard floor the host grid cannot cap below.
    public double pendingCellTablesDeclW = 0;
    // The cell's own `padding:` SHORTHAND, both sides (pt): part of the hard floor a nested declared
    // grid sets on its host, and nothing else (the longhands keep their calibrated consumers).
    public double cellShortPadPt = 0;
    // …and the widest MIN-CONTENT among the nested grids (a grid's own min-content, not its natural):
    // the floor a declared host box cannot cap below.
    public double pendingCellTablesMinW = 0;
    // …and the widest MAX-CONTENT among them: what a UA-boxed host cell wants for a nested grid,
    // whatever box the grid declared (a percent of the host is no intrinsic size).
    public double pendingCellTablesMaxW = 0;
}
}
