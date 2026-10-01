using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>One side of a CSS border after the cascade: width in pt (0 = none), style keyword, colour (null = the current text colour).</summary>
    internal struct CssBorderSide
    {
        public double W;
        public string Style;
        public Color? Col;
    }

    /// <summary>The CSS `medium` border width in pt (probed: a side shorthand without a width strokes 1.00 pt).</summary>
    private const double CssBorderMediumPt = 1.0;
    /// <summary>The CSS `thin` and `thick` border widths in pt (probed: 0.5 and 2.0).</summary>
    private const double CssBorderThinPt = 0.5;
    private const double CssBorderThickPt = 2.0;
    /// <summary>A dashed frame's nominal dash and gap, in widths (the reference era strokes [2w w] from the corner).</summary>
    private const double CssDashLenWidths = 2.0;
    private const double CssDashGapWidths = 1.0;

    private static readonly string[] CssBorderStyleWords =
        { "none", "hidden", "solid", "dashed", "dotted", "double", "groove", "ridge", "inset", "outset" };

    private static double? CssBorderWidthOf(string tok) => tok.ToLowerInvariant() switch
    {
        "thin" => CssBorderThinPt,
        "medium" => CssBorderMediumPt,
        "thick" => CssBorderThickPt,
        _ => TryParseLength(tok) is { } pt ? pt : IsZeroLength(tok) ? 0.0 : null,
    };

    /// <summary>The four border sides (top, right, bottom, left) of an inline style after the CSS cascade: longhands and
    /// shorthands apply in declaration order, a shorthand resetting the parts it omits to their initial values (width
    /// medium, style none, colour current), and a shorthand naming two styles or two widths is invalid and ignored.</summary>
    private static CssBorderSide[] CssBorderSides(string style)
    {
        var sides = new CssBorderSide[4];
        for (var i = 0; i < 4; i++) sides[i] = new CssBorderSide { W = CssBorderMediumPt, Style = "none", Col = null };
        var sideNames = new[] { "top", "right", "bottom", "left" };
        foreach (Match dm in StyleDeclRx.Matches(style))
        {
            var prop = dm.Groups[1].Value.ToLowerInvariant();
            var val = dm.Groups[2].Value.Trim();
            if (!prop.StartsWith("border", StringComparison.Ordinal)) continue;
            var parts = prop.Split('-');
            var sideIdx = parts.Length >= 2 ? Array.IndexOf(sideNames, parts[1]) : -1;
            var leaf = parts[^1];
            if (prop == "border" || (sideIdx >= 0 && parts.Length == 2))
            {
                if (CssBorderShorthand(val) is not { } sh) continue;
                for (var i = 0; i < 4; i++)
                    if (sideIdx < 0 || i == sideIdx) sides[i] = sh;
            }
            else if (leaf is "width" or "style" or "color")
            {
                var vals = CssSideValues(val);
                if (vals is null) continue;
                for (var i = 0; i < 4; i++)
                {
                    if (sideIdx >= 0 && i != sideIdx) continue;
                    var v = vals[sideIdx >= 0 ? 0 : i];
                    if (leaf == "width" && CssBorderWidthOf(v) is { } w) sides[i].W = w;
                    else if (leaf == "style" && Array.IndexOf(CssBorderStyleWords, v.ToLowerInvariant()) >= 0) sides[i].Style = v.ToLowerInvariant();
                    else if (leaf == "color" && ParseCssColor(v) is { } c) sides[i].Col = c;
                }
            }
        }
        for (var i = 0; i < 4; i++)
            if (sides[i].Style is "none" or "hidden") sides[i].W = 0;
        return sides;
    }

    /// <summary>A 1-4 value side list expanded to (top, right, bottom, left); null when the value is empty.</summary>
    private static string[]? CssSideValues(string val)
    {
        var t = val.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return t.Length switch
        {
            0 => null,
            1 => new[] { t[0], t[0], t[0], t[0] },
            2 => new[] { t[0], t[1], t[0], t[1] },
            3 => new[] { t[0], t[1], t[2], t[1] },
            _ => new[] { t[0], t[1], t[2], t[3] },
        };
    }

    /// <summary>A `border`/`border-side` shorthand: width, style and colour in any order, each at most once; null when invalid.</summary>
    private static CssBorderSide? CssBorderShorthand(string val)
    {
        var side = new CssBorderSide { W = CssBorderMediumPt, Style = "none", Col = null };
        int nW = 0, nS = 0, nC = 0;
        foreach (var tok in val.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (Array.IndexOf(CssBorderStyleWords, tok.ToLowerInvariant()) >= 0) { side.Style = tok.ToLowerInvariant(); nS++; }
            else if (CssBorderWidthOf(tok) is { } w) { side.W = w; nW++; }
            else if (ParseCssColor(tok) is { } c) { side.Col = c; nC++; }
            else return null;
        }
        return nW > 1 || nS > 1 || nC > 1 ? null : side;
    }

    /// <summary>A table's CLASS rule chrome (UA form grids): its border sides after the cascade, and its
    /// background as the box fill.</summary>
    private static void ReadMetricTableClassChrome(MetricTableState mt, Dictionary<string, string> rule)
    {
        var sb = new StringBuilder();
        foreach (var kv in rule)
            if (kv.Key.StartsWith("border", StringComparison.OrdinalIgnoreCase))
                sb.Append(kv.Key).Append(':').Append(kv.Value).Append(';');
        if (sb.Length > 0)
        {
            var sides = CssBorderSides(sb.ToString());
            foreach (var sd in sides)
                if (sd.W > 0) { mt.mps.sideFrames = sides; break; }
        }
        if ((rule.TryGetValue("background-color", out var bg) || rule.TryGetValue("background", out bg))
            && ParseCssColor(bg.Trim()) is { } bgc)
            mt.mps.tableBg = bgc;
    }

    /// <summary>A table with no border attribute keeps the CSS border sides its own inline style declares (the
    /// attribute grid has its own frame model).</summary>
    /// <summary>The pt form's inline border longhands cascade over its class frame (read after the
    /// class chrome): `border-bottom-style: none` on the title tables leaves three sides, and the next
    /// grid's top rule sits where their bottom would (probed on the helpdesk form).</summary>
    private static void CascadePtFormInlineSides(MetricTableState mt, Dictionary<string, string> ta)
    {
        if (!mt.mps.ptFormCells || mt.mps.sideFrames is not { } classSides || !ta.TryGetValue("style", out var pfSt) || pfSt is null
            || !Regex.IsMatch(pfSt, @"border", RegexOptions.IgnoreCase)) return;
        var cascade = new StringBuilder();
        var sideNames = new[] { "top", "right", "bottom", "left" };
        for (var i = 0; i < 4; i++)
            if (classSides[i].W > 0)
                cascade.Append(Compat.Format(System.Globalization.CultureInfo.InvariantCulture,
                    $"border-{sideNames[i]}: {classSides[i].W:0.###}pt {classSides[i].Style} {(classSides[i].Col is { } c ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : "")};"));
        cascade.Append(pfSt);
        var merged = CssBorderSides(cascade.ToString());
        for (var i = 0; i < 4; i++)
            if (merged[i].Style is "none" or "hidden") merged[i].W = 0;
        mt.mps.sideFrames = merged;
    }

    private static void ReadMetricTableSideFrames(MetricTableState mt, Dictionary<string, string> ta)
    {
        if (ta.ContainsKey("border") || !ta.TryGetValue("style", out var st) || st is null
            || !Regex.IsMatch(st, @"border", RegexOptions.IgnoreCase)
            // a `border-style` longhand on a SEPARATED grid frames the table through the inline
            // frame alone (one stroke round the box and its trailing spacing - probed on the test
            // form's cellspacing=5 grids); a cellspacing=0 grid keeps the side frames its greens
            // were calibrated on
            || (Regex.IsMatch(st, @"(?<![-\w])border-style\s*:", RegexOptions.IgnoreCase)
                && ta.TryGetValue("cellspacing", out var csAttr) && double.TryParse(csAttr.Trim().TrimEnd('p', 'x'),
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var csV) && csV > 0)) return;
        var sides = CssBorderSides(st);
        foreach (var sd in sides)
            if (sd.W > 0) { mt.mps.sideFrames = sides; return; }
    }

    /// <summary>Every box paints its own borders: each declared side of a CSS-framed table strokes a centre line of its
    /// width just inside the border box, corner to corner, a dashed side at the nominal [2w w] from the corner.</summary>
    private static void EmitCssSideFrames(Page page, CssBorderSide[] sides, double x0, double x1, double topY, double botY, System.Globalization.CultureInfo invc)
    {
        var sb = new StringBuilder("q ");
        for (var i = 0; i < 4; i++)
        {
            var sd = sides[i];
            if (sd.W <= 0) continue;
            var c = sd.Col ?? Color.Black;
            var dash = sd.Style switch
            {
                "dashed" => Compat.Format(invc, $"[{sd.W * CssDashLenWidths:0.##} {sd.W * CssDashGapWidths:0.##}] 0 d "),
                "dotted" => Compat.Format(invc, $"[{sd.W:0.##} {sd.W:0.##}] 0 d "),
                _ => "[] 0 d ",
            };
            var h = sd.W / 2;
            var (ax, ay, bx, by) = i switch
            {
                0 => (x0, topY - h, x1, topY - h),
                1 => (x1 - h, topY, x1 - h, botY),
                2 => (x0, botY + h, x1, botY + h),
                _ => (x0 + h, topY, x0 + h, botY),
            };
            sb.Append(Compat.Format(invc,
                $"{c.R / 255.0:0.###} {c.G / 255.0:0.###} {c.B / 255.0:0.###} RG {sd.W:0.##} w {dash}{ax:F2} {ay:F2} m {bx:F2} {by:F2} l S "));
        }
        sb.Append("Q\n");
        page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
    }

    /// <summary>The inline style of a tag's attribute string, or an empty string.</summary>
    private static string InlineStyleOf(string attrs)
        => Regex.Match(attrs, @"\bstyle\s*=\s*(['""])(.*?)\1", RegexOptions.IgnoreCase | RegexOptions.Singleline) is { Success: true } m
            ? m.Groups[2].Value
            // (an unquoted value runs to the next space: the enterprise summary's framed 300 px wrapper)
            : Regex.Match(attrs, @"\bstyle\s*=\s*([^\s>""']+)", RegexOptions.IgnoreCase) is { Success: true } u ? u.Groups[1].Value : "";

    /// <summary>A wrapper table's CSS chrome: its own border sides (none when a border attribute frames it) and its
    /// single cell's; every box paints its own borders, so both stand between the wrapper's box and its children.</summary>
    private static (CssBorderSide[] table, CssBorderSide[] cell) WrapperCssChrome(string tableHtml, string wrapperAttrs)
    {
        var none = CssBorderSides("");
        // (a `border=0` attribute frames nothing: the CSS sides still paint - measured on the
        // enterprise summary's `border=0 style=border-width:1px;border-style:Solid` wrappers)
        var tableSides = Regex.IsMatch(wrapperAttrs, @"\bborder\s*=\s*[""']?[1-9]", RegexOptions.IgnoreCase)
            ? none : CssBorderSides(InlineStyleOf(wrapperAttrs));
        var td = Regex.Match(tableHtml, @"<td\b([^>]*)>", RegexOptions.IgnoreCase);
        var cellSides = td.Success ? CssBorderSides(InlineStyleOf(td.Groups[1].Value)) : none;
        return (tableSides, cellSides);
    }

    /// <summary>The wrapper's and its cell's CSS borders, painted around the children's box: the cell's sides hug the
    /// children, the wrapper's sides stand one cell border further out, top and bottom at the wrapper's own edges.</summary>
    private static void PaintWrapperCssFrames(Page page, (CssBorderSide[] table, CssBorderSide[] cell) chrome,
        double childX0, double childX1, double topY, double botY, System.Globalization.CultureInfo invc)
    {
        var (ts, cs) = chrome;
        var cellX0 = childX0 - cs[3].W;
        var cellX1 = childX1 + cs[1].W;
        EmitCssSideFrames(page, cs, cellX0, cellX1, topY - ts[0].W, botY + ts[2].W, invc);
        EmitCssSideFrames(page, ts, cellX0 - ts[3].W, cellX1 + ts[1].W, topY, botY, invc);
    }
}
