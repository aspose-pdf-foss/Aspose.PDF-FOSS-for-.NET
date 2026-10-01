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

    /// <summary>An image paragraph in a cell: its scaled box, the row height it demands, its alignment inside the cell and the draw it books.</summary>
    private bool? PlanImageParagraph(Image cellImg, RowPlanColumnState pc, RowPlanState rp, int col, Row row, double[] colWidths, double svgFillHeight)
    {
        var ri = new RowPlanImageState();
        if (ResolveImageSource(ri, cellImg, pc, rp, col, colWidths, svgFillHeight) is { } resolveImageSourceResult) return resolveImageSourceResult;
        ResolveImageDisplaySize(ri, cellImg, pc, svgFillHeight);
        BookImageLines(ri, cellImg, pc, rp, col, row);
        return true;
    }

    /// <summary>A picture riding a <see cref="FloatingBox"/> seated in a cell: out of the cell's flow, it books
    /// no line and draws at the box's size from the cell's content origin, offset by the box's Left/Top;
    /// a box with no size of its own takes the picture's Fix box; one with neither draws nothing.</summary>
    private void PlanSeatedImage(FloatingBox seat, Image seatImg, RowPlanColumnState pc, RowPlanState rp, int col)
    {
        var w = seat.Width > 0 ? seat.Width : seatImg.FixWidth;
        var h = seat.Height > 0 ? seat.Height : seatImg.FixHeight;
        if (w <= 0 || h <= 0 || ReadImageBytes(seatImg) is not { Length: > 0 } bytes) return;
        AddCellImage(rp.plan, col, new CellImage
        {
            Data = bytes, Width = w, Height = h, Align = HorizontalAlignment.Left,
            LineOffset = pc.lines.Count, Seated = true, SeatLeft = seat.Left, SeatTop = seat.Top,
        });
    }

    /// <summary>A nested table in a cell: it is measured at the cell's width, its rows become lines of the outer row and its draws are booked at the cell origin.</summary>
    private bool? PlanInnerTableParagraph(Table inner, RowPlanColumnState pc, RowPlanState rp, int col, Row row)
    {
        // Generator dialect: the inner grid renders IN PLACE as a table of
        // its own (a logo grid's bordered cells and
        // image draw inside the host cell, starting at the host's inner
        // corner). A measure pass at the host cell's inner box sizes it;
        // one exact-height reserve line holds its place in the row.
        if (GeneratorCellModel && _buildPage is not null)
        {
            if (PlanGeneratorCellInnerTable(inner, pc, rp, col, row) is { } planGeneratorCellInnerTableResult) return planGeneratorCellInnerTableResult;
        }
        // The REAL slice pass (opt-in): measure the inner grid at the
        // cell's content width and reserve its height; the draw pass
        // renders it in place. Falls back to the legacy flatten when the
        // measurement cannot run.
        if (NestedTableRender && _buildPage is not null)
        {
            if (PlanNestedRenderInnerTable(inner, pc, rp, col) is { } planNestedRenderInnerTableResult) return planNestedRenderInnerTableResult;
        }
        var innerRows = inner.Rows;
        Consider(rp, pc.defaultFontSize * 1.2, pc.defaultFontSize);
        for (int ri = 0; ri < innerRows.Count; ri++)
        {
            var irow = innerRows.At(ri);
            var segments = new List<string>();
            for (int ici = 0; ici < irow.Cells.Count; ici++)
            {
                var icell = irow.Cells.At(ici);
                foreach (var ip in icell.Paragraphs)
                {
                    string? rawText = null;
                    if (ip is TextFragment itf) rawText = itf.Text;
                    else if (ip is HtmlFragment ihtml) rawText = HtmlFragment.StripHtmlTags(ihtml.HtmlContent ?? "");
                    if (string.IsNullOrEmpty(rawText)) continue;
                    foreach (var part in rawText.Split('\n'))
                    {
                        var trimmed = part.Trim();
                        if (trimmed.Length > 0) segments.Add(trimmed);
                    }
                }
            }
            // Ensure each inner row renders a minimum of one blank
            // line when it carries non-text content; otherwise the
            // height collapses and pagination under-counts.
            if (segments.Count == 0) segments.Add(" ");
            foreach (var seg in segments)
            {
                foreach (var l in WrapText(seg, pc.defaultFontSize, pc.availWidth))
                    pc.lines.Add(new CellLine { Text = l, FontSize = pc.defaultFontSize, ForegroundColor = pc.textState?.ForegroundColor });
            }
        }
        return true;
    }
}
