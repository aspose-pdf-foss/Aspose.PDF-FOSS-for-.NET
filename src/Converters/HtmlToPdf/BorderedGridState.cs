using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class BorderedGridState
{
    public System.Text.StringBuilder sbB = null!;
    // WinAnsi Type1 resources for <font face> cells (the Markdown pattern),
    // allocated from F8 up and registered on the page lazily. The bordered
    // branch never paginates, so the page snapshot stays valid throughout.
    public Dictionary<string, string> extraRes = null!;
    public Page borderPage = null!;
    public double tableTopTd;
    public double rowTopTd;
    // Margin-free email grid: per-row truth from each row's FIRST cell.
    //   * height:Npt — the row box is the DECLARED height plus the top
    //     pad (bottom pad 0), not the content extent.
    //   * border-width TRB — the row's bottom value is the boundary it
    //     closes with (the header's 2.25 triplet, the body rows' 1pt).
    //   * border-color — a value list carrying the -moz-use-text-color
    //     debris is DEAD (the row strokes black); a clean value
    //     colours the row's lines (the header's orange).
    // Measured: boundary centers 446.22 → 467.645
    // → 495.27 → 522.27 = 1pt top + (19.05+0.75) + 2.25 + (25.25+0.75)
    // + 1 + (25.25+0.75) + 1.
    public double[]? rowDeclH;
    public double[]? rowBotW;
    public Color?[]? rowStrokeCol;
    public int rowIdx;
    // Declared-height grid: rowTopTd ended on the LAST boundary's
    // center — the table ends at that border's bottom edge, and its
    // rows already stroked every edge (no outer frame).
    public double tableBottomTd;
    // The outer box spans the availW under width:100%; a FIXED grid's
    // chrome pushes its right edge past it.
    public double outerW;
    public double outerR;
    // The attribute grid's row box hugs its tallest cell (a size=1
    // header row is a 9px band); the css-bordered mode keeps its
    // calibrated one-line floor.
    public double rowContentB;
    // collapse shares the borders across the boundary: the row pitch
    // carries no border of its own (measured: 13.5 exactly per strut row)
    public double cellBoxH;
    // A band row (the table's inline-style height shared to the rows)
    // floors the box; content centres in the grown box below.
    public double bandExtra;
    // Declared-height rows (the email grid): box = the tr's height
    // plus the top pad (bottom pad 0), floored by the content.
    public double rowTopHalf;
    public double colXB;
    public int spanSkip;
    public double rowSubBotTd;
    public System.Text.StringBuilder rowEdgeStrokes = null!;
    public double boxW;
    // bgcolor cell fill inside the cell border box. The
    // declared-height grid's fill spans boundary center to
    // boundary center (measured: the header band 446.2..467.65,
    // under its own border lines).
    public double fillH;
    public double fillW;
    public Converters.HtmlToPdfConverter.MetricCell mc = null!;
    public double cellFs;
    public (double asc, double sum) cFm;
    public double cellLineH;
    public string mFace = null!;
    public string fontRes = null!;
    // Middle vertical alignment (the HTML cell default);
    // a valign='top' cell seats its first line at the row top.
    public double lineTopTd;
    // A link cell with no sheet anchor rule draws the UA link
    // ink: #0000FF text with an underline one tenth of an em
    // below each baseline (measured on the grid).
    public bool uaLink;
    public Color? cellInk;
    public double cellAsc;
    // The table's left edge; the sizing pass may shift it when the grid is centred.
    public double tableX;
}
}
