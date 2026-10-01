using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class StlClassPositionedState
{
    // The geometry is ENTIRELY in the stylesheet; an auto-derived base path
    // does not resolve it (mirrors the page_N stl_ dialect's rule).
    public string css = null!;
    // .name { left: Xpt; top: Ypt; position: absolute }  — pt units only (the
    // page_N flavour positions in em via inline styles and never matches here).
    public Dictionary<string, (double Left, double Top)> pos = null!;
    public Dictionary<string, Aspose.Pdf.Converters.HtmlToPdfConverter.StlClsStyle> styles = null!;
    // The markup: class-only divs whose classes the stylesheet positions, with
    // pure inline content (spans / text / the svg object). Any inline style=
    // positioning or table structure means a different dialect.
    public List<(double Left, double Top, string Inner)> divs = null!;
    public System.Text.RegularExpressions.MatchCollection divMatches = null!;
    // Positioned text divs must dominate the body — a page merely CONTAINING
    // a few absolute classes keeps its own flow.
    public int totalDivs;
    public double pageW;
    public double pageH;
    public Document doc = null!;
    public Page page = null!;
    public System.Globalization.CultureInfo inv = null!;
    // A face draws through Standard-14 where one matches (serif output that
    // embeds nothing — the UA flow's rule); any other resolvable installed
    // face rides a named Type1 dict the rasterizer resolves. An unresolvable
    // family substitutes the UA serif (probed: Modern No. 20 → Times).
    public Dictionary<string, string> extraFaces = null!;
    public double strutDrop;
    public System.Text.StringBuilder runs = null!;
    public System.Text.StringBuilder svgPaths = null!;
    public string html = default!;
    public HtmlLoadOptions? options = null;
}
}
