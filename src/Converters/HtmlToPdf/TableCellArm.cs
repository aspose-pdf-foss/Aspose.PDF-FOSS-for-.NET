using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>One arm of BuildTableFromHtml's token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void HandleCellOpen(TableParseState ps, TableColumnModel colModel, Table table, Token tok, string tag, HtmlLoadOptions? options, double cellFontSize, bool dwFormCells, bool fullWidthCjkMin, bool widenProbe, bool redlineCells, bool breakAnywhereDoc, bool cellFontShorthand, List<CssElem>? chainBase, double chainSpacingPt, List<(string Tag, int PrevBoldDepth)> chainUnbold, string? cssBaseFamily, double cssBasePt, string? defaultCellFace, double formGridStrutDropPt, bool hasBorder, double inlineFaceRatio, bool overDeclaredDraw, double padSide, bool uaDocGrid, bool uaSerifMin, bool ptCellWidths, bool bandDialect, double cellLineHeightPt, string? cssRunFace, bool formGridDialect, double formGridStrutPt, bool liftNestedTables, bool tightExtras, bool uaCellBoxes, double borderWidth, double pad, Dictionary<string, Dictionary<string, string>> css, IReadOnlyDictionary<string, Dictionary<string, string>>? docCss, List<CssChainRule>? chainRules, List<CssElem>? cssAncestors, Dictionary<string, string> tblStyle, bool docElementGrid, bool pinnedBodyGrid, bool authoredCellChrome, bool chainBorderSeparate)
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
        co.tblStyle = tblStyle;
        co.docElementGrid = docElementGrid;
        co.pinnedBodyGrid = pinnedBodyGrid;
        co.authoredCellChrome = authoredCellChrome;
        co.chainBorderSeparate = chainBorderSeparate;
        if (co.ps.tableDepth > 1) return;   // nested cell: text flows into the host cell
        if (co.ps.cell is not null) CloseCell(co.ps, co.colModel, co.table, co.options, co.cellFontSize, co.dwFormCells, co.fullWidthCjkMin, co.breakAnywhereDoc, co.cellFontShorthand, co.chainBase, co.chainSpacingPt, co.chainUnbold, co.cssBaseFamily, co.cssBasePt, co.defaultCellFace, co.formGridStrutDropPt, co.hasBorder, co.inlineFaceRatio, co.overDeclaredDraw, co.padSide, co.uaDocGrid, co.widenProbe, co.uaSerifMin, co.ptCellWidths, co.redlineCells, co.bandDialect, co.cellLineHeightPt, co.cssRunFace, co.formGridDialect, co.formGridStrutPt, co.liftNestedTables, co.tightExtras, co.uaCellBoxes, co.borderWidth, co.pad);
        OpenCellAndReadAttributes(co);
        ApplyCellAlignmentAndChrome(co);
        ReadCellClassStyle(co);
        ResolveCellWidth(co);
        ReadLiftedCellPadding(co);
        ReadCellWidthAttributes(co);
        ReadCellStyleAttributes(co);
        ReadLiftedHostFrame(co);
        ApplyCellClassRules(co);
        if (co.ps.uaSheetGrid && co.chainRules is { Count: > 0 }) ApplyUaChainRules(co);
        ApplyChainCellRules(co);
        ApplyChainRowBackground(co);
        ReadCellOwnInlineStyle(co);
        ApplyCellCascadeBorders(co);
    }

    /// <summary>One arm of BuildTableFromHtml's token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void HandleImgOpen(TableParseState ps, Table table, Token tok, HtmlLoadOptions? options, double cellFontSize, bool dwFormCells, bool fullWidthCjkMin, bool widenProbe, bool redlineCells, bool cellFontShorthand, bool overDeclaredDraw, double padSide, bool liftNestedTables, double pad, List<byte[]>? inlineSvgs, bool wordMailCells = false, (double Fraction, bool Important, double LengthPt) sheetImgMax = default)
    {
        var ci = new CellImgState();
        ci.ps = ps;
        ci.table = table;
        ci.tok = tok;
        ci.options = options;
        ci.cellFontSize = cellFontSize;
        ci.dwFormCells = dwFormCells;
        ci.wordMailCells = wordMailCells;
        ci.fullWidthCjkMin = fullWidthCjkMin;
        ci.widenProbe = widenProbe;
        ci.redlineCells = redlineCells;
        ci.cellFontShorthand = cellFontShorthand;
        ci.overDeclaredDraw = overDeclaredDraw;
        ci.padSide = padSide;
        ci.liftNestedTables = liftNestedTables;
        ci.pad = pad;
        ci.inlineSvgs = inlineSvgs;
        // An image inside a cell (a logo, an inline-<svg> placeholder, an SVG
        // diagram) becomes an Image paragraph; the generator's cell renderer
        // rasterizes SVG sources and sizes unfixed images by the document rule.
        if (!(ci.ps.cell is not null && ci.tok.Attributes is not null
        && ci.tok.Attributes.TryGetValue("src", out var cellSrc) && !string.IsNullOrEmpty(cellSrc))) return;
        LoadCellImage(ci, cellSrc);
        // A picture capped by `max-width: N%` asks its column only for its width as a preference, never
        // as a floor: the column keeps its declared share and the picture shrinks into it (CSS). A picture
        // that does not load keeps its declared box as today: there is nothing to shrink. The picture's own
        // max-width, in whatever form (`none`, a length), overrides the sheet's rule; a length - its own or the
        // sheet's - caps its box.
        if (ci.cellImgBytes is { Length: > 0 })
        {
            if (OwnDeclarationWins(ci.tok.Attributes, "max-width", sheetImgMax.Important))
            {
                ci.maxFrac = CssPercentFraction(ci.tok.Attributes["style"], "max-width");
                ci.maxWidthPt = MaxWidthLengthPt(ci.tok.Attributes["style"]);
            }
            else (ci.maxFrac, ci.maxWidthPt) = (sheetImgMax.Fraction, sheetImgMax.LengthPt);
            CapBoxToMaxWidth(ci);
        }
        // (…unless the picture stands in a positioned wrapper: out of flow, it lends the cell nothing)
        if (ci.maxFrac > 0 && ci.liftNestedTables && ci.ciw > 0 && !InPositionedInline(ci.ps))
            ci.ps.cellImgMaxPt = Math.Max(ci.ps.cellImgMaxPt, ci.ciw * PxToPtW);
        // Form-document dialect: an unreachable image with explicit CSS
        // dimensions still occupies its box — the layout draws the
        // broken-image frame (a bordered white box with a torn-page
        // glyph) at the declared size instead of collapsing the cell.
        if (!PlaceCellImage(ci)) return;
    }

    /// <summary>A lifted cell's `border` shorthand, kept for the layout-table placement: it strokes the frame
    /// round a cell hosting a nested grid, whose own build never reads it.</summary>
    private static void ReadLiftedHostFrame(CellOpenState co)
    {
        if (co.liftNestedTables && co.tok.Attributes is not null
            && co.tok.Attributes.TryGetValue("style", out var st) && st is not null
            && TryParseBorderShorthand(st, "border") is (var w, var color))
            co.ps.cell!.HtmlHostFrame = new BorderInfo(BorderSide.Box, w, color ?? Color.Black);
    }

    /// <summary>A declaration's percentage as a fraction (`max-width: 100%` -> 1); 0 when the style
    /// declares the property in another unit or not at all.</summary>
    private static double CssPercentFraction(string? style, string prop)
    {
        if (style is null) return 0;
        var m = SafeRegex.Match(style, @"(?<![-\w])" + prop + @"\s*:\s*([\d.]+)\s*%", RegexOptions.IgnoreCase);
        return m.Success && double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var pct) ? pct / WholeWidthPercent : 0;
    }

    /// <summary>A declaration's `max-width` length in points; 0 when the style declares it as a percent, `none`
    /// or not at all.</summary>
    private static double MaxWidthLengthPt(string style)
    {
        var m = SafeRegex.Match(style, @"(?<![-\w])max-width\s*:\s*([^;!]+)", RegexOptions.IgnoreCase);
        return m.Success && TryParseLength(m.Groups[1].Value.Trim()) is { } capPt ? capPt : 0;
    }

    /// <summary>A `max-width` length caps the box the picture lays out at - its declared sides, a side it leaves
    /// open given by its file's aspect - at that width, its height by the same factor (CSS: the used width is
    /// the smaller of the two). A box sized as a share of its cell is capped once its column is solved.</summary>
    private static void CapBoxToMaxWidth(CellImgState ci)
    {
        var capPx = ci.maxWidthPt / PxToPt;
        if (capPx <= 0 || ci.ciwFrac > 0) return;
        var (w, h) = (ci.ciw, ci.cih);
        ci.boxBeforeCap = (w, h);
        if ((w <= 0) != (h <= 0) && TryReadImagePixelSize(ci.cellImgBytes!) is (var iw, var ih))
        {
            if (w <= 0) w = h * iw / ih;
            else h = w * ih / iw;
            // A lone side declared in pixels keeps the side the file's aspect gives whether or not the cap binds: alone it
            // would leave the picture at its file's own size, drawn as wide as its column lets it. A percent attribute is
            // no such side (its number is read as pixels): it keeps its lone side unless the cap binds.
            if (!ci.tok.Attributes!.TryGetValue(ci.ciw > 0 ? "width" : "height", out var side) || !side.Contains('%'))
                (ci.ciw, ci.cih) = (w, h);
        }
        if (w <= capPx) return;
        if (h > 0) ci.cih = h * capPx / w;
        ci.ciw = capPx;
    }

    /// <summary>The sheet's own `img { max-width }` rule - a percent as a fraction, else a length in points - and
    /// whether it is !important; 0 when the sheet caps no picture.</summary>
    private static (double Fraction, bool Important, double LengthPt) SheetImgMaxWidth(TableStyleConfig cfg)
    {
        foreach (var sheet in new[] { cfg.css, cfg.docCss })
        {
            if (sheet is null || !sheet.TryGetValue("img", out var rule) || !rule.TryGetValue("max-width", out var v)) continue;
            var frac = CssPercentFraction("max-width:" + v, "max-width");
            var lengthPt = frac > 0 ? 0 : MaxWidthLengthPt("max-width:" + v);
            if (frac > 0 || lengthPt > 0) return (frac, v.IndexOf("!important", StringComparison.OrdinalIgnoreCase) >= 0, lengthPt);
        }
        return (0, false, 0);
    }

    /// <summary>Whether an element's own style decides a property over the sheet's rule for it: it does when it
    /// declares the property at all, unless only the sheet's declaration is !important (the CSS cascade).</summary>
    private static bool OwnDeclarationWins(Dictionary<string, string>? attrs, string prop, bool sheetImportant)
        => DeclaresOwn(attrs, prop)
            && (!sheetImportant || SafeRegex.IsMatch(attrs!["style"], @"(?<![-\w])" + prop + @"\s*:[^;]*!important", RegexOptions.IgnoreCase));
}
