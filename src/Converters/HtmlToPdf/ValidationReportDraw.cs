using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// Validation report drawing helpers: page lookup, layered content emit, fills, rules, boxes, text runs, measurement, wrapping and the section bar.
    private static string Rgb(ValidationReportState vr, Color c, string op)
        => Compat.Format(vr.invc,
            $"{c.R / 255.0:0.###} {c.G / 255.0:0.###} {c.B / 255.0:0.###} {op} ");

    private static Page PageAt(ValidationReportState vr, int i)
    {
        while (vr.pages.Count <= i)
        {
            var p = vr.doc.Pages.Add(vr.pageWidth, vr.pageHeight);
            EnsureFonts(p);
            vr.ops.Add((vr.pages.Count, VrLayerCanvas, vr.seq++, Compat.Format(vr.invc,
                $"q {Rgb(vr, VrPageBg, "rg")}{vr.marginLeft:0.##} {vr.marginBottom:0.##} "
                + $"{vr.pageWidth - vr.marginLeft - vr.marginRight:0.##} {vr.contentH:0.##} re f Q")));
            vr.pages.Add(p);
        }
        return vr.pages[i];
    }

    private static void Emit(ValidationReportState vr, int sheet, int layer, string text)
    {
        PageAt(vr, sheet);
        vr.ops.Add((sheet, layer, vr.seq++, text));
    }

    // Y runs continuously through the report; the sheet it lands on and the
    // offset inside that sheet fall straight out of the content height.
    private static (int Sheet, double Top) Loc(ValidationReportState vr, double y)
    {
        var i = Math.Max(0, (int)Math.Floor(y / vr.contentH + 1e-9));
        return (i, vr.marginTop + (y - i * vr.contentH));
    }

    private static void Fill(ValidationReportState vr, double y0, double y1, double x, double w, Color c, int layer)
    {
        if (y1 - y0 <= 1e-6 || w <= 0) return;
        var last = Loc(vr, y1 - 1e-6).Sheet;
        for (var i = Loc(vr, y0).Sheet; i <= last; i++)
        {
            var top = Math.Max(y0, i * vr.contentH);
            var bot = Math.Min(y1, (i + 1) * vr.contentH);
            if (bot - top <= 1e-6) continue;
            var yTop = vr.marginTop + (top - i * vr.contentH);
            Emit(vr, i, layer, Compat.Format(vr.invc,
                $"q {Rgb(vr, c, "rg")}{x:0.##} {vr.pageHeight - yTop - (bot - top):0.##} "
                + $"{w:0.##} {bot - top:0.##} re f Q"));
        }
    }

    private static void HRule(ValidationReportState vr, double y, double x0, double x1, Color c)
    {
        var (i, top) = Loc(vr, y);
        Emit(vr, i, VrLayerStroke, Compat.Format(vr.invc,
            $"q {Rgb(vr, c, "RG")}{VrBorderPt:0.##} w {x0:0.##} {vr.pageHeight - top:0.##} m "
            + $"{x1:0.##} {vr.pageHeight - top:0.##} l S Q"));
    }

    private static void VRule(ValidationReportState vr, double y0, double y1, double x, Color c)
    {
        if (y1 - y0 <= 1e-6) return;
        var last = Loc(vr, y1 - 1e-6).Sheet;
        for (var i = Loc(vr, y0).Sheet; i <= last; i++)
        {
            var top = Math.Max(y0, i * vr.contentH);
            var bot = Math.Min(y1, (i + 1) * vr.contentH);
            if (bot - top <= 1e-6) continue;
            var yTop = vr.marginTop + (top - i * vr.contentH);
            Emit(vr, i, VrLayerStroke, Compat.Format(vr.invc,
                $"q {Rgb(vr, c, "RG")}{VrBorderPt:0.##} w {x:0.##} {vr.pageHeight - yTop:0.##} m "
                + $"{x:0.##} {vr.pageHeight - yTop - (bot - top):0.##} l S Q"));
        }
    }

    // A bordered box: the sides run the whole span, while the top and the
    // bottom rule land only on the sheets those edges fall on.
    private static void Box(ValidationReportState vr, double y0, double y1, double x0, double x1, Color? fill, Color border,
        int layer = VrLayerFrame)
    {
        if (fill is { } f) Fill(vr, y0, y1, x0, x1 - x0, f, layer);
        VRule(vr, y0, y1, x0 + VrBorderPt / 2, border);
        VRule(vr, y0, y1, x1 - VrBorderPt / 2, border);
        HRule(vr, y0 + VrBorderPt / 2, x0, x1, border);
        HRule(vr, y1 - VrBorderPt / 2, x0, x1, border);
    }

    private static double Measure(ValidationReportState vr, byte[] ttf, string name, string s, double size)
    {
        if (PageAt(vr, 0).Dict.Get("Resources") is not Core.PdfDictionary res
            || res.Get("Font") is not Core.PdfDictionary fd) return s.Length * size * 0.5;
        return Text.Type0FontEmbedder.MeasureText(fd, ttf, name, s, size,
            stripSpacesInBaseFont: true);
    }

    private static void Run(ValidationReportState vr, double lineTop, double x, double size, byte[] ttf, string name,
        string s, Color c)
    {
        if (s.Length == 0) return;
        var (i, top) = Loc(vr, lineTop);
        var pg = PageAt(vr, i);
        if (pg.Dict.Get("Resources") is not Core.PdfDictionary res
            || res.Get("Font") is not Core.PdfDictionary fd) return;
        var (rn, hex) = Text.Type0FontEmbedder.Embed(fd, ttf, name, s,
            stripSpacesInBaseFont: true);
        // half-leading inside the rounded line box, then the face's ascent
        var baseline = top + (VrLineH(size) - size * VrLineEm) / 2 + size * VrAscEm;
        Emit(vr, i, VrLayerText, Compat.Format(vr.invc,
            $"BT {Rgb(vr, c, "rg")}/{rn} {size:0.##} Tf 1 0 0 1 {x:0.##} "
            + $"{vr.pageHeight - baseline:0.##} Tm ")
            + "<" + Compat.ToHexString(hex) + "> Tj ET");
    }

    private static List<string> Wrap(ValidationReportState vr, byte[] ttf, string name, string s, double size, double width)
    {
        var outp = new List<string>();
        var cur = "";
        foreach (var w in s.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var t = cur.Length == 0 ? w : cur + " " + w;
            if (cur.Length > 0 && Measure(vr, ttf, name, t, size) > width)
            { outp.Add(cur); cur = w; }
            else cur = t;
        }
        if (cur.Length > 0) outp.Add(cur);
        if (outp.Count == 0) outp.Add("");
        return outp;
    }

    private static double Bar(ValidationReportState vr, string itemClass, double top)
    {
        var x = vr.boxL + VrBorderPt + VrBarInsetPt;
        var ty = top + VrBorderPt + VrBarPadPt + VrBarItemDropPt;
        foreach (var t in VrTexts(vr.src, itemClass))
        {
            Run(vr, ty, x, VrBarTextPt, vr.faceReg, "SegoeUI", t, VrInk);
            x += Measure(vr, vr.faceReg, "SegoeUI", t, VrBarTextPt) + VrBarInsetPt;
        }
        Box(vr, top, top + VrBarHeightPt, vr.boxL, vr.boxR, VrWhite, VrBarBorder, VrLayerContainer);
        return top + VrBarHeightPt + VrBarGapPt;
    }
}
