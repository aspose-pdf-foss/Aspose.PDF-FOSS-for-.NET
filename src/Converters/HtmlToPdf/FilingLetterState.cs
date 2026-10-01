using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class FilingLetterState
{
    public string s = null!;
    public string body = null!;
    public List<Aspose.Pdf.Converters.HtmlToPdfConverter.FilingItem> parsed = null!;
    public string cur = null!;
    public double gapNext;
    public double indent;
    public int leftDepth;
    public int indentDepth;
    public int depth;
    public int i;
    public int n;
    // the letter opens image-first and speaks in centered lines
    public bool hasImg;
    public string? html = null;
}
}
