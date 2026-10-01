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
    public bool wrapperStacks = false;
    public bool reportCells = false;
    public List<List<MetricCell>> rows = default!;
    public string face = default!;
    public string boldFace = default!;
    public (double asc, double sum) fm = default!;
    public double indent = 0;
    public HtmlLoadOptions? loadOptions = null;
    public double p = 0;
    public bool rtl = false;
    public double s = 0;
    public string tableHtml = default!;
    public double tablePct = 0;
    public double tableWpt = 0;
    public string tag = default!;
}
}
