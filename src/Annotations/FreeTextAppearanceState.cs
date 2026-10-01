using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

public partial class FreeTextAnnotation
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class FreeTextAppearanceState
{
    // Text is taken from /Contents, falling back to the plain text of the
    // /RC rich-text packet when /Contents is empty (a FreeText can carry its
    // text only as rich text).
    public string? text = null!;
    public Rectangle? rect = null!;
    public Annotations.DefaultAppearance da = null!;
    public string fontName = null!;
    public double fontSize;
    public System.Drawing.Color color;
    // Explicit TextStyle values (those differing from its defaults) take precedence
    // over /DA, so formatting set via TextStyle after creation is honoured.
    public Annotations.TextStyle ts = null!;
    public double w;
    public double h;
    public double borderWidth;
    // Text inset scales with the border: 2pt inside a standard 1pt border, flush
    // with the rectangle for a borderless (W=0) typewriter annotation. Calibrated
    // so that a 12pt Helvetica line of 130.08pt in a 136.05pt
    // bordered rect stays on one line (inset ≤ 2.98), while a 12pt Courier line
    // of 93.6pt in a 96.3pt borderless rect also stays whole (inset ≤ 1.36).
    public double inset;
    public double avail;
    // Arbitrary rotation (Adobe XFDF /Rotate, in degrees). When set, the text is
    // rotated about the rectangle centre and /Rect is expanded to the rotated
    // bounding box so the rotated text isn't clipped by the appearance /BBox.
    public double rotateDeg;
    public bool rotated;
    // ★ A QUARTER TURN (the Rotation enum: on90/on180/on270) is a different model
    // from the arbitrary-degree XFDF rotation below. /Rect is ALREADY the box the
    // caller wants — Rectangle.Rotate turned it about its centre, so a 200x30 box
    // is now the 30x200 box the appearance is drawn in — and re-expanding it here would
    // rotate the box straight back. The box is kept and only the TEXT turns inside
    // it: measured on rotated FreeText output, the border is the
    // plain inset rectangle and the text runs along the box's long axis from the
    // leading edge, lines advancing across it.
    public bool quarterTurn;
    public double bboxW;
    public double bboxH;
    public double rcos;
    public double rsin;
    public double ehw;
    public double ehh;
    public Aspose.Pdf.Core.PdfDictionary fontDict = null!;
    public Aspose.Pdf.Text.FontMetrics metrics = null!;
    public List<string> lines = null!;
    public System.Globalization.CultureInfo ci = null!;
    public double leading;
    public System.Text.StringBuilder sb = null!;
    // Stroke the border rectangle so a styled/dashed border is visible. The
    // stroke uses the text colour and is inset by half the line width so it
    // sits centred on the rectangle edge.
    // A FreeText with no /BS or /Border entry gets the PDF-default 1pt border
    // (PDF 32000-1 §12.5.4 /Border default [0 0 1]) — only an explicit zero
    // width suppresses it.
    public Aspose.Pdf.Core.PdfDictionary? bsDict = null!;
    // Alignment is taken from the persisted /Q justification (which survives a
    // re-wrap of the annotation on save), falling back to the in-memory TextStyle.
    public Aspose.Pdf.HorizontalAlignment align;
    public Aspose.Pdf.Core.PdfStream apStream = null!;
    public Aspose.Pdf.Core.PdfArray bbox = null!;
    public Aspose.Pdf.Core.PdfDictionary fonts = null!;
    public Aspose.Pdf.Core.PdfDictionary res = null!;
    public Aspose.Pdf.Core.PdfDictionary ap = null!;
}
}
