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
    private bool RenderRowColumnBox(RowColumnState rc, int col, ref double cellX, ContentStreamBuilder builder, RowSlice slice,
        double[] colWidths, string fontName, int[] cellMap,
        List<(Rectangle rect, Hyperlink link)>? links, List<(byte[] data, Rectangle rect)>? imageSink,
        List<(Aspose.Pdf.Forms.RadioButtonOptionField opt, Rectangle rect)>? optionSink, List<byte[]>? graphSink,
        List<(Aspose.Pdf.Forms.CheckboxField cbf, Rectangle rect)>? checkboxSink, Page? page,
        List<(Note note, double x, double baseline, double size)>? footnoteSink)
    {
        rc.row = slice.Plan.Row;
        rc.gridToCell = slice.Plan.GridToCell;
        if (rc.gridToCell is not null)
        {
            rc.origIdx = col < rc.gridToCell.Length ? rc.gridToCell[col] : -1;
            if (rc.origIdx == -2) return false;                       // own ColSpan cover — x already advanced
            if (rc.origIdx < 0 || rc.origIdx >= rc.row.Cells.Count) { cellX += colWidths[col]; return false; }
        }
        else if (slice.Plan.ColToCell is { } colToCell)
        {
            rc.origIdx = col < colToCell.Length ? colToCell[col] : -1;
            if (rc.origIdx == -2) return false;                       // covered by an earlier cell's span
            if (rc.origIdx < 0) { cellX += colWidths[col]; return false; }
        }
        else
        {
            rc.origIdx = cellMap[col];
            if (rc.origIdx >= rc.row.Cells.Count) { cellX += colWidths[col]; return false; }
        }
        rc.cell = rc.row.Cells.At(rc.origIdx);
        rc.span = Math.Max(1, Math.Min(rc.cell.ColSpan, colWidths.Length - col));
        rc.cellWidth = GetCellWidth(colWidths, col, rc.span);
        rc.cellBoxWidth = rc.cellWidth
            + (LastColBoxOverhang > 0 && col + rc.span >= colWidths.Length ? LastColBoxOverhang : 0);
        // A row-spanning cell is drawn by the span-block pass (its rect covers
        // several rows); reserve its columns and move on.
        if (rc.gridToCell is not null && slice.Plan.EffRowSpan is not null &&
            slice.Plan.EffRowSpan[rc.origIdx] > 1)
        { cellX += rc.cellWidth; return false; }
        rc.padding = EffectivePad(rc.cell, rc.row);
        rc.dp = DefaultPad(rc.cell, rc.row);
        rc.padLeft = rc.padding?.Left ?? rc.dp;
        rc.padTop = rc.padding?.Top ?? 0;

        // Record the cell's laid-out rectangle (page space) for callers that
        // query Cell.Rect/Width after save. Union across slices when a row is
        // split across pages.
        rc.cell.Width = rc.cellWidth;
        rc.sliceRect = new Rectangle(cellX, slice.TopY - slice.Height, cellX + rc.cellBoxWidth, slice.TopY);
        rc.cell.Rect = rc.cell.Rect is null
            ? rc.sliceRect
            : new Rectangle(
                Math.Min(rc.cell.Rect.LLX, rc.sliceRect.LLX), Math.Min(rc.cell.Rect.LLY, rc.sliceRect.LLY),
                Math.Max(rc.cell.Rect.URX, rc.sliceRect.URX), Math.Max(rc.cell.Rect.URY, rc.sliceRect.URY));

        rc.bandInset = HtmlRowSpacingPt / 2;
        rc.pitchBorder = _columnPitch > 0 && !rc.cell.IsNoBorder
            ? rc.cell.Border ?? rc.row.DefaultCellBorder ?? rc.row.Border ?? DefaultCellBorder
            : null;

        rc.bgColor = rc.cell.BackgroundColor ?? rc.row.BackgroundColor;
        if (rc.bgColor is not null)
        {
            builder.SetFillColor(rc.bgColor);
            var bgRadius = (rc.cell.Border ?? rc.row.DefaultCellBorder ?? rc.row.Border ?? DefaultCellBorder)
                ?.RoundedBorderRadius ?? 0;
            if (bgRadius > 0)
                FillRoundedRect(builder, cellX + rc.bandInset,
                    slice.TopY - slice.Height + rc.bandInset,
                    rc.cellWidth - 2 * rc.bandInset, slice.Height - 2 * rc.bandInset, bgRadius);
            else if (rc.pitchBorder is not null)
            {
                var (fl, fb, fr, ft) = SideInsets(rc.pitchBorder, half: false);
                if (rc.cell.SpanCutLeft) fl = 0;
                if (rc.cell.SpanCutRight) fr = 0;
                builder.Rectangle(cellX + fl, slice.TopY - slice.Height + fb,
                    rc.cellWidth - fl - fr, slice.Height - fb - ft);
                builder.Fill();
            }
            else
            {
                // Over-declared grid document: a band fill on the row's LAST
                // cell bleeds to the page's right edge — section bands paint
                // page-wide while the content keeps the
                // standard box — and every band covers its trailing border-
                // spacing gap (the fills overpaint each other; the
                // page background never shows between two banded rows).
                var bgW = rc.cellWidth - 2 * rc.bandInset;
                var span0 = Math.Max(1, Math.Min(rc.cell.ColSpan, colWidths.Length - col));
                var bgDrop = HtmlBandBleedRightPt > 0 ? RowSpacingPt : 0;
                if (HtmlBandBleedRightPt > 0 && col + span0 >= colWidths.Length)
                    bgW = HtmlBandBleedRightPt - (cellX + rc.bandInset);
                builder.Rectangle(cellX + rc.bandInset,
                    slice.TopY - slice.Height + rc.bandInset - bgDrop,
                    bgW, slice.Height - 2 * rc.bandInset + bgDrop);
                builder.Fill();
            }
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
            var (bl, bb, br, bt) = rc.pitchBorder is not null
                ? SideInsets(rc.pitchBorder, half: false)
                : (0d, 0d, 0d, 0d);
            var bgX = cellX + bl;
            var bgY = slice.TopY - slice.Height + bb;
            var bgW = rc.cellWidth - bl - br;
            var bgH = slice.Height - bb - bt;
            if (bgW > 0 && bgH > 0)
            {
                if (page is not null)
                {
                    try
                    {
                        var bgName = ImageStamp.FromEncodedBytes(cellBgBytes).RegisterXObject(page);
                        builder.SaveState();
                        builder.SetMatrix(bgW, 0, 0, bgH, bgX, bgY);
                        builder.DrawXObject(bgName);
                        builder.RestoreState();
                    }
                    catch { /* an undecodable background is simply not painted */ }
                }
                else
                {
                    imageSink?.Add((cellBgBytes, new Rectangle(bgX, bgY, bgX + bgW, bgY + bgH)));
                }
            }
        }

        // Border
        if (!rc.cell.IsNoBorder)
        {
            var cellBorder = rc.cell.Border ?? rc.row.DefaultCellBorder ?? rc.row.Border ?? DefaultCellBorder;
            // Form-grid cells stroke INSIDE their box, CSS-fashion: the stroke
            // centre sits half a width in from the cell edge, so two abutting
            // cells show a pair of lines one width apart (e.g. the
            // 185.45/186.20 doublet), not one shared line; each side runs the
            // box's full extent so the corners paint.
            if (cellBorder is not null && FormGridCells)
                DrawFormGridBorder(builder, cellBorder, cellX + rc.bandInset,
                    slice.TopY - slice.Height + rc.bandInset,
                    rc.cellWidth - 2 * rc.bandInset, slice.Height - 2 * rc.bandInset);
            else if (rc.pitchBorder is not null)
            {
                var (sl, sb, sr, st) = SideInsets(rc.pitchBorder, half: true);
                var drawn = rc.pitchBorder;
                // A span cut by the slice edge: no rule on the cut side, the others
                // run to the box edge there.
                if (rc.cell.SpanCutLeft || rc.cell.SpanCutRight)
                {
                    var sides = rc.pitchBorder.Side;
                    if (rc.cell.SpanCutLeft) { sides &= ~BorderSide.Left; sl = 0; }
                    if (rc.cell.SpanCutRight) { sides &= ~BorderSide.Right; sr = 0; }
                    drawn = new BorderInfo(sides, rc.pitchBorder.Width, rc.pitchBorder.Color);
                }
                DrawPitchBorder(builder, drawn, cellX + sl, slice.TopY - slice.Height + sb,
                    rc.cellBoxWidth - sl - sr, slice.Height - sb - st);
            }
            else if (cellBorder is not null)
                DrawBorder(builder, cellBorder, cellX + rc.bandInset, slice.TopY - slice.Height + rc.bandInset,
                    rc.cellBoxWidth - 2 * rc.bandInset, slice.Height - 2 * rc.bandInset);
        }

        return true;
    }
}
