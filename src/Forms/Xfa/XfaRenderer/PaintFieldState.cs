using System.Globalization;
using System.Text;
using System.Xml;

namespace Aspose.Pdf.Forms.Xfa;

internal static partial class XfaRenderer
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class PaintFieldState
{
    public (double reserve, string placement) caption;
    public double res;
    public string plc = null!;
    public string cap = null!;
    public double fs;
    public bool check;
    public bool radio;
    public string? ui;
    // Caption region: left (the XFA default) / right reserve a horizontal strip,
    // top / bottom a vertical one; the edit region is what remains.
    public double capW;
    public double capH;
    public (double fs, bool bold) captionFont;
    public double capFs;
    public bool capBold;
    public double ex;
    public double ew;
    public double ey;
    public double eh;
    // Widget box: the visible edit chrome (border box / underline / fill) sits
    // INSIDE the margin insets — the field box holds caption strip + insets +
    // widget, and only the widget shows a border. Text keeps the edit region
    // (AddText applies the left/right insets itself).
    public (double, double, double, double) widgetMargins;
    public double wmT;
    public double wmB;
    public double wmL;
    public double wmR;
    public double bx;
    public double by;
    public double bw;
    public double bh;
    // datasets-bound value if present (bound-but-empty stays empty), else the
    // SOM-resolved value, else the template default. All picture-formatted;
    // a match="none" bind blocks the data paths entirely.
    public string? val;
    // A FIELD-level border paints its fill (Designer's shaded answer cells) and,
    // with a visible edge, strokes the field's whole box (table cells outline
    // this way — the row rules of a Designer grid).
    public double[]? fieldFill;
    public System.Xml.XmlElement? fieldBorder;
    public Ctx ctx = default!;
    public XmlElement e = default!;
    public double x = 0;
    public double y = 0;
    public double w = 0;
    public double h = 0;
    public string path = default!;
}
}
