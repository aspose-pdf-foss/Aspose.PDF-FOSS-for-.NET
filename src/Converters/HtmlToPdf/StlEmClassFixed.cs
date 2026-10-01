using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// The em-unit CLASS-positioned stl_ export: a slide deck saved as one fixed-size
// container div per source page, every line a `<div class="stl_NN">` whose
// `position: absolute; left: Xem; top: Yem` lives in the stylesheet, its text in a
// `display: inline` class carrying font-size/line-height/letter-spacing/word-spacing
// in em, the page's vector ink in an `<object>` background, and the real glyphs in
// `@font-face` WOFF programs. The em unit is the CONTAINER class's own pt font-size,
// so the sheet's `@media ... { .layer { font-size: 10em; transform: scale(0.1) } }`
// authoring trick never applies — the reference resolves neither media queries nor
// transforms, and the plain cascade is what lands on the page.
internal static partial class HtmlToPdfConverter
{
    /// <summary>Content top of this dialect's band; every line seats at class top + this.</summary>
    private const double StlEmContentTopPt = 78.0;

    /// <summary>Band pitch: the flow advances by this much per sheet.</summary>
    private const double StlEmBandPitchPt = 698.0;

    /// <summary>Sheet width − (96 + the widest laid-out element).</summary>
    private const double StlEmRightPadPt = 90.0;

    /// <summary>Sheet y a line box may not cross: the page's 72 pt bottom margin.</summary>
    private const double StlEmFitBottomPt = 770.0;

    /// <summary>A line box that crosses the band's bottom edge moves whole to the next
    /// sheet and seats at the page's TOP margin rather than at its own flow position.</summary>
    private const double StlEmWidowTopPt = 72.0;

    /// <summary>Cheap necessary condition for the dialect, so a document that cannot be it
    /// never pays for the fixed-layout readers' full-document scans: an export of this shape
    /// carries nothing but divs and spans, so ANY flow tag rules it out. It decides on the
    /// first one, which in practice is a few hundred bytes in.</summary>
    internal static bool IsStlEmClassCandidate(string html) =>
        !Regex.IsMatch(html, @"<(table|p|h[1-6]|ul|ol|input|form|textarea)\b", RegexOptions.IgnoreCase);

    /// <summary>One stylesheet class, as far as this dialect reads it.</summary>
    private sealed class StlEmCls
    {
        public bool Absolute;
        public double? LeftEm, TopEm;
        public double? FontSizeEm, LineHeightEm, LetterSpacingEm, WordSpacingEm;
        public string? Family, Color;
        public double? WidthPt, HeightPt, FontSizePt;
        public double? WidthEm, HeightEm;
    }

