using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// Bootstrap screen emitters: a text run, a glyph icon and a filled/stroked box on the page.
    private static void EmitRun(BootstrapScreenState bs, double pageHeight, string res, double fs, double x, double yTd, string text, Color col)
    {
        bs.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(bs.invc,
            $"BT {col.R / 255.0:0.###} {col.G / 255.0:0.###} {col.B / 255.0:0.###} rg " +
            $"/{res} {fs:F2} Tf 1 0 0 1 {x:F2} {pageHeight - yTd:F2} Tm ({EscapePdfString(text)}) Tj ET\n")));
    }

    // A glyphicon: the substituted system symbol face, embedded
    // as Type0; a code point that face lacks draws nothing but still advances.
    private static double EmitIcon(BootstrapScreenState bs, double pageHeight, int cp, double fs, double x, double yTd, Color col)
    {
        var faceName = cp == 0x2709 || cp == 0xE003 ? "Segoe UI Symbol" : "MS Gothic";
        var ttf = Text.SystemFontResolver.Resolve(faceName);
        var s = char.ConvertFromUtf32(cp);
        var adv = BsNotdefIconAdvEm * fs;
        if (ttf is not null
            && bs.page.Dict.Get("Resources") as Core.PdfDictionary is { } res
            && res.Get("Font") as Core.PdfDictionary is { } fontDict)
        {
            var (rn, hex) = Text.Type0FontEmbedder.Embed(fontDict, ttf, faceName, s,
                stripSpacesInBaseFont: true);
            bs.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(bs.invc,
                $"BT {col.R / 255.0:0.###} {col.G / 255.0:0.###} {col.B / 255.0:0.###} rg " +
                $"/{rn} {fs:F2} Tf 1 0 0 1 {x:F2} {pageHeight - yTd:F2} Tm ")
                + "<" + Compat.ToHexString(hex) + "> Tj ET\n"));
            // A private-use icon has no glyph in the substitute face and keeps
            // the measured 0.75 em advance; a real symbol advances naturally.
            if (cp < 0xE000)
            {
                var real = Text.Type0FontEmbedder.MeasureText(fontDict, ttf, faceName, s, fs,
                    stripSpacesInBaseFont: true);
                if (real > 0.01) adv = real;
            }
        }
        return adv;
    }

    private static void Box(BootstrapScreenState bs, double pageHeight, double x, double yTd, double w, double h, Color? fill, Color? stroke, double sw)
    {
        var sb = new StringBuilder("q ");
        if (fill is { } f)
            sb.Append(Compat.Format(bs.invc,
                $"{f.R / 255.0:0.###} {f.G / 255.0:0.###} {f.B / 255.0:0.###} rg {x:F2} {pageHeight - yTd - h:F2} {w:F2} {h:F2} re f "));
        if (stroke is { } st)
            sb.Append(Compat.Format(bs.invc,
                $"{st.R / 255.0:0.###} {st.G / 255.0:0.###} {st.B / 255.0:0.###} RG {sw:0.##} w " +
                $"{x + sw / 2:F2} {pageHeight - yTd - h + sw / 2:F2} {w - sw:F2} {h - sw:F2} re S "));
        sb.Append("Q\n");
        bs.page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
    }
}
