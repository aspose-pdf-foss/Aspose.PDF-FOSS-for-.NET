using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// DNN report helpers: page creation, the touched bottom, text, fills, lines and page breaks.
    private static void EnsurePage(DnnReportState dn, int p)
    {
        while (dn.pageOps.Count <= p)
        {
            dn.pageOps.Add(new List<string>());
            dn.pageStamps.Add(new List<(byte[], double, double, double, double)>());
            dn.pageBottom.Add(dn.marginT);
            dn.moduleTopPerPage.Add(dn.pageOps.Count == 1 ? dn.marginT + DnnModuleTopPt : dn.marginT);
            dn.moduleBotPerPage.Add(0);
        }
    }

    private static void Touch(DnnReportState dn, int p, double bottom)
    {
        EnsurePage(dn, p);
        if (bottom > dn.pageBottom[p]) dn.pageBottom[p] = Math.Min(bottom, dn.bandBottom);
    }

    private static void DrawText(DnnReportState dn, int p, double x, double baseline, string text, bool bold, double size,
        (double R, double G, double B) color)
    {
        if (text.Length == 0) return;
        var faceName = bold ? "Verdana Bold" : "Verdana";
        var face = PosFace(faceName);
        if (face.ttf is null) { faceName = bold ? "Arial Bold" : "Arial"; face = PosFace(faceName); }
        if (face.ttf is null) return;
        var (rn, hex) = Text.Type0FontEmbedder.Embed(dn.fontDict, face.ttf, faceName, text,
            stripSpacesInBaseFont: true);
        EnsurePage(dn, p);
        dn.pageOps[p].Add(Compat.Format(dn.inv,
            $"BT {color.R:F3} {color.G:F3} {color.B:F3} rg /{rn} {size:F2} Tf 1 0 0 1 {x:F2} {dn.pageH - baseline:F2} Tm <{Compat.ToHexString(hex)}> Tj ET\n"));
    }

    private static void FillRect(DnnReportState dn, int p, double x0, double y0, double x1, double y1,
        (double R, double G, double B) c)
    {
        EnsurePage(dn, p);
        dn.pageOps[p].Add(Compat.Format(dn.inv,
            $"q {c.R:F3} {c.G:F3} {c.B:F3} rg {x0:F2} {dn.pageH - y1:F2} {x1 - x0:F2} {y1 - y0:F2} re f Q\n"));
    }

    private static void Line(DnnReportState dn, int p, double x0, double y0, double x1, double y1,
        (double R, double G, double B) c)
    {
        EnsurePage(dn, p);
        dn.pageOps[p].Add(Compat.Format(dn.inv,
            $"q {c.R:F3} {c.G:F3} {c.B:F3} RG 0.75 w {x0:F2} {dn.pageH - y0:F2} m {x1:F2} {dn.pageH - y1:F2} l S Q\n"));
    }

    private static void BreakPage(DnnReportState dn, double need)
    {
        if (dn.y + need <= dn.bandBottom) return;
        dn.moduleBotPerPage[dn.page] = 0;               // box runs off this page
        Touch(dn, dn.page, dn.bandBottom);
        dn.page++;
        EnsurePage(dn, dn.page);
        dn.y = dn.marginT;
    }
}
