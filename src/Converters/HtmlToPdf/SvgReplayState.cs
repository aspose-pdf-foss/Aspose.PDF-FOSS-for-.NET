using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class SvgReplayState
{
    public System.Globalization.CultureInfo inv = null!;
    // viewBox → prepend its origin/scale into the placement.
    public System.Text.RegularExpressions.Match vb = null!;
    public double[] total = null!;
    // Root svg→page matrix: <mask> defs are declared in userSpaceOnUse (root svg)
    // coordinates, independent of the group transforms active at their USE site.
    public double[] rootTotal = null!;
    // Luminosity masks (<mask id> holding one raster): id → the def's user-space
    // rect and its grayscale png. Their spans are excluded from the content scan.
    public Dictionary<string, (double X, double Y, double W, double H, byte[] Png)> maskDefs = null!;
    public List<(int Start, int End)> maskSpans = null!;
    public Stack<(double[] M, double Alpha, string? MaskId)> stack = null!;
    public double alpha;
    public string? maskId;
    public Dictionary<string, string> gsNames = null!;
    public System.Text.StringBuilder sb = null!;
    public Page page = default!;
    public string svg = default!;
    public double[] placement = default!;
    public string svgDir = default!;
    public HtmlLoadOptions? options = null;
}
}
