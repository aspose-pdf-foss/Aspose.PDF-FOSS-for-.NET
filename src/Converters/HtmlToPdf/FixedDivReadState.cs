using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class FixedDivReadState
{
    // The stl_ dialect keeps all appearance in the stylesheet — without it
    // the bare markup flows (the caller's reflow path). Only CALLER
    // context counts here: inline <style> blocks, or a linked stylesheet reached
    // through an explicitly supplied BasePath. The auto base derived from the
    // file's own directory resolves resources, but does not flip this route —
    // a sidecar-styled page loaded without a base path reflows.
    public string css = null!;
    // Class → declarations we honor (font-size em, font-family, color,
    // letter-spacing em on the root 12 pt em). Class names are arbitrary
    // (CssClassNamesPrefix renames the stl_ scheme).
    public Dictionary<string, (double? fs, string? fam, string? col, double? ls)> clsFont = null!;
    public Dictionary<string, List<Aspose.Pdf.Converters.HtmlToPdfConverter.OwnFace>> ownFaces = null!;
    public System.Text.RegularExpressions.MatchCollection pageDivs = null!;
    public string html = default!;
    public HtmlLoadOptions? options = null;
    public List<FixedPageDiv> divs = default!;
}
}
