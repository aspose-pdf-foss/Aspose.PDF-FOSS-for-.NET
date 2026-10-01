using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;

namespace Aspose.Pdf;

public sealed partial class Document
{
    private void LayoutImageParagraph(Image img, FlowLayout flow, Page page, PageLayoutState pl, double marginLeft, double marginRight, double marginTop, double marginBottom)
    {
        var ip = new ImageParagraphState();
        ip.img = img;
        ip.flow = flow;
        ip.page = page;
        ip.marginLeft = marginLeft;
        ip.marginRight = marginRight;
        ip.marginTop = marginTop;
        ip.marginBottom = marginBottom;
        ip.pendingInline = pl.pendingInlineLineHeight;
        ReadImageSourceBytes(ip);
        if (ip.imgData is null) return;

        DecodeBase64Source(ip);

        RasterizeSvgSource(ip);

        if (!TryEmitBilevelFrames(ip)) return;

        if (!SplitImageFrames(ip)) return;

        ip.embedBlackWhite = ip.img.IsBlackWhite || ImageStamp.IsBilevelSource(ip.imgData);

        if (ip.img.FixedRectangle is { } fixedAt)
        {
            // Placed by the caller: drawn where it says, on the page the flow is
            // on, its background and border around that box; the cursor stays.
            var (fl, fb, fr, ft) = ip.img.Border is { } fixedBorder ? FlowLayout.BorderBands(fixedBorder) : (0, 0, 0, 0);
            var fixedBox = new Rectangle(fixedAt.LLX - fl, fixedAt.LLY - fb, fixedAt.URX + fr, fixedAt.URY + ft);
            var fixedPaint = FlowLayout.BuildBlockPaint(ip.img.Border, fixedBox, ip.img.BackgroundColor, new FlowLayout.BlockFill(fixedBox, null, null));
            ip.flow.PlaceImageFrame(ip.frames![0], fixedAt, fixedPaint, ip.embedBlackWhite);
            return;
        }
        ip.availW = ip.page.Width - ip.marginLeft - ip.marginRight;
        ip.availH = ip.page.Height - ip.marginTop - ip.marginBottom;
        // The image's own top margin is space reserved above it: the cursor
        // drops by it before the image seats (measured: a 50 pt Margin.Top
        // puts the image 50 pt under the preceding paragraph); the bottom
        // margin is the gap after it.
        if (ip.img.Margin?.Top > 0) ip.flow.AdvanceY(ip.img.Margin.Top);
        for (int frameIdx = 0; frameIdx < ip.frames!.Count; frameIdx++)
        {
            if (!LayoutImageFrame(ip, frameIdx)) break;
        }
        if (ip.img.Margin?.Bottom > 0) ip.flow.AdvanceY(ip.img.Margin.Bottom);
        pl.pendingInlineLineHeight = ip.pendingInline;
    }
}
