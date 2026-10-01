using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class StyledRunsState
{
    public System.Globalization.CultureInfo inv = null!;
    public System.Text.StringBuilder sb = null!;
    public List<(double x0, double w, string url)> underlines = null!;
    public int i;
    public double runX;
    public Document doc = default!;
    public Page page = default!;
    public double x = 0;
    public double y = 0;
    public string lineText = default!;
    public List<string?> urls = default!;
    public double fontSize = 0;
    public List<(Page page, Aspose.Pdf.Rectangle rect, string url)> pendingLinks = default!;
    public Core.PdfDictionary docFontDict = default!;
    public ArraySegment<double> extraAdv = default!;
    public ArraySegment<bool> supFlags = default!;
    public double supFontSize = 0;
    public double supRise = 0;
}
}
