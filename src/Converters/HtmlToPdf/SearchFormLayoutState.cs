using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class SearchFormLayoutState
{
    public System.Globalization.CultureInfo invf = null!;
    public double totalH;
    public double cellW;
    public double cellX;
    public double inputW;
    public double inputH;
    public Aspose.Pdf.Forms.TextBoxField fld = null!;
    public Aspose.Pdf.Core.PdfDictionary? res0;
    public Aspose.Pdf.Core.PdfDictionary? fdict;
    public (byte[]? ttf, Aspose.Pdf.Text.GlyphOutlineParser? parser, double upm) arial;
    public ConvertState cv = default!;
    public SearchForm sf = default!;
    public HtmlLoadOptions? options = null;
}
}
