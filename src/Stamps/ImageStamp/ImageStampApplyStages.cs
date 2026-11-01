using Aspose.Pdf.Core;

namespace Aspose.Pdf;

public partial class ImageStamp
{
    /// <summary>The stages of the image stamp apply: the page-rotation compensation.</summary>
    private static void CompensateStampForPageRotation(ImageStampApplyState st)
    {
        if (st.page.ShownToMediaMatrix() is { } f)
        {
            (st.ma, st.mb, st.mc, st.md, st.me, st.mf) = (
                st.ma * f.A + st.mb * f.C, st.ma * f.B + st.mb * f.D,
                st.mc * f.A + st.md * f.C, st.mc * f.B + st.md * f.D,
                st.me * f.A + st.mf * f.C + f.E, st.me * f.B + st.mf * f.D + f.F);
        }
    }
}
