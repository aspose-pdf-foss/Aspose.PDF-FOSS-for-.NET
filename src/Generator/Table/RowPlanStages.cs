using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public partial class Table : BaseParagraph
{
    /// <summary>The stages of the row plan: the line-height settle, the cell-totals fold and the blank-row height settle.</summary>
    private void SettleBlankRowHeight(RowPlanState rp, Row row)
    {
        if (rp.plan.MinBlankHeight <= 0)
            // A row with NO CELLS reserves nothing: it draws nothing and the generator
            // gives it no height (a `Rows.Add()` with only FixedRowHeight = 0 in front of
            // a fixed-height row leaves that row at the content top, not a line below it).
            rp.plan.MinBlankHeight = row.Cells.Count == 0 ? 0
                : rp.plan.LineCount != 0 ? 20
                : HonorCellFontFaces ? 0
                // redline grids: a hidden-content row collapses to the tight
                // spacer drawn for it (3.2 pt — measured net of the next
                // row's own billed margin-top)
                : RedlineCellSeat ? 3.2
                // A column-pagination slice: this row's text lives in another
                // slice; here it is one line of the row's cell font (the height
                // the row has where its text renders), not the generic blank slot.
                : ColumnSliceChild && row.Cells.Count > 0
                    ? ResolveFragmentDrawSize(row.Cells.At(0), row)
                // A generator cell with no paragraphs has no line at all: the row is
                // its padding (probed: 5 + 5 with a 0.5 rule, against 10 + 10 for a
                // cell holding an empty fragment).
                : GeneratorCellModel ? rp.maxVertPad
                : rp.plan.LineHeight;
        // A whitespace-only row (e.g. a " " spacer) is likewise a tight spacer drawn
        // without cell padding so it reserves just its line.
        // …except in a generator cell, where a " " or "" fragment is one ordinary line
        // at its own size plus the cell padding (probed: 10 + 5 + 5 with a 0.5 rule).
        // …but under CSS run boxes a row of empty cells is an ORDINARY row: the browser
        // gives it its cells' padding plus one line box (the invisible character those
        // cells hold is still a line), which is what draws its rules.
        // Under the lifted nested-table render, a row whose "blank" lines are
        // nested-table or image RESERVES is content, not a spacer — dropping its
        // padding would strip the cellspacing bands and jam the nested grid
        // against the row border. Legacy dialects keep the historical rule.
        // (the UA checkbox grid's rows are ordinary rows too: their invisible widgets hold a line box)
        rp.plan.IsBlankRow = !CssRunBoxes && !GeneratorCellModel && !HtmlUaControlGrid && rp.plan.CellInline is null && rp.plan.LineCount > 0
            && row.FixedRowHeight <= 0
            && !((NestedTableRender || GeneratorCellModel) && rp.plan.CellTables is not null)
            && System.Linq.Enumerable.All(rp.plan.CellLines,
                cl => System.Linq.Enumerable.All(cl,
                    l => string.IsNullOrWhiteSpace(l.Text) && !(NestedTableRender && l.ImgReserve)));
        // XML-generator dialect: an all-empty row is an
        // ORDINARY row — its cells' padding plus one line at the default cell
        // font size (5+10+5 = 20 for the padded report rows), never the tight
        // spacer or the 1.2-em default line.
        if (XmlGeneratorModel)
        {
            rp.plan.IsBlankRow = false;
            if (rp.plan.LineCount == 0)
            {
                var xmlFs = DefaultCellTextState?.FontSize > 0 ? (double)DefaultCellTextState.FontSize : 10.0;
                rp.plan.MinBlankHeight = Math.Max(rp.plan.MinBlankHeight, rp.maxVertPad + xmlFs + XmlLineSpacing);
            }
            // Every line advances by its OWN font size (a 14/10/14 pt paragraph
            // stack is 38 pt of content, not 3 × 14) — the row sizes to the exact
            // per-cell stacks, like the control-cell rule.
            else if (rp.maxOwnTotal > 0)
                rp.plan.ExactTotalH = rp.maxOwnTotal;
        }
    }

    /// <summary></summary>
    private void FoldRowCellTotals(RowPlanState rp)
    {
        rp.maxCellTotal = 0.0;
        rp.anyExactCell = false;
        rp.maxOwnTotal = 0.0;
        foreach (var (cpv, cn, ctight, cexact, cown) in rp.cellTotals)
        {
            var ch = cexact > 0 ? cexact : cn == 0 ? 0 : (cn - 1) * rp.plan.LineHeight + ctight;
            if (cexact > 0) rp.anyExactCell = true;
            if (cpv + ch > rp.maxCellTotal) rp.maxCellTotal = cpv + ch;
            if (cn > 0 && cpv + cown > rp.maxOwnTotal) rp.maxOwnTotal = cpv + cown;
        }
        // A row holding an exact-stack control cell sizes to the max over cells
        // of each cell's OWN stacked height (every text line at its own font
        // size, boxes at their box height) — the uniform grid would price a
        // 7pt side label at the row's 10pt pitch and oversize the row.
        rp.plan.ExactTotalH = rp.anyExactCell ? rp.maxOwnTotal : 0;
    }

    /// <summary></summary>
    private void SettleRowLineHeight(RowPlanState rp)
    {
        rp.plan.LineHeight = rp.maxLineHeight > 0 ? rp.maxLineHeight : DefaultLineHeightPt;
        rp.plan.TightLine = rp.tightForMax > 0 ? rp.tightForMax : rp.plan.LineHeight;
        // Band-doc tables (HonorCellFontFaces): unstyled text rows advance at the CSS
        // line box of their font size — round(pt·(4/3)·1.15)px·0.75, e.g. 9 pt for an
        // 8 pt line — not at the bare font size, which packs multi-line cells ~1 pt/line
        // tighter than a browser lays them out.
        // The lifted HTML render lays its text out on the same browser line box (its
        // 8 pt body columns pitch at 9 pt, line for line with a browser).
        // …and so does a grid the stylesheet styles: its 8 pt body columns pitch at 9,
        // line for line with a browser. A table the stylesheet never addresses
        // keeps the calibrated bare-em pitch — re-pitching it grows every row ~12 %
        // and walks the whole table down the page.
        if ((HonorCellFontFaces || (NestedTableRender && HtmlChainStyledCells))
            && rp.plan.CssContentH <= 0 && rp.maxLineHeight > 0)
        {
            rp.plan.LineHeight = CssLineBoxPt(rp.plan.LineHeight);
            rp.plan.TightLine = CssLineBoxPt(rp.plan.TightLine);
        }
        // An inline-styled grid pitches at ITS face's CSS line box (Verdana's
        // 2489/2048 em puts a 9 px cell on an 11 px = 8.25 pt line, the pitch the
        // reference rows step at).
        else if (InlineFaceGridRatio > 0 && rp.plan.CssContentH <= 0 && rp.maxLineHeight > 0)
        {
            rp.plan.LineHeight = FaceCssLineBoxPt(rp.plan.LineHeight, InlineFaceGridRatio);
            rp.plan.TightLine = FaceCssLineBoxPt(rp.plan.TightLine, InlineFaceGridRatio);
        }
        rp.rowContentH = rp.plan.LineCount == 0 ? 0.0
            : (rp.plan.LineCount - 1) * rp.plan.LineHeight + rp.plan.TightLine;
    }
}
