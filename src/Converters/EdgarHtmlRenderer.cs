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
internal static class EdgarHtmlRenderer
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

    sealed class Engine
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

        // Advance the cursor for a new line box; returns its baseline (td).
        double PlaceLineBox(double asc, double desc, double marginTop, double borderTop)
        {
            double top;
            if (_atPageTop)
            {
                // margins collapse into the body top only on the very first page;
                // pages after a forced break keep the pending margins.
                _margins.Add(marginTop);
                top = _y + (_dropTopMargins ? 0 : _margins.Max()) + borderTop;
                _atPageTop = false;
            }
            else
            {
                _margins.Add(marginTop);
                top = _y + _prevBorderBottom + _margins.Max() + borderTop;
            }
            _margins.Clear();
            var baseline = top + asc;
            _y = baseline + desc;

            return baseline;
        }

        void EndBlock(double marginBottom, double borderBottom)
        {
            _margins.Add(marginBottom);
            _prevBorderBottom = borderBottom;
        }

        bool Fits(double asc, double desc, double marginTop, double borderTop)
        {
            if (_atPageTop) return true;
            var top = _y + _prevBorderBottom + Math.Max(_margins.Count > 0 ? _margins.Max() : 0, marginTop) + borderTop;
            return top + asc + desc <= BottomLimit + 0.01;
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

        void AddRun(Run r, double x, double baselineTd)
        {
            _pg.Runs.Add(new PlacedRun { X = x, BaselineTd = baselineTd, Run = r });
            if (r.AnchorsBefore is not null)
            {
                foreach (var aid in r.AnchorsBefore)
                {
                    var a = Anchors[aid];
                    if (a.PageIdx < 0)
                    {
                        a.PageIdx = _pages.Count - 1;
                        a.TopPdf = PageH - baselineTd;
                        _pg.AnchorPoints.Add((_annotSeq++, aid, 96, PageH - baselineTd));
                    }
                }
                r.AnchorsBefore = null;
            }
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

        List<Run> CollectRuns(Node el, Style blockStyle, List<int>? pendingAnchors = null)
        {
            var runs = new List<Run>();
            pendingAnchors ??= new List<int>();
            Collect(el, blockStyle, runs, pendingAnchors, -1);
            // whitespace collapse across the whole paragraph
            CollapseWs(runs);
            return runs;
        }

        void Collect(Node n, Style st, List<Run> runs, List<int> pendingAnchors, int linkId)
        {
            foreach (var c in n.Children)
            {
                if (c.Tag == "")
                {
                    var text = DecodeEntities(c.Text);
                    if (text.Length == 0) continue;
                    var face = GetFace(st.Family, st.Bold, st.Italic);
                    if (face is null) continue;
                    var r = new Run
                    {
                        Text = text,
                        Face = face,
                        Size = st.Sup ? Math.Round(st.Size * 0.85, 2) : st.Size,
                        Color = st.Color,
                        Sup = st.Sup,
                        LinkId = linkId,
                    };
                    if (pendingAnchors.Count > 0 && text.Trim().Length > 0)
                    {
                        r.AnchorsBefore = new List<int>(pendingAnchors);
                        pendingAnchors.Clear();
                    }
                    runs.Add(r);
                    continue;
                }
                switch (c.Tag)
                {
                    case "b" or "strong":
                    {
                        var s2 = st.Clone(); s2.Bold = true;
                        ApplyStyleAttr(c.Attr("style"), s2);
                        Collect(c, s2, runs, pendingAnchors, linkId);
                        break;
                    }
                    case "i" or "em":
                    {
                        var s2 = st.Clone(); s2.Italic = true;
                        ApplyStyleAttr(c.Attr("style"), s2);
                        Collect(c, s2, runs, pendingAnchors, linkId);
                        break;
                    }
                    case "sup":
                    {
                        var s2 = st.Clone(); s2.Sup = true;
                        Collect(c, s2, runs, pendingAnchors, linkId);
                        break;
                    }
                    case "font":
                    {
                        var s2 = st.Clone();
                        var color = c.Attr("color");
                        if (color.Length > 0 && TryColor(color, out var cc)) s2.Color = cc;
                        var sizeAttr = c.Attr("size");
                        if (sizeAttr.Length > 0 && int.TryParse(sizeAttr, out var hsz))
                            s2.Size = hsz switch { 1 => 7.5, 2 => 10, 3 => 12, 4 => 13.5, 5 => 18, 6 => 24, 7 => 36, _ => s2.Size };
                        ApplyStyleAttr(c.Attr("style"), s2);
                        Collect(c, s2, runs, pendingAnchors, linkId);
                        break;
                    }
                    case "a":
                    {
                        var nameAttr = c.Attr("name");
                        if (nameAttr.Length > 0)
                        {
                            if (!AnchorIdx.ContainsKey(nameAttr))
                            {
                                Anchors.Add(new AnchorInfo { Name = nameAttr });
                                AnchorIdx[nameAttr] = Anchors.Count - 1;
                            }
                            pendingAnchors.Add(AnchorIdx[nameAttr]);
                        }
                        var href = c.Attr("href");
                        var lid = linkId;
                        if (href.StartsWith("#")) lid = GetLinkId(href.Substring(1));
                        Collect(c, st, runs, pendingAnchors, lid);
                        break;
                    }
                    case "br":
                        runs.Add(new Run { Text = "\n", Face = GetFace(st.Family, st.Bold, st.Italic)!, Size = st.Size });
                        break;
                    case "img":
                        // inline images: none in this dialect outside block paragraphs
                        break;
                    default:
                        Collect(c, st, runs, pendingAnchors, linkId);
                        break;
                }
            }
        }

        static void CollapseWs(List<Run> runs)
        {
            bool prevSpace = true; // leading whitespace collapses away
            foreach (var r in runs)
            {
                if (r.Text == "\n") { prevSpace = true; continue; }
                var sb = new StringBuilder(r.Text.Length);
                foreach (var ch in r.Text)
                {
                    if (ch is ' ' or '\t' or '\r' or '\n')
                    {
                        if (!prevSpace) sb.Append(' ');
                        prevSpace = true;
                    }
                    else
                    {
                        sb.Append(ch);
                        prevSpace = ch == ' ' ? false : false;
                    }
                }
                r.Text = sb.ToString();
            }
            // trailing whitespace of the block collapses away
            for (int i = runs.Count - 1; i >= 0; i--)
            {
                if (runs[i].Text == "\n") continue;
                runs[i].Text = runs[i].Text.TrimEnd(' ');
                if (runs[i].Text.Length > 0) break;
            }
            runs.RemoveAll(r => r.Text.Length == 0 && r.AnchorsBefore is null);
        }

        // ——— paragraph layout ————————————————————————————————————————

        void LayoutParagraph(Node el, Style st, bool anonymous)
        {
            // block-level image paragraph?
            var imgs = el.Children.Where(c => c.Tag == "img").ToList();
            var runs = CollectRuns(el, st);
            bool hasText = runs.Any(r => r.Text.Trim(' ', ' ').Length > 0);
            if (imgs.Count > 0 && !hasText)
            {
                LayoutImageBlock(imgs[0], st);
                return;
            }

            if (runs.Count == 0 || runs.All(r => r.Text.Length == 0))
            {
                // empty paragraph: no line box, margins pass through (collapse)
                if (!anonymous)
                {
                    _margins.Add(st.MarginTop);
                    _margins.Add(st.MarginBottom);
                }
                return;
            }

            LayoutRuns(runs, st);
        }

        void LayoutRuns(List<Run> runs, Style st)
        {
            // dominant face/size for line metrics: the largest (size, then asc)
            var metricRun = runs.Where(r => r.Text.Length > 0).OrderByDescending(r => r.Size).First();
            var (pitch, asc, desc) = LineBox(metricRun.Face, metricRun.Size, st.LineHeight);

            var x0 = 96 + st.MarginLeft;
            var width = _contentW - st.MarginLeft;
            var firstIndent = st.TextIndent;

            // build word stream: (text, run) glyph clusters split at breakable spaces
            var lines = WrapRuns(runs, width, firstIndent);

            for (int li = 0; li < lines.Count; li++)
            {
                if (!Fits(asc, desc, li == 0 ? st.MarginTop : 0, li == 0 ? st.BorderTopW : 0))
                    BreakPage(explicitHeader: false);
                var baseline = PlaceLineBox(asc, desc, li == 0 ? st.MarginTop : 0, li == 0 ? st.BorderTopW : 0);
                double lineW = lines[li].Sum(p => p.W);
                double x = x0 + (li == 0 ? firstIndent : 0);
                if (st.Align == "center") x = x0 + (width - lineW) / 2;
                else if (st.Align == "right") x = x0 + width - lineW;

                foreach (var piece in lines[li])
                {
                    var r = piece.Run;
                    var supRaise = r.Sup ? 1.26 : 0;
                    AddRun(new Run { Text = piece.Text, Face = r.Face, Size = r.Size, Color = r.Color, Sup = r.Sup, LinkId = r.LinkId, AnchorsBefore = r.AnchorsBefore },
                        x, baseline - supRaise);
                    r.AnchorsBefore = null;
                    if (r.LinkId >= 0 && piece.Text.Trim(' ', (char)0xA0).Length > 0)
                        AddLinkRect(r.LinkId, x, baseline, x + piece.W, r.Face, r.Size);
                    x += piece.W;
                }
            }

            // border-bottom line under the block
            if (st.BorderBottomW > 0)
            {
                _pg.Rects.Add(new RectFill
                {
                    X = x0, TopTd = _y + st.BorderBottomW / 2, W = width, H = 0,
                    Color = st.BorderBottomColor, Stroke = true, LineW = st.BorderBottomW,
                });
            }
            EndBlock(st.MarginBottom, st.BorderBottomW);
        }

        internal sealed class Piece
        {
            public string Text = "";
            public double W;
            public Run Run = null!;
        }

        List<List<Piece>> WrapRuns(List<Run> runs, double width, double firstIndent)
        {
            var lines = new List<List<Piece>>();
            var line = new List<Piece>();
            double lineW = 0;
            double avail = width - firstIndent;

            void EndLine()
            {
                if (line.Count > 0) lines.Add(line);
                line = new List<Piece>();
                lineW = 0;
                avail = width;
            }

            foreach (var run in runs)
            {
                if (run.Text == "\n") { EndLine(); continue; }
                int i = 0;
                var text = run.Text;
                while (i < text.Length)
                {
                    // segment = leading space + word (unbreakable incl. nbsp)
                    int j = i;
                    if (text[j] == ' ') j++;
                    while (j < text.Length && text[j] != ' ') j++;
                    var seg = text.Substring(i, j - i);
                    var segW = run.Face.Measure(seg, run.Size);
                    if (line.Count > 0 && lineW + segW > avail + 1e-6 && seg.Trim(' ').Length > 0)
                    {
                        EndLine();
                        var trimmed = seg.TrimStart(' ');
                        AddPiece(trimmed, run);
                    }
                    else
                    {
                        AddPiece(seg, run);
                    }
                    i = j;
                }
            }
            EndLine();
            return lines;

            void AddPiece(string s, Run run)
            {
                if (s.Length == 0) return;
                var w = run.Face.Measure(s, run.Size);
                if (line.Count > 0 && line[^1].Run == run)
                {
                    // merge; re-measure across the boundary for kerning continuity
                    var merged = line[^1].Text + s;
                    var mw = run.Face.Measure(merged, run.Size);
                    lineW += mw - line[^1].W;
                    line[^1].Text = merged;
                    line[^1].W = mw;
                }
                else
                {
                    var piece = new Piece { Text = s, W = w, Run = run };
                    line.Add(piece);
                    lineW += w;
                }
            }
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

        ColModel BuildColModel(Node table, List<Node> rows, int nCols, Style tableStyle)
        {
            var m = new ColModel
            {
                Min = new double[nCols],
                Max = new double[nCols],
                Pct = new double[nCols],
                NCols = nCols,
            };
            // pass 1: single-span cells
            foreach (var tr in rows)
            {
                var trStyle = tableStyle.Clone();
                ApplyStyleAttr(tr.Attr("style"), trStyle);
                int col = 0;
                foreach (var td in tr.Children.Where(x => x.Tag is "td" or "th"))
                {
                    int span = Math.Max(1, ParseIntAttr(td, "colspan"));
                    var wAttr = td.Attr("width");
                    if (wAttr.EndsWith("%") && double.TryParse(wAttr.TrimEnd('%'), out var p))
                        m.Pct[col] = Math.Max(m.Pct[col], p);
                    if (span == 1)
                    {
                        var cellStyle = trStyle.Clone();
                        ApplyStyleAttr(td.Attr("style"), cellStyle);
                        var (mn, mx) = CellContentWidths(td, cellStyle);
                        m.Min[col] = Math.Max(m.Min[col], mn);
                        m.Max[col] = Math.Max(m.Max[col], mx);
                    }
                    col += span;
                }
            }
            // pass 2: colspans replace spanned mins/maxes proportionally to MAX
            foreach (var tr in rows)
            {
                var trStyle = tableStyle.Clone();
                ApplyStyleAttr(tr.Attr("style"), trStyle);
                int col = 0;
                foreach (var td in tr.Children.Where(x => x.Tag is "td" or "th"))
                {
                    int span = Math.Max(1, ParseIntAttr(td, "colspan"));
                    if (span > 1)
                    {
                        var cellStyle = trStyle.Clone();
                        ApplyStyleAttr(td.Attr("style"), cellStyle);
                        var (mn, mx) = CellContentWidths(td, cellStyle);
                        int hi = Math.Min(col + span, nCols);
                        double sMin = 0, sMax = 0;
                        for (int i = col; i < hi; i++) { sMin += m.Min[i]; sMax += m.Max[i]; }
                        if (mn > sMin)
                        {
                            for (int i = col; i < hi; i++)
                                m.Min[i] = sMax > 0 ? mn * m.Max[i] / sMax : mn / (hi - col);
                        }
                        if (mx > sMax)
                        {
                            for (int i = col; i < hi; i++)
                                m.Max[i] = sMax > 0 ? mx * m.Max[i] / sMax : mx / (hi - col);
                        }
                        for (int i = col; i < hi; i++)
                            if (m.Max[i] < m.Min[i]) m.Max[i] = m.Min[i];
                    }
                    col += span;
                }
            }
            return m;
        }

        double[] DistributeColumns(ColModel m, double W)
        {
            int n = m.NCols;
            var colW = new double[n];
            // cumulative-capped pct claims, left to right
            var claim = new double[n];
            double running = 0;
            for (int i = 0; i < n; i++)
            {
                if (m.Pct[i] > 0)
                {
                    var eff = Math.Min(m.Pct[i], Math.Max(0, 100 - running));
                    running += eff;
                    claim[i] = eff / 100.0 * W;
                }
            }
            double sumMin = m.Min.Sum();
            double B = W - sumMin;
            if (B <= 0)
            {
                for (int i = 0; i < n; i++) colW[i] = m.Min[i];
                return colW;
            }
            // stage 1: pct fill
            var need = new double[n];
            double sumNeed = 0;
            for (int i = 0; i < n; i++)
            {
                if (m.Pct[i] > 0) { need[i] = Math.Max(0, claim[i] - m.Min[i]); sumNeed += need[i]; }
            }
            if (sumNeed > B)
            {
                for (int i = 0; i < n; i++)
                    colW[i] = m.Min[i] + (m.Pct[i] > 0 ? B * need[i] / sumNeed : 0);
                return colW;
            }
            for (int i = 0; i < n; i++)
                colW[i] = m.Pct[i] > 0 ? Math.Max(m.Min[i], claim[i]) : m.Min[i];
            B -= sumNeed;
            // stage 2: auto fill toward max
            var needA = new double[n];
            double sumNeedA = 0;
            for (int i = 0; i < n; i++)
            {
                if (m.Pct[i] <= 0) { needA[i] = Math.Max(0, m.Max[i] - m.Min[i]); sumNeedA += needA[i]; }
            }
            if (sumNeedA > B)
            {
                for (int i = 0; i < n; i++)
                    if (m.Pct[i] <= 0) colW[i] = m.Min[i] + B * needA[i] / sumNeedA;
                return colW;
            }
            for (int i = 0; i < n; i++)
                if (m.Pct[i] <= 0) colW[i] = Math.Max(m.Min[i], m.Max[i]);
            B -= sumNeedA;
            if (B <= 0) return colW;
            // stage 3: surplus
            bool anyAuto = false;
            double sumAutoMax = 0;
            int autoCount = 0;
            for (int i = 0; i < n; i++)
                if (m.Pct[i] <= 0) { anyAuto = true; sumAutoMax += m.Max[i]; autoCount++; }
            if (anyAuto)
            {
                if (sumAutoMax > 0)
                {
                    for (int i = 0; i < n; i++)
                        if (m.Pct[i] <= 0) colW[i] += B * m.Max[i] / sumAutoMax;
                }
                else
                {
                    for (int i = 0; i < n; i++)
                        if (m.Pct[i] <= 0) colW[i] += B / autoCount;
                }
            }
            else
            {
                double sumP = 0;
                for (int i = 0; i < n; i++) sumP += m.Pct[i];
                if (sumP > 0)
                    for (int i = 0; i < n; i++) colW[i] += B * m.Pct[i] / sumP;
            }
            return colW;
        }

        /// <summary>Page width grows so the widest table's min-content fits:
        /// pageW = max(595, maxOverTables(Σ column mins) + 186).</summary>
        double MeasureWidestTable(Node body)
        {
            double best = 0;
            foreach (var table in body.Descendants().Where(n => n.Tag == "table"))
            {
                var st = new Style();
                ApplyStyleAttr(table.Attr("style"), st);
                var rows = new List<Node>();
                CollectRows(table, rows);
                if (rows.Count == 0) continue;
                int nCols = 0;
                foreach (var tr in rows)
                {
                    int c = 0;
                    foreach (var td in tr.Children.Where(x => x.Tag is "td" or "th"))
                        c += Math.Max(1, ParseIntAttr(td, "colspan"));
                    nCols = Math.Max(nCols, c);
                }
                if (nCols == 0) continue;
                var model = BuildColModel(table, rows, nCols, st);
                best = Math.Max(best, model.Min.Sum());
            }
            return best;
        }

        void LayoutTable(Node table, Style inherited)
        {
            var st = inherited.Clone();
            ApplyStyleAttr(table.Attr("style"), st);

            var rows = new List<Node>();
            CollectRows(table, rows);
            if (rows.Count == 0) return;

            int nCols = 0;
            foreach (var tr in rows)
            {
                int c = 0;
                foreach (var td in tr.Children.Where(x => x.Tag is "td" or "th"))
                    c += Math.Max(1, ParseIntAttr(td, "colspan"));
                nCols = Math.Max(nCols, c);
            }
            if (nCols == 0) return;

            var model = BuildColModel(table, rows, nCols, st);

            // table width: N% of the content box (100% default); absent → shrink
            // to max-content, capped at the content box
            double W = _contentW;
            var wAttr = table.Attr("width");
            if (wAttr.EndsWith("%") && double.TryParse(wAttr.TrimEnd('%'), out var wp))
                W = wp / 100.0 * _contentW;
            else if (wAttr.Length == 0)
                W = Math.Min(_contentW, Math.Max(model.Min.Sum(), model.Max.Sum()));

            var colW = DistributeColumns(model, W);

            // narrower tables with align=center sit centered in the content box
            var drawn = colW.Sum();
            _tableX = 96;
            if (drawn < _contentW - 0.01
                && table.Attr("align").Equals("center", StringComparison.OrdinalIgnoreCase))
                _tableX = 96 + (_contentW - drawn) / 2;

            // per-column half-border carried from a bordered row into the next
            // (BORDER-COLLAPSE: the border straddles the shared row edge)
            var carry = new double[nCols];
            foreach (var tr in rows)
                carry = LayoutRow(tr, colW, st, carry);
            if (carry.Length > 0 && carry.Max() > 0)
                _y += carry.Max();
            _tableX = 96;

            // table imposes no extra bottom margin of its own
        }

        static void CollectRows(Node n, List<Node> rows)
        {
            foreach (var c in n.Children)
            {
                if (c.Tag == "tr") rows.Add(c);
                else if (c.Tag is "tbody" or "thead" or "tfoot") CollectRows(c, rows);
            }
        }

        static int ParseIntAttr(Node td, string name)
        {
            return int.TryParse(td.Attr(name), out var v) ? v : 1;
        }

        (double mn, double mx) CellContentWidths(Node td, Style cellStyle)
        {
            // max = longest unwrapped line; min = longest unbreakable word (plus the
            // continuation-line indent for hanging paragraphs); an explicit
            // <p style="width:Xpt"> forces both to X
            double mx = 0, mn = 0;
            bool nowrap = td.Attrs is not null && td.Attrs.ContainsKey("nowrap");
            foreach (var block in EnumerateCellBlocks(td))
            {
                var st = cellStyle.Clone();
                st.MarginLeft = 0; st.TextIndent = 0;
                double? forcedW = null;
                if (block.Tag == "p")
                {
                    ApplyStyleAttr(block.Attr("style"), st);
                    var wDecl = Regex.Match(block.Attr("style"), @"(?:^|;)\s*width\s*:\s*([\d.]+)\s*pt",
                        RegexOptions.IgnoreCase);
                    if (wDecl.Success)
                        forcedW = double.Parse(wDecl.Groups[1].Value, CultureInfo.InvariantCulture);
                }
                if (forcedW is { } fw)
                {
                    mx = Math.Max(mx, fw);
                    mn = Math.Max(mn, fw);
                    continue;
                }
                var lineIndent = Math.Max(0, st.MarginLeft); // continuation lines
                var runs = CollectRuns(block, st);
                double w = 0, word = 0;
                bool firstWordOfBlock = true;
                void EndWord()
                {
                    if (word > 0)
                        mn = Math.Max(mn, word + (firstWordOfBlock ? Math.Max(0, st.MarginLeft + st.TextIndent) : lineIndent));
                    firstWordOfBlock = false;
                    word = 0;
                }
                foreach (var r in runs)
                {
                    if (r.Text == "\n")
                    {
                        EndWord();
                        mx = Math.Max(mx, w); w = 0;
                        continue;
                    }
                    w += r.Face.Measure(r.Text, r.Size);
                    int i = 0;
                    var text = r.Text;
                    while (i < text.Length)
                    {
                        if (text[i] == ' ') { EndWord(); i++; continue; }
                        int j = i;
                        while (j < text.Length && text[j] != ' ') j++;
                        word += r.Face.Measure(text.Substring(i, j - i), r.Size);
                        i = j;
                    }
                }
                EndWord();
                mx = Math.Max(mx, w + Math.Max(0, st.MarginLeft + st.TextIndent));
            }
            if (nowrap) mn = mx;
            return (mn, mx);
        }

        IEnumerable<Node> EnumerateCellBlocks(Node td)
        {
            // direct <p> children flow as separate blocks; loose inline content is one block
            var loose = new Node { Tag = "p" };
            foreach (var c in td.Children)
            {
                if (c.Tag == "p")
                {
                    if (loose.Children.Count > 0) { yield return loose; loose = new Node { Tag = "p" }; }
                    yield return c;
                }
                else loose.Children.Add(c);
            }
            if (loose.Children.Count > 0) yield return loose;
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

        /// <summary>Lay out one cell as a mini block flow (same gap model as the page
        /// flow: desc + collapsed margins + borders + asc), returning line offsets
        /// from the cell top.</summary>
        CellFlow LayoutCellFlow(Node td, Style trStyle, double x0, double width, int col, int span)
        {
            var flow = new CellFlow { Col = col, Span = span, Valign = td.Attr("valign").ToLowerInvariant(), X0 = x0, Width = width };
            var cellStyle = trStyle.Clone();
            ApplyStyleAttr(td.Attr("style"), cellStyle);
            double y = 0;            // running content bottom (box bottoms)
            bool first = true;
            var margins = new List<double>();
            double prevBorderBottom = 0;
            foreach (var block in EnumerateCellBlocks(td))
            {
                var st = cellStyle.Clone();
                st.MarginLeft = 0; st.TextIndent = 0; st.MarginTop = 0; st.MarginBottom = 0;
                st.BorderTopW = 0; st.BorderBottomW = 0; st.LineHeight = null;
                st.Align = td.Attr("align").ToLowerInvariant();
                if (block.Tag == "p")
                {
                    var alignAttr = block.Attr("align");
                    if (alignAttr.Length > 0) st.Align = alignAttr.ToLowerInvariant();
                    ApplyStyleAttr(block.Attr("style"), st);
                }
                var runs = CollectRuns(block, st);
                if (runs.Count == 0 || runs.All(r => r.Text.Length == 0 && r.AnchorsBefore is null))
                {
                    margins.Add(st.MarginTop);
                    margins.Add(st.MarginBottom);
                    continue;
                }
                var metricRun = runs.Where(r => r.Text.Length > 0).OrderByDescending(r => r.Size).FirstOrDefault();
                if (metricRun is null) continue;
                var (pitch, asc, desc) = LineBox(metricRun.Face, metricRun.Size, st.LineHeight);
                var wrapped = WrapRuns(runs, Math.Max(1, width - st.MarginLeft), st.TextIndent);
                for (int li = 0; li < wrapped.Count; li++)
                {
                    double top;
                    if (first)
                    {
                        top = st.BorderTopW; // cell top: margins vanish (cellpadding 0)
                        first = false;
                    }
                    else if (li == 0)
                    {
                        margins.Add(st.MarginTop);
                        top = y + prevBorderBottom + margins.Max() + st.BorderTopW;
                    }
                    else
                    {
                        top = y; // consecutive lines of a block abut
                    }
                    margins.Clear();
                    prevBorderBottom = 0;
                    var line = new CellLine
                    {
                        Top = top,
                        Asc = asc,
                        Desc = desc,
                        St = st,
                        Pieces = wrapped[li],
                        FirstLine = li == 0,
                        BorderTopW = li == 0 ? st.BorderTopW : 0,
                        BorderTopColor = st.BorderTopColor,
                        BorderBottomW = li == wrapped.Count - 1 ? st.BorderBottomW : 0,
                        BorderBottomColor = st.BorderBottomColor,
                    };
                    flow.Lines.Add(line);
                    y = top + asc + desc;
                }
                margins.Add(st.MarginBottom);
                prevBorderBottom = st.BorderBottomW;
            }
            flow.Height = y + prevBorderBottom;
            return flow;
        }

        static bool RunHasInk(Run r) => r.Text.Trim(' ', (char)0xA0).Length > 0;

        double[] LayoutRow(Node tr, double[] colW, Style tableStyle, double[] carryIn)
        {
            int nCols = colW.Length;
            var carryOut = new double[nCols];
            var trStyle = tableStyle.Clone();
            ApplyStyleAttr(tr.Attr("style"), trStyle);
            var bg = tr.Attr("bgcolor");
            int bgColor = -1;
            if (bg.Length > 0)
            {
                var m = Regex.Match(bg, @"#?([0-9A-Fa-f]{6})");
                if (m.Success) bgColor = int.Parse(m.Groups[1].Value, NumberStyles.HexNumber);
            }

            var cells = tr.Children.Where(x => x.Tag is "td" or "th").ToList();
            if (cells.Count == 0) return carryOut;

            // spacer row: explicit height attr and no visible content
            int hAttr = 0;
            foreach (var td in cells)
                if (int.TryParse(td.Attr("height"), out var hv)) hAttr = Math.Max(hAttr, hv);
            bool anyBorderPara = cells.Any(td => td.Descendants().Any(d =>
                d.Tag == "p" && d.Attr("style").Contains("border", StringComparison.OrdinalIgnoreCase)));
            bool anyTdBorder = cells.Any(td => TdBorderBottom(td) > 0);
            bool empty = !anyBorderPara && !anyTdBorder
                && cells.All(td => CollectRuns(td, trStyle).All(r => !RunHasInk(r)));
            if (empty && hAttr > 0)
            {
                double h = hAttr * 0.75 + (carryIn.Length > 0 ? carryIn.Max() : 0);
                double top = _atPageTop ? _y + (_dropTopMargins ? 0 : (_margins.Count > 0 ? _margins.Max() : 0)) : _y + (_margins.Count > 0 ? _margins.Max() : 0) + _prevBorderBottom;
                _atPageTop = false;
                _margins.Clear();
                if (top + h > BottomLimit) { BreakPage(false); top = _y; }
                _y = top + h;
                EndBlock(0, 0);
                return carryOut;
            }
            if (empty && cells.All(td => td.Children.Count == 0))
                return carryIn; // width-definition row: zero height, borders pass on

            // per-cell mini flows
            var flows = new List<CellFlow>();
            var tdBorders = new List<double>();
            int colIdx = 0;
            foreach (var td in cells)
            {
                int span = Math.Max(1, ParseIntAttr(td, "colspan"));
                double x0 = _tableX;
                for (int i = 0; i < colIdx; i++) x0 += colW[i];
                double width = 0;
                for (int i = colIdx; i < Math.Min(colIdx + span, colW.Length); i++) width += colW[i];
                flows.Add(LayoutCellFlow(td, trStyle, x0, width, colIdx, span));
                tdBorders.Add(TdBorderBottom(td));
                colIdx += span;
            }

            // per-cell content top offset from the row top (half border carried in)
            var topOffsets = new List<double>();
            foreach (var f in flows)
            {
                double off = 0;
                for (int i = f.Col; i < Math.Min(f.Col + f.Span, nCols); i++)
                    off = Math.Max(off, i < carryIn.Length ? carryIn[i] : 0);
                topOffsets.Add(off);
            }

            // the row edge: max over cells of contentTop + stack + own half border
            double rowH = 0;
            for (int i = 0; i < flows.Count; i++)
                rowH = Math.Max(rowH, topOffsets[i] + flows[i].Height + tdBorders[i] / 2);
            if (rowH <= 0) return carryIn;

            // align cells vertically: bottom-valign content bottoms sit at the row
            // edge minus the cell's own half border; top-valign at its content top
            for (int i = 0; i < flows.Count; i++)
            {
                var f = flows[i];
                double contentBottomTarget = rowH - tdBorders[i] / 2;
                double dy = f.Valign switch
                {
                    "top" => topOffsets[i],
                    "middle" => topOffsets[i] + (contentBottomTarget - topOffsets[i] - f.Height) / 2,
                    _ => contentBottomTarget - f.Height, // bottom is the EDGAR default
                };
                if (dy > 0)
                    foreach (var ln in f.Lines) ln.Top += dy;
            }

            // place the row: it may straddle pages at line granularity
            double rowTop;
            if (_atPageTop) { rowTop = _y + (_dropTopMargins ? 0 : (_margins.Count > 0 ? _margins.Max() : 0)); _atPageTop = false; }
            else
            {
                rowTop = _y + _prevBorderBottom + (_margins.Count > 0 ? _margins.Max() : 0);
            }
            _margins.Clear();
            _prevBorderBottom = 0;

            // if even the shallowest first line misses the page, push the whole row
            double firstLineBottom = flows.Where(f => f.Lines.Count > 0)
                .Select(f => rowTop + f.Lines[0].Top + f.Lines[0].Asc + f.Lines[0].Desc)
                .DefaultIfEmpty(rowTop).Min();
            if (firstLineBottom > BottomLimit + 0.01)
            {
                BreakPage(false);
                rowTop = _y;
                _atPageTop = false;
            }

            // bg fill for the row region (clipped to this page)
            if (bgColor >= 0)
                _pg.Rects.Add(new RectFill { X = _tableX, TopTd = rowTop, W = colW.Sum(), H = Math.Min(rowH, BottomLimit - rowTop), Color = bgColor, Stroke = false });

            // place lines in vertical order; break mid-row when a line misses
            double pageShift = 0;
            var pending = flows.SelectMany(f => f.Lines.Select(l => (f, l)))
                .OrderBy(t => t.l.Top).ToList();
            foreach (var (f, ln) in pending)
            {
                var top = rowTop + ln.Top - pageShift;
                var bottom = top + ln.Asc + ln.Desc;
                if (bottom > BottomLimit + 0.01)
                {
                    BreakPage(false);
                    _atPageTop = false;
                    pageShift += top - _y;
                    top = rowTop + ln.Top - pageShift;
                }
                var baseline = top + ln.Asc;
                if (ln.BorderTopW > 0)
                    _pg.Rects.Add(new RectFill { X = f.X0, TopTd = top - ln.BorderTopW / 2, W = f.Width, H = 0, Color = ln.BorderTopColor, Stroke = true, LineW = ln.BorderTopW });
                double lineW = ln.Pieces.Sum(p => p.W);
                double x = f.X0 + ln.St.MarginLeft + (ln.FirstLine ? ln.St.TextIndent : 0);
                if (ln.St.Align == "center") x = f.X0 + (f.Width - lineW) / 2;
                else if (ln.St.Align == "right") x = f.X0 + f.Width - lineW;
                foreach (var piece in ln.Pieces)
                {
                    var r = piece.Run;
                    AddRun(new Run { Text = piece.Text, Face = r.Face, Size = r.Size, Color = r.Color, Sup = r.Sup, LinkId = r.LinkId, AnchorsBefore = r.AnchorsBefore }, x, baseline - (r.Sup ? 1.26 : 0));
                    r.AnchorsBefore = null;
                    if (r.LinkId >= 0 && RunHasInk(piece.Run) && piece.Text.Trim(' ', (char)0xA0).Length > 0)
                        AddLinkRect(r.LinkId, x, baseline, x + piece.W, r.Face, r.Size);
                    x += piece.W;
                }
                if (ln.BorderBottomW > 0)
                    _pg.Rects.Add(new RectFill { X = f.X0, TopTd = top + ln.Asc + ln.Desc + ln.BorderBottomW / 2, W = f.Width, H = 0, Color = ln.BorderBottomColor, Stroke = true, LineW = ln.BorderBottomW });
            }

            // collapsed td borders: stroke on the row edge; carry half into next row
            for (int i = 0; i < flows.Count; i++)
            {
                if (tdBorders[i] > 0)
                {
                    var f = flows[i];
                    _pg.Rects.Add(new RectFill { X = f.X0, TopTd = rowTop + rowH - pageShift, W = f.Width, H = 0, Color = 0, Stroke = true, LineW = tdBorders[i] });
                    for (int c = f.Col; c < Math.Min(f.Col + f.Span, nCols); c++)
                        carryOut[c] = tdBorders[i] / 2;
                }
            }

            _y = rowTop + rowH - pageShift;
            EndBlock(0, 0);
            return carryOut;
        }

        static double TdBorderBottom(Node td)
        {
            var style = td.Attr("style");
            if (style.Length == 0) return 0;
            var s = new Style();
            ApplyStyleAttr(style, s);
            return s.BorderBottomW;
        }

        // ——— emission ————————————————————————————————————————————————

        Document Emit()
        {
            var doc = Document.Create();
            var ic = CultureInfo.InvariantCulture;
            static string F(double v) => ((double)(float)v).ToString("0.######", CultureInfo.InvariantCulture);

            foreach (var pg in _pages)
            {
                var page = doc.Pages.Add(_pageW, PageH);
                var fontDict = Table.ResolvePageFontDict(page);
                var sb = new StringBuilder();
                sb.Append("q\n1 0 0 -1 0 ").Append(F(PageH)).Append(" cm\n");
                // body background (white) over the page content box
                sb.Append("q\n1 1 1 rg\n");
                sb.Append(F(PageMargin)).Append(' ').Append(F(TopMargin)).Append(' ')
                  .Append(F(_pageW - 2 * PageMargin)).Append(' ').Append(F(PageH - 2 * TopMargin)).Append(" re\nf*\nQ\n");

                foreach (var rect in pg.Rects)
                {
                    double r = ((rect.Color >> 16) & 0xFF) / 255.0, g = ((rect.Color >> 8) & 0xFF) / 255.0, b = (rect.Color & 0xFF) / 255.0;
                    if (rect.Stroke)
                    {
                        sb.Append("q\n").Append(F(r)).Append(' ').Append(F(g)).Append(' ').Append(F(b)).Append(" RG\n");
                        sb.Append(F(rect.LineW)).Append(" w\n");
                        sb.Append(F(rect.X)).Append(' ').Append(F(rect.TopTd)).Append(" m\n");
                        sb.Append(F(rect.X + rect.W)).Append(' ').Append(F(rect.TopTd)).Append(" l\nS\nQ\n");
                    }
                    else
                    {
                        sb.Append("q\n").Append(F(r)).Append(' ').Append(F(g)).Append(' ').Append(F(b)).Append(" rg\n");
                        sb.Append(F(rect.X)).Append(' ').Append(F(rect.TopTd)).Append(' ')
                          .Append(F(rect.W)).Append(' ').Append(F(rect.H)).Append(" re\nf\nQ\n");
                    }
                }

                foreach (var run in pg.Runs)
                {
                    var text = run.Run.Text;
                    if (text.Length == 0) continue;
                    var face = run.Run.Face;
                    var (res, hex) = Text.Type0FontEmbedder.Embed(fontDict, face.Ttf, face.Display, text, stripSpacesInBaseFont: true);
                    double r = ((run.Run.Color >> 16) & 0xFF) / 255.0, g = ((run.Run.Color >> 8) & 0xFF) / 255.0, b = (run.Run.Color & 0xFF) / 255.0;
                    sb.Append("BT\n/").Append(res).Append(' ').Append(run.Run.Size.ToString("0.###", ic)).Append(" Tf\n");
                    sb.Append(F(r)).Append(' ').Append(F(g)).Append(' ').Append(F(b)).Append(" rg\n");
                    sb.Append("1 0 0 -1 ").Append(F(run.X)).Append(' ').Append(F(run.BaselineTd)).Append(" Tm\n");
                    sb.Append(BuildKernedTj(face, text, hex, run.Run.Size));
                    sb.Append("0 g\nET\n");
                }

                sb.Append("Q\n");
                page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));

                foreach (var img in pg.Images)
                {
                    try
                    {
                        page.AddImage(img.Data, new Rectangle(img.X, PageH - img.TopTd - img.H, img.X + img.W, PageH - img.TopTd));
                    }
                    catch { }
                }
            }

            // annotations in document order per page
            for (int pi = 0; pi < _pages.Count; pi++)
            {
                var pg = _pages[pi];
                var page = doc.Pages[pi + 1];
                var items = new List<(int order, System.Action emit)>();
                foreach (var (order, linkId, llx, lly, urx, ury) in pg.LinkRects)
                {
                    var link = Links[linkId];
                    items.Add((order, (System.Action)(() =>
                    {
                        if (!AnchorIdx.TryGetValue(link.TargetName, out var aid)) return;
                        var a = Anchors[aid];
                        if (a.PageIdx < 0) return;
                        var action = Annotations.PdfAction.CreateGoTo(a.PageIdx, 96.0, a.TopPdf, null);
                        page.Annotations.AddLinkAnnotation(new Rectangle(llx, lly, urx, ury), action);
                    })));
                }
                foreach (var (order, anchorId, x, y) in pg.AnchorPoints)
                {
                    items.Add((order, (System.Action)(() =>
                    {
                        var dict = new PdfDictionary();
                        dict.Set("Type", new PdfName("Annot"));
                        dict.Set("Subtype", new PdfName("Link"));
                        var rectArr = new PdfArray();
                        rectArr.Add(new PdfReal(x)); rectArr.Add(new PdfReal(y));
                        rectArr.Add(new PdfReal(x)); rectArr.Add(new PdfReal(y));
                        dict.Set("Rect", rectArr);
                        var border = new PdfArray();
                        border.Add(new PdfInteger(0)); border.Add(new PdfInteger(0)); border.Add(new PdfInteger(0));
                        dict.Set("Border", border);
                        page.Annotations.AddImportedDict(dict);
                    })));
                }
                foreach (var it in items.OrderBy(t => t.order)) it.emit();
            }

            return doc;
        }

        static string BuildKernedTj(Face face, string text, byte[] hex, double size)
        {
            // hex = 2 bytes per UTF-16 unit from the embedder; interleave kern moves
            var sb = new StringBuilder();
            sb.Append('[');
            var seg = new StringBuilder();
            void Flush()
            {
                if (seg.Length > 0) { sb.Append('<').Append(seg).Append('>'); seg.Clear(); }
            }
            for (int i = 0; i < text.Length && 2 * i + 1 < hex.Length; i++)
            {
                if (i > 0)
                {
                    var k = face.Parser.GetKernAdjustment(face.Gid(text[i - 1]), face.Gid(text[i]));
                    if (k != 0)
                    {
                        Flush();
                        var adj = -(k * 1000.0 / face.Upm);
                        sb.Append(' ').Append(((float)adj).ToString("0.######", CultureInfo.InvariantCulture)).Append(' ');
                    }
                }
                seg.Append(hex[2 * i].ToString("X2")).Append(hex[2 * i + 1].ToString("X2"));
            }
            Flush();
            sb.Append("] TJ\n");
            return sb.ToString();
        }
    }
}
