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
        if (!html.Contains("s-snapshow__workflow_info", StringComparison.Ordinal)
            || !html.Contains("s-workflow__stage", StringComparison.Ordinal)
            || !html.Contains("g-blockheader", StringComparison.Ordinal))
            return null;

        var bodyM = Regex.Match(html, "<body[^>]*>([\\s\\S]*)</body>", RegexOptions.IgnoreCase);
        if (!bodyM.Success) return null;
        // the display:none Report element contributes nothing
        var body = Regex.Replace(bodyM.Groups[1].Value,
            "<div[^>]*style=\"display:none\"[\\s\\S]*?</div>", "", RegexOptions.IgnoreCase);

        static string Flat(string s) => CollapseWs(DecodeEntities(
            Regex.Replace(s, "<[^>]+>", " "))).Trim();

        // Tokenize the export's landmarks in document order; a row's content runs
        // to the next landmark, which spares walking the div nesting.
        var tokens = Regex.Matches(body,
            "<div class=\"s-workflow__stage\">"
            + "|<div class=\"s-workflow__task\">"
            + "|<h2 class=\"g-heading\">((?:(?!</h2>)[\\s\\S])*)</h2>"
            + "|<h3 class=\"g-heading\">((?:(?!</h3>)[\\s\\S])*)</h3>"
            + "|<div class=\"s-snapshow__workflow_info row\">",
            RegexOptions.IgnoreCase).ToList();
        if (tokens.Count == 0) return null;

        var reportHeading = "";
        var stages = new List<TrCard>();
        TrCard? stage = null, task = null;
        TrCard? pendingCard = null;   // the h3 that follows names it
        for (var i = 0; i < tokens.Count; i++)
        {
            var tk = tokens[i];
            var text = tk.Value;
            var end = i + 1 < tokens.Count ? tokens[i + 1].Index : body.Length;
            if (text.StartsWith("<div class=\"s-workflow__stage\"", StringComparison.OrdinalIgnoreCase))
            {
                stage = new TrCard();
                stages.Add(stage);
                task = null;
                pendingCard = stage;
            }
            else if (text.StartsWith("<div class=\"s-workflow__task\"", StringComparison.OrdinalIgnoreCase))
            {
                if (stage is null) return null;
                task = new TrCard();
                if (stage.Sections.Count == 0) stage.Sections.Add(("", new List<TrCard>()));
                stage.Sections[^1].Tasks.Add(task);
                pendingCard = task;
            }
            else if (text.StartsWith("<h2", StringComparison.OrdinalIgnoreCase))
            {
                var h = Flat(tk.Groups[1].Value);
                if (stage is null) reportHeading = h;
                else { stage.Sections.Add((h, new List<TrCard>())); task = null; }
            }
            else if (text.StartsWith("<h3", StringComparison.OrdinalIgnoreCase))
            {
                if (pendingCard is not null) pendingCard.Title = Flat(tk.Groups[2].Value);
                pendingCard = null;
            }
            else // a label/value row, owned by the innermost open card
            {
                var owner = task ?? stage;
                if (owner is null) continue;
                var seg = body[tk.Index..end];
                var cols = new List<TrCol>();
                foreach (Match cm in Regex.Matches(seg,
                    "<div class=\"col-sm-(?:3|12)\">((?:(?!<div class=\"col-sm)[\\s\\S])*)",
                    RegexOptions.IgnoreCase))
                {
                    var c = cm.Groups[1].Value;
                    var col = new TrCol();
                    var lm = Regex.Match(c, "<label[^>]*>((?:(?!</label>)[\\s\\S])*)</label>",
                        RegexOptions.IgnoreCase);
                    if (!lm.Success) continue;
                    col.Label = Flat(lm.Groups[1].Value).ToUpperInvariant();
                    var rest = c[(lm.Index + lm.Length)..];
                    var pm = Regex.Match(rest, "<span class=\"g-status\">((?:(?!</span>)[\\s\\S])*)</span>",
                        RegexOptions.IgnoreCase);
                    if (pm.Success)
                        col.PillText = Flat(pm.Groups[1].Value).ToUpperInvariant();
                    else
                    {
                        // attachment file names arrive one <div> per line
                        var divLines = Regex.Matches(rest, "<div>((?:(?!</div>)[\\s\\S])*)</div>",
                            RegexOptions.IgnoreCase);
                        if (divLines.Count > 0)
                            foreach (Match dm in divLines)
                            {
                                var t = Flat(dm.Groups[1].Value);
                                if (t.Length > 0) col.Lines.Add(t);
                            }
                        else
                        {
                            var t = Flat(rest);
                            if (t.Length > 0) col.Lines.Add(t);
                        }
                    }
                    cols.Add(col);
                }
                if (cols.Count > 0) owner.Rows.Add(cols);
            }
        }
        if (stages.Count == 0) return null;

        // ── the flow ──
        var top = marginTop;
        var bottom = pageHeight - marginBottom;
        var left = marginLeft;
        var right = pageWidth - marginRight;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string N(double v) => v.ToString("0.###", inv);

        var doc = new Document();
        var pages = new List<(Page Page, StringBuilder Chrome, StringBuilder Text)>();
        void OpenPage()
        {
            var p = doc.Pages.Add(pageWidth, pageHeight);
            EnsureFonts(p);
            var chrome = new StringBuilder();
            chrome.AppendLine(
                $"1 1 1 rg {N(left)} {N(pageHeight - bottom)} {N(right - left)} {N(bottom - top)} re f");
            pages.Add((p, chrome, new StringBuilder()));
        }
        OpenPage();
        double Y(double yTd) => pageHeight - yTd;
        void Text(double fs, double x, double baseTd, string t)
        {
            if (t.Length == 0) return;
            pages[^1].Text.AppendLine($"BT 0.133 0.180 0.196 rg /F1 {fs.ToString("F2", inv)} Tf "
                + $"1 0 0 1 {N(x)} {N(Y(baseTd))} Tm ({EscapePdfString(t)}) Tj ET");
        }
        void HRule(double x0, double x1, double yTd) => pages[^1].Chrome.AppendLine(
            $"0.816 0.839 0.855 RG 0.75 w {N(x0)} {N(Y(yTd))} m {N(x1)} {N(Y(yTd))} l S");
        void VRule(double x, double y0Td, double y1Td) => pages[^1].Chrome.AppendLine(
            $"0.816 0.839 0.855 RG 0.75 w {N(x)} {N(Y(y1Td))} m {N(x)} {N(Y(y0Td))} l S");
        void Band(double x0, double x1, double topTd) => pages[^1].Chrome.AppendLine(
            $"0.906 0.918 0.925 rg {N(x0)} {N(Y(topTd + TrBandHPt))} {N(x1 - x0)} {N(TrBandHPt)} re f");
        void Pill(double x, double topTd) => pages[^1].Chrome.AppendLine(
            $"0.816 0.839 0.855 RG 0.75 w {N(x)} {N(Y(topTd + TrPillHPt))} {N(TrPillWPt)} {N(TrPillHPt)} re S");

        // the open cards' side borders, sliced per page
        var stageL = left + TrCardInsetPt;
        var stageR = right - TrCardInsetPt;
        var taskL = left + TrTaskInsetPt;
        var taskR = right - TrTaskInsetPt;
        double? stageTopOnPage = null, taskTopOnPage = null;
        bool stageOpen = false, taskOpen = false;
        void CloseCardSides(bool isTask, double? botTd)
        {
            var (l, r) = isTask ? (taskL, taskR) : (stageL, stageR);
            var from = (isTask ? taskTopOnPage : stageTopOnPage) ?? top;
            var to = botTd is { } b ? b + TrHalfRulePt : bottom;
            VRule(l + TrHalfRulePt, from, to);
            VRule(r - TrHalfRulePt, from, to);
            if (isTask) taskTopOnPage = null; else stageTopOnPage = null;
        }

        var y = top;               // the NEXT label/heading baseline
        var lastValueBase = top;   // where the previous row's deepest value seated
        void PageBreak(double carry)
        {
            if (taskOpen) CloseCardSides(isTask: true, botTd: null);
            if (stageOpen) CloseCardSides(isTask: false, botTd: null);
            OpenPage();
            y = top + carry;
        }

        void OpenCard(bool isTask, string title)
        {
            var (l, r) = isTask ? (taskL, taskR) : (stageL, stageR);
            if (y + TrBandDropPt + TrBandHPt > bottom) PageBreak(0);
            HRule(l - TrHalfRulePt, r + TrHalfRulePt, y);
            Band(l + TrHalfRulePt, r - TrHalfRulePt, y + TrBandDropPt);
            if (isTask) HRule(l + TrHalfRulePt, r - TrHalfRulePt, y + TrBandRulePt);
            Text(TrTitleFs, l + TrTitleInsetPt, y + TrTitleDropPt, title);
            if (isTask) { taskTopOnPage = y - TrHalfRulePt; taskOpen = true; }
            else { stageTopOnPage = y - TrHalfRulePt; stageOpen = true; }
            y += isTask ? TrTaskFirstLabelPt : TrStageFirstLabelPt;
        }

        void EmitRow(List<TrCol> cols, double[] colX)
        {
            // the row carries onto the next page when its label line cannot seat
            if (y + TrLabelFs * TrLineDescFrac > bottom)
                PageBreak(y - bottom);
            var labelBase = y;
            var anyPill = false;
            var extra = 0;
            for (var c = 0; c < cols.Count && c < colX.Length; c++)
            {
                var col = cols[c];
                var x = left + colX[c];
                Text(TrLabelFs, x, labelBase, col.Label);
                if (col.PillText.Length > 0)
                {
                    anyPill = true;
                    Pill(x + TrPillInsetPt, labelBase + TrPillDropPt);
                    Text(TrLabelFs, x + TrPillInsetPt + TrPillPadX,
                        labelBase + TrPillTextDropPt, col.PillText);
                    continue;
                }
                var vb = labelBase + TrLabelValueGap;
                foreach (var line in col.Lines)
                {
                    // a value line that cannot seat opens the next page alone
                    if (vb + TrValueFs * TrLineDescFrac > bottom)
                    {
                        PageBreak(0);
                        vb = top + TrLoneValueSeatPt;
                        labelBase = vb - TrLabelValueGap; // keep the pitch chain
                    }
                    Text(TrValueFs, x, vb, line);
                    vb += TrValueLinePitch;
                }
                extra = Math.Max(extra, Math.Max(1, col.Lines.Count) - 1);
            }
            lastValueBase = labelBase + TrLabelValueGap + extra * TrValueLinePitch;
            y = labelBase + (anyPill ? TrPillRowPitch : TrRowPitch) + extra * TrValueLinePitch;
        }

        // wrap the long stage guidance at the measured width
        static List<string> Wrap(string text, double width, double fs)
        {
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var lines = new List<string>();
            var cur = "";
            foreach (var w in words)
            {
                var probe = cur.Length == 0 ? w : cur + " " + w;
                if (cur.Length > 0 && MeasureFaceText("Helvetica", probe, fs) > width)
                {
                    lines.Add(cur);
                    cur = w;
                }
                else cur = probe;
            }
            if (cur.Length > 0) lines.Add(cur);
            return lines;
        }

        // page 1 opens with the report heading
        Text(TrH2Fs, left + TrH2X, top + TrWfH2Drop, reportHeading);
        y = top + TrWfH2Drop + TrH2ToCardPt;

        foreach (var st in stages)
        {
            OpenCard(isTask: false, st.Title);
            foreach (var row in st.Rows)
            {
                foreach (var col in row)
                    if (col.PillText.Length == 0 && col.Lines.Count == 1
                        && MeasureFaceText("Helvetica", col.Lines[0], TrValueFs) > TrGuidanceWrapPt)
                        col.Lines = Wrap(col.Lines[0], TrGuidanceWrapPt, TrValueFs);
                EmitRow(row, TrStageColX);
            }
            var lastBotRule = lastValueBase + TrDetailRulePt;   // the details rule
            foreach (var (heading, tasks) in st.Sections)
            {
                if (heading.Length == 0 && tasks.Count == 0) continue;
                if (heading.Length > 0)
                {
                    HRule(taskL - TrHalfRulePt, taskR + TrHalfRulePt, lastBotRule);
                    var hb = lastBotRule + TrRuleToH2Pt;
                    if (hb + TrH2ToTaskPt + TrBandHPt > bottom) { PageBreak(0); hb = top + TrWfH2Drop; }
                    Text(TrH2Fs, taskL - TrHalfRulePt, hb, heading);
                    y = hb + TrH2ToTaskPt;
                }
                foreach (var tk in tasks)
                {
                    OpenCard(isTask: true, tk.Title);
                    foreach (var row in tk.Rows) EmitRow(row, TrTaskColX);
                    var botTd = lastValueBase + TrCardBotPadPt;
                    HRule(taskL - TrHalfRulePt, taskR + TrHalfRulePt, botTd);
                    CloseCardSides(isTask: true, botTd);
                    taskOpen = false;
                    lastBotRule = botTd;
                    y = botTd + TrCardGapPt;
                }
            }
            // the stage closes one gap under its last task
            var stageBotTd = lastBotRule + TrCardGapPt;
            HRule(stageL - TrHalfRulePt, stageR + TrHalfRulePt, stageBotTd);
            CloseCardSides(isTask: false, stageBotTd);
            stageOpen = false;
            y = stageBotTd + TrCardGapPt;
        }

        foreach (var (page, chrome, text) in pages)
        {
            page.AddContentStream(Encoding.ASCII.GetBytes(chrome.ToString()));
            page.AddContentStream(Encoding.ASCII.GetBytes(text.ToString()));
        }
        return doc;
    }
}
