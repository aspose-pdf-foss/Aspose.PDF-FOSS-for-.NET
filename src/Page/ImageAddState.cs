using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public sealed partial class Page
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class ImageAddState
{
    // Detect JPEG by FFD8 header
    public bool isJpeg;
    // Detect PNG by 89504E47 header
    public bool isPng;
    // Detect BMP by 'BM' header
    public bool isBmp;
    // Detect JPEG 2000: a JP2/JPX box wrapper (signature box 00000000 0C 6A502020)
    // or a raw codestream (SOC marker FF4F immediately followed by SIZ FF51).
    public bool isJpx;
    public ImageStamp stamp = null!;
    // With aspectFit the rectangle is a bounding box, not a target frame: the image
    // fits INSIDE it at its own aspect ratio, centred on both axes — a square image
    // in a wide rect keeps its shape instead of stretching to fill.
    public double dx;
    public double dy;
    public double dw;
    public double dh;
    public byte[] imageData = default!;
    public Rectangle rect = default!;
    public bool blackWhite = false;
}
}
