using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class DivSegmentState
{
    public List<Aspose.Pdf.Converters.HtmlToPdfConverter.DivSeg> segs = null!;
    public System.Text.RegularExpressions.Regex divRx = null!;
    public int pos;
    public string html = default!;
    public IReadOnlyDictionary<string, Dictionary<string, string>>? classCss = null;
    public double contentWidthPt = 0;
    public bool allowPxCols = false;
    public Match? hit;
    public int afterOpen;
    public int end;
}
}
