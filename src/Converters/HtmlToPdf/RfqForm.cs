using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// The bilingual request-for-quotation form: an outer wrapper table whose rows
// hold, in order, the logo/title header, the REQ-NO/Date band and the
// Closing/Delivery band (both #eef7f8), the grey bilingual intro paragraphs,
// and the bordered items grid (#8d98b2 header, #eff0f1 rows, cellspacing 1).
// English runs draw in Arial/Arial-Bold, the Arabic ones in shaped Tahoma
// through an embedded Type0 face — the faces the expected output picked.
// Every position below is measured on the expected render; the only
// live rule is the wrapping, where a label that does not fit its
// cell drops exactly its LAST word to the following line.
internal static partial class HtmlToPdfConverter
{
    // ── shared sheet geometry (A4, margins 96, content box 96..499) ────────
    private const double RfqTextLeftPt = 98.25;     // content left + the cell inset
    private const double RfqBandLeftPt = 96.0;      // the #eef7f8 bands run edge to edge
    private const double RfqBandRightPt = 499.0;
    // ── the logo/title header ───────────────────────────────────────────────
    private const double RfqLogoBase = 100.05;      // the alt text, UA serif 12
    private const double RfqArTitleX = 398.10;      // Arabic title, right-anchored block
    private const double RfqArTitleBase = 92.14;
    private const double RfqEnTitleX = 336.53;      // REQUEST FOR QUOTATION
    private const double RfqEnTitleBase = 109.16;
    private const double RfqTitleFs = 12.0;
    // ── the REQ-NO/Date band (#eef7f8, 121.5..162.75) ───────────────────────
    private const double RfqBand1Top = 121.5;
    private const double RfqBand1Bot = 162.75;
    private const double RfqReqNoBase = 132.76;     // REQ. NO. keeps its own line
    private const double RfqVal1X = 155.82;         // the inline-block value
    private const double RfqValBase = 145.80;
    private const double RfqArReqX = 256.50;        // ": <word>" after the value
    private const double RfqArReqWrapBase = 158.37; // the dropped last word's line
    private const double RfqDateX = 322.04;
    private const double RfqDateBase = 146.17;      // the Date cell sits 0.37 lower
    private const double RfqVal2X = 385.32;
    private const double RfqColonX = 461.62;        // the raw ':' text node, serif 12
    private const double RfqArDateX = 464.96;
    // ── the Closing/Delivery band (#eef7f8, 162.75..191.25) ─────────────────
    private const double RfqBand2Bot = 191.25;
    private const double RfqClosingBase = 180.38;
    private const double RfqArCloseX = 285.87;      // both Arabic closing lines
    private const double RfqArClose1Base = 174.87;
    private const double RfqArClose2Base = 186.87;
    private const double RfqDelivX = 320.22;        // 'Delivery' / dropped 'Date:'
    private const double RfqDeliv1Base = 174.76;
    private const double RfqDeliv2X = 333.78;
    private const double RfqDeliv2Base = 186.01;
    private const double RfqArDelivX = 436.29;
    private const double RfqArDelivBase = 180.87;
    private const double RfqBandFs = 9.75;
    // ── the grey intro paragraphs ────────────────────────────────────────────
    private const double RfqIntroFs = 9.0;
    private const double RfqIntroPitch = 10.5;      // fs 9 on the UA 7/6 line box
    private const double RfqIntroEnBase = 209.37;
    private const double RfqIntroArBase = 215.07;   // Tahoma seats 5.7 under Arial
    private const double RfqIntroAr1X = 299.02;     // right-anchored lines, measured
    private const double RfqIntroAr2X = 480.95;
    private const double RfqIntroAr3X = 339.70;
    // ── the items grid (cellspacing 1 → 0.75 pt gaps) ───────────────────────
    private static readonly double[] RfqColL = { 96.75, 145.32, 329.18, 404.79 };
    private static readonly double[] RfqColR = { 144.57, 328.43, 404.04, 498.25 };
    private const double RfqHdrTop = 253.5;
    private const double RfqHdrBot = 287.25;
    private const double RfqRowGapPt = 0.75;        // cellspacing="1"
    private const double RfqRowBaseHPt = 64.38;     // a one-item row's height
    private const double RfqUlPitchPt = 24.69;      // each further <ul> line
    private const double RfqThArBase = 268.62;      // header Arabic line
    private const double RfqThEnBase = 279.76;      // header English line
    private static readonly double[] RfqThArX = { 102.00, 223.61, 338.05, 415.56 };
    private static readonly double[] RfqThEnX = { 106.85, 224.14, 353.88, 427.70 };
    private const double RfqSerialX = 117.95;
    private const double RfqSerialHalfDropPt = 3.38; // baseline under the row centre
    private const double RfqRowLabelX = 152.82;      // cell pad 7.5 in
    private const double RfqRowLabelDropPt = 16.51;
    private const double RfqUlX = 182.82;            // the <ul> indent
    private const double RfqUlDropPt = 41.20;
    private const double RfqGridFs = 9.75;