    /// <summary>Every `.name { ... }` rule of <paramref name="css"/>, merged per class
    /// (later rules win per property). Descendant selectors (`.ie .stl_08`) and every
    /// `@media` block are left out: the reference applies neither.</summary>
    private static Dictionary<string, StlEmCls> ReadStlEmClasses(string css)
    {
        var map = new Dictionary<string, StlEmCls>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(StripAtBlocks(css), @"(?<sel>[^{}]+)\{(?<body>[^{}]*)\}"))
        {
            var sel = m.Groups["sel"].Value.Trim();
            if (sel.Length < 2 || sel[0] != '.') continue;
            var name = sel[1..];
            if (!Regex.IsMatch(name, @"^[\w-]+$")) continue;   // a descendant/compound selector
            var body = m.Groups["body"].Value;
            if (!map.TryGetValue(name, out var c)) map[name] = c = new StlEmCls();
            if (Regex.IsMatch(body, @"position:\s*absolute")) c.Absolute = true;
            c.LeftEm = StlEmNum(body, "left") ?? c.LeftEm;
            c.TopEm = StlEmNum(body, "top") ?? c.TopEm;
            c.FontSizeEm = StlEmNum(body, "font-size") ?? c.FontSizeEm;
            c.LineHeightEm = StlEmNum(body, "line-height") ?? c.LineHeightEm;
            c.LetterSpacingEm = StlEmNum(body, "letter-spacing") ?? c.LetterSpacingEm;
            c.WordSpacingEm = StlEmNum(body, "word-spacing") ?? c.WordSpacingEm;
            c.WidthPt = StlPtNum(body, "width") ?? c.WidthPt;
            c.HeightPt = StlPtNum(body, "height") ?? c.HeightPt;
            c.WidthEm = StlEmNum(body, "width") ?? c.WidthEm;
            c.HeightEm = StlEmNum(body, "height") ?? c.HeightEm;
            c.FontSizePt = StlPtNum(body, "font-size") ?? c.FontSizePt;
            var fam = Regex.Match(body, @"font-family:\s*""?(?<v>[^;""}]+)");
            if (fam.Success) c.Family = fam.Groups["v"].Value.Trim();
            var col = Regex.Match(body, @"(?<!-)color:\s*(?<v>[^;}]+)");
            if (col.Success) c.Color = col.Groups["v"].Value.Trim();
        }
        return map;
    }

    private static double? StlEmNum(string body, string prop) => StlUnitNum(body, prop, "em");

    private static double? StlPtNum(string body, string prop) => StlUnitNum(body, prop, "pt");

    private static double? StlUnitNum(string body, string prop, string unit)
    {
        // The property name must stand alone: `top` is not `border-top`, `height` not
        // `line-height`. The unit suffix delimits the value on the right.
        var m = Regex.Match(body, @"(?<![\w-])" + prop + @":\s*(?<v>-?[\d.]+)" + unit);
        return m.Success && double.TryParse(m.Groups["v"].Value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    /// <summary>The stylesheet with every at-rule BLOCK that nests further rules
    /// (`@media`, `@supports`) removed, braces counted so a nested block ends where it
    /// really ends. Declaration-only at-rules (`@font-face`) stay: their own reader
    /// runs over the untouched text.</summary>
    private static string StripAtBlocks(string css)
    {
        var sb = new System.Text.StringBuilder(css.Length);
        var i = 0;
        while (i < css.Length)
        {
            var at = css.IndexOf("@media", i, StringComparison.OrdinalIgnoreCase);
            var sup = css.IndexOf("@supports", i, StringComparison.OrdinalIgnoreCase);
            if (at < 0 || (sup >= 0 && sup < at)) at = sup;
            if (at < 0) { sb.Append(css, i, css.Length - i); break; }
            sb.Append(css, i, at - i);
            var open = css.IndexOf('{', at);
            if (open < 0) break;
            var depth = 0;
            var j = open;
            for (; j < css.Length; j++)
            {
                if (css[j] == '{') depth++;
                else if (css[j] == '}' && --depth == 0) { j++; break; }
            }
            i = j;
        }
        return sb.ToString();
    }

    /// <summary>The class every source page's container div carries: it sizes the page box
    /// in pt AND sets the pt font-size the dialect's em unit is measured in. The class the
    /// markup repeats MOST is the container (ties go to the first by name, so the pick never
    /// depends on stylesheet order); null when no class is repeated at all.</summary>
    /// <summary>The repeated fixed-size container class - one per source page. Preferred in the
    /// dialect's own pt spelling; a container that declares its box and font-size in EM instead
    /// resolves them against the ROOT font size, which is what a browser gives a sheet that names
    /// no body size (measured on the two raster exports: 51em x 12 = 612.00 and 66em x 12 = 792.00,
    /// the image's intrinsic width and the band advance the reference actually uses).</summary>
    private static string? StlEmContainerClass(string html, Dictionary<string, StlEmCls> classes)
        => StlEmContainerClass(html, classes, em: false) ?? StlEmContainerClass(html, classes, em: true);

    private static string? StlEmContainerClass(string html, Dictionary<string, StlEmCls> classes, bool em)
    {
        string? best = null;
        var bestCount = 1;
        foreach (var (name, c) in classes)
        {
            if (em) { if (!StlEmResolveEmBox(c)) continue; }
            else if (c.WidthPt is not > 0 || c.HeightPt is not > 0 || c.FontSizePt is not > 0) continue;
            var n = Regex.Matches(html, @"<div class=""" + Regex.Escape(name) + @"""[^>]*>").Count;
            if (n > bestCount || (n == bestCount && best is not null
                                  && string.CompareOrdinal(name, best) < 0))
            {
                best = name;
                bestCount = n;
            }
        }
        return best;
    }

    /// <summary>Resolves an em-declared container box against the root font size, in place, so the
    /// rest of the dialect reads it exactly as it reads a pt-declared one. False when the class
    /// does not declare a full box in em.</summary>
    private static bool StlEmResolveEmBox(StlEmCls c)
    {
        if (c.WidthPt is > 0 && c.HeightPt is > 0 && c.FontSizePt is > 0) return true;
        if (c.WidthEm is not > 0 || c.HeightEm is not > 0 || c.FontSizeEm is not > 0) return false;
        c.FontSizePt = c.FontSizeEm!.Value * CssRootFontPt;
        c.WidthPt = c.WidthEm!.Value * CssRootFontPt;
        c.HeightPt = c.HeightEm!.Value * CssRootFontPt;
        return true;
    }
}
