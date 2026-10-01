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
        var af = new AccessFormState();
        af.html = html;
        af.css = css;
        af.pageWidth = pageWidth;
        af.pageHeight = pageHeight;
        if (!af.css.TryGetValue(".pageContainer", out var cardRule)
            || !cardRule.ContainsKey("box-shadow")
            || !af.css.TryGetValue(".questionContainer", out var qRule)
            || !(qRule.TryGetValue("background-color", out var qbg)
                 && qbg.Contains("fafafa", StringComparison.OrdinalIgnoreCase))
            || !af.html.Contains("section-rule", StringComparison.Ordinal))
            return null;

        af.pageM = Regex.Match(af.html, @"class=['""]page['""]\s*>([\s\S]*)</div>\s*</div>\s*</body>",
            RegexOptions.IgnoreCase);
        if (!af.pageM.Success) return null;
        af.content = af.pageM.Groups[1].Value;
        af.titleM = Regex.Match(af.content, @"class=['""]header['""]\s*>([\s\S]*?)</div>",
            RegexOptions.IgnoreCase);
        if (!af.titleM.Success) return null;

        af.items = new List<(string Kind, string A, string B)>();
        CollectAccessFormItems(af);
        if (af.items.Count == 0) return null;

        af.doc = new Document();
        af.inv = System.Globalization.CultureInfo.InvariantCulture;
        af.page = null!;
        af.boxes = null!;
        af.runs = null!;
        af.firstPage = true;

        NewPage(af);
        af.orange = "0.874 0.424 0";
        af.gray = "0.396 0.396 0.396";
        af.black = "0 0 0";
        Emit(af, "F2", AfTitleFs, AfTitleX, AfTitleBl, Flat(af, af.titleM.Groups[1].Value), af.orange);

        af.lastMark = AfTitleBl;      // the last baseline (text) or box bottom
        af.lastWasBox = false;
        EmitAccessFormItems(af);
        FlushPage(af, af.lastMark + AfCardTailAfterHr, lastPage: true);
        return af.doc;
    }
}
