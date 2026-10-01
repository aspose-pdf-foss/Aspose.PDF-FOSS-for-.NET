using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class NavRowBlockState
{
    public double fontPx;
    public double rowHeightPx;
    public Color? barBorder;
    public string? bb;
    public List<Aspose.Pdf.Converters.HtmlToPdfConverter.RowRun> runs = null!;
    public double leftPad;
    public double rightPad;
    public HtmlNode container = default!;
    public HtmlNode barEl = default!;
    public Color barColor = default!;
    public double barHeightPx = 0;
    public IReadOnlyDictionary<string, Dictionary<string, string>>? css = null;
}
}
