using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The grid and every grid nested inside it, in document order.</summary>
    private static void CollectGrids(Table t, List<Table> into)
    {
        into.Add(t);
        foreach (var r in t.Rows)
            foreach (Cell c in r.Cells)
                foreach (var p in c.Paragraphs)
                    if (p is Table inner) CollectGrids(inner, into);
    }

    /// <summary>Font ascent/descent (fractions of em) for the CSS line-box layout of
    /// styled table-cell lines: line box = 1.2 × em, baseline sits at
    /// ascent·em + (box − (ascent+descent)·em)/2 below the box top (see grp/S notes):
    /// legacy Korean faces (Dotum/Gungsuh/Gulim/Batang — typically not
    /// installed) 0.857/0.1435; Arial from its real hhea table;
    /// Malgun Gothic (the default substitution face for Korean HTML) with
    /// calibrated values. Unknown families use the Malgun default.</summary>
    private static (double Asc, double Desc) CssFamilyMetrics(string? family)
    {
        if (family is not null)
        {
            var f = family.Trim().Trim('\'', '"').ToLowerInvariant();
            if (f is "돋움" or "dotum" or "궁서" or "gungsuh" or "굴림" or "gulim" or "바탕" or "batang"
                or "돋움체" or "dotumche" or "궁서체" or "gungsuhche" or "굴림체" or "gulimche" or "바탕체" or "batangche")
                return (0.857, 0.1435);
            if (f is "arial" or "helvetica")
                return (0.90527, 0.21191);
            if (f is "times new roman" or "times")
                return (0.89111, 0.21631);
            if (f is "verdana")
                return (1.00537, 0.20996);
        }
        return (1.0791, 0.2280); // Malgun Gothic–like default
    }

    /// <summary>Resolve a table cell's explicit CSS width in px (inline <c>style="width:Npx"</c>
    /// first, then a <c>class</c> rule's width); 0 when none is specified.</summary>
    /// <summary>The <c>line-height: normal</c> box for a font size: the browser rounds
    /// 1.1499 em to whole pixels, so the pitch steps in 0.75 pt increments.</summary>
    private static double NormalLineHeightPt(double fontSizePt) =>
        fontSizePt > 0 ? 0.75 * Math.Round(1.1499 * (fontSizePt / 0.75)) : 0;

    /// <summary>Distance from a line box's TOP to the baseline it carries: half the
    /// leading plus the face's ascent. The flow cursor is kept in baseline space, so
    /// a box that lays out from its own top — a rule, a table — starts this far ABOVE
    /// the cursor, and the last baseline of a text block sits this far below its box
    /// top.</summary>
    /// <summary>Symbol-font private-use chars (U+F0xx): a symbol face's
    /// glyphs offset into the PUA (Wingdings box marks).</summary>
    private static bool IsSymbolPua(char c) => c >= '' && c <= '';
    private static bool HasSymbolPua(string s)
    {
        foreach (var c in s) if (IsSymbolPua(c)) return true;
        return false;
    }

    /// <summary>Width of <paramref name="text"/> in the redline small-caps
    /// rendering: lowercase measures UPPERCASE at RedlineSmallCapsEm of the
    /// size, everything else at full size.</summary>
    private static double MeasureSmallCapsText(string face, string text, double fs)
    {
        double w = 0; var i = 0;
        while (i < text.Length)
        {
            var lower = char.IsLower(text[i]);
            var j = i + 1;
            while (j < text.Length && char.IsLower(text[j]) == lower) j++;
            var seg = text[i..j];
            w += MeasureFaceText(face, lower ? seg.ToUpperInvariant() : seg,
                lower ? fs * RedlineSmallCapsEm : fs);
            i = j;
        }
        return w;
    }

    /// <summary>Greedy wrap on real face advances where the FIRST line fits a
    /// narrower box (a positive text-indent) and later lines take the full
    /// measure.</summary>
    private static string[] RedlineIndentWrap(string text, double firstAvail,
        double avail, string face, double fs)
    {
        var spaceW = MeasureFaceText(face, "a a", fs) - MeasureFaceText(face, "aa", fs);
        var lines = new List<string>();
        var cur = new StringBuilder(); double curW = 0;
        foreach (var word in text.Split(' '))
        {
            var wW = MeasureFaceText(face, word, fs);
            var cap = lines.Count == 0 ? firstAvail : avail;
            if (cur.Length > 0 && curW + spaceW + wW > cap)
            {
                lines.Add(cur.ToString());
                cur.Clear(); curW = 0;
            }
            if (cur.Length > 0) { cur.Append(' '); curW += spaceW; }
            cur.Append(word); curW += wW;
        }
        if (cur.Length > 0) lines.Add(cur.ToString());
        return lines.Count > 0 ? lines.ToArray() : new[] { "" };
    }

    /// <summary>Greedy word wrap measured with the small-caps advances.</summary>
    private static string[] SmallCapsWordWrap(string text, double avail, string face, double fs)
    {
        var spaceW = MeasureFaceText(face, "a a", fs) - MeasureFaceText(face, "aa", fs);
        var lines = new List<string>();
        var cur = new StringBuilder(); double curW = 0;
        foreach (var word in text.Split(' '))
        {
            var wW = MeasureSmallCapsText(face, word, fs);
            if (cur.Length > 0 && curW + spaceW + wW > avail)
            {
                lines.Add(cur.ToString());
                cur.Clear(); curW = 0;
            }
            if (cur.Length > 0) { cur.Append(' '); curW += spaceW; }
            cur.Append(word); curW += wW;
        }
        if (cur.Length > 0) lines.Add(cur.ToString());
        return lines.Count > 0 ? lines.ToArray() : new[] { "" };
    }

    private static double BaselineInLineBoxPt(double fontSizePt) =>
        fontSizePt > 0 ? NormalLineHeightPt(fontSizePt) / 2 + 0.3374 * fontSizePt : 0;

    /// <summary>Greedy break of <paramref name="text"/> into lines that fit
    /// <paramref name="widthPt"/>. Spaces are the normal opportunities; a run that
    /// cannot fit on its own breaks after the last hyphen that fits, and failing that
    /// mid-character (the <c>word-wrap: break-word</c> rule). Never returns empty.</summary>
    private static List<string> WrapToBox(string text, double widthPt, Func<string, double> measure)
    {
        var outLines = new List<string>();
        var rest = text;
        while (rest.Length > 0)
        {
            if (measure(rest) <= widthPt) { outLines.Add(rest); break; }
            // Longest space-delimited prefix that fits.
            var cut = -1;
            for (var sp = rest.IndexOf(' '); sp > 0; sp = rest.IndexOf(' ', sp + 1))
            {
                if (measure(rest[..sp]) > widthPt) break;
                cut = sp;
            }
            if (cut < 0)
            {
                // The first run alone overflows: break inside it, after the last hyphen
                // that fits when there is one, else at the last character that fits.
                var fit = 1;
                while (fit < rest.Length && measure(rest[..(fit + 1)]) <= widthPt) fit++;
                var dash = rest.LastIndexOf('-', Math.Min(fit, rest.Length - 1));
                cut = dash > 0 ? dash + 1 : fit;
                outLines.Add(rest[..cut]);
                rest = rest[cut..];
                continue;
            }
            outLines.Add(rest[..cut]);
            rest = rest[(cut + 1)..];
        }
        if (outLines.Count == 0) outLines.Add(text);
        return outLines;
    }

    private static double ResolveCellWidthPt(Dictionary<string, string>? attrs,
        IReadOnlyDictionary<string, Dictionary<string, string>> css, bool contentBox = false,
        bool readWidthAttr = false)
    {
        if (attrs is null) return 0;
        // `<td width="15">` is HTML4's spelling of `width: 15px` and the only width a
        // layout table gives its spacer columns. The percent form is a share, handled
        // by the caller's cellWidthPct.
        if (readWidthAttr && attrs.TryGetValue("width", out var wAttr)
            && !wAttr.Contains('%', StringComparison.Ordinal)
            && double.TryParse(wAttr.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var wA) && wA > 0)
            return wA;
        if (attrs.TryGetValue("style", out var st))
        {
            // …in px, or in an absolute unit (the report export's `WIDTH: 177.8mm` setter
            // cells), all measured in px here like the attribute form.
            var m = Regex.Match(st, @"(?<![-\w])width\s*:\s*(\d+(?:\.\d+)?)\s*(px|pt|mm|cm|in)", RegexOptions.IgnoreCase);
            if (m.Success && double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var w))
            {
                w *= m.Groups[2].Value.ToLowerInvariant() switch
                {
                    "pt" => 1 / PxPt,
                    "mm" => 96.0 / 25.4,
                    "cm" => 96.0 / 2.54,
                    "in" => 96.0,
                    _ => 1.0,
                };
                // Content-box: the cell's own padding sits OUTSIDE the declared width,
                // so the column it fixes is width + horizontal padding.
                if (contentBox)
                    foreach (Match pm in Regex.Matches(st,
                        @"padding-(left|right)\s*:\s*(\d+(?:\.\d+)?)\s*px", RegexOptions.IgnoreCase))
                        if (double.TryParse(pm.Groups[2].Value, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var p))
                            w += p;
                return w;
            }
        }
        if (attrs.TryGetValue("class", out var cls))
            foreach (var c in cls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (css.TryGetValue("." + c, out var d) && d.TryGetValue("width", out var wv))
                {
                    var m = Regex.Match(wv, @"(\d+(?:\.\d+)?)\s*px", RegexOptions.IgnoreCase);
                    if (m.Success && double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var w))
                        return w;
                }
        return 0;
    }

    /// <summary>One segment of a div-structure scan: plain flow HTML, a float band
    /// (side-by-side percent-width columns), or a bordered box around inner flow.</summary>
    private sealed class DivSeg
    {
        public const int Flow = 0, Band = 1, Box = 2, Col = 3;
        public int Kind;
        public string Html = "";                                   // Flow: raw fragment; Box/Col: inner HTML
        public List<(string Inner, double StartFrac, double WidthFrac, double PadTopPt)>? Cols; // Band
        public double BorderPt, PadTopPt, PadBottomPt;             // Box
        public double PadSidePt, MarginBottomPt, BorderGray;       // Box (print-grid)
        public double BoxWidthPt, BoxHeightPt;                     // Box (declared size)
        public double WidthFrac, ColPadPt;                         // Col (print-grid stacked column)
    }

    private static string DivStyleOf(string openTag)
    {
        // (the value may stand unquoted: `style=border-style:None;width:800px`)
        var m = Regex.Match(openTag, @"\bstyle\s*=\s*(""([^""]*)""|'([^']*)'|([^\s>""']+))", RegexOptions.IgnoreCase);
        return m.Success ? (m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Success ? m.Groups[3].Value : m.Groups[4].Value) : "";
    }

    /// <summary>True when the document contains a float-column band: a &lt;div&gt; whose
    /// inline style carries both <c>float:left</c> and a <c>width:N%</c> (the SEC-filing
    /// two-column card signature). Either declaration order matches.</summary>
    /// <summary>The width a table DECLARES in its own attribute, in points, or null when
    /// it declares none. The float flow lays such a table out at that width and lets it
    /// overflow, rather than squeezing it into the content box.</summary>
    private static double? CertDeclaredTableWidthPt(string? tableHtml)
    {
        if (string.IsNullOrEmpty(tableHtml)) return null;
        var m = Regex.Match(tableHtml, @"<table\b[^>]*\bwidth\s*=\s*[""']?(\d+(?:\.\d+)?)\s*[""']?",
            RegexOptions.IgnoreCase);
        return m.Success && double.TryParse(m.Groups[1].Value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var px) && px > 0
            ? px * 0.75 : null;
    }

    private static bool HasFloatColumnBand(string html) =>
        Regex.IsMatch(html, @"<div\b[^>]*float\s*:\s*left[^>]*width\s*:\s*\d+(?:\.\d+)?%",
            RegexOptions.IgnoreCase)
        || Regex.IsMatch(html, @"<div\b[^>]*width\s*:\s*\d+(?:\.\d+)?%[^>]*float\s*:\s*left",
            RegexOptions.IgnoreCase);

    private static bool IsFloatColStyle(string style, bool allowPx = false) =>
        Regex.IsMatch(style, @"float\s*:\s*left", RegexOptions.IgnoreCase)
        && (Regex.IsMatch(style, @"width\s*:\s*\d+(?:\.\d+)?%", RegexOptions.IgnoreCase)
            || (allowPx && Regex.IsMatch(style, @"width\s*:\s*\d+(?:\.\d+)?px", RegexOptions.IgnoreCase)));

    private static bool IsBorderBoxStyle(string style) =>
        Regex.IsMatch(style, @"border\s*:\s*solid", RegexOptions.IgnoreCase)
        // width-first order too ("border: 1px solid gainsboro" — the class-box form);
        // a ZERO width (`border: 0px solid …`, the signature pad's) is no box at all
        || Regex.IsMatch(style, @"border\s*:\s*(?!0+(?:\.0+)?\s*px\s)\d+(?:\.\d+)?\s*px\s+solid", RegexOptions.IgnoreCase);

    private static double StylePct(string style, string prop)
    {
        var m = Regex.Match(style, prop + @"\s*:\s*(\d+(?:\.\d+)?)%", RegexOptions.IgnoreCase);
        return m.Success && double.TryParse(m.Groups[1].Value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    /// <summary>Length of a declaration the style states IN ITS OWN RIGHT: the property
    /// must open a declaration, so a `border-width` never answers a `width` question.</summary>
    private static double StyleOwnLenPt(string style, string prop)
    {
        var m = Regex.Match(style, @"(?:^|;)\s*" + prop + @"\s*:\s*(\d+(?:\.\d+)?)\s*(pt|px)",
            RegexOptions.IgnoreCase);
        if (!m.Success || !double.TryParse(m.Groups[1].Value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v)) return 0;
        return m.Groups[2].Value.Equals("px", StringComparison.OrdinalIgnoreCase) ? v * 0.75 : v;
    }

    private static double StyleLenPt(string style, string prop)
    {
        var m = Regex.Match(style, prop + @"\s*:\s*(\d+(?:\.\d+)?)\s*(pt|px)", RegexOptions.IgnoreCase);
        if (!m.Success || !double.TryParse(m.Groups[1].Value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v)) return 0;
        return m.Groups[2].Value.Equals("px", StringComparison.OrdinalIgnoreCase) ? v * 0.75 : v;
    }

    /// <summary>Border stroke width of a `border:solid …` shorthand, in points
    /// (defaults to 1px = 0.75pt when the shorthand names no length).</summary>
    private static double BorderSolidPt(string style)
    {
        var m = Regex.Match(style, @"border\s*:\s*solid\s+(\d+(?:\.\d+)?)\s*(pt|px)?", RegexOptions.IgnoreCase);
        if (!m.Success)
            m = Regex.Match(style, @"border\s*:\s*(\d+(?:\.\d+)?)\s*(pt|px)?\s+solid", RegexOptions.IgnoreCase);
        if (!m.Success) return 0.75;
        double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v);
        if (v <= 0) return 0.75;
        return m.Groups[2].Value.Equals("pt", StringComparison.OrdinalIgnoreCase) ? v : v * 0.75;
    }

    /// <summary>Stroke grey level for a box border (0 = black): a named/hex light
    /// colour like gainsboro strokes light so the frame reads light.</summary>
    private static double BorderGrayOf(string style)
    {
        var m = Regex.Match(style, @"border\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
        if (!m.Success) return 0;
        var c = ParseCssColor(m.Groups[1].Value);
        if (c is null) return 0;
        return (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
    }

    /// <summary>Index just past the `&lt;/div&gt;` matching an open tag whose content
    /// starts at <paramref name="contentStart"/>; also outputs the content end
    /// (start of that close tag). −1 when unbalanced.</summary>
    private static (int result, int contentEnd) FindDivEnd(string html, int contentStart)
    {
        int contentEnd = default;
        var depth = 1;
        var rx = new Regex(@"<(/?)div\b[^>]*>", RegexOptions.IgnoreCase);
        for (var m = rx.Match(html, contentStart); m.Success; m = m.NextMatch())
        {
            if (m.Groups[1].Value.Length > 0) depth--; else depth++;
            if (depth == 0) { contentEnd = m.Index; return (m.Index + m.Length, contentEnd); }
        }
        contentEnd = -1;
        return (-1, contentEnd);
    }

    /// <summary>Scan HTML for float-column groups and bordered divs; everything else
    /// stays as plain flow fragments. Nested structures inside a column or box are
    /// resolved by the caller's recursion, not here.</summary>
    private static List<DivSeg> SegmentDivStructures(string html, IReadOnlyDictionary<string, Dictionary<string, string>>? classCss = null, double contentWidthPt = 0, bool allowPxCols = false)
    {
        var dv = new DivSegmentState();
        dv.html = html;
        dv.classCss = classCss;
        dv.contentWidthPt = contentWidthPt;
        dv.allowPxCols = allowPxCols;
        dv.segs = new List<DivSeg>();
        dv.divRx = new Regex(@"<div\b[^>]*>", RegexOptions.IgnoreCase);
        dv.pos = 0;
        while (dv.pos < dv.html.Length)
        {
            if (!SegmentNextDiv(dv)) break;
        }
        if (dv.segs.Count > 0 && dv.pos < dv.html.Length)
            dv.segs.Add(new DivSeg { Kind = DivSeg.Flow, Html = dv.html[dv.pos..] });
        return dv.segs;
    }

    /// <summary>Expand every CSS <c>font:</c> shorthand inside a style attribute or
    /// &lt;style&gt; block into its longhand declarations (font-style / font-weight /
    /// font-size / line-height / font-family). System-font keywords and values that
    /// do not fit the size+family grammar are left untouched. With
    /// <paramref name="familylessResets"/> (the form-report dialect), a shorthand that
    /// names a size but NO family — invalid CSS a browser drops whole — is applied the
    /// way the expected render applies it: every omitted longhand resets to its
    /// initial value, so the family falls back to the UA serif and the weight to
    /// normal (`font: normal 14px` on an h2 → serif, not the inherited sans, not bold).</summary>
    internal static string ExpandFontShorthands(string html, bool familylessResets = false)
    {
        if (html.IndexOf("font", StringComparison.OrdinalIgnoreCase) < 0) return html;

        string ExpandDecls(string decls, bool keepShorthand = false) =>
            Regex.Replace(decls, @"(?<![-\w])font\s*:\s*([^;}""']+)", mm =>
            {
                var v = mm.Groups[1].Value.Trim();
                var m2 = Regex.Match(v,
                    @"^(?<pre>((normal|italic|oblique|bold|bolder|lighter|small-caps|[1-9]00)\s+)*)" +
                    @"(?<size>[\d.]+(px|pt|em|rem|%)|(x{1,2}-)?small|(x{1,2}-)?large|medium|larger|smaller)" +
                    @"(\s*/\s*(?<lh>[\d.]+(px|pt|em|rem|%)?))?\s+(?<fam>.+)$",
                    RegexOptions.IgnoreCase);
                if (!m2.Success && familylessResets)
                {
                    var m3 = Regex.Match(v,
                        @"^(?<pre>((normal|italic|oblique|bold|bolder|lighter|small-caps|[1-9]00)\s+)*)" +
                        @"(?<size>[\d.]+(px|pt|em|rem|%))" +
                        @"(\s*/\s*(?<lh>[\d.]+(px|pt|em|rem|%)?))?$",
                        RegexOptions.IgnoreCase);
                    if (m3.Success)
                    {
                        var sb3 = new StringBuilder();
                        var pre3 = m3.Groups["pre"].Value;
                        if (Regex.IsMatch(pre3, @"\b(italic|oblique)\b", RegexOptions.IgnoreCase))
                            sb3.Append("font-style: italic;");
                        var wm3 = Regex.Match(pre3, @"\b(bold|bolder|[1-9]00)\b", RegexOptions.IgnoreCase);
                        sb3.Append("font-weight: ").Append(wm3.Success ? wm3.Value : "normal").Append(';');
                        sb3.Append("font-size: ").Append(m3.Groups["size"].Value).Append(';');
                        if (m3.Groups["lh"].Success)
                            sb3.Append("line-height: ").Append(m3.Groups["lh"].Value).Append(';');
                        sb3.Append("font-family: Times New Roman");
                        return sb3.ToString();
                    }
                }
                if (!m2.Success) return mm.Value;   // e.g. `font: menu` — leave as-is
                var sb = new StringBuilder();
                var pre = m2.Groups["pre"].Value;
                if (Regex.IsMatch(pre, @"\b(italic|oblique)\b", RegexOptions.IgnoreCase))
                    sb.Append("font-style: italic;");
                var wm = Regex.Match(pre, @"\b(bold|bolder|[1-9]00)\b", RegexOptions.IgnoreCase);
                if (wm.Success) sb.Append("font-weight: ").Append(wm.Value).Append(';');
                sb.Append("font-size: ").Append(m2.Groups["size"].Value).Append(';');
                if (m2.Groups["lh"].Success)
                    sb.Append("line-height: ").Append(m2.Groups["lh"].Value).Append(';');
                sb.Append("font-family: ").Append(m2.Groups["fam"].Value.Trim());
                // Stylesheet rules keep the shorthand itself beside the longhands: the
                // `td/table { font: … }` SHORTHAND is the form-document dialect's
                // signature (grid tables, CSS line-box cell pitch), which a pure
                // longhand rewrite would erase.
                return keepShorthand ? "font: " + v + ";" + sb : sb.ToString();
            }, RegexOptions.IgnoreCase);

        // style="…" / style='…' attributes.
        html = Regex.Replace(html, @"(\bstyle\s*=\s*)(""([^""]*)""|'([^']*)')", m =>
        {
            var quoted = m.Groups[2].Value;
            var quote = quoted[0];
            var inner = quoted[1..^1];
            if (inner.IndexOf("font", StringComparison.OrdinalIgnoreCase) < 0) return m.Value;
            return m.Groups[1].Value + quote + ExpandDecls(inner) + quote;
        }, RegexOptions.IgnoreCase);

        // <style> blocks.
        html = Regex.Replace(html, @"(<style[^>]*>)([\s\S]*?)(</style>)", m =>
            m.Groups[1].Value + ExpandDecls(m.Groups[2].Value, keepShorthand: true) + m.Groups[3].Value,
            RegexOptions.IgnoreCase);
        return html;
    }

    /// <summary>Rewrite Bootstrap-2 "form-horizontal" rows &#8212; a
    /// <c>&lt;label style="float:left;width:Wpx;text-align:right"&gt;</c> beside a
    /// <c>&lt;div class="controls" style="margin-left:Mpx"&gt;</c> inside a
    /// control-group &#8212; into a three-cell table row so label and value share ONE
    /// line (the flow renderer would stack them). DOM-driven: extents come from
    /// the parser, so heterogeneous nesting (wrapper divs, in-group clears,
    /// sub-columns) cannot desync the replacement.</summary>
    internal static string TransformFormHorizontalRows(string html, double containerPx = 0)
    {
        if (html.IndexOf("control-group", StringComparison.OrdinalIgnoreCase) < 0
            || html.IndexOf("<label", StringComparison.OrdinalIgnoreCase) < 0) return html;
        HtmlNode dom;
        try
        {
            dom = ParseDom(Regex.Replace(html, @"<(script|style|head)[^>]*>[\s\S]*?</\1>",
                m => new string(' ', m.Length), RegexOptions.IgnoreCase));
        }
        catch { return html; }

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        // Column width the value cell sizes against: the nearest ancestor px-width
        // float column, else the page-content default.
        // The rows inherit the BODY face and size — the synthesized tables carry them
        // inline so cell measurement (wrap points) and rendering use the real font
        // metrics, not the Helvetica fallback (a 180px Tahoma-bold label wraps
        // where Helvetica squeaks by, and it must wrap).
        var rowStyle = "";
        {
            var bodyTagM = Regex.Match(html, @"<body\b[^>]*>", RegexOptions.IgnoreCase);
            if (bodyTagM.Success)
            {
                var bodyStyleM = Regex.Match(bodyTagM.Value, @"style\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase);
                var bodyStyle = bodyStyleM.Success ? bodyStyleM.Groups[1].Value : "";
                var famM = Regex.Match(bodyStyle, @"font-family\s*:\s*([^;""]+)", RegexOptions.IgnoreCase);
                var fam = famM.Success ? famM.Groups[1].Value.Split(',')[0].Trim() : "";
                var fsPt = 12.0;
                var fsM = Regex.Match(bodyStyle, @"font-size\s*:\s*([\d.]+)\s*(em|px|pt)", RegexOptions.IgnoreCase);
                if (fsM.Success && double.TryParse(fsM.Groups[1].Value,
                        System.Globalization.NumberStyles.Float, inv, out var fsv))
                    fsPt = fsM.Groups[2].Value.ToLowerInvariant() switch
                    {
                        "px" => fsv * 0.75,
                        "pt" => fsv,
                        _ => fsv * 12.0,   // em of the 16px = 12pt UA base
                    };
                if (fam.Length > 0)
                    rowStyle = " style=\"font-family: " + fam + ";font-size: "
                        + fsPt.ToString("0.##", inv) + "pt\"";
            }
        }

        var repls = new List<(int start, int end, string repl)>();
        foreach (var g in dom.Descendants())
        {
            if (!TransformFormRowGroup(g, containerPx, html, inv, rowStyle, repls)) break;
        }
        if (repls.Count == 0) return html;
        repls.Sort((a, b) => a.start.CompareTo(b.start));
        var sb = new StringBuilder(html.Length);
        var pos = 0;
        foreach (var (s3, e3, r3) in repls)
        {
            if (s3 < pos) continue;   // nested inside an already-replaced group
            sb.Append(html, pos, s3 - pos).Append(r3);
            pos = e3;
        }
        sb.Append(html, pos, html.Length - pos);
        return sb.ToString();
    }

    /// <summary>Strip script/style/head/comment/doctype bodies so the table tokenizer
    /// sees only structural markup (mirrors the front of <see cref="ParseBlocks"/>).</summary>
    private static string StripNonContent(string html)
    {
        // (a title is never content, wherever a headless document leaves it)
        html = Regex.Replace(html, @"<(script|style|head|title)[^>]*>[\s\S]*?</\1>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<!DOCTYPE[^>]*>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<!--[\s\S]*?-->", "");
        html = Regex.Replace(html, ConditionalCommentMarker, "", RegexOptions.IgnoreCase);
        // An XML prolog / processing instruction (XHTML sources) is markup, not text.
        html = Regex.Replace(html, @"<\?[\s\S]*?\?>", "");
        return html;
    }

    /// <summary>Collapse runs of collapsible whitespace to a single space. U+00A0 —
    /// what <c>&amp;nbsp;</c> decodes to — is CONTENT, not whitespace: a browser neither
    /// collapses it with its neighbours nor breaks on it, so it survives verbatim into
    /// the extracted text. (.NET's <c>\s</c> and <c>Trim()</c> both treat it as
    /// whitespace, which is why both are spelled out here.)</summary>
    /// <summary>The presentational <c>align</c> attribute of a row or cell, or null
    /// when it names nothing this layout understands.</summary>
    private static HorizontalAlignment? ParseAlignAttr(string v) =>
        v.Trim().ToLowerInvariant() switch
        {
            "right" => HorizontalAlignment.Right,
            "center" => HorizontalAlignment.Center,
            "left" => HorizontalAlignment.Left,
            _ => null,
        };

    private static string CollapseWs(string s) =>
        Regex.Replace(s, @"[^\S\u00A0]+", " ").Trim(' ', '\t', '\r', '\n', '\f', '\v');

    /// <summary>True when the buffer is empty or holds only whitespace (ASCII space/tab/
    /// newline or the non-breaking space U+00A0 that &amp;nbsp; decodes to).</summary>
    private static bool IsAllWhitespace(System.Text.StringBuilder sb)
    {
        for (var i = 0; i < sb.Length; i++)
        {
            var c = sb[i];
            if (c is not (' ' or '\t' or '\r' or '\n' or ' ') && !IsRunMark(c)) return false;
        }
        return true;
    }

    private static double? TryGetCssLength(IReadOnlyDictionary<string, Dictionary<string, string>> css,
        string selector, string prop)
    {
        double pts = 0;
        if (css.TryGetValue(selector, out var d) && d.TryGetValue(prop, out var v) && TryParseLength(v) is { } len)
        {
            pts = len;
            return pts;
        }
        return null;
    }

    // Tags that open a block-level element; each starts a new Block on exit.
    private static readonly HashSet<string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "blockquote", "ul", "ol", "li", "tr", "td", "th",
        "h1", "h2", "h3", "h4", "h5", "h6",
        "table", "pre", "hr",
    };

    // Tags whose inner content is discarded entirely.
    private static readonly HashSet<string> SkipTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "head", "meta", "link", "title",
    };

    // HTML void elements — no close tag ever follows, so a hidden one cannot open
    // a suppression scope that waits for its close.
    private static readonly HashSet<string> VoidTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input",
        "meta", "link", "param", "source", "track", "wbr",
    };

    private static readonly Regex HiddenInlineRx = new(
        @"(?:display\s*:\s*none|visibility\s*:\s*hidden)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>True when the element resolves to display:none or visibility:hidden —
    /// via its inline style, or a stylesheet rule for its type, one of its classes
    /// (plain or tag-compound), or its id. Hidden content is not rendered at all.</summary>
    private static bool IsHiddenElement(string tag,
        Dictionary<string, string>? attrs,
        IReadOnlyDictionary<string, Dictionary<string, string>>? css)
    {
        if (attrs is not null && attrs.TryGetValue("style", out var inlineStyle)
            && !string.IsNullOrEmpty(inlineStyle) && HiddenInlineRx.IsMatch(inlineStyle))
            return true;
        if (css is null || css.Count == 0) return false;

        bool RuleHides(string selector)
        {
            if (!css.TryGetValue(selector, out var decls)) return false;
            if (decls.TryGetValue("display", out var disp)
                && disp.Contains("none", StringComparison.OrdinalIgnoreCase))
                return true;
            return decls.TryGetValue("visibility", out var vis)
                   && vis.Contains("hidden", StringComparison.OrdinalIgnoreCase);
        }

        var tagLower = tag.ToLowerInvariant();
        if (RuleHides(tagLower)) return true;
        if (attrs is not null)
        {
            if (attrs.TryGetValue("class", out var cls) && !string.IsNullOrWhiteSpace(cls))
                foreach (var c in cls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    if (RuleHides("." + c) || RuleHides(tagLower + "." + c))
                        return true;
            if (attrs.TryGetValue("id", out var id) && !string.IsNullOrWhiteSpace(id)
                && RuleHides("#" + id.Trim()))
                return true;
        }
        return false;
    }
}
