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
    /// <summary>Lets a row-spanning cell be CUT by a page break instead of
    /// moving whole to the next page. The block then keeps the rows it has on
    /// each page, writes as many of its lines as that portion holds, and picks
    /// the rest up overleaf -- where the last row it covers absorbs whatever
    /// those remaining lines still need.
    ///
    /// Off by default: a block that does not fit is carried whole, which is
    /// what the keep-together bundle exists to do.</summary>
    public bool SpanBlockSplitsAtPageBreak { get; set; }

    /// <summary>The part of a block's height that is not its lines: the cell's
    /// own padding and the rule its box bills.</summary>
    private double SpanBlockFixedHeight(SpanBlock block)
    {
        var pad = EffectivePad(block.Cell, block.Row);
        // A grid resolved boundary by boundary bills the half of each rule the
        // block's own top and bottom ended up with.
        var rules = _collapsedRules is { } resolved && resolved.CellSides.TryGetValue(block.Cell, out var sides)
            ? (sides.Top + sides.Bottom) / 2
            : CollapsedGridEdgeHeight();
        return (pad?.Top ?? 0) + (pad?.Bottom ?? 0) + rules;
    }

    /// <summary>How many of the block's lines, starting at <paramref name="from"/>,
    /// fit in a portion <paramref name="portionH"/> tall. A line only counts when
    /// its WHOLE advance fits, which is what leaves a three-line portion in the
    /// 62.94 pt of content three rows give.</summary>
    private int SpanLinesFitting(SpanBlock block, int from, double portionH)
    {
        var room = portionH - SpanBlockFixedHeight(block);
        var used = 0.0;
        var count = 0;
        for (var li = from; li < block.Lines.Count; li++)
        {
            var pitch = SpanLinePitch(block.Lines[li], block.LineHeight);
            if (used + pitch > room + 1e-3) break;
            used += pitch;
            count++;
        }
        return count;
    }

    /// <summary>Cuts every block the closing page holds only part of: it records
    /// how many lines that page took and re-floors the block's LAST row for what
    /// is left, now that the split is known. The floor could not be settled
    /// before the row loop ran -- how much a block still owes its last row
    /// depends on where the break fell.</summary>
    private void SplitSpanBlocksAtBreak(MultiPageBuildState mp, int nextRow)
    {
        if (!SpanBlockSplitsAtPageBreak || mp.spanBlocks is not { Count: > 0 }) return;
        foreach (var block in mp.spanBlocks)
        {
            if (block.StartRow >= nextRow || block.EndRow <= nextRow) continue;
            var portionH = 0.0;
            foreach (var slice in mp.slices)
                if (slice.RowIndex >= block.StartRow && slice.RowIndex < nextRow)
                    portionH += slice.Height;
            block.Portions ??= new List<int>();
            var already = 0;
            foreach (var taken in block.Portions) already += taken;
            var fits = SpanLinesFitting(block, already, portionH);
            block.Portions.Add(fits);
            ReFloorSplitSpanLastRow(mp, block, nextRow, already + fits);
        }
    }

    /// <summary>Re-floors a cut block's last row against the lines it has left.
    /// The rows it covers on the new page keep their natural heights and the
    /// last one takes the remainder, exactly as an uncut block's does.</summary>
    private void ReFloorSplitSpanLastRow(MultiPageBuildState mp, SpanBlock block, int nextRow, int drawn)
    {
        if (mp.shareFloor is null || !SpanHeightFallsOnLastRow) return;
        var last = Math.Min(block.EndRow, mp.rowPlans.Count) - 1;
        if (last < nextRow || last >= mp.shareFloor.Length) return;
        var demand = SpanBlockFixedHeight(block);
        for (var li = drawn; li < block.Lines.Count; li++)
            demand += SpanLinePitch(block.Lines[li], block.LineHeight);
        var above = 0.0;
        for (var r = nextRow; r < last; r++) above += RowPlanHeight(mp.rowPlans[r]);
        mp.shareFloor[last] = demand - above;
    }

    /// <summary>True when a block's content goes down AHEAD of the cells of the
    /// row it is written with -- a block that opens that row, on the page that
    /// holds its final row. The portion a page break CUT off is written once
    /// the page's rows are, after the last of them: the reference finishes the
    /// page before it writes what the cell managed to fit on it.</summary>
    private static bool SpanWrittenAheadOfRow(SpanBlock block, int contentRow) =>
        block.GridCol == 0 && contentRow == block.EndRow - 1;

    /// <summary>The lines a block writes on the page being drawn: the portion the
    /// layout recorded for it, or everything still undrawn on the page that ends
    /// it. Advances the block's own cursor, so the pages consume the portions in
    /// the order the layout cut them.</summary>
    private static (int Start, int Count) TakeSpanPortion(SpanBlock block)
    {
        var start = block.LinesDrawn;
        var count = block.Portions is { } portions && block.PortionsDrawn < portions.Count
            ? portions[block.PortionsDrawn]
            : block.Lines.Count - start;
        block.LinesDrawn = start + count;
        block.PortionsDrawn++;
        return (start, Math.Max(0, count));
    }
}
