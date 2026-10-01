using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Rfq form: the sheet's text - titles, bands, headers and data rows.</summary>
    private static void EmitRfqText(RfqFormState rq)
    {
        // ── the text ──
        const string InkRg = "0.278 0.278 0.278 rg";    // #474747
        const string TitleRg = "0.043 0.161 0.447 rg";  // #0b2972
        const string GreyRg = "0.714 0.714 0.714 rg";   // #b6b6b6
        const string WhiteRg = "1 1 1 rg";
        rq.runs = new StringBuilder();
        // the header: alt text in the UA serif, the two right-anchored titles
        if (rq.logoM.Success)
            Emit(rq, "F5", RfqTitleFs, RfqTextLeftPt, RfqLogoBase, RqFlat(rq.logoM.Groups[1].Value), "0 0 0 rg");
        EmitAr(rq, bold: true, RfqTitleFs, RfqArTitleX, RfqArTitleBase, rq.titleLabels[0], TitleRg);
        Emit(rq, "F2", RfqTitleFs, RfqEnTitleX, RfqEnTitleBase, rq.titleLabels[1], TitleRg);

        // the REQ-NO/Date band; the raw ':' text node draws in the serif
        var (arReqHead, arReqTail) = RqSplitLastWord(rq.reqSpans[2]);
        Emit(rq, "F2", RfqBandFs, RfqTextLeftPt, RfqReqNoBase, rq.reqSpans[0], InkRg);
        Emit(rq, "F1", RfqBandFs, RfqVal1X, RfqValBase, rq.reqSpans[1], InkRg);
        EmitAr(rq, bold: true, RfqBandFs, RfqArReqX, RfqValBase, arReqHead, InkRg);
        EmitAr(rq, bold: true, RfqBandFs, RfqTextLeftPt, RfqArReqWrapBase, arReqTail, InkRg);
        Emit(rq, "F2", RfqBandFs, RfqDateX, RfqDateBase, rq.dateSpans[0], InkRg);
        Emit(rq, "F1", RfqBandFs, RfqVal2X, RfqDateBase, rq.dateSpans[1], InkRg);
        Emit(rq, "F5", RfqTitleFs, RfqColonX, RfqDateBase, ":", "0 0 0 rg");
        EmitAr(rq, bold: true, RfqBandFs, RfqArDateX, RfqDateBase, rq.dateSpans[2], InkRg);

        // the Closing/Delivery band
        var (arCloseHead, arCloseTail) = RqSplitLastWord(rq.band2Labels[1]);
        var (delivHead, delivTail) = RqSplitLastWord(rq.band2Labels[2]);
        Emit(rq, "F2", RfqBandFs, RfqTextLeftPt, RfqClosingBase, rq.band2Labels[0], InkRg);
        EmitAr(rq, bold: true, RfqBandFs, RfqArCloseX, RfqArClose1Base, arCloseHead, InkRg);
        EmitAr(rq, bold: true, RfqBandFs, RfqArCloseX, RfqArClose2Base, arCloseTail, InkRg);
        Emit(rq, "F2", RfqBandFs, RfqDelivX, RfqDeliv1Base, delivHead, InkRg);
        Emit(rq, "F2", RfqBandFs, RfqDeliv2X, RfqDeliv2Base, delivTail, InkRg);
        EmitAr(rq, bold: true, RfqBandFs, RfqArDelivX, RfqArDelivBase, rq.band2Labels[3], InkRg);

        // the grey intro: the first label of each column drops its last word
        var (en0Head, en0Tail) = RqSplitLastWord(rq.introEn[0]);
        rq.enLines = new[] { en0Head, en0Tail, rq.introEn[1], rq.introEn[2] };
        for (var i = 0; i < rq.enLines.Length; i++)
            Emit(rq, "F1", RfqIntroFs, RfqTextLeftPt, RfqIntroEnBase + i * RfqIntroPitch, rq.enLines[i], GreyRg);
        var (ar0Head, ar0Tail) = RqSplitLastWord(rq.introAr[0]);
        EmitAr(rq, bold: false, RfqIntroFs, RfqIntroAr1X, RfqIntroArBase, ar0Head, GreyRg);
        EmitAr(rq, bold: false, RfqIntroFs, RfqIntroAr2X, RfqIntroArBase + RfqIntroPitch, ar0Tail, GreyRg);
        EmitAr(rq, bold: false, RfqIntroFs, RfqIntroAr3X, RfqIntroArBase + 2 * RfqIntroPitch, rq.introAr[1], GreyRg);

        // the grid header, both language lines centred per column (positions baked)
        for (var c = 0; c < 4; c++)
        {
            EmitAr(rq, bold: true, RfqGridFs, RfqThArX[c], RfqThArBase, rq.headers[c].Ar, WhiteRg);
            Emit(rq, "F2", RfqGridFs, RfqThEnX[c], RfqThEnBase, rq.headers[c].En, WhiteRg);
        }

        // the data rows: centred serial, bold label, one line per <ul>
        for (var r = 0; r < rq.rows.Count; r++)
        {
            var (top, h) = rq.rowTops[r];
            var (serial, label, items) = rq.rows[r];
            Emit(rq, "F1", RfqGridFs, RfqSerialX, top + h / 2 + RfqSerialHalfDropPt, serial, InkRg);
            Emit(rq, "F2", RfqGridFs, RfqRowLabelX, top + RfqRowLabelDropPt, label, InkRg);
            for (var k = 0; k < items.Count; k++)
                Emit(rq, "F1", RfqGridFs, RfqUlX, top + RfqUlDropPt + k * RfqUlPitchPt, items[k], InkRg);
        }

    }

    /// <summary>Rfq form: the coloured bands and grid cells filled.</summary>
    private static void FillRfqBands(RfqFormState rq)
    {
        rq.inv = System.Globalization.CultureInfo.InvariantCulture;
        rq.tahoma = ArFont(bold: false);
        rq.tahomaBd = ArFont(bold: true);

        // ── the fills ──
        const string BandRg = "0.933 0.969 0.973 rg";   // #eef7f8
        const string HdrRg = "0.553 0.596 0.698 rg";    // #8d98b2
        const string RowRg = "0.937 0.941 0.945 rg";    // #eff0f1
        rq.fills = new StringBuilder();
        Fill(rq, BandRg, RfqBandLeftPt, RfqBand1Top, RfqBandRightPt, RfqBand1Bot);
        Fill(rq, BandRg, RfqBandLeftPt, RfqBand1Bot, RfqBandRightPt, RfqBand2Bot);
        for (var c = 0; c < 4; c++)
            Fill(rq, HdrRg, RfqColL[c], RfqHdrTop, RfqColR[c], RfqHdrBot);
        rq.rowTop = RfqHdrBot + RfqRowGapPt;
        rq.rowTops = new List<(double Top, double H)>();
        foreach (var (_, _, items) in rq.rows)
        {
            var h = RfqRowBaseHPt + Math.Max(0, items.Count - 1) * RfqUlPitchPt;
            rq.rowTops.Add((rq.rowTop, h));
            for (var c = 0; c < 4; c++)
                Fill(rq, RowRg, RfqColL[c], rq.rowTop, RfqColR[c], rq.rowTop + h);
            rq.rowTop += h + RfqRowGapPt;
        }
        rq.page.AddContentStream(Encoding.ASCII.GetBytes(rq.fills.ToString()));
    }

    /// <summary>Rfq form: the grid's headers and data rows read off the markup.</summary>
    private static void ParseRfqGrid(RfqFormState rq)
    {
        rq.headers = new List<(string Ar, string En)>();
        rq.rows = new List<(string Serial, string Label, List<string> Items)>();
        foreach (Match tr in Regex.Matches(rq.grid, "<tr>((?:(?!</tr>)[\\s\\S])*)</tr>",
            RegexOptions.IgnoreCase))
        {
            var cells = RqCells(tr.Groups[1].Value);
            if (cells.Count == 0) continue;
            if (tr.Groups[1].Value.Contains("<th", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var th in cells)
                {
                    var ls = RqLabels(th.Groups[2].Value);
                    if (ls.Count >= 2) rq.headers.Add((ls[0], ls[1]));
                }
                continue;
            }
            if (cells.Count < 2) continue;
            var serial = RqFlat(cells[0].Groups[2].Value);
            var label = RqLabels(cells[1].Groups[2].Value) is { Count: > 0 } ll ? ll[0] : "";
            var items = new List<string>();
            foreach (Match ul in Regex.Matches(cells[1].Groups[2].Value,
                "<ul\\b[^>]*>((?:(?!</ul>)[\\s\\S])*)</ul>", RegexOptions.IgnoreCase))
            {
                var t = RqFlat(ul.Groups[1].Value);
                if (t.Length > 0) items.Add(t);
            }
            rq.rows.Add((serial, label, items));
        }
    }
}
