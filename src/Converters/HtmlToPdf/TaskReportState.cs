using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class TaskReportState
{
    public System.Text.RegularExpressions.Match bodyM = null!;
    // the display:none Report element contributes nothing
    public string body = null!;
    // Tokenize the export's landmarks in document order; a row's content runs
    // to the next landmark, which spares walking the div nesting.
    public List<System.Text.RegularExpressions.Match> tokens = null!;
    public string reportHeading = null!;
    public List<Aspose.Pdf.Converters.HtmlToPdfConverter.TrCard> stages = null!;
    public TrCard? stage;
    public TrCard? task;
    public TrCard? pendingCard;
    // ── the flow ──
    public double top;
    public double bottom;
    public double left;
    public double right;
    public System.Globalization.CultureInfo inv = null!;
    public Document doc = null!;
    public List<(Aspose.Pdf.Page Page, System.Text.StringBuilder Chrome, System.Text.StringBuilder Text)> pages = null!;
    // the open cards' side borders, sliced per page
    public double stageL;
    public double stageR;
    public double taskL;
    public double taskR;
    public double? stageTopOnPage;
    public double? taskTopOnPage;
    public bool stageOpen;
    public bool taskOpen;
    public double y;
    public double lastValueBase;
    public string html = null!;
    public double pageWidth = 0;
    public double pageHeight = 0;
    public double marginLeft = 0;
    public double marginRight = 0;
    public double marginTop = 0;
    public double marginBottom = 0;
}
}
