using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class RadiusGridRenderState
{
    // the grid's own class rule: a radius over collapsed-away spacing
    public string? gridCls;
    public Color band = null!;
    public System.Text.RegularExpressions.Match tblM = null!;
    public string face = null!;
    // ── the grid ──────────────────────────────────────────────────────
    public List<List<(string Text, int Span, int RowSpan, bool Head)>> rows = null!;
    public int nCols;
    // Resolve each cell's real column: a rowspan reserves its columns in
    // the rows below, so a later row's cells slot into what is left.
    public List<List<(string Text, int Col, int Span, int RowSpan, bool Head)>> placed = null!;
    public Dictionary<(int Row, int Col), bool> occupied = null!;
    // A column takes its MIN-CONTENT — the widest single word any of its
    // own (unspanned) cells holds — plus the cell chrome.
    public string boldFace = null!;
    public double[] colW = null!;
    public double gridW;
    public double tableX;
    public double gridRight;
    // a continuous render is exactly one content band of the AUTHORED
    // sheet tall (the model: 842 - 72 - 72 = 698)
    public double pageHeight;
    public Document doc = null!;
    public Page page = null!;
    public string res = null!;
    public string resB = null!;
    public System.Globalization.CultureInfo invc = null!;
    // ── the header band, then the body ────────────────────────────────
    public double tableTop;
    public int headRows;
    // the band is the tallest cell that spans every header row
    public double bandH;
    public double lastHeadH;
    public double firstHeadH;
    public double rowTop;
    public string html = default!;
    public HtmlLoadOptions? options = null;
    public IReadOnlyDictionary<string, Dictionary<string, string>> css = default!;
    public double pageWidth = 0;
    public double authoredHeightPt = 0;
}
}
