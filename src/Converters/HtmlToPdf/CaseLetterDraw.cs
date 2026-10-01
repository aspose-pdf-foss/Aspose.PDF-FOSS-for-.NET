using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// Case letter helpers: text cleanup, stroke flush, rules, text runs, measurement and wrapping.
    private static string Clean(CaseLetterState ce, string s) => Regex.Replace(DecodeEntities(
        Regex.Replace(s, "<[^>]+>", " ")).Replace(' ', ' '), @"\s+", " ").Trim();

    private static void FlushStrokes(CaseLetterState ce)
    {
        if (ce.strokes.Length == 0) return;
        ce.page.AddContentStream(Encoding.ASCII.GetBytes("q 0 0 0 RG " + ClBorderW.ToString("0.##", ce.inv) + " w\n" + ce.strokes + "Q\n"));
        ce.strokes.Clear();
    }

    private static void HLine(CaseLetterState ce, double x0, double x1, double yTop) => ce.strokes.Append(Compat.Format(ce.inv,
        $"{x0:F2} {ClPageH - yTop:F2} m {x1:F2} {ClPageH - yTop:F2} l S\n"));

    private static void VLine(CaseLetterState ce, double x, double y0Top, double y1Top) => ce.strokes.Append(Compat.Format(ce.inv,
        $"{x:F2} {ClPageH - y0Top:F2} m {x:F2} {ClPageH - y1Top:F2} l S\n"));

    private static void T(CaseLetterState ce, double size, double x, double baselineTop, string text, bool bold = false)
        => EmitGridsterText(ce.page, ce.resByFace, size, x, ClPageH - baselineTop, text,
            bold ? "Arial,Bold" : "Arial");

    private static double W(CaseLetterState ce, string s, double size, bool bold = false)
        => MeasureFaceText(bold ? "Arial,Bold" : "Arial", s, size);

    private static List<string> Wrap(CaseLetterState ce, string text, double box, double size, bool bold = false)
    {
        var lines = new List<string>();
        var cur = new StringBuilder();
        foreach (var word in text.Split(' ', System.StringSplitOptions.RemoveEmptyEntries))
        {
            var cand = cur.Length == 0 ? word : cur + " " + word;
            if (cur.Length > 0 && W(ce, cand, size, bold) > box) { lines.Add(cur.ToString()); cur.Clear(); cur.Append(word); }
            else { cur.Clear(); cur.Append(cand); }
        }
        if (cur.Length > 0) lines.Add(cur.ToString());
        if (lines.Count == 0) lines.Add("");
        return lines;
    }
}
