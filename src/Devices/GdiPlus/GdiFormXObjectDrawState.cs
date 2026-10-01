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
private sealed class GdiFormXObjectDrawState
{
    public Devices.GdiPlusPageRenderer.Scope savedScope = null!;
    public System.Drawing.Drawing2D.GraphicsState savedGdi = null!;
    public PdfStream formStream = default!;
    public GraphicsState state = default!;
    public bool forceComposite = false;
    public byte[] content = null!;
    public Scope formScope = null!;
    public double[]? formMatrix;
    public double[] effectiveCtm = null!;
    public System.Drawing.Drawing2D.GraphicsPath? bboxClip;
    public PdfDictionary? groupDict;
    public bool isTransparencyGroup;
    public bool isIsolatedGroup;
    public bool isKnockoutGroup;
    public bool needsComposite;
}
}
