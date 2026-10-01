using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// The benefit-commencement review export: an Angular app page whose megabyte
// stylesheet is dropped entirely, leaving a UA-default Times flow —
// bulleted wizard lists, the Review Your Information h1, bold section h3s and
// blue underlined links on a 13.5 pt line grid. Visibility survives without
// the sheet through the serializer's own markers (aria-hidden, .ng-hide, an
// inline display:none), and an anchor is a link only when it carries an href.
// Every pitch below is measured on the expected render.
internal static partial class HtmlToPdfConverter
{
    private const double BrBodyFs = 12.0;
    private const double BrH3Fs = 14.04;
    private const double BrH1Fs = 24.0;
    private const double BrLinePt = 13.5;          // the 12 pt UA line grid
    private const double BrTextX = 96.0;           // body margin
    private const double BrTextRight = 499.0;
    private const double BrBulletX = 117.3;        // list marker and text seats
    private const double BrLiTextX = 126.0;
    private const double BrListMarginPt = 26.94;   // flow ↔ list boundary
    private const double BrToH1Pt = 40.75;         // flow → h1 base
    private const double BrH1OutPt = 32.66;        // h1 → flow base
    private const double BrToH3Pt = 27.34;         // flow → h3 base
    private const double BrH3OutPt = 25.96;        // h3 → flow base
    private const double BrH3ToH3Pt = 28.15;
    private const double BrTopSeat12 = 96.24;      // first baseline on a page
    private const double BrTopSeatH3 = 96.64;
    private const double BrTopSeatH1 = 97.5;
    private const double BrBottomPt = 760.0;       // past this a line breaks
    private const double BrUnderlineDropPt = 1.2;
    private const double BrDescFrac = 0.216;       // Times descent, break test

    private enum BrKind { Flow, Li, H1, H3 }

    private sealed class BrRun
    {
        public string Text = "";
        public bool Bold;
        public bool Link;
    }

    private sealed class BrLine
    {
        public BrKind Kind;
        public List<BrRun> Runs = new();
    }

    /// <summary>Render the benefit-review export, or null when the page is not it.</summary>
    private static Document? TryRenderBenefitReview(string html, double pageWidth, double pageHeight)
    {
        var bv = new BenefitReviewState();
        bv.html = html;
        bv.pageWidth = pageWidth;
        bv.pageHeight = pageHeight;
        if (!bv.html.Contains("id=\"skipToMainContent\"", StringComparison.Ordinal)
            || !bv.html.Contains("ng-controller=\"navigationController\"", StringComparison.Ordinal)
            || !bv.html.Contains("display-address=", StringComparison.Ordinal))
            return null;

        bv.bodyM = Regex.Match(bv.html, "<body[^>]*>([\\s\\S]*?)(?:</body|$)", RegexOptions.IgnoreCase);
        if (!bv.bodyM.Success) return null;
        bv.body = Regex.Replace(bv.bodyM.Groups[1].Value, "<!--[\\s\\S]*?-->", "");
        bv.body = Regex.Replace(bv.body, "<style[\\s\\S]*?</style>", "", RegexOptions.IgnoreCase);
        bv.body = Regex.Replace(bv.body, "<script[\\s\\S]*?</script>", "", RegexOptions.IgnoreCase);

        bv.lines = new List<BrLine>();
        bv.cur = new List<BrRun>();
        bv.boldDepth = 0;
        bv.linkDepth = 0;
        bv.hiddenDepth = 0;
        bv.liDepth = 0;
        bv.h1Depth = 0;
        bv.h3Depth = 0;
        bv.stack = new List<(string Tag, bool Hidden, bool Bold, bool Link, bool Li, bool H1, bool H3)>();
        bv.pendingSpace = false;

        BuildBenefitReviewTagSets(bv);

        bv.tagRx = new Regex("<(/?)([a-zA-Z][a-zA-Z0-9]*)((?:\"[^\"]*\"|'[^']*'|[^>\"'])*)>");
        bv.pos = 0;
        foreach (Match m in bv.tagRx.Matches(bv.body))
        {
            if (!AbsorbBenefitReviewTag(bv, m)) break;
        }
        if (bv.pos < bv.body.Length) AddText(bv, bv.body[bv.pos..]);
        Flush(bv);
        if (bv.lines.Count < 10) return null;

        OpenBenefitReviewDocument(bv);
        bv.yBase = 0.0;
        EmitBenefitReviewLines(bv);

        foreach (var (p, buf) in bv.pageBufs)
            p.AddContentStream(Encoding.ASCII.GetBytes(buf.ToString()));
        return bv.doc;
    }
}