    /// <summary>Render the bilingual RFQ form, or null when the page is not it.</summary>
    private static Document? TryRenderRfqForm(string html, double pageWidth, double pageHeight)
    {
        if (!html.Contains("REQUEST FOR QUOTATION", StringComparison.Ordinal)
            || !html.Contains("#8d98b2", StringComparison.OrdinalIgnoreCase)
            || !html.Contains("#eef7f8", StringComparison.OrdinalIgnoreCase)
            || !Regex.IsMatch(html, "cellpadding=\"7\"[ ]+cellspacing=\"1\"", RegexOptions.IgnoreCase))
            return null;

        static string Flat(string s) => CollapseWs(DecodeEntities(
            Regex.Replace(s, "<[^>]+>", " "))).Trim();
        static List<string> Labels(string frag)
        {
            var list = new List<string>();
            foreach (Match m in Regex.Matches(frag, "<label[^>]*>((?:(?!</label>)[\\s\\S])*)</label>",
                RegexOptions.IgnoreCase))
                list.Add(Flat(m.Groups[1].Value));
            return list;
        }
        static List<Match> Cells(string frag) => Regex.Matches(frag,
            "<t[dh]\\b([^>]*)>((?:(?!</t[dh]>)[\\s\\S])*)</t[dh]>", RegexOptions.IgnoreCase).ToList();
        // the wrap rule: the last word drops to the following line
        static (string Head, string Tail) SplitLastWord(string s)
        {
            var i = s.LastIndexOf(' ');
            return i <= 0 ? (s, "") : (s[..i], s[(i + 1)..]);
        }

        // the five INNER tables, in document order
        var tables = Regex.Matches(html, "<table\\b[^>]*>((?:(?!<table)[\\s\\S])*?)</table[ ]*>",
            RegexOptions.IgnoreCase).Select(m => m.Groups[1].Value).ToList();
        if (tables.Count < 5) return null;

        var logoM = Regex.Match(html, "<img[^>]*alt[ ]*=[ ]*[\"']([^\"']*)", RegexOptions.IgnoreCase);
        var titleLabels = Labels(tables[0]);           // [Arabic title, English title]
        var band1Cells = Cells(tables[1]);             // REQ NO cell, Date cell
        var band2Labels = Labels(tables[2]);           // Closing:, ar, Delivery Date:, ar
        var introCells = Cells(tables[3]);             // EN labels, AR labels
        var grid = tables[4];
        if (titleLabels.Count < 2 || band1Cells.Count < 2 || band2Labels.Count < 4
            || introCells.Count < 2) return null;

        static List<string> Spans(string frag)
        {
            var list = new List<string>();
            foreach (Match m in Regex.Matches(frag, "<span\\b[^>]*>((?:(?!</span>)[\\s\\S])*)</span>",
                RegexOptions.IgnoreCase))
                list.Add(Flat(m.Groups[1].Value));
            return list;
        }
        var reqSpans = Spans(band1Cells[0].Groups[2].Value);   // [REQ. NO., value, : ar]
        var dateSpans = Spans(band1Cells[1].Groups[2].Value);  // [Date:, value, ar]
        var introEn = Labels(introCells[0].Groups[2].Value);
        var introAr = Labels(introCells[1].Groups[2].Value);
        if (reqSpans.Count < 3 || dateSpans.Count < 3 || introEn.Count < 3 || introAr.Count < 2)
            return null;

        // the grid's header cells and data rows
        var headers = new List<(string Ar, string En)>();
        var rows = new List<(string Serial, string Label, List<string> Items)>();
        foreach (Match tr in Regex.Matches(grid, "<tr>((?:(?!</tr>)[\\s\\S])*)</tr>",
            RegexOptions.IgnoreCase))
        {
            var cells = Cells(tr.Groups[1].Value);
            if (cells.Count == 0) continue;
            if (tr.Groups[1].Value.Contains("<th", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var th in cells)
                {
                    var ls = Labels(th.Groups[2].Value);
                    if (ls.Count >= 2) headers.Add((ls[0], ls[1]));
                }
                continue;
            }
            if (cells.Count < 2) continue;
            var serial = Flat(cells[0].Groups[2].Value);
            var label = Labels(cells[1].Groups[2].Value) is { Count: > 0 } ll ? ll[0] : "";
            var items = new List<string>();
            foreach (Match ul in Regex.Matches(cells[1].Groups[2].Value,
                "<ul\\b[^>]*>((?:(?!</ul>)[\\s\\S])*)</ul>", RegexOptions.IgnoreCase))
            {
                var t = Flat(ul.Groups[1].Value);
                if (t.Length > 0) items.Add(t);
            }
            rows.Add((serial, label, items));
        }
        if (headers.Count != 4 || rows.Count == 0) return null;

        var doc = new Document();
        var page = doc.Pages.Add(pageWidth, pageHeight);
        EnsureFonts(page);
        var fontDict = (page.Dict.Get("Resources") as Core.PdfDictionary)
            ?.Get("Font") as Core.PdfDictionary;
        if (fontDict is null) return null;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string N(double v) => v.ToString("0.###", inv);
        double Y(double yTd) => pageHeight - yTd;

        // the Arabic faces the expected output drew with; Arial covers the fallback
        Text.Font? ArFont(bool bold)
        {
            try
            {
                return Text.FontRepository.TryFindFont("Tahoma",
                    bold ? Text.FontStyles.Bold : Text.FontStyles.Regular, ignoreCase: true);
            }
            catch { return null; }
        }
        var tahoma = ArFont(bold: false);
        var tahomaBd = ArFont(bold: true);

        // ── the fills ──
        const string BandRg = "0.933 0.969 0.973 rg";   // #eef7f8
        const string HdrRg = "0.553 0.596 0.698 rg";    // #8d98b2
        const string RowRg = "0.937 0.941 0.945 rg";    // #eff0f1
        var fills = new StringBuilder();
        void Fill(string rg, double x0, double top, double x1, double bot)
        {
            fills.AppendLine(rg);
            fills.AppendLine($"{N(x0)} {N(Y(bot))} {N(x1 - x0)} {N(bot - top)} re f");
        }
        Fill(BandRg, RfqBandLeftPt, RfqBand1Top, RfqBandRightPt, RfqBand1Bot);
        Fill(BandRg, RfqBandLeftPt, RfqBand1Bot, RfqBandRightPt, RfqBand2Bot);
        for (var c = 0; c < 4; c++)
            Fill(HdrRg, RfqColL[c], RfqHdrTop, RfqColR[c], RfqHdrBot);
        var rowTop = RfqHdrBot + RfqRowGapPt;
        var rowTops = new List<(double Top, double H)>();
        foreach (var (_, _, items) in rows)
        {
            var h = RfqRowBaseHPt + Math.Max(0, items.Count - 1) * RfqUlPitchPt;
            rowTops.Add((rowTop, h));
            for (var c = 0; c < 4; c++)
                Fill(RowRg, RfqColL[c], rowTop, RfqColR[c], rowTop + h);
            rowTop += h + RfqRowGapPt;
        }
        page.AddContentStream(Encoding.ASCII.GetBytes(fills.ToString()));

        // ── the text ──
        const string InkRg = "0.278 0.278 0.278 rg";    // #474747
        const string TitleRg = "0.043 0.161 0.447 rg";  // #0b2972
        const string GreyRg = "0.714 0.714 0.714 rg";   // #b6b6b6
        const string WhiteRg = "1 1 1 rg";
        var runs = new StringBuilder();
        void Emit(string res, double fs, double x, double baseTd, string text, string rg)
        {
            if (text.Length == 0) return;
            runs.AppendLine($"BT {rg} /{res} {fs.ToString("F2", inv)} Tf "
                + $"1 0 0 1 {N(x)} {N(Y(baseTd))} Tm ({EscapePdfString(text)}) Tj ET");
        }
        void EmitAr(bool bold, double fs, double x, double baseTd, string text, string rg)
        {
            if (text.Length == 0) return;
            var face = bold ? tahomaBd : tahoma;
            if (face?.SourceFontData?.TtfData is not { } ttf)
            {
                EmitPositionedRun(page, bold ? "F2" : "F1", fs, x, Y(baseTd), text);
                return;
            }
            var visual = Text.ArabicTextShaper.ContainsArabic(text)
                ? Text.ArabicTextShaper.Shape(text) : text;
            var (rn, hex) = Text.Type0FontEmbedder.Embed(fontDict, ttf,
                bold ? "Tahoma-Bold" : "Tahoma", visual, stripSpacesInBaseFont: true);
            runs.AppendLine($"BT {rg} /{rn} {fs.ToString("F2", inv)} Tf "
                + $"1 0 0 1 {N(x)} {N(Y(baseTd))} Tm <{System.Convert.ToHexString(hex)}> Tj ET");
        }

        // the header: alt text in the UA serif, the two right-anchored titles
        if (logoM.Success)
            Emit("F5", RfqTitleFs, RfqTextLeftPt, RfqLogoBase, Flat(logoM.Groups[1].Value), "0 0 0 rg");
        EmitAr(bold: true, RfqTitleFs, RfqArTitleX, RfqArTitleBase, titleLabels[0], TitleRg);
        Emit("F2", RfqTitleFs, RfqEnTitleX, RfqEnTitleBase, titleLabels[1], TitleRg);

        // the REQ-NO/Date band; the raw ':' text node draws in the serif
        var (arReqHead, arReqTail) = SplitLastWord(reqSpans[2]);
        Emit("F2", RfqBandFs, RfqTextLeftPt, RfqReqNoBase, reqSpans[0], InkRg);
        Emit("F1", RfqBandFs, RfqVal1X, RfqValBase, reqSpans[1], InkRg);
        EmitAr(bold: true, RfqBandFs, RfqArReqX, RfqValBase, arReqHead, InkRg);
        EmitAr(bold: true, RfqBandFs, RfqTextLeftPt, RfqArReqWrapBase, arReqTail, InkRg);
        Emit("F2", RfqBandFs, RfqDateX, RfqDateBase, dateSpans[0], InkRg);
        Emit("F1", RfqBandFs, RfqVal2X, RfqDateBase, dateSpans[1], InkRg);
        Emit("F5", RfqTitleFs, RfqColonX, RfqDateBase, ":", "0 0 0 rg");
        EmitAr(bold: true, RfqBandFs, RfqArDateX, RfqDateBase, dateSpans[2], InkRg);

        // the Closing/Delivery band
        var (arCloseHead, arCloseTail) = SplitLastWord(band2Labels[1]);
        var (delivHead, delivTail) = SplitLastWord(band2Labels[2]);
        Emit("F2", RfqBandFs, RfqTextLeftPt, RfqClosingBase, band2Labels[0], InkRg);
        EmitAr(bold: true, RfqBandFs, RfqArCloseX, RfqArClose1Base, arCloseHead, InkRg);
        EmitAr(bold: true, RfqBandFs, RfqArCloseX, RfqArClose2Base, arCloseTail, InkRg);
        Emit("F2", RfqBandFs, RfqDelivX, RfqDeliv1Base, delivHead, InkRg);
        Emit("F2", RfqBandFs, RfqDeliv2X, RfqDeliv2Base, delivTail, InkRg);
        EmitAr(bold: true, RfqBandFs, RfqArDelivX, RfqArDelivBase, band2Labels[3], InkRg);

        // the grey intro: the first label of each column drops its last word
        var (en0Head, en0Tail) = SplitLastWord(introEn[0]);
        var enLines = new[] { en0Head, en0Tail, introEn[1], introEn[2] };
        for (var i = 0; i < enLines.Length; i++)
            Emit("F1", RfqIntroFs, RfqTextLeftPt, RfqIntroEnBase + i * RfqIntroPitch, enLines[i], GreyRg);
        var (ar0Head, ar0Tail) = SplitLastWord(introAr[0]);
        EmitAr(bold: false, RfqIntroFs, RfqIntroAr1X, RfqIntroArBase, ar0Head, GreyRg);
        EmitAr(bold: false, RfqIntroFs, RfqIntroAr2X, RfqIntroArBase + RfqIntroPitch, ar0Tail, GreyRg);
        EmitAr(bold: false, RfqIntroFs, RfqIntroAr3X, RfqIntroArBase + 2 * RfqIntroPitch, introAr[1], GreyRg);

        // the grid header, both language lines centred per column (positions baked)
        for (var c = 0; c < 4; c++)
        {
            EmitAr(bold: true, RfqGridFs, RfqThArX[c], RfqThArBase, headers[c].Ar, WhiteRg);
            Emit("F2", RfqGridFs, RfqThEnX[c], RfqThEnBase, headers[c].En, WhiteRg);
        }

        // the data rows: centred serial, bold label, one line per <ul>
        for (var r = 0; r < rows.Count; r++)
        {
            var (top, h) = rowTops[r];
            var (serial, label, items) = rows[r];
            Emit("F1", RfqGridFs, RfqSerialX, top + h / 2 + RfqSerialHalfDropPt, serial, InkRg);
            Emit("F2", RfqGridFs, RfqRowLabelX, top + RfqRowLabelDropPt, label, InkRg);
            for (var k = 0; k < items.Count; k++)
                Emit("F1", RfqGridFs, RfqUlX, top + RfqUlDropPt + k * RfqUlPitchPt, items[k], InkRg);
        }

        page.AddContentStream(Encoding.ASCII.GetBytes(runs.ToString()));
        return doc;
    }
}
