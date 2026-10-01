using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal partial class HtmlToPdfConverter
{
    /// <summary>Mso form grid: the page opened at the solved table width and its fonts registered.</summary>
    private static void OpenMsoFormPage(MsoFormGridState mg)
    {
        mg.pageW = 90.0 + MsoBodyInsetPt + mg.tableW + 90.0;
        mg.page = mg.doc.Pages.Add(mg.pageW, 842.0);
        EnsureFont(mg.page, "Helvetica", "F1");
        EnsureFont(mg.page, "Helvetica-Bold", "F2");
        EnsureFont(mg.page, "Times-Roman", "F3");
        EnsureFont(mg.page, "Times-Bold", "F4");
        EnsureFont(mg.page, "Times-BoldItalic", "F5");
        EnsureFont(mg.page, "Helvetica-Oblique", "F6");
        EnsureFont(mg.page, "Helvetica-BoldOblique", "F7");
        EnsureFont(mg.page, "ZapfDingbats", "F8");

        mg.x0 = 90.0 + MsoBodyInsetPt;
        mg.yTop = 72.0 + MsoBodyInsetPt;      // table seats one body inset under the margin
        mg.y = 842.0 - mg.yTop;

        mg.sb = new StringBuilder();
        mg.tsb = new StringBuilder();
    }

    /// <summary>Mso form grid: the column widths solved from natural cell widths, a spanning deficit spread over its columns.</summary>
    private static void SolveMsoColumns(MsoFormGridState mg)
    {
        mg.nCols = 0;
        foreach (var r in mg.rows)
        {
            var c = 0;
            foreach (var mc in r.Cells) c += mc.ColSpan;
            mg.nCols = Math.Max(mg.nCols, c);
        }
        mg.colW = new double[mg.nCols];
        // single-span pins first; a spanning deficit spreads over its columns
        foreach (var r in mg.rows)
        {
            var c = 0;
            foreach (var mc in r.Cells)
            {
                var nat = MsoCellNaturalW(mc);
                if (mc.ColSpan == 1 && nat > mg.colW[c]) mg.colW[c] = nat;
                c += mc.ColSpan;
            }
        }
        for (var pass = 0; pass < 2; pass++)
            foreach (var r in mg.rows)
            {
                var c = 0;
                foreach (var mc in r.Cells)
                {
                    var nat = MsoCellNaturalW(mc);
                    double have = 0;
                    for (var k = 0; k < mc.ColSpan && c + k < mg.nCols; k++) have += mg.colW[c + k];
                    if (mc.ColSpan > 1 && nat > have)
                    {
                        var add = (nat - have) / mc.ColSpan;
                        for (var k = 0; k < mc.ColSpan && c + k < mg.nCols; k++) mg.colW[c + k] += add;
                    }
                    c += mc.ColSpan;
                }
            }
        mg.tableW = 0;
        foreach (var w in mg.colW) mg.tableW += w;
    }
}
