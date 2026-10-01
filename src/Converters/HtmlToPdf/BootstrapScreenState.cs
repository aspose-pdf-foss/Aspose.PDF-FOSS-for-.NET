using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class BootstrapScreenState
{
    // The face: first INSTALLED family of the body stack, else the sans
    // default substitute (Arial).
    public string face = null!;
    public double xHalf;
    public Color bodyColor = null!;
    public Color linkColor = null!;
    public double lineH;
    public double drop;
    // h2: the theme's size, 1.1 line box, 18px/9px margins.
    public double h2Fs;
    public double h2LineF;
    public double h2MarT;
    public double h2MarB;
    public double h2LineH;
    public double h2Drop;
    // p { margin: 0 0 9px }
    public double pMarB;
    // .container { padding-left/right: 15px }
    public double containerPad;
    // table geometry from the sheet: cell padding, border colour, thead's
    // 2px bottom border, the table's own margin-bottom and white background.
    public double cellPad;
    public double tableMarB;
    public Color borderCol = null!;
    public Color tableBg = null!;
    // .btn box model + variant colours.
    public double btnPadY;
    public double btnPadX;
    public double btnLgPadY;
    public double btnLgPadX;
    public double btnLgFs;
    public double btnLgLineF;
    // ── parse the body into a linear block list ──
    public System.Text.RegularExpressions.Match bodyM = null!;
    public string bodyHtml = null!;
    // The MVC-template arm: a fixed navbar over a jumbotron. Both are drawn
    // from their measured chrome below and STRIPPED here so the linear walk
    // does not double-render their content; the arm also wraps paragraphs at
    // the container width, collapses a paragraph margin into a following
    // heading's, and draws <hr> rules — all reference-measured behaviours of
    // this page shape.
    public bool jumboDoc;
    public string navBrand = null!;
    public string jumboH1 = null!;
    public string jumboLead = null!;
    public string jumboBtnLabel = null!;
    public string jumboBtn2Label = null!;
    public Color jumboBtnFill = null!;
    public Color jumboBtnBorder = null!;
    public List<Aspose.Pdf.Converters.HtmlToPdfConverter.BsBlock> blocks = null!;
    // ── layout ──
    public Document doc = null!;
    public Page page = null!;
    public string boldFace = null!;
    public double contentX;
    public double contentW;
    public System.Globalization.CultureInfo invc = null!;
    public double yTd;
    public double lastPMarB;
    public Dictionary<string, string> body = null!;
    public Color bodyBg = null!;
    public double bodyFs;
    public double lineFactor;
    public (double asc, double sum) fm;
}
}
