using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of <see cref="BuildTableFromHtml"/>: the column
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
    public double collapsedLineH;
    public int curSection;
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
    public double pendingAbsLeftFrac;
    public int pendingNestSpan;
    public double pendingRowH;
    public bool pendingRowHExact;
    public List<MetricCell>? row;
    public HorizontalAlignment? rowAlign;
    public Color? rowBg;
    public bool rowBold;
    // tr class skins (the boleto micro-framework): row typography defaults
    // and `.cls td` descendant bags applied to every cell of the row
    public string? rowFace;
    public Color? rowFore;
    public double? rowFs;
    public bool rowFsFromClass;
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
    public Color? tableBg;
    public bool tableClassFont;
    public double tableHeightPt;
    public Color? tableStyleBg;
    public double tableStyleHPt;
    public int whiteDepth;
    public bool widthClassTable;
    public double wtBwBottom;
    public double wtBw;
    public double wtPMarginB;
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
    public List<string>? rowClasses = null;
    public List<bool> rowHeightExact = new List<bool>();
    public List<double> rowHeights = new List<double>();
    public List<int> rowSections = new List<int>();
    public List<Dictionary<string, string>>? rowTdBags = null;
    public List<(StringBuilder Sb, double? Fs)> sizedSegs = new List<(StringBuilder Sb, double? Fs)>();
}
}
