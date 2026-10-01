using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class AuditReportState
{
    // Roboto ships WITH the document rather than the system, registered as a
    // folder source under its file name. Ask for the installed face by name -
    // a plain repository lookup answers with a substitute whose advances run
    // over a percent wide, which is enough to move a line break.
    public byte[] reg = null!;
    public System.Text.RegularExpressions.Match bodyM = null!;
    public string body = null!;
    public double pageW;
    public double left;
    public double top;
    public double bottom;
    public List<Aspose.Pdf.Converters.HtmlToPdfConverter.CtItem> items = null!;
    public List<(int Sheet, double Top)> rows = null!;
    public List<(int Sheet, double X, double Top, double W, double H, Aspose.Pdf.Color C)> fills = null!;
    public List<(int Sheet, double X0, double X1, double Y, Aspose.Pdf.Color C)> rules = null!;
    public int sheet;
    public double y;
    public bool sheetHasGrid;
    // a section may split into floated columns; while one is open the
    // percentages resolve against IT rather than the sheet
    public double colLeft;
    public double colWidth;
    public double colRowTop;
    public double colDeepest;
    public int colSheet;
    public bool inCols;
    public Document doc = null!;
    public List<Aspose.Pdf.Page> pages = null!;
    public double pendingMain;
    public bool mainOpen;
    // ── emit ────────────────────────────────────────────────────────────
    public System.Globalization.CultureInfo invc = null!;
    public List<(int Sheet, int Layer, int Seq, string Text)> ops = null!;
    public int seq;
}
}
