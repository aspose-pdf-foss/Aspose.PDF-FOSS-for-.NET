using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// Positioned form drawing helpers: per-page builders, y mapping, text, fills, strokes, boxes and circles.
    private static StringBuilder SbFor(PositionedFormState pf, double yTd) => yTd <= pf.contentBottom ? pf.sb1 : pf.sb2;

    private static double MapY(PositionedFormState pf, double yTd) => yTd <= pf.contentBottom ? yTd : yTd - pf.contentBottom + 72.0;

    private static void PfText(PositionedFormState pf, double x, double yTd, string t, bool bold = false, double size = 0)
    {
        var sz = size > 0 ? size : pf.fs;
        SbFor(pf, yTd).AppendLine(Compat.Format(pf.inv,
            $"BT /{(bold ? "F9" : "F8")} {sz:0.##} Tf 1 0 0 1 {x:F2} {pf.pageHeight - MapY(pf, yTd):F2} Tm ({EscapePdfString(t)}) Tj ET"));
    }

    private static void Fill(PositionedFormState pf, double x, double yTd, double w, double h, Color c)
    {
        // split across the page boundary
        var y0 = yTd; var y1 = yTd + h;
        foreach (var (a, b) in new[] { (Math.Min(y0, pf.contentBottom), Math.Min(y1, pf.contentBottom)),
                                       (Math.Max(y0, pf.contentBottom), Math.Max(y1, pf.contentBottom)) })
        {
            if (b - a < 0.01) continue;
            var sb = a >= pf.contentBottom ? pf.sb2 : pf.sb1;
            var ay = a >= pf.contentBottom ? a - pf.contentBottom + 72.0 : a;
            var by = b >= pf.contentBottom ? b - pf.contentBottom + 72.0 : b;
            sb.AppendLine(Compat.Format(pf.inv,
                $"q {c.R / 255.0:0.###} {c.G / 255.0:0.###} {c.B / 255.0:0.###} rg {x:F2} {pf.pageHeight - by:F2} {w:F2} {by - ay:F2} re f Q"));
        }
    }

    private static void HStroke(PositionedFormState pf, double x0, double x1, double yTd, Color c, double w)
    {
        SbFor(pf, yTd).AppendLine(Compat.Format(pf.inv,
            $"q {c.R / 255.0:0.###} {c.G / 255.0:0.###} {c.B / 255.0:0.###} RG {w:0.##} w {x0:F2} {pf.pageHeight - MapY(pf, yTd):F2} m {x1:F2} {pf.pageHeight - MapY(pf, yTd):F2} l S Q"));
    }

    private static void VStroke(PositionedFormState pf, double x, double y0Td, double y1Td, Color c, double w)
    {
        foreach (var (a, b) in new[] { (Math.Min(y0Td, pf.contentBottom), Math.Min(y1Td, pf.contentBottom)),
                                       (Math.Max(y0Td, pf.contentBottom), Math.Max(y1Td, pf.contentBottom)) })
        {
            if (b - a < 0.01) continue;
            var sb = a >= pf.contentBottom ? pf.sb2 : pf.sb1;
            var ay = a >= pf.contentBottom ? a - pf.contentBottom + 72.0 : a;
            var by = b >= pf.contentBottom ? b - pf.contentBottom + 72.0 : b;
            sb.AppendLine(Compat.Format(pf.inv,
                $"q {c.R / 255.0:0.###} {c.G / 255.0:0.###} {c.B / 255.0:0.###} RG {w:0.##} w {x:F2} {pf.pageHeight - ay:F2} m {x:F2} {pf.pageHeight - by:F2} l S Q"));
        }
    }

    private static void Box(PositionedFormState pf, double x, double yTd, double w, double h, Color c, double bw)
    {
        HStroke(pf, x, x + w, yTd + bw / 2, c, bw);
        HStroke(pf, x, x + w, yTd + h - bw / 2, c, bw);
        VStroke(pf, x + bw / 2, yTd, yTd + h, c, bw);
        VStroke(pf, x + w - bw / 2, yTd, yTd + h, c, bw);
    }

    private static void Circle(PositionedFormState pf, double cx, double yTd, double r)
    {
        const double k = 0.5523;
        var cy = pf.pageHeight - MapY(pf, yTd);
        SbFor(pf, yTd).AppendLine(Compat.Format(pf.inv,
            $"q 0 0 0 RG 0.75 w {cx + r:F2} {cy:F2} m " +
            $"{cx + r:F2} {cy + k * r:F2} {cx + k * r:F2} {cy + r:F2} {cx:F2} {cy + r:F2} c " +
            $"{cx - k * r:F2} {cy + r:F2} {cx - r:F2} {cy + k * r:F2} {cx - r:F2} {cy:F2} c " +
            $"{cx - r:F2} {cy - k * r:F2} {cx - k * r:F2} {cy - r:F2} {cx:F2} {cy - r:F2} c " +
            $"{cx + k * r:F2} {cy - r:F2} {cx + r:F2} {cy - k * r:F2} {cx + r:F2} {cy:F2} c S Q"));
    }
}
