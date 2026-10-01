using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The text and rule draws of the flex-grid layout.
    private static double FWidth(string s, double pt)
        => MeasureFaceText("Times New Roman Bold", s, pt);

    private static void FDraw(FlexGridLayoutState xg, string s, double x, double glyphTopDown, double pt)
    {
        if (xg.fontDictF is null || xg.fSerifB.ttf is null || s.Length == 0) return;
        var baseline = xg.pageHeight - (glyphTopDown + SerifAscEm * pt);
        var (rn, hex) = Text.Type0FontEmbedder.Embed(xg.fontDictF, xg.fSerifB.ttf,
            "Times New Roman Bold", s, stripSpacesInBaseFont: true);
        xg.flow.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(xg.invF,
            $"BT 0 0 0 rg /{rn} {pt:F1} Tf 1 0 0 1 {x:F2} {baseline:F2} Tm <{Compat.ToHexString(hex)}> Tj ET\n")));
    }

    private static void FLine(FlexGridLayoutState xg, double x0, double y0d, double x1, double y1d)
        => xg.flow.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(xg.invF,
            $"q 0 0 0 RG 0.75 w {x0:F2} {xg.pageHeight - y0d:F2} m {x1:F2} {xg.pageHeight - y1d:F2} l S Q\n")));
}
