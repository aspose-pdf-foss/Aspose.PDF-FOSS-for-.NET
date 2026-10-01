using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class SearchFormBlockState
{
    public HtmlNode? textInput;
    public List<(string Label, string Name)> buttons = null!;
    public HtmlNode? icon;
    public HtmlNode? link;
    public Converters.HtmlToPdfConverter.SearchForm sf = null!;
    public string? inputName;
    // input class widths: widest = the centered cell, plus the input's own box
    public double cellW;
    public double inputContentW;
    public double inputH;
    // padding: the inline shorthand only (the
    // overlay-icon inset lays out of the box rather than the padding-right override)
    public double padL;
    public double padR;
    public double wrapMargin;
    public HtmlNode form = default!;
    public IReadOnlyDictionary<string, Dictionary<string, string>>? css = null;
}
}
