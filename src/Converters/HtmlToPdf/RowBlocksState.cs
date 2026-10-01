using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class RowBlocksState
{
    public HtmlNode dom = null!;
    public List<(int start, int end, Aspose.Pdf.Converters.HtmlToPdfConverter.Block block)> extracts = null!;
    public System.Text.StringBuilder sb = null!;
    public string html = default!;
    public IReadOnlyDictionary<string, Dictionary<string, string>>? css = null;
}
}
