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
    /// <summary>One column of a row slice render, verbatim: the body of RenderRowSlice's
    /// per-column loop. The cell cursor advances through <paramref name="cellX"/>.</summary>
    private void RenderRowSliceColumn(int col, ref double cellX, ContentStreamBuilder builder, RowSlice slice,
        double[] colWidths, string fontName, int[] cellMap,
        List<(Rectangle rect, Hyperlink link)>? links, List<(byte[] data, Rectangle rect)>? imageSink,
        List<(Aspose.Pdf.Forms.RadioButtonOptionField opt, Rectangle rect)>? optionSink, List<byte[]>? graphSink,
        List<(Aspose.Pdf.Forms.CheckboxField cbf, Rectangle rect)>? checkboxSink, Page? page,
        List<(Note note, double x, double baseline, double size)>? footnoteSink)
    {
        var rc = new RowColumnState();
        if (!RenderRowColumnBox(rc, col, ref cellX, builder, slice, colWidths, fontName, cellMap, links, imageSink, optionSink, graphSink, checkboxSink, page, footnoteSink)) return;
        RenderRowColumnContent(rc, col, ref cellX, builder, slice, colWidths, fontName, cellMap, links, imageSink, optionSink, graphSink, checkboxSink, page, footnoteSink);
        RenderRowColumnChrome(rc, col, ref cellX, builder, slice, colWidths, fontName, cellMap, links, imageSink, optionSink, graphSink, checkboxSink, page, footnoteSink);
    }
}
