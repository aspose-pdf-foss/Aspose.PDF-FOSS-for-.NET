using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal partial class HtmlToPdfConverter
{
// Mso form grid: 
    private static string RunRes(MsoFormGridState mg, MsoRun run) => run.Face == "Arial"
        ? (run.Bold ? (run.Italic ? "F7" : "F2") : run.Italic ? "F6" : "F1")
        : run.Bold ? (run.Italic ? "F5" : "F4") : "F3";

    private static string RunFace(MsoFormGridState mg, MsoRun run) => run.Face == "Arial"
        ? "Arial" + (run.Bold ? " Bold" : "")
        : "Times New Roman" + (run.Bold ? " Bold" : "");

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
