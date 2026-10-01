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
    /// <summary>Row plan column: the cell's tight, exact and own-stack heights accumulated from its lines.</summary>
    private void AccumulateColumnLineMetrics(int col, RowPlanState rp, RowPlanColumnState pc)
    {
        pc.cellTight = 0;
        foreach (var cl in pc.lines)
        {
            // An HTML-engine line is a CSS line BOX: the cell ends at the box, not at
            // the bare font size, or the row's padding is netted short by the box's
            // leading (probed: a two-line 12 pt <li> cell pitches 2 x 13.5 plus its
            // 4.5 pt of padding and rule, not 1.5 less).
            var tight = cl.HtmlEngine && cl.BoxH > 0 ? cl.BoxH : cl.FontSize + cl.Leading;
            if (tight > pc.cellTight) pc.cellTight = tight;
        }
        pc.cellExact = 0;
        pc.cellOwnStack = 0;
        pc.cellHasBox = false;
        pc.cellHasReserve = false;
        foreach (var cl in pc.lines)
        {
            // A line that is a box of its own height prices exactly that -- unless
            // the generator model prices the picture from CellImages below.
            if (cl.OwnPitch > 0)
            {
                pc.cellHasReserve = true;
                // (a generator cell's picture RESERVE is priced from CellImages below;
                // a line that seated itself -- runs a picture raised -- is its own pitch)
                if (!GeneratorCellModel || !cl.ImgReserve) pc.cellOwnStack += cl.OwnPitch;
                continue;
            }
            if (cl.Checkbox is { } cb)
            {
                pc.cellHasBox = true;
                pc.cellOwnStack += cb.Height > 0 ? cb.Height : cl.FontSize;
            }
            // Nested-table reserve line: its FontSize IS the grid's full height,
            // and a boxed line's own height is its BoxH — the exact stack must
            // price them truly (lifted render only; legacy stays byte-stable).
            // EMPTY filler lines around the placeholder draw nothing and take
            // nothing (they were padding the row ~30 pt below the grid).
            else if (NestedTableRender)
            {
                if (cl.ImgReserve) pc.cellHasReserve = true;
                // A text line occupies its CSS line BOX, the same pitch the draw
                // stacks it at — pricing it at the bare em here let a tall cell's
                // lines run past the row band and draw over the row below.
                if (cl.ImgReserve || cl.Text.Length > 0 || cl.Boxes is { Count: > 0 } || cl.BoxH > 0
                    || cl.HtmlEngine)
                    pc.cellOwnStack += cl.BoxH > 0 ? cl.BoxH
                        : cl.ImgReserve ? cl.FontSize
                        : CssLineBoxPt(cl.FontSize);
            }
            else if (GeneratorCellModel && cl.ImgReserve)
            {
                // A NESTED-GRID reserve line's FontSize IS the grid's measured
                // height, so the cell's own stack has to price it: the generator
                // draws that grid in place, and the uniform line grid would
                // re-quantize every sibling line in the row to the grid's height
                // (a 12 pt heading above a 68 pt grid became a 136 pt row).
                // A cell IMAGE's reserve is priced below, from CellImages.
                if (rp.plan.CellTables?.ContainsKey(col) == true)
                {
                    pc.cellOwnStack += cl.FontSize;
                    pc.cellHasReserve = true;
                }
            }
            else if (GeneratorCellModel && cl.BoxH > 0) pc.cellOwnStack += cl.BoxH;
            else pc.cellOwnStack += cl.FontSize + cl.Leading;
        }
        if (GeneratorCellModel && rp.plan.CellImages is { } genImgs && genImgs.TryGetValue(col, out var genCellImgs))
        {
            foreach (var gi in genCellImgs) pc.cellOwnStack += gi.BoxHeight > 0 ? gi.BoxHeight : gi.Height;
            // …and that stack IS the cell's height: a picture is a box of its own
            // height, not a whole number of text lines. A 100 pt image under a
            // 10 pt caption makes a 110 pt row; quantising the reserve into nine
            // 12 pt line boxes made it 119.
            pc.cellExact = pc.cellOwnStack;
        }
        if (pc.genExactStack) pc.cellExact = pc.cellOwnStack;
        // …and so does a cell whose paragraphs DECLARED their line boxes: a
        // declared box IS the line's own height, so the cell stands at the sum of
        // them and the row takes the tallest such stack. The uniform grid would
        // instead price this cell's lines at a taller neighbour's pitch (a 12 pt
        // two-line cell beside an 18 pt one-line cell became 54 pt, not 36).
        if (pc.lines.Exists(l => DeclaresLineBox(l))) pc.cellExact = pc.cellOwnStack;
        if (pc.cellHasBox && pc.lines.Count > 1) pc.cellExact = pc.cellOwnStack;
        if (pc.badgeOnlyCell) pc.cellExact = pc.cellOwnStack;
        // A nested-grid cell sizes as its exact stack (the reserve's height plus
        // any sibling lines) — the uniform line grid would re-quantize it.
        if (pc.cellHasReserve) pc.cellExact = pc.cellOwnStack;
        if (pc.cellInlineExact) pc.cellExact = pc.cellOwnStack;
        // A generator cell whose lines differ in size is an EXACT stack: each line
        // occupies its own size, so a 4 pt spacer paragraph above a 14 pt line is
        // 18 pt of content, not two 14 pt grid lines.
        if (GeneratorCellModel && pc.cellMixedSizes) pc.cellExact = pc.cellOwnStack;
    }

    /// <summary>Row plan column: a css-mode cell's line stack measured into the plan's line count.</summary>
    private void MeasureCssColumnStack(int col, RowPlanState rp, RowPlanColumnState pc)
    {
        if (pc.cssMode)
        {
            double sum = 0;
            foreach (var l in pc.lines)
            {
                if (l.BoxH <= 0 && pc.genExactStack)
                {
                    l.BoxH = l.FontSize;
                    l.BaseOff = l.FontSize * (1 - pc.genDescEm);
                }
                // A UA-boxed cell's image reserve line is exactly the image's box (the mailing's 77 px logo row).
                else if (l.BoxH <= 0 && UaCellBoxes && l.ImgReserve && l.FontSize > 0)
                {
                    l.BoxH = l.FontSize;
                    l.BaseOff = l.FontSize;
                }
                else if (l.BoxH <= 0 && UaCellBoxes && l.CssAsc > 0 && l.CssDesc > 0
                    && !(NestedTableRender && l.ImgReserve))
                {
                    // A UA-boxed cell line stands on its FACE's CSS `line-height: normal`
                    // box - the win height in whole pixels (Verdana 6 pt: 10 px = 7.50, the
                    // pitch the reference steps at) - and seats half the surplus leading plus
                    // the ascent below the box top, not on a flat 1.2 em.
                    // (…or on the line-height its markup DECLARES - the mailing's `body { line-height: 125% }`
                    //  steps its 13 px lines 16.25 px - seating the same half-leading way)
                    l.BoxH = l.OwnDeclared && l.OwnLinePt > 0 ? l.OwnLinePt : FaceCssLineBoxPt(l.FontSize, l.CssAsc + l.CssDesc);
                    l.BaseOff = CssRunBaseOff(l.BoxH, l.FontSize, l.CssAsc, l.CssDesc);
                }
                else if (l.BoxH <= 0)
                {
                    // A nested-table reserve line's FontSize IS the grid's exact
                    // height — the 1.2 line-box factor would pad the row by 20%
                    // of the whole grid.
                    l.BoxH = NestedTableRender && l.ImgReserve
                        ? l.FontSize : l.FontSize * 1.2;
                    l.BaseOff = l.CssAsc > 0
                        ? l.FontSize * (l.CssAsc + (1.2 - l.CssAsc - l.CssDesc) / 2)
                        : l.FontSize;
                }
                sum += l.BoxH;
            }
            (rp.plan.CssCells ??= new HashSet<int>()).Add(col);
            if (sum > rp.plan.CssContentH && !pc.badgeOnlyCell)
            {
                rp.plan.CssContentH = sum;
                // ⚠ The content box ends on its LAST BASELINE, but trimming to
                // it here is the wrong lever: it recovers only ~2.2 pt and hurts the
                // page overall. Row 0's real excess is one whole 12.75 pt line box —
                // the cell's leading zero-width space takes a line of its own, where a
                // browser merges it into the first text line (whose box then grows by
                // descent × (24 − 9.75) = 3.562, giving its 35.812 first-baseline step).
                rp.plan.CssContentTight = 0;
            }
        }
        else if (pc.lines.Count > rp.plan.NonCssLineCount) rp.plan.NonCssLineCount = pc.lines.Count;
    }

    /// <summary>Row plan column: the cell's lines classified - css mode, pre box and mixed sizes.</summary>
    private void ClassifyColumnLines(int col, RowPlanState rp, RowPlanColumnState pc)
    {
        pc.cssMode = false;
        pc.preBox = false;
        pc.cellMixedSizes = false;
        if (rp.plan.CellInline is null || !rp.plan.CellInline.ContainsKey(col))
        {
            double sz0 = -1; var mixed = false; var anyCss = false; var anyControl = false;
            var anyForce = false;
            foreach (var l in pc.lines)
            {
                if (l.Option is not null || l.Checkbox is not null
                    || l.InlineOptions is not null
                    || l.Text.IndexOf(InlineButtonChar) >= 0
                    || l.InputBoxes is not null
                    || l.Text.IndexOf(InlineCheckChar) >= 0) { anyControl = true; break; }
                if (l.CssAsc > 0) anyCss = true;
                if (l.CssForce) anyForce = true;   // form-dialect cell: CSS boxes at a uniform size too
                if (l.Boxes is { Count: > 0 }) anyForce = true;   // inline boxes stack by their own BoxH
                if (l.BoxH > 0) pc.preBox = true;   // box set at line build (bold-serif HTML cell)
                if (sz0 < 0) sz0 = l.FontSize;
                else if (Math.Abs(l.FontSize - sz0) > 0.01) mixed = true;
            }
            pc.cellMixedSizes = mixed;
            // HTML-engine lines whose CSS boxes differ (a 33pt span line beside
            // 12pt lines) stack by their own boxes too - the uniform grid was
            // calibrated for the single-box engine cells only.
            var engineMixed = false;
            double engBox = -1;
            foreach (var l in pc.lines)
                if (l.HtmlEngine && l.BoxH > 0)
                {
                    if (engBox < 0) engBox = l.BoxH;
                    else if (Math.Abs(l.BoxH - engBox) > 0.75) { engineMixed = true; break; }
                }
            pc.cssMode = !anyControl && (anyCss && mixed || pc.preBox || anyForce || engineMixed
                // (a UA-boxed cell stacks every line on its face's own CSS box)
                || UaCellBoxes && anyCss);
        }
    }

    /// <summary>Row plan column: the cell's paragraphs turned into plan lines.</summary>
    private void BuildColumnLines(int col, RowPlanState rp, RowPlanColumnState pc, Row row, double[] colWidths, int[] cellMap, int[]? gridToCell, int[]? effRowSpan, double svgFillHeight)
    {
        pc.cellNeedsInline = false;
        pc.cellInlineExact = false;
        pc.cellInlineFromGraphOnly = false;
        foreach (var gp in pc.cell.Paragraphs)
        {
            if (gp is Aspose.Pdf.Drawing.Graph) { pc.cellNeedsInline = true; pc.cellInlineFromGraphOnly = true; continue; }
            if (IsInlineParagraph(gp) || IsMultiSegmentFragment(gp))
            { pc.cellNeedsInline = true; pc.cellInlineFromGraphOnly = false; break; }
        }
        if (pc.cellNeedsInline)
        {
            // Cells mixing Graph paragraphs with inline text (e.g. a colour-swatch
            // legend or a horizontal bar graph) get a left-to-right inline layout;
            (var inlineRows, var inlineH) = BuildInlineCellLayout(pc.cell, pc.availWidth, pc.defaultFontSize, pc.textState, pc.cellAlign);
            (rp.plan.CellInline ??= new())[col] = inlineRows;
            if (GeneratorCellModel)
            {
                // Generator cells stack their inline rows at each row's OWN pitch
                // (an 8 pt Arial cell beside a 10 pt Helvetica one pitches 8 per
                // line while its neighbour pitches 10) — the cell is an exact stack.
                foreach (var ir in inlineRows)
                    pc.lines.Add(new CellLine { Text = "", FontSize = InlineRowHeight(ir, pc.defaultFontSize) });
                pc.cellInlineExact = true;
                if (pc.cellInlineFromGraphOnly) inlineH = pc.defaultFontSize;
            }
            else
                foreach (var _ in inlineRows) pc.lines.Add(new CellLine { Text = "", FontSize = pc.defaultFontSize });
            Consider(rp, inlineH, inlineH);
        }
        else
        {
        pc.genPendingBottom = 0.0;
        pc.paraLineStart = 0;
        pc.paraLeading = 0.0;
        pc.paraLineBox = (0, 0);
        foreach (var paragraph in pc.cell.Paragraphs)
            if (!BuildRowPlanParagraph(paragraph, pc, rp, col, row, colWidths, cellMap, gridToCell, effRowSpan, svgFillHeight)) break;
        StampLeading(pc.lines, pc.paraLineStart, pc.paraLeading);
        StampDeclaredLineBox(pc.lines, pc.paraLineStart, pc.paraLineBox);
        if (pc.genPendingBottom > 0)
            pc.lines.Add(new CellLine { Text = "", BoxH = pc.genPendingBottom, MarginSpacer = true });
        }
    }

    /// <summary>Row plan column: the grid column mapped to its cell; a span-covered column plans blank.</summary>
    private bool ResolveColumnCell(int col, RowPlanState rp, RowPlanColumnState pc, Row row, int[] cellMap, int[]? gridToCell, int[]? effRowSpan)
    {
        if (gridToCell is not null)
        {
            pc.origIdx = col < gridToCell.Length ? gridToCell[col] : -1;
            if (pc.origIdx < 0 || pc.origIdx >= row.Cells.Count) { rp.plan.CellLines.Add(new List<CellLine>()); return true; }
            // A row-spanning cell's content, padding and metrics belong to the span
            // block, not this row's plan — its grid columns stay blank here.
            if (effRowSpan is not null && effRowSpan[pc.origIdx] > 1)
            { rp.plan.CellLines.Add(new List<CellLine>()); return true; }
        }
        else if (rp.plan.ColToCell is { } colToCell)
        {
            pc.origIdx = colToCell[col];
            if (pc.origIdx < 0) { rp.plan.CellLines.Add(new List<CellLine>()); return true; }
        }
        else
        {
            pc.origIdx = cellMap[col];
            if (pc.origIdx >= row.Cells.Count) { rp.plan.CellLines.Add(new List<CellLine>()); return true; }
        }
        return false;
    }
}
