using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class RtlSvgDiagramState
{
    public System.Globalization.CultureInfo invd = null!;
    public double canvasW;
    public double canvasRight;
    public double canvasLeft;
    public (byte[]? ttf, Aspose.Pdf.Text.GlyphOutlineParser? parser, double upm) arialD;
    public Aspose.Pdf.Core.PdfDictionary? fontDictD;
    public double titleRowH;
    public double figH;
    public double labelRowH;
    public double legendBoxH;
    public double legendLabelH;
    public double totalH;
    public ConvertState cv = default!;
    public RtlSvgTable dg = default!;
    public List<byte[]> inlineSvgs = default!;
}
}
