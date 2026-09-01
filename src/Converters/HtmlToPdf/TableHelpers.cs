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
            var m = Regex.Match(st, @"width\s*:\s*(\d+(?:\.\d+)?)\s*px", RegexOptions.IgnoreCase);
            if (m.Success && double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var w))
            {
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
        public double WidthFrac, ColPadPt;                         // Col (print-grid stacked column)
    }

    private static string DivStyleOf(string openTag)
    {
        var m = Regex.Match(openTag, @"style\s*=\s*(""([^""]*)""|'([^']*)')", RegexOptions.IgnoreCase);
        return m.Success ? (m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value) : "";
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
        // width-first order too ("border: 1px solid gainsboro" — the class-box form).
        || Regex.IsMatch(style, @"border\s*:\s*\d+(?:\.\d+)?\s*px\s+solid", RegexOptions.IgnoreCase);

    private static double StylePct(string style, string prop)
    {
        var m = Regex.Match(style, prop + @"\s*:\s*(\d+(?:\.\d+)?)%", RegexOptions.IgnoreCase);
        return m.Success && double.TryParse(m.Groups[1].Value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
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
    private static int FindDivEnd(string html, int contentStart, out int contentEnd)
    {
        var depth = 1;
        var rx = new Regex(@"<(/?)div\b[^>]*>", RegexOptions.IgnoreCase);
        for (var m = rx.Match(html, contentStart); m.Success; m = m.NextMatch())
        {
            if (m.Groups[1].Value.Length > 0) depth--; else depth++;
            if (depth == 0) { contentEnd = m.Index; return m.Index + m.Length; }
        }
        contentEnd = -1;
        return -1;
    }

    /// <summary>Scan HTML for float-column groups and bordered divs; everything else
    /// stays as plain flow fragments. Nested structures inside a column or box are
    /// resolved by the caller's recursion, not here.</summary>
    private static List<DivSeg> SegmentDivStructures(string html,
        IReadOnlyDictionary<string, Dictionary<string, string>>? classCss = null,
        double contentWidthPt = 0, bool allowPxCols = false)
    {
        // Print-grid mode (classCss set): a div's effective style folds its CLASS
        // rules' box declarations under the inline style, so class-styled grids
        // (.col-xs-N width%, .infobox borders) segment like inline-styled ones.
        string EffStyle(string openTag)
        {
            var st = DivStyleOf(openTag);
            if (classCss is null) return st;
            var clm = Regex.Match(openTag, @"class\s*=\s*[""']([^""']*)[""']", RegexOptions.IgnoreCase);
            if (!clm.Success) return st;
            var sb = new StringBuilder(st);
            foreach (var c in clm.Groups[1].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                if (classCss.TryGetValue("." + c, out var d))
                    foreach (var kv in d)
                        if (kv.Key is "border" or "padding" or "padding-top" or "padding-bottom"
                            or "padding-left" or "padding-right" or "margin-bottom" or "width")
                        { sb.Append(';').Append(kv.Key).Append(':').Append(kv.Value); }
            return sb.ToString();
        }
        bool IsColScopeStyle(string st) =>
            classCss is not null
            && !Regex.IsMatch(st, @"float\s*:\s*left", RegexOptions.IgnoreCase)
            && !Regex.IsMatch(st, @"border\s*:", RegexOptions.IgnoreCase)
            && StylePct(st, "width") is > 0 and < 100;
        var segs = new List<DivSeg>();
        var divRx = new Regex(@"<div\b[^>]*>", RegexOptions.IgnoreCase);
        var pos = 0;
        while (pos < html.Length)
        {
            Match? hit = null; string style = "";
            for (var m = divRx.Match(html, pos); m.Success; m = m.NextMatch())
            {
                var st = EffStyle(m.Value);
                if (IsFloatColStyle(st, allowPxCols) || IsBorderBoxStyle(st) || IsColScopeStyle(st)) { hit = m; style = st; break; }
            }
            if (hit is null) break;
            var afterOpen = hit.Index + hit.Length;
            var end = FindDivEnd(html, afterOpen, out var contentEnd);
            if (end < 0) break;
            if (hit.Index > pos)
                segs.Add(new DivSeg { Kind = DivSeg.Flow, Html = html[pos..hit.Index] });
            if (IsColScopeStyle(style) && !IsFloatColStyle(style, allowPxCols) && !IsBorderBoxStyle(style))
            {
                segs.Add(new DivSeg
                {
                    Kind = DivSeg.Col,
                    Html = html[afterOpen..contentEnd],
                    WidthFrac = StylePct(style, "width") / 100.0,
                    ColPadPt = StyleLenPt(style, "padding-left") + StyleLenPt(style, "padding-right"),
                });
                pos = end;
                continue;
            }
            if (IsFloatColStyle(style, allowPxCols))
            {
                var cols = new List<(string, double, double, double)>();
                var cursor = 0.0;
                // px→fraction against the content box (px-width Bootstrap columns).
                double PxFrac(string st3, string prop)
                {
                    if (contentWidthPt <= 0) return 0;
                    var pm = Regex.Match(st3, prop + @"\s*:\s*(\d+(?:\.\d+)?)px", RegexOptions.IgnoreCase);
                    return pm.Success && double.TryParse(pm.Groups[1].Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var pv)
                        ? pv * 0.75 / contentWidthPt : 0;
                }
                void AddCol(string st2, string inner)
                {
                    var w = StylePct(st2, "width") / 100.0;
                    if (w <= 0) w = PxFrac(st2, "width");
                    var ml = StylePct(st2, "margin-left") / 100.0;
                    if (ml <= 0) ml = PxFrac(st2, "margin-left");
                    var pr = StylePct(st2, "padding-right") / 100.0;
                    if (pr <= 0) pr = PxFrac(st2, "padding-right") + PxFrac(st2, "margin-right");
                    var start = cursor + ml;
                    cols.Add((inner, start, w, StyleLenPt(st2, "padding-top")));
                    cursor = start + w + pr;
                }
                AddCol(style, html[afterOpen..contentEnd]);
                pos = end;
                while (true)
                {
                    var nm = divRx.Match(html, pos);
                    if (!nm.Success || !string.IsNullOrWhiteSpace(html[pos..nm.Index])) break;
                    var st3 = DivStyleOf(nm.Value);
                    if (!IsFloatColStyle(st3, allowPxCols)) break;
                    // A float that cannot fit beside the ones already collected wraps
                    // below them (px-column dialect): stop this band — the next loop
                    // pass starts a fresh band for it, stacking it as its own row
                    // (two 380px floats inside a 380px parent stack, not overlap).
                    if (allowPxCols)
                    {
                        var w3 = StylePct(st3, "width") / 100.0;
                        if (w3 <= 0) w3 = PxFrac(st3, "width");
                        var ml3 = StylePct(st3, "margin-left") / 100.0;
                        if (ml3 <= 0) ml3 = PxFrac(st3, "margin-left");
                        if (cursor + ml3 + w3 > 1.02) break;
                    }
                    var ne = FindDivEnd(html, nm.Index + nm.Length, out var nce);
                    if (ne < 0) break;
                    AddCol(st3, html[(nm.Index + nm.Length)..nce]);
                    pos = ne;
                }
                segs.Add(new DivSeg { Kind = DivSeg.Band, Cols = cols });
            }
            else
            {
                // A `padding: Npx` shorthand covers all four sides when no side-specific
                // declaration is present (the class-box form).
                var padAll = StyleLenPt(style, "padding");
                var padT = StyleLenPt(style, "padding-top");
                var padB = StyleLenPt(style, "padding-bottom");
                var padL = StyleLenPt(style, "padding-left");
                var padR = StyleLenPt(style, "padding-right");
                segs.Add(new DivSeg
                {
                    Kind = DivSeg.Box,
                    Html = html[afterOpen..contentEnd],
                    BorderPt = BorderSolidPt(style),
                    PadTopPt = padT > 0 ? padT : padAll,
                    PadBottomPt = padB > 0 ? padB : padAll,
                    PadSidePt = padL + padR > 0 ? padL + padR : 2 * padAll,
                    MarginBottomPt = StyleLenPt(style, "margin-bottom"),
                    BorderGray = BorderGrayOf(style),
                });
                pos = end;
            }
        }
        if (segs.Count > 0 && pos < html.Length)
            segs.Add(new DivSeg { Kind = DivSeg.Flow, Html = html[pos..] });
        return segs;
    }

    /// <summary>Render a NON-HTML binary file loaded through HtmlLoadOptions:
    /// HTML5-tokenize (a &lt;letter… tag swallows to the next '&gt;',
    /// an unterminated one swallows the rest), lay the remaining mojibake out as one
    /// anonymous Times New Roman 12 pt paragraph, and size the page to the min-content
    /// width: pageW = 90+6 + W + 90 where W is the widest unbreakable segment. Whitespace
    /// collapses to one space; FF/VT are forced line breaks; other C0 controls are
    /// invisible fixed advances (9 pt; GS 0, DEL 6, NBSP 3).</summary>
    private static Document? TryConvertBinaryText(string html)
    {
        var ttf = Text.SystemFontResolver.Resolve("Times New Roman");
        if (ttf is null) return null;
        Text.GlyphOutlineParser gp;
        try { gp = new Text.GlyphOutlineParser(ttf); } catch { return null; }
        var upm = gp.UnitsPerEm > 0 ? gp.UnitsPerEm : 1000;
        double Adv(char ch) => gp.CMap.TryGetValue(ch, out var g)
            ? Math.Round(gp.GetAdvanceWidth(g) * 1000.0 / upm) * 12.0 / 1000.0
            : 6.0;

        // ---- HTML5-style tokenize: keep text, swallow tags/comments.
        var textBuf = new StringBuilder(html.Length);
        for (var i = 0; i < html.Length;)
        {
            var c = html[i];
            if (c == '<' && i + 3 < html.Length && html[i + 1] == '!' && html[i + 2] == '-' && html[i + 3] == '-')
            {
                var e = html.IndexOf("-->", i + 4, StringComparison.Ordinal);
                i = e < 0 ? html.Length : e + 3;
                continue;
            }
            if (c == '<' && i + 1 < html.Length
                && (char.IsLetter(html[i + 1]) || html[i + 1] is '/' or '!' or '?'))
            {
                var e = html.IndexOf('>', i + 1);
                i = e < 0 ? html.Length : e + 1;
                continue;
            }
            textBuf.Append(c);
            i++;
        }
        var text = textBuf.ToString();

        // ---- Item stream: (advance, glyph-or-null). Paragraphs split at FF/VT.
        // An item list per unbreakable segment; a segment boundary is a collapsed
        // whitespace run, a position before '\', or one after '-'.
        var paragraphs = new List<List<(double w, List<(double adv, char? ch)> items)>>();
        var curPara = new List<(double, List<(double, char?)>)>();
        var curSeg = new List<(double adv, char? ch)>();
        double curSegW = 0;
        var pendingSpace = false;

        void EndSegment()
        {
            if (curSeg.Count > 0)
            {
                curPara.Add((curSegW, curSeg));
                curSeg = new List<(double, char?)>();
                curSegW = 0;
            }
        }
        void EndParagraph()
        {
            EndSegment();
            paragraphs.Add(curPara);
            curPara = new List<(double, List<(double, char?)>)>();
            pendingSpace = false;
        }
        void AddItem(double adv, char? ch)
        {
            if (pendingSpace)
            {
                // The collapsed space run becomes ONE 3 pt inter-segment separator
                // (a space draws no ink, so it is an invisible advance).
                EndSegment();
                curPara.Add((3.0, new List<(double, char?)> { (3.0, null) }));
                pendingSpace = false;
            }
            curSeg.Add((adv, ch));
            curSegW += adv;
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is ' ' or '\t' or '\r' or '\n')
            {
                EndSegment();
                pendingSpace = true;
                continue;
            }
            switch (c)
            {
                case '\f':
                case '\v':
                    EndParagraph();
                    continue;
                case ' ':
                    AddItem(3.0, null);
                    continue;
                case '':
                    AddItem(6.0, null);
                    continue;
            }
            if (c < 0x20)
            {
                AddItem(c == 0x1D ? 0.0 : 9.0, null);
                continue;
            }
            // Extra break opportunities: before '\', after '-'.
            if (c == '\\') EndSegment();
            AddItem(Adv(c), c);
            if (c == '-') EndSegment();
        }
        EndParagraph();

        // ---- Min-content width and page size.
        double maxSeg = 0;
        foreach (var para in paragraphs)
            foreach (var (w, _) in para)
                if (w > maxSeg) maxSeg = w;
        const double left = 96.0, right = 90.0;
        var pageW = Math.Max(595.0, left + maxSeg + right);
        const double pageH = 842.0;
        var limit = left + Math.Max(maxSeg, pageW - left - right);

        var doc = Document.Create();
        var fontDict = new Core.PdfDictionary();
        var page = doc.Pages.Add(pageW, pageH);
        EnsureFonts(page, fontDict);

        // ---- Greedy wrap + emission. First baseline 88.91 from the page top,
        // then a constant 13.5 pt pitch (per-line strut quirks stay within
        // rendering tolerance).
        var invc = System.Globalization.CultureInfo.InvariantCulture;
        double baseline = 88.91;
        var sb = new StringBuilder();

        void EmitRun(double x, double y, string run)
        {
            if (run.Length == 0) return;
            var (rn, hex) = Text.Type0FontEmbedder.Embed(fontDict, ttf, "TimesNewRoman", run);
            sb.Append("BT /").Append(rn).Append(" 12 Tf 1 0 0 1 ")
              .Append(x.ToString("F2", invc)).Append(' ')
              .Append((pageH - y).ToString("F2", invc)).Append(" Tm <")
              .Append(System.Convert.ToHexString(hex)).Append("> Tj ET\n");
        }

        void NextLine()
        {
            baseline += 13.5;
            if (baseline > pageH - 60)
            {
                page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
                sb.Clear();
                page = doc.Pages.Add(pageW, pageH);
                EnsureFonts(page, fontDict);
                baseline = 88.91;
            }
        }

        foreach (var para in paragraphs)
        {
            double x = left;
            var lineHasContent = false;
            foreach (var (w, items) in para)
            {
                var isSpaceSep = items.Count == 1 && items[0].ch is null
                                 && Math.Abs(items[0].adv - 3.0) < 0.001;
                if (!lineHasContent && isSpaceSep)
                    continue;   // leading space at a line start is dropped
                if (lineHasContent && !isSpaceSep && x + w > limit + 0.01)
                {
                    NextLine();
                    x = left;
                    lineHasContent = false;
                }
                // Emit the segment: visible glyph runs, split around invisible advances.
                var run = new StringBuilder();
                double runX = x;
                foreach (var (adv, ch) in items)
                {
                    if (ch is { } gch)
                    {
                        run.Append(gch);
                        x += adv;
                    }
                    else
                    {
                        EmitRun(runX, baseline, run.ToString());
                        run.Clear();
                        x += adv;
                        runX = x;
                    }
                }
                EmitRun(runX, baseline, run.ToString());
                if (!isSpaceSep) lineHasContent = true;
            }
            NextLine();
        }
        page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
        return doc;
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
        static string ClassOf(HtmlNode n) => n.Attrs is not null && n.Attrs.TryGetValue("class", out var c) ? c : "";
        static string StyleOf(HtmlNode n) => n.Attrs is not null && n.Attrs.TryGetValue("style", out var s2) ? s2 : "";
        static (int start, int end)? ContentSpan(HtmlNode n) =>
            n.Children.Count > 0 ? (n.Children[0].SrcIndex, n.Children[^1].SrcEnd) : null;
        static double PxOf(string style, string prop)
        {
            var m = Regex.Match(style, prop + @"\s*:\s*(\d+(?:\.\d+)?)px", RegexOptions.IgnoreCase);
            return m.Success && double.TryParse(m.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
        }
        // Column width the value cell sizes against: the nearest ancestor px-width
        // float column, else the page-content default.
        double ColWidth(HtmlNode n)
        {
            for (var p2 = n.Parent; p2 is not null; p2 = p2.Parent)
            {
                var st = StyleOf(p2);
                if (p2.Tag == "div" && PxOf(st, "width") > 0
                    && Regex.IsMatch(st, @"float\s*:\s*left", RegexOptions.IgnoreCase))
                    return PxOf(st, "width");
            }
            return containerPx > 0 ? containerPx : 780.0;
        }

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
            if (g.Tag != "div" || !ClassOf(g).Contains("control-group", StringComparison.OrdinalIgnoreCase))
                continue;
            HtmlNode? label = null, controls = null;
            foreach (var d in g.Descendants())
            {
                if (label is null && d.Tag == "label") label = d;
                if (controls is null && d.Tag == "div"
                    && ClassOf(d).Contains("controls", StringComparison.OrdinalIgnoreCase)) controls = d;
                if (label is not null && controls is not null) break;
            }
            if (label is null || controls is null) continue;
            var labStyle = StyleOf(label);
            var wLab = PxOf(labStyle, "width");
            var mVal = PxOf(StyleOf(controls), "margin-left");
            if (wLab <= 0 || mVal <= wLab
                || !Regex.IsMatch(labStyle, @"float\s*:\s*left", RegexOptions.IgnoreCase)) continue;
            var wVal = Math.Max(60, ColWidth(g) - mVal);
            // A value span carrying its own CSS width sets the value cell's width —
            // it may overflow the enclosing float column exactly as the float does
            // in a browser (`width:210px` inside the 380px column runs past 380).
            foreach (var d in controls.Descendants())
                if (d.Tag == "span")
                {
                    var sw = PxOf(StyleOf(d), "width");
                    if (sw > 0) wVal = sw;
                    break;
                }
            // The cell's inner text box loses a few px to the cell inset; pad the
            // value cell so its wrap width matches the span's CSS content width
            // ("…catheters," stays on the 190px line; the inset-
            // narrowed cell broke it a word early).
            wVal += 6;
            var bold = Regex.IsMatch(labStyle, @"font-weight\s*:\s*(700|bold)", RegexOptions.IgnoreCase);
            var labText = ContentSpan(label) is { } ls2 ? html[ls2.start..ls2.end].Trim() : "";
            var valHtml = ContentSpan(controls) is { } cs2 ? html[cs2.start..cs2.end].Trim() : "";
            // class=fh-row marks the synthesized row for the layout pass (per-row CSS
            // rhythm); data-fhw carries its natural width so a row wider than its
            // float column keeps that width instead of being squeezed to fit.
            var row = "<table class=\"fh-row\" data-fhw=\"" + (wLab + (mVal - wLab) + wVal).ToString(inv)
                + "\"" + rowStyle + "><tr><td style=\"width:" + wLab.ToString(inv)
                + "px;text-align:right;\">" + (bold ? "<b>" : "") + labText + (bold ? "</b>" : "")
                + "</td><td style=\"width:" + (mVal - wLab).ToString(inv)
                + "px\"></td><td style=\"width:" + wVal.ToString(inv)
                + "px\">" + valHtml + "</td></tr></table>";
            repls.Add((g.SrcIndex, g.SrcEnd, row));
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
        html = Regex.Replace(html, @"<(script|style|head)[^>]*>[\s\S]*?</\1>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<!DOCTYPE[^>]*>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<!--[\s\S]*?-->", "");
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
            if (c is not (' ' or '\t' or '\r' or '\n' or ' ')) return false;
        }
        return true;
    }

    private static bool TryGetCssLength(IReadOnlyDictionary<string, Dictionary<string, string>> css,
        string selector, string prop, out double pts)
    {
        pts = 0;
        return css.TryGetValue(selector, out var d) && d.TryGetValue(prop, out var v) && TryParseLength(v, out pts);
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
