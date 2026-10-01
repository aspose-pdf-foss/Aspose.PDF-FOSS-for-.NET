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
    /// <summary>The stages of one row-column box: the background fill, the background image and the border.</summary>
    private void DrawCellBorder(RowColumnState rc, double cellX)
    {
        // A grid resolved boundary by boundary strokes each boundary with the rule
        // that won it, whichever of the two cells brought it.
        if (CollapsedRulesFor(rc.colWidths.Length) is { } rules)
        {
            var skeleton = CollapsedSkeleton() / 2;
            var xs = new double[rc.span + 1];
            xs[0] = cellX + skeleton;
            for (var k = 0; k < rc.span; k++) xs[k + 1] = xs[k] + rc.colWidths[rc.col + k];
            var top = rc.slice.TopY - skeleton;
            DrawCollapsedBoxRules(rc.builder, rules, rc.slice.RowIndex, rc.col, rc.span, xs,
                new[] { top, top - rc.slice.Height },
                closesBottom: rc.slice.RowIndex >= Rows.Count - 1 || _sliceClosesPage,
                closesRight: rc.col + rc.span >= rc.colWidths.Length);
            return;
        }
        var cellBorder = rc.cell.Border ?? rc.row.DefaultCellBorder ?? rc.row.Border ?? DefaultCellBorder;
        // Form-grid cells stroke INSIDE their box, CSS-fashion: the stroke
        // centre sits half a width in from the cell edge, so two abutting
        // cells show a pair of lines one width apart (e.g. the
        // 185.45/186.20 doublet), not one shared line; each side runs the
        // box's full extent so the corners paint.
        if (cellBorder is not null && FormGridCells)
            DrawFormGridBorder(rc.builder, cellBorder, cellX + rc.bandInset,
                rc.slice.TopY - rc.slice.Height + rc.bandInset,
                rc.cellWidth - 2 * rc.bandInset, rc.slice.Height - 2 * rc.bandInset);
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
            // A SEPARATE grid strokes INSIDE its own pitch box: every rule is
            // inset half a width, so the ink stays within the column. (A collapsed
            // one strokes on its boundaries and returned above.)
            if (RulesInsideColumnWidth && rc.cell.CornerRadii is { } corners)
                RulePainter.PaintRoundedBox(rc.builder, drawn, cellX, rc.slice.TopY - rc.slice.Height,
                    rc.cellBoxWidth, rc.slice.Height, corners.Resolve(rc.cellBoxWidth, rc.slice.Height), null,
                    default, null);
            else if (RulesInsideColumnWidth)
                DrawRulesInsideBox(rc.builder, drawn, cellX, rc.slice.TopY - rc.slice.Height,
                    rc.cellBoxWidth, rc.slice.Height);
            else
                DrawPitchBorder(rc.builder, drawn, cellX + sl, rc.slice.TopY - rc.slice.Height + sb,
                    rc.cellBoxWidth - sl - sr, rc.slice.Height - sb - st);
        }
        else if (cellBorder is not null)
            DrawBorder(rc.builder, cellBorder, cellX + rc.bandInset, rc.slice.TopY - rc.slice.Height + rc.bandInset,
                rc.cellBoxWidth - 2 * rc.bandInset, rc.slice.Height - 2 * rc.bandInset);
    }

    /// <summary></summary>
    private void DrawCellBackgroundImage(RowColumnState rc, byte[] cellBgBytes, double cellX)
    {
        var (bl, bb, br, bt) = rc.pitchBorder is not null
            ? SideInsets(rc.pitchBorder, half: false)
            : (0d, 0d, 0d, 0d);
        var bgX = cellX + bl;
        var bgY = rc.slice.TopY - rc.slice.Height + bb;
        var bgW = rc.cellWidth - bl - br;
        var bgH = rc.slice.Height - bb - bt;
        if (bgW > 0 && bgH > 0)
        {
            if (rc.page is not null)
            {
                try
                {
                    var bgName = ImageStamp.FromEncodedBytes(cellBgBytes).RegisterXObject(rc.page);
                    rc.builder.SaveState();
                    rc.builder.SetMatrix(bgW, 0, 0, bgH, bgX, bgY);
                    rc.builder.DrawXObject(bgName);
                    rc.builder.RestoreState();
                }
                catch { /* an undecodable background is simply not painted */ }
            }
            else
            {
                rc.imageSink?.Add((cellBgBytes, new Rectangle(bgX, bgY, bgX + bgW, bgY + bgH)));
            }
        }
    }

    /// <summary></summary>
    private void FillCellBackground(RowColumnState rc, double cellX)
    {
        rc.builder.SetFillColor(rc.bgColor!);
        var bgRadius = (rc.cell.Border ?? rc.row.DefaultCellBorder ?? rc.row.Border ?? DefaultCellBorder)
            ?.RoundedBorderRadius ?? 0;
        if (bgRadius > 0)
            FillRoundedRect(rc.builder, cellX + rc.bandInset,
                rc.slice.TopY - rc.slice.Height + rc.bandInset,
                rc.cellWidth - 2 * rc.bandInset, rc.slice.Height - 2 * rc.bandInset, bgRadius);
        else if (rc.pitchBorder is not null || CollapsedCellSides(rc.cell, rc.colWidths.Length) is not null)
        {
            // A grid resolved boundary by boundary fills inside the half of each of
            // the box's own rules, counted from the lines the pitch box stands off
            // -- a box that asked for no rule of its own included. A box whose rules
            // stand inside its column width is filled whole, rules included, and
            // the rules are laid over the fill (the CSS border box).
            var (fl, fb, fr, ft) = CollapsedCellSides(rc.cell, rc.colWidths.Length) is { } sides
                ? CollapsedFillInsets(sides)
                : RulesInsideColumnWidth ? (0d, 0d, 0d, 0d)
                : SideInsets(rc.pitchBorder!, half: false);
            if (rc.cell.SpanCutLeft) fl = 0;
            if (rc.cell.SpanCutRight) fr = 0;
            var fill = (X: cellX + fl, Y: rc.slice.TopY - rc.slice.Height + fb,
                W: rc.cellWidth - fl - fr, H: rc.slice.Height - fb - ft);
            if (rc.cell.CornerRadii is { } corners)
            {
                // Rounded: the fill's own area, each corner held to half of it.
                RulePainter.PaintRoundedBox(rc.builder, null, fill.X, fill.Y, fill.W, fill.H,
                    corners.Resolve(fill.W, fill.H), rc.bgColor, fill, null);
                return;
            }
            rc.builder.Rectangle(fill.X, fill.Y, fill.W, fill.H);
            rc.builder.Fill();
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
            var span0 = Math.Max(1, Math.Min(rc.cell.ColSpan, rc.colWidths.Length - rc.col));
            var bgDrop = HtmlBandBleedRightPt > 0 ? RowSpacingPt : 0;
            if (HtmlBandBleedRightPt > 0 && rc.col + span0 >= rc.colWidths.Length)
                bgW = HtmlBandBleedRightPt - (cellX + rc.bandInset);
            rc.builder.Rectangle(cellX + rc.bandInset,
                rc.slice.TopY - rc.slice.Height + rc.bandInset - bgDrop,
                bgW, rc.slice.Height - 2 * rc.bandInset + bgDrop);
            rc.builder.Fill();
        }
    }
}
