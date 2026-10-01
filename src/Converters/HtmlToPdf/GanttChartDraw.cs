using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// Gantt chart: 
    private static void GcText(GanttChartState gc, double size, double x, double y, string text, string face, (double R, double G, double B) ink)
        => gc.texts.Add((size, x, y, text, face, ink));

    private static void Fill(GanttChartState gc, (double R, double G, double B) c, double x, double topY, double w, double h)
        => gc.sb.Append(Compat.Format(gc.inv,
            $"q {c.R:0.###} {c.G:0.###} {c.B:0.###} rg {x:F2} {gc.pageHeight - topY - h:F2} {w:F2} {h:F2} re f Q\n"));

    private static void Stroke(GanttChartState gc, (double R, double G, double B) c, double w, double x0, double y0, double x1, double y1)
        => gc.sb.Append(Compat.Format(gc.inv,
            $"q {c.R:0.###} {c.G:0.###} {c.B:0.###} RG {w:0.##} w " +
            $"{x0:F2} {gc.pageHeight - y0:F2} m {x1:F2} {gc.pageHeight - y1:F2} l S Q\n"));
}
