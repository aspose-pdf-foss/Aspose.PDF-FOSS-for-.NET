using System.Text;
using System.Text.RegularExpressions;
namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The block and cell tags of a table: the headings, the divs, the cells themselves and the tags that open a run inside one.</summary>
    private static void OpenTableBlockTag(TableStyleConfig cfg, TableParseState ps, TableColumnModel colModel, Table table, Token tok, string tag)
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
                OpenLineBreak(cfg, ps);
                break;
            case "img":
                HandleImgOpen(ps, table, tok, cfg.options, cfg.cellFontSize, cfg.dwFormCells, cfg.fullWidthCjkMin, cfg.widenProbe, cfg.redlineCells, cfg.cellFontShorthand, cfg.overDeclaredDraw, cfg.padSide, cfg.liftNestedTables, cfg.pad, cfg.inlineSvgs, cfg.wordMailCells, SheetImgMaxWidth(cfg));
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
                OpenGridFieldCapture(cfg, ps, tok, tag);
                break;
            case "option":
                OpenSelectOption(cfg, ps, tok);
                OpenGridFieldOption(ps, tok);
                break;
            case "textarea":
                OpenTextArea(cfg, ps, tok);
                OpenGridFieldCapture(cfg, ps, tok, tag);
                break;
            case "input":
                OpenInputControl(cfg, ps, table, tok);
                break;
        }
    }
}
