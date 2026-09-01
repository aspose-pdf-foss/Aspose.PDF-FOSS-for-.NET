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
        RowPlanParagraphText(pp, paragraph, pc, rp, col, row, colWidths, cellMap, gridToCell, effRowSpan, svgFillHeight);
        return true;
    }
}
