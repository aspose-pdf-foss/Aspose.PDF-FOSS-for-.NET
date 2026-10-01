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
private sealed class GroupLayerCompositeState
{
    public int w;
    public int h;
    public double ga;
    public Devices.Rasterizer.BlendMode mode;
    public byte[]? softMask;
    public System.Drawing.Rectangle rect;
    public System.Drawing.Imaging.BitmapData dst = null!;
    public System.Drawing.Imaging.BitmapData src = null!;
    public System.Drawing.Imaging.BitmapData? ko;
    public Bitmap layer = default!;
    public GraphicsState state = default!;
    public string blendMode = default!;
    public System.Drawing.Rectangle bounds = default!;
    public float[]? covWeight = null;
    public byte[]? stampMask = null;
    public Bitmap? koBackdrop = null;
    public bool koReplace = false;
    public int x0;
    public int x1;
    public int segBytes;
    public byte[] drow = null!;
    public byte[] srow = null!;
    public byte[]? krow;
    public long dRow;
    public long sRow;
    public bool dirty;
    public int i;
    // Source coverage: the supersampled mask when supplied, else the
    // layer's own BGRA straight alpha.
    public double sca;
    public double a;
    public int sb;
    public int sg;
    public int sr;
    public int db;
    public int dg;
    public int dr;
    public double dn;
    // PDF 32000 §11.3.6 general "over" with blend and backdrop alpha:
    //   Cs' = (1-αb)·Cs + αb·B(Cb,Cs)          (blend only acts where a backdrop exists)
    //   αr  = a + αb·(1-a)
    //   Cr  = (Cs'·a + Cb·αb·(1-a)) / αr        (straight, un-premultiplied)
    // With αb=1 (opaque page) this reduces to Cr = B·a + Cb·(1-a); with
    // αb=0 (transparent nested layer) to Cr = Cs — no darkening toward black.
    public double bbr;
    public double bbg;
    public double bbb;
    public double outA;
    public double inv;
}
}
