using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class InlineOpenState
{
    public double prevPt;
    public Color? prevColor;
    public bool styleBold;
    public bool styleItalic;
    public TableParseState ps = null!;
    public Token tok = null!;
    public string tag = null!;
    public HtmlLoadOptions? options = null;
    public double cellFontSize = 0;
    public bool dwFormCells = false;
    public bool widenProbe = false;
    public bool redlineCells = false;
    public List<CssElem>? chainBase = null;
    public List<(string Tag, int PrevBoldDepth)> chainUnbold = null!;
    public bool ptCellWidths = false;
    public bool bandDialect = false;
    public string? cssRunFace = null;
    public bool formGridDialect = false;
    public bool liftNestedTables = false;
    public Dictionary<string, Dictionary<string, string>> css = null!;
    public IReadOnlyDictionary<string, Dictionary<string, string>>? docCss = null;
    public List<CssChainRule>? chainRules = null;
}
}
