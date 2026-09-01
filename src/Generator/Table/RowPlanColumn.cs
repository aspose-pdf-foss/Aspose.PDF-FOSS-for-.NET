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
// The row-plan column pass, lifted out of BuildRowPlan; it works on the row-plan state.
    private static void Consider(RowPlanState rp, double lineHeight, double tight)
    {
        if (lineHeight > rp.maxLineHeight) { rp.maxLineHeight = lineHeight; rp.tightForMax = tight; }
    }

    /// <summary>One column of the row plan, verbatim: the body of BuildRowPlan's per-column
    /// loop. Returns false where the loop broke out; a continue became return true.</summary>
    private bool BuildRowPlanColumn(int col, RowPlanState rp, Row row, double[] colWidths, int[] cellMap, int[]? gridToCell, int[]? effRowSpan, double svgFillHeight)
    {
        var pc = new RowPlanColumnState();
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
        pc.cell = row.Cells.At(pc.origIdx);
        pc.padding = EffectivePad(pc.cell, row);
        pc.dp = DefaultPad(pc.cell, row);
        pc.vb = pc.cell.Border ?? row.DefaultCellBorder ?? row.Border ?? DefaultCellBorder;
        pc.borderV = BorderTopBottom(pc.vb);
        // Collapsed borders (the pt-styled fragment's grids): every drawn
        // boundary is SHARED between adjacent rows, so a row bills one
        // stroke width, not its top + bottom pair (probed: 10 pt rows pitch
        // 12.5 = the 1.2 em line box + one 0.5 pt stroke).
        if (HtmlWrapInsetsCellMargins) pc.borderV /= 2;
        pc.padV = (pc.padding?.Top ?? 0) + (pc.padding?.Bottom ?? 0) + pc.borderV;
        if (pc.padV > rp.maxVertPad) rp.maxVertPad = pc.padV;
        if ((pc.padding?.Top ?? 0) > rp.maxTopPad) rp.maxTopPad = pc.padding?.Top ?? 0;

        // Generator cells with a drawn border and no explicit padding wrap in
        // the border's inner box (the draw starts the text at the inner edge
        // too): a 20 % column of a 420 pt grid with 1 pt rules wraps in 83.
        var (pitchL, pitchR) = CellBorderPitch();
        pc.padLeft = pc.padding?.Left ?? (pitchL > 0 ? 0 : pc.dp);
        pc.padRight = pc.padding?.Right ?? (pitchR > 0 ? 0 : pc.dp);
        pc.span = Math.Max(1, Math.Min(pc.cell.ColSpan, colWidths.Length - col));
        pc.cellWidth = GetCellWidth(colWidths, col, pc.span);
        pc.availWidth = pc.cellWidth - pc.padLeft - pc.padRight - _columnPitch;
        // A column the HTML layout sized carries the markup's cell rule inside its
        // width — the text box is what is left after it, the same box that pass
        // wrapped in when it worked out the row's height.
        if ((HtmlLayoutWrap || CssRunBoxes) && HtmlCellBorderPt > 0) pc.availWidth -= 2 * HtmlCellBorderPt;

        pc.textState = pc.cell.DefaultCellTextState ?? row.DefaultCellTextState ?? DefaultCellTextState;
        pc.defaultFontSize = ResolveCellFontSize(pc.cell, row);
        pc.cellAlign = ResolveCellAlignment(pc.cell, row);
        pc.lines = new List<CellLine>();

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
            // reserve one blank text line per inline row for height accounting.
            var inlineRows = BuildInlineCellLayout(pc.cell, pc.availWidth, pc.defaultFontSize, pc.textState, pc.cellAlign, out var inlineH);
            (rp.plan.CellInline ??= new())[col] = inlineRows;
            if (GeneratorCellModel)
            {
                // Generator cells stack their inline rows at each row's OWN pitch
                // (an 8 pt Arial cell beside a 10 pt Helvetica one pitches 8 per
                // line while its neighbour pitches 10) — the cell is an exact stack.
                // A graph-only inline cell prices its TEXT rows at the cell's own
                // resolved size, the way the plain path does.
                foreach (var ir in inlineRows)
                    pc.lines.Add(new CellLine
                    {
                        Text = "",
                        FontSize = pc.cellInlineFromGraphOnly
                            ? GraphOnlyRowHeight(ir, pc.defaultFontSize)
                            : InlineRowHeight(ir, pc.defaultFontSize),
                    });
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
        foreach (var paragraph in pc.cell.Paragraphs)
            if (!BuildRowPlanParagraph(paragraph, pc, rp, col, row, colWidths, cellMap, gridToCell, effRowSpan, svgFillHeight)) break;
        StampLeading(pc.lines, pc.paraLineStart, pc.paraLeading);
        if (pc.genPendingBottom > 0)
            pc.lines.Add(new CellLine { Text = "", BoxH = pc.genPendingBottom, MarginSpacer = true });
        }
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
            pc.cssMode = !anyControl && (anyCss && mixed || pc.preBox || anyForce || engineMixed);
        }
        pc.badgeOnlyCell = NestedTableRender && pc.lines.Count > 0
            && pc.lines.TrueForAll(l => l.Text.Length == 0 && l.Boxes is { Count: > 0 });
        pc.genExactStack = GeneratorCellModel && pc.cssMode
            && pc.lines.Exists(l => l.MarginSpacer || l.GenEngineExact);
        pc.genDescEm = pc.genExactStack ? CellFontDescentEm(pc.cell, row).DescentEm : 0;
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

        pc.hasStyledPara = pc.lines.Exists(l => l.Text.Length > 0 && l.BoxH > 0 && l.BoxH != l.FontSize);
        if (pc.hasStyledPara)
            while (pc.lines.Count > 0 && pc.lines[0].Text.Length == 0 && !pc.lines[0].ImgReserve
                   && !pc.lines[0].HtmlEngine && pc.lines[0].Boxes is null && !pc.lines[0].MarginSpacer)
                pc.lines.RemoveAt(0);
        rp.plan.CellLines.Add(pc.lines);
        if (pc.lines.Count > rp.plan.LineCount) rp.plan.LineCount = pc.lines.Count;
        pc.cellTight = 0;
        foreach (var cl in pc.lines) if (cl.FontSize + cl.Leading > pc.cellTight) pc.cellTight = cl.FontSize + cl.Leading;
        pc.cellExact = 0;
        pc.cellOwnStack = 0;
        pc.cellHasBox = false;
        pc.cellHasReserve = false;
        foreach (var cl in pc.lines)
        {
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
        rp.cellTotals.Add((pc.padV, pc.lines.Count, pc.cellTight, pc.cellExact, pc.cellOwnStack));
        return true;
    }

}
