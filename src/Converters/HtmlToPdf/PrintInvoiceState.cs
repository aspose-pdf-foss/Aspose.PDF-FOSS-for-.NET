using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class PrintInvoiceState
{
    public System.Text.RegularExpressions.Match bodyPctM = null!;
    public Dictionary<string, Dictionary<string, string>> css = null!;
    public HtmlNode dom = null!;
    // Collect top-level tables in document order; each row's cells with
    // their emphasis. A table containing a row of 4+ populated cells is the
    // ITEM table; tables after it are trailer tables.
    public List<List<(List<(string Text, bool Th, bool Italic)> Cells, bool AnyTh)>> tables = null!;
    public int itemTableIdx;
    public Document doc = null!;
    public double pageW;
    public Page page = null!;
    public Aspose.Pdf.Core.PdfDictionary fontDict = null!;
    public double x0;
    public double bodyW;
    public double bodyR;
    public double fontPt;
    public (byte[]? ttf, Aspose.Pdf.Text.GlyphOutlineParser? parser, double upm) reg;
    public (byte[]? ttf, Aspose.Pdf.Text.GlyphOutlineParser? parser, double upm) bold;
    public (byte[]? ttf, Aspose.Pdf.Text.GlyphOutlineParser? parser, double upm) ital;
    public System.Globalization.CultureInfo inv = null!;
    public double[] colEdges = null!;
    public double yTop;
    public double trailerTop;
    public System.Text.RegularExpressions.Match qrM = null!;
    public string html = default!;
    public HtmlLoadOptions? options = null;
}
}
