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
    /// <summary>Puts a row-spanning cell's SURPLUS height on the last row it
    /// covers. The block is then as tall as the text it holds -- as tall as a
    /// one-row cell holding the same text would be -- while every row it covers
    /// but the last keeps its own natural height, and the row the block is
    /// written with takes the whole difference.
    ///
    /// The default spreads the demand EVENLY over the rows the cell covers, so
    /// each of them grows a share; that is what the generator's grids expect.</summary>
    public bool SpanHeightFallsOnLastRow { get; set; }

    /// <summary>The height a spanning cell's block needs: its padding, the rule
    /// its box bills, and every wrapped line's own advance. It is the height the
    /// same text would make a ONE-ROW cell, which is what the block has to reach
    /// however many rows it covers.</summary>
    private double SpanBlockDemand(SpanBlock block)
    {
        var demand = SpanBlockFixedHeight(block);
        foreach (var line in block.Lines) demand += SpanLinePitch(line, block.LineHeight);
        return demand;
    }

    /// <summary>One span line's advance. A line carrying a declared line box
    /// advances by that box, the same as it does inside an ordinary cell; one
    /// that declared none keeps the block's uniform pitch.</summary>
    private static double SpanLinePitch(CellLine line, double uniformLineHeight) =>
        line.LineBoxAscentEm > 0 && line.LineBoxDescentEm > 0
            ? line.FontSize + line.Leading
            : line.FontSize + line.TopGap;

    /// <summary>The legacy model: every row the cell covers is floored at an EVEN
    /// share of its demand, so a block taller than its rows grows all of them.</summary>
    private void ShareSpanHeightEvenly(MultiPageBuildState mp, SpanBlock block)
    {
        var pad = EffectivePad(block.Cell, block.Row);
        var demand = (pad?.Top ?? 0) + (pad?.Bottom ?? 0);
        foreach (var line in block.Lines) demand += line.FontSize + line.TopGap;
        var rows = Math.Max(1, Math.Min(block.EndRow, mp.rowPlans.Count) - block.StartRow);
        var share = demand / rows;
        for (var r = block.StartRow; r < block.EndRow && r < mp.shareFloor!.Length; r++)
            if (share > mp.shareFloor[r]) mp.shareFloor[r] = share;
    }

    /// <summary>The last-row model: the rows the cell covers keep their natural
    /// heights and the LAST of them is floored at whatever the block still needs
    /// over them. A block that already fits asks for nothing.</summary>
    private void FloorSpanLastRow(MultiPageBuildState mp, SpanBlock block)
    {
        var last = Math.Min(block.EndRow, mp.rowPlans.Count) - 1;
        if (last < block.StartRow || last >= mp.shareFloor!.Length) return;
        var above = 0.0;
        for (var r = block.StartRow; r < last; r++) above += RowPlanHeight(mp.rowPlans[r]);
        var floor = SpanBlockDemand(block) - above;
        if (floor > mp.shareFloor[last]) mp.shareFloor[last] = floor;
    }
}
