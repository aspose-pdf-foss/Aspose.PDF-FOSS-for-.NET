using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class FlexGridLayoutState
{
    public System.Globalization.CultureInfo invF = null!;
    public (byte[]? ttf, Aspose.Pdf.Text.GlyphOutlineParser? parser, double upm) fSerifB;
    public Aspose.Pdf.Core.PdfDictionary? fontDictF;
    public double contL;
    // With a physical-width flow.page wrapper the container spans exactly
    // that width; otherwise it fills to the body inset on the right.
    public double contR;
    public double contT;
    public double contW;
    // wrapper: the div flavour's 98%-wide 1%-padded inner div; the
    // table flavour's <table width=100%> inset by the UA 2px
    // border-spacing instead.
    public double wrapL;
    public double wrapW;
    // first row top: the table flavour's h1 band runs 3.4pt deeper
    // (the table's own border-spacing above its first row).
    public double fy;
    // the UA border-spacing between table rows (2px).
    public double rowGap;
    public double labelDy;
    public double valueInset;
    public FlexGrid fg = default!;
    public HtmlFlowCursor flow = default!;
    public Document doc = default!;
    public Core.PdfDictionary docFontDict = default!;
    public double marginBottom = 0;
    public double marginLeft = 0;
    public double marginRight = 0;
    public double marginTop = 0;
    public double pageHeight = 0;
    public double pageWidth = 0;
}
}
