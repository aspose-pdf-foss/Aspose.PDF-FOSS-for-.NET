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
        if (ResolveColumnCell(col, rp, pc, row, cellMap, gridToCell, effRowSpan)) return true;
        pc.cell = row.Cells.At(pc.origIdx);
        pc.padding = EffectivePad(pc.cell, row);
        pc.dp = DefaultPad(pc.cell, row);
        pc.vb = pc.cell.Border ?? row.DefaultCellBorder ?? row.Border ?? DefaultCellBorder;
        pc.borderV = BorderTopBottom(pc.vb);
        // Collapsed borders (the pt-styled fragment's grids): every drawn
        // boundary is SHARED between adjacent rows, so a row bills one
        // stroke width, not its top + bottom pair (probed: 10 pt rows pitch
        // 12.5 = the 1.2 em line box + one 0.5 pt stroke).
        // …and a grid the CALLER declared collapsed bills the same way, for the
        // same reason: its rows meet on shared boundaries.
        if (HtmlWrapInsetsCellMargins || IsBordersCollapsed) pc.borderV /= 2;
        // …and where the grid resolved each boundary on its own, the cell bills half
        // of the rule its top and its bottom each ended up with.
        var collapsedSides = CollapsedCellSides(pc.cell, colWidths.Length);
        if (collapsedSides is { } resolved) pc.borderV = (resolved.Top + resolved.Bottom) / 2;
        pc.padV = (pc.padding?.Top ?? 0) + (pc.padding?.Bottom ?? 0) + pc.borderV;
        if (pc.padV > rp.maxVertPad) rp.maxVertPad = pc.padV;
        if (pc.borderV > rp.maxBorderV) rp.maxBorderV = pc.borderV;
        if ((pc.padding?.Top ?? 0) > rp.maxTopPad) rp.maxTopPad = pc.padding?.Top ?? 0;

        // Generator cells with a drawn border and no explicit padding wrap in
        // the border's inner box (the draw starts the text at the inner edge
        // too): a 20 % column of a 420 pt grid with 1 pt rules wraps in 83.
        var (pitchL, pitchR) = CellBorderPitch();
        pc.padLeft = pc.padding?.Left ?? (pitchL > 0 ? 0 : pc.dp);
        pc.padRight = pc.padding?.Right ?? (pitchR > 0 ? 0 : pc.dp);
        pc.span = Math.Max(1, Math.Min(pc.cell.ColSpan, colWidths.Length - col));
        pc.cellWidth = GetCellWidth(colWidths, col, pc.span);
        pc.availWidth = pc.cellWidth - pc.padLeft - pc.padRight
            - (collapsedSides is { } across ? (across.Left + across.Right) / 2 : CellRuleInsetAcross(pc.cell, row));
        // A column the HTML layout sized carries the markup's cell rule inside its
        // width — the text box is what is left after it, the same box that pass
        // wrapped in when it worked out the row's height.
        if ((HtmlLayoutWrap || CssRunBoxes) && HtmlCellBorderPt > 0) pc.availWidth -= 2 * HtmlCellBorderPt;

        pc.textState = pc.cell.DefaultCellTextState ?? row.DefaultCellTextState ?? DefaultCellTextState;
        pc.defaultFontSize = ResolveCellFontSize(pc.cell, row);
        pc.cellAlign = ResolveCellAlignment(pc.cell, row);
        pc.lines = new List<CellLine>();

        BuildColumnLines(col, rp, pc, row, colWidths, cellMap, gridToCell, effRowSpan, svgFillHeight);
        ClassifyColumnLines(col, rp, pc);
        pc.badgeOnlyCell = NestedTableRender && pc.lines.Count > 0
            && pc.lines.TrueForAll(l => l.Text.Length == 0 && l.Boxes is { Count: > 0 });
        pc.genExactStack = GeneratorCellModel && pc.cssMode
            && pc.lines.Exists(l => l.MarginSpacer || l.GenEngineExact);
        pc.genDescEm = pc.genExactStack ? CellFontDescentEm(pc.cell, row).DescentEm : 0;
        MeasureCssColumnStack(col, rp, pc);

        pc.hasStyledPara = pc.lines.Exists(l => l.Text.Length > 0 && l.BoxH > 0 && l.BoxH != l.FontSize);
        if (pc.hasStyledPara)
            while (pc.lines.Count > 0 && pc.lines[0].Text.Length == 0 && !pc.lines[0].ImgReserve
                   && !pc.lines[0].HtmlEngine && pc.lines[0].Boxes is null && !pc.lines[0].MarginSpacer)
                pc.lines.RemoveAt(0);
        rp.plan.CellLines.Add(pc.lines);
        if (pc.lines.Count > rp.plan.LineCount) rp.plan.LineCount = pc.lines.Count;
        AccumulateColumnLineMetrics(col, rp, pc);
        rp.cellTotals.Add((pc.padV, pc.lines.Count, pc.cellTight, pc.cellExact, pc.cellOwnStack));
        return true;
    }

}
