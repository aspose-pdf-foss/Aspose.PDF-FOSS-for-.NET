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
    // table bgcolor: one band behind the whole grid (rows and spacings alike).
    // pt-report/newsletter mode: the band's real height is only known after
    // the sub-grids lay out — remember where to UNDERLAY it and paint after
    // the rows (the wrapper-stack pattern). Other flows keep the estimated
    // pre-paint their greens were calibrated on.
    public bool tableBgUnderlay;
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
    public bool rtl;
    public bool paragraphCells;
    public bool serifReportCells;
    public HtmlLoadOptions? loadOptions;
    // The solved column geometry, shared by the render stages.
    public int nCols;
    public double[] colW = null!;
    public double availW;
    public double tableX;
    public double hheaSum;
    public System.Globalization.CultureInfo invc = null!;
}
}
