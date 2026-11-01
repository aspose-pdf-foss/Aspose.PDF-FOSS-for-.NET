using Aspose.Pdf.Core;
using Aspose.Pdf.Devices;

namespace Aspose.Pdf;

public sealed partial class Document
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class TransparentScanState
{
    public Aspose.Pdf.Core.PdfDictionary? extGStates;
    // gs name → the transparency-relevant values it sets (null = not set).
    // Sm: true = the state sets a soft-mask DICT, false = /SMask /None
    // (clears one), null = /SMask not mentioned. A luminosity/alpha soft mask
    // is transparency like a fractional alpha is: PDF/A forbids it, and
    // neutralising it without a raster leaves the masked paint opaque (a
    // gradient-fade band turns into a solid box over the photo beneath).
    public Dictionary<string, (double? Ca, double? CA, string? Bm, bool? Sm)> gsInfo = null!;
    public bool anyTransparent;
    public bool initialTransparent;
    public string text = null!;
    public System.Text.StringBuilder output = null!;
    public Stack<(Aspose.Pdf.Document.SimMatrix Ctm, double FillA, double StrokeA, string Bm, bool Sm, (int comps, double[] vals)? FillCol, (int comps, double[] vals)? StrokeCol, int Tr)> stack = null!;
    public Document.SimMatrix ctm;
    public double fillA;
    public double strokeA;
    public string blend = null!;
    public bool softMask;
    // The text rendering mode (Tr), and where the operands of the pending operator start in
    // the output: a show under an alpha of 0 is wrapped in the invisible mode there.
    public int textRender;
    public int operandStart;
    // Current fill/stroke colour for the recolour mode (1=gray, 3=rgb, 4=cmyk; null
    // = unknown, e.g. after cs/CS to a pattern space). PDF initial = black.
    public (int comps, double[] vals)? fillCol;
    public (int comps, double[] vals)? strokeCol;
    public string? lastName;
    public List<double> nums = null!;
    public bool changed;
    public int pos;
    // Current path extent in device space.
    public double pMinX;
    public double pMinY;
    public double pMaxX;
    public double pMaxY;
    public bool hasPath;
    // Rough text tracking for regions: the text matrix (Tm, Td, BT) and the font size.
    public Document.SimMatrix tm = Document.SimMatrix.Identity;
    public double fontSize;
    // The selected font is an embedded composite one: its constant-alpha text is not composited.
    public bool embeddedCompositeFont;
    public int lastStringLen;
    // an inline image ends the scan with the page kept as-is
    public bool bailOut;
}
}
