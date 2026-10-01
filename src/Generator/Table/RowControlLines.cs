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
    private void RenderControlLines(ContentStreamBuilder builder, List<CellLine> cellLines,
        int firstLine, int lastLine, double leftX, double topY, double lineHeight, string fontName,
        List<(Aspose.Pdf.Forms.RadioButtonOptionField opt, Rectangle rect)>? optionSink = null,
        List<(Aspose.Pdf.Forms.CheckboxField cbf, Rectangle rect)>? checkboxSink = null,
        double? seatBottom = null, Page? fontPage = null)
    {
        var cl = new ControlLinesState();
        cl.exactStack = false;
        if (lastLine - firstLine > 1)
            for (var li = firstLine; li < lastLine; li++)
                if (cellLines[li].Checkbox is not null) { cl.exactStack = true; break; }

        cl.serifText = DwFormCells && fontPage is not null;
        cl.textFont = cl.serifText ? RegisterFont(fontPage!, "Times-Roman") : fontName;
        cl.yCursor = topY;
        cl.walkY = topY;
        for (var li = firstLine; li < lastLine; li++)
        {
            if (!RenderControlLine(cl, li, builder, cellLines, lastLine, leftX, lineHeight, fontName, optionSink, checkboxSink, seatBottom, fontPage)) break;
        }
    }
}
