using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The closing tags the table structure leaves to the cell's own run: the emphasis and font tags, the anchors, the list items and the block elements the UA boxes close.</summary>
    private static void HandleCloseInlineAndBlockTag(TableStyleConfig cfg, TableParseState ps, TableColumnModel colModel, string tag)
    {
        if (tag is "strong" or "b")
        {
            if (ps.boldDepth > 0)
            {
                ps.boldDepth--;
                // Form-grid: the bold run CLOSES here - mark the boundary so
                // the tail of the line returns to the regular face.
                if ((cfg.formGridDialect || cfg.wordMailCells || (ps.uaCellBoxes)) && ps.lineRunMarks is not null)
                    ps.lineRunMarks.Add((ps.line.Length, ps.boldDepth > 0));
                if (cfg.widenProbe && ps.cell is not null) ps.line.Append('\uE001');
            }
        }
        else if (tag is "sup" or "sub")
        {
            // Probe: close a superscript run (measured at 85% of the line size).
            if (cfg.widenProbe && ps.cell is not null) ps.line.Append('\uE003');
        }
        else if (tag == "pre")
        {
            if (ps.preDepth > 0 && --ps.preDepth == 0 && ps.cell is not null)
            {
                if (!SheetPreWraps(cfg)) colModel.preMaxLinePt = Math.Max(colModel.preMaxLinePt, MeasureLine(ps, cfg.options, cfg.cellFontSize, cfg.dwFormCells, cfg.fullWidthCjkMin, cfg.widenProbe, ps.line.ToString(),
                    pt: ps.lineFontPt > 0 ? ps.lineFontPt : ps.curFontPt,
                    fam: ps.lineFamily ?? ps.curFamily));
                if (ps.line.Length > 0) PushLine(ps, cfg.redlineCells, cfg.dwFormCells, cfg.widenProbe);
                // restore the pre's own style push
                for (var k = ps.styleStack.Count - 1; k >= 0; k--)
                    if (ps.styleStack[k].Tag == "pre")
                    {
                        ps.curFontPt = ps.styleStack[k].PrevPt;
                        ps.curFamily = ps.styleStack[k].PrevFamily;
                        ps.curColor = ps.styleStack[k].PrevColor;
                        ps.styleStack.RemoveAt(k);
                        break;
                    }
            }
        }
        else if (tag is "p" or "span" or "font" or "label" or "div" or "h1" or "h2"
            || (ps.uaCellBoxes && tag is "i" or "em" or "h3" or "h4" or "h5" or "h6"))
        {
            if (tag == "p" && ps.uaCellBoxes)
            {
                if (ps.cell is not null && ps.line.Length > 0) PushLine(ps, cfg.redlineCells, cfg.dwFormCells, cfg.widenProbe);
                UaCloseBlock(ps, "p");
            }
            // A UA heading closes its block: its line, then its own bottom margin (in its size).
            if (tag is "h1" or "h2" or "h3" or "h4" or "h5" or "h6" && ps.uaCellBoxes && ps.cell is not null)
            {
                if (ps.line.Length > 0) PushLine(ps, cfg.redlineCells, cfg.dwFormCells, cfg.widenProbe);
                UaLeaveHeadingMargin(ps, tag, null, top: false);
                ps.uaLineAlign = null;
                ps.curLineHeightPct = 0;
            }
            CloseInlineOrBlockTag(cfg, ps, tag);
        }
    }
}
