using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>One token of the table's markup: the tag it opens or closes, or the text it adds to the open cell.</summary>
    private static bool BuildTableFromToken(TableStyleConfig cfg, TableParseState ps, TableColumnModel colModel, Table table, Token tok, string? cssRunFace)
    {
        if (tok.Kind == TokenKind.Text) { HandleTextToken(cfg, ps, colModel, table, tok, cssRunFace); return true; }
        var tag = tok.Tag!.ToLowerInvariant();
        // display:none subtree inside a cell (hidden pager selects, state-carrier
        // inputs): its content never reaches the cell text.
        if (ps.hiddenSubDepth > 0)
        {
            if (tag == ps.hiddenSubTag)
            {
                if (tok.IsClose) { if (--ps.hiddenSubDepth == 0) ps.hiddenSubTag = null; }
                else if (!tok.IsSelfClosing) ps.hiddenSubDepth++;
            }
            return true;
        }
        if (!tok.IsClose && ps.cell is not null && IsHiddenElement(tag, tok.Attributes, cfg.css))
        {
            if (!tok.IsSelfClosing && !VoidTags.Contains(tag))
            {
                ps.hiddenSubTag = tag;
                ps.hiddenSubDepth = 1;
            }
            return true;
        }
        // Any structural tag cancels a pending htmlPage-container break; inline
        // style tags ride along inside the container.
        if (tag is not ("span" or "font" or "strong" or "b" or "em" or "i" or "u" or "a"))
            ps.htmlPageBreakPending = false;
        if (tag == "u")
        {
            if (tok.IsClose) ps.uDepth = Math.Max(0, ps.uDepth - 1);
            else if (!tok.IsSelfClosing) ps.uDepth++;
        }
        if (cfg.liftNestedTables && !tok.IsClose && tag == "span" && ps.cell is not null
            && ps.line.Length > 0 && tok.Attributes is not null
            && tok.Attributes.TryGetValue("class", out var hpClass)
            && string.Equals(hpClass?.Trim(), "htmlPage", StringComparison.OrdinalIgnoreCase))
            ps.htmlPageBreakPending = true;
        if (tok.IsClose) { HandleCloseTag(cfg, ps, colModel, table, tok, tag, cssRunFace); return true; }
        OpenTableTag(cfg, ps, colModel, table, tok, tag, cssRunFace);
        return true;
    }

    /// <summary>An opening tag inside the table: the row, cell, block and inline-run arms it opens.</summary>
    private static void OpenTableTag(TableStyleConfig cfg, TableParseState ps, TableColumnModel colModel, Table table, Token tok, string tag, string? cssRunFace)
    {
        switch (tag)
        {
            case "table":
                ps.tableDepth++;
                // A nested table's content opens on a fresh line of the host cell.
                if (ps.tableDepth > 1 && ps.cell is not null && ps.line.Length > 0) PushLine(ps, cfg.redlineCells, cfg.dwFormCells, cfg.widenProbe);
                break;
            case "tr":
                HandleRowOpen(cfg, ps, colModel, table, tok, tag, cssRunFace);
                break;
            case "tbody":
            case "thead":
            case "tfoot":
                if (ps.tableDepth <= 1 && _ancestorGridSheet) ps.sectionFontPt = RowGroupFontPt(tok);
                break;
            case "p":
            case "span":
            case "font":
            // A <label> is an ordinary inline box: the font-family/font-size it
            // declares style the run it wraps, exactly as a <span>'s would.
            case "label":
                HandleInlineOpen(ps, colModel, table, tok, tag, cfg.options, cfg.cellFontSize, cfg.dwFormCells, cfg.fullWidthCjkMin, cfg.widenProbe, cfg.redlineCells, cfg.breakAnywhereDoc, cfg.cellFontShorthand, cfg.chainBase, cfg.chainSpacingPt, cfg.chainUnbold, cfg.cssBaseFamily, cfg.cssBasePt, cfg.defaultCellFace, cfg.formGridStrutDropPt, cfg.hasBorder, cfg.inlineFaceRatio, cfg.overDeclaredDraw, cfg.padSide, cfg.uaDocGrid, cfg.uaSerifMin, cfg.ptCellWidths, cfg.bandDialect, cfg.cellLineHeightPt, cssRunFace, cfg.formGridDialect, cfg.formGridStrutPt, cfg.liftNestedTables, cfg.tightExtras, cfg.uaCellBoxes, cfg.borderWidth, cfg.pad, cfg.css, cfg.docCss, cfg.chainRules, cfg.cssAncestors, cfg.inlineSvgs, cfg.nestedHtml, cfg.makeRadio, cfg.availWidthPt, cfg.defaultCellFontPt, cfg.tblStyle, cfg.docElementGrid, cfg.pinnedBodyGrid, cfg.authoredCellChrome, cfg.chainBorderSeparate, cfg.elemRuleBorder);
                break;
            case "sup":
            case "sub":
                // Probe: open a superscript/subscript run — its glyphs measure at
                // 85% of the line size in the min-content pass (the filing-dialect
                // CSS shrink), marked by a sentinel pair in the line buffer.
                if (cfg.widenProbe && ps.cell is not null) ps.line.Append('\uE002');
                break;
            case "h1":
            case "h2":
                OpenHeadingRun(cfg, ps, tok, tag);
                break;
            // (the lower headings are blocks of a UA-boxed cell too: their own line, size and margins)
            case "h3":
            case "h4":
            case "h5":
            case "h6":
                if (cfg.uaCellBoxes && ps.cell is not null) OpenHeadingRun(cfg, ps, tok, tag);
                break;
            case "div":
                HandleDivOpen(ps, colModel, table, tok, tag, cfg.options, cfg.cellFontSize, cfg.dwFormCells, cfg.fullWidthCjkMin, cfg.widenProbe, cfg.redlineCells, cfg.breakAnywhereDoc, cfg.cellFontShorthand, cfg.chainBase, cfg.chainSpacingPt, cfg.chainUnbold, cfg.cssBaseFamily, cfg.cssBasePt, cfg.defaultCellFace, cfg.formGridStrutDropPt, cfg.hasBorder, cfg.inlineFaceRatio, cfg.overDeclaredDraw, cfg.padSide, cfg.uaDocGrid, cfg.uaSerifMin, cfg.ptCellWidths, cfg.bandDialect, cfg.cellLineHeightPt, cssRunFace, cfg.formGridDialect, cfg.formGridStrutPt, cfg.liftNestedTables, cfg.tightExtras, cfg.uaCellBoxes, cfg.borderWidth, cfg.pad, cfg.css, cfg.docCss, cfg.chainRules, cfg.cssAncestors, cfg.inlineSvgs, cfg.nestedHtml, cfg.makeRadio, cfg.availWidthPt, cfg.defaultCellFontPt, cfg.tblStyle, cfg.docElementGrid, cfg.pinnedBodyGrid, cfg.authoredCellChrome, cfg.chainBorderSeparate, cfg.elemRuleBorder);
                break;
            case "td":
            case "th":
                HandleCellOpen(ps, colModel, table, tok, tag, cfg.options, cfg.cellFontSize, cfg.dwFormCells, cfg.fullWidthCjkMin, cfg.widenProbe, cfg.redlineCells, cfg.breakAnywhereDoc, cfg.cellFontShorthand, cfg.chainBase, cfg.chainSpacingPt, cfg.chainUnbold, cfg.cssBaseFamily, cfg.cssBasePt, cfg.defaultCellFace, cfg.formGridStrutDropPt, cfg.hasBorder, cfg.inlineFaceRatio, cfg.overDeclaredDraw, cfg.padSide, cfg.uaDocGrid, cfg.uaSerifMin, cfg.ptCellWidths, cfg.bandDialect, cfg.cellLineHeightPt, cssRunFace, cfg.formGridDialect, cfg.formGridStrutPt, cfg.liftNestedTables, cfg.tightExtras, cfg.uaCellBoxes, cfg.borderWidth, cfg.pad, cfg.css, cfg.docCss, cfg.chainRules, cfg.cssAncestors, cfg.inlineSvgs, cfg.nestedHtml, cfg.makeRadio, cfg.availWidthPt, cfg.defaultCellFontPt, cfg.tblStyle, cfg.docElementGrid, cfg.pinnedBodyGrid, cfg.authoredCellChrome, cfg.chainBorderSeparate, cfg.elemRuleBorder);
                break;
            case "a":
                OpenAnchorRun(cfg, ps, tok);
                break;
            case "strong":
            case "b":
                OpenBoldRun(cfg, ps);
                break;
            case "i":
            default: OpenTableBlockTag(cfg, ps, colModel, table, tok, tag, cssRunFace); break;
        }
    }
}
