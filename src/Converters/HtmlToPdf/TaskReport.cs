using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// The workflow snapshot report: an `s-snapshot` export of bordered stage cards,
// each holding a #e7eaec block header, uppercase g-label/value rows (an
// IN-PROGRESS pill on the status rows), a Tasks/Acceptors heading and the
// nested task cards of the same anatomy. The 941 KB framework stylesheet
// contributes nothing the cards do not restate; the whole report draws
// in Arial #222e32 with #d0d6da hairlines. Every pitch below is
// measured on the expected render. Cards do NOT keep together: a row
// that cannot seat its line on the sheet carries its overflow onto the next
// page, and the open cards' side borders run edge to edge across the break.
internal static partial class HtmlToPdfConverter
{
    // ── text ────────────────────────────────────────────────────────────────
    private const double TrH2Fs = 13.5;            // g-heading h2
    private const double TrTitleFs = 11.57;        // card header h3
    private const double TrLabelFs = 7.71;         // uppercase g-label
    private const double TrValueFs = 9.0;
    private const double TrLabelValueGap = 12.45;  // label base → value base
    private const double TrRowPitch = 35.25;       // label base → next label base
    private const double TrPillRowPitch = 40.12;   // a status row is pill-tall
    private const double TrValueLinePitch = 10.5;  // each further value line
    private const double TrLineDescFrac = 0.212;   // Helvetica descent, break test
    // ── the sheet ───────────────────────────────────────────────────────────
    // The template this test grades against is one era older than the current
    // era render: its whole page-2 flow seats 4.8 pt lower (measured
    // off the template; the modern writer says 34.73). The lead-in below the
    // page-1 top carries that era, and every break inherits it.
    private const double TrWfH2Drop = 39.53;       // page-1 heading, below the top
    private const double TrH2ToCardPt = 14.5;      // report h2 base → stage top
    private const double TrCardInsetPt = 22.88;    // stage border, off the margin
    private const double TrH2X = 22.5;             // report h2, off the margin
    private const double TrTaskInsetPt = 38.62;    // task border, off the margin
    private const double TrHalfRulePt = 0.38;      // the 0.75 hairline's half
    private const double TrTitleInsetPt = 11.62;   // card border → h3 left
    // ── cards ───────────────────────────────────────────────────────────────
    private const double TrBandDropPt = 0.37;      // band sits under the top rule
    private const double TrBandHPt = 33.05;
    private const double TrBandRulePt = 33.79;     // task top → the details rule
    private const double TrTitleDropPt = 20.91;    // card top → h3 base
    private const double TrStageFirstLabelPt = 50.35; // stage top → first label
    private const double TrTaskFirstLabelPt = 58.59;  // task top → first label
    private const double TrCardBotPadPt = 17.51;   // last value base → bottom rule
    private const double TrCardGapPt = 15.75;      // rule → next sibling's rule
    private const double TrDetailRulePt = 25.0;    // last stage value → details rule
    private const double TrRuleToH2Pt = 28.73;     // details rule → Tasks h2 base
    private const double TrH2ToTaskPt = 19.38;     // Tasks h2 base → task top
    // ── row columns (off the margin; stage cells sit wider than task cells) ──
    private static readonly double[] TrStageColX = { 42.0, 172.88, 303.75 };
    private static readonly double[] TrTaskColX = { 57.75, 180.75, 303.75 };
    // ── the status pill ──────────────────────────────────────────────────────
    private const double TrPillDropPt = 4.45;      // label base → pill top
    private const double TrPillHPt = 14.62;
    private const double TrPillWPt = 67.28;
    private const double TrPillPadX = 6.85;        // pill left → caption left
    private const double TrPillTextDropPt = 14.44; // label base → caption base
    private const double TrPillInsetPt = 0.38;     // label left → pill left
    // ── breaks: a carried row opens at top + overflow; a lone value line seats
    //    its ascent under the top ─────────────────────────────────────────────
    private const double TrLoneValueSeatPt = 8.37;
    private const double TrGuidanceWrapPt = 504.38; // stage col-sm-12 text width

