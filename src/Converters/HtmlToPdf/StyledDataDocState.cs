using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class StyledDataDocState
{
    public double marginTop;
    public double marginLeftLay;
    public double bodyWidth;
    public double bodyMarginLeft;
    public double bodyMarginTop;
    // Per-face parsers and vertical metrics.
    public Dictionary<string, Aspose.Pdf.Text.GlyphOutlineParser> glyphParsers = null!;
    public Dictionary<string, (double winAsc, double winDesc, double upm)> faceMetrics = null!;
    // ---- Layout pass: place every run, tracking the rightmost extent ----
    public List<(double y, double x, string text, Aspose.Pdf.Converters.HtmlToPdfConverter.StyledNode p)> runsOut = null!;
    public double maxUrx;
    public double y;
    public List<double> pendingMargins = null!;
    public double prevDesc;
    public StyledNode bodyNode = default!;
    public double size;
    public double lh;
    public double asc;
    public double ls;
    public double lsF;
    public double x0;
    public double colWidth;
    // Character stream tagged with run index (span boundaries stay separate ops).
    public List<(char c, int run)> stream = null!;
    // Greedy space-break wrap; letter-spacing widens every glyph advance
    // (including a run's last — the next run starts that much further right).
    public List<List<(char c, int run)>> lines = null!;
    public double mt;
}
}
