using System.Runtime.InteropServices;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Devices.Rasterizer;
using Aspose.Pdf.IO;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Devices;

public sealed partial class SoftwarePageRenderer
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class DrawImageState
{
    public Aspose.Pdf.Core.PdfDictionary dict = null!;
    public int imgW;
    public int imgH;
    public byte[] decoded = null!;
    // Check for image mask
    public bool isImageMask;
    // /SMask: PDF 32000 §11.6.5.3 — an indirect reference to a soft-mask
    // grayscale image (W×H bytes, 8bpc). Sample value = per-pixel opacity.
    // Resolved here so the various Blit branches below can pass it through.
    public byte[]? smask;
    // /Mask: PDF 32000 §8.9.6.3 — an explicit 1-bit stencil mask selecting which
    // base-image pixels are painted. Folded into the same per-pixel alpha plane the
    // blit branches consume; when both /SMask and /Mask are present their opacities
    // multiply.
    public byte[]? stencil;
    public double[] ctm = null!;
    // Compute destination rectangle in page coordinates. The PDF unit square
    // (0,0)-(1,1) maps to ctm[5]…ctm[5]+ctm[3] vertically; either bound can be
    // higher depending on the sign of ctm[3]. Most PDFs use positive ctm[3]
    // (image-y=0 at the bottom of the rect, image-y=1 at the top), but
    // generators that emit pre-flipped image data use ctm[3]<0 to compensate.
    // Pick the higher PDF y as the top of the rendered rectangle and the
    // lower as the bottom; pixel coordinates are origin-top-left, so the
    // higher PDF y becomes the lower pixel row.
    public double destX;
    public double destW;
    public double destH;
    public double topPdfY;
    // Convert to pixel coords. Round (not truncate) matches GDI+ behaviour on the
    // nearest integer pixel, keeping image placement aligned with the GDI+ renderer.
    public int px;
    public int py;
    // A rectangle that is non-empty in user space must cover at least one device
    // pixel. A raster logo exploded into ~2700 one-unit-tall inline scanline strips
    // (0.12 pt each under the page's 0.12 scale = 0.25 px at 150 dpi) rounded EVERY
    // strip to a height of zero and the whole logo silently vanished; GDI+ paints
    // each sliver's partial coverage instead. Clamp so a thin sliver still lands.
    public int pw;
    public int ph;
    // CTM-driven mirror flags, computed before EVERY paint branch (mask, JPEG, JPX,
    // indexed, raw): a negative ctm[3] mirrors vertically, a negative ctm[0]
    // horizontally. The dest rect is placed from |ctm|, so only the SAMPLING
    // direction carries the mirror. An IMAGE MASK needs them too — it sat above
    // the old declaration and silently never mirrored.
    public bool flipY;
    public bool flipX;
    // Decode pixels based on color space
    public int bpc;
    public Devices.SoftwarePageRenderer.ImageColorSpaceInfo csInfo;
    public string cs = null!;
    // Overprint (PDF 32000 §8.6.7) only changes the result for a SUBTRACTIVE image -
    // DeviceCMYK, or a /Separation / /DeviceN spot space. An overprinted spot plate
    // composites ONTO the process colour underneath instead of knocking it out, so
    // painting it opaquely erases the artwork it was meant to tint: a spot varnish over
    // a CMYK photo left nothing but the flat plate colour. The GDI+ side has consulted
    // state.OverprintFill on every image draw for a while; this is the software half.
    public bool overprint;
    // JPEG 2000 (JPXDecode filter): a JP2 box wrapper (signature box
    // 00 00 00 0C 6A 50 …) or a bare J2K codestream (0xFF 0x4F SOC).
    public bool isJ2kFile;
    public bool isJ2kCodestream;
    public RenderContext ctx = default!;
    public PdfStream xobjStream = default!;
    public GraphicsState state = default!;
    public int smaskW;
    public int smaskH;
    public int stencilW;
    public int stencilH;
}
}
