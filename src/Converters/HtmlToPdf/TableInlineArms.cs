using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>One arm of BuildTableFromHtml's token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void HandleInlineOpen(TableParseState ps, Token tok, string tag, HtmlLoadOptions? options, double cellFontSize, bool dwFormCells, bool widenProbe, bool redlineCells, List<CssElem>? chainBase, List<(string Tag, int PrevBoldDepth)> chainUnbold, bool ptCellWidths, bool bandDialect, string? cssRunFace, bool formGridDialect, bool liftNestedTables, Dictionary<string, Dictionary<string, string>> css, IReadOnlyDictionary<string, Dictionary<string, string>>? docCss, List<CssChainRule>? chainRules)
    {
        var io = new InlineOpenState();
        io.ps = ps;
        io.tok = tok;
        io.tag = tag;
        io.options = options;
        io.cellFontSize = cellFontSize;
        io.dwFormCells = dwFormCells;
        io.widenProbe = widenProbe;
        io.redlineCells = redlineCells;
        io.chainBase = chainBase;
        io.chainUnbold = chainUnbold;
        io.ptCellWidths = ptCellWidths;
        io.bandDialect = bandDialect;
        io.cssRunFace = cssRunFace;
        io.formGridDialect = formGridDialect;
        io.liftNestedTables = liftNestedTables;
        io.css = css;
        io.docCss = docCss;
        io.chainRules = chainRules;
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
    private static void HandleDivOpen(TableParseState ps, Token tok, string tag, HtmlLoadOptions? options, double cellFontSize, bool dwFormCells, bool widenProbe, bool redlineCells, List<CssElem>? chainBase, bool liftNestedTables, bool uaCellBoxes, double pad, IReadOnlyDictionary<string, Dictionary<string, string>>? docCss, List<CssChainRule>? chainRules, bool pinnedBodyGrid)
    {
        var dv = new DivOpenState();
        dv.ps = ps;
        dv.tok = tok;
        dv.tag = tag;
        dv.options = options;
        dv.cellFontSize = cellFontSize;
        dv.dwFormCells = dwFormCells;
        dv.widenProbe = widenProbe;
        dv.redlineCells = redlineCells;
        dv.chainBase = chainBase;
        dv.liftNestedTables = liftNestedTables;
        dv.uaCellBoxes = uaCellBoxes;
        dv.pad = pad;
        dv.docCss = docCss;
        dv.chainRules = chainRules;
        dv.pinnedBodyGrid = pinnedBodyGrid;
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
