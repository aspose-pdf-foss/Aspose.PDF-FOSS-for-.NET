using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class RtlSvgTableState
{
    public double widthPx;
    public List<Aspose.Pdf.Converters.HtmlToPdfConverter.HtmlNode> rows = null!;
    public Converters.HtmlToPdfConverter.RtlSvgTable dt = null!;
    // legend row: last row, >= 2 cells, every cell = one svg placeholder + text
    public List<Aspose.Pdf.Converters.HtmlToPdfConverter.HtmlNode> legendCells = null!;
    public HtmlNode table = default!;
    public IReadOnlyDictionary<string, Dictionary<string, string>>? css = null;
}
}
