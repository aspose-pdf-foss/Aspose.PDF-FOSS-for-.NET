using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// The payment-receipt card export: a bordered rounded .receiptDetails card
// holding a float-right header (agency heading, inline-li address, two
// inline-block summary sections), then a .receiptTransactions block indented
// 185px holding the grey dotted auth band and the label/value details table.
// The whole sheet draws in Arial at #666 on the caller's page
// box; every position below is measured on the expected render.
internal static partial class HtmlToPdfConverter
{
    // ── the card (.receiptDetails: border 1px #eceff0, height 160px,
    //    padding 5px 5px 15px 5px) ──────────────────────────────────────────
    private const double RcCardTopPt = 78.38;      // margin 72 + UA 6 + half border
    private const double RcCardSidePt = 96.38;     // margin 96 + half border
    private const double RcCardHPt = 135.75;       // (160px + 5 + 15 padding) × 0.75
    // ── the float-right header (730px) and its content ─────────────────────
    private const double RcHeaderWPt = 547.5;      // width: 730px
    private const double RcAgentBase = 106.87;     // agencyHeading baseline
    private const double RcAgentFs = 14.04;        // 1.3em of the header's 1.2em
    private const double RcAgentOffPt = 7.13;      // margin-left: 10px, inside the header
    private const double RcAddrBase = 132.45;      // the inline-li address row
    private const double RcAddrFs = 9.0;           // .addressList font-size: 12px
    private const double RcAddrOffPt = 37.13;      // list indent inside the header
    private const double RcAddrGapPt = 7.5;        // li margin-left: 10px
    // ── the two inline-block summary sections (340px each) ─────────────────
    private const double RcSumBase = 164.98;       // section 1 first row baseline
    private const double RcSumLift = 4.16;         // section 2 sits this much higher
    private const double RcSumPitch = 22.5;        // 30px row pitch
    private const double RcSumFs = 12.0;
    private const double RcS1LabelOff = 19.5;      // off the header left (margin 20px + pad)
    private const double RcS1ValueOff = 125.22;    // value column off the header left
    private const double RcS2LabelOff = 277.5;     // section 2 label column
    private const double RcS2ValueOff = 358.52;    // section 2 value column
    // ── the auth band (.receiptDetail-head: #eceff0 fill, dotted border) ───
    private const double RcBandLeftPt = 235.5;     // margin 96 + margin-left 185px + half
    private const double RcBandTopPt = 230.25;
    private const double RcBandHPt = 30.5;
    private const double RcBandRightInsetPt = 96.75;
    private const double RcAuthBase = 249.41;
    private const double RcAuthLabelRight = 359.83;   // 'Auth Code' right-aligns here
    private const double RcAuthValueX = 365.84;
    private const double RcRcptLabelRight = 712.48;   // 'Receipt Number' right edge
    private const double RcRcptValueX = 718.5;
    // the broken-image placeholder the unreachable success.png leaves
    private const double RcImgBoxX = 243.75;
    private const double RcImgBoxTop = 238.5;
    private const double RcImgBoxSz = 14.0;
    // ── the details body ────────────────────────────────────────────────────
    private const double RcBodyLabelX = 243.0;
    private const double RcBodyValueX = 421.0;
    private const double RcFeesValueX = 421.75;
    private const double RcAmountRight = 588.48;   // detailValueAmt right edge
    private const double RcFeesAmtRight = 587.7;   // the nested fees grid's right edge
    private const double RcTotalAmtRight = 589.99; // totalValue keeps no right padding
    private const double RcBodyBase = 279.16;      // Passenger baseline
    private const double RcBodyPitch = 19.5;
    private const double RcFeesDrop = 2.25;        // the nested fees line under its label
    private const double RcPostFeesPitch = 21.75;  // fees line → first amount row
    private const double RcTotalPitch = 21.83;     // last amount row → Total
    private const double RcBodyFs = 12.0;
    private const double RcTotalFs = 14.4;         // totalLabel: 1.2em

