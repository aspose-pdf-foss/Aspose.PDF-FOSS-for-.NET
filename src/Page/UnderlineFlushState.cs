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
private sealed class UnderlineFlushState
{
    public Content.ContentStreamBuilder builder = null!;
    public byte[] bytes = null!;
    public Aspose.Pdf.Text.Position fragPos = null!;
    public float fs;
    // Width: prefer fragment's Rectangle width (computed during absorption),
    // fall back to MeasureString.
    public double w;
    // Underline geometry (verified on Arial and Calibri
    // sources): the line's top edge sits a tenth of the font's descent below
    // the fragment's rect bottom (Position.YIndent), and the thickness is 5%
    // of the font size — so the bottom offset is (0.05 + descent/10)·fs.
    // Fonts without a descent metric keep the historical constant, which is
    // this same formula evaluated for a typical 0.269 descent.
    public double ulThick;
    public double ulDescent;
    public Aspose.Pdf.Text.FontMetrics? ulMetrics;
    public double ulOffset;
    // Rotation-aware path: for text drawn under a rotating CTM, emit the
    // underline along the baseline via a cm transform (a perpendicular page-Y
    // offset would leave the line floating off the rotated text). Horizontal
    // text (TextDirX=1, TextDirY=0) is unaffected and takes the path below.
    public double ulDirX;
    public double ulDirY;
    public double ulDirLen;
    // In page space (Y-up), underline is BELOW baseline = lower Y.
    // But with CTM Y-flip, the offset direction reverses.
    public Matrix? ctm;
    public bool yFlipped;
    public double underlineY;
    public double underlineH;
    // Transform from page space to content-stream space using the inverse CTM.
    // Emit raw 're' in content-stream coordinates (no cm prefix) so
    // IsRectanglePresent matches the bare coordinates.
    public double rectX;
    public double rectY;
    public Color? fg;
    public double r;
    public double g;
    public double b;
}
}
