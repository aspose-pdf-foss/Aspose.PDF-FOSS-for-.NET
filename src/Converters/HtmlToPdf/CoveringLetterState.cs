using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class CoveringLetterState
{
    public (double asc, double sum) cm;
    public double contentL;
    public double contentR;
    public double justL;
    public double justR;
    public double liX;
    public double liRight;
    public double dropP;
    public double dropEm;
    // ── parse ──
    public System.Text.RegularExpressions.Match h2M = null!;
    public System.Text.RegularExpressions.Match justM = null!;
    public System.Text.RegularExpressions.Match addrM = null!;
    public List<string> h2Lines = null!;
    public System.Text.RegularExpressions.MatchCollection addrTds = null!;
    public string addrName = null!;
    public List<string> addrLeft = null!;
    // ── the flow: .justify children in order ──
    public List<Aspose.Pdf.Converters.HtmlToPdfConverter.LtLine> flow = null!;
    public double prevBottom;
    public double firstGapExtra;
    public string body = null!;
    // ── emit ──
    public Document doc = null!;
    public Page page = null!;
    public System.Globalization.CultureInfo inv = null!;
    // page 1 header: the h2 block and the two-column address table
    public double h2Line;
    public double h2Drop;
    public double y;
    public double tableX;
    public double tableRight;
    // the paginated letter flow
    public Page pg = null!;
    public string html = default!;
    public double pageWidth = 0;
    public double pageHeight = 0;
    public List<string> addrRight = null!;
}
}
