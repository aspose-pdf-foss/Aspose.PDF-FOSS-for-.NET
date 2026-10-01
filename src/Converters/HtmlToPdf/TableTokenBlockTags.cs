using System.Text;
using System.Text.RegularExpressions;
namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The block and cell tags of a table: the headings, the divs, the cells themselves and the tags that open a run inside one.</summary>
    private static void OpenTableBlockTag(TableStyleConfig cfg, TableParseState ps, TableColumnModel colModel, Table table, Token tok, string tag, string? cssRunFace)
    {
        switch (tag)
        {
            case "em":
                // A UA-boxed cell's emphasis is an italic run, restored at its close.
                if (ps.uaCellBoxes && ps.cell is not null && !tok.IsSelfClosing)
                {
                    ps.styleStack.Add((tag, ps.curFontPt, ps.curFamily, false, ps.curColor, true, false));
                    ps.italicDepth++;
                }
                break;
            case "hr":
                if (ps.cell is not null && ps.uaCellBoxes)
                {
                    // the UA rule in place: 0.5em margins round a 1.5 pt line box, collapsing
                    if (ps.line.Length > 0) PushLine(ps, cfg.redlineCells, cfg.dwFormCells, cfg.widenProbe);
                    UaLeaveBlockMargin(ps, UaHrMarginEm);
                    ps.lineFontPt = UaHrRuleFontPt; ps.lineFamily = ps.curFamily; ps.lineStyleSet = true;
                    ps.uaHrRulePending = true;
                    PushLine(ps, cfg.redlineCells, cfg.dwFormCells, cfg.widenProbe, keepIfBlank: true);
                    UaLeaveBlockMargin(ps, UaHrMarginEm);
                }
                else if (ps.cell is not null)
                {
                    if (ps.line.Length > 0) PushLine(ps, cfg.redlineCells, cfg.dwFormCells, cfg.widenProbe);
                    if (ps.row is not null) (colModel.hrCells ??= new()).Add((ps.row, ps.cell));
                }
                break;
            case "pre":
                OpenPreformattedBlock(cfg, ps, colModel, tok);
                break;
            case "br":
                OpenLineBreak(cfg, ps, table);
                break;
            case "img":
                HandleImgOpen(ps, colModel, table, tok, tag, cfg.options, cfg.cellFontSize, cfg.dwFormCells, cfg.fullWidthCjkMin, cfg.widenProbe, cfg.redlineCells, cfg.breakAnywhereDoc, cfg.cellFontShorthand, cfg.chainBase, cfg.chainSpacingPt, cfg.chainUnbold, cfg.cssBaseFamily, cfg.cssBasePt, cfg.defaultCellFace, cfg.formGridStrutDropPt, cfg.hasBorder, cfg.inlineFaceRatio, cfg.overDeclaredDraw, cfg.padSide, cfg.uaDocGrid, cfg.uaSerifMin, cfg.ptCellWidths, cfg.bandDialect, cfg.cellLineHeightPt, cssRunFace, cfg.formGridDialect, cfg.formGridStrutPt, cfg.liftNestedTables, cfg.tightExtras, cfg.uaCellBoxes, cfg.borderWidth, cfg.pad, cfg.css, cfg.docCss, cfg.chainRules, cfg.cssAncestors, cfg.inlineSvgs, cfg.nestedHtml, cfg.makeRadio, cfg.availWidthPt, cfg.defaultCellFontPt, cfg.tblStyle, cfg.docElementGrid, cfg.pinnedBodyGrid, cfg.authoredCellChrome, cfg.chainBorderSeparate, cfg.elemRuleBorder, cfg.wordMailCells);
                break;
            case "ol":
            case "ul":
                OpenListBlock(cfg, ps, tok, tag);
                break;
            case "li":
                OpenListItem(cfg, ps);
                break;
            case "select":
                if (cfg.dwFormCells && ps.cell is not null && !tok.IsClose)
                { ps.dwSelectDepth = 1; ps.dwSelectedOpt = null; ps.dwFirstOpt = null; ps.dwSawSelected = false; }
                break;
            case "option":
                OpenSelectOption(cfg, ps, tok);
                break;
            case "textarea":
                OpenTextArea(cfg, ps, tok);
                break;
            case "input":
                OpenInputControl(cfg, ps, table, tok);
                break;
        }
    }
}
