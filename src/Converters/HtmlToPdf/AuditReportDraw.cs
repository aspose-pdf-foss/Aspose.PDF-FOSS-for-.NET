using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// Audit report drawing helpers: page lookup, measurement, wrapping, line and atomic-block emitters, the main-column close and the colour operator.
    private static Page PageAt(AuditReportState ar, int i)
    {
        while (ar.pages.Count <= i)
        {
            var p = ar.doc.Pages.Add(ar.pageW, CtSheetHPt);
            EnsureFonts(p);
            ar.pages.Add(p);
        }
        return ar.pages[i];
    }

    private static double Measure(AuditReportState ar, byte[] ttf, string s, double size)
    {
        if (s.Length == 0) return 0;
        if (PageAt(ar, 0).Dict.Get("Resources") is not Core.PdfDictionary res
            || res.Get("Font") is not Core.PdfDictionary fd) return s.Length * size * 0.5;
        return Text.Type0FontEmbedder.MeasureText(fd, ttf, "RobotoRegular", s, size,
            stripSpacesInBaseFont: true);
    }

    private static List<string> Wrap(AuditReportState ar, string text, double width, double size)
    {
        var outp = new List<string>();
        var cur = "";
        foreach (var w in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var t = cur.Length == 0 ? w : cur + " " + w;
            if (cur.Length > 0 && Measure(ar, ar.reg!, t, size) > width) { outp.Add(cur); cur = w; }
            else cur = t;
        }
        if (cur.Length > 0) outp.Add(cur);
        if (outp.Count == 0) outp.Add("");
        return outp;
    }

    // a splittable run of lines: each line moves to the next sheet on its own
    private static void Lines(AuditReportState ar, IEnumerable<string> lines, double x, double size, Color ink)
    {
        foreach (var ln in lines)
        {
            if (ar.y + CtLineH(size) > ar.bottom) { ar.sheet++; ar.y = ar.top; ar.sheetHasGrid = false; }
            if (ln.Length > 0)
                ar.items.Add(new CtItem
                {
                    Sheet = ar.sheet, Y = ar.y + CtHalf(size), X = x, Size = size,
                    Text = ln, Ink = ink,
                });
            ar.y += CtLineH(size);
        }
    }

    private static void Atomic(AuditReportState ar, double h, double padTop, double x, double size, string text, Color ink)
    {
        if (ar.y + h > ar.bottom) { ar.sheet++; ar.y = ar.top; ar.sheetHasGrid = false; }
        if (text.Length > 0)
            ar.items.Add(new CtItem
            {
                Sheet = ar.sheet, Y = ar.y + padTop + CtHalf(size), X = x, Size = size,
                Text = text, Ink = ink,
            });
        ar.y += h;
    }

    private static double TextX(AuditReportState ar) => ar.colLeft + ar.colWidth * CtTextIndentFrac;

    private static double TextWrap(AuditReportState ar) => ar.colWidth * CtTextWrapFrac;

    // .auditReportHeadingMain's own 5px padding-bottom, paid once its
    // heading and body text have been placed
    private static void CloseMain(AuditReportState ar)
    {
        if (!ar.mainOpen) return;
        ar.mainOpen = false;
        ar.y += CtMainPadBotPt;
    }

    private static string Rgb(AuditReportState ar, Color c, string op)
        => Compat.Format(ar.invc,
            $"{c.R / 255.0:0.###} {c.G / 255.0:0.###} {c.B / 255.0:0.###} {op} ");
}
