using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>Lays one table row out: every cell's lines wrapped into its column width (blank runs, radio and check glyphs, text pieces), the row height as the tallest cell floored by the declared minimum.</summary>
    private static void LayoutStepTableRow(PsTableLayoutState pl, List<List<Converters.HtmlToPdfConverter.StepLine>> row)
    {
        pl.rowH = 0.0;
        pl.colX = pl.psTblX + PsCellGap(pl.pt);
        pl.cellsOut = new List<(List<double>, List<(int, double, Converters.HtmlToPdfConverter.StepSeg?, string?)>)>();
        for (var c = 0; c < row.Count; c++)
        {
            LayoutStepTableCell(pl, row, c);
        }
        pl.rowFloor = pl.laidRows.Count < pl.pt.RowMinPt.Count
            ? pl.pt.RowMinPt[pl.laidRows.Count] : 0.0;
        pl.laidRows.Add((Math.Max(pl.rowH, pl.rowFloor) + pl.pt.CellSpacingPt,
            pl.cellsOut));
    }

    /// <summary>Lays one cell out into its column: each cell line's segments (blank runs, radio and check glyphs, text fitted word by word) wrapped at the inner width, the pieces and line heights collected, the cell height summed.</summary>
    private static void LayoutStepTableCell(PsTableLayoutState pl, List<List<Converters.HtmlToPdfConverter.StepLine>> row, int c)
    {
        pl.colWc = c < pl.declared.Length ? pl.declared[c] : 48.75;
        pl.iw = Math.Max(pl.pt.FormRhythm
            ? pl.colWc - 2 * PsCellPad(pl.pt) : pl.colWc + 1.0, 8.0);
        pl.lhs = new List<double>();
        pl.pieces = new List<(int, double, Converters.HtmlToPdfConverter.StepSeg?, string?)>();
        pl.ccx = 0.0;
        pl.lineH = pl.baseLine;
        pl.lineDirty = false;
        pl.blockBlankW = 0.0;
        pl.clMargin = 0.0;
        foreach (var cl in row[c])
        {
            LayoutStepCellLine(pl, cl);
        }
        pl.cellH = pl.pt.FormRhythm ? 1.5 : 4.08 * pl.kfs;
        foreach (var lh in pl.lhs) pl.cellH += lh;
        pl.cellsOut.Add((pl.lhs, pl.pieces));
        // a column the sheet cuts off before it starts is
        // never measured - the row is only as tall as what
        // can actually be drawn on the page
        if (pl.colX < pl.page.Width - 0.5) pl.rowH = Math.Max(pl.rowH, pl.cellH);
        pl.colX += pl.colWc + PsCellGap(pl.pt);
    }

    /// <summary>Lays one cell line out: an empty paragraph keeps its box and margin, blank runs and glyphs advance the pen, text is fitted word by word across lines at the inner width.</summary>
    private static void LayoutStepCellLine(PsTableLayoutState pl, Converters.HtmlToPdfConverter.StepLine cl)
    {
        // the margin stands ABOVE the block: it grows
        // the box the line before it closed, not its own
        pl.clMargin = pl.pt.FormRhythm ? cl.MarginTopPt : 0.0;
        if (pl.clMargin > 0 && pl.lhs.Count > 0)
        {
            pl.lhs[^1] += pl.clMargin;
            pl.clMargin = 0;
        }
        // an empty paragraph takes a line box AND
        // the paragraph's own margins, above and below
        if (cl.EmptyPara)
        {
            var pBox = pl.pt.FormRhythm && cl.LinePt > 0 ? cl.LinePt : pl.baseLine;
            var pMar = pl.pt.FormRhythm && cl.ParaMarginPt > 0
                ? cl.ParaMarginPt : pl.cfs;
            pl.lhs.Add(pBox + 2 * pMar);
            pl.clMargin = 0;
            pl.ccx = 0; pl.lineH = pl.baseLine; pl.lineDirty = false;
            return;
        }
        foreach (var seg in cl.Segs)
        {
            if (seg.BlankPt > 0)
            {
                if (pl.ccx > 0 && pl.ccx + seg.PadLeftPt + seg.BlankPt > pl.iw + 0.5) NewLine(pl);
                else pl.ccx += seg.PadLeftPt;
                pl.pieces.Add((pl.lhs.Count, pl.ccx, seg, null));
                pl.ccx += seg.BlankPt;
                pl.lineH = pl.pt.FormRhythm ? pl.baseLine : 14.4 * pl.kfs;
                pl.lineDirty = true;
                pl.blockBlankW = seg.PadLeftPt < 0.01 && cl.Segs.Count == 1
                    ? seg.BlankPt : 0.0;
            }
            else if (seg.Radio || seg.Checkbox)
            {
                // a choice symbol is a 13 px box with
                // 5 px of margin after it
                var gw = pl.pt.FormRhythm && seg.Radio ? 13.5 * pl.kfs : 11.2 * pl.kfs;
                if (pl.ccx > 0 && pl.ccx + gw > pl.iw + 0.5) NewLine(pl);
                pl.pieces.Add((pl.lhs.Count, pl.ccx, seg, null));
                pl.ccx += gw;
                pl.lineDirty = true;
            }
            else if (seg.Text is { } st)
            {
                // a label under a block-level blank wraps at
                // the blank's width, not the cell's
                var tw = pl.blockBlankW > 0 && !pl.lineDirty
                    ? Math.Min(pl.iw, pl.blockBlankW + 4.5) : pl.iw;
                if (pl.lineDirty) pl.ccx += seg.PadLeftPt;
                var rem = st;
                while (rem.Length > 0)
                {
                    var avail = tw - pl.ccx;
                    var w1 = PsFirstWordEnd(rem);
                    if (pl.lineDirty
                        && PsMeasure(rem[..w1].TrimEnd(), seg.Bold, pl.cfs) > avail + 0.5
                        && PsMeasure(rem[..w1].Trim(), seg.Bold, pl.cfs) <= tw)
                    {
                        NewLine(pl);
                        rem = rem.TrimStart();
                        continue;
                    }
                    var fit = PsFitPrefix(rem, pl.cfs, seg.Bold, Math.Max(avail, 4));
                    if (rem[..fit].Trim().Length > 0 || pl.lineDirty)
                    {
                        pl.pieces.Add((pl.lhs.Count, pl.ccx, seg, rem[..fit]));
                        pl.ccx += PsMeasure(rem[..fit], seg.Bold, pl.cfs);
                        pl.lineDirty = true;
                    }
                    rem = rem[fit..];
                    if (rem.Length > 0) { NewLine(pl); rem = rem.TrimStart(); }
                }
            }
        }
        if (pl.lineDirty || pl.ccx > 0) NewLine(pl);
        if (cl.Segs.Count != 1 || cl.Segs[0].BlankPt <= 0) pl.blockBlankW = 0.0;
    }
}