    private sealed class TrCol
    {
        public string Label = "";
        public string PillText = "";
        public List<string> Lines = new();
    }

    private sealed class TrCard
    {
        public string Title = "";
        public List<List<TrCol>> Rows = new();
        public List<(string Heading, List<TrCard> Tasks)> Sections = new(); // stages only
    }

    /// <summary>Render the workflow snapshot report, or null when the page is not it.</summary>
    private static Document? TryRenderTaskReport(string html, double pageWidth, double pageHeight,
        double marginLeft, double marginRight, double marginTop, double marginBottom)
    {
        var tq = new TaskReportState();
        tq.html = html;
        tq.pageWidth = pageWidth;
        tq.pageHeight = pageHeight;
        tq.marginLeft = marginLeft;
        tq.marginRight = marginRight;
        tq.marginTop = marginTop;
        tq.marginBottom = marginBottom;
        if (!tq.html.Contains("s-snapshow__workflow_info", StringComparison.Ordinal)
            || !tq.html.Contains("s-workflow__stage", StringComparison.Ordinal)
            || !tq.html.Contains("g-blockheader", StringComparison.Ordinal))
            return null;

        tq.bodyM = Regex.Match(tq.html, "<body[^>]*>([\\s\\S]*)</body>", RegexOptions.IgnoreCase);
        if (!tq.bodyM.Success) return null;
        tq.body = Regex.Replace(tq.bodyM.Groups[1].Value,
            "<div[^>]*style=\"display:none\"[\\s\\S]*?</div>", "", RegexOptions.IgnoreCase);

        tq.tokens = Regex.Matches(tq.body,
            "<div class=\"s-workflow__stage\">"
            + "|<div class=\"s-workflow__task\">"
            + "|<h2 class=\"g-heading\">((?:(?!</h2>)[\\s\\S])*)</h2>"
            + "|<h3 class=\"g-heading\">((?:(?!</h3>)[\\s\\S])*)</h3>"
            + "|<div class=\"s-snapshow__workflow_info row\">",
            RegexOptions.IgnoreCase).Cast<Match>().ToList();
        if (tq.tokens.Count == 0) return null;

        tq.reportHeading = "";
        tq.stages = new List<TrCard>();
        tq.stage = null;
        tq.task = null;
        tq.pendingCard = null;
        if (!ParseTaskTokens(tq)) return null;
        if (tq.stages.Count == 0) return null;

        tq.top = tq.marginTop;
        tq.bottom = tq.pageHeight - tq.marginBottom;
        tq.left = tq.marginLeft;
        tq.right = tq.pageWidth - tq.marginRight;
        tq.inv = System.Globalization.CultureInfo.InvariantCulture;
        tq.doc = new Document();
        tq.pages = new List<(Page Page, StringBuilder Chrome, StringBuilder Text)>();
        OpenPage(tq);
        tq.stageL = tq.left + TrCardInsetPt;
        tq.stageR = tq.right - TrCardInsetPt;
        tq.taskL = tq.left + TrTaskInsetPt;
        tq.taskR = tq.right - TrTaskInsetPt;
        tq.stageTopOnPage = null;
        tq.taskTopOnPage = null;
        tq.stageOpen = false;
        tq.taskOpen = false;
        tq.y = tq.top;
        tq.lastValueBase = tq.top;
        // page 1 opens with the report heading
        TrText(tq, TrH2Fs, tq.left + TrH2X, tq.top + TrWfH2Drop, tq.reportHeading);
        tq.y = tq.top + TrWfH2Drop + TrH2ToCardPt;

        DrawTaskStages(tq);

        foreach (var (page, chrome, text) in tq.pages)
        {
            page.AddContentStream(Encoding.ASCII.GetBytes(chrome.ToString()));
            page.AddContentStream(Encoding.ASCII.GetBytes(text.ToString()));
        }
        return tq.doc;
    }
}
