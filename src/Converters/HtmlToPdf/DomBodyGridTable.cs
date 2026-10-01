using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The sheet's 1px collapsed grid border, in points.</summary>
    private const double GridBorderPt = 0.75;

    /// <summary>Collapsed-grid table renderer for the inline-body-margin dialect:
    /// 1px border-collapse grid at real cellpadding, columns from width-% attributes
    /// resolved in source order with the LAST column taking the remainder (the
    /// sheet over-declares 110%), colspan splitting its share equally, char-level
    /// break-all wrapping at the face's real advances, a &lt;br&gt; inside a cell
    /// CONCATENATING (the date cells draw as one line), and
    /// LINE-AT-A-TIME pagination: an over-tall row splits mid-row at the content
    /// limit, its side borders running to the page edge and the continuation page
    /// resuming half a border below the top edge. All geometry measured on the
    /// expected render. Emits runs + border strokes directly and advances the flow
    /// cursor past the table's bottom border and margin-bottom.</summary>
    private static void RenderBodyBoxGridTable(Document doc, FlowPosition cursor,
        string tableHtml, double marginLeft, double contentWidth,
        double pageWidth, double pageHeight, double marginBottom,
        string face, (double asc, double sum) fm, double lineSum,
        Core.PdfDictionary docFontDict)
    {
        var gd = new GridTableState();
        gd.doc = doc;
        gd.tableHtml = tableHtml;
        gd.marginLeft = marginLeft;
        gd.contentWidth = contentWidth;
        gd.pageWidth = pageWidth;
        gd.pageHeight = pageHeight;
        gd.marginBottom = marginBottom;
        gd.face = face;
        gd.fm = fm;
        gd.lineSum = lineSum;
        gd.docFontDict = docFontDict;
        gd.cursor = cursor;
        gd.invc = System.Globalization.CultureInfo.InvariantCulture;

        ReadGridTableAttributes(gd);

        ParseGridRows(gd);
        if (gd.rows.Count == 0) return;

        SolveGridColumns(gd);

        WrapGridCells(gd);

        gd.faceRes = gd.face.Replace(" ", "");
        EnsureGridFonts(gd, gd.cursor.page);

        gd.bops = new StringBuilder();
        gd.limit = gd.pageHeight - gd.marginBottom;
        gd.borderCenter = gd.pageHeight - gd.cursor.y + gd.marTopPt + GridBorderPt / 2;
        HLine(gd, gd.borderCenter);
        foreach (var r in gd.rows)
        {
            RenderGridRow(gd, r);
        }
        FlushBorders(gd, gd.cursor.page);
        // the flow resumes one full border below the bottom stroke's center, plus
        // the table's own margin-bottom
        gd.cursor.y = gd.pageHeight - (gd.borderCenter + GridBorderPt + gd.marBottomPt);
    }
}
