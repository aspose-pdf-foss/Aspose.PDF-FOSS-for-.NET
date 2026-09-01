using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Converters;

/// <summary>
/// Dedicated flow engine for the EDGAR "RRD ProFile" filing dialect: stylesheet-less
/// HTML whose layout lives entirely in inline styles — explicit
/// <c>page-break-before:always</c> paragraphs each followed by a beveled <c>&lt;hr&gt;</c>
/// and an <c>&lt;h5&gt;</c> "Table of Contents" backlink, ARIAL paragraphs with pt
/// margins, data tables whose first row of empty <c>&lt;td width="N%"&gt;</c> cells
/// declares the grid, literal <c>&amp;ltR&amp;gt</c> redline markers rendered as text,
/// <c>&lt;a name&gt;</c> anchors that become zero-area link annotations and
/// <c>&lt;a href="#..."&gt;</c> links that become GoTo/XYZ annotations.
///
/// The vertical model is a CSS line-box flow: a line box is R = round(winSum·sizePx/upm)
/// px tall, the baseline sits halfLead + winAsc·px below the box top
/// (halfLead = (R − winSum·sizePx/upm)/2), and the gap between the baselines of
/// consecutive blocks is descLB(prev) + borders + max(collapsed margins) + ascLB(next).
/// </summary>
internal static partial class EdgarHtmlRenderer
{
    // ── Gate ────────────────────────────────────────────────────────────────────

    /// <summary>The EDGAR filing shape: no stylesheet, several explicit page-break
    /// paragraphs each followed by the beveled rule + h5 backlink header, and inline
    /// ARIAL pt styling throughout.</summary>
    public static bool IsEdgarFilingDoc(string html)
    {
        if (html.IndexOf("<style", StringComparison.OrdinalIgnoreCase) >= 0) return false;
        if (Regex.Matches(html, @"<p\s+style=.page-break-before:always").Count < 3) return false;
        if (!Regex.IsMatch(html, @"<hr\s[^>]*size=""?3", RegexOptions.IgnoreCase)) return false;
        if (!Regex.IsMatch(html, @"<h5[^>]*>\s*<a\s+href=""#", RegexOptions.IgnoreCase)) return false;
        return Regex.IsMatch(html, @"font-family:ARIAL", RegexOptions.IgnoreCase);
    }

    // ── Geometry constants for this dialect ────────────────────────────────────

    const double PageH = 842.0;
    const double PageMargin = 90.0;      // left/right page margin
    const double BodyMargin = 6.0;       // 8px body margin at 0.75pt/px
    const double TopMargin = 72.0;       // page top margin (content td origin)
    const double BottomLimit = 770.0;    // td: flow may not cross 842-72
    const double H5Em = 9.96;            // h5 font-size 0.83em of 12pt
    const double H5Margin = 14.94;       // h5 UA margin: 1.5em
    const double HrBottomTd = 86.94;     // beveled rule strip bottom (explicit pages)

    // ── Entry ───────────────────────────────────────────────────────────────────

    public static Document? TryConvert(string html, HtmlLoadOptions? options)
    {
        try
        {
            var dom = ParseDom(html);
            var body = dom.Descendants().FirstOrDefault(n => n.Tag == "body") ?? dom;
            var eng = new Engine(options);
            return eng.Render(body, html);
        }
        catch
        {
            return null; // any structural surprise: fall back to the legacy flow
        }
    }

    // ── Mini DOM (p/tr/td auto-close; void elements) ────────────────────────────

    internal sealed class Node
    {
        public string Tag = "";              // "" for text nodes
        public string Text = "";
        public Dictionary<string, string>? Attrs;
        public List<Node> Children = new();
        public Node? Parent;

        public string Attr(string name) =>
            Attrs is not null && Attrs.TryGetValue(name, out var v) ? v : "";

        public IEnumerable<Node> Descendants()
        {
            foreach (var c in Children)
            {
                yield return c;
                foreach (var d in c.Descendants()) yield return d;
            }
        }
    }

    static readonly HashSet<string> Void = new(StringComparer.OrdinalIgnoreCase)
        { "hr", "br", "img", "meta", "link", "input", "col", "area", "base" };

    // Tags whose open auto-closes an open <p>
    static readonly HashSet<string> ClosesP = new(StringComparer.OrdinalIgnoreCase)
        { "p", "table", "tr", "td", "th", "hr", "h1", "h2", "h3", "h4", "h5", "h6", "ul", "ol", "li", "div", "center", "blockquote" };

