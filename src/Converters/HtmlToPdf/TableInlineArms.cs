using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>One arm of BuildTableFromHtml's token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void HandleInlineOpen(TableParseState ps, TableColumnModel colModel, Table table, Token tok, string tag, HtmlLoadOptions? options, double cellFontSize, bool dwFormCells, bool fullWidthCjkMin, bool widenProbe, bool redlineCells, bool breakAnywhereDoc, bool cellFontShorthand, List<CssElem>? chainBase, double chainSpacingPt, List<(string Tag, int PrevBoldDepth)> chainUnbold, string? cssBaseFamily, double cssBasePt, string? defaultCellFace, double formGridStrutDropPt, bool hasBorder, double inlineFaceRatio, bool overDeclaredDraw, double padSide, bool uaDocGrid, bool uaSerifMin, bool ptCellWidths, bool bandDialect, double cellLineHeightPt, string? cssRunFace, bool formGridDialect, double formGridStrutPt, bool liftNestedTables, bool tightExtras, bool uaCellBoxes, double borderWidth, double pad, Dictionary<string, Dictionary<string, string>> css, IReadOnlyDictionary<string, Dictionary<string, string>>? docCss, List<CssChainRule>? chainRules, List<CssElem>? cssAncestors, List<byte[]>? inlineSvgs, List<string> nestedHtml, Func<string, bool, Aspose.Pdf.Forms.RadioButtonOptionField>? makeRadio, double availWidthPt, double defaultCellFontPt, Dictionary<string, string> tblStyle, bool docElementGrid, bool pinnedBodyGrid, bool authoredCellChrome, bool chainBorderSeparate, bool elemRuleBorder)
    {
        var io = new InlineOpenState();
        io.ps = ps;
        io.colModel = colModel;
        io.table = table;
        io.tok = tok;
        io.tag = tag;
        io.options = options;
        io.cellFontSize = cellFontSize;
        io.dwFormCells = dwFormCells;
        io.fullWidthCjkMin = fullWidthCjkMin;
        io.widenProbe = widenProbe;
        io.redlineCells = redlineCells;
        io.breakAnywhereDoc = breakAnywhereDoc;
        io.cellFontShorthand = cellFontShorthand;
        io.chainBase = chainBase;
        io.chainSpacingPt = chainSpacingPt;
        io.chainUnbold = chainUnbold;
        io.cssBaseFamily = cssBaseFamily;
        io.cssBasePt = cssBasePt;
        io.defaultCellFace = defaultCellFace;
        io.formGridStrutDropPt = formGridStrutDropPt;
        io.hasBorder = hasBorder;
        io.inlineFaceRatio = inlineFaceRatio;
        io.overDeclaredDraw = overDeclaredDraw;
        io.padSide = padSide;
        io.uaDocGrid = uaDocGrid;
        io.uaSerifMin = uaSerifMin;
        io.ptCellWidths = ptCellWidths;
        io.bandDialect = bandDialect;
        io.cellLineHeightPt = cellLineHeightPt;
        io.cssRunFace = cssRunFace;
        io.formGridDialect = formGridDialect;
        io.formGridStrutPt = formGridStrutPt;
        io.liftNestedTables = liftNestedTables;
        io.tightExtras = tightExtras;
        io.uaCellBoxes = uaCellBoxes;
        io.borderWidth = borderWidth;
        io.pad = pad;
        io.css = css;
        io.docCss = docCss;
        io.chainRules = chainRules;
        io.cssAncestors = cssAncestors;
        io.inlineSvgs = inlineSvgs;
        io.nestedHtml = nestedHtml;
        io.makeRadio = makeRadio;
        io.availWidthPt = availWidthPt;
        io.defaultCellFontPt = defaultCellFontPt;
        io.tblStyle = tblStyle;
        io.docElementGrid = docElementGrid;
        io.pinnedBodyGrid = pinnedBodyGrid;
        io.authoredCellChrome = authoredCellChrome;
        io.chainBorderSeparate = chainBorderSeparate;
        io.elemRuleBorder = elemRuleBorder;
        if (io.ps.cell is null) return;
        // NOTE: no keep here — the line flushed at a <p> OPEN is the
        // inter-tag residue before the paragraph (e.g. "<td> <p>"), not
        // paragraph content; keeping it would give every such cell a
        // phantom blank first line.
        if (io.tag == "p" && io.ps.line.Length > 0) PushLine(io.ps, io.redlineCells, io.dwFormCells, io.widenProbe, joinNext: true);
        io.prevPt = io.ps.curFontPt; var prevFamily = io.ps.curFamily;
        io.prevColor = io.ps.curColor;
        io.styleBold = false;
        io.styleItalic = false;
        // Chain-selector run styling (`.Title span.SmallerTitle`,
        // `.RiskCategory` pills): the class/inline handlers below win.
        ApplyChainSelector(io);
        // A stylesheet class named on the RUN itself ("<span
        // class='rteFontSize-5'>") sizes that run inside the cell's
        // paragraph. The run's own inline style still wins below.
        ApplyRunFace(io);
        ApplyInlineStyle(io);
        // The sheet's paragraph margin opens each paragraph below the line before it.
        if (io.tag == "p" && io.ps.sheetPMarginTopPt > 0 && io.ps.lineMarginTop <= 0) io.ps.lineMarginTop = io.ps.sheetPMarginTopPt;
        // (…after closing a paragraph still open - the HTML parser's own rule: the mailing's unclosed
        //  membership paragraph leaves its 1.5 em under itself before the tax-ID paragraph opens)
        if (io.tag == "p" && io.ps.uaCellBoxes) UaCloseParagraphForBlock(io.ps, io.redlineCells, io.dwFormCells, io.widenProbe);
        if (io.tag == "p" && io.ps.uaCellBoxes) UaOpenBlock(io.ps, "p", io.tok);
        if (io.ps.wordMailCells && io.tag == "p" && io.tok.Attributes is not null)
        {
            ApplyWordMailParagraphAlign(io);
            ReadWordMailLineHeight(io);
        }
        io.ps.styleStack.Add((io.tag, io.prevPt, prevFamily, io.styleBold, io.prevColor, io.styleItalic, PositionedInline(io.tok)));
        // Redline decorations: the span's text-decoration and
        // its diff-marker class's border-bottom scope ink runs
        // to the cell lines they cover.
        ApplyRedlineAttributes(io);
    }

    /// <summary>One arm of BuildTableFromHtml's token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void HandleDivOpen(TableParseState ps, TableColumnModel colModel, Table table, Token tok, string tag, HtmlLoadOptions? options, double cellFontSize, bool dwFormCells, bool fullWidthCjkMin, bool widenProbe, bool redlineCells, bool breakAnywhereDoc, bool cellFontShorthand, List<CssElem>? chainBase, double chainSpacingPt, List<(string Tag, int PrevBoldDepth)> chainUnbold, string? cssBaseFamily, double cssBasePt, string? defaultCellFace, double formGridStrutDropPt, bool hasBorder, double inlineFaceRatio, bool overDeclaredDraw, double padSide, bool uaDocGrid, bool uaSerifMin, bool ptCellWidths, bool bandDialect, double cellLineHeightPt, string? cssRunFace, bool formGridDialect, double formGridStrutPt, bool liftNestedTables, bool tightExtras, bool uaCellBoxes, double borderWidth, double pad, Dictionary<string, Dictionary<string, string>> css, IReadOnlyDictionary<string, Dictionary<string, string>>? docCss, List<CssChainRule>? chainRules, List<CssElem>? cssAncestors, List<byte[]>? inlineSvgs, List<string> nestedHtml, Func<string, bool, Aspose.Pdf.Forms.RadioButtonOptionField>? makeRadio, double availWidthPt, double defaultCellFontPt, Dictionary<string, string> tblStyle, bool docElementGrid, bool pinnedBodyGrid, bool authoredCellChrome, bool chainBorderSeparate, bool elemRuleBorder)
    {
        var dv = new DivOpenState();
        dv.ps = ps;
        dv.colModel = colModel;
        dv.table = table;
        dv.tok = tok;
        dv.tag = tag;
        dv.options = options;
        dv.cellFontSize = cellFontSize;
        dv.dwFormCells = dwFormCells;
        dv.fullWidthCjkMin = fullWidthCjkMin;
        dv.widenProbe = widenProbe;
        dv.redlineCells = redlineCells;
        dv.breakAnywhereDoc = breakAnywhereDoc;
        dv.cellFontShorthand = cellFontShorthand;
        dv.chainBase = chainBase;
        dv.chainSpacingPt = chainSpacingPt;
        dv.chainUnbold = chainUnbold;
        dv.cssBaseFamily = cssBaseFamily;
        dv.cssBasePt = cssBasePt;
        dv.defaultCellFace = defaultCellFace;
        dv.formGridStrutDropPt = formGridStrutDropPt;
        dv.hasBorder = hasBorder;
        dv.inlineFaceRatio = inlineFaceRatio;
        dv.overDeclaredDraw = overDeclaredDraw;
        dv.padSide = padSide;
        dv.uaDocGrid = uaDocGrid;
        dv.uaSerifMin = uaSerifMin;
        dv.ptCellWidths = ptCellWidths;
        dv.bandDialect = bandDialect;
        dv.cellLineHeightPt = cellLineHeightPt;
        dv.cssRunFace = cssRunFace;
        dv.formGridDialect = formGridDialect;
        dv.formGridStrutPt = formGridStrutPt;
        dv.liftNestedTables = liftNestedTables;
        dv.tightExtras = tightExtras;
        dv.uaCellBoxes = uaCellBoxes;
        dv.borderWidth = borderWidth;
        dv.pad = pad;
        dv.css = css;
        dv.docCss = docCss;
        dv.chainRules = chainRules;
        dv.cssAncestors = cssAncestors;
        dv.inlineSvgs = inlineSvgs;
        dv.nestedHtml = nestedHtml;
        dv.makeRadio = makeRadio;
        dv.availWidthPt = availWidthPt;
        dv.defaultCellFontPt = defaultCellFontPt;
        dv.tblStyle = tblStyle;
        dv.docElementGrid = docElementGrid;
        dv.pinnedBodyGrid = pinnedBodyGrid;
        dv.authoredCellChrome = authoredCellChrome;
        dv.chainBorderSeparate = chainBorderSeparate;
        dv.elemRuleBorder = elemRuleBorder;
        // Pinned-body report: the sheet's `div { padding: 4px }` boxes a
        // div INSIDE a cell too — its vertical pads grow the row the way
        // the pill row sits taller than its plain siblings.
        if (dv.pinnedBodyGrid && dv.ps.cell is not null && dv.docCss is not null
            && dv.docCss.TryGetValue("div", out var cdivR)
            && cdivR.TryGetValue("padding", out var cdivP)
            && TryParseLength(cdivP) is { } cdivPt && cdivPt > 0)
        {
            // The div's own box pads sit INSIDE the cell's padding —
            // they stack on it, they do not compete with it.
            dv.ps.cellChainPadTopPt = Math.Max(dv.ps.cellChainPadTopPt, dv.pad + cdivPt);
            dv.ps.cellChainPadBotPt = Math.Max(dv.ps.cellChainPadBotPt, dv.pad + cdivPt);
        }
        dv.divChainStyled = false;
        // Chain-selector block styling (`.Title > div` silver plates,
        // `.TrafficLight` boxes): fonts ride the styleStack exactly like
        // an inline span style; a styled box's fill is approximated as
        // the cell's own until block boxes render for real.
        ApplyDivChainSelector(dv);
        // A PLAIN div is a BLOCK box: it opens on a line of its own, so a
        // run of them stacks. `<div>IntroText</div>` eight times is eight
        // lines, not one — they were running together and the section that
        // holds them came out a single line tall.
        ApplyLiftedDivStyle(dv);
        // A fixed-width div inside a cell: its box sizes the column (the
        // content wraps inside it — see the CloseCell measurement). A
        // percent margin-left resolves against the div's own box:
        // x = W + p·x  ⇒  x = W / (1 − p).
        // A block inside a cell contributes its own box to the row. The WIDTH
        // half of that is content-box sizing (uaCellBoxes only); the HEIGHT is
        // plain CSS — a fixed-height block floors its row in any dialect.
        ApplyDivAttributes(dv);
    }
}
