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
    private static void PsRenderTableSegment(StepRowState sr, FlowLayout flow, Converters.HtmlToPdfConverter.StepTable pt,
        List<List<string>> headLines, double headH,
        List<(double h, List<(List<double> lhs, List<(int li, double x, Converters.HtmlToPdfConverter.StepSeg? seg, string? txt)> pieces)>)> laidRows,
        double[] declared, int firstRow = 0)
    {
        var ts = new TableSegmentState();
        ts.sr = sr;
        ts.flow = flow;
        ts.pt = pt;
        ts.headLines = headLines;
        ts.headH = headH;
        ts.laidRows = laidRows;
        ts.declared = declared;
        ts.firstRow = firstRow;
        ts.cfs = ts.pt.CellFontPt > 0 ? ts.pt.CellFontPt : psFs;
        ts.kfs = ts.cfs / psFs;
        ts.gap = PsCellGap(ts.pt);
        ts.totalH = ts.headH + ts.gap;
        foreach (var lr in ts.laidRows) ts.totalH += lr.h;
        ts.tx0 = PsTableX(ts.sr, ts.pt);
        ts.txR = Math.Min(ts.tx0 + ts.pt.WidthPt, ts.sr.psRowRight);
        ts.topY = ts.flow.CurrentY;
        ts.tb2 = new Content.ContentStreamBuilder();
        ts.tb2.SaveState();
        if (ts.sr.psBulletPending) PsDrawBullet(ts.sr, ts.flow, ts.tb2, ts.topY - psAscent);

        ts.bgY = ts.topY - ts.headH - ts.gap;
        DrawTableSegmentRowBackgrounds(ts);
        ts.tb2.SetFillColor(Color.Black);

        // grid
        ts.tb2.SetStrokeGray(0.5).SetLineWidth(0.75);
        DrawTableSegmentRules(ts);
        ts.tb2.SetStrokeGray(0);

        ts.thRes = Table.RegisterFont(ts.flow.CurrentPage, "Helvetica-Bold");
        ts.hx = ts.tx0 + ts.gap;
        RenderTableSegmentHead(ts);

        ts.rowTop = ts.topY - ts.headH - ts.gap;
        RenderTableSegmentRows(ts);
        ts.tb2.RestoreState();
        ts.flow.InjectContentAtCursor(ts.tb2.Build());
        ts.flow.AdvanceY(ts.totalH);
    }
}
