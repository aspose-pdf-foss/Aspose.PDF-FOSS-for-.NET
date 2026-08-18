using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// The covering-letter dialect (the `.covering-letter` frame + `data-berthr-editable`
// spans): a marina renewal letter whose CDN stylesheet pins every paragraph and
// span to `line-height: 100%` — wrapped paragraph lines ride a bare 10.5 pt box
// while any line that a <br> ends OR opens takes the universal selector's 1.2em
// (12.6 pt) box, as do list items (li is neither p nor span). The `.justify` band
// insets the letter 15px inside the body box, whose right edge sits 84 pt from
// the sheet edge (the body width:100% resolution, measured); paragraphs justify
// to that band and bullets to the ul's 15px margins. Pagination is plain
// bottom-margin overflow — the split line restarts flush at the top margin with
// pending gaps dropped. Only constants the stylesheet cannot supply are measured
// on the reference render.
internal static partial class HtmlToPdfConverter
{
    private const double LtRightInset = 84.0;   // body content right inset (measured; width:100% body)
    private const double LtLineP = 10.5;        // p line box: line-height 100% of the 14px base
    private const double LtLineEm = 12.6;       // the * 1.2em line box (li rows, br-adjacent lines)
    private const double LtJustPad = 11.25;     // .justify margin: 15px
    private const double LtUlMargin = 11.25;    // ul margin: 15px
    private const double LtLiIndent = 30.0;     // the UA 40px list indentation
    private const double LtBulletOff = 7.61;    // marker pen left of the item text (measured)
    private const double LtPTop = 7.5;          // .covering-letter p margin-top 10px
    private const double LtPBottom = 3.75;      // .covering-letter p margin-bottom 5px
    private const double LtTableML = 18.75;     // address table margin-left 25px
    private const double LtTableMT = 75.0;      // address table margin-top 100px
    private const double LtTableMB = 7.5;       // address table margin-bottom 10px
    private const double LtAddrRow2Off = 18.9;  // second address row below the first (measured)
    private const double LtSupRise = 3.67;      // superscript raise (measured)
    private const double LtSupGrow = 2.24;      // the sup's overshoot grows its line box (measured)
    private const double LtMargin = 72.0;       // top/bottom page margins

    private sealed class LtLine
    {
        public List<OsRun> Segs = new();
        public double X, JustifyTo, BoxH, Drop, GapBefore;
        public bool Bullet;                     // draw the list marker left of X
        public byte[]? Img;
        public double ImgW, ImgH;
    }

