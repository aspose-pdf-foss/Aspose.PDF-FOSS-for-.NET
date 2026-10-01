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
    /// <summary>Row column chrome: the cell's inline items and its text clip emitted.</summary>
    private void EmitCellInlineItems(RowColumnState rc, double cellX)
    {
        // Inline graph/text content (legend swatches, bar graphs): drawn once at the
        // cell top on the first slice. Text is shown in the table stream; each graph
        // is emitted as its own page-space content stream via graphSink.
        if (rc.slice.LineStart == 0 &&
            rc.slice.Plan.CellInline is { } inlineMap && inlineMap.TryGetValue(rc.col, out var inlineRows))
        {
            var inlineMark = rc.builder.Mark;
            rc.builder.ResetTextExtent();
            // Generator dialect: the block starts inside the border like any other
            // cell text, and seats one face descent above the full-em drop.
            // …only for a face the fragment names itself (or the default Helvetica): a
            // multi-segment line drawn in the TABLE default face keeps the full-em drop
            // (probed by the absorber positions of an Arial-default sub/superscript cell:
            // sub at top − fs − 0.245 fs, main at top − fs).
            var inlineCellFromGraphOnly = CellInlineFromGraphOnly(rc.cell);
            var inlineLift = rc.generatorCell && (rc.cellFragmentFace || inlineCellFromGraphOnly)
                ? rc.cellDescentEm : 0;
            rc.inlineStack = 0.0;
            for (var ri = 0; ri < inlineRows.Count; ri++)
            {
                EmitCellInlineRow(rc, cellX, inlineRows, ri, inlineLift);
            }
            if (rc.generatorCell)
                EmitCellTextClip(rc.builder, inlineMark, cellX + rc.borderInsetLeft + rc.clipPadL, rc.cellWidth - _columnPitch - rc.clipPadL - rc.clipPadR, rc.cellDescentEm, rc.cellFace);
        }
    }

    /// <summary>Row column chrome: the cell's nested tables built onto the page.</summary>
    private void BuildCellInnerTables(RowColumnState rc, double cellX)
    {
        // Nested tables — each inner grid renders in place on the slice holding its
        // reserved lines (the cell-image window precedent), as its own page-space
        // content stream via graphSink so the outer table's stream stays untouched.
        if (!_measureOnly && _buildPage is not null
            && rc.slice.Plan.CellTables is { } ctabs && ctabs.TryGetValue(rc.col, out var colTabs))
            foreach (var ct in colTabs)
            {
                if (!BuildCellInnerTable(rc, cellX, colTabs, ct)) break;
            }
    }

    /// <summary>Row column chrome: the cell's images placed in its content box.</summary>
    private void EmitCellImages(RowColumnState rc, double cellX)
    {
        // Image content — recorded once, at the row's top slice, for the caller to blit
        // onto the materialised page (overflow pages don't exist yet during the build).
        // The image is collected as a page-space rect; the cell border drawn above into
        // builder frames it once both content streams land on the page.
        if (rc.imageSink is not null
            && rc.slice.Plan.CellImages is { } imgs && imgs.TryGetValue(rc.col, out var colImgs))
            foreach (var ci in colImgs)
            {
                // A seated picture is out of the cell's flow: it draws once, on the row's
                // first slice, from the content origin offset by its box's Left/Top.
                if (ci.Seated)
                {
                    if (rc.slice.LineStart > 0) continue;
                    var seatX = cellX + rc.padLeft + ci.SeatLeft;
                    var seatTopY = rc.slice.TopY - rc.padTop - ci.SeatTop
                        - (rc.generatorCell ? rc.borderInsetTop : 0);
                    rc.imageSink.Add((ci.Data, new Rectangle(seatX, seatTopY - ci.Height, seatX + ci.Width, seatTopY)));
                    continue;
                }
                // Each image belongs to the slice holding ITS line, and is seated from
                // THAT slice's top — an absolute line offset would push a continuation
                // slice's image off the bottom of the page it was never drawn on.
                // Identity on an unsplit row: LineStart is 0 and every line is in range.
                if (ci.LineOffset < rc.slice.LineStart
                    || ci.LineOffset >= rc.slice.LineStart + rc.slice.LineCount) continue;
                var rel = ci.LineOffset - rc.slice.LineStart;
                var padRight = rc.padding?.Right ?? rc.dp;
                var imgX = cellX + rc.padLeft + ci.XOffset;
                if (ci.Align == HorizontalAlignment.Center)
                    // Centred the way a text line is: in the PADDED box of a collapsed
                    // grid (whose left pad carries the rule), else in the cell.
                    imgX = IsBordersCollapsed
                        ? cellX + rc.padLeft + Math.Max(0, (rc.cellWidth - rc.padLeft - padRight - ci.Width) / 2)
                        : cellX + Math.Max(rc.padLeft, (rc.cellWidth - ci.Width) / 2);
                else if (ci.Align == HorizontalAlignment.Right)
                    imgX = cellX + Math.Max(0, rc.cellWidth - padRight - ci.Width);
                // Cell VerticalAlignment centres/bottoms the image within the row's
                // content band (the row is usually taller than the image because its
                // height is reserved in whole text lines). With SEVERAL images stacked
                // in one cell there is no single band to centre in, so they sit where
                // their lines put them.
                var imgVaOffset = 0.0;
                if (colImgs.Count == 1 && rc.effVA is VerticalAlignment.Center or VerticalAlignment.Bottom)
                {
                    var availH = rc.slice.Height - rc.padTop - rc.padBot - rel * rc.slice.Plan.LineHeight;
                    if (availH > ci.Height)
                        imgVaOffset = rc.effVA == VerticalAlignment.Center
                            ? (availH - ci.Height) / 2
                            : availH - ci.Height;
                }
                // Seat the image below any text lines that precede it in the cell (e.g. a
                // title line above a centred logo) rather than at the cell top: by the
                // lines' own pitches when the picture is a box of the cell's own stack,
                // else by whole lines of the row's pitch.
                var above = ci.OwnSeat ? OwnStackAbove(rc, ci.LineOffset) + ci.MarginTop : rel * rc.slice.Plan.LineHeight;
                var imgTopY = rc.slice.TopY - rc.padTop - above - (ci.OwnSeat ? 0 : imgVaOffset)
                    // Generator dialect: the image sits inside the cell's top rule.
                    - (rc.generatorCell ? rc.borderInsetTop : 0);
                var box = new Rectangle(imgX, imgTopY - ci.Height, imgX + ci.Width, imgTopY);
                if (ci.Block is not null) rc.blockSink?.Add((ci.Block, ci.Part!, box));
                else rc.imageSink.Add((ci.Data, box));
            }
    }

    /// <summary>The height of the cell's lines this slice draws before line
    /// <paramref name="lineOffset"/>, each at its own pitch.</summary>
    private double OwnStackAbove(RowColumnState rc, int lineOffset)
    {
        var lines = rc.slice.Plan.CellLines is { } all && rc.col < all.Count ? all[rc.col] : null;
        if (lines is null) return 0;
        double height = 0;
        for (var i = rc.slice.LineStart; i < lineOffset && i < lines.Count; i++)
            height += DeclaredLinePitch(lines[i], rc.slice.Plan.LineHeight);
        return height;
    }

    /// <summary>Row column chrome: the generator cell's text clip emitted.</summary>
    private void EmitGeneratorCellClip(RowColumnState rc, double cellX)
    {
        if (rc.generatorCell)
        {
            // Mixed-size HTML-engine cell: the single-size clip model reads the
            // smaller-size lines as sub/superscript satellites of the biggest one
            // and crops them away — bound such a cell by every line's own box.
            // A cell whose lines DECLARE their boxes at mixed sizes (the exact
            // cell-line model) is bound the same way: its 12 pt line above a 20 pt
            // one is a main line of its own, not a satellite to crop.
            var clipMixedEngine = false;
            double clipEngSz = -1;
            for (var li = 0; rc.cellLines is not null && li < rc.cellLines.Count && !clipMixedEngine; li++)
            {
                var l = rc.cellLines[li];
                if (!(l.HtmlEngine || DeclaresLineBox(l)) || l.FontSize <= 0) continue;
                if (clipEngSz < 0) clipEngSz = l.FontSize;
                else if (Math.Abs(l.FontSize - clipEngSz) > 0.5) clipMixedEngine = true;
            }
            EmitCellTextClip(rc.builder, rc.clipMark, cellX + rc.borderInsetLeft + rc.clipPadL, rc.cellWidth - _columnPitch - rc.clipPadL - rc.clipPadR, rc.cellDescentEm, rc.cellFace, clipMixedEngine);
        }
    }
}
