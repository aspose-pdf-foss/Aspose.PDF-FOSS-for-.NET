using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class InputFieldBlockState
{
    public double fieldH;
    public double boxAbove;
    public Block block = default!;
    public HtmlFlowCursor flow = default!;
    public HtmlDocProfile profile = default!;
    public Document doc = default!;
    public Core.PdfDictionary docFontDict = default!;
    public double marginBottom = 0;
    public double marginLeft = 0;
    public double marginTop = 0;
    public double pageHeight = 0;
    public double pageWidth = 0;
    public double blockFontSize = 0;
    public double lineHeight = 0;
    public double lineLeft;
    public double lineRight;
    // Assemble the wrapped lines first. A control is an atomic word
    // whose pen advance is its box width; text splits into tokens that
    // carry their trailing spaces, measured in the serif face.
    public List<(List<(Aspose.Pdf.Converters.HtmlToPdfConverter.Block? Ctl, string? Txt, double X, double FontPt, string Res)> Items, bool HasText, double MaxAdv, double MaxAbove)> runLines = null!;
    public List<(Aspose.Pdf.Converters.HtmlToPdfConverter.Block? Ctl, string? Txt, double X, double FontPt, string Res)> curItems = null!;
    public double pen;
    public bool curHasText;
    public double curMaxAdv;
    public double curMaxAbove;
    // A run stays TOGETHER over a page boundary: a
    // question whose control box no longer fits takes its label lines
    // with it to the fresh page (a run taller than a page still
    // paginates line by line).
    // The room a run needs on this page: every line's pre-drop and
    // advance, except that a LAST line whose box is bottom-anchored
    // only needs descent room under its baseline (such a line sits
    // 2.7 pt above the margin).
    public double runTotalAdv;
}
}
