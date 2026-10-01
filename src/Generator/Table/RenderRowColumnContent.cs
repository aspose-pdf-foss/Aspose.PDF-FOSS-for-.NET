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
    // pt-styled fragment seat: every grid cell's text bottom rides a
    // CONSTANT 1.75 pt deeper than the legacy seat (measured on the
    // render: 1.8 at 10 pt rows, 1.7 at the 8 pt card — a fixed
    // offset, not an em share, and independent of the cell's borders).
    private const double PtGridSeatDropPt = 1.75;

    /// <summary>The content stage of a column render: the cell lines, images and controls, verbatim.</summary>
    private void RenderRowColumnContent(RowColumnState rc)
    {
        ResolveCellContentBox(rc);
        OffsetVerticallyAlignedLines(rc);
        // A bordered cell's content starts at the border's inner edge with no
        // implicit horizontal padding ("x" seats 5 pt in from a
        // 5 pt border, not 5+2); explicit padding still applies.
        if (!SeatCellLineRange(rc)) return;
        if (!(rc.firstLine < rc.lastLine)) return;
        ScanCellLineKinds(rc);

        if (rc.hasOption)
        {
            // Form-control lines need path drawing (the glyph) interleaved with
            // text, which a single text object can't hold — render line by line.
            RenderControlLines(rc.builder, rc.cellLines!, rc.firstLine, rc.lastLine,
                rc.cellX + rc.padLeft, rc.slice.TopY - rc.contentTop, rc.slice.Plan.LineHeight, rc.fontName, rc.optionSink, rc.checkboxSink,
                rc.slice.TopY - rc.slice.Height + (rc.padding?.Bottom ?? 0), rc.page);
        }
        else if (rc.anyNonLeft || rc.anyType0 || rc.cssCell || rc.anyBoxes || rc.anyLinks || rc.xmlMixedSizes)
        {
            RenderStyledCellLines(rc);
        }
        else
        {
            RenderPlainCellLines(rc);
        }

    }
}
