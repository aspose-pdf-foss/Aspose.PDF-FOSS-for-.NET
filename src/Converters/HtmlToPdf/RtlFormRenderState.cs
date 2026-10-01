using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class RtlFormRenderState
{
    public string face = null!;
    public System.Text.RegularExpressions.Match bodyM = null!;
    public string body = null!;
    // the table splits the flow; its cells run INDEPENDENT column flows
    public System.Text.RegularExpressions.Match tblM = null!;
    public string before = null!;
    public string after = null!;
    public Document doc = null!;
    public Page page = null!;
    public double boxRight;
    public double top;
    public double bottom;
    public double y0;
    public string html = default!;
    public double pageWidth = 0;
    public double pageHeight = 0;
}
}
