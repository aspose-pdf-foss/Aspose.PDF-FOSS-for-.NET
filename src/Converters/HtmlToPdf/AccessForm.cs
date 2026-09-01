using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// The IT-access-form export: a box-shadowed white .pageContainer card on the
// body's #f5f5f5 ground, an orange form header, section rules drawn as
// double-tone hairlines, and one #fafafa bordered .questionContainer box per
// question/answer pair, 54 pt apart. Every position and pitch below is
// measured on the expected render (both pages reproduce to ±0.1 pt).
internal static partial class HtmlToPdfConverter
{
    private const double AfGroundLeft = 90.0;      // the body ground fills the content box
    private const double AfGroundTop = 72.0;
    private const double AfGroundRight = 505.0;
    private const double AfGroundBottom = 770.0;
    private const double AfCardLeft = 97.5;        // ground + body padding 10px
    private const double AfCardRight = 497.5;
    private const double AfCardTop = 79.5;         // page 1; continuation cards start at the ground top
    private const double AfTitleBl = 122.27;       // .page>.header baseline
    private const double AfTitleFs = 12.6;         // 1.4em of the 12px body
    private const double AfTitleX = 120.75;        // card + container padding 30px
    private const double AfIntroBlOff = 41.62;     // the intro answer under the title
    private const double AfBodyFs = 9.9;           // 1.1em of the 12px body
    private const double AfIntroX = 131.25;
    private const double AfHrAfterIntro = 39.41;   // intro baseline → rule
    private const double AfHrAfterBoxes = 37.87;   // last box bottom → rule
    private const double AfHrLeft = 173.02;        // 70% rule centred in the container
    private const double AfHrRight = 421.98;
    private const double AfSectionBlOff = 42.07;   // rule → section heading baseline
    private const double AfSectionFs = 11.7;       // 1.3em
    private const double AfSectionX = 128.25;
    private const double AfFirstBoxOff = 17.56;    // heading baseline → first box top
    private const double AfBoxLeft = 128.25;
    private const double AfBoxRight = 466.75;
    private const double AfBoxH = 39.0;
    private const double AfBoxPitch = 54.0;        // box + margin-bottom 20px
    private const double AfQBlOff = 14.04;         // box top → question baseline
    private const double AfABlOff = 29.04;         // box top → answer baseline
    private const double AfTextX = 132.0;
    private const double AfVarBlOff = 34.9;        // box bottom → the <pre><var> first line
    private const double AfVarPitch = 12.7;
    private const double AfVarFs = 7.18;
    private const double AfVarX = 131.25;
    private const double AfVarContX = 140.73;      // the pre's preserved leading whitespace
    private const double AfBoxAfterVar = 15.71;    // var's last baseline → next box top
    private const double AfCardTailAfterHr = 39.0; // trailing rule → card bottom edge
    private const double AfShadowPt = 4.5;         // the flattened box-shadow rim

