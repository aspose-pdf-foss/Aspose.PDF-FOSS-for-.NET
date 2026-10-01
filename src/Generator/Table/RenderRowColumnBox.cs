using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public partial class Table
{
    /// <summary>The box stage of a column render: the cell lookup, its widths, paddings and rectangles, verbatim; a return that ended the column became return false.</summary>
    private bool RenderRowColumnBox(RowColumnState rc)
    {
        rc.row = rc.slice.Plan.Row;
        rc.gridToCell = rc.slice.Plan.GridToCell;
        if (rc.gridToCell is not null)
        {
            rc.origIdx = rc.col < rc.gridToCell.Length ? rc.gridToCell[rc.col] : -1;
            if (rc.origIdx == -2) return false;                       // own ColSpan cover — x already advanced
            if (rc.origIdx < 0 || rc.origIdx >= rc.row.Cells.Count) { rc.cellX += rc.colWidths[rc.col] + CellSpacingH; return false; }
        }
        else if (rc.slice.Plan.ColToCell is { } colToCell)
        {
            rc.origIdx = rc.col < colToCell.Length ? colToCell[rc.col] : -1;
            if (rc.origIdx == -2) return false;                       // covered by an earlier cell's span
            if (rc.origIdx < 0) { rc.cellX += rc.colWidths[rc.col] + CellSpacingH; return false; }
        }
        else
        {
            rc.origIdx = rc.cellMap[rc.col];
            if (rc.origIdx >= rc.row.Cells.Count) { rc.cellX += rc.colWidths[rc.col] + CellSpacingH; return false; }
        }
        rc.cell = rc.row.Cells.At(rc.origIdx);
        rc.span = Math.Max(1, Math.Min(rc.cell.ColSpan, rc.colWidths.Length - rc.col));
        rc.cellWidth = GetCellWidth(rc.colWidths, rc.col, rc.span);
        rc.cellBoxWidth = rc.cellWidth
            + (LastColBoxOverhang > 0 && rc.col + rc.span >= rc.colWidths.Length ? LastColBoxOverhang : 0);
        // A row-spanning cell is drawn by the span-block pass (its rect covers
        // several rows); reserve its columns and move on.
        if (rc.gridToCell is not null && rc.slice.Plan.EffRowSpan is not null &&
            rc.slice.Plan.EffRowSpan[rc.origIdx] > 1)
        { rc.cellX += rc.cellWidth + CellSpacingH; return false; }
        rc.padding = EffectivePad(rc.cell, rc.row);
        rc.dp = DefaultPad(rc.cell, rc.row);
        rc.padLeft = rc.padding?.Left ?? rc.dp;
        rc.padTop = rc.padding?.Top ?? 0;
        // (a UA-boxed cell's continuation slice carries no top padding: the box was padded on its first page)
        if (UaCellBoxes && rc.slice.LineStart > 0) rc.padTop = 0;

        // Record the cell's laid-out rectangle (page space) for callers that
        // query Cell.Rect/Width after save. Union across slices when a row is
        // split across pages.
        rc.cell.Width = rc.cellWidth;
        rc.sliceRect = new Rectangle(rc.cellX, rc.slice.TopY - rc.slice.Height, rc.cellX + rc.cellBoxWidth, rc.slice.TopY);
        rc.cell.Rect = rc.cell.Rect is null
            ? rc.sliceRect
            : new Rectangle(
                Math.Min(rc.cell.Rect.LLX, rc.sliceRect.LLX), Math.Min(rc.cell.Rect.LLY, rc.sliceRect.LLY),
                Math.Max(rc.cell.Rect.URX, rc.sliceRect.URX), Math.Max(rc.cell.Rect.URY, rc.sliceRect.URY));

        rc.bandInset = HtmlRowSpacingPt / 2;
        rc.pitchBorder = _columnPitch > 0 && !rc.cell.IsNoBorder
            ? rc.cell.Border ?? rc.row.DefaultCellBorder ?? rc.row.Border ?? DefaultCellBorder
            : null;

        // (a row painted as a band was filled once for the whole row already)
        rc.bgColor = rc.cell.BackgroundColor ?? (rc.row.BackgroundIsBand ? null : rc.row.BackgroundColor);
        if (rc.bgColor is not null)
        {
            FillCellBackground(rc, rc.cellX);
        }

        // Background IMAGE — the cell's own artwork, stretched over the box the
        // cell's rules enclose (a 400 pt column with 0.1 pt rules
        // draws its 60 pt row's image 400 × 59.8 at the inner corner). It goes into
        // THIS content stream, ahead of the rules and the text, because a page stamp
        // would be appended after them and hide a white caption written over
        // it. A spill page has no Page object here yet; its background is handed to
        // the image sink instead, which the flow blits when the page materialises.
        if (rc.cell.BackgroundImage is { } cellBgImage && !_measureOnly
            && ReadRawImageBytes(cellBgImage) is { Length: > 0 } cellBgBytes)
        {
            DrawCellBackgroundImage(rc, cellBgBytes, rc.cellX);
        }

        // Border
        // (a cell that asked for no rule still opens its boundaries in a resolved
        // grid, and a neighbour's rule may have won them)
        if (!rc.cell.IsNoBorder || CollapsedRulesFor(rc.colWidths.Length) is not null)
        {
            DrawCellBorder(rc, rc.cellX);
        }

        return true;
    }
}
