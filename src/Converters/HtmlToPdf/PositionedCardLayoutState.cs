using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class PositionedCardLayoutState
{
    public System.Globalization.CultureInfo invC = null!;
    public (byte[]? ttf, Aspose.Pdf.Text.GlyphOutlineParser? parser, double upm) cSerifR;
    public (byte[]? ttf, Aspose.Pdf.Text.GlyphOutlineParser? parser, double upm) cSerifB;
    public Aspose.Pdf.Core.PdfDictionary? fontDictC;
    public double cx0;
    public double mediaTop;
    public double mediaBot;
    public double cardRight;
    public PositionedCard pc = default!;
    public HtmlFlowCursor flow = default!;
    public double marginLeft = 0;
}
}
