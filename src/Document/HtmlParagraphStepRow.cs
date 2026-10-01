using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;

namespace Aspose.Pdf;

public sealed partial class Document
{
    private const double psAckTableRightMargin = 33.6;
    private const double psLandscapeColumnW = 729 * 0.75;

    /// <summary>One procedure-step row, verbatim: the body of LayoutProcedureStepRows' row loop with
    /// the measuring helpers only it uses.</summary>
    private void LayoutStepRow(int prowIdx, List<Converters.HtmlToPdfConverter.StepRow> psRows, HtmlFragment html, FlowLayout flow, Page page, double marginLeft, double marginRight, double marginTop, double marginBottom, double psWrapRight)
    {
        // Where the baseline sits in a line box: the box's leading is
        // split above and below the face's own content area, so a line
        // set tighter than the face rides higher in its box. Arial's
        // ascent and descent are 1854 and 434 per 2048 em.
        // The line box a run of this size sits in when nothing declares
        // one: normal leading on an integer number of css pixels.
        var sr = new StepRowState();
        sr.prow = psRows[prowIdx];
        sr.psContentX = marginLeft + 55.5 + sr.prow.IndentPt;
        sr.psBulletX = marginLeft + 1.5 + sr.prow.IndentPt;
        sr.psRowRight = sr.prow.ContentWidthPt > 0
            ? sr.psContentX + sr.prow.ContentWidthPt
            : sr.prow.AckTable
                ? sr.prow.Landscape ? sr.psContentX + psLandscapeColumnW
                    : page.Width - psAckTableRightMargin
                : psWrapRight;
        sr.psLimit = sr.psRowRight - sr.psContentX;
        OpenStepRow(sr, psRows, prowIdx, flow, page);
        LayoutStepRowItems(sr, flow, page, marginRight, psWrapRight);
        DrawStepRowAcknowledgement(sr, flow, page, marginRight, psWrapRight);
        CloseStepRow(sr, flow, page, marginRight, psWrapRight);
        // the row's bottom margin collapses with the next row's top
        // one rather than adding to it, and is dropped altogether
        // where the sheet ends - so only the top margin is spent
    }
}
