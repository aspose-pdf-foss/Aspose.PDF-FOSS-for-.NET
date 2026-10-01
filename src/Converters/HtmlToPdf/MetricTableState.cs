using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class MetricTableState
{
    public Converters.HtmlToPdfConverter.MetricParseState mps = null!;
    // The sheet's `table { font: 10pt Arial }` SHORTHAND seeds the grid's
    // size AND face — the longhand reads above never see either half. The
    // cells then DRAW in that face too (tableRuleFace below).
    public bool tableRuleFace;
    // A face whose win metrics sum to one em or less (SimSun's bitmap-era
    // 220+36/256) would render zero-leading lines; the engine paces such
    // CJK faces at 1.2 em (measured on the official-letter reference).
    public double fmSum;
    public double lineH;
    public string boldFace = null!;
    public double bw;
    // Outer-frame-only COLLAPSE grid: the TABLE rule alone carries a solid
    // border (longhands) under border-collapse — the frame collapses onto
    // the table box and the cells, declaring no borders of their own, draw
    // none. Columns come from the width classes, given back to the grid box
    // deficit ∝ slack (declared − min-content) when over-declared.
    public double collapseBoxW;
    // The frame the table tag's own inline `border-style` draws around the grid
    // (width, corner radius, colour); 0 = none.
    public double frameW;
    public double frameRadius;
    public Color frameColor = Color.Black;
    // width: 100% from the sheet's table rule — the grid fills the content box.
    public bool tableFills;
    // Parse the table structure. Geometry attributes sit on the <table> tag;
    // a class rule's MARGIN-LEFT indents the whole table box.
    public double s;
    public double p;
    public double indent;
    public double tablePct;
    public double tableWpt;
    // Element-rule collapse grid: the table AND td rules both carry a
    // solid border shorthand under border-collapse ("table, th, td
    // { border: 1px solid; border-collapse: collapse }") — the source
    // engine draws the shared-1px grid across the symmetric content
    // frame (measured: box 96..499 on the 409 pt
    // band, cell fills 97.5..497.5, zero spacing).
    public bool elemCollapseGrid;
    public List<List<Aspose.Pdf.Converters.HtmlToPdfConverter.MetricCell>> rows = null!;
    public System.Text.StringBuilder text = null!;
    public Stack<bool> whiteSpans = null!;
    // one entry per open span: whether it is a float (its text makes no line box in the cell)
    public Stack<bool> floatSpans = null!;
    public int floatDepth;
    // The sheet's `a { color: … }` rule inks anchor text in cells (the source
    // renderer applies it as an inline colour; the corpus wraps whole cell
    // contents in one <a>, so it styles the rest of the cell like <font color>).
    public Color? rmtAnchorColor;
    // Report cells: a span's typography ends WITH the span — the state to
    // restore at its close (the whole-cell restyle stays for the legacy flows).
    public Stack<(double? fs, string? fc, bool b, Aspose.Pdf.Color? fo)> spanSaves = null!;
    // the NEWSLETTER dialect only — the NHS/boleto report greens were
    // calibrated on the whole-cell typography model and keep it
    public bool reportCells;
    // A table with NO text anywhere (a logo strip whose only content is an image
    // that failed to load) collapses each row to its padding band — the flow
    // advances just the cell padding for it. A blank SPACER row inside a text
    // table keeps its line box (the calibrated metric behaviour).
    public bool tableHasText;
    // WinAnsi Type1 resources for styled cells in the flat (borderless) grid,
    // registered on whichever page the row lands on.
    public Dictionary<string, string> flatRes = null!;
    /// <summary>The grid's caption text (UA grids): drawn centred over the box before the rows.</summary>
    public string? captionText;
    // table bgcolor: one band behind the whole grid (rows and spacings alike).
    // pt-report/newsletter mode: the band's real height is only known after
    // the sub-grids lay out — remember where to UNDERLAY it and paint after
    // the rows (the wrapper-stack pattern). Other flows keep the estimated
    // pre-paint their greens were calibrated on.
    public bool tableBgUnderlay;
    /// <summary>The widest intrinsic box of the declared tables nested in this grid's cells (their declared width
    /// plus their own padding and border): a declared grid width is a minimum that such a box widens.</summary>
    public double nestedDeclaredFloorPt;
    public Page tableBgPage = null!;
    public int tableBgStartIdx;
    public double tableBgStartY;
    // Outer-frame collapse grid: rows sit INSIDE the frame — content drops
    // one frame width; the frame strokes after the rows, around the box.
    public double cbFrameTopY;
    public Page cbFramePage = null!;
    // A ROWSPAN cell's content must FIT its spanned rows: they grow evenly
    // to cover the deficit (the order ticket's 48 pt masthead stretches
    // both title rows, their cells then centring in the taller boxes).
    public double[] rowSpanExtra = null!;
    // The table inputs, captured from the method parameters (page and y stay ref parameters).
    public Document doc = null!;
    public string tableHtml = null!;
    public IReadOnlyDictionary<string, Dictionary<string, string>> css = null!;
    public double marginLeft;
    public double contentWidth;
    public double pageWidth;
    public double pageHeight;
    public double marginTop;
    public double marginBottom;
    public string face = null!;
    public (double asc, double sum) fm;
    public Core.PdfDictionary docFontDict = null!;
    public bool stdSerif;
    public double baseFontSize;
    public bool wrapperStacks;
    public double symInsetPt;
    /// <summary>An auto-width grid keeps the body inset on its right on a grown sheet too: a
    /// min-floor grid's ink grew the sheet (measured: the holdings grid fills 449.75 + its frame on
    /// the 455.91 box; a percent grid's box is the inset span already).</summary>
    public bool keepRightInset;
    /// <summary>The table is being laid out in its own declared box for the sheet's ink walk: it keeps that box.</summary>
    public bool declaredBoxLayout;
    /// <summary>The padding of the host cell a nested grid stands in (0 at the flow's top level): a
    /// declared box overflows into it, never past it.</summary>
    public double hostCellPadPt;
    public bool rtl;
    public bool paragraphCells;
    public bool serifReportCells;
    public HtmlLoadOptions? loadOptions;
    /// <summary>The sheet's adjacent-sibling cell rules: a cell dressed by the cell to its left.</summary>
    public List<CssSiblingCellRule>? siblingCellRules;
    // The solved column geometry, shared by the render stages.
    public int nCols;
    public double[] colW = null!;
    public double availW;
    public double tableX;
    public double hheaSum;
    public System.Globalization.CultureInfo invc = null!;
    public List<List<Aspose.Pdf.Converters.HtmlToPdfConverter.MetricCell>> rowsIn = null!;
    public int[] order = null!;
    public double declSum;
    public double rhSum;
    public double cbAvail;
    public double[] cbDeclBox = null!;
    public double[] cbMinBox = null!;
    public double cbSumDecl;
    public double cbSumSlack;
    public double cbDeficit;
    public double usableSym;
    public double[] minCol = null!;
    public double natSum;
    public double fixedSum;
    public double surplus;
    public double targetContent;
    public double sumW;
    // the element-rule collapse grid fills the SYMMETRIC frame, its
    // shared borders inside it (measured: cols sum to 400 in the 403
    // box — 96..499 on the 409 band)
    public double tfAvail;
    public double sumW0;
    // SEVERAL over-full auto columns distribute like the browser: each
    // keeps its min-content (longest word) and the remaining width goes
    // out proportionally to the max-content EXCESS over that floor
    // (probed on the three-paragraph grid: 207/155/44 of a 409 box).
    public int autoCols;
    public double rtlTotal;
    public double[] colPct = null!;
    /// <summary>A column's own COLGROUP absolute width in points, as a MINIMUM on its content
    /// box. Kept apart from the cell-declared widths so honouring it cannot disturb them.</summary>
    public double[] colGroupPt = null!;
    /// <summary>Per column, the colgroup's declared `min-width` as a content width (0 = none).</summary>
    public double[] colMinWidthPt = null!;
    public double[] colPx = null!;
    public bool[] colPxStyle = null!;
    public bool[] colZero = null!;      // a column some cell declares at zero width
    public bool[]? colVoid;             // a column no real cell reaches (UA form grids): no box, no spacing
    /// <summary>Width held by columns their own absolute style width pins.</summary>
    public double pinnedW;
    public bool[] colFixed = null!;
    /// <summary>A UA grid column a width ATTRIBUTE declares: its box is max(declared, min-content); it
    /// takes no surplus and shares a deficit with the others in proportion to slack.</summary>
    public bool[] colDeclared = null!;
    public bool? declaredOverConstrained;
    public double usableW;
    // stdSerif percent grid: browser auto layout, measured —
    // declared share of the SYMMETRIC usable box (one UA body margin inside
    // the right content edge too), w = max(declared, min-content); an
    // over-full set gives its deficit back proportionally to each column's
    // SLACK (w − min-content). Non-percent columns ride along at their
    // natural width with zero slack.
    public bool uaPctGrid;
    // Clamp: shrink the right-most non-fixed column into the remaining width.
    public double total;
    /// <summary>The pt form grid's min-content box (its raised column mins, spacings and chrome) - what
    /// the sheet grows to when the grid cannot fit the body. Set by SolvePtFormGridColumns.</summary>
    public double ptFormMinContentPt;
    /// <summary>The pt form grid flag as handed to the parse, before its parse state exists.</summary>
    public bool ptFormCells;
    // The flow position this table renders at - the caller's own object, so a table
    // that outgrows the page moves the caller's cursor as it goes.
    public FlowPosition cursor = null!;
}
}