    static readonly Regex TagRx = new(@"<(/?)([A-Za-z][A-Za-z0-9]*)((?:[^>""']|""[^""]*""|'[^']*')*?)(/?)>",
        RegexOptions.Compiled);
    static readonly Regex AttrRx = new(
        "([A-Za-z_:][-A-Za-z0-9_:.]*)\\s*(?:=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s\">]+)))?",
        RegexOptions.Compiled);

    internal static Node ParseDom(string html)
    {
        html = Regex.Replace(html, @"<!--.*?-->", "", RegexOptions.Singleline);
        var root = new Node { Tag = "#root" };
        var cur = root;
        int idx = 0;
        void AddText(int from, int to)
        {
            if (to <= from) return;
            var t = html.Substring(from, to - from);
            if (t.Length > 0)
                cur.Children.Add(new Node { Text = t, Parent = cur });
        }
        foreach (Match m in TagRx.Matches(html))
        {
            AddText(idx, m.Index);
            idx = m.Index + m.Length;
            var tag = m.Groups[2].Value.ToLowerInvariant();
            bool isClose = m.Groups[1].Value == "/";
            if (isClose)
            {
                for (var n = cur; n is not null && n != root; n = n.Parent)
                {
                    if (n.Tag == tag) { cur = n.Parent ?? root; break; }
                }
                continue;
            }
            // implicit closes
            if (ClosesP.Contains(tag))
            {
                for (var n = cur; n is not null && n != root; n = n.Parent)
                {
                    if (n.Tag == "p") { cur = n.Parent ?? root; break; }
                    if (n.Tag is "td" or "th" or "tr" or "table") break;
                }
            }
            if (tag is "tr" or "td" or "th")
            {
                for (var n = cur; n is not null && n != root; n = n.Parent)
                {
                    if (tag == "tr" && n.Tag is "tr" or "td" or "th") { cur = FindUp(n, "tr")?.Parent ?? n.Parent ?? root; break; }
                    if (tag is "td" or "th" && n.Tag is "td" or "th") { cur = n.Parent ?? root; break; }
                    if (n.Tag == "table") break;
                }
            }
            var el = new Node
            {
                Tag = tag,
                Attrs = ParseAttrs(m.Groups[3].Value),
                Parent = cur,
            };
            cur.Children.Add(el);
            if (m.Groups[4].Value != "/" && !Void.Contains(tag))
                cur = el;
        }
        AddText(idx, html.Length);
        return root;

        static Node? FindUp(Node n, string tag)
        {
            for (var x = n; x is not null; x = x.Parent)
                if (x.Tag == tag) return x;
            return null;
        }
    }

