using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class MetricImgOpenState
{
    public double imgH;
    public MetricParseState mps = default!;
    public Token tok = default!;
    public StringBuilder text = default!;
    public IReadOnlyDictionary<string, Dictionary<string, string>> css = default!;
    public bool stdSerif = false;
    public bool reportCells = false;
    public HtmlLoadOptions? loadOptions = null;
}
}
