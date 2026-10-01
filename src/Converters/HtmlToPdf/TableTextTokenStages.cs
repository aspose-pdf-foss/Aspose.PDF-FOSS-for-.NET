using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Table text token: the token's text appended to the cell's line.</summary>
    private static void AppendTextTokenToLine(TableStyleConfig cfg, TableParseState ps, TableColumnModel colModel, Token tok, string? cssRunFace)
    {
        if (AppendPreformattedText(cfg, ps, colModel, tok)) return;
        // Under `white-space: pre-wrap` the newline that follows the opening
        // tag is CONTENT, so the box's first line box is empty and the text
        // starts on the second. Collapsing whitespace would eat it.
        if (ps.preWrapPending)
        {
            ps.preWrapPending = false;
            if (tok.Value.StartsWith("\n") || tok.Value.StartsWith("\r"))
                PushLine(ps, cfg.redlineCells, cfg.dwFormCells, cfg.widenProbe, keepIfBlank: true);
        }
        // First visible content of an htmlPage container span (an &nbsp;
        // counts — it holds a line box) starts on a fresh line.
        if (ps.htmlPageBreakPending)
        {
            var hpVis = false;
            foreach (var hpC in DecodeEntities(tok.Value))
                if (!char.IsWhiteSpace(hpC) || hpC == ' ') { hpVis = true; break; }
            if (hpVis)
            {
                ps.htmlPageBreakPending = false;
                // A pending line that is itself only whitespace/&nbsp;
                // COLLAPSES at the container boundary instead of pushing —
                // only an explicit <br> materialises a whitespace box.
                if (IsAllWhitespace(ps.line)) ps.line.Clear();
                else if (ps.line.Length > 0) PushLine(ps, cfg.redlineCells, cfg.dwFormCells, cfg.widenProbe);
            }
        }
        // A run whose size differs from the one this line is already bound to
        // opens its OWN line box: the cell paragraph becomes a stack of
        // same-size runs, each wrapped and pitched on its own size. (A browser
        // reflows a mixed-size paragraph continuously; the sizes change on run
        // boundaries, which is where its lines break anyway.)
        if ((cssRunFace is not null || cfg.chainBase is not null) && ps.lineStyleSet
            && !string.IsNullOrWhiteSpace(tok.Value)
            && EffectiveChainDisplay(ps) != "inline-block"
            && Math.Abs((ps.curFontPt > 0 ? ps.curFontPt : cfg.cellFontSize)
                - (ps.lineFontPt > 0 ? ps.lineFontPt : cfg.cellFontSize)) > 0.01)
        {
            // …unless all the line holds so far is zero-width spaces. Those are
            // invisible and carry no advance, so they are not a line of their
            // own: they ride along on the run that follows, and the size change
            // simply REBINDS this line instead of closing it. (A cell opening
            // "&#8203;<span class=…>" must not spend a whole line box on it.)
            if (ps.line.ToString().Trim(ZeroWidthSpace).Trim().Length == 0)
                ps.lineStyleSet = false;
            else PushLine(ps, cfg.redlineCells, cfg.dwFormCells, cfg.widenProbe);
        }
        // Bind the currently active inline style to the line when its first
        // real text arrives, so a later close tag can't restyle it. In the
        // form-grid dialect a sized &nbsp; is real content too — the grid's
        // spacer row takes its declared 36pt line box (U+00A0 classes as
        // whitespace, so the plain test skips it).
        // DataWorks: option/textarea inner text is captured for the
        // control box, never flowed as cell text.
        if (cfg.dwFormCells && ps.dwSelectDepth > 0)
        {
            if (ps.dwOptSelected) ps.dwOptBuf.Append(DecodeEntities(tok.Value));
            return;
        }
        if (cfg.dwFormCells && ps.dwTextareaOpen)
        {
            ps.dwTaBuf.Append(DecodeEntities(tok.Value));
            return;
        }
        if (!ps.lineStyleSet && (!string.IsNullOrWhiteSpace(tok.Value)
                || (cfg.formGridDialect
                    && tok.Value.IndexOf("&nbsp;", StringComparison.OrdinalIgnoreCase) >= 0)))
        { ps.lineFontPt = ps.curFontPt; ps.lineFamily = ps.curFamily; ps.lineStyleSet = true; }
        // DataWorks cells scope a span's colour to ITS OWN text run
        // (the red validation star beside black button captions) —
        // the first-colored-token-wins line colour would paint the
        // whole control line.
        if (ps.lineColor is null && !cfg.dwFormCells) ps.lineColor = ps.curColor;
        if (!string.IsNullOrWhiteSpace(tok.Value))
        {
            ps.lineHadText = true;
            ps.cellPendingBrBlank = false;
            if (ps.uaCellBoxes) ps.lineMaxRunPt = Math.Max(ps.lineMaxRunPt, ps.curFontPt > 0 ? ps.curFontPt : ps.uaBaseFontPt);
            if (ps.boldDepth == 0) ps.lineAllBold = false;
            if (ps.italicDepth == 0) ps.lineAllItalic = false;
        }
        var dwAppendText = DecodeEntities(tok.Value);
        if (ps.uaCellBoxes && (ps.cellUpper || ps.uaUpperDepths.Count > 0)) dwAppendText = dwAppendText.ToUpperInvariant();
        if (cfg.dwFormCells && ps.curColor is { } dwRunCol && dwAppendText.Length > 0
            && !string.IsNullOrWhiteSpace(dwAppendText))
            (ps.lineColorRuns ??= new List<(int S, int L, Color C)>())
                .Add((ps.line.Length, dwAppendText.Length, dwRunCol));
        ps.line.Append(dwAppendText);
    }

    /// <summary>Table text token: a nested-table marker in the text absorbed into the cell.</summary>
    private static void AbsorbNestedTableMark(TableStyleConfig cfg, TableParseState ps, Table table, Token tok)
    {
        foreach (Match nm in Regex.Matches(tok.Value, Regex.Escape(NestedMark) + @"(\d+)\]"))
        {
            var ni = int.Parse(nm.Groups[1].Value);
            if (ni < 0 || ni >= cfg.nestedHtml.Count) continue;
            // A UA-boxed cell's pending block margin seats above a nested grid as above a line (the mailing's
            // h4 heading stands its 1.12 em over the itemized grid).
            if (cfg.uaCellBoxes && ps.cell is not null && ps.uaPendingMarginPt > 0 && UaBlankLine(ps.line))
            {
                ps.line.Clear();
                ps.lineFontPt = UaMarginOnlyFontPt; ps.lineFamily = ps.curFamily; ps.lineStyleSet = true;
                PushLine(ps, cfg.redlineCells, cfg.dwFormCells, cfg.widenProbe, keepIfBlank: true);
            }
            // A nested grid's box is what is LEFT of its host's: the host grid's own frame and the
            // columns its row has already closed are spent, and a  grid fills the rest.
            // Measuring it against the whole outer box makes every percent column a share of width the
            // grid does not have, and a nest that has been squeezed to its min-content draws nowhere near
            // where the reference draws it.
            var hostBoxW = cfg.availWidthPt;
            if (ps.sheetTdBoxRule)
            {
                var hostContentW = hostBoxW - TableFrameSidesPt(table);
                // What the row has already spent: its closed cells' floors, or - where those cells
                // DECLARED a percent - the share of the box those percents claim, whichever is more.
                var spent = ps.rowMinSum;
                if (ps.rowPctSum > 0) spent = Math.Max(spent, ps.rowPctSum / 100.0 * hostContentW);
                hostBoxW = Math.Max(0, hostContentW - spent);
            }
            var innerAvailW = (ps.cellWidthPt > 0 ? ps.cellWidthPt : hostBoxW) - ps.liStandingIndentPt;
            var (inner, innerNatW) = BuildTableFromHtml(cfg.nestedHtml[ni], innerAvailW,
                cfg.options, cfg.inlineSvgs,
                cfg.docCss ?? cfg.css, cfg.bandDialect, false, cfg.cellLineHeightPt,
                // (a UA-boxed nested grid inherits the size running in its host cell - the remittance form's
                //  `style="font-size: 12px"` container types every grid inside it at 9)
                cfg.uaCellBoxes ? (ps.curFontPt > 0 ? ps.curFontPt : ps.cellClassPt > 0 ? ps.cellClassPt : cfg.cellFontSize) : cfg.defaultCellFontPt,
                cfg.tightExtras,
                liftNestedTables: true,
                ptCellWidths: cfg.ptCellWidths,
                // The probe's measuring model must survive the nesting: a
                // grid measured with legacy half-em CJK and no ideograph
                // breaks reports artifact floors, and those floors — not
                // the document's real widest content — size the page.
                fullWidthCjkMin: cfg.fullWidthCjkMin,
                overDeclaredDraw: cfg.overDeclaredDraw,
                // The inner grid inherits this cell's ancestor chain, so
                // tree-addressed rules keep matching through the nesting.
                chainRules: cfg.chainRules,
                // (a UA-boxed grid hands its nested grids the chain through its own table and cell elements)
                cssAncestors: cfg.uaCellBoxes ? UaCellChain(cfg.cssAncestors, ps) : cfg.chainBase is null ? null : BuildOpenChain(ps, cfg.chainBase),
                makeRadio: cfg.makeRadio,
                // The DataWorks form dialect survives the nesting: an inner
                // results grid keeps the serif cell face and control boxes.
                dwFormCells: cfg.dwFormCells,
                wordMailCells: cfg.wordMailCells,
                // A UA-boxed grid's nested grids inherit its face and its browser line
                // boxes: the inner rows pitch and dress like the outer ones.
                uaCellBoxes: cfg.uaCellBoxes,
                defaultCellFace: cfg.dwFormCells || (cfg.uaCellBoxes) ? cfg.defaultCellFace : null,
                // (…and the body face and line factor the sheet gave the host)
                cssRunFace: cfg.uaBodyFace, uaLineFactor: cfg.uaLineFactor, uaSheetGrid: cfg.uaSheetGrid,
                nestedGrid: true);
            // (a UA-boxed grid whose rows hold no cell at all - the mailing's empty sponsor table - is no box: the
            //  browser gives it no height, so it joins nothing)
            if (inner is not null && cfg.uaCellBoxes && inner.Rows.Count > 0 && UaGridHasNoCells(inner)) inner = null;
            if (inner is not null)
            {
                SeatNestedTableInCellFlow(cfg, ps, table, inner, ni);
                FoldNestedTableWidthsIntoCell(cfg, ps, inner, innerNatW, innerAvailW);
            }
        }
    }
}
