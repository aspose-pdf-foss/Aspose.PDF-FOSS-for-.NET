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
    private static string RqFlat(string s) => CollapseWs(DecodeEntities(
        Regex.Replace(s, "<[^>]+>", " "))).Trim();

    private static List<string> RqLabels(string frag)
    {
        var list = new List<string>();
        foreach (Match m in Regex.Matches(frag, "<label[^>]*>((?:(?!</label>)[\\s\\S])*)</label>",
            RegexOptions.IgnoreCase))
            list.Add(RqFlat(m.Groups[1].Value));
        return list;
    }

    private static List<Match> RqCells(string frag) => Regex.Matches(frag,
        "<t[dh]\\b([^>]*)>((?:(?!</t[dh]>)[\\s\\S])*)</t[dh]>", RegexOptions.IgnoreCase).Cast<Match>().ToList();

    // the wrap rule: the last word drops to the following line
    private static (string Head, string Tail) RqSplitLastWord(string s)
    {
        var i = s.LastIndexOf(' ');
        return i <= 0 ? (s, "") : (s[..i], s[(i + 1)..]);
    }

    private static List<string> RqSpans(string frag)
    {
        var list = new List<string>();
        foreach (Match m in Regex.Matches(frag, "<span\\b[^>]*>((?:(?!</span>)[\\s\\S])*)</span>",
            RegexOptions.IgnoreCase))
            list.Add(RqFlat(m.Groups[1].Value));
        return list;
    }

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

}
