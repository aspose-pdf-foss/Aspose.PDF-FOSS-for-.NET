using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class GridTableState
{
    // the flow position the grid renders at - the caller's own object, moved in place
    public FlowPosition cursor = null!;
    public System.Globalization.CultureInfo invc = null!;
    // ── table tag attributes ─────────────────────────────────────────────
    public double pad;
    public double fontSize;
    public double marTopPt;
    public double marBottomPt;
    // ── parse rows/cells: runs with bold/italic, cell <br> concatenates ──
    public List<List<Aspose.Pdf.Converters.HtmlToPdfConverter.GridCell>> rows = null!;
    public List<GridCell>? row;
    public GridCell? cell;
    public System.Text.StringBuilder text = null!;
    public int boldDepth;
    public int italDepth;
    // ── column grid from the first row: percents of the inner width resolved
    // in source order (a colspan splits its share equally); the LAST column
    // takes the remainder — the sheet's shares sum past 100%. ──
    public int nCols;
    public double innerW;
    public double[] colW = null!;
    public double[] edgeX = null!;
    // ── wrap cells: char-level break-all at the face's real advances ──
    public double lineH;
    public double drop;
    // ── dialect font resources: WinAnsi entries under the face's TrueType
    // names (the raster side resolves the system face for them) ──
    public string faceRes = null!;
    // ── border strokes, buffered per page ──
    public System.Text.StringBuilder bops = null!;
    // ── layout: line-at-a-time with mid-row pagination ──
    public double limit;
    public double borderCenter;
    public Document doc = default!;
    public string tableHtml = default!;
    public double marginLeft = 0;
    public double contentWidth = 0;
    public double pageWidth = 0;
    public double pageHeight = 0;
    public double marginBottom = 0;
    public string face = default!;
    public (double asc, double sum) fm = default!;
    public double lineSum = 0;
    public Core.PdfDictionary docFontDict = default!;
}
}
