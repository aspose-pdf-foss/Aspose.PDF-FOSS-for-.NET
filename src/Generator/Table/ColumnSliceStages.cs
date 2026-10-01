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
    /// <summary>The stages of the column-slice table: one source row at a time.</summary>
    private void AddColumnSliceRow(ColumnSliceState sl, int r)
    {
        var row = Rows.At(r);
        double fullRowH = 0;
        try
        {
            var fullPlan = BuildRowPlan(row, sl.colWidths, sl.fullMap);
            if (fullPlan.LineCount > 0)
                fullRowH = (fullPlan.LineCount - 1) * fullPlan.LineHeight
                    + fullPlan.TightLine + fullPlan.VertPadding;
        }
        catch { fullRowH = 0; }
        var bandRow = sl.band.Rows.Add();
        bandRow.Border = row.Border;
        bandRow.DefaultCellBorder = row.DefaultCellBorder;
        bandRow.DefaultCellPadding = row.DefaultCellPadding;
        bandRow.DefaultCellTextState = row.DefaultCellTextState;
        bandRow.BackgroundColor = row.BackgroundColor;
        bandRow.FixedRowHeight = row.FixedRowHeight;
        bandRow.MinRowHeight = Math.Max(fullRowH, row.MinRowHeight);
        bandRow.MinRowHeightIncludesRules = fullRowH >= row.MinRowHeight;
        bandRow.VerticalAlignment = row.VerticalAlignment;

        // Two passes over the same row: the repeating prefix [0, repeat) and
        // the slice's own chunk [colStart, colEnd). A repeat-prefix cell keeps
        // its text in EVERY slice; a chunk cell only where it starts.
        for (var range = 0; range < 2; range++)
        {
            var rs = range == 0 ? 0 : sl.colStart;
            var re = range == 0 ? sl.repeat : sl.colEnd;
            if (re <= rs) continue;
            var gridPos = 0;
            for (var ci = 0; ci < row.Cells.Count && gridPos < re; ci++)
            {
                var cell = row.Cells.At(ci);
                var span = Math.Max(1, cell.ColSpan);
                var cellStart = gridPos;
                var cellEnd = gridPos + span;
                gridPos = cellEnd;
                var isStart = range == 0
                    ? cellStart < sl.repeat
                    : cellStart >= sl.colStart && cellStart < sl.colEnd;
                var overlap = Math.Min(cellEnd, re) - Math.Max(cellStart, rs);
                if (overlap <= 0) continue;
                var bandCell = new Cell
                {
                    ColSpan = overlap,
                    RowSpan = cell.RowSpan,
                    Border = cell.Border,
                    BackgroundColor = cell.BackgroundColor,
                    Margin = cell.Margin,
                    IsNoBorder = cell.IsNoBorder,
                    DefaultCellTextState = cell.DefaultCellTextState,
                    IsWordWrapped = cell.IsWordWrapped,
                    VerticalAlignment = cell.VerticalAlignment,
                    Alignment = cell.Alignment,
                    BackgroundImage = cell.BackgroundImage,
                    SpanCutLeft = cellStart < rs,
                    SpanCutRight = cellEnd > re,
                };
                if (isStart)
                    bandCell.Paragraphs = cell.Paragraphs;
                bandRow.Cells.Add(bandCell);
            }
        }
    }
}
