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
    public TableColumnModel colModel = null!;
    public Table table = null!;
    public Token tok = null!;
    public string tag = null!;
    public HtmlLoadOptions? options = null;
    public double cellFontSize = 0;
    public bool dwFormCells = false;
    public bool fullWidthCjkMin = false;
    public bool widenProbe = false;
    public bool redlineCells = false;
    public bool breakAnywhereDoc = false;
    public bool cellFontShorthand = false;
    public List<CssElem>? chainBase = null;
    public double chainSpacingPt = 0;
    public List<(string Tag, int PrevBoldDepth)> chainUnbold = null!;
    public string? cssBaseFamily = null;
    public double cssBasePt = 0;
    public string? defaultCellFace = null;
    public double formGridStrutDropPt = 0;
    public bool hasBorder = false;
    public double inlineFaceRatio = 0;
    public bool overDeclaredDraw = false;
    public double padSide = 0;
    public bool uaDocGrid = false;
    public bool uaSerifMin = false;
    public bool ptCellWidths = false;
    public bool bandDialect = false;
    public double cellLineHeightPt = 0;
    public string? cssRunFace = null;
    public bool formGridDialect = false;
    public double formGridStrutPt = 0;
    public bool liftNestedTables = false;
    public bool tightExtras = false;
    public bool uaCellBoxes = false;
    public double borderWidth = 0;
    public double pad = 0;
    public Dictionary<string, Dictionary<string, string>> css = null!;
    public IReadOnlyDictionary<string, Dictionary<string, string>>? docCss = null;
    public List<CssChainRule>? chainRules = null;
    public List<CssElem>? cssAncestors = null;
    public List<byte[]>? inlineSvgs = null;
    public List<string> nestedHtml = null!;
    public Func<string, bool, Aspose.Pdf.Forms.RadioButtonOptionField>? makeRadio = null;
    public double availWidthPt = 0;
    public double defaultCellFontPt = 0;
    public Dictionary<string, string> tblStyle = null!;
    public bool docElementGrid = false;
    public bool pinnedBodyGrid = false;
    public bool authoredCellChrome = false;
    public bool chainBorderSeparate = false;
    public bool elemRuleBorder = false;
}
}
