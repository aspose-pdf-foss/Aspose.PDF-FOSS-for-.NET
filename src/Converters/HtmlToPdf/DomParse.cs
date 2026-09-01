using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Parse the markup into a lightweight element tree. Void elements never
    /// nest; a mismatched close tag pops up to its nearest matching ancestor (or is
    /// dropped). Script/style/comment content must already be stripped.</summary>
    private static HtmlNode ParseDom(string html)
    {
        var root = new HtmlNode { Tag = "#root", SrcIndex = 0, SrcEnd = html.Length };
        var cur = root;
        foreach (var tok in Tokenize(html))
        {
            if (tok.Kind == TokenKind.Text)
            {
                if (tok.Value.Length > 0)
                    cur.Children.Add(new HtmlNode { Text = tok.Value, Parent = cur, SrcIndex = tok.SrcIndex, SrcEnd = tok.SrcEnd });
                continue;
            }
            if (tok.IsClose)
            {
                for (var n = cur; n is not null && n != root; n = n.Parent)
                {
                    if (n.Tag.Equals(tok.Tag, StringComparison.OrdinalIgnoreCase))
                    {
                        n.SrcEnd = tok.SrcEnd;
                        // Everything between stays where it landed; unclosed inner
                        // elements keep their children but end here too.
                        for (var m = cur; m != n; m = m.Parent!) m.SrcEnd = tok.SrcIndex;
                        cur = n.Parent ?? root;
                        break;
                    }
                }
                continue;
            }
            var el = new HtmlNode
            {
                Tag = tok.Tag!.ToLowerInvariant(),
                Attrs = tok.Attributes,
                Parent = cur,
                SrcIndex = tok.SrcIndex,
                SrcEnd = tok.SrcEnd,
            };
            cur.Children.Add(el);
            if (!tok.IsSelfClosing && !VoidTags.Contains(el.Tag))
                cur = el;
        }
        return root;
    }

    private static List<Token> Tokenize(string html)
    {
        var tokens = new List<Token>();
        int idx = 0;
        foreach (Match m in TagRx.Matches(html))
        {
            if (m.Index > idx)
            {
                var text = html.Substring(idx, m.Index - idx);
                if (text.Length > 0)
                    tokens.Add(new Token { Kind = TokenKind.Text, Value = text, SrcIndex = idx, SrcEnd = m.Index });
            }
            var attrs = ParseAttributes(m.Groups[3].Value);
            tokens.Add(new Token
            {
                Kind = TokenKind.Tag,
                Tag = m.Groups[2].Value,
                IsClose = m.Groups[1].Value == "/",
                IsSelfClosing = m.Groups[4].Value == "/",
                Attributes = attrs,
                SrcIndex = m.Index,
                SrcEnd = m.Index + m.Length,
            });
            idx = m.Index + m.Length;
        }
        if (idx < html.Length)
        {
            var text = html.Substring(idx);
            if (text.Length > 0)
                tokens.Add(new Token { Kind = TokenKind.Text, Value = text, SrcIndex = idx, SrcEnd = html.Length });
        }
        return tokens;
    }

    private static Dictionary<string, string>? ParseAttributes(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        // JSON-escaped HTML dialect (CMS/SharePoint exports wrap the markup in a quoted
        // string): attribute quotes arrive as \" — the value then reads as an UNQUOTED
        // token up to the next whitespace, KEEPING the \" wrappers (so a href URI keeps
        // them and a style value with spaces truncates at the first one) — this dialect
        // parses exactly so.
        if (s.IndexOf("\\\"", StringComparison.Ordinal) >= 0)
        {
            var dictEsc = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in EscapedAttrRx.Matches(s))
                if (!dictEsc.ContainsKey(m.Groups[1].Value))
                    dictEsc[m.Groups[1].Value] = m.Groups[2].Success ? m.Groups[2].Value : "";
            return dictEsc.Count > 0 ? dictEsc : null;
        }
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in AttrRx.Matches(s))
        {
            var name = m.Groups[1].Value;
            var val = m.Groups[2].Success ? m.Groups[2].Value
                     : m.Groups[3].Success ? m.Groups[3].Value
                     : m.Groups[4].Success ? m.Groups[4].Value
                     : "";
            // A repeated attribute keeps its FIRST value (the HTML parsing rule) —
            // generated markup carries duplicates like a display:none style followed
            // by a second layout style, and the hiding one must win.
            if (!dict.ContainsKey(name)) dict[name] = val;
            // …except that a repeated STYLE is not thrown away: generated markup splits
            // one declaration block over two attributes (`style='display:block' …
            // style="padding-right:20px"`) and BOTH are honoured. Merge them
            // first-wins PER PROPERTY, so the display:none case above still holds and
            // the second block only contributes what the first never declared.
            else if (val.Length > 0 && name.Equals("style", StringComparison.OrdinalIgnoreCase))
                dict[name] = MergeStyleFirstWins(dict[name], val);
        }
        return dict.Count > 0 ? dict : null;
    }

    /// <summary>WordWrapPastFloat on a face's REAL advances: the lines level with a float
    /// break at <paramref name="narrowWidth"/> and the rest at <paramref name="fullWidth"/>.
    /// The 0.52-em estimate over-measures Arial and broke the certificate's paragraphs four
    /// lines early.</summary>
    private static string[] MeasuredWordWrapPastFloat(string text, double narrowWidth,
        double fullWidth, int narrowLines, string face, double sizePt)
    {
        if (string.IsNullOrEmpty(text)) return [""];
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return [""];
        var spaceW = MeasureFaceText(face, " ", sizePt);
        var result = new List<string>();
        var line = new StringBuilder();
        double lineW = 0;
        foreach (var word in words)
        {
            var limit = result.Count < narrowLines ? narrowWidth : fullWidth;
            var w = MeasureFaceText(face, word, sizePt);
            if (line.Length > 0 && lineW + spaceW + w > limit)
            {
                result.Add(line.ToString());
                line.Clear(); lineW = 0;
            }
            if (line.Length > 0) { line.Append(' '); lineW += spaceW; }
            line.Append(word); lineW += w;
        }
        if (line.Length > 0) result.Add(line.ToString());
        return result.Count == 0 ? [""] : result.ToArray();
    }

    private static string EscapePdfString(string s)
    {
        // The content stream is written with Encoding.ASCII, so a raw non-ASCII char
        // (bullet U+2022, curly quotes, en/em dash, accented Latin) would be flattened
        // to '?'. Encode to Windows-1252 (the fonts declare /WinAnsiEncoding) and emit
        // any byte outside printable ASCII as an octal escape so it survives the ASCII
        // write and renders as the right glyph.
        var sb = new StringBuilder(s.Length + 8);
        foreach (var ch in s)
        {
            byte b = Aspose.Pdf.Text.Cp1252.TryGetByte(ch, out var wb) ? wb : (byte)'?';
            switch (b)
            {
                case (byte)'\\': sb.Append("\\\\"); break;
                case (byte)'(': sb.Append("\\("); break;
                case (byte)')': sb.Append("\\)"); break;
                default:
                    if (b >= 0x20 && b <= 0x7E) sb.Append((char)b);
                    else sb.Append('\\').Append(System.Convert.ToString(b, 8).PadLeft(3, '0'));
                    break;
            }
        }
        return sb.ToString();
    }

    private static void EnsureFonts(Page page, Core.PdfDictionary? sharedFontDict = null)
    {
        // When the caller supplies a per-conversion font dict, every page of the
        // conversion shares that ONE /Font resource dictionary. Type0FontEmbedder's
        // cache is keyed on the font dict, so a fallback face's program (Arial,
        // SimSun, … — megabytes each) is embedded once per DOCUMENT instead of once
        // per page; the writer serializes the shared objects a single time.
        if (sharedFontDict is not null)
        {
            var resources = page.Dict.Get("Resources") as Core.PdfDictionary;
            if (resources is null)
            {
                resources = new Core.PdfDictionary();
                page.Dict.Set("Resources", resources);
            }
            if (resources.Get("Font") is null)
                resources.Set("Font", sharedFontDict);
        }
        EnsureFont(page, "Helvetica", "F1");
        EnsureFont(page, "Helvetica-Bold", "F2");
        EnsureFont(page, "Helvetica-Oblique", "F3");
        EnsureFont(page, "Courier", "F4");
        // Standard-14 serif faces for the UA-default flow (Times-Roman/-Bold/-Italic):
        // serif output that embeds nothing, so a font-family-free document renders in
        // the browser default face without bloating the file or embedding a program.
        EnsureFont(page, "Times-Roman", "F5");
        EnsureFont(page, "Times-Bold", "F6");
        EnsureFont(page, "Times-Italic", "F7");
    }

    private static void EnsureFont(Page page, string baseFontName, string resName)
    {
        var resources = page.Dict.Get("Resources") as Core.PdfDictionary;
        if (resources is null)
        {
            resources = new Core.PdfDictionary();
            page.Dict.Set("Resources", resources);
        }
        var fontDict = resources.Get("Font") as Core.PdfDictionary;
        if (fontDict is null)
        {
            fontDict = new Core.PdfDictionary();
            resources.Set("Font", fontDict);
        }
        if (!fontDict.ContainsKey(resName))
        {
            var font = new Core.PdfDictionary();
            font.Set("Type", new Core.PdfName("Font"));
            font.Set("Subtype", new Core.PdfName("Type1"));
            font.Set("BaseFont", new Core.PdfName(baseFontName));
            font.Set("Encoding", new Core.PdfName("WinAnsiEncoding"));
            fontDict.Set(resName, font);
        }
    }
    private enum TokenKind { Text, Tag }

    // Quote-aware tag scan: an attribute VALUE may carry raw markup with '>'
    // inside its quotes (Angular popover payloads embed whole <div> trees) —
    // the tag ends at the first '>' OUTSIDE any quoted value. A REAL closing
    // quote is followed by whitespace, '/' or '>' — a quote whose "close"
    // lands mid-word (the typo'd `class="clearfix>` reaching into a later
    // tag's attribute) falls back to a plain character, so that tag still
    // ends at its '>' exactly as the legacy scan did, while legitimate
    // multi-line style values keep their spans.
    private static readonly Regex TagRx = new(
        @"<(/?)([A-Za-z][A-Za-z0-9]*)\s*((?:[^>""']|""[^""]*""(?=[\s/>])|'[^']*'(?=[\s/>])|[""'])*?)(/?)>",
        RegexOptions.Compiled);

    private static readonly Regex EscapedAttrRx = new(
        @"([A-Za-z_:][-A-Za-z0-9_:.]*)\s*(?:=\s*(\S+))?",
        RegexOptions.Compiled);

    private static readonly Regex AttrRx = new(
        "([A-Za-z_:][-A-Za-z0-9_:.]*)\\s*(?:=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s\">]+)))?",
        RegexOptions.Compiled);

}
