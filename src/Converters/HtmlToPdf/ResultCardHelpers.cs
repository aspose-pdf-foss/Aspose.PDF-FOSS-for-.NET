using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The result-card render helpers: the Arial line box, its baseline drop, the run emitter and the horizontal rule.
    private static double ArialLine(ResultCardState rc, double fs) => Math.Round(fs / PxPt * rc.arial.sum, MidpointRounding.AwayFromZero) * PxPt;

    private static double Drop(ResultCardState rc, double fs, double box, (double asc, double sum) fm)
        => (box - fs * fm.sum) / 2 + fs * fm.asc;

    private static void Run(ResultCardState rc, string res, double fs, double x, double yTd, string text, Color? col = null)
    {
        rc.sb.Append("BT ");
        if (col is { } c)
            rc.sb.Append(Compat.Format(rc.inv, $"{c.R / 255.0:0.###} {c.G / 255.0:0.###} {c.B / 255.0:0.###} rg "));
        rc.sb.Append(Compat.Format(rc.inv,
            $"/{res} {fs:0.##} Tf 1 0 0 1 {x:F2} {rc.pageHeight - yTd:F2} Tm ({EscapePdfString(text)}) Tj "));
        if (col is not null) rc.sb.Append("0 g ");
        rc.sb.AppendLine("ET");
    }

    private static void HLine(ResultCardState rc, double x0, double x1, double yTd, Color c)
        => rc.sb.AppendLine(Compat.Format(rc.inv,
            $"q {c.R / 255.0:0.###} {c.G / 255.0:0.###} {c.B / 255.0:0.###} RG 0.75 w " +
            $"{x0:F2} {rc.pageHeight - yTd:F2} m {x1:F2} {rc.pageHeight - yTd:F2} l S Q"));
}
