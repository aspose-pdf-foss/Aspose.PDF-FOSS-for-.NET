using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The print-invoice render helpers: text measure, the run drawer and the dashed rule.
    private static double Measure(PrintInvoiceState pv, string t2, bool b2, bool i2)
        => MeasureFaceText((b2, i2) switch { (true, _) => "Calibri Bold", (_, true) => "Calibri Italic", _ => "Calibri" }, t2, pv.fontPt);

    private static void Draw(PrintInvoiceState pv, string t2, double x, double glyphTop, bool b2, bool i2)
    {
        (byte[]? ttf, Text.GlyphOutlineParser? parser, double upm) f2;
        if (b2 && pv.bold.ttf is not null) f2 = pv.bold;
        else if (i2 && pv.ital.ttf is not null) f2 = pv.ital;
        else f2 = pv.reg;
        if (f2.ttf is null || t2.Length == 0) return;
        var baseline = 842.0 - glyphTop - PrintCalibriAscEm * pv.fontPt;
        var (rn, hex) = Text.Type0FontEmbedder.Embed(pv.fontDict, f2.ttf,
            (b2, i2) switch { (true, _) => "Calibri Bold", (_, true) => "Calibri Italic", _ => "Calibri" }, t2,
            stripSpacesInBaseFont: true);
        pv.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(pv.inv,
            $"BT 0 0 0 rg /{rn} {pv.fontPt:F1} Tf 1 0 0 1 {x:F2} {baseline:F2} Tm <{Compat.ToHexString(hex)}> Tj ET\n")));
    }

    private static void Dash(PrintInvoiceState pv, double xA, double xB, double yTd)
        => pv.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(pv.inv,
            $"q 0 0 0 RG 0.75 w [1 0.5] 0 d {xA:F2} {842.0 - yTd:F2} m {xB:F2} {842.0 - yTd:F2} l S Q\n")));
}
