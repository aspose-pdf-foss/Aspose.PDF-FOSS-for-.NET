using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class ReceiptCardState
{
    // the agency heading and inline address items
    public System.Text.RegularExpressions.Match agentM = null!;
    public List<string> addrItems = null!;
    public System.Text.RegularExpressions.Match addrM = null!;
    // the two summary sections' label/value rows
    public List<List<(string Label, string Value)>> sections = null!;
    // the auth band's two label/value pairs
    public System.Text.RegularExpressions.Match headM = null!;
    public string authVal = null!;
    public string rcptVal = null!;
    // the details body: label + (value | nested fees grid | right amount)
    public System.Text.RegularExpressions.Match bodyM = null!;
    public List<(string Label, string Value, bool Amount, bool Total, bool Fees)> bodyRows = null!;
    public Document doc = null!;
    public Page page = null!;
    public System.Globalization.CultureInfo inv = null!;
    public System.Text.StringBuilder sb = null!;
    // the auth band fill + dotted border
    public double bandRight;
    // ── text ── (everything #666)
    public System.Text.StringBuilder runs = null!;
    public double hdrLeft;
    public double ax;
    public double by;
    public bool afterFees;
    public string html = default!;
    public IReadOnlyDictionary<string,
        Dictionary<string, string>> css = default!;
    public double pageWidth = 0;
    public double pageHeight = 0;
}
}
