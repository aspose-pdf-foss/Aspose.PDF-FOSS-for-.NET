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
    /// <returns>The running pen after this column, however the column ended.</returns>
    private double RenderRowSliceColumn(int col, double cellX, ContentStreamBuilder builder, RowSlice slice,
        double[] colWidths, string fontName, int[] cellMap,
        List<(Rectangle rect, Hyperlink link)>? links, List<(byte[] data, Rectangle rect)>? imageSink,
        List<(Aspose.Pdf.Forms.RadioButtonOptionField opt, Rectangle rect)>? optionSink, List<byte[]>? graphSink,
        List<(Aspose.Pdf.Forms.CheckboxField cbf, Rectangle rect)>? checkboxSink, Page? page,
        List<(Note note, double x, double baseline, double size)>? footnoteSink,
        List<(ReservedBlock block, ReservedPart part, Rectangle rect)>? blockSink)
    {
        var rc = new RowColumnState();
        rc.col = col;
        rc.builder = builder;
        rc.slice = slice;
        rc.colWidths = colWidths;
        rc.fontName = fontName;
        rc.cellMap = cellMap;
        rc.links = links;
        rc.imageSink = imageSink;
        rc.blockSink = blockSink;
        rc.optionSink = optionSink;
        rc.graphSink = graphSink;
        rc.checkboxSink = checkboxSink;
        rc.page = page;
        rc.footnoteSink = footnoteSink;
        rc.cellX = cellX;
        if (!RenderRowColumnBox(rc)) return rc.cellX;
        RenderRowColumnContent(rc);
        RenderRowColumnChrome(rc);
        return rc.cellX;
    }
}
