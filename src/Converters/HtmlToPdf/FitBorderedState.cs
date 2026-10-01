using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class FitBorderedState
{
    public double innerW;
    public MetricParseState mps = default!;
    /// <summary>The grid being solved, for the measures that parse a cell's nested grids.</summary>
    public MetricTableState? host;
    public List<List<MetricCell>> rows = default!;
    public double[] colW = default!;
    public bool[] colFixed = default!;
    public double[] colPct = default!;
    public double[] colPx = default!;
    public bool[] colPxStyle = default!;
    public int nCols = 0;
    public double availW = 0;
    public double bw = 0;
    public double p = 0;
    public double pageWidth = 0;
    public double pageHeight = 0;
    public double marginTop = 0;
    public double marginBottom = 0;
    public double tableWpt = 0;
    public double tablePct = 0;
    public double baseFontSize = 0;
    public bool paragraphCells = false;
    public string tableHtml = default!;
    public double s = 0;
    public string face = default!;
    public string boldFace = default!;
    public double symInsetPt = 0;
    public bool tableFills = false;
    /// <summary>The grid is a UA (standard-serif) grid: its column max-content is its cells' longest line.</summary>
    public bool stdSerif = false;
    public double chromeB;
    public double[] naturalB = null!;
    // Attribute grid: undeclared columns take what the grid box leaves
    // beside the pixel-fixed ones, floored at their min-content (the
    // widest unbreakable chunk) — an over-long word overflows the box
    // rather than shrinking below it. The grid box is the SYMMETRIC
    // content frame (one UA body margin inside the right content edge
    // too), and the outer border straddles OUTSIDE it — measured: the
    // grid spans 96..499 with its outer border edge at 500.5.
    public double availB;
    public double gridChrome;
    // Declared shares apply only when they FIT beside the min-contents;
    // an over-constrained set falls back to min-content columns (the
    // 15%-column's long word forces its share past 15, so the 85%
    // partner cannot keep 85 — measured: it takes the REMAINDER).
    // Attribute grids resolved their overflow above — pixel-fixed
    // columns must not fall back to natural widths.
    public double sumB;
    // Natural columns that STILL over-fill the box give the deficit
    // back ∝ their slack (max-content − min-content), floored at
    // min-content, and their text wraps at the solved width — the
    // banked auto-width rule (measured on the aggregate grid:
    // 62.1/79.2/64.1/66.2/58.4/52.1, reproduced within 0.3 pt).
    // An attribute grid reaches here only when its own last-column
    // shrink bottomed out at min-content and the grid still spills.
    public double bankAvail;
}
}
