using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>One arm of BuildTableFromHtml's token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void HandleCellOpen(TableParseState ps, TableColumnModel colModel, Table table, Token tok, string tag, HtmlLoadOptions? options, double cellFontSize, bool dwFormCells, bool fullWidthCjkMin, bool widenProbe, bool redlineCells, bool breakAnywhereDoc, bool cellFontShorthand, List<CssElem>? chainBase, double chainSpacingPt, List<(string Tag, int PrevBoldDepth)> chainUnbold, string? cssBaseFamily, double cssBasePt, string? defaultCellFace, double formGridStrutDropPt, bool hasBorder, double inlineFaceRatio, bool overDeclaredDraw, double padSide, bool uaDocGrid, bool uaSerifMin, bool ptCellWidths, bool bandDialect, double cellLineHeightPt, string? cssRunFace, bool formGridDialect, double formGridStrutPt, bool liftNestedTables, bool tightExtras, bool uaCellBoxes, double borderWidth, double pad, Dictionary<string, Dictionary<string, string>> css, IReadOnlyDictionary<string, Dictionary<string, string>>? docCss, List<CssChainRule>? chainRules, List<CssElem>? cssAncestors, List<byte[]>? inlineSvgs, List<string> nestedHtml, Func<string, bool, Aspose.Pdf.Forms.RadioButtonOptionField>? makeRadio, double availWidthPt, double defaultCellFontPt, Dictionary<string, string> tblStyle, bool docElementGrid, bool pinnedBodyGrid, bool authoredCellChrome, bool chainBorderSeparate, bool elemRuleBorder)
    {
        var co = new CellOpenState();
        co.ps = ps;
        co.colModel = colModel;
        co.table = table;
        co.tok = tok;
        co.tag = tag;
        co.options = options;
        co.cellFontSize = cellFontSize;
        co.dwFormCells = dwFormCells;
        co.fullWidthCjkMin = fullWidthCjkMin;
        co.widenProbe = widenProbe;
        co.redlineCells = redlineCells;
        co.breakAnywhereDoc = breakAnywhereDoc;
        co.cellFontShorthand = cellFontShorthand;
        co.chainBase = chainBase;
        co.chainSpacingPt = chainSpacingPt;
        co.chainUnbold = chainUnbold;
        co.cssBaseFamily = cssBaseFamily;
        co.cssBasePt = cssBasePt;
        co.defaultCellFace = defaultCellFace;
        co.formGridStrutDropPt = formGridStrutDropPt;
        co.hasBorder = hasBorder;
        co.inlineFaceRatio = inlineFaceRatio;
        co.overDeclaredDraw = overDeclaredDraw;
        co.padSide = padSide;
        co.uaDocGrid = uaDocGrid;
        co.uaSerifMin = uaSerifMin;
        co.ptCellWidths = ptCellWidths;
        co.bandDialect = bandDialect;
        co.cellLineHeightPt = cellLineHeightPt;
        co.cssRunFace = cssRunFace;
        co.formGridDialect = formGridDialect;
        co.formGridStrutPt = formGridStrutPt;
        co.liftNestedTables = liftNestedTables;
        co.tightExtras = tightExtras;
        co.uaCellBoxes = uaCellBoxes;
        co.borderWidth = borderWidth;
        co.pad = pad;
        co.css = css;
        co.docCss = docCss;
        co.chainRules = chainRules;
        co.cssAncestors = cssAncestors;
        co.inlineSvgs = inlineSvgs;
        co.nestedHtml = nestedHtml;
        co.makeRadio = makeRadio;
        co.availWidthPt = availWidthPt;
        co.defaultCellFontPt = defaultCellFontPt;
        co.tblStyle = tblStyle;
        co.docElementGrid = docElementGrid;
        co.pinnedBodyGrid = pinnedBodyGrid;
        co.authoredCellChrome = authoredCellChrome;
        co.chainBorderSeparate = chainBorderSeparate;
        co.elemRuleBorder = elemRuleBorder;
        if (co.ps.tableDepth > 1) return;   // nested cell: text flows into the host cell
        if (co.ps.cell is not null) CloseCell(co.ps, co.colModel, co.table, co.options, co.cellFontSize, co.dwFormCells, co.fullWidthCjkMin, co.breakAnywhereDoc, co.cellFontShorthand, co.chainBase, co.chainSpacingPt, co.chainUnbold, co.cssBaseFamily, co.cssBasePt, co.defaultCellFace, co.formGridStrutDropPt, co.hasBorder, co.inlineFaceRatio, co.overDeclaredDraw, co.padSide, co.uaDocGrid, co.widenProbe, co.uaSerifMin, co.ptCellWidths, co.redlineCells, co.bandDialect, co.cellLineHeightPt, co.cssRunFace, co.formGridDialect, co.formGridStrutPt, co.liftNestedTables, co.tightExtras, co.uaCellBoxes, co.borderWidth, co.pad);
        OpenCellAndReadAttributes(co);
        ApplyCellAlignmentAndChrome(co);
        ReadCellClassStyle(co);
        ResolveCellWidth(co);
        ReadLiftedCellPadding(co);
        ReadCellWidthAttributes(co);
        ReadCellStyleAttributes(co);
        ApplyCellClassRules(co);
        if (co.ps.uaSheetGrid && co.chainRules is { Count: > 0 }) ApplyUaChainRules(co);
        ApplyChainCellRules(co);
        ReadCellOwnInlineStyle(co);
    }

    /// <summary>One arm of BuildTableFromHtml's token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void HandleImgOpen(TableParseState ps, TableColumnModel colModel, Table table, Token tok, string tag, HtmlLoadOptions? options, double cellFontSize, bool dwFormCells, bool fullWidthCjkMin, bool widenProbe, bool redlineCells, bool breakAnywhereDoc, bool cellFontShorthand, List<CssElem>? chainBase, double chainSpacingPt, List<(string Tag, int PrevBoldDepth)> chainUnbold, string? cssBaseFamily, double cssBasePt, string? defaultCellFace, double formGridStrutDropPt, bool hasBorder, double inlineFaceRatio, bool overDeclaredDraw, double padSide, bool uaDocGrid, bool uaSerifMin, bool ptCellWidths, bool bandDialect, double cellLineHeightPt, string? cssRunFace, bool formGridDialect, double formGridStrutPt, bool liftNestedTables, bool tightExtras, bool uaCellBoxes, double borderWidth, double pad, Dictionary<string, Dictionary<string, string>> css, IReadOnlyDictionary<string, Dictionary<string, string>>? docCss, List<CssChainRule>? chainRules, List<CssElem>? cssAncestors, List<byte[]>? inlineSvgs, List<string> nestedHtml, Func<string, bool, Aspose.Pdf.Forms.RadioButtonOptionField>? makeRadio, double availWidthPt, double defaultCellFontPt, Dictionary<string, string> tblStyle, bool docElementGrid, bool pinnedBodyGrid, bool authoredCellChrome, bool chainBorderSeparate, bool elemRuleBorder, bool wordMailCells = false)
    {
        var ci = new CellImgState();
        ci.ps = ps;
        ci.colModel = colModel;
        ci.table = table;
        ci.tok = tok;
        ci.tag = tag;
        ci.options = options;
        ci.cellFontSize = cellFontSize;
        ci.dwFormCells = dwFormCells;
        ci.wordMailCells = wordMailCells;
        ci.fullWidthCjkMin = fullWidthCjkMin;
        ci.widenProbe = widenProbe;
        ci.redlineCells = redlineCells;
        ci.breakAnywhereDoc = breakAnywhereDoc;
        ci.cellFontShorthand = cellFontShorthand;
        ci.chainBase = chainBase;
        ci.chainSpacingPt = chainSpacingPt;
        ci.chainUnbold = chainUnbold;
        ci.cssBaseFamily = cssBaseFamily;
        ci.cssBasePt = cssBasePt;
        ci.defaultCellFace = defaultCellFace;
        ci.formGridStrutDropPt = formGridStrutDropPt;
        ci.hasBorder = hasBorder;
        ci.inlineFaceRatio = inlineFaceRatio;
        ci.overDeclaredDraw = overDeclaredDraw;
        ci.padSide = padSide;
        ci.uaDocGrid = uaDocGrid;
        ci.uaSerifMin = uaSerifMin;
        ci.ptCellWidths = ptCellWidths;
        ci.bandDialect = bandDialect;
        ci.cellLineHeightPt = cellLineHeightPt;
        ci.cssRunFace = cssRunFace;
        ci.formGridDialect = formGridDialect;
        ci.formGridStrutPt = formGridStrutPt;
        ci.liftNestedTables = liftNestedTables;
        ci.tightExtras = tightExtras;
        ci.uaCellBoxes = uaCellBoxes;
        ci.borderWidth = borderWidth;
        ci.pad = pad;
        ci.css = css;
        ci.docCss = docCss;
        ci.chainRules = chainRules;
        ci.cssAncestors = cssAncestors;
        ci.inlineSvgs = inlineSvgs;
        ci.nestedHtml = nestedHtml;
        ci.makeRadio = makeRadio;
        ci.availWidthPt = availWidthPt;
        ci.defaultCellFontPt = defaultCellFontPt;
        ci.tblStyle = tblStyle;
        ci.docElementGrid = docElementGrid;
        ci.pinnedBodyGrid = pinnedBodyGrid;
        ci.authoredCellChrome = authoredCellChrome;
        ci.chainBorderSeparate = chainBorderSeparate;
        ci.elemRuleBorder = elemRuleBorder;
        // An image inside a cell (a logo, an inline-<svg> placeholder, an SVG
        // diagram) becomes an Image paragraph; the generator's cell renderer
        // rasterizes SVG sources and sizes unfixed images by the document rule.
        if (!(ci.ps.cell is not null && ci.tok.Attributes is not null
        && ci.tok.Attributes.TryGetValue("src", out var cellSrc) && !string.IsNullOrEmpty(cellSrc))) return;
        LoadCellImage(ci, cellSrc);
        // Form-document dialect: an unreachable image with explicit CSS
        // dimensions still occupies its box — the layout draws the
        // broken-image frame (a bordered white box with a torn-page
        // glyph) at the declared size instead of collapsing the cell.
        if (!PlaceCellImage(ci)) return;
    }
}
