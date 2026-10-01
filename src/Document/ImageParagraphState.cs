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
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class ImageParagraphState
{
    public double pendingInline;
    public byte[]? imgData;
    // An SVG source rasterises through the built-in SVG converter first —
    // the raster embed path below can't decode vector data and the image
    // would drop silently from the flow. The natural size is the SVG
    // viewport in points (attrs read 1:1) so the layout below doesn't
    // read raster pixels as points.
    public double svgNatW;
    public double svgNatH;
    // Page.AddImage embeds JPEG / PNG / raw RGB directly. Other raster
    // formats (TIFF / BMP / GIF, possibly multi-frame) are decoded with the
    // platform image codec to one PNG per frame; each frame is placed on its
    // own page, matching how a multi-page TIFF expands into multiple pages.
    public byte hdr0;
    public byte hdr1;
    // Baseline JPEG and PNG embed directly. Progressive JPEG is routed
    // through the codec re-encode (the embedded-image decoder is
    // baseline-only, so a progressive frame would render blank).
    public bool isJpeg;
    public bool isPng;
    // JPEG 2000 (.jp2/.jpx) — the platform codec can't decode it, so keep the
    // raw bytes and let Page.AddImage route them through the built-in JPXDecode
    // decoder (System.Drawing returns null for these, which used to drop the image).
    public bool isJpx;
    public List<byte[]>? frames;
    // A genuinely bilevel source embeds losslessly as a compact 1-bit
    // image instead of an 8-bit re-encode (a scanned/fax page would
    // otherwise balloon the output).
    public bool embedBlackWhite;
    public double availW;
    public double availH;
    public Image img = default!;
    public FlowLayout flow = default!;
    public Page page = default!;
    public double marginLeft = 0;
    public double marginRight = 0;
    public double marginTop = 0;
    public double marginBottom = 0;
}
}
