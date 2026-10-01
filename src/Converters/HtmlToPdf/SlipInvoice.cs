using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// The till-slip invoice: a body that declares itself a percent-wide table box
// and holds nothing but full-width tables, a floated QR code and a closing
// rule. Every table fills that box, and its columns take their max-content
// share of it — the rule the probe derived for auto columns
// (see the html-table-columns spec), with a spanning cell pushing the columns
// it covers up to its own width first.
internal static partial class HtmlToPdfConverter
{
    private const double SlipSpacingPt = 1.5;    // the 2px default border-spacing
    private const double SlipPadPt = 0.75;       // the 1px default cellpadding
    private const double SlipTableGapPt = 1.5;   // the gap a following table opens
    private const double SlipBorderPt = 0.5;     // the dashed row rules
    // The closing <hr> sits this far under the last row's bottom (measured:
    // the last row closes at 296.64 and the rule draws at 323.61).
    private const double SlipHrGapPt = 26.97;

    /// <summary>A declared CSS length on an element's inline style, in points.</summary>
    private static double DeclaredLen(string attrs, string prop, double fallback)
    {
        var m = Regex.Match(attrs, prop + @"\s*:\s*([\d.]+\s*\w+)", RegexOptions.IgnoreCase);
        return m.Success && TryParseLength(m.Groups[1].Value.Replace(" ", "")) is { } v && v > 0
            ? v : fallback;
    }

}
