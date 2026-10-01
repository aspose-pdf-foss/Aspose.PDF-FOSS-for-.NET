using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of <c>BuildTableFromHtml</c>: the column
/// and width model the parse loop fills and the width solver consumes. One instance
/// per invocation; never shared.</summary>
private sealed class MetricParseState
{
    // Collapsed ATTRIBUTE grid (border=N + style border-collapse:collapse):
    // cell borders share their boundaries — the row pitch is the bare content
    // height, text seats at the cell box edge, and percent halves split the
    // table box beside the pixel-fixed columns (measured:
    // the 50% halves of a 1000px table land 372.75 wide each).
    public bool attrCollapse;
    public int boldDepth;
    public Color borderColor = null!;
    /// <summary>The legacy grid's outer frame: a thick border attribute's width in pt (0 = the cell stroke), and its dashed/dotted style from the tag's border-style.</summary>
    public double frameW;
    public string? frameDash;
    /// <summary>A table's CSS border sides (top, right, bottom, left) from its own inline style, when it has no border attribute: width in pt (0 = none), style keyword and colour.</summary>
    public CssBorderSide[]? sideFrames;
    // border=N ATTRIBUTE mode (legacy HTML tables): real grid borders like the
    // css-bordered mode, but the outer box HUGS the column grid instead of
    // filling the content box, and align=center centres that box on the page
    // (the symmetric UA content frame's middle — measured:
    // a 229.5pt grid at (595−229.5)/2).
    public bool borderHugs;
    // Bordered separate-border mode (the UA-flow edge-to-edge dialect): the
    // sheet's table {border: 1px solid} + td {border} draw real grid borders —
    // outer box, then per-cell boxes inset by the 2px UA border-spacing.
    public bool bordered;
    public MetricCell? cell;
    public int cellBoldChars;
    public int cellPlainChars;
    public bool centerTable;
    public Color collapsedCol = null!;
    // Collapsed CLASS grid (border-collapse:collapse + border longhands on
    // the table's class): light 1px cell borders share row boundaries — the
    // pitch grows one border per row and the grid strokes in the rule's colour.
    public bool collapsedGrid;
    // the sheet zeroes the table's border-spacing: it authors the cell boxes (paddings, borders) precisely
    public bool sheetSpacingZero;
    public double collapsedLineH;
    public int curSection;
    /// <summary>The table's own inline `font-weight: bold`: every cell it holds draws bold.</summary>
    public bool tableBold;
    /// <summary>The table's own inline `padding-left`, in points: box space between its left
    /// frame and its first column (measured: a `padding-left: 10` grid seats its columns 7.5 in).</summary>
    public double tablePadLeftPt;
    /// <summary>The table's own CSS top and bottom padding: band space inside its frame, above the first row and under the last.</summary>
    public double tablePadTopPt, tablePadBottomPt;
    public MetricDivSeg? curSeg;
    // Cell font: the stylesheet's table/td font-size (the metric flow honors the
    // CSS); otherwise the caller's base size (11 pt for the MSHTML metric flow,
    // the UA 16px base for the browser-default flow).
    public double fontSize;
    public int hiddenDepth;
    public string? hiddenTag;
    // The saved-statement idiom (inline border-spacing + per-cell inline
    // font sizes): rows pitch on their own content, not the table strut.
    public bool inlineStatementGrid;
    // table-layout from a table.<class> rule, resolved once the tag is seen.
    public bool layoutFixed;
    public bool leadBold;
    public bool leadSeen;
    // …and the LEAD text's typography (a styled heading span) is captured
    // when its first ink arrives, before the spans close and restore.
    public double? leadFs;
    public string? leadFace;
    public Color? leadFore;
    public int nestDepth;
    // the div boxes open in the current cell: true where the div stated its own box
    public List<bool> divBoxStack = new();
    public double pendingAbsLeftFrac;
    public int pendingNestSpan;
    public double pendingRowH;
    public bool pendingRowHExact;
    public bool pendingRowHAttr;      // the row's own height= attribute (a bordered grid paces on it)
    public List<MetricCell>? row;
    /// <summary>Columns a row-spanning cell from a row above still occupies: (first column,
    /// columns spanned, rows left including the spanning row). Aged at every real row close.</summary>
    public List<(int col, int span, int remaining)> rowspanOcc = new List<(int col, int span, int remaining)>();
    public HorizontalAlignment? rowAlign;
    public Color? rowBg;
    public bool rowBold;
    // tr class skins (the boleto micro-framework): row typography defaults
    // and `.cls td` descendant bags applied to every cell of the row
    public string? rowFace;
    public Color? rowFore;
    public double? rowFs;
    public bool rowFsFromClass;
    /// <summary>The row states its OWN typography inline (a `style` on the `tr` naming a size or
    /// a family): its cells pitch on those boxes, with no table strut under them.</summary>
    public bool rowInlineTypo;
    public bool rowVBottom;
    public bool rowVTop;
    public bool sawTable;
    // Report cells: run-bold accounting — b/strong lives in boldDepth, not
    // on the cell; a segment (or a p-less cell) is bold when ALL its ink is.
    public int segBoldChars;
    public int segPlainChars;
    // a SEGMENT's typography is what its FIRST ink saw — a trailing styled
    // span (the report paragraphs' nbsp tails) cannot restyle it at close
    public double? segFs;
    public string? segFace;
    public Color? segFore;
    public bool segInkSeen;
    public bool segItalic;
    /// <summary>UA cells: the style frames of the blocks open in the current cell, outermost first -
    /// a nested block inherits its parent's typography, alignment and side insets.</summary>
    public List<MetricDivSeg> divStyleStack = new();
    /// <summary>Headings open as bands in a plain UA cell (see OpenUaCellHeadingBand): their closes close a band.</summary>
    public int uaHeadingBands;
    /// <summary>The padding a td rule gives the td cells beyond the table's UA padding (the th cells keep the UA's).</summary>
    public double tdPadExtraPt;
    /// <summary>The cell colour a UA cell's open b/strong tags replaced, restored at their closes.</summary>
    public Stack<Color?> strongSaves = new();
    /// <summary>The table's own class names, for the `.cls td` rules its cells stand in.</summary>
    public List<string> tableClassNames = new();
    public int italicDepth;
    /// <summary>UA cells: the run styles the cell's marks index, and an inline box's left padding
    /// waiting for the next ink to carry it.</summary>
    public List<MetricRunStyle> runStyles = new();
    /// <summary>How many run marks the cell's raw text buffer holds: a position recorded for the
    /// other consumers of that buffer (the bold marks) counts the ink, not the marks.</summary>
    public int textMarkCount;
    public double pendingRunPadLeft;
    public Color? tableBg;
    public bool tableClassFont;
    /// <summary>…and that size is an absolute length (pt/px/cm/mm/in), not a percent or em of the
    /// base: only then does the row strut follow it (a percent class size keeps the base strut).</summary>
    public bool tableClassAbsoluteSize;
    /// <summary>UA block cells (see HtmlDocProfile.uaBlockCells): the paragraph segments carry
    /// their own inline typography, per-line bold and size, and right floats.</summary>
    public bool uaBlockCells;
    /// <summary>UA form cells (see HtmlDocProfile.uaFormCells): text inputs and absolutely
    /// positioned divs are the cell's replaced and out-of-flow boxes.</summary>
    public bool uaFormCells;
    /// <summary>The pt form grid (see HtmlDocProfile.ptFormDoc): columns on the probed pt-form rule.</summary>
    public bool ptFormCells;
    /// <summary>The table's cellpadding (pt), for the pt form's class paddings to replace rather than add to.</summary>
    public double cellPadPt;
    /// <summary>The page margin box's corner (the sheet margin, inside which the body inset
    /// sits): what absolutely positioned content offsets from, the same in every nested grid.</summary>
    public double pageMarginLeft;
    public double pageMarginTop;
    /// <summary>An absolutely positioned div being captured out of the cell's flow, and how
    /// deep inside it the parse is.</summary>
    public MetricAbsText? absCapture;
    public int absDepth;
    /// <summary>A fieldset legend being captured for the cell (UA form cells): its text is the
    /// frame's label, not the cell's first line.</summary>
    public StringBuilder? legendCapture;
    /// <summary>A table caption being captured (UA grids): the centred line the grid opens with.</summary>
    public StringBuilder? captionCapture;
    /// <summary>The sheet lays a cell's paragraphs INLINE (`table td p { display: inline }`): they
    /// carry no block margins, and the `p` rule's line-height paces the cell's lines.</summary>
    public bool pInlineCells;
    public double pInlineLineHeightEm;
    /// <summary>Where a nested grid's rows resume under the page top on a continuation page:
    /// the host row's box restarts one host cellspacing down, and its cell padding and the
    /// fieldset's top chrome are not spent again (measured on the test request: the frame's
    /// sides resume at 73.5 and the first row's widget at 75.0 on a 72 pt margin).</summary>
    public double continuationInsetPt;
    /// <summary>The size the current segment line's first ink saw (UA block cells), and whether
    /// that line has seen ink yet.</summary>
    public double? segLineFs;
    public bool segLineInkSeen;
    /// <summary>The family the table's own class states: every cell that names no closer
    /// face draws and measures in it.</summary>
    public string? tableClassFace;
    public double tableHeightPt;
    public Color? tableStyleBg;
    /// <summary>The table's own `white-space: nowrap` (or `pre`): it inherits into every cell.</summary>
    public bool tableNoWrap;
    public double tableStyleHPt;
    public int whiteDepth;
    public bool widthClassTable;
    public double wtBwBottom;
    public double wtBw;
    public double wtPMarginB;
    public int inlineWrapDepth;      // open inline wrappers (span/font/b/i/u/a) inside the current cell
    public double wtPadH;
    public double wtPadB;
    // Excel-fragment grid (inline border-collapse + per-cell inline border
    // longhands): the width attribute is the cell's BORDER BOX — columns pin
    // to it exactly, shared borders inside.
    public bool wtInlineGrid;
    public bool wtPMarginDefaulted;
    // …its per-side cell padding (vertical/horizontal split from the cells'
    // inline `padding: T R B L`), the cells' own declared border width (the
    // shared boundary each row advances by), and the in-cell <p> block's
    // margin-bottom (part of the cell's content height).
    public double wtPadV;
    public List<(int pos, bool on)> cellBoldMarks = new List<(int pos, bool on)>();
    public StringBuilder divText = new StringBuilder();
    public List<string>? nestedTables = null;
    /// <summary>Per nested table, the classes of the divs open round it in its cell (null when none).</summary>
    public List<string[]?>? nestedTableHostClasses = null;
    /// <summary>The classes of the blocks open round THIS grid in its host cell, for the sheet's descendant rules.</summary>
    public string[]? hostBlockClasses;
    public List<string>? rowClasses = null;
    public List<bool> rowHeightExact = new List<bool>();
    public List<bool> rowHeightAttr = new List<bool>();
    public List<double> rowHeights = new List<double>();
    public List<int> rowSections = new List<int>();
    public List<Dictionary<string, string>>? rowTdBags = null;
    public List<(StringBuilder Sb, double? Fs)> sizedSegs = new List<(StringBuilder Sb, double? Fs)>();
    /// <summary>UA cells: the typography each open &lt;font&gt; tag replaced, restored at its close,
    /// and the typography the cell's first ink saw - the cell's own once its fonts have closed.</summary>
    public Stack<(double? Fs, string? Face, Color? Fore)> fontStack = new();
    public bool firstInkSeen;
    /// <summary>The cell text's length when its last paragraph opened: a paragraph closing at the
    /// same length held nothing.</summary>
    public int paraOpenTextLen = -1;
    /// <summary>The last paragraph opened declaring `margin: 0` - it closes on a line break alone.</summary>
    public bool paraZeroMargin;
    public double? firstInkFs;
    public string? firstInkFace;
    public Color? firstInkFore;
}
}
