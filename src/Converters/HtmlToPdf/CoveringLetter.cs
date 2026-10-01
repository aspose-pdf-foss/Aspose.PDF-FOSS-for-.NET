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
// on the expected render.
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

    /// <summary>Parse a letter fragment into styled runs: bold/italic spans, the
    /// superscript raise, break markers; Arial at the 14px base throughout.</summary>
    /// <summary>Covering letter: one html fragment flattened to its plain text.</summary>
    private static string LtFlat(string s) => Regex.Replace(DecodeEntities(
        Regex.Replace(s, @"<[^>]+>", "")), @"[ \t\r\n]+", " ").Trim(' ');

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
