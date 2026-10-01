using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class TableWidthSolveState
{
    public double pctCapW;
    public double pctNaturalW;
    public double[]? pctMinsForDraw;
    // A table declaring an ABSOLUTE width ATTRIBUTE (`width="680"`) FILLS it,
    // like a browser: when the columns' content fit stays narrower, they grow
    // proportionally — and a declared width beyond the available area carries
    // into the page auto-widen through the natural width. CSS width rules are
    // deliberately NOT fill targets here: the stylesheet grids resolve through
    // the percent/colgroup models above.
    public double tableWidthAbsPt;
    // Space and no-break space share one glyph, and the first of the two to occur
    // in the document's rendered text decides how BOTH read back out of the page —
    // a document that opens with an &nbsp; cell reports nbsp between all its words,
    // one that opens with plain text reports plain spaces even for &nbsp; entities.
    // ...and the decision belongs to the DOCUMENT, not to one grid: a lifted
    // nested table renders as its own Table, so scanning this one alone let an
    // inner grid that opens with plain text report plain spaces while the sheet's
    // very first cell held an &nbsp;. Walk the nested grids in document order too,
    // and hand every one of them the same winner.
    public List<Aspose.Pdf.Table> grids = null!;
    // The extraction inputs, captured from the method parameters.
    public TableColumnModel colModel = null!;
    public Table table = null!;
    public Dictionary<string, string> tblStyle = null!;
    public Match tblTag = null!;
    public List<CssElem>? chainBase;
    public double availWidthPt;
    public double cellFontSize;
    public bool cellFontShorthand;
    public bool dwFormCells;
    public bool fullWidthCjkMin;
    public bool overDeclaredDraw;
    public bool uaDocGrid;
    public double padSide;
    public double rowPctDeclMax;
    public int headerRows;
    public bool ptCellWidths;
    public bool uaCellBoxes;
    public bool uaSerifMin;
    public double rowPxAtMax;
    public int rowPxCellsAtMax;
    public double sumPct;
    public int nSpec;
    public double rem;
    public double total;
    public double tableW;
    public double[] mins = null!;
    public double sumW;
    // Over-declared grid dialect, AUTO layout: a declared percent column
    // holds its EXACT share of the box — only a min-content overflow
    // grows it — and the auto columns split what remains (measured on
    // the owner grid: [20,17,10,5→min,auto=remainder,14] of the
    // standard box). The legacy proportional squeeze must not re-shrink
    // the exact shares.
    public bool overExactShares;
    // No explicit widths: content-fit. Only when the caller opts in with a real available
    // width (the wide-table ConvertFromHtml path); legacy callers (header/footer & in-flow
    // HtmlFragment tables) keep the equal-% fallback below so their layout is unchanged.
    // Use max-content (no wrapping) when the table fits the available width; otherwise fall
    // back to min-content (columns shrink to their widest word and multi-word cells wrap) —
    // matching a browser's auto table layout.
    public double sumMax;
    // Min-content, but keep header cells on one line when the resulting table still fits the
    // available width; if even that overflows, fall back to the pure widest-word min so a wide
    // header never forces the page/table wider (that would override the caller's page size).
    public List<double> minPref = null!;
    public double sumPref;
    public List<double> chosenMin = null!;
    public List<double> chosen = null!;
    public bool wordMailCells;
    public System.Text.StringBuilder sb = null!;
    // A chain-rule percent-width table fills whatever box it lands in at
    // DRAW time (the outer cell's real width is unknown while it builds),
    // so its columns are emitted as PERCENT shares — surplus rides every
    // column proportionally (the surplus rule).
    public double sumChosenAll;
    public bool emitPctCols;
    public double emitBoxW;
    public bool lastAbsorbs;
    // ONE column declaring `width="100%"` is the layout-table idiom for "give me
    // everything my siblings' content does not need": its row-mates are spacer
    // cells that must keep exactly their content, and the whole remainder is the
    // declared column's. Spreading the row proportionally instead left a
    // 100 %-wide cell about a quarter of its row, and the grid nested inside it
    // shrank in the same proportion at every further level.
    public int fillCol;
    public double sumOtherMins;
    // A chain-dialect percent grid (per-cell `width: N%`) resolves at DRAW
    // time against its real box — the build's available width is the outer
    // table's, and pt columns fixed against it come out ~2× wide and clip.
    public double cwSum;
    public bool emitPctHere;
    // The total width the chosen columns occupy; the solver's stages grow and fit it.
    public double naturalWidthPt;
}
}
