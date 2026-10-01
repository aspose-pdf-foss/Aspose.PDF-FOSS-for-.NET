using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// Rfq form: 
    private static string N(RfqFormState rq, double v) => v.ToString("0.###", rq.inv);

    private static double Y(RfqFormState rq, double yTd) => rq.pageHeight - yTd;

    // the Arabic faces the expected output drew with; Arial covers the fallback
    private static Text.Font? ArFont(bool bold)
    {
        try
        {
            return Text.FontRepository.TryFindFont("Tahoma",
                bold ? Text.FontStyles.Bold : Text.FontStyles.Regular, ignoreCase: true);
        }
        catch { return null; }
    }

    private static void Fill(RfqFormState rq, string rg, double x0, double top, double x1, double bot)
    {
        rq.fills.AppendLine(rg);
        rq.fills.AppendLine($"{N(rq, x0)} {N(rq, Y(rq, bot))} {N(rq, x1 - x0)} {N(rq, bot - top)} re f");
    }

    private static void Emit(RfqFormState rq, string res, double fs, double x, double baseTd, string text, string rg)
    {
        if (text.Length == 0) return;
        rq.runs.AppendLine($"BT {rg} /{res} {fs.ToString("F2", rq.inv)} Tf "
            + $"1 0 0 1 {N(rq, x)} {N(rq, Y(rq, baseTd))} Tm ({EscapePdfString(text)}) Tj ET");
    }

    private static void EmitAr(RfqFormState rq, bool bold, double fs, double x, double baseTd, string text, string rg)
    {
        if (text.Length == 0) return;
        var face = bold ? rq.tahomaBd : rq.tahoma;
        if (face?.SourceFontData?.TtfData is not { } ttf)
        {
            EmitPositionedRun(rq.page, bold ? "F2" : "F1", fs, x, Y(rq, baseTd), text);
            return;
        }
        var visual = Text.ArabicTextShaper.ContainsArabic(text)
            ? Text.ArabicTextShaper.Shape(text) : text;
        var (rn, hex) = Text.Type0FontEmbedder.Embed(rq.fontDict, ttf,
            bold ? "Tahoma-Bold" : "Tahoma", visual, stripSpacesInBaseFont: true);
        rq.runs.AppendLine($"BT {rg} /{rn} {fs.ToString("F2", rq.inv)} Tf "
            + $"1 0 0 1 {N(rq, x)} {N(rq, Y(rq, baseTd))} Tm <{Compat.ToHexString(hex)}> Tj ET");
    }
}
