using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class AgendaOutlineRenderState
{
    public string face = null!;
    public double pageH;
    public double pageW;
    public double contentW;
    public string res = null!;
    public string resI = null!;
    public double y;
    // ── the header: its details column is a percent-wide float, and its
    // lines centre on that column's own middle ──────────────────────────
    public System.Text.RegularExpressions.Match hdrM = null!;
    public int prevLvl;
    public string html = default!;
    public Page page = default!;
    public double marginLeft = 0;
    public double marginRight = 0;
    public double marginTop = 0;
}
}
