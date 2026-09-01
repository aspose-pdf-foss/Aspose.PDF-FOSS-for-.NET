using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The css @page margin rule, lifted out of ConvertFromHtml; it reads and
// writes the conversion state only. Body is verbatim.
    private static void ApplyCssPageMargins(ConvertState cv)
    {
        if (cv.cssPageRule is not { } cp) return;
        if (cp.MarginLeftPt is { } cpL) cv.marginLeft = cpL;
        if (cp.MarginRightPt is { } cpR) cv.marginRight = cpR;
        if (cp.MarginTopPt is { } cpT) cv.marginTop = cpT;
        if (cp.MarginBottomPt is { } cpB) cv.marginBottom = cpB;
        cv.cssPageFirstTopLift = cp.FirstMarginTopPt is { } cpF ? cv.marginTop - cpF : 0.0;
    }
}
