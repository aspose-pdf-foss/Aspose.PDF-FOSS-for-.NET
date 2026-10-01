using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class MetricRowsState
{
    // The extraction inputs, captured from the method parameters.
    // the flow position the rows render at - the caller's own object, moved in place
    public FlowPosition cursor = null!;
    public MetricParseState mps = null!;
    public List<List<MetricCell>> rows = null!;
    public double[] colW = null!;
    public int nCols;
    public double availW;
    public double s;
    public double lineH;
    public string face = null!;
    public string boldFace = null!;
    public double hheaSum;
    public (double asc, double sum) fm;
    public double p;
    public double pageWidth;
    public double pageHeight;
    public double marginTop;
    public double marginBottom;
    public double tableWpt;
    public double tablePct;
    public double baseFontSize;
    public bool paragraphCells;
    public string tableHtml = null!;
    public double symInsetPt;
    public IReadOnlyDictionary<string, Dictionary<string, string>> css = null!;
    /// <summary>Whether the sheet asks cells or rows to avoid a break inside (read once per grid).</summary>
    public bool? cellBreakInsideAvoided;
    public Document doc = null!;
    public Core.PdfDictionary docFontDict = null!;
    public HtmlLoadOptions? loadOptions;
    public System.Globalization.CultureInfo invc = null!;
    public Dictionary<string, string> flatRes = null!;
    public bool reportCells;
    public bool serifReportCells;
    public bool stdSerif;
    public bool wrapperStacks;
    public double collapseBoxW;
    public double[] rowSpanExtra = null!;
    public bool tableHasText;
    public bool tableRuleFace;
    public double tableX;
    public double marginLeft;
    public double contentWidth;
    /// <summary>The sheet's adjacent-sibling cell rules, forwarded into nested grids.</summary>
    public List<CssSiblingCellRule>? siblingCellRules;
    public List<Aspose.Pdf.Converters.HtmlToPdfConverter.MetricCell> r = null!;
    // an all-empty row still holds one line box; a row whose every
    // sized cell takes its font from a CLASS skin is content-paced (the
    // boleto rows carry no base-font strut)
    public bool classPaced;
    public double rowContentH;
    // A ROWSPAN cell's content overlays the FOLLOWING rows — it never
    // inflates its own row's box (the header's rowspan=4 address cell).
    // …and an nbsp-only cell in a row with REAL text never raises it
    // either (the financial grid's 12 pt spacer cells ride their 11 pt
    // label rows at the labels' pitch — probed on the statement ladder).
    public double rowRealTextH;
    public bool rowHasRealText;
    public double rowBoxH;
    public double rowNaturalBoxH;
    // a tr style height floors the row's box (the letter's paced rows);
    // a CLASS height — on the row or a cell — paces it exactly (the
    // boleto's h13/h12 grid rows and its 1px .cut tear-off row)
    public double rowCellClassH;
    public bool rowHasText;
    public double rowClassH;
    public double rowStyleH;
    // Band rows (the table's inline-style height shared to the rows):
    // content centres in the grown box (probed: the 135px band's cell
    // baselines sit on the band's vertical middle).
    public double bandCenterPad;
    public double contentTop;
    public double colX;
    /// <summary>UA block cells: no in-flow block has been drawn in the cell yet (the first one's
    /// margin-top is spent only in an unpadded cell).</summary>
    public bool uaFirstBlock;
    public double rowSubBottom;
    public double rowRealBottom;
    public int flatSkip;
    // a recursed sub-grid that outgrew the estimate carries the row
    // with it — the next row opens below the real drawn bottom. In the
    // report/newsletter wrapper mode a sub-table row's advance IS its
    // real drawn extent — the estimate is a pre-pass floor only, and
    // letting it win strands the next table a page early.
    public double rowAdvance;
    public MetricCell mc = null!;
    public double cellFs;
    public double cellLineH;
    // Middle vertical alignment (the HTML cell default);
    // a valign='top' cell seats its first line at the row top.
    // the collapsed class grid top-aligns its cells
    public double lineTop;
    public double cellShear;   // the synthetic italic slant the cell's runs draw with (0 = none)
    public string mFace = null!;
    // Browser-UA flow draws the Standard-14 serif faces (F5/F6);
    // the MSHTML metric flow keeps its embedded-face resources;
    // a <font face>/font-family cell brings its own WinAnsi face.
    public string fontRes = null!;
    public double segTop;
    public double sgPrevMb;
    // the bands of the cell being drawn and the one in hand (a box opening measures the bands it holds)
    public List<MetricDivSeg>? curBands;
    public int curBandIdx;
    // the open div box: where its blocks' text starts, their wrap width, and what closes it
    public double boxTextX, boxContentW, boxPadBottom, boxMarginBottom;
    // the last band drawn closed a paragraph (a quirks cell drops its bottom margin)
    public bool lastBandParagraph;
}
}
