using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// UBL invoice drawing helpers: flattening, text runs, fills and rules.
    // tags drop without a stand-in space (the sheet's spans butt together:
    // DK-8900), and NBSP survives the collapse (the
    // nbsp-spaced runs at their two-space width)
    private static string Flat(UblInvoiceState ub, string frag) => Regex.Replace(DecodeEntities(
        Regex.Replace(frag, @"<[^>]+>", "")), @"[ \t\r\n]+", " ").Trim();

    private static void Emit(UblInvoiceState ub, string res, double fs, double x, double yTd, string text)
        => EmitPositionedRun(ub.page, res, fs, x, ub.pageHeight - yTd, text);

    private static void EmitRight(UblInvoiceState ub, string res, string face, double fs, double x1, double yTd, string text)
        => Emit(ub, res, fs, x1 - MeasureFaceText(face, text, fs), yTd, text);

    private static void FillW(UblInvoiceState ub, double x, double yTd, double w, double h)
        => ub.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(ub.invc,
            $"q 1 1 1 rg {x:F2} {ub.pageHeight - yTd - h:F2} {w:F2} {h:F2} re f Q\n")));

    private static void Rule(UblInvoiceState ub, double x0, double x1, double yTd, double w)
        => ub.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(ub.invc,
            $"q 0.533 0.533 0.533 RG {w:0.##} w {x0:F2} {ub.pageHeight - yTd:F2} m {x1:F2} {ub.pageHeight - yTd:F2} l S Q\n")));
}
