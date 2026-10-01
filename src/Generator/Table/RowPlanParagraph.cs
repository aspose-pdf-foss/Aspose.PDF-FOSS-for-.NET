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
    /// <summary>One paragraph of a row-plan column, verbatim: the body of BuildRowPlanColumn's
    /// paragraph loop. Returns false where the loop broke out; a continue became return true.</summary>
    private bool BuildRowPlanParagraph(BaseParagraph paragraph, RowPlanColumnState pc, RowPlanState rp, int col, Row row, double[] colWidths, int[] cellMap, int[]? gridToCell, int[]? effRowSpan, double svgFillHeight)
    {
        if (RowPlanParagraphObjects(paragraph, pc, rp, col, row, colWidths, cellMap, gridToCell, effRowSpan, svgFillHeight)) return true;
        var pp = new RowPlanParagraphState();
        var linesBeforePara = pc.lines.Count;
        RowPlanParagraphText(pp, paragraph, pc, rp, col, row, colWidths, cellMap, gridToCell, effRowSpan, svgFillHeight);
        // A list item's marker hangs before the paragraph's first text line.
        // ...and a ONE-line item whose marker's box (the marker face's ascent + descent at the marker
        // size) is taller than its line stands on the marker's box, its text at the top (measured: a
        // Times 12 marker under an Arial 10 line makes the item 13.28, not 11.25; a multi-line item
        // takes its lines' stack).
        if (paragraph is TextFragment { HtmlListMarker: { } listMarker })
        {
            var textLines = 0;
            CellLine? firstText = null;
            for (var mi = linesBeforePara; mi < pc.lines.Count; mi++)
                if (pc.lines[mi].Text.Length > 0) { textLines++; firstText ??= pc.lines[mi]; }
            if (firstText is not null)
            {
                firstText.Marker = listMarker;
                if (textLines == 1 && firstText.BoxH > 0 && firstText.BoxH < listMarker.BoxPt)
                    firstText.BoxH = listMarker.BoxPt;
            }
        }
        return true;
    }
}
