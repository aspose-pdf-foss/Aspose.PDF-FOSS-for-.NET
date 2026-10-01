using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class CloseCellState
{
    // A cell whose lines carry explicit per-line styles (font-size on a <p>/<span>)
    // renders as CSS line boxes: every line gets its TRUE size (styled size, else the
    // stylesheet base), plus line-box metrics for the generator's mixed-size layout.
    // The CSS run dialect lays EVERY cell out as CSS line boxes, styled or not:
    // the uniform per-row grid takes the tallest cell's pitch, so one 24 pt
    // column would otherwise stretch every 10 pt column in its row to match.
    public bool anyStyled;
    // Resolve the recorded box-run segments into per-line decorations. The
    // box model owns the pen: each box advances it by its full width (pads
    // take real space) plus a 3 pt sibling gap; text centres inside its box
    // (pill labels sit at the left pad, ahead of their circle). A run's
    // LATER segment (the ID line under a title plate) reuses the plate's
    // placed box — no rectangle, just its own centred text run.
    public Dictionary<int, List<InlineBoxDecoration>>? boxByLine;
    public int tfLineIdx;
    public bool cellHadNestedTable;
    // Record this cell's content width against the column(s) it spans, so a table with no
    // explicit widths auto-fits each column to its widest content (+ cell padding).
    public double cellMin;
    public double cellMax;
    public double cellHdr;
    public double cellMinBrk;
    public double cellMinSerif;
    public int span;
    // Column footprint = content + cell padding (both sides) + the cell box border the
    // generator draws around it, so the summed natural width matches the rendered grid.
    // The page-widen probe measures BARE content: the widen pass adds no
    // per-column slack for zero-padding zero-border tables.
    // The CSS run dialect measures the browser's own box — cell padding plus the
    // cell's own border — with none of the legacy per-column slack.
    // A chain rule's own horizontal padding is ALREADY inside cellMin/cellMax
    // (added just above), and the table-level padSide is read from the SAME
    // declaration — adding it again bills the pair twice per column and widens
    // the sheet by a full padding pair per column.
    public double padSideExtra;
    // pt-styled fragment: the min-content footprint is exactly word +
    // pads + paragraph margins (already summed into cellMin above) — no
    // legacy slack tail, no border share (the collapsed borders live
    // outside the column boxes).
    public double extra;
    // The extraction inputs, captured from the method parameters.
    public TableParseState ps = null!;
    public TableColumnModel colModel = null!;
    public Table table = null!;
    public HtmlLoadOptions? options;
    public double cellFontSize;
    public bool dwFormCells;
    public bool fullWidthCjkMin;
    public bool breakAnywhereDoc;
    public bool cellFontShorthand;
    public List<CssElem>? chainBase;
    public double chainSpacingPt;
    public List<(string Tag, int PrevBoldDepth)> chainUnbold = null!;
    public string? cssBaseFamily;
    public double cssBasePt;
    public string? defaultCellFace;
    public double formGridStrutDropPt;
    public bool hasBorder;
    public double inlineFaceRatio;
    public bool overDeclaredDraw;
    public double padSide;
    public bool uaDocGrid;
    public bool widenProbe;
    public bool uaSerifMin;
    public bool ptCellWidths;
    public bool redlineCells;
    public bool bandDialect;
    public double cellLineHeightPt;
    public string? cssRunFace;
    public bool formGridDialect;
    public double formGridStrutPt;
    public bool liftNestedTables;
    public bool tightExtras;
    // The cell's min-content with a nested grid counted at ITS min-content (0 = use cellMin).
    public double cellMinFloor;
    public bool uaCellBoxes;
    public double borderWidth;
    public double pad;
    public string ln = null!;
    public Aspose.Pdf.Text.TextFragment tf = null!;
    public string? famForFont;
}
}
