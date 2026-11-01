using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal partial class HtmlToPdfConverter
{
// Mso form grid: 
    private static string RunRes(MsoRun run)
    {
        if (run.Face == "Arial")
        {
            if (run.Bold) return run.Italic ? "F7" : "F2";
            if (run.Italic) return "F6";
            return "F1";
        }
        if (run.Bold) return run.Italic ? "F5" : "F4";
        return "F3";
    }

    private static string RunFace(MsoRun run)
    {
        if (run.Face == "Arial") return "Arial" + (run.Bold ? " Bold" : "");
        return "Times New Roman" + (run.Bold ? " Bold" : "");
    }

    private static void CloseGroup(MsoFormGridState mg, double yNow)
    {
        if (!mg.groupOpen) return;
        mg.groupOpen = false;
        var hostW = mg.tableW - MsoRosterSideWPt;
        var gl = new StringBuilder("q 0 0 0 RG 1.5 w ");
        gl.Append(Compat.Format(mg.invc, $"{mg.x0:F2} {mg.groupTop:F2} m {mg.x0:F2} {yNow:F2} l S "));
        gl.Append(Compat.Format(mg.invc, $"{mg.x0 + hostW:F2} {mg.groupTop:F2} m {mg.x0 + hostW:F2} {yNow:F2} l S "));
        gl.Append(Compat.Format(mg.invc, $"{mg.x0:F2} {yNow:F2} m {mg.x0 + hostW:F2} {yNow:F2} l S "));
        gl.Append(Compat.Format(mg.invc, $"{mg.x0 + mg.tableW:F2} {mg.groupTop:F2} m {mg.x0 + mg.tableW:F2} {yNow:F2} l S "));
        gl.Append(Compat.Format(mg.invc, $"{mg.x0 + hostW:F2} {yNow:F2} m {mg.x0 + mg.tableW:F2} {yNow:F2} l S "));
        gl.Append("Q\n");
        mg.sb.Append(gl);
    }
}
