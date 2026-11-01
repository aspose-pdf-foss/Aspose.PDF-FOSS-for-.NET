using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class DivOpenState
{
    // True once a chain rule has styled this div — a styled div may be
    // an inline-block plate or a box run, and those keep riding their
    // line; only a PLAIN div takes the block break below.
    public bool divChainStyled;
    public TableParseState ps = null!;
    public Token tok = null!;
    public string tag = null!;
    public HtmlLoadOptions? options = null;
    public double cellFontSize = 0;
    public bool dwFormCells = false;
    public bool widenProbe = false;
    public bool redlineCells = false;
    public List<CssElem>? chainBase = null;
    public bool liftNestedTables = false;
    public bool uaCellBoxes = false;
    public double pad = 0;
    public IReadOnlyDictionary<string, Dictionary<string, string>>? docCss = null;
    public List<CssChainRule>? chainRules = null;
    public bool pinnedBodyGrid = false;
}
}