    /// <summary>Render the receipt-card export, or null when the page is not it.</summary>
    private static Document? TryRenderReceiptCard(string html, IReadOnlyDictionary<string,
        Dictionary<string, string>> css, double pageWidth, double pageHeight)
    {
        if (!css.TryGetValue(".receiptDetails", out var cardRule)
            || !cardRule.ContainsKey("border-radius")
            || !css.TryGetValue(".receiptHeader", out var hdrRule)
            || !(hdrRule.TryGetValue("float", out var hf)
                 && hf.Contains("right", StringComparison.OrdinalIgnoreCase))
            || !html.Contains("receiptDetail-head", StringComparison.Ordinal))
            return null;

        static string Flat(string s) => CollapseWs(DecodeEntities(
            Regex.Replace(s, @"<[^>]+>", " "))).Trim();

        // the agency heading and inline address items
        var agentM = Regex.Match(html, @"class=[""']agencyHeading[""'][^>]*>([\s\S]*?)</span>",
            RegexOptions.IgnoreCase);
        var addrItems = new List<string>();
        var addrM = Regex.Match(html, @"class=[""']addressList[""'][\s\S]*?<ul>([\s\S]*?)</ul>",
            RegexOptions.IgnoreCase);
        if (addrM.Success)
            foreach (Match li in Regex.Matches(addrM.Groups[1].Value, @"<li[^>]*>([\s\S]*?)</li>",
                RegexOptions.IgnoreCase))
            {
                var t = Flat(li.Groups[1].Value);
                if (t.Length > 0) addrItems.Add(t);
            }

        // the two summary sections' label/value rows
        var sections = new List<List<(string Label, string Value)>>();
        foreach (Match sm in Regex.Matches(html,
            @"class=[""']summarySection[""'][\s\S]*?<table>([\s\S]*?)</table>", RegexOptions.IgnoreCase))
        {
            var rows = new List<(string, string)>();
            foreach (Match tr in Regex.Matches(sm.Groups[1].Value, @"<tr>([\s\S]*?)</tr>",
                RegexOptions.IgnoreCase))
            {
                var tds = Regex.Matches(tr.Groups[1].Value, @"<td[^>]*>([\s\S]*?)</td>",
                    RegexOptions.IgnoreCase);
                if (tds.Count >= 2)
                    rows.Add((Flat(tds[0].Groups[1].Value), Flat(tds[1].Groups[1].Value)));
            }
            sections.Add(rows);
        }

        // the auth band's two label/value pairs
        var headM = Regex.Match(html, @"receiptDetail-head[""'][\s\S]*?<table>([\s\S]*?)</table>",
            RegexOptions.IgnoreCase);
        string authVal = "", rcptVal = "";
        if (headM.Success)
        {
            var tds = Regex.Matches(headM.Groups[1].Value, @"<td[^>]*>([\s\S]*?)</td>",
                RegexOptions.IgnoreCase);
            // [img][Auth Code][value][Receipt Number][value]
            if (tds.Count >= 5)
            {
                authVal = Flat(tds[2].Groups[1].Value);
                rcptVal = Flat(tds[4].Groups[1].Value);
            }
        }

        // the details body: label + (value | nested fees grid | right amount)
        var bodyM = Regex.Match(html, @"receiptDetail-body[""'][\s\S]*?<table>([\s\S]*)</table>\s*</div>",
            RegexOptions.IgnoreCase);
        if (!bodyM.Success) return null;
        var bodyRows = new List<(string Label, string Value, bool Amount, bool Total, bool Fees)>();
        foreach (Match tr in Regex.Matches(bodyM.Groups[1].Value,
            @"<tr>((?:(?!</tr>)[\s\S])*)</tr>", RegexOptions.IgnoreCase))
        {
            var row = tr.Groups[1].Value;
            var tds = Regex.Matches(row, @"<td\b([^>]*)>((?:(?!</td>|<td\b)[\s\S])*)</td>",
                RegexOptions.IgnoreCase);
            if (tds.Count < 2) continue;
            var label = Flat(tds[0].Groups[2].Value);
            if (label.Length == 0) continue;
            var attrs0 = tds[0].Groups[1].Value;
            var isTotal = attrs0.Contains("totalLabel", StringComparison.OrdinalIgnoreCase);
            var isFees = row.Contains("<table", StringComparison.OrdinalIgnoreCase);
            var isAmount = tds[1].Groups[1].Value.Contains("detailValueAmt", StringComparison.OrdinalIgnoreCase)
                || isTotal;
            var value = Flat(isFees
                ? Regex.Replace(tds[1].Groups[2].Value, @"</?t[a-z]+\b[^>]*>", " ")
                : tds[1].Groups[2].Value);
            bodyRows.Add((label, value, isAmount, isTotal, isFees));
        }
        if (bodyRows.Count == 0) return null;

        var doc = new Document();
        var page = doc.Pages.Add(pageWidth, pageHeight);
        EnsureFonts(page);
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        string N(double v) => v.ToString("0.###", inv);
        // page-down y → PDF y
        double Y(double yTd) => pageHeight - yTd;

        // ── boxes ──
        // the card outline
        sb.AppendLine("0.925 0.937 0.941 RG 0.75 w");
        sb.AppendLine($"{N(RcCardSidePt)} {N(Y(RcCardTopPt + RcCardHPt))} {N(pageWidth - 2 * RcCardSidePt)} {N(RcCardHPt)} re S");
        // the auth band fill + dotted border
        var bandRight = pageWidth - RcBandRightInsetPt;
        sb.AppendLine("0.925 0.937 0.941 rg");
        sb.AppendLine($"{N(RcBandLeftPt)} {N(Y(RcBandTopPt + RcBandHPt))} {N(bandRight - RcBandLeftPt)} {N(RcBandHPt)} re f");
        sb.AppendLine("[0.75 0.75] 0 d 0.502 0.502 0.502 RG 0.75 w");
        sb.AppendLine($"{N(RcBandLeftPt + 0.38)} {N(Y(RcBandTopPt + RcBandHPt))} {N(bandRight - RcBandLeftPt - 0.76)} {N(RcBandHPt)} re S");
        sb.AppendLine("[] 0 d");
        // the broken-image placeholder (two-tone 14pt box)
        sb.AppendLine("1 w 0.333 0.333 0.333 RG");
        sb.AppendLine($"{N(RcImgBoxX)} {N(Y(RcImgBoxTop + 0.5))} m {N(RcImgBoxX + RcImgBoxSz)} {N(Y(RcImgBoxTop + 0.5))} l S");
        sb.AppendLine($"{N(RcImgBoxX + 0.5)} {N(Y(RcImgBoxTop + RcImgBoxSz))} m {N(RcImgBoxX + 0.5)} {N(Y(RcImgBoxTop))} l S");
        sb.AppendLine("0.667 0.667 0.667 RG");
        sb.AppendLine($"{N(RcImgBoxX)} {N(Y(RcImgBoxTop + RcImgBoxSz - 0.5))} m {N(RcImgBoxX + RcImgBoxSz)} {N(Y(RcImgBoxTop + RcImgBoxSz - 0.5))} l S");
        sb.AppendLine($"{N(RcImgBoxX + RcImgBoxSz - 0.5)} {N(Y(RcImgBoxTop + RcImgBoxSz))} m {N(RcImgBoxX + RcImgBoxSz - 0.5)} {N(Y(RcImgBoxTop))} l S");
        page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));

        // ── text ── (everything #666)
        var runs = new StringBuilder();
        void Emit(string res, double fs, double x, double yTd, string text)
        {
            runs.AppendLine("BT 0.4 0.4 0.4 rg");
            runs.Append($"/{res} {fs.ToString("F2", inv)} Tf ");
            runs.Append($"1 0 0 1 {N(x)} {N(Y(yTd))} Tm ");
            runs.AppendLine($"({EscapePdfString(text)}) Tj ET");
        }
        double W(string t, double fs, bool bold) =>
            MeasureFaceText(bold ? "Helvetica-Bold" : "Helvetica", t, fs);

        var hdrLeft = pageWidth - RcCardSidePt - 3.75 - RcHeaderWPt;
        if (agentM.Success)
            Emit("F2", RcAgentFs, hdrLeft + RcAgentOffPt, RcAgentBase, Flat(agentM.Groups[1].Value));
        var ax = hdrLeft + RcAddrOffPt;
        foreach (var item in addrItems)
        {
            Emit("F1", RcAddrFs, ax, RcAddrBase, item);
            ax += W(item + " ", RcAddrFs, bold: false) + RcAddrGapPt;
        }
        for (var s = 0; s < sections.Count && s < 2; s++)
        {
            var labelX = hdrLeft + (s == 0 ? RcS1LabelOff : RcS2LabelOff);
            var valueX = hdrLeft + (s == 0 ? RcS1ValueOff : RcS2ValueOff);
            var y = RcSumBase - (s == 1 ? RcSumLift : 0);
            foreach (var (label, value) in sections[s])
            {
                Emit("F1", RcSumFs, labelX, y, label);
                if (value.Length > 0) Emit("F2", RcSumFs, valueX, y, value);
                y += RcSumPitch;
            }
        }
        Emit("F1", RcBodyFs, RcAuthLabelRight - W("Auth Code", RcBodyFs, false), RcAuthBase, "Auth Code");
        if (authVal.Length > 0) Emit("F2", RcBodyFs, RcAuthValueX, RcAuthBase, authVal);
        Emit("F1", RcBodyFs, RcRcptLabelRight - W("Receipt Number", RcBodyFs, false), RcAuthBase, "Receipt Number");
        if (rcptVal.Length > 0) Emit("F2", RcBodyFs, RcRcptValueX, RcAuthBase, rcptVal);

        var by = RcBodyBase;
        var afterFees = false;
        for (var i = 0; i < bodyRows.Count; i++)
        {
            var (label, value, isAmount, isTotal, isFees) = bodyRows[i];
            if (i > 0) by += isTotal ? RcTotalPitch : afterFees ? RcPostFeesPitch : RcBodyPitch;
            afterFees = false;
            var lfs = isTotal ? RcTotalFs : RcBodyFs;
            Emit(isTotal ? "F2" : "F1", lfs, RcBodyLabelX, by, label);
            if (isFees)
            {
                // the nested fees grid: name left, amount right, a touch lower
                var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var amt = parts.Length > 1 ? parts[^1] : "";
                var name = amt.Length > 0 ? value[..value.LastIndexOf(amt, StringComparison.Ordinal)].Trim() : value;
                Emit("F2", RcBodyFs, RcFeesValueX, by + RcFeesDrop, name);
                if (amt.Length > 0)
                    Emit("F2", RcBodyFs, RcFeesAmtRight - W(amt, RcBodyFs, true), by + RcFeesDrop, amt);
                afterFees = true;
            }
            else if (isAmount && value.Length > 0)
                Emit("F2", lfs,
                    (isTotal ? RcTotalAmtRight : RcAmountRight) - W(value, lfs, true), by, value);
            else if (value.Length > 0)
                Emit("F2", RcBodyFs, RcBodyValueX, by, value);
        }

        page.AddContentStream(Encoding.ASCII.GetBytes(runs.ToString()));
        return doc;
    }
}
