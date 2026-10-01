using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class OutSystemsState
{
    public List<(char Kind, string Frag)> elems = null!;
    public double contentX;
    public double contentW;
    public double q4;
    public double drop12;
    public double dropTnr;
    public double pMargin;
    public Document doc = null!;
    public Page page = null!;
    public System.Globalization.CultureInfo inv = null!;
    // ── the flow ──
    public double y;
    public double pendingGap;
    public double natRight;
    public int tableIdx;
    // ── the shrink: content pinned at the left margin and the page top ──
    public double s;
    public string html = null!;
    public double pageWidth = 0;
    public double pageHeight = 0;
    public double mL = 0;
    public double mR = 0;
}
}
