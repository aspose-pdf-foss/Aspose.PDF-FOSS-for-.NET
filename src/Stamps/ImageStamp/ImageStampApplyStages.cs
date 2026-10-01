using Aspose.Pdf.Core;

namespace Aspose.Pdf;

public partial class ImageStamp
{
    /// <summary>The stages of the image stamp apply: the page-rotation compensation.</summary>
    private void CompensateStampForPageRotation(ImageStampApplyState st)
    {
        var box = st.page.MediaBox;
        var rot = ((st.page.RotateDegrees % 360) + 360) % 360;
        (double ra, double rb, double rc, double rd, double re, double rf)? frame = rot switch
        {
            90 => (0, 1, -1, 0, box.URX, 0),
            180 => (-1, 0, 0, -1, box.URX, box.URY),
            270 => (0, -1, 1, 0, 0, box.URY),
            _ => null,
        };
        if (frame is { } f)
        {
            (st.ma, st.mb, st.mc, st.md, st.me, st.mf) = (
                st.ma * f.ra + st.mb * f.rc, st.ma * f.rb + st.mb * f.rd,
                st.mc * f.ra + st.md * f.rc, st.mc * f.rb + st.md * f.rd,
                st.me * f.ra + st.mf * f.rc + f.re, st.me * f.rb + st.mf * f.rd + f.rf);
        }
    }
}
