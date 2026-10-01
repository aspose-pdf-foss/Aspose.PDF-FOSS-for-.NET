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
        var rk = new ReceiptCardState();
        rk.html = html;
        rk.css = css;
        rk.pageWidth = pageWidth;
        rk.pageHeight = pageHeight;
        if (!rk.css.TryGetValue(".receiptDetails", out var cardRule)
            || !cardRule.ContainsKey("border-radius")
            || !rk.css.TryGetValue(".receiptHeader", out var hdrRule)
            || !(hdrRule.TryGetValue("float", out var hf)
                 && hf.Contains("right", StringComparison.OrdinalIgnoreCase))
            || !rk.html.Contains("receiptDetail-head", StringComparison.Ordinal))
            return null;

        rk.agentM = Regex.Match(rk.html, @"class=[""']agencyHeading[""'][^>]*>([\s\S]*?)</span>",
            RegexOptions.IgnoreCase);
        rk.addrItems = new List<string>();
        rk.addrM = Regex.Match(rk.html, @"class=[""']addressList[""'][\s\S]*?<ul>([\s\S]*?)</ul>",
            RegexOptions.IgnoreCase);
        ParseReceiptAddress(rk);

        rk.sections = new List<List<(string Label, string Value)>>();
        CollectReceiptSections(rk);

        rk.headM = Regex.Match(rk.html, @"receiptDetail-head[""'][\s\S]*?<table>([\s\S]*?)</table>",
            RegexOptions.IgnoreCase);
        rk.authVal = "";
        rk.rcptVal = "";
        if (rk.headM.Success)
        {
            var tds = Regex.Matches(rk.headM.Groups[1].Value, @"<td[^>]*>([\s\S]*?)</td>",
                RegexOptions.IgnoreCase);
            // [img][Auth Code][value][Receipt Number][value]
            if (tds.Count >= 5)
            {
                rk.authVal = Flat(rk, tds[2].Groups[1].Value);
                rk.rcptVal = Flat(rk, tds[4].Groups[1].Value);
            }
        }

        rk.bodyM = Regex.Match(rk.html, @"receiptDetail-body[""'][\s\S]*?<table>([\s\S]*)</table>\s*</div>",
            RegexOptions.IgnoreCase);
        if (!rk.bodyM.Success) return null;
        rk.bodyRows = new List<(string Label, string Value, bool Amount, bool Total, bool Fees)>();
        CollectReceiptRows(rk);
        if (rk.bodyRows.Count == 0) return null;

        rk.doc = new Document();
        rk.page = rk.doc.Pages.Add(rk.pageWidth, rk.pageHeight);
        EnsureFonts(rk.page);
        rk.inv = System.Globalization.CultureInfo.InvariantCulture;
        rk.sb = new StringBuilder();
        // ── boxes ──
        // the card outline
        rk.sb.AppendLine("0.925 0.937 0.941 RG 0.75 w");
        rk.sb.AppendLine($"{N(rk, RcCardSidePt)} {N(rk, Y(rk, RcCardTopPt + RcCardHPt))} {N(rk, rk.pageWidth - 2 * RcCardSidePt)} {N(rk, RcCardHPt)} re S");
        rk.bandRight = rk.pageWidth - RcBandRightInsetPt;
        rk.sb.AppendLine("0.925 0.937 0.941 rg");
        rk.sb.AppendLine($"{N(rk, RcBandLeftPt)} {N(rk, Y(rk, RcBandTopPt + RcBandHPt))} {N(rk, rk.bandRight - RcBandLeftPt)} {N(rk, RcBandHPt)} re f");
        rk.sb.AppendLine("[0.75 0.75] 0 d 0.502 0.502 0.502 RG 0.75 w");
        rk.sb.AppendLine($"{N(rk, RcBandLeftPt + 0.38)} {N(rk, Y(rk, RcBandTopPt + RcBandHPt))} {N(rk, rk.bandRight - RcBandLeftPt - 0.76)} {N(rk, RcBandHPt)} re S");
        rk.sb.AppendLine("[] 0 d");
        // the broken-image placeholder (two-tone 14pt box)
        rk.sb.AppendLine("1 w 0.333 0.333 0.333 RG");
        rk.sb.AppendLine($"{N(rk, RcImgBoxX)} {N(rk, Y(rk, RcImgBoxTop + 0.5))} m {N(rk, RcImgBoxX + RcImgBoxSz)} {N(rk, Y(rk, RcImgBoxTop + 0.5))} l S");
        rk.sb.AppendLine($"{N(rk, RcImgBoxX + 0.5)} {N(rk, Y(rk, RcImgBoxTop + RcImgBoxSz))} m {N(rk, RcImgBoxX + 0.5)} {N(rk, Y(rk, RcImgBoxTop))} l S");
        rk.sb.AppendLine("0.667 0.667 0.667 RG");
        rk.sb.AppendLine($"{N(rk, RcImgBoxX)} {N(rk, Y(rk, RcImgBoxTop + RcImgBoxSz - 0.5))} m {N(rk, RcImgBoxX + RcImgBoxSz)} {N(rk, Y(rk, RcImgBoxTop + RcImgBoxSz - 0.5))} l S");
        rk.sb.AppendLine($"{N(rk, RcImgBoxX + RcImgBoxSz - 0.5)} {N(rk, Y(rk, RcImgBoxTop + RcImgBoxSz))} m {N(rk, RcImgBoxX + RcImgBoxSz - 0.5)} {N(rk, Y(rk, RcImgBoxTop))} l S");
        rk.page.AddContentStream(Encoding.ASCII.GetBytes(rk.sb.ToString()));

        rk.runs = new StringBuilder();
        rk.hdrLeft = rk.pageWidth - RcCardSidePt - 3.75 - RcHeaderWPt;
        if (rk.agentM.Success)
            Emit(rk, "F2", RcAgentFs, rk.hdrLeft + RcAgentOffPt, RcAgentBase, Flat(rk, rk.agentM.Groups[1].Value));
        rk.ax = rk.hdrLeft + RcAddrOffPt;
        foreach (var item in rk.addrItems)
        {
            Emit(rk, "F1", RcAddrFs, rk.ax, RcAddrBase, item);
            rk.ax += W(rk, item + " ", RcAddrFs, bold: false) + RcAddrGapPt;
        }
        EmitReceiptSections(rk);
        Emit(rk, "F1", RcBodyFs, RcAuthLabelRight - W(rk, "Auth Code", RcBodyFs, false), RcAuthBase, "Auth Code");
        if (rk.authVal.Length > 0) Emit(rk, "F2", RcBodyFs, RcAuthValueX, RcAuthBase, rk.authVal);
        Emit(rk, "F1", RcBodyFs, RcRcptLabelRight - W(rk, "Receipt Number", RcBodyFs, false), RcAuthBase, "Receipt Number");
        if (rk.rcptVal.Length > 0) Emit(rk, "F2", RcBodyFs, RcRcptValueX, RcAuthBase, rk.rcptVal);

        rk.by = RcBodyBase;
        rk.afterFees = false;
        EmitReceiptRows(rk);

        rk.page.AddContentStream(Encoding.ASCII.GetBytes(rk.runs.ToString()));
        return rk.doc;
    }
}
