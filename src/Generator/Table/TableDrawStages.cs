using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public partial class Table : BaseParagraph
{
    /// <summary>Draws one row: each cell's background, border, and its paragraphs' text (word-wrapped when the cell wraps) at the cell's padding, then the cursor moves down by the row height.</summary>
    private void DrawTableRow(TableDrawState tk, int rowIdx)
    {
        tk.row = Rows.At(rowIdx);
        tk.rowHeight = CalculateRowHeight(tk.row, tk.colWidths);
        tk.cellX = tk.tableX;

        for (var colIdx = 0; colIdx < tk.row.Cells.Count && colIdx < tk.colWidths.Length; colIdx++)
        {
            DrawTableCell(tk, colIdx);
        }

        tk.currentY -= tk.rowHeight;
    }

    /// <summary>Draws one cell: its background and border, then each paragraph's text at the padding (fragments word-wrapped when the cell wraps), and the pen moves to the next column.</summary>
    private void DrawTableCell(TableDrawState tk, int colIdx)
    {
        tk.cell = tk.row.Cells.At(colIdx);
        tk.cellWidth = GetCellWidth(tk.colWidths, colIdx, tk.cell.ColSpan);
        tk.cellTopY = tk.currentY;

        tk.padding = tk.cell.Margin ?? tk.row.DefaultCellPadding ?? DefaultCellPadding;
        tk.padLeft = tk.padding?.Left ?? 2;
        tk.padRight = tk.padding?.Right ?? 2;
        tk.padTop = tk.padding?.Top ?? 2;
        tk.padBottom = tk.padding?.Bottom ?? 2;

        tk.bgColor = tk.cell.BackgroundColor ?? tk.row.BackgroundColor;
        if (tk.bgColor is not null)
        {
            tk.builder.SetFillColor(tk.bgColor);
            tk.builder.Rectangle(tk.cellX, tk.cellTopY - tk.rowHeight, tk.cellWidth, tk.rowHeight);
            tk.builder.Fill();
        }

        // Draw cell border
        if (!tk.cell.IsNoBorder)
        {
            var cellBorder = tk.cell.Border ?? tk.row.DefaultCellBorder ?? tk.row.Border ?? DefaultCellBorder;
            if (cellBorder is not null)
            {
                DrawBorder(tk.builder, cellBorder, tk.cellX, tk.cellTopY - tk.rowHeight, tk.cellWidth, tk.rowHeight);
            }
        }

        tk.textState = tk.cell.DefaultCellTextState ?? tk.row.DefaultCellTextState ?? DefaultCellTextState;
        tk.fontSize = ResolveCellFontSize(tk.cell, tk.row);
        tk.textX = tk.cellX + tk.padLeft;
        tk.textY = tk.cellTopY - tk.padTop - tk.fontSize;

        foreach (var paragraph in tk.cell.Paragraphs)
        {
            DrawCellParagraph(tk, paragraph);
        }

        tk.cellX += tk.cellWidth;
    }

    /// <summary>Draws one cell paragraph: a text fragment at its own size and colour, word-wrapped over the cell's inner width when the cell wraps, the text baseline stepping down per line.</summary>
    private void DrawCellParagraph(TableDrawState tk, BaseParagraph paragraph)
    {
        if (paragraph is TextFragment tf)
        {
            var fragFontSize = ResolveCellParagraphFontSize(tf, tk.fontSize, tk.cell, tk.row);
            var effectiveTextY = tk.cellTopY - tk.padTop - fragFontSize;

            tk.builder.BeginText();
            tk.builder.SetFont(tk.fontName, fragFontSize);

            // Apply text color
            if (tf.TextState.ForegroundColor is { } fg)
                tk.builder.SetFillColor(fg.R / 255.0, fg.G / 255.0, fg.B / 255.0);
            else if (tk.textState?.ForegroundColor is { } tsFg)
                tk.builder.SetFillColor(tsFg.R / 255.0, tsFg.G / 255.0, tsFg.B / 255.0);
            else
                tk.builder.SetFillColor(0, 0, 0);

            tk.builder.MoveTextPosition(tk.textX, effectiveTextY);

            if (tk.cell.IsWordWrapped && tf.Text.Length > 0)
            {
                var availWidth = tk.cellWidth - tk.padLeft - tk.padRight;
                var lines = WrapText(tf.Text, fragFontSize, availWidth);
                for (var li = 0; li < lines.Count; li++)
                {
                    if (li > 0)
                    {
                        tk.builder.MoveTextPosition(0, -fragFontSize * 1.2);
                    }
                    tk.builder.ShowText(lines[li]);
                }
            }
            else
            {
                tk.builder.ShowText(tf.Text);
            }

            tk.builder.EndText();
            tk.textY -= fragFontSize * 1.2;
        }
    }
}
