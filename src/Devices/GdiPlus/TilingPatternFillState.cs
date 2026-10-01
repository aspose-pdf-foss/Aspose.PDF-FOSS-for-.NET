using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Text;
using GdiColor = System.Drawing.Color;
using GdiMatrix = System.Drawing.Drawing2D.Matrix;
using GraphicsState = Aspose.Pdf.Content.GraphicsState;
using GdiState = System.Drawing.Drawing2D.GraphicsState;

namespace Aspose.Pdf.Devices;

public sealed partial class GdiPlusPageRenderer
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class TilingPatternFillState
{
    public Aspose.Pdf.Core.PdfObject? patObj;
    public Aspose.Pdf.Core.PdfDictionary pd = null!;
    public double bx0;
    public double by0;
    public double bx1;
    public double by1;
    public double xstep;
    public double ystep;
    public double[] patMatrix = null!;
    public byte[] content = null!;
    // Bound the tiling loop: map the fill region's device bounds into pattern space.
    public System.Drawing.RectangleF db;
    public System.Drawing.PointF[] corners = null!;
    public int iMin;
    public int iMax;
    public int jMin;
    public int jMax;
    // A pattern fill that carries transparency — group fill-alpha (/ca < 1), an active
    // ExtGState soft mask, or a non-Normal blend mode — must composite as a unit
    // (PDF 32000 §11.6.5-6): render the tiles onto a transparent layer, then blend that
    // layer onto the page once at the fill alpha / mask / blend. Painting the cells
    // straight onto the page (the common opaque case) ignores the alpha and over-inks
    // the region — e.g. a faded content panel drawn as an opaque dark overlay.
    public bool needsComposite;
    public int pw;
    public int ph;
    public int rx0;
    public int ry0;
    public int rx1;
    public int ry1;
    public System.Drawing.Rectangle compRect;
    // Capture the inherited device-space clip so the pattern fill stays bounded.
    public System.Drawing.Drawing2D.Matrix savedClipT = null!;
    public System.Drawing.Region deviceClip = null!;
    public System.Drawing.Bitmap layer = null!;
    public System.Drawing.Graphics savedLG = null!;
    public System.Drawing.Graphics lg = null!;
    public GraphicsPath path = default!;
    public GraphicsState state = default!;
    public GdiMatrix world = default!;
    public string patName = default!;
}
}
