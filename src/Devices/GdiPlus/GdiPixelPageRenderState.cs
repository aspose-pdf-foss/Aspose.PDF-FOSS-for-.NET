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
private sealed class GdiPixelPageRenderState
{
    public Rectangle rawMb = null!;
    // Visible region = crop box clipped to media box; content is sized and offset
    // to it so anything outside the crop area is excluded.
    public Rectangle crop = null!;
    // /Rotate (PDF 32000 §14.8.2.7) composes into the initial CTM exactly as the
    // software renderer does, so a 90°/270° page swaps its pixel dimensions and
    // the content swings clockwise into the visible canvas.
    public int rot;
    public Rectangle effectiveMb = null!;
    public double[]? initialPageCtm;
    public Devices.RgbaBuffer result = null!;
    public Page page = default!;
    // The objects resolved before the render began: they outlive its cache clear.
    public HashSet<(int objNum, int gen)> cachedBefore = null!;
    public int pixelW = 0;
    public int pixelH = 0;
    public double? xScale = null;
    public double? yScale = null;
}
}