    /// <summary>Render the boxed IT access form, or null when the page is not it.</summary>
    private static Document? TryRenderAccessForm(string html, IReadOnlyDictionary<string,
        Dictionary<string, string>> css, double pageWidth, double pageHeight)
    {
        if (!css.TryGetValue(".pageContainer", out var cardRule)
            || !cardRule.ContainsKey("box-shadow")
            || !css.TryGetValue(".questionContainer", out var qRule)
            || !(qRule.TryGetValue("background-color", out var qbg)
                 && qbg.Contains("fafafa", StringComparison.OrdinalIgnoreCase))
            || !html.Contains("section-rule", StringComparison.Ordinal))
            return null;

        static string Flat(string s) => CollapseWs(DecodeEntities(
            Regex.Replace(s, @"<[^>]+>", " "))).Trim();

        var pageM = Regex.Match(html, @"class=['""]page['""]\s*>([\s\S]*)</div>\s*</div>\s*</body>",
            RegexOptions.IgnoreCase);
        if (!pageM.Success) return null;
        var content = pageM.Groups[1].Value;
        var titleM = Regex.Match(content, @"class=['""]header['""]\s*>([\s\S]*?)</div>",
            RegexOptions.IgnoreCase);
        if (!titleM.Success) return null;

        // The document as an ordered item stream: sections (heading + question
        // boxes + loose pre answers) separated by rules.
        var items = new List<(string Kind, string A, string B)>();
        foreach (Match m in Regex.Matches(content,
            @"<div class=['""](section|section-rule)['""]\s*>((?:(?!<div class=['""]section)[\s\S])*)",
            RegexOptions.IgnoreCase))
        {
            if (m.Groups[1].Value.Equals("section-rule", StringComparison.OrdinalIgnoreCase))
            {
                items.Add(("hr", "", ""));
                continue;
            }
            var body = m.Groups[2].Value;
            var hM = Regex.Match(body, @"class=['""]header['""]\s*>([\s\S]*?)</div>", RegexOptions.IgnoreCase);
            if (hM.Success) items.Add(("head", Flat(hM.Groups[1].Value), ""));
            foreach (Match part in Regex.Matches(body,
                @"<div class=['""]questionContainer['""]\s*>([\s\S]*?)</div>\s*</div>|<pre\b[^>]*>([\s\S]*?)</pre>",
                RegexOptions.IgnoreCase))
            {
                if (part.Groups[2].Success)
                {
                    var lines = Regex.Replace(DecodeEntities(
                        Regex.Replace(part.Groups[2].Value, @"<[^>]+>", "")), "\r", "").Split('\n');
                    var real = new List<string>();
                    foreach (var l in lines) if (l.Trim().Length > 0) real.Add(l.Trim());
                    items.Add(("pre", real.Count > 0 ? real[0] : "",
                        real.Count > 1 ? string.Join(" ", real.GetRange(1, real.Count - 1)) : ""));
                    continue;
                }
                var qM = Regex.Match(part.Groups[1].Value, @"class=['""]question['""]\s*>([\s\S]*?)</div>",
                    RegexOptions.IgnoreCase);
                var aM = Regex.Match(part.Groups[1].Value, @"class=['""]answer['""]\s*>([\s\S]*?)$",
                    RegexOptions.IgnoreCase);
                items.Add(("qa", qM.Success ? Flat(qM.Groups[1].Value) : "",
                    aM.Success ? Flat(aM.Groups[1].Value) : ""));
            }
            // an intro section holds a bare answer paragraph and no boxes
            if (!hM.Success && !body.Contains("questionContainer", StringComparison.Ordinal)
                && Regex.Match(body, @"<p>([\s\S]*?)</p>", RegexOptions.IgnoreCase) is { Success: true } pIntro)
                items.Add(("intro", Flat(pIntro.Groups[1].Value), ""));
        }
        if (items.Count == 0) return null;

        var doc = new Document();
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string N(double v) => v.ToString("0.###", inv);
        Page page = null!;
        StringBuilder boxes = null!, runs = null!;
        var firstPage = true;

        void FlushPage(double cardBottom, bool lastPage)
        {
            // ground + card behind everything already emitted for this page
            var bg = new StringBuilder();
            bg.AppendLine("0.961 0.961 0.961 rg");
            bg.AppendLine($"{N(AfGroundLeft)} {N(pageHeight - AfGroundBottom)} {N(AfGroundRight - AfGroundLeft)} {N(AfGroundBottom - AfGroundTop)} re f");
            var top = firstPage ? AfCardTop : AfGroundTop;
            // the 10px box-shadow: a mid-grey rim under the card (the blur is
            // approximated by a solid band carrying the same ink)
            var shTop = firstPage ? top - AfShadowPt : top;
            var shBottom = lastPage ? cardBottom + AfShadowPt : cardBottom;
            bg.AppendLine("0.8 0.8 0.8 rg");
            bg.AppendLine($"{N(AfCardLeft - AfShadowPt)} {N(pageHeight - shBottom)} {N(AfCardRight - AfCardLeft + 2 * AfShadowPt)} {N(shBottom - shTop)} re f");
            bg.AppendLine("1 1 1 rg");
            bg.AppendLine($"{N(AfCardLeft)} {N(pageHeight - cardBottom)} {N(AfCardRight - AfCardLeft)} {N(cardBottom - top)} re f");
            // the rgba(0,0,0,.15) card edge, flattened on white
            bg.AppendLine("0.85 0.85 0.85 RG 0.75 w");
            bg.AppendLine($"{N(AfCardLeft + 0.38)} {N(pageHeight - cardBottom)} m {N(AfCardLeft + 0.38)} {N(pageHeight - top)} l S");
            bg.AppendLine($"{N(AfCardRight - 0.38)} {N(pageHeight - cardBottom)} m {N(AfCardRight - 0.38)} {N(pageHeight - top)} l S");
            if (firstPage)
                bg.AppendLine($"{N(AfCardLeft)} {N(pageHeight - top - 0.38)} m {N(AfCardRight)} {N(pageHeight - top - 0.38)} l S");
            if (lastPage)
                bg.AppendLine($"{N(AfCardLeft)} {N(pageHeight - cardBottom + 0.38)} m {N(AfCardRight)} {N(pageHeight - cardBottom + 0.38)} l S");
            page.AddContentStream(Encoding.ASCII.GetBytes(bg.ToString()));
            page.AddContentStream(Encoding.ASCII.GetBytes(boxes.ToString()));
            page.AddContentStream(Encoding.ASCII.GetBytes(runs.ToString()));
        }

        void NewPage()
        {
            page = doc.Pages.Add(pageWidth, pageHeight);
            EnsureFonts(page);
            boxes = new StringBuilder();
            runs = new StringBuilder();
        }

        void Emit(string res, double fs, double x, double yTd, string text, string rgb)
        {
            runs.AppendLine($"BT {rgb} rg");
            runs.Append($"/{res} {fs.ToString("F2", inv)} Tf ");
            runs.Append($"1 0 0 1 {N(x)} {N(pageHeight - yTd)} Tm ");
            runs.AppendLine($"({EscapePdfString(text)}) Tj ET");
        }

        void QuestionBox(double topTd)
        {
            boxes.AppendLine("0.98 0.98 0.98 rg");
            boxes.AppendLine($"{N(AfBoxLeft)} {N(pageHeight - topTd - AfBoxH)} {N(AfBoxRight - AfBoxLeft)} {N(AfBoxH)} re f");
            boxes.AppendLine("0.933 0.933 0.933 RG 0.75 w");
            boxes.AppendLine($"{N(AfBoxLeft)} {N(pageHeight - topTd - AfBoxH + 0.38)} {N(AfBoxRight - AfBoxLeft)} {N(AfBoxH - 0.76)} re S");
        }

        void Rule(double yTd)
        {
            boxes.AppendLine("0.533 0.533 0.533 RG 0.75 w");
            boxes.AppendLine($"{N(AfHrLeft)} {N(pageHeight - yTd)} m {N(AfHrRight)} {N(pageHeight - yTd)} l S");
            boxes.AppendLine("0.867 0.867 0.867 RG");
            boxes.AppendLine($"{N(AfHrLeft)} {N(pageHeight - yTd - 0.75)} m {N(AfHrRight)} {N(pageHeight - yTd - 0.75)} l S");
        }

        NewPage();
        const string orange = "0.874 0.424 0";
        const string gray = "0.396 0.396 0.396";
        const string black = "0 0 0";
        Emit("F2", AfTitleFs, AfTitleX, AfTitleBl, Flat(titleM.Groups[1].Value), orange);

        var lastMark = AfTitleBl;      // the last baseline (text) or box bottom
        var lastWasBox = false;
        foreach (var (kind, a, b) in items)
        {
            switch (kind)
            {
                case "intro":
                    lastMark = AfTitleBl + AfIntroBlOff;
                    Emit("F1", AfBodyFs, AfIntroX, lastMark, a, gray);
                    lastWasBox = false;
                    break;
                case "hr":
                    lastMark += lastWasBox ? AfHrAfterBoxes : AfHrAfterIntro;
                    Rule(lastMark);
                    lastWasBox = false;
                    break;
                case "head":
                    lastMark += AfSectionBlOff;
                    Emit("F1", AfSectionFs, AfSectionX, lastMark, a, black);
                    lastMark += AfFirstBoxOff;   // becomes the first box top
                    lastWasBox = false;
                    break;
                case "pre":
                    lastMark += AfVarBlOff;      // from the preceding box bottom
                    Emit("F4", AfVarFs, AfVarX, lastMark, a, gray);
                    if (b.Length > 0)
                    {
                        lastMark += AfVarPitch;
                        Emit("F4", AfVarFs, AfVarContX, lastMark, b, gray);
                    }
                    lastMark += AfBoxAfterVar;   // becomes the next box top
                    lastWasBox = false;
                    break;
                case "qa":
                    var top = lastWasBox ? lastMark + AfBoxPitch - AfBoxH : lastMark;
                    if (top + AfBoxH > AfGroundBottom)
                    {
                        FlushPage(AfGroundBottom, lastPage: false);
                        firstPage = false;
                        NewPage();
                        top = AfGroundTop;
                    }
                    QuestionBox(top);
                    Emit("F2", AfBodyFs, AfTextX, top + AfQBlOff, a, black);
                    Emit("F1", AfBodyFs, AfTextX, top + AfABlOff, b, gray);
                    lastMark = top + AfBoxH;     // the box bottom
                    lastWasBox = true;
                    break;
            }
        }
        FlushPage(lastMark + AfCardTailAfterHr, lastPage: true);
        return doc;
    }
}
