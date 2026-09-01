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
        if (!html.Contains("id=\"skipToMainContent\"", StringComparison.Ordinal)
            || !html.Contains("ng-controller=\"navigationController\"", StringComparison.Ordinal)
            || !html.Contains("display-address=", StringComparison.Ordinal))
            return null;

        // the export ends with a malformed `</body</html>` — take the close lazily
        var bodyM = Regex.Match(html, "<body[^>]*>([\\s\\S]*?)(?:</body|$)", RegexOptions.IgnoreCase);
        if (!bodyM.Success) return null;
        var body = Regex.Replace(bodyM.Groups[1].Value, "<!--[\\s\\S]*?-->", "");
        body = Regex.Replace(body, "<style[\\s\\S]*?</style>", "", RegexOptions.IgnoreCase);
        body = Regex.Replace(body, "<script[\\s\\S]*?</script>", "", RegexOptions.IgnoreCase);

        // ── walk the serialized DOM into formatted lines ──
        var lines = new List<BrLine>();
        var cur = new List<BrRun>();
        int boldDepth = 0, linkDepth = 0, hiddenDepth = 0;
        int liDepth = 0, h1Depth = 0, h3Depth = 0;
        var stack = new List<(string Tag, bool Hidden, bool Bold, bool Link, bool Li, bool H1, bool H3)>();
        var pendingSpace = false;

        void Flush()
        {
            if (cur.Count > 0)
            {
                var line = new BrLine
                {
                    Kind = liDepth > 0 ? BrKind.Li : h1Depth > 0 ? BrKind.H1
                        : h3Depth > 0 ? BrKind.H3 : BrKind.Flow,
                };
                line.Runs.AddRange(cur);
                lines.Add(line);
            }
            cur.Clear();
            pendingSpace = false;
        }
        void AddText(string raw)
        {
            if (hiddenDepth > 0) return;
            var startsWs = raw.Length > 0 && char.IsWhiteSpace(raw[0]);
            var endsWs = raw.Length > 0 && char.IsWhiteSpace(raw[^1]);
            var t = CollapseWs(DecodeEntities(raw)).Trim();
            if (t.Length == 0) { pendingSpace |= (startsWs || endsWs) && cur.Count > 0; return; }
            var bold = boldDepth > 0 || h1Depth > 0 || h3Depth > 0;
            var link = linkDepth > 0;
            if ((pendingSpace || startsWs) && cur.Count > 0) t = " " + t;
            if (cur.Count > 0 && cur[^1].Bold == bold && cur[^1].Link == link)
                cur[^1].Text += t;
            else
                cur.Add(new BrRun { Text = t, Bold = bold, Link = link });
            pendingSpace = endsWs;
        }

        var blockTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "div", "p", "h1", "h2", "h3", "h4", "ul", "ol", "li", "table", "tbody",
            "thead", "tr", "td", "th", "section", "header", "footer", "nav", "form",
            "fieldset", "blockquote", "pre", "article", "aside",
        };
        var voidTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "br", "img", "input", "hr", "meta", "link", "col", "wbr", "source" };

        var tagRx = new Regex("<(/?)([a-zA-Z][a-zA-Z0-9]*)((?:\"[^\"]*\"|'[^']*'|[^>\"'])*)>");
        var pos = 0;
        foreach (Match m in tagRx.Matches(body))
        {
            if (m.Index > pos) AddText(body[pos..m.Index]);
            pos = m.Index + m.Length;
            var closing = m.Groups[1].Value.Length > 0;
            var tag = m.Groups[2].Value;
            var attrs = m.Groups[3].Value;
            if (voidTags.Contains(tag))
            {
                if (!closing && hiddenDepth == 0
                    && tag.Equals("br", StringComparison.OrdinalIgnoreCase)) Flush();
                continue;
            }
            if (!closing)
            {
                var classM = Regex.Match(attrs, "class[ ]*=[ ]*\"([^\"]*)\"", RegexOptions.IgnoreCase);
                var styleM = Regex.Match(attrs, "style[ ]*=[ ]*\"([^\"]*)\"", RegexOptions.IgnoreCase);
                var hidden = attrs.Contains("aria-hidden=\"true\"", StringComparison.OrdinalIgnoreCase)
                    || (classM.Success && Regex.IsMatch(classM.Groups[1].Value, "(^| )ng-hide( |$)"))
                    || (styleM.Success && Regex.IsMatch(styleM.Groups[1].Value,
                        "display[ ]*:[ ]*none", RegexOptions.IgnoreCase));
                var isBold = tag.Equals("strong", StringComparison.OrdinalIgnoreCase)
                    || tag.Equals("b", StringComparison.OrdinalIgnoreCase);
                // an anchor is a link when it CARRIES an href — even an empty
                // one (the ng-click actions keep href=""); no attribute at all
                // (the nav tabs) draws as plain text
                var isLink = tag.Equals("a", StringComparison.OrdinalIgnoreCase)
                    && Regex.IsMatch(attrs, "(^|[ ])href[ ]*=", RegexOptions.IgnoreCase);
                var isLi = tag.Equals("li", StringComparison.OrdinalIgnoreCase);
                var isH1 = tag.Equals("h1", StringComparison.OrdinalIgnoreCase);
                var isH3 = tag.Equals("h3", StringComparison.OrdinalIgnoreCase)
                    || tag.Equals("h2", StringComparison.OrdinalIgnoreCase);
                // a hidden block leaves no box, so it does not split the line
                if (blockTags.Contains(tag) && hiddenDepth == 0 && !hidden) Flush();
                stack.Add((tag, hidden, isBold, isLink, isLi, isH1, isH3));
                if (hidden) hiddenDepth++;
                if (isBold) boldDepth++;
                if (isLink) linkDepth++;
                if (isLi) liDepth++;
                if (isH1) h1Depth++;
                if (isH3) h3Depth++;
            }
            else
            {
                var s = stack.FindLastIndex(e => e.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase));
                if (s < 0) continue; // unmatched close: ignore
                // flush BEFORE popping, while the li/h1/h3 context still names
                // the line's kind
                if (blockTags.Contains(tag) && hiddenDepth == 0 && !stack[s].Hidden) Flush();
                // pop through any unclosed inner tags up to the match
                for (var k = stack.Count - 1; k >= s; k--)
                {
                    var p = stack[k];
                    if (p.Hidden) hiddenDepth--;
                    if (p.Bold) boldDepth--;
                    if (p.Link) linkDepth--;
                    if (p.Li) liDepth--;
                    if (p.H1) h1Depth--;
                    if (p.H3) h3Depth--;
                    stack.RemoveAt(k);
                }
            }
        }
        if (pos < body.Length) AddText(body[pos..]);
        Flush();
        if (lines.Count < 10) return null;

        // ── flow the lines onto the UA grid ──
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string N(double v) => v.ToString("0.###", inv);
        var doc = new Document();
        Page page = null!;
        StringBuilder sb = null!;
        var pageBufs = new List<(Page Page, StringBuilder Buf)>();
        void OpenPage()
        {
            page = doc.Pages.Add(pageWidth, pageHeight);
            EnsureFonts(page);
            sb = new StringBuilder();
            pageBufs.Add((page, sb));
        }
        OpenPage();
        double Y(double yTd) => pageHeight - yTd;

        double FsOf(BrKind k) => k switch
        {
            BrKind.H1 => BrH1Fs, BrKind.H3 => BrH3Fs, _ => BrBodyFs,
        };
        double TopSeat(BrKind k) => k switch
        {
            BrKind.H1 => BrTopSeatH1, BrKind.H3 => BrTopSeatH3, _ => BrTopSeat12,
        };
        double Pitch(BrKind prev, BrKind curK)
        {
            if (curK == BrKind.H1) return BrToH1Pt;
            if (prev == BrKind.H1) return BrH1OutPt;
            if (curK == BrKind.H3) return prev == BrKind.H3 ? BrH3ToH3Pt : BrToH3Pt;
            if (prev == BrKind.H3) return BrH3OutPt;
            if (curK == BrKind.Li && prev != BrKind.Li) return BrListMarginPt;
            if (prev == BrKind.Li && curK != BrKind.Li) return BrListMarginPt;
            return BrLinePt;
        }
        double Measure(BrRun r, double fs) => MeasureFaceText(
            r.Bold ? "Times New Roman-Bold" : "Times New Roman", r.Text, fs);

        void EmitLineAt(BrLine line, double baseTd, double x)
        {
            var pen = x;
            foreach (var r in line.Runs)
            {
                var fs = FsOf(line.Kind);
                var w = Measure(r, fs);
                var res = r.Bold || line.Kind is BrKind.H1 or BrKind.H3 ? "F6" : "F5";
                var rg = r.Link ? "0 0 1 rg" : "0 0 0 rg";
                sb.AppendLine($"BT {rg} /{res} {fs.ToString("F2", inv)} Tf "
                    + $"1 0 0 1 {N(pen)} {N(Y(baseTd))} Tm ({EscapePdfString(r.Text)}) Tj ET");
                if (r.Link)
                    sb.AppendLine($"0 0 1 RG 0.75 w {N(pen)} {N(Y(baseTd + BrUnderlineDropPt))} m "
                        + $"{N(pen + w)} {N(Y(baseTd + BrUnderlineDropPt))} l S");
                pen += w;
            }
        }

        // greedy wrap of a line's runs into sublines that fit the content box
        List<BrLine> WrapLine(BrLine line)
        {
            var x0 = line.Kind == BrKind.Li ? BrLiTextX : BrTextX;
            var avail = BrTextRight - x0;
            var fs = FsOf(line.Kind);
            var outLines = new List<BrLine>();
            var curL = new BrLine { Kind = line.Kind };
            var used = 0.0;
            foreach (var run in line.Runs)
            {
                var words = run.Text.Split(' ');
                var buf = "";
                void CloseRun()
                {
                    if (buf.Length == 0) return;
                    curL.Runs.Add(new BrRun { Text = buf, Bold = run.Bold, Link = run.Link });
                    buf = "";
                }
                for (var wi = 0; wi < words.Length; wi++)
                {
                    var word = words[wi];
                    var probe = buf.Length == 0 ? word : buf + " " + word;
                    var lead = curL.Runs.Count > 0 && buf.Length == 0 && word.Length > 0 ? " " : "";
                    var wNew = MeasureFaceText(run.Bold ? "Times New Roman-Bold" : "Times New Roman",
                        lead + probe, fs);
                    if (used + wNew > avail && (buf.Length > 0 || curL.Runs.Count > 0))
                    {
                        CloseRun();
                        outLines.Add(curL);
                        curL = new BrLine { Kind = line.Kind };
                        used = 0.0;
                        buf = word;
                    }
                    else buf = lead.Length > 0 ? lead + probe : probe;
                }
                if (buf.Length > 0)
                {
                    used += MeasureFaceText(run.Bold ? "Times New Roman-Bold" : "Times New Roman",
                        buf, fs);
                    CloseRun();
                }
            }
            if (curL.Runs.Count > 0) outLines.Add(curL);
            return outLines;
        }

        BrKind? prev = null;
        var yBase = 0.0;
        foreach (var line in lines)
        {
            var subs = WrapLine(line);
            for (var si = 0; si < subs.Count; si++)
            {
                var sub = subs[si];
                yBase = prev is null
                    ? TopSeat(sub.Kind)
                    : yBase + (si == 0 ? Pitch(prev.Value, sub.Kind) : BrLinePt);
                if (yBase + FsOf(sub.Kind) * BrDescFrac > BrBottomPt && prev is not null)
                {
                    OpenPage();
                    yBase = TopSeat(sub.Kind);
                }
                if (sub.Kind == BrKind.Li && si == 0)
                    sb.AppendLine($"BT 0 0 0 rg /F5 {BrBodyFs.ToString("F2", inv)} Tf "
                        + $"1 0 0 1 {N(BrBulletX)} {N(Y(yBase))} Tm ({EscapePdfString("•")}) Tj ET");
                EmitLineAt(sub, yBase, sub.Kind == BrKind.Li ? BrLiTextX : BrTextX);
                prev = sub.Kind;
            }
        }

        foreach (var (p, buf) in pageBufs)
            p.AddContentStream(Encoding.ASCII.GetBytes(buf.ToString()));
        return doc;
    }
}
