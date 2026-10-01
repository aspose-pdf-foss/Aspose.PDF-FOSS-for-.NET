using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class BenefitReviewState
{
    // the export ends with a malformed `</body</html>` — take the close lazily
    public System.Text.RegularExpressions.Match bodyM = null!;
    public string body = null!;
    // ── walk the serialized DOM into formatted lines ──
    public List<Aspose.Pdf.Converters.HtmlToPdfConverter.BrLine> lines = null!;
    public List<Aspose.Pdf.Converters.HtmlToPdfConverter.BrRun> cur = null!;
    public int boldDepth;
    public int linkDepth;
    public int hiddenDepth;
    public int liDepth;
    public int h1Depth;
    public int h3Depth;
    public List<(string Tag, bool Hidden, bool Bold, bool Link, bool Li, bool H1, bool H3)> stack = null!;
    public bool pendingSpace;
    public System.Text.RegularExpressions.Regex tagRx = null!;
    public int pos;
    // ── flow the lines onto the UA grid ──
    public System.Globalization.CultureInfo inv = null!;
    public Document doc = null!;
    public Page page = null!;
    public StringBuilder sb = null!;
    public List<(Aspose.Pdf.Page Page, System.Text.StringBuilder Buf)> pageBufs = null!;
    public BrKind? prev;
    public double yBase;
    public string html = default!;
    public double pageWidth = 0;
    public double pageHeight = 0;
    public HashSet<string> blockTags = null!;
    public HashSet<string> voidTags = null!;
}
}
