using System.Globalization;
using System.Text;
using System.Xml;

namespace Aspose.Pdf.Forms.Xfa;

internal static partial class XfaRenderer
{
    private static void PaintField(Ctx ctx, XmlElement e, double x, double y, double w, double h, string path)
    {
        var pf = new PaintFieldState();
        pf.ctx = ctx;
        pf.e = e;
        pf.x = x;
        pf.y = y;
        pf.w = w;
        pf.h = h;
        pf.path = path;
        if (DumpPos) System.Console.Error.WriteLine($"FIELDPOS\t{pf.path}\t{pf.x:F1}\t{pf.ctx.PageH - pf.y:F1}");
        pf.caption = Caption(pf.e);
        pf.res = pf.caption.reserve;
        pf.plc = pf.caption.placement;
        pf.cap = InnerText(pf.e, "caption");
        pf.fs = FontSize(pf.e);
        pf.check = IsCheckbox(pf.e);
        pf.radio = pf.e.LocalName == "exclGroup" || Ui(pf.e) == "radio";
        pf.ui = Ui(pf.e);

        if (pf.ui == "imageEdit")
        {
            PaintImageField(pf);
            return;
        }

        if (pf.ui == "button")
        {
            PaintButtonField(pf);
            return;
        }

        if (pf.check || pf.radio)
        {
            PaintCheckField(pf);
            return;
        }

        ResolveFieldRegions(pf);

        ResolveFieldValue(pf);

        PaintFieldBorder(pf);

        PaintEditChrome(pf);

        PaintFieldText(pf);
    }
}
