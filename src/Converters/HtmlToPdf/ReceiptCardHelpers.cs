using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{

    private static string N(ReceiptCardState rk, double v) => v.ToString("0.###", rk.inv);

    // page-down y → PDF y
    private static double Y(ReceiptCardState rk, double yTd) => rk.pageHeight - yTd;

    private static void Emit(ReceiptCardState rk, string res, double fs, double x, double yTd, string text)
    {
        rk.runs.AppendLine("BT 0.4 0.4 0.4 rg");
        rk.runs.Append($"/{res} {fs.ToString("F2", rk.inv)} Tf ");
        rk.runs.Append($"1 0 0 1 {N(rk, x)} {N(rk, Y(rk, yTd))} Tm ");
        rk.runs.AppendLine($"({EscapePdfString(text)}) Tj ET");
    }

    private static double ReceiptW(string t, double fs, bool bold) =>
        MeasureFaceText(bold ? "Helvetica-Bold" : "Helvetica", t, fs);
}
