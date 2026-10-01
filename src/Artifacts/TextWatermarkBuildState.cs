using System.IO;
using Aspose.Pdf.Content;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public partial class WatermarkArtifact
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class TextWatermarkBuildState
{
    public string? renderText;
    public double pageWidth;
    public double pageHeight;
    public float fontSize;
    public string baseFont = null!;
    // The lines stack upward from the position: the LAST line's descent sits on
    // the box floor and every earlier line is one pitch higher.
    public string[] lines = null!;
    public string writtenFace = null!;
    public double textWidth;
    // Vertical extent of the text line: the written face's ascent+descent box.
    public double ascent;
    public double descent;
    public double pitch;
    public double textHeight;
    public double x;
    public double y;
    public Rectangle bbox = null!;
    public System.Globalization.CultureInfo ci = null!;
    // Form content: colour + text run at absolute page coordinates; the form's
    // BBox spans the page so no placement matrix is needed on the page side.
    public System.Text.StringBuilder inner = null!;
    public Color? fg;
    // A ROTATED watermark carries its rotation on the PAGE-LEVEL cm (composed
    // after the /Rotate compensation) with the form's text at the origin —
    // the output takes exactly this shape (q R·cm /Fm Do Q), and rotation
    // inside the form's Tm renders mirrored on /Rotate pages.
    public string? rotationCm;
    public string formName = null!;
    public System.Text.StringBuilder sb = null!;
    public string bboxStr = null!;
    public Page page = default!;
    public string fontResourceName = default!;
}
}
