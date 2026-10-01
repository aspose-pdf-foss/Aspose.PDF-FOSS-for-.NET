using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class RfqFormState
{
    // the five INNER tables, in document order
    public List<string> tables = null!;
    public System.Text.RegularExpressions.Match logoM = null!;
    public List<string> titleLabels = null!;
    public List<System.Text.RegularExpressions.Match> band1Cells = null!;
    public List<string> band2Labels = null!;
    public List<System.Text.RegularExpressions.Match> introCells = null!;
    public string grid = null!;
    public List<string> reqSpans = null!;
    public List<string> dateSpans = null!;
    public List<string> introEn = null!;
    public List<string> introAr = null!;
    // the grid's header cells and data rows
    public List<(string Ar, string En)> headers = null!;
    public List<(string Serial, string Label, List<string> Items)> rows = null!;
    public Document doc = null!;
    public Page page = null!;
    public System.Globalization.CultureInfo inv = null!;
    public Aspose.Pdf.Text.Font? tahoma;
    public Aspose.Pdf.Text.Font? tahomaBd;
    public System.Text.StringBuilder fills = null!;
    public double rowTop;
    public List<(double Top, double H)> rowTops = null!;
    public System.Text.StringBuilder runs = null!;
    public string[] enLines = null!;
    public string html = default!;
    public double pageWidth = 0;
    public double pageHeight = 0;
    public Aspose.Pdf.Core.PdfDictionary fontDict = null!;
}
}
