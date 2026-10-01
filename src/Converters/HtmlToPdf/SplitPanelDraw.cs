using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// Split panel: 
    private static void FillRect(SplitPanelState sp, double x0, double topTd, double x1, double botTd, Color c)
        => sp.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(sp.invc,
            $"q {c.R / 255.0:0.###} {c.G / 255.0:0.###} {c.B / 255.0:0.###} rg " +
            $"{x0:F2} {sp.pageHeight - botTd:F2} {x1 - x0:F2} {botTd - topTd:F2} re f Q\n")));
}
