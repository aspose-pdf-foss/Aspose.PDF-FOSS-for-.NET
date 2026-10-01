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
private sealed class GdiCidTextDrawState
{
    public double[] tm = null!;
    public double[] ctm = null!;
    public double tfs;
    public double th;
    public int upm;
    // Non-embedded predefined CJK CIDFonts have no /FontFile*, so parser is null.
    // PDF 32000 §9.6.6 expects the reader to supply a system font matching the
    // /CIDSystemInfo. Mirror SoftwarePageRenderer.DrawCidText: route glyph lookup
    // through a broad-coverage system CJK font (CID/Unicode → cmap).
    public Text.IGlyphOutlineSource? fallback;
    public int fbUpm;
    public bool vertical;
    // 1-byte custom CMaps (codespace <00> <FF>) show one CID per byte.
    public int step;
    public byte[] rawBytes = default!;
    public CidFontInfo cid = default!;
    public IGlyphOutlineSource? parser = null;
    public FontMetrics? metrics = null;
    public GraphicsState state = default!;
    public double hScale = 0;
    public GdiColor fill = default!;
}
}