    /// <summary>Render a covering-letter export, or null without the fingerprint.</summary>
    private static Document? TryRenderCoveringLetter(string html,
        double pageWidth, double pageHeight)
    {
        if (!html.Contains("covering-letter", StringComparison.Ordinal)
            || !html.Contains("data-berthr-editable", StringComparison.Ordinal)
            || !Regex.IsMatch(html, @"class\s*=\s*[""']justify[""']", RegexOptions.IgnoreCase))
            return null;
        if (WinMetricsFor("Arial") is not { } am) return null;
        var cm = WinMetricsFor("Candara Bold") ?? am;

        var contentL = 96.0;
        var contentR = pageWidth - LtRightInset;
        var justL = contentL + LtJustPad;
        var justR = contentR - LtJustPad;
        var liX = justL + LtUlMargin + LtLiIndent;
        var liRight = justR - LtUlMargin;
        var dropP = MetricBaselineDrop(10.5, LtLineP, am);
        var dropEm = MetricBaselineDrop(10.5, LtLineEm, am);

        // ── parse ──
        var h2M = Regex.Match(html, @"<h2\b[^>]*>([\s\S]*?)</h2>", RegexOptions.IgnoreCase);
        var justM = Regex.Match(html,
            @"<div\b[^>]*class\s*=\s*[""']justify[""'][^>]*>([\s\S]*?)</div>", RegexOptions.IgnoreCase);
        var addrM = Regex.Match(html, @"<table[^>]*margin-top:\s*100px[\s\S]*?(<table[^>]*>[\s\S]*?</table>)",
            RegexOptions.IgnoreCase);
        if (!h2M.Success || !justM.Success || !addrM.Success) return null;

        string Flat(string s) => Regex.Replace(DecodeEntities(
            Regex.Replace(s, @"<[^>]+>", "")), @"[ \t\r\n]+", " ").Trim(' ');
        List<string> BrLines(string frag)
        {
            var parts = Regex.Split(frag, @"<br\s*/?>", RegexOptions.IgnoreCase)
                .Select(Flat).ToList();
            while (parts.Count > 0 && parts[^1].Length == 0) parts.RemoveAt(parts.Count - 1);
            return parts;
        }

        var h2Lines = BrLines(h2M.Groups[1].Value);
        var addrTds = Regex.Matches(addrM.Groups[1].Value,
            @"<td\b[^>]*>([\s\S]*?)</td>", RegexOptions.IgnoreCase);
        if (addrTds.Count < 3) return null;
        var addrName = Flat(addrTds[0].Groups[1].Value);
        // the rowspan cell keeps its interior blank lines; only the final
        // trailing <br> makes no line
        var addrRight = Regex.Split(addrTds[1].Groups[1].Value, @"<br\s*/?>", RegexOptions.IgnoreCase)
            .Select(Flat).ToList();
        while (addrRight.Count > 0 && addrRight[^1].Length == 0) addrRight.RemoveAt(addrRight.Count - 1);
        addrRight.Add("");                       // the pair of closing <br>s leaves one blank line
        var addrLeft = BrLines(addrTds[2].Groups[1].Value);

        // ── the flow: .justify children in order ──
        var flow = new List<LtLine>();
        double prevBottom = LtTableMB;           // the address table's margin-bottom opens the flow
        var firstGapExtra = LtSupGrow;           // the date line's superscript overshoot

        void AddLines(List<OsRun> runs, double x, double right, bool justify, bool liMode,
            double marginTop, double marginBottom, bool bullet)
        {
            var groups = OsBreakGroups(runs);
            var gap = Math.Max(prevBottom, marginTop) + firstGapExtra;
            firstGapExtra = 0;
            var firstOfBlock = true;
            for (var g = 0; g < groups.Count; g++)
            {
                var lines = OsWrap(groups[g], right - x);
                if (lines.Count == 0) lines.Add(new List<OsRun>());
                for (var i = 0; i < lines.Count; i++)
                {
                    var em = liMode || groups[g].Count == 0
                        || (i == 0 && g > 0) || (i == lines.Count - 1 && g < groups.Count - 1);
                    var lastOfGroup = i == lines.Count - 1;
                    flow.Add(new LtLine
                    {
                        Segs = lines[i],
                        X = x,
                        JustifyTo = justify && !lastOfGroup ? right : 0,
                        BoxH = em ? LtLineEm : LtLineP,
                        Drop = em ? dropEm : dropP,
                        GapBefore = firstOfBlock ? gap : 0,
                        Bullet = bullet && g == 0 && i == 0,
                    });
                    firstOfBlock = false;
                }
            }
            prevBottom = marginBottom;
        }

        var body = justM.Groups[1].Value;
        for (var i = 0; i < body.Length;)
        {
            var m = Regex.Match(body[i..], @"<(p|ul|br)\b", RegexOptions.IgnoreCase);
            if (!m.Success) break;
            var at = i + m.Index;
            var tag = m.Groups[1].Value.ToLowerInvariant();
            if (tag == "br")
            {
                // a bare <br> between blocks: one anonymous 1.2em line, no collapse
                flow.Add(new LtLine
                {
                    X = justL, BoxH = LtLineEm, Drop = dropEm, GapBefore = prevBottom,
                });
                prevBottom = 0;
                i = body.IndexOf('>', at) + 1;
                continue;
            }
            var close = body.IndexOf($"</{tag}>", at, StringComparison.OrdinalIgnoreCase);
            if (close < 0) break;
            var openEnd = body.IndexOf('>', at);
            var attrs = body[at..openEnd];
            var inner = body[(openEnd + 1)..close];
            i = close + tag.Length + 3;

            var mtM = Regex.Match(attrs, @"margin-top:\s*([\d.]+)px");
            var marginTop = mtM.Success
                ? double.Parse(mtM.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * 0.75
                : LtPTop;
            if (tag == "ul")
            {
                var firstLi = true;
                foreach (Match li in Regex.Matches(inner, @"<li\b[^>]*>([\s\S]*?)</li>", RegexOptions.IgnoreCase))
                {
                    AddLines(LtParseRuns(li.Groups[1].Value, out _), liX, liRight,
                        justify: true, liMode: true,
                        firstLi ? LtUlMargin : 0, 0, bullet: true);
                    firstLi = false;
                    prevBottom = 0;
                }
                prevBottom = LtUlMargin;
                continue;
            }
            var runs = LtParseRuns(inner, out var img);
            if (img is { } im)
            {
                // the signature paragraph: the image seats on its line's baseline
                var bytes = FetchRemoteImage(im.Src);
                double w = im.WPx * 0.75, h = w * 0.75;
                if (bytes is not null && TryReadImagePixelSize(bytes, out var pw, out var ph) && pw > 0)
                    h = w * ph / pw;
                flow.Add(new LtLine
                {
                    X = justL, BoxH = h + (LtLineP - dropP), Drop = h,
                    GapBefore = Math.Max(prevBottom, marginTop),
                    Img = bytes, ImgW = w, ImgH = h,
                });
                prevBottom = LtPBottom;
                continue;
            }
            AddLines(runs, justL, justR, justify: true, liMode: false,
                marginTop, LtPBottom, bullet: false);
        }
        if (flow.Count == 0) return null;

        // ── emit ──
        var doc = new Document();
        var page = doc.Pages.Add(pageWidth, pageHeight);
        EnsureFonts(page);
        EnsureFont(page, "Arial", "F8");
        EnsureFont(page, "ArialBold", "F9");
        EnsureFont(page, "ArialItalic", "F11");
        EnsureFont(page, "CandaraBold", "F12");
        var inv = System.Globalization.CultureInfo.InvariantCulture;

        static string ResFor(string face) => face switch
        {
            "Arial Bold" => "F9",
            "Arial Italic" => "F11",
            "Candara Bold" => "F12",
            _ => "F8",
        };
        void Stream(Page dst, string s) => dst.AddContentStream(Encoding.ASCII.GetBytes(s));
        double SegsW(List<OsRun> segs)
        {
            double w = 0;
            foreach (var r in segs) w += MeasureFaceText(r.Face, r.Text, r.Fs);
            return w;
        }
        void EmitRun(Page dst, OsRun r, double x, double yTd)
        {
            EmitPositionedRun(dst, ResFor(r.Face), r.Fs, x, pageHeight - yTd + r.Rise, r.Text);
            if (r.Under)
                Stream(dst, string.Create(inv,
                    $"q 1.05 w {x:F2} {pageHeight - yTd - 1.05:F2} m {x + MeasureFaceText(r.Face, r.Text, r.Fs):F2} {pageHeight - yTd - 1.05:F2} l S Q\n"));
        }
        void EmitLine(Page dst, LtLine ln, double baseTd)
        {
            var x = ln.X;
            if (ln.JustifyTo > 0)
            {
                // stretch the word spaces so the last glyph seats on the band edge
                var natural = SegsW(ln.Segs);
                var spaces = ln.Segs.Sum(s => s.Text.Count(c => c == ' '));
                var extra = ln.JustifyTo - ln.X - natural;
                if (spaces > 0 && extra > 0.01)
                {
                    var per = extra / spaces;
                    foreach (var seg in ln.Segs)
                    {
                        var pieces = seg.Text.Split(' ');
                        for (var k = 0; k < pieces.Length; k++)
                        {
                            if (pieces[k].Length > 0)
                            {
                                EmitRun(dst, seg with { Text = pieces[k] }, x, baseTd);
                                x += MeasureFaceText(seg.Face, pieces[k], seg.Fs);
                            }
                            if (k < pieces.Length - 1)
                                x += MeasureFaceText(seg.Face, " ", seg.Fs) + per;
                        }
                    }
                    return;
                }
            }
            foreach (var seg in ln.Segs)
            {
                if (seg.Text.Length > 0) EmitRun(dst, seg, x, baseTd);
                x += MeasureFaceText(seg.Face, seg.Text, seg.Fs);
            }
        }

        // page 1 header: the h2 block and the two-column address table
        var h2Line = 16.5 * 1.2;                 // 22px type on its 1.2em line
        var h2Drop = MetricBaselineDrop(16.5, h2Line, cm);
        var y = LtMargin + OsUaBody;
        Stream(page, "q 0.106 0.208 0.369 rg\n"); // the sheet's #1b355e heading blue
        for (var i = 0; i < h2Lines.Count; i++)
            EmitRun(page, new OsRun(h2Lines[i], "Candara Bold", 16.5),
                contentL, y + i * h2Line + h2Drop);
        Stream(page, "0 0 0 rg\nQ\n");
        y += h2Lines.Count * h2Line + LtTableMT;

        var tableX = contentL + LtTableML;
        var tableRight = tableX + 0.9 * (contentR - contentL);
        EmitRun(page, new OsRun(addrName, "Arial", 10.5), tableX, y + dropEm);
        for (var i = 0; i < addrRight.Count; i++)
        {
            if (addrRight[i].Length == 0) continue;
            var r = new OsRun(addrRight[i], "Arial", 10.5);
            EmitRun(page, r, tableRight - MeasureFaceText(r.Face, r.Text, r.Fs),
                y + i * LtLineEm + dropEm);
        }
        for (var i = 0; i < addrLeft.Count; i++)
            EmitRun(page, new OsRun(addrLeft[i], "Arial", 10.5),
                tableX, y + LtAddrRow2Off + i * LtLineEm + dropEm);
        y += Math.Max(addrRight.Count * LtLineEm, LtAddrRow2Off + addrLeft.Count * LtLineEm);

        // the paginated letter flow
        var pg = page;
        foreach (var ln in flow)
        {
            var top = y + ln.GapBefore;
            if (top + ln.BoxH > pageHeight - LtMargin)
            {
                pg = doc.Pages.Add(pageWidth, pageHeight);
                EnsureFonts(pg);
                EnsureFont(pg, "Arial", "F8");
                EnsureFont(pg, "ArialBold", "F9");
                EnsureFont(pg, "ArialItalic", "F11");
                top = LtMargin;
            }
            if (ln.Img is not null)
                pg.AddImage(ln.Img, new Rectangle(ln.X, pageHeight - top - ln.ImgH,
                    ln.X + ln.ImgW, pageHeight - top));
            if (ln.Bullet)
                EmitRun(pg, new OsRun("•", "Arial", 10.5), ln.X - LtBulletOff, top + ln.Drop);
            EmitLine(pg, ln, top + ln.Drop);
            y = top + ln.BoxH;
        }
        return doc;
    }

    /// <summary>Parse a letter fragment into styled runs: bold/italic spans, the
    /// superscript raise, break markers; Arial at the 14px base throughout.</summary>
    private static List<OsRun> LtParseRuns(string frag, out (string Src, double WPx)? img)
    {
        img = null;
        var runs = new List<OsRun>();
        var stack = new List<(bool Bold, bool Italic, bool Under, double Fs, double Rise)>();
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        foreach (Match m in Regex.Matches(frag, @"<[^>]+>|[^<]+"))
        {
            var tok = m.Value;
            if (tok[0] == '<')
            {
                var close = tok.Length > 1 && tok[1] == '/';
                var name = Regex.Match(tok, @"^</?\s*([a-zA-Z0-9]+)").Groups[1].Value.ToLowerInvariant();
                switch (name)
                {
                    case "span" or "sup" when !close:
                    {
                        var style = Regex.Match(tok, @"style\s*=\s*[""']([^""']*)").Groups[1].Value;
                        var (bold, italic, under, fs, rise) = stack.Count > 0
                            ? stack[^1] : (false, false, false, 10.5, 0.0);
                        if (Regex.IsMatch(style, @"font-weight\s*:\s*bold", RegexOptions.IgnoreCase))
                            bold = true;
                        if (Regex.IsMatch(style, @"font-style\s*:\s*italic", RegexOptions.IgnoreCase))
                            italic = true;
                        if (Regex.IsMatch(style, @"text-decoration\s*:\s*underline", RegexOptions.IgnoreCase))
                            under = true;
                        if (name == "sup"
                            || Regex.IsMatch(style, @"font-size\s*:\s*75\s*%", RegexOptions.IgnoreCase))
                        {
                            fs *= 0.75;
                            rise += LtSupRise;
                        }
                        stack.Add((bold, italic, under, fs, rise));
                        break;
                    }
                    case "span" or "sup":
                        if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
                        break;
                    case "br":
                        runs.Add(new OsRun("\n", "", 0));
                        break;
                    case "img" when !close:
                    {
                        var src = Regex.Match(tok, @"src\s*=\s*[""']([^""']*)").Groups[1].Value;
                        var wM = Regex.Match(tok, @"width\s*=\s*[""']?([\d.]+)");
                        if (src.Length > 0)
                            img = (src, wM.Success ? double.Parse(wM.Groups[1].Value, inv) : 80.0);
                        break;
                    }
                }
                continue;
            }
            var text = Regex.Replace(DecodeEntities(tok), @"[ \t\r\n]+", " ");
            if (text.Length == 0) continue;
            var st = stack.Count > 0 ? stack[^1] : (Bold: false, Italic: false, Under: false, Fs: 10.5, Rise: 0.0);
            var face = st.Bold ? "Arial Bold" : st.Italic ? "Arial Italic" : "Arial";
            if (runs.Count > 0 && runs[^1].Text != "\n" && runs[^1].Text.EndsWith(' ')
                && text.StartsWith(' '))
                text = text[1..];
            if (text.Length > 0)
                runs.Add(new OsRun(text, face, st.Fs, st.Rise, st.Under));
        }
        while (runs.Count > 0 && runs[0].Text != "\n"
            && runs[0].Text.TrimStart(' ').Length == 0)
            runs.RemoveAt(0);
        if (runs.Count > 0 && runs[0].Text != "\n" && runs[0].Text.StartsWith(' '))
            runs[0] = runs[0] with { Text = runs[0].Text.TrimStart(' ') };
        while (runs.Count > 0 && runs[^1].Text != "\n"
            && runs[^1].Text.TrimEnd(' ').Length == 0)
            runs.RemoveAt(runs.Count - 1);
        if (runs.Count > 0 && runs[^1].Text != "\n" && runs[^1].Text.EndsWith(' '))
            runs[^1] = runs[^1] with { Text = runs[^1].Text.TrimEnd(' ') };
        // a trailing <br> at block end opens no line
        while (runs.Count > 0 && runs[^1].Text == "\n")
            runs.RemoveAt(runs.Count - 1);
        return runs;
    }
}
