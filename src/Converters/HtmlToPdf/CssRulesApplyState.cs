using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class CssRulesApplyState
{
    public string tagLower = null!;
    public IReadOnlyDictionary<string, Dictionary<string, string>>? css = null;
    public string tag = default!;
    public Dictionary<string, string>? attrs = null;
    public BlockStyle s = default!;
    public bool metricLayout = false;
    public bool coverStyles = false;
    public bool floatFlow = false;
}
}
