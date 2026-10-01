using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class SlipInvoiceState
{
    // …in a face the sheet names and a size it pins
    public string face = null!;
    public double fs;
    public System.Text.RegularExpressions.Match bodyM = null!;
    public string body = null!;
    public double lineH;
    public double drop;
    public string boldFace = null!;
    public string italFace = null!;
    // the body box: its percent of the sheet, opened at the UA body margin
    public double boxLeft;
    public double boxW;
    public double padTop;
    public Document doc = null!;
    public Page page = null!;
    public string res = null!;
    public string resB = null!;
    public string resI = null!;
    public System.Globalization.CultureInfo invc = null!;
    // The first baseline sits under the body's padding AND its UA margin,
    // then the table's own spacing and the cell's padding (measured:
    // 5.669 + 6 + 1.5 + 0.75 + the 8.7 drop = 22.62 exactly).
    public double rowAdvance;
    public double y;
    public bool drewAny;
    public string html = default!;
    public HtmlLoadOptions? options = null;
    public IReadOnlyDictionary<string, Dictionary<string, string>> css = default!;
    public double pageWidth = 0;
    public double pageHeight = 0;
    // ── the rows ──────────────────────────────────────────────────
    public List<List<(string Text, int Span, bool Head, bool Ital, bool Right, bool Rule)>> rows = null!;
    public List<string?> rowImgs = null!;
    public List<string?> rowDivs = null!;
    public int nCols;
    // ── the columns: max-content boxes, spanning cells pushing the
    // columns they cover, then a proportional share of the box ──────
    public double inset;
    public double[] box = null!;
    public double sum;
    public double avail;
}
}
