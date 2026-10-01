using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class AccessFormState
{
    public System.Text.RegularExpressions.Match pageM = null!;
    public string content = null!;
    public System.Text.RegularExpressions.Match titleM = null!;
    // The document as an ordered item stream: sections (heading + question
    // boxes + loose pre answers) separated by rules.
    public List<(string Kind, string A, string B)> items = null!;
    public Document doc = null!;
    public System.Globalization.CultureInfo inv = null!;
    public Page page = null!;
    public StringBuilder boxes = null!;
    public StringBuilder runs = null!;
    public bool firstPage;
    public string orange = null!;
    public string gray = null!;
    public string black = null!;
    public double lastMark;
    public bool lastWasBox;
    public string html = default!;
    public IReadOnlyDictionary<string,
        Dictionary<string, string>> css = default!;
    public double pageWidth = 0;
    public double pageHeight = 0;
}
}
