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
    private static (List<List<string>> headLines, double headH, List<(double h, List<(List<double> lhs, List<(int li, double x, Converters.HtmlToPdfConverter.StepSeg? seg, string? txt)> pieces)>)> laidRows, double totalH, double[] declared) PsLayoutTable(Page page, StepRowState sr, Converters.HtmlToPdfConverter.StepTable pt)
    {
        var pl = new PsTableLayoutState();
        pl.page = page;
        pl.sr = sr;
        pl.pt = pt;
        pl.cfs = pl.pt.CellFontPt > 0 ? pl.pt.CellFontPt : psFs;
        pl.kfs = pl.cfs / psFs;
        pl.baseLine = pl.pt.FormRhythm ? PsCssLineBox(pl.cfs) : 11.4 * pl.kfs;
        pl.declared = PsColumnWidths(pl.pt, pl.cfs, pl.kfs);
        pl.visible = new double[pl.declared.Length];
        pl.used = 0.0;
        for (var c = 0; c < pl.declared.Length; c++)
        {
            pl.visible[c] = Math.Max(0, Math.Min(pl.declared[c], pl.pt.WidthPt - pl.used));
            pl.used += pl.declared[c];
        }
        pl.headLines = new List<List<string>>();
        pl.headH = 0.0;
        for (var c = 0; c < pl.pt.Header.Count && c < pl.visible.Length; c++)
        {
            var iw = Math.Max(pl.visible[c] - 4.5, 1.0);
            var ls = PsWrap(pl.pt.Header[c], 10.5, true, iw);
            pl.headLines.Add(ls);
            pl.headH = Math.Max(pl.headH, Math.Max(12.75, ls.Count * 12.24));
        }

        pl.laidRows = new List<(double h, List<(List<double> lhs, List<(int li, double x, Converters.HtmlToPdfConverter.StepSeg? seg, string? txt)> pieces)>)>();
        pl.psTblX = PsTableX(pl.sr, pl.pt);
        foreach (var row in pl.pt.Rows)
        {
            LayoutStepTableRow(pl, row);
        }

        pl.totalH = pl.headH + PsCellGap(pl.pt);
        foreach (var lr in pl.laidRows) pl.totalH += lr.h;
        return (pl.headLines, pl.headH, pl.laidRows, pl.totalH, pl.declared);
    }
}
