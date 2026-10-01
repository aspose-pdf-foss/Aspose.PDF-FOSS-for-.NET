using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The access-form render helpers: flattened markup text, numeric parsing, the page flush and new page, the run emitter, the question box and the rule.
    private static string Flat(AccessFormState af, string s) => CollapseWs(DecodeEntities(
        Regex.Replace(s, @"<[^>]+>", " "))).Trim();

    private static string N(AccessFormState af, double v) => v.ToString("0.###", af.inv);

    private static void FlushPage(AccessFormState af, double cardBottom, bool lastPage)
    {
        // ground + card behind everything already emitted for this page
        var bg = new StringBuilder();
        bg.AppendLine("0.961 0.961 0.961 rg");
        bg.AppendLine($"{N(af, AfGroundLeft)} {N(af, af.pageHeight - AfGroundBottom)} {N(af, AfGroundRight - AfGroundLeft)} {N(af, AfGroundBottom - AfGroundTop)} re f");
        var top = af.firstPage ? AfCardTop : AfGroundTop;
        // the 10px box-shadow: a mid-grey rim under the card (the blur is
        // approximated by a solid band carrying the same ink)
        var shTop = af.firstPage ? top - AfShadowPt : top;
        var shBottom = lastPage ? cardBottom + AfShadowPt : cardBottom;
        bg.AppendLine("0.8 0.8 0.8 rg");
        bg.AppendLine($"{N(af, AfCardLeft - AfShadowPt)} {N(af, af.pageHeight - shBottom)} {N(af, AfCardRight - AfCardLeft + 2 * AfShadowPt)} {N(af, shBottom - shTop)} re f");
        bg.AppendLine("1 1 1 rg");
        bg.AppendLine($"{N(af, AfCardLeft)} {N(af, af.pageHeight - cardBottom)} {N(af, AfCardRight - AfCardLeft)} {N(af, cardBottom - top)} re f");
        // the rgba(0,0,0,.15) card edge, flattened on white
        bg.AppendLine("0.85 0.85 0.85 RG 0.75 w");
        bg.AppendLine($"{N(af, AfCardLeft + 0.38)} {N(af, af.pageHeight - cardBottom)} m {N(af, AfCardLeft + 0.38)} {N(af, af.pageHeight - top)} l S");
        bg.AppendLine($"{N(af, AfCardRight - 0.38)} {N(af, af.pageHeight - cardBottom)} m {N(af, AfCardRight - 0.38)} {N(af, af.pageHeight - top)} l S");
        if (af.firstPage)
            bg.AppendLine($"{N(af, AfCardLeft)} {N(af, af.pageHeight - top - 0.38)} m {N(af, AfCardRight)} {N(af, af.pageHeight - top - 0.38)} l S");
        if (lastPage)
            bg.AppendLine($"{N(af, AfCardLeft)} {N(af, af.pageHeight - cardBottom + 0.38)} m {N(af, AfCardRight)} {N(af, af.pageHeight - cardBottom + 0.38)} l S");
        af.page.AddContentStream(Encoding.ASCII.GetBytes(bg.ToString()));
        af.page.AddContentStream(Encoding.ASCII.GetBytes(af.boxes.ToString()));
        af.page.AddContentStream(Encoding.ASCII.GetBytes(af.runs.ToString()));
    }

    private static void NewPage(AccessFormState af)
    {
        af.page = af.doc.Pages.Add(af.pageWidth, af.pageHeight);
        EnsureFonts(af.page);
        af.boxes = new StringBuilder();
        af.runs = new StringBuilder();
    }

    private static void Emit(AccessFormState af, string res, double fs, double x, double yTd, string text, string rgb)
    {
        af.runs.AppendLine($"BT {rgb} rg");
        af.runs.Append($"/{res} {fs.ToString("F2", af.inv)} Tf ");
        af.runs.Append($"1 0 0 1 {N(af, x)} {N(af, af.pageHeight - yTd)} Tm ");
        af.runs.AppendLine($"({EscapePdfString(text)}) Tj ET");
    }

    private static void QuestionBox(AccessFormState af, double topTd)
    {
        af.boxes.AppendLine("0.98 0.98 0.98 rg");
        af.boxes.AppendLine($"{N(af, AfBoxLeft)} {N(af, af.pageHeight - topTd - AfBoxH)} {N(af, AfBoxRight - AfBoxLeft)} {N(af, AfBoxH)} re f");
        af.boxes.AppendLine("0.933 0.933 0.933 RG 0.75 w");
        af.boxes.AppendLine($"{N(af, AfBoxLeft)} {N(af, af.pageHeight - topTd - AfBoxH + 0.38)} {N(af, AfBoxRight - AfBoxLeft)} {N(af, AfBoxH - 0.76)} re S");
    }

    private static void Rule(AccessFormState af, double yTd)
    {
        af.boxes.AppendLine("0.533 0.533 0.533 RG 0.75 w");
        af.boxes.AppendLine($"{N(af, AfHrLeft)} {N(af, af.pageHeight - yTd)} m {N(af, AfHrRight)} {N(af, af.pageHeight - yTd)} l S");
        af.boxes.AppendLine("0.867 0.867 0.867 RG");
        af.boxes.AppendLine($"{N(af, AfHrLeft)} {N(af, af.pageHeight - yTd - 0.75)} m {N(af, AfHrRight)} {N(af, af.pageHeight - yTd - 0.75)} l S");
    }
}