    static Dictionary<string, string>? ParseAttrs(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in AttrRx.Matches(s))
        {
            var name = m.Groups[1].Value;
            var val = m.Groups[2].Success ? m.Groups[2].Value
                : m.Groups[3].Success ? m.Groups[3].Value
                : m.Groups[4].Success ? m.Groups[4].Value : "";
            dict[name] = val;
        }
        return dict.Count > 0 ? dict : null;
    }

    // ── Entities (HTML5 legacy: &lt / &gt without ';' decode only when not
    //    followed by an alphanumeric — "&ltR&gt" renders as "&ltR>") ─────────────

    internal static string DecodeEntities(string s)
    {
        if (s.IndexOf('&') < 0) return s;
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] != '&') { sb.Append(s[i]); continue; }
            // numeric
            var m = Regex.Match(s.Substring(i, Math.Min(12, s.Length - i)), @"^&#(x?)([0-9A-Fa-f]+);?");
            if (m.Success && m.Groups[2].Length > 0)
            {
                int code = m.Groups[1].Length > 0
                    ? int.Parse(m.Groups[2].Value, NumberStyles.HexNumber)
                    : int.Parse(m.Groups[2].Value);
                // Windows-1252 mapping for the C1 range the filings use (&#147; etc.)
                sb.Append(Cp1252C1(code));
                i += m.Length - 1;
                continue;
            }
            // named
            var nm = Regex.Match(s.Substring(i, Math.Min(10, s.Length - i)), @"^&([A-Za-z]+)(;?)");
            if (nm.Success)
            {
                var name = nm.Groups[1].Value;
                bool hasSemi = nm.Groups[2].Value == ";";
                string? rep = name switch
                {
                    "nbsp" => " ",
                    "amp" => "&",
                    "quot" => "\"",
                    "bull" => "•",
                    "mdash" => "—",
                    "ndash" => "–",
                    "copy" => "©",
                    "reg" => "®",
                    _ => null,
                };
                // legacy no-semicolon forms: &lt / &gt / &amp / &nbsp decode unless
                // the next char is alphanumeric or '='
                if (rep is null && name.StartsWith("lt")) { name = "lt"; rep = "<"; }
                if (rep is null && name.StartsWith("gt")) { name = "gt"; rep = ">"; }
                if (rep is not null)
                {
                    int consumed = name.Length + (hasSemi ? 1 : 0);
                    if (!hasSemi)
                    {
                        // legacy rule: only a prefix match; next char must not be alnum/=
                        int after = i + 1 + name.Length;
                        if (after < s.Length && (char.IsLetterOrDigit(s[after]) || s[after] == '='))
                        {
                            sb.Append('&');
                            continue;
                        }
                    }
                    sb.Append(rep);
                    i += consumed; // '&' + name (+';')
                    continue;
                }
            }
            sb.Append('&');
        }
        return sb.ToString();
    }

    // cp1252 mapping for the C1 range (&#145;-&#153; smart punctuation etc.)
    static readonly string[] C1Map =
    {
        "\u20AC", "\u0081", "\u201A", "\u0192", "\u201E", "\u2026", "\u2020", "\u2021", "\u02C6", "\u2030", "\u0160", "\u2039", "\u0152", "\u008D", "\u017D", "\u008F", "\u0090", "\u2018", "\u2019", "\u201C", "\u201D", "\u2022", "\u2013", "\u2014", "\u02DC", "\u2122", "\u0161", "\u203A", "\u0153", "\u009D", "\u017E", "\u0178",
        // (cp1252 C1 punctuation, ASCII-escaped)
    };

    static string Cp1252C1(int code)
    {
        if (code is >= 0x80 and <= 0x9F) return C1Map[code - 0x80];
        try { return char.ConvertFromUtf32(code); } catch { return ""; }
    }

    // ── Faces & metrics ─────────────────────────────────────────────────────────

    internal sealed class Face
    {
        public string Display = "";                 // "Arial Bold" → BaseFont "ArialBold"
        public byte[] Ttf = Array.Empty<byte>();
        public Text.GlyphOutlineParser Parser = null!;
        public double Upm, WinAsc, WinDesc;
        public double WinSum => WinAsc + WinDesc;

        public double Adv(int cp, double size)
        {
            var gid = Parser.CMap.TryGetValue(cp, out var g) ? g : 0;
            if (gid == 0) return 0.5 * size;
            return Math.Round(Parser.GetAdvanceWidth(gid) * 1000.0 / Upm) * size / 1000.0;
        }

        public int Gid(int cp) => Parser.CMap.TryGetValue(cp, out var g) ? g : 0;

        /// <summary>Kern adjustment (pt) applied between two chars.</summary>
        public double Kern(int cpL, int cpR, double size)
        {
            var k = Parser.GetKernAdjustment(Gid(cpL), Gid(cpR));
            return k == 0 ? 0 : k * 1000.0 / Upm * size / 1000.0;
        }

        public double Measure(string s, double size)
        {
            double w = 0;
            for (int i = 0; i < s.Length; i++)
            {
                w += Adv(s[i], size);
                if (i > 0) w += Kern(s[i - 1], s[i], size);
            }
            return w;
        }
    }

    static readonly Dictionary<string, Face?> _faces = new(StringComparer.OrdinalIgnoreCase);

    internal static Face? GetFace(string family, bool bold, bool italic)
    {
        var fam = family.ToLowerInvariant() switch
        {
            "arial" or "helvetica" => "Arial",
            "wingdings" => "Wingdings",
            _ => "Times New Roman",
        };
        var name = fam;
        if (bold && italic) name += " Bold Italic";
        else if (bold) name += " Bold";
        else if (italic) name += " Italic";
        if (fam == "Wingdings") name = fam;
        if (_faces.TryGetValue(name, out var cached)) return cached;
        Face? face = null;
        try
        {
            var ttf = Text.FontRepository.GetTtfData(name);
            if (ttf is not null)
            {
                var tp = new Text.TrueTypeParser(ttf);
                tp.Parse();
                if (tp.UnitsPerEm > 0 && tp.UsWinAscent > 0)
                {
                    face = new Face
                    {
                        Display = name,
                        Ttf = ttf,
                        Parser = new Text.GlyphOutlineParser(ttf),
                        Upm = tp.UnitsPerEm,
                        WinAsc = tp.UsWinAscent,
                        WinDesc = tp.UsWinDescent,
                    };
                }
            }
        }
        catch { }
        _faces[name] = face;
        return face;
    }

    /// <summary>Line-box metrics: (pitch, ascLB, descLB) in pt for a face/size,
    /// optionally with a CSS line-height override (pt).</summary>
    internal static (double pitch, double asc, double desc) LineBox(Face f, double size, double? lineHeightPt = null)
    {
        var px = size / 0.75;
        var raw = f.WinSum * px / f.Upm;
        // A "normal" line box is font-INDEPENDENT: round(1.15·sizePx) px
        // (holds up to 120pt, where it diverges from the win-box round). The
        // half-leading still splits the difference to the font's win box.
        double R = lineHeightPt is { } lh
            ? Math.Round(lh / 0.75, MidpointRounding.AwayFromZero)
            : Math.Round(1.15 * px, MidpointRounding.AwayFromZero);
        var halfLead = (R - raw) / 2;
        var asc = 0.75 * (halfLead + f.WinAsc * px / f.Upm);
        var pitch = 0.75 * R;
        return (pitch, asc, pitch - asc);
    }

    // ── Styles ──────────────────────────────────────────────────────────────────

    internal sealed class Style
    {
        public string Family = "Times New Roman";
        public double Size = 12.0;
        public bool Bold, Italic, Sup;
        public int Color;                    // 0xRRGGBB
        public double MarginTop, MarginBottom, MarginLeft;
        public double TextIndent;
        public double? LineHeight;
        public double BorderTopW, BorderBottomW;
        public int BorderTopColor, BorderBottomColor;
        public bool PageBreakBefore;
        public string Align = "";            // left/center/right
        public Style Clone() => (Style)MemberwiseClone();
    }

    internal static void ApplyStyleAttr(string styleText, Style s)
    {
        foreach (var decl in styleText.Split(';'))
        {
            var kv = decl.Split(':', 2);
            if (kv.Length != 2) continue;
            var prop = kv[0].Trim().ToLowerInvariant();
            var val = kv[1].Trim();
            switch (prop)
            {
                case "font-size": if (TryLen(val, s.Size, out var fs)) s.Size = fs; break;
                case "font-family": s.Family = val.Split(',')[0].Trim().Trim('"', '\''); break;
                case "font-weight": s.Bold = val.StartsWith("bold", StringComparison.OrdinalIgnoreCase) || val == "700"; break;
                case "font-style": s.Italic = val.StartsWith("italic", StringComparison.OrdinalIgnoreCase); break;
                case "margin-top": if (TryLen(val, s.Size, out var mt)) s.MarginTop = mt; break;
                case "margin-bottom": if (TryLen(val, s.Size, out var mb)) s.MarginBottom = mb; break;
                case "margin-left": if (TryLen(val, s.Size, out var ml)) s.MarginLeft = ml; break;
                case "text-indent": if (TryLen(val, s.Size, out var ti)) s.TextIndent = ti; break;
                case "line-height": if (TryLen(val, s.Size, out var lh2)) s.LineHeight = lh2; break;
                case "page-break-before": if (val.Equals("always", StringComparison.OrdinalIgnoreCase)) s.PageBreakBefore = true; break;
                case "color": if (TryColor(val, out var c)) s.Color = c; break;
                case "border-top": ParseBorder(val, out s.BorderTopW, out s.BorderTopColor); break;
                case "border-bottom": ParseBorder(val, out s.BorderBottomW, out s.BorderBottomColor); break;
                case "text-align": s.Align = val.ToLowerInvariant(); break;
            }
        }
    }

    static void ParseBorder(string val, out double w, out int color)
    {
        w = 0; color = 0;
        var m = Regex.Match(val, @"([\d.]+)\s*(px|pt)");
        if (m.Success)
        {
            w = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            if (m.Groups[2].Value == "px") w *= 0.75;
        }
        var c = Regex.Match(val, @"#([0-9A-Fa-f]{6})");
        if (c.Success) color = int.Parse(c.Groups[1].Value, NumberStyles.HexNumber);
    }

    internal static bool TryLen(string val, double em, out double pt)
    {
        pt = 0;
        var m = Regex.Match(val.Trim(), @"^(-?[\d.]+)\s*(pt|px|em|%)?$");
        if (!m.Success) return false;
        var num = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        pt = m.Groups[2].Value switch
        {
            "px" => num * 0.75,
            "em" => num * em,
            "%" => num / 100.0 * em,
            _ => num,
        };
        return true;
    }

    static bool TryColor(string val, out int color)
    {
        color = 0;
        var m = Regex.Match(val.Trim(), @"^#([0-9A-Fa-f]{6})$");
        if (m.Success) { color = int.Parse(m.Groups[1].Value, NumberStyles.HexNumber); return true; }
        switch (val.Trim().ToLowerInvariant())
        {
            case "black": color = 0; return true;
            case "white": color = 0xFFFFFF; return true;
            case "red": color = 0xFF0000; return true;
            case "blue": color = 0x0000FF; return true;
        }
        return false;
    }

    // ── Inline runs ─────────────────────────────────────────────────────────────

    internal sealed class Run
    {
        public string Text = "";
        public Face Face = null!;
        public double Size;
        public int Color;
        public bool Sup;
        public int LinkId = -1;              // index into engine.Links
        public List<int>? AnchorsBefore;     // anchor ids planted at this run's start
    }

    // ── Engine ──────────────────────────────────────────────────────────────────

    sealed class PlacedRun
    {
        public double X, BaselineTd;
        public Run Run = null!;
    }

    sealed class RectFill
    {
        public double X, TopTd, W, H;
        public int Color;
        public bool Stroke;                  // line (stroke) vs fill
        public double LineW;
    }

    sealed class PlacedImage
    {
        public double X, TopTd, W, H;
        public byte[] Data = null!;
    }

    sealed class PageOut
    {
        public List<PlacedRun> Runs = new();
        public List<RectFill> Rects = new();
        public List<PlacedImage> Images = new();
        public List<(int annotOrder, int linkId, double llx, double lly, double urx, double ury)> LinkRects = new();
        public List<(int annotOrder, int anchorId, double x, double y)> AnchorPoints = new();
        public bool Explicit;                // starts with the hr+h5 header
    }

    sealed class LinkInfo
    {
        public string TargetName = "";       // href="#name"
    }

    sealed class AnchorInfo
    {
        public string Name = "";
        public int PageIdx = -1;
        public double TopPdf;                // true baseline of the anchor's line (PDF coords)
    }

    sealed partial class Engine
    {
        readonly HtmlLoadOptions? _options;
        public List<LinkInfo> Links = new();
        public List<AnchorInfo> Anchors = new();
        public Dictionary<string, int> AnchorIdx = new(StringComparer.Ordinal);

        List<PageOut> _pages = new();
        PageOut _pg = null!;
        double _y;                            // td cursor: bottom of previous block's line box
        double _prevBorderBottom;
        List<double> _margins = new();
        bool _atPageTop;
        bool _dropTopMargins;                 // page 1 only: top margins collapse into the body top
        int _annotSeq;
        double _pageW, _contentW;
        double _tableX = 96;                  // current table left origin (centered narrow tables shift it)

        public Engine(HtmlLoadOptions? options) { _options = options; }

        // ——— structure walk ————————————————————————————————————————————

        public Document? Render(Node body, string html)
        {
            var arial = GetFace("Arial", false, false);
            var tnr = GetFace("Times New Roman", false, false);
            if (arial is null || tnr is null) return null;

            // Page width: grown so the widest table's min-content fits. The
            // growth rule is pageW = Σcolmin + 186 (the driver table then renders
            // at Σcolmin, eating 6pt of the right body padding); the content box
            // stays pageW − 192.
            var widest = MeasureWidestTable(body);
            _pageW = Math.Max(595.0, widest + 186.0);
            _contentW = _pageW - 192.0;

            StartPage(explicitHeader: false, first: true);

            var bodyStyle = new Style();
            FlowChildren(body, bodyStyle);

            return Emit();
        }

        void StartPage(bool explicitHeader, bool first = false)
        {
            _pg = new PageOut { Explicit = explicitHeader };
            _pages.Add(_pg);
            _y = first ? TopMargin + BodyMargin : TopMargin;
            _dropTopMargins = first;
            _prevBorderBottom = 0;
            _margins.Clear();
            _atPageTop = true;
        }

        void EndBlock(double marginBottom, double borderBottom)
        {
            _margins.Add(marginBottom);
            _prevBorderBottom = borderBottom;
        }

        void BreakPage(bool explicitHeader)
        {
            StartPage(explicitHeader);
        }

        int GetLinkId(string target)
        {
            Links.Add(new LinkInfo { TargetName = target });
            return Links.Count - 1;
        }

        void AddLinkRect(int linkId, double x0, double baselineTd, double x1, Face f, double size)
        {
            var px = size / 0.75;
            var asc = 0.75 * f.WinAsc * px / f.Upm;
            var desc = 0.75 * f.WinDesc * px / f.Upm;
            var yBase = PageH - baselineTd;
            _pg.LinkRects.Add((_annotSeq++, linkId, x0, yBase - desc, x1, yBase + asc));
        }

        // ——— flow ————————————————————————————————————————————————————

        void FlowChildren(Node parent, Style inherited)
        {
            var pendingText = new List<Node>();
            foreach (var child in parent.Children)
            {
                if (child.Tag == "" || IsInlineTag(child.Tag))
                {
                    pendingText.Add(child);
                    continue;
                }
                FlushAnonymous(pendingText, inherited);
                FlowBlock(child, inherited);
            }
            FlushAnonymous(pendingText, inherited);
        }

        static bool IsInlineTag(string t) =>
            t is "b" or "i" or "u" or "em" or "strong" or "font" or "a" or "sup" or "sub" or "span" or "small" or "big" or "br" or "img" or "nobr";

        void FlushAnonymous(List<Node> nodes, Style inherited)
        {
            if (nodes.Count == 0) return;
            var wrapper = new Node { Tag = "p" };
            wrapper.Children.AddRange(nodes);
            var st = inherited.Clone();
            st.MarginTop = 0; st.MarginBottom = 0; st.MarginLeft = 0; st.TextIndent = 0;
            st.LineHeight = null; st.Align = "";
            LayoutParagraph(wrapper, st, anonymous: true);
            nodes.Clear();
        }

        void FlowBlock(Node el, Style inherited)
        {
            switch (el.Tag)
            {
                case "p":
                case "h5":
                {
                    var st = inherited.Clone();
                    st.MarginTop = 0; st.MarginBottom = 0; st.MarginLeft = 0; st.TextIndent = 0;
                    st.LineHeight = null; st.BorderTopW = 0; st.BorderBottomW = 0;
                    st.Align = el.Attr("align").ToLowerInvariant();
                    if (el.Tag == "h5")
                    {
                        st.Size = H5Em; st.Bold = true;
                        st.MarginTop = H5Margin; st.MarginBottom = H5Margin;
                    }
                    ApplyStyleAttr(el.Attr("style"), st);
                    if (st.PageBreakBefore)
                    {
                        // EDGAR page break: the break-para is empty; its 1em UA
                        // margins collapse through onto the new page's top.
                        BreakPage(explicitHeader: true);
                        _margins.Add(12.0);
                        return;
                    }
                    LayoutParagraph(el, st, anonymous: false);
                    break;
                }
                case "hr":
                    LayoutHr();
                    return;
                case "table":
                    LayoutTable(el, inherited);
                    break;
                case "div":
                case "center":
                    FlowChildren(el, inherited);
                    break;
                default:
                    FlowChildren(el, inherited);
                    break;
            }
        }

        void LayoutHr()
        {
            // beveled 3px rule: box height 2.94, strokes inside
            double top;
            if (_atPageTop)
            {
                top = _y + (_dropTopMargins ? 0 : (_margins.Count > 0 ? _margins.Max() : 0));
                _atPageTop = false;
            }
            else
            {
                top = _y + _prevBorderBottom + (_margins.Count > 0 ? _margins.Max() : 0);
            }
            _margins.Clear();
            _prevBorderBottom = 0;
            _pg.Rects.Add(new RectFill { X = 96, TopTd = top + 1.815, W = _contentW, H = 0, Color = 0x666666, Stroke = true, LineW = 0.75 });
            _pg.Rects.Add(new RectFill { X = 96, TopTd = top + 2.565, W = _contentW, H = 0, Color = 0xBBBBBB, Stroke = true, LineW = 0.75 });
            _y = top + 2.94;
            EndBlock(6.0, 0);
        }

        // ——— inline collection ————————————————————————————————————————

        // ——— paragraph layout ————————————————————————————————————————

        internal sealed class Piece
        {
            public string Text = "";
            public double W;
            public Run Run = null!;
        }

        void LayoutImageBlock(Node img, Style st)
        {
            var src = img.Attr("src");
            byte[]? data = null;
            try
            {
                var basePath = _options?.BasePath ?? "";
                var path = System.IO.Path.Combine(basePath, src);
                if (System.IO.File.Exists(path)) data = System.IO.File.ReadAllBytes(path);
            }
            catch { }
            if (data is null) return;
            if (!TryJpegSize(data, out var wPx, out var hPx)) { wPx = 100; hPx = 40; }
            double w = wPx * 0.75, h = hPx * 0.75;

            double top;
            if (_atPageTop) { _margins.Add(st.MarginTop); top = _y + (_dropTopMargins ? 0 : (_margins.Count > 0 ? _margins.Max() : 0)); _atPageTop = false; }
            else
            {
                _margins.Add(st.MarginTop);
                top = _y + _prevBorderBottom + _margins.Max();
            }
            _margins.Clear();
            if (top + h > BottomLimit)
            {
                BreakPage(false);
                top = _y;
                _atPageTop = false;
            }
            double x = 96;
            if (st.Align == "center") x = 96 + (_contentW - w) / 2;
            else if (st.Align == "right") x = 96 + _contentW - w;
            _pg.Images.Add(new PlacedImage { X = x, TopTd = top, W = w, H = h, Data = data });
            _y = top + h;
            EndBlock(st.MarginBottom, 0);
        }

        static bool TryJpegSize(byte[] d, out int w, out int h)
        {
            w = h = 0;
            if (d.Length < 4 || d[0] != 0xFF || d[1] != 0xD8) return false;
            int i = 2;
            while (i + 9 < d.Length)
            {
                if (d[i] != 0xFF) { i++; continue; }
                var marker = d[i + 1];
                if (marker == 0xD8 || (marker >= 0xD0 && marker <= 0xD9)) { i += 2; continue; }
                int len = (d[i + 2] << 8) | d[i + 3];
                if (marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
                {
                    h = (d[i + 5] << 8) | d[i + 6];
                    w = (d[i + 7] << 8) | d[i + 8];
                    return true;
                }
                i += 2 + len;
            }
            return false;
        }

        // ——— tables (v1) ————————————————————————————————————————————————

        // ——— tables: column-width algorithm ———————————————————————————————
        //
        // Per column: min/max content widths (nbsp runs unbreakable, hanging paras
        // min = line-indent + longest word, explicit <p width:Xpt> forces min=max=X).
        // Colspan (min m, max M) over cols S: m > Σmin(S) → min_i = m·max_i/Σmax(S)
        // (equal split when Σmax=0); M > Σmax(S) → max_i likewise. Distribution at
        // table width W: pct claims p′·W (p′ cumulative-capped at 100 L→R);
        // need_i = max(0, claim−min); B = W−Σmin; over-constrained → pro-rata by
        // need; then autos fill min→max the same way; surplus → autos ∝ max
        // (equal when all maxes 0; to pct cols ∝ p′ when no autos).
        // Page growth: pageW = max(595, maxOverTables(Σcolmin) + 186).

        sealed class ColModel
        {
            public double[] Min = null!, Max = null!, Pct = null!;
            public int NCols;
        }

        static int ParseIntAttr(Node td, string name)
        {
            return int.TryParse(td.Attr(name), out var v) ? v : 1;
        }

        // One laid-out line inside a cell: offset (of the line box TOP) from the
        // cell's content top, its metrics, pieces, and drawing style.
        sealed class CellLine
        {
            public double Top;               // from cell top
            public double Asc, Desc;
            public double BorderTopW;        // drawn above this line's box
            public int BorderTopColor;
            public double BorderBottomW;     // drawn below (block bottom)
            public int BorderBottomColor;
            public List<Piece> Pieces = new();
            public bool FirstLine;
            public Style St = null!;
        }

        sealed class CellFlow
        {
            public List<CellLine> Lines = new();
            public double Height;            // content stack height
            public int Col, Span;
            public string Valign = "";
            public double X0, Width;
        }

        static bool RunHasInk(Run r) => r.Text.Trim(' ', (char)0xA0).Length > 0;

        // ——— emission ————————————————————————————————————————————————

    }
}
