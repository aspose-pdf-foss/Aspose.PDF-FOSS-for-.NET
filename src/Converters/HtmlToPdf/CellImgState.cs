using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class CellImgState
{
    // The extraction inputs, captured from the method parameters.
    public TableParseState ps = null!;
    /// <summary>The image resolved to the UA broken-image placeholder.</summary>
    public bool uaBrokenImage;
    public TableColumnModel colModel = null!;
    public Table table = null!;
    public Token tok = null!;
    public string tag = null!;
    public HtmlLoadOptions? options;
    public double cellFontSize;
    public bool dwFormCells;
    public bool wordMailCells;
    public bool fullWidthCjkMin;
    public bool widenProbe;
    public bool redlineCells;
    public bool breakAnywhereDoc;
    public bool cellFontShorthand;
    public List<CssElem>? chainBase;
    public double chainSpacingPt;
    public List<(string Tag, int PrevBoldDepth)> chainUnbold = null!;
    public string? cssBaseFamily;
    public double cssBasePt;
    public string? defaultCellFace;
    public double formGridStrutDropPt;
    public bool hasBorder;
    public double inlineFaceRatio;
    public bool overDeclaredDraw;
    public double padSide;
    public bool uaDocGrid;
    public bool uaSerifMin;
    public bool ptCellWidths;
    public bool bandDialect;
    public double cellLineHeightPt;
    public string? cssRunFace;
    public bool formGridDialect;
    public double formGridStrutPt;
    public bool liftNestedTables;
    public bool tightExtras;
    public bool uaCellBoxes;
    public double borderWidth;
    public double pad;
    public Dictionary<string, Dictionary<string, string>> css = null!;
    public IReadOnlyDictionary<string, Dictionary<string, string>>? docCss;
    public List<CssChainRule>? chainRules;
    public List<CssElem>? cssAncestors;
    public List<byte[]>? inlineSvgs;
    public List<string> nestedHtml = null!;
    public Func<string, bool, Aspose.Pdf.Forms.RadioButtonOptionField>? makeRadio;
    public double availWidthPt;
    public double defaultCellFontPt;
    public Dictionary<string, string> tblStyle = null!;
    public bool docElementGrid;
    public bool pinnedBodyGrid;
    public bool authoredCellChrome;
    public bool chainBorderSeparate;
    public bool elemRuleBorder;
    public byte[]? cellImgBytes;
    public double ciw;
    public double cih;
}
}
