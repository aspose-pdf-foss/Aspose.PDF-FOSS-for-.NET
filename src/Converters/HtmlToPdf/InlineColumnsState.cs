using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class InlineColumnsState
{
    public System.Text.RegularExpressions.Match contM = null!;
    public int nCols;
    public string body = null!;
    public System.Text.RegularExpressions.Match endM = null!;
    // `column-gap` on the same rule; the CSS initial value is 1 em.
    public System.Text.RegularExpressions.Match gapM = null!;
    // The body's own inline style seeds the flow: its face, its size and the
    // horizontal padding that insets the content box on both sides.
    public System.Text.RegularExpressions.Match bodyM = null!;
    public string bodySt = null!;
    public string face = null!;
    public double fs;
    public double padX;
    public double lineH;
    public double drop;
    public PageInfo? pageInfo;
    public double marginTop;
    public double marginBottom;
    public double marginX;
    public double contentW;
    public double gap;   // the column-gap in points
    public double colW;
    // ── the flow, as lines ────────────────────────────────────────────
    // Each entry is one line box: its runs (x offset inside the column,
    // text, bold) and whether its word gaps stretch to fill the column.
    public List<(List<(double Dx, string Text, bool Bold)> Runs, bool Justify)> flow = null!;
    // ── pour the lines down the columns ───────────────────────────────
    public Document doc = null!;
    public Page page = null!;
    public int col;
    public double y;
    public double bottom;
    public System.Globalization.CultureInfo inv = null!;
    public string html = default!;
    public HtmlLoadOptions? options = null;
    public double pageWidth = 0;
    public double pageHeight = 0;
}
}
