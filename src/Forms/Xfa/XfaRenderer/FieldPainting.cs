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

        if (pf.ui == "barcode")
        {
            ResolveFieldValue(pf);
            if (PaintBarcodeField(pf)) return;
        }

        if (pf.ui == "imageEdit")
        {
            PaintImageField(pf);
            return;
        }

        if (pf.ui == "button")
        {
            int first = pf.ctx.Items.Count;
            PaintButtonField(pf);
            // A button shown on screen only is drawn by its widget, which does not print: deleting the field
            // or printing the page leaves nothing of it.
            if (RecordPlacement(pf) is { } placed && ExcludedFromPrint(pf.e))
            {
                placed.ScreenOnly = true;
                for (int i = first; i < pf.ctx.Items.Count; i++) pf.ctx.Items[i].ValueOf = placed;
            }
            return;
        }

        if (pf.check || pf.radio)
        {
            PaintCheckField(pf);
            RecordPlacement(pf);
            return;
        }

        ResolveFieldRegions(pf);

        ResolveFieldValue(pf);

        PaintFieldBorder(pf);

        PaintEditChrome(pf);

        PaintFieldText(pf);
        RecordPlacement(pf);
    }
}
