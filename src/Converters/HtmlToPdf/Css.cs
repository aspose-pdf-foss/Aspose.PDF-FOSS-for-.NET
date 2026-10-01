using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Apply the document stylesheet's type-selector and class-selector rules
    /// to <paramref name="s"/> for an element with tag <paramref name="tag"/> and the
    /// given attributes. Type rule first, then each class (left-to-right) — matching the
    /// simple cascade the converter needs for font-family / size.</summary>
    private static void ApplyCssRules(IReadOnlyDictionary<string, Dictionary<string, string>>? css, string tag, Dictionary<string, string>? attrs, BlockStyle s, bool metricLayout = false, bool coverStyles = false, bool floatFlow = false)
    {
        var cq = new CssRulesApplyState();
        cq.css = css;
        cq.tag = tag;
        cq.attrs = attrs;
        cq.s = s;
        cq.metricLayout = metricLayout;
        cq.coverStyles = coverStyles;
        cq.floatFlow = floatFlow;
        if (cq.css is null || cq.css.Count == 0) return;
        cq.tagLower = cq.tag.ToLowerInvariant();
        ApplyCssSelector(cq, cq.tagLower);
        if (cq.attrs is not null && cq.attrs.TryGetValue("class", out var cls) && !string.IsNullOrWhiteSpace(cls))
            foreach (var c in cls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                // Styled-article dialect: the responsive GRID classes model the
                // SCREEN column system — rows' negative gutters cancel column
                // paddings exactly, and the print layout nets the
                // whole family to zero. Skip their box rules rather than
                // accumulate one side of a pair (content sits at x=90).
                if (cq.s.ArticleRhythm && Regex.IsMatch(c,
                        @"^(container(-\w+)?|row|col(-\w+)*|split|[mp][slxeytb]?-(\w+-)?\d)$",
                        RegexOptions.IgnoreCase))
                    continue;
                ApplyCssSelector(cq, "." + c);
                ApplyCssSelector(cq, cq.tagLower + "." + c); // compound "tag.class" (e.g. h1.page)
            }
        // An id is at least as specific as any class — an "#elem { … }" rule
        // resolves for the element that carries the id, through the same
        // restricted property subset every other selector form gets.
        if (cq.attrs is not null && cq.attrs.TryGetValue("id", out var idAttr)
            && !string.IsNullOrWhiteSpace(idAttr))
            ApplyCssSelector(cq, "#" + idAttr.Trim());
    }

    /// <summary>Parse a tiny subset of CSS from the document's &lt;style&gt; blocks into
    /// a selector → declarations map. Handles comma-separated type and class selectors
    /// (".a", "div", "th, td"); everything else (descendant combinators, ids, media
    /// queries) is ignored. Used only to resolve font-family / size for HTML→PDF.</summary>
    /// <summary>True when a semantic &lt;header&gt;/&lt;footer&gt; resolves to
    /// <c>position: fixed</c> — via an inline <c>style</c>, a type rule for the tag, or a rule for
    /// one of its classes — i.e. a running region that repeats on every page. A header/footer that is
    /// normal flow content (no fixed positioning) returns false and stays in document flow.</summary>
    private static bool IsFixedRegion(string openTagAttrs, string tagName,
        IReadOnlyDictionary<string, Dictionary<string, string>> css)
    {
        static bool PinsFixed(string? decl) =>
            decl is not null && Regex.IsMatch(decl, @"position\s*:\s*fixed", RegexOptions.IgnoreCase);

        var styleM = Regex.Match(openTagAttrs, @"style\s*=\s*(['""])(?<v>.*?)\1", RegexOptions.IgnoreCase);
        if (styleM.Success && PinsFixed(styleM.Groups["v"].Value)) return true;

        bool RulePinsFixed(string key) =>
            css.TryGetValue(key, out var d) && d.TryGetValue("position", out var p)
            && p.Trim().Equals("fixed", StringComparison.OrdinalIgnoreCase);
        if (RulePinsFixed(tagName)) return true;

        var classM = Regex.Match(openTagAttrs, @"class\s*=\s*(['""])(?<v>.*?)\1", RegexOptions.IgnoreCase);
        if (classM.Success)
            foreach (var c in classM.Groups["v"].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                if (RulePinsFixed("." + c)) return true;
        return false;
    }

    /// <summary>Resolve CSS <c>var(--name[, fallback])</c> references in a declaration value
    /// against the collected custom-property map. Unknown names with no fallback resolve to
    /// empty. Custom properties are treated as document-global (last definition wins) — enough
    /// for the common <c>:root</c> / single-rule usage the converter needs.</summary>
    private static string ResolveCssVars(string value, Dictionary<string, string> vars)
    {
        if (value.IndexOf("var(", StringComparison.OrdinalIgnoreCase) < 0) return value;
        return Regex.Replace(value, @"var\(\s*(--[\w-]+)\s*(?:,\s*([^()]*?)\s*)?\)",
            m =>
            {
                if (vars.TryGetValue(m.Groups[1].Value, out var v) && v.Length > 0) return v;
                return m.Groups[2].Success ? m.Groups[2].Value : "";
            }, RegexOptions.IgnoreCase);
    }

    /// <summary>The legacy HTML-comment wrapper round a style block's text (<c>&lt;!-- … --&gt;</c>)
    /// hides nothing from a stylesheet parser, yet the flat parser keeps reading the first rule's
    /// selector as <c>&lt;!-- span.cls_007</c> - the calibrated flows were measured that way. The
    /// absolutely positioned page reads its class rules from a document with the markers removed.</summary>
    private static string StripStyleCommentMarkers(string html)
        => Regex.Replace(html, @"(<style\b[^>]*>)([\s\S]*?)(</style>)",
            m => m.Groups[1].Value + m.Groups[2].Value.Replace("<!--", " ").Replace("-->", " ") + m.Groups[3].Value,
            RegexOptions.IgnoreCase);

    /// <summary>An ELEMENT rule of the sheet by its tag, read through the legacy comment wrapper too:
    /// the flat parser keys a sheet's first rule as <c>&lt;!-- td</c> (see StripStyleCommentMarkers),
    /// and a law that reads the tag rule must still find it there. The calibrated flows keep their
    /// own plain lookups.</summary>
    /// <summary>A rule of the sheet keyed as the legacy comment wrapper leaves its FIRST rule (`&lt;!-- H2`):
    /// the flat parser keeps the marker in the key, and a law that needs the rule finds it here (measured
    /// on the enterprise summary: `H2 { Arial 18px bold }` drew the UA serif 18 until it did).</summary>
    private static Dictionary<string, string>? CommentWrappedRule(IReadOnlyDictionary<string, Dictionary<string, string>>? css, string key)
    {
        if (css is null) return null;
        foreach (var kv in css)
        {
            var k = kv.Key.TrimStart();
            if (k.StartsWith("<!--", StringComparison.Ordinal) && k.Substring(4).Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                return kv.Value;
        }
        return null;
    }

    private static Dictionary<string, string>? ElementRule(IReadOnlyDictionary<string, Dictionary<string, string>>? css, string tag)
    {
        if (css is null) return null;
        if (css.TryGetValue(tag, out var rule)) return TableDescendantRule(css, tag, rule);
        foreach (var kv in css)
        {
            var key = kv.Key.TrimStart();
            if (!key.StartsWith("<!--", StringComparison.Ordinal)) continue;
            if (key.Substring(4).Trim().Equals(tag, StringComparison.OrdinalIgnoreCase)) return TableDescendantRule(css, tag, kv.Value);
        }
        return TableDescendantRule(css, tag, null);
    }

    /// <summary>A cell's element rule with the sheet's `table td` / `tr td` style descendant rules
    /// folded in: a cell always stands in a table and a row, so such a rule reaches every cell as
    /// the bare element rule does (probed on the state analysis: `table td { white-space: nowrap }`
    /// keeps every cell on one line and grows the sheet to the grid).</summary>
    private static Dictionary<string, string>? TableDescendantRule(IReadOnlyDictionary<string, Dictionary<string, string>> css, string tag, Dictionary<string, string>? rule)
    {
        if (tag is not ("td" or "th")) return rule;
        Dictionary<string, string>? merged = null;
        foreach (var kv in css)
        {
            var parts = kv.Key.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !parts[^1].Equals(tag, StringComparison.OrdinalIgnoreCase)) continue;
            var plain = true;
            for (var i = 0; i < parts.Length - 1 && plain; i++)
                plain = parts[i].ToLowerInvariant() is "table" or "tbody" or "thead" or "tfoot" or "tr";
            if (!plain) continue;
            merged ??= rule is null ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) : new Dictionary<string, string>(rule, StringComparer.OrdinalIgnoreCase);
            foreach (var d in kv.Value) merged[d.Key] = d.Value;
        }
        return merged ?? rule;
    }

    /// <summary>The first class the body tag carries, or null.</summary>
    private static string? BodyClassName(string html)
    {
        var m = Regex.Match(html, @"<body\b[^>]*\bclass\s*=\s*[""']?([\w-]+)", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>A selector chain rooted at the body's own class, keyed by its tail: the root is
    /// dropped, and so is every wrapper on the way that adds no selectivity - a bare `div`, a `div.X`
    /// the document carries once, the table structure tags - while a classed table or any other
    /// part stays. Null when the chain is not body-rooted or still names more than two parts
    /// (probed on the change-control print sheet: `.ev-print > div.fields table th` is every th,
    /// `.ev-print > div.fields table.text-content tr > td` the text-content grid's cells alone).</summary>
    private static string? FlattenBodyClassChain(string key, string bodyClass, string html)
    {
        var parts = Regex.Split(key.Trim(), @"\s*>\s*|\s+");
        if (parts.Length < 2 || !parts[0].Equals("." + bodyClass, StringComparison.OrdinalIgnoreCase)) return null;
        var kept = new List<string>();
        for (var i = 1; i < parts.Length - 1; i++)
        {
            var p = parts[i];
            if (p.Equals("div", StringComparison.OrdinalIgnoreCase)
                || p.ToLowerInvariant() is "tbody" or "thead" or "tfoot" or "tr") continue;
            if (Regex.Match(p, @"^div\.([\w-]+)$", RegexOptions.IgnoreCase) is { Success: true } wrap
                && Regex.Matches(html, @"class\s*=\s*[""'][^""']*\b" + Regex.Escape(wrap.Groups[1].Value) + @"\b", RegexOptions.IgnoreCase).Count == 1)
                continue;
            kept.Add(p);
        }
        kept.Add(parts[^1]);
        return kept.Count > 2 ? null : string.Join(" ", kept);
    }

    internal static Dictionary<string, Dictionary<string, string>> ParseStyleSheet(string html)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        // Phase 1: gather all CSS custom properties (--name: value) across every <style>
        // block — including :root and any rule — into a document-global map so var()
        // references resolve regardless of the selector that declared them.
        var vars = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match block in Regex.Matches(html, @"<style[^>]*>([\s\S]*?)</style>", RegexOptions.IgnoreCase))
        {
            var cssText = Regex.Replace(block.Groups[1].Value, @"/\*[\s\S]*?\*/", "");
            foreach (Match cv in Regex.Matches(cssText, @"(--[\w-]+)\s*:\s*([^;}]+)"))
                vars[cv.Groups[1].Value] = cv.Groups[2].Value.Trim();
        }
        var bodyClass = BodyClassName(html);
        foreach (Match block in Regex.Matches(html, @"<style[^>]*>([\s\S]*?)</style>", RegexOptions.IgnoreCase))
        {
            var css = Regex.Replace(block.Groups[1].Value, @"/\*[\s\S]*?\*/", "");
            // (the legacy HTML comment wrapper round a sheet is not part of its first selector - a
            // rule keyed `<!-- H2` reaches no heading; measured on the enterprise summary, whose
            // `H2 { Arial 18px bold }` drew the UA serif 18)
            // (the legacy comment wrapper stays in the FIRST rule's key: the calibrated flows read it
            // that way; a law that needs the rule reads it through CommentWrappedRule)
            // @media groups resolve for the PRINT target: screen-only groups drop
            // whole, every other group unwraps in place — its rules then merge in
            // document order (a trailing @media print block overrides the base).
            css = FlattenMediaBlocks(css);
            foreach (Match rule in Regex.Matches(css, @"([^{}]+)\{([^{}]*)\}"))
            {
                var selectors = rule.Groups[1].Value;
                var body = rule.Groups[2].Value;
                var decls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (Match d in StyleDeclRx.Matches(body))
                {
                    var prop = d.Groups[1].Value.Trim().ToLowerInvariant();
                    var val = ResolveCssVars(d.Groups[2].Value.Trim(), vars);
                    // CSS grammar: a QUOTED value ('MARGIN-LEFT:"10PT"') is a string
                    // token, invalid for every property whose grammar takes lengths/
                    // keywords — the declaration is dropped, exactly as the source
                    // renderer drops it (the legacy quoted-stylesheet corpus then
                    // renders in pure UA defaults). font-family and content DO take
                    // strings: a quoted family is ONE literal (unknown names fall
                    // back to the default face downstream).
                    if (val.Length >= 2 && (val[0] == '"' || val[0] == '\'') && val[^1] == val[0]
                        && prop is not ("font-family" or "content"))
                        continue;
                    decls[prop] = val;
                }
                if (decls.Count == 0) continue;
                foreach (var sel in selectors.Split(','))
                {
                    var key = sel.Trim();
                    // A chain rooted at the BODY's own class reaches every element its tail names
                    // through the wrappers it walks: the tail alone keys it (a pseudo-class on the
                    // tail kept), so the print sheet's `.ev-print > div.fields table th` rules
                    // its th cells and `.ev-print h1` its heading.
                    var bodyChain = false;
                    if (bodyClass is not null && FlattenBodyClassChain(key, bodyClass, html) is { } flat)
                    { key = flat; bodyChain = true; }
                    // A child chain through table structure (".cls > tbody > tr > td") says
                    // the same thing as the descendant form the parser already collapses
                    // (".cls tr td" → ".cls td"): tbody/thead/tfoot/tr add no selectivity
                    // between a table and its cells. Rewrite those to the descendant form
                    // so the cell grid picks the rule up; every other combinator still
                    // disqualifies the selector.
                    key = Regex.Replace(key, @"\s*>\s*(tbody|thead|tfoot|tr)\b", " ",
                        RegexOptions.IgnoreCase);
                    key = Regex.Replace(key, @"\s*>\s*(t[dh])\b", " $1", RegexOptions.IgnoreCase);
                    if (key.Length == 0 || key.IndexOfAny(bodyChain ? new[] { '>', '+', '~', '[' } : new[] { '>', '+', '~', ':', '[' }) >= 0)
                        continue;
                    // Simple type / class / id selectors, plus two-part descendant
                    // selectors ("#gbz .gbzt") normalized to a single space — the
                    // styled-run resolver matches those against the ancestor chain.
                    var parts = key.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    // A three-part chain through a purely structural table container
                    // (".listTable tr td") collapses to its ends: a td is always
                    // inside a tr, so the middle part adds no selectivity.
                    if (parts.Length == 3 && parts[1] is "tr" or "tbody" or "thead" or "tfoot")
                        parts = new[] { parts[0], parts[2] };
                    // (…and a chain from the table through its cell to the cell's content
                    // ("table td p") collapses the table the same way: a cell always stands in one)
                    if (parts.Length == 3 && parts[0].Equals("table", StringComparison.OrdinalIgnoreCase)
                        && parts[1].ToLowerInvariant() is "td" or "th")
                        parts = new[] { parts[1], parts[2] };
                    if (parts.Length > 2) continue;
                    key = string.Join(" ", parts);
                    if (!result.TryGetValue(key, out var existing))
                        result[key] = new Dictionary<string, string>(decls, StringComparer.OrdinalIgnoreCase);
                    else
                        foreach (var kv in decls) existing[kv.Key] = kv.Value;
                }
            }
        }
        return result;
    }

    // Inline-box layout constants of the status-report dialect — named per the
    // dialect convention so none of them reads as an ad-hoc number:
    /// <summary>White gap between adjacent inline-block boxes — the collapsed markup
    /// whitespace (2 pt between title plates/pills).</summary>
    private const double InlineBoxSiblingGapPt = 2.0;

    /// <summary>Extra slack a boxed cell claims beyond its boxes' extent: column
    /// shares are fixed at BUILD time against the builder's available width, and the
    /// DRAW resolves them against the real box — the two differ by rounding, and
    /// without the slack the neighbouring column can land ON the last box.</summary>
    private const double InlineBoxColumnSlackPt = 4.0;

    /// <summary>Vertical white inset of a status pill inside its line box
    /// (1–2 pt above and below the rounded rectangle).</summary>
    private const double PillLineInsetPt = 1.5;

    /// <summary>A declared-height plate's breathing inside its stack — a
    /// title cell's content height is its plate + 2·2 pt.</summary>
    private const double PlateBreathingPt = 2.0;

    /// <summary>Gap between a badge box's label text and its trailing circle
    /// (label end → circle ≈ 4.5 pt).</summary>
    private const double BadgeLabelGapPt = 4.5;

    /// <summary>Horizontal inset of a chain-dialect cell's content from the row
    /// border (~2 pt between the border and the pills/grids).</summary>
    private const double ChainCellSideInsetPt = 2.0;

    /// <summary>The UA's default <c>border-spacing: 2px</c> on tables that do NOT
    /// declare <c>border-collapse: collapse</c> — their cell borders separate by it
    /// (the Managers grid's white gaps are 1px border + 2px spacing + 1px border).</summary>
    private const double SeparateBorderSpacingPt = 1.5;

    /// <summary>The UA's default <c>td {{ padding: 1px }}</c> in points.</summary>
    internal const double UaCellPadPt = 0.75;

    /// <summary>The UA's initial font size (16 px) in points — the em a body-level
    /// length resolves against when the stylesheet declares no size of its own.</summary>
    private const double DefaultBodyFontPt = 12.0;

    /// <summary>The CSS root font size — 16px at 96dpi. `rem` lengths resolve
    /// against it, and the styled-article flow's content box inherits it
    /// (`.td-content { font-size: 1rem }` in the docs-site sheets).</summary>
    private const double CssRootFontPt = 12.0;

    // The styled-article block rhythm of a
    // docs-site page at the 12pt root (values scale with the root):
    // paragraphs and lists carry `margin: 0 0 1rem`; list items pitch one
    // line box + 3pt (the sheet's own item gap); list content sits 21.3pt
    // inside its list box with the bullet leading the text run; headings keep
    // `margin-top 2rem / margin-bottom 1rem` (h1 opens the page: ½rem below).
    private const double ArticleListIndentPt = 21.3;

    private const double ArticleLiGapPt = 3.0;

    /// <summary>Panel (`.td-toc`) list geometry:
    /// level-1 items 33.3pt inside the content edge, 36pt more per nesting level.</summary>
    private const double ArticleTocIndentPt = 33.3;

    private const double ArticleTocLevelPt = 36.0;

    /// <summary>Space above the article's opening h1 (h1 margin-top 2rem
    /// + the content row's .5rem padding + the body's 2px top border).</summary>
    private const double ArticleH1TopPt = 31.5;

    /// <summary>Panel (`.td-toc`) vertical box: .5rem padding above the
    /// header, 23.5pt below the last item, then the panel's 2rem margin-bottom.</summary>
    private const double ArticlePanelPadTopPt = 6.0;

    private const double ArticlePanelPadBottomPt = 23.5;

    private const double ArticlePanelMarginBottomPt = 24.0;

    // Verdana form-grid quantities: this dialect lays out on a
    // whole-CSS-px grid at 96 dpi. A line's `line-height: normal` box is the
    // face's Windows line (usWinAscent + usWinDescent over the em) at the run's
    // px size, rounded to whole px — Verdana 12pt/16px → 19px = 14.25pt,
    // 10pt → 16px = 12.0, 36pt/48px → 58px = 43.5; Times New Roman 12pt →
    // 18px = 13.5. A cell's floor (the CSS strut) is the box of the AMBIENT
    // font at the tag soup's default size: Verdana 12 inside the wrapper's
    // <font face='Verdana'>, the serif default outside it — every
    // row height decomposes as
    // max(line boxes, strut) + borders + padding, exact to 0.01pt.
    internal const double VerdanaWinLineRatio = (2059.0 + 430.0) / 2048.0;

    internal const double SerifWinLineRatio = (1825.0 + 443.0) / 2048.0;

    /// <summary>Verdana's Windows ascent/descent shares of the em. A form-grid
    /// line's baseline sits max(strut drop, run drop) below the cell content
    /// top, each drop = half-leading + winAscent of its box (exact on
    /// five row anchors: label 115.40, member 589.40/604.40/617.90, band
    /// 544.40, description 440.83).</summary>
    internal const double VerdanaWinAscent = 2059.0 / 2048.0;

    internal const double VerdanaWinDescent = 430.0 / 2048.0;

    /// <summary>Times New Roman's Windows ascent/descent shares of the em —
    /// the glyph box inside a serif flow line (baseline seat =
    /// half-leading + ascent).</summary>
    internal const double SerifWinAscent = 1825.0 / 2048.0;

    internal const double SerifWinDescent = 443.0 / 2048.0;

    /// <summary>The HTML default font size (`size=3`) the form-grid struts
    /// resolve against.</summary>
    internal const double FormGridBasePt = 12.0;

    /// <summary>A face's `line-height: normal` box at 96 dpi: whole CSS px,
    /// in points.</summary>
    internal static double PxLinePt(double fontPt, double winRatio) =>
        Math.Round(fontPt / 0.75 * winRatio, MidpointRounding.AwayFromZero) * 0.75;

    /// <summary>Fallback strut when a form-grid caller passes none: the
    /// Verdana-12 19px box.</summary>
    internal const double VerdanaGridMinLinePt = 19.0 * 0.75;

    /// <summary>A small background-image badge (`background: url(…)` on an
    /// inline-block box, the status-light idiom): its fill is sampled from the
    /// referenced image's centre pixel and its size taken from the image's own
    /// pixel dimensions — everything derives from the document's asset, nothing
    /// is keyed on class names. Null when the decls carry no loadable image.</summary>
    private static (Color Fill, double DiameterPt)? BackgroundBadge(
        Dictionary<string, string> decls, HtmlLoadOptions? options)
    {
        if (!decls.TryGetValue("background", out var bg)
            && !decls.TryGetValue("background-image", out bg)) return null;
        var um = Regex.Match(bg, @"url\(\s*[""']?([^""')]+)[""']?\s*\)", RegexOptions.IgnoreCase);
        if (!um.Success) return null;
        var data = LoadConverterImage(um.Groups[1].Value.Trim(), options);
        if (data is null) return null;
        // GDI+ sampling is Windows-only (the repo-wide System.Drawing convention);
        // elsewhere the badge simply doesn't render, like any other unloadable asset.
        if (!Compat.IsWindows()) return null;
        try
        {
#pragma warning disable CA1416
            using var ms = new System.IO.MemoryStream(data);
            using var bmp = new System.Drawing.Bitmap(ms);
            var px = bmp.GetPixel(bmp.Width / 2, bmp.Height / 2);
            if (px.A < 32) return null;
            return (Color.FromRgbBytes(px.R, px.G, px.B), Math.Max(bmp.Width, bmp.Height) * 0.75);
#pragma warning restore CA1416
        }
        catch { return null; }
    }

    /// <summary>CSS padding shorthand → (top, right, bottom, left) points.</summary>
    private static (double T, double R, double B, double L) ChainPadPt(string v, double fontPt)
    {
        var parts = v.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        double P(int i) => i < parts.Length ? Math.Max(0, ChainLenPt(parts[i], fontPt)) : 0;
        return parts.Length switch
        {
            0 => (0, 0, 0, 0),
            1 => (P(0), P(0), P(0), P(0)),
            2 => (P(0), P(1), P(0), P(1)),
            3 => (P(0), P(1), P(2), P(1)),
            _ => (P(0), P(1), P(2), P(3)),
        };
    }

    /// <summary>A rule's padding as (top, right, bottom, left) points: the shorthand first, then any
    /// side LONGHAND over it. A stylesheet that only ever writes <c>padding-left</c>/<c>padding-right</c>
    /// declares the cell's box just as a shorthand does, and reading the shorthand alone loses it.</summary>
    private static (double T, double R, double B, double L) ChainPadSidesPt(
        IReadOnlyDictionary<string, string> decls, double fontPt, bool readLonghands)
    {
        var (t, r, b, l) = decls.TryGetValue("padding", out var sh)
            ? ChainPadPt(sh, fontPt) : (0, 0, 0, 0);
        if (!readLonghands) return (t, r, b, l);
        double Side(string name, double had)
            => decls.TryGetValue(name, out var v) ? Math.Max(0, ChainLenPt(v, fontPt)) : had;
        return (Side("padding-top", t), Side("padding-right", r),
                Side("padding-bottom", b), Side("padding-left", l));
    }

    /// <summary>The em base a sheet's percent body size sets for this conversion (0 = the legacy 11 pt).</summary>
    [ThreadStatic] private static double SheetEmBasePt;

    private static double? TryParseLength(string s)
    {
        double pts = 0;
        // Accept "13px" / "10pt" / "1em" / ".875rem" / "6.25in" — CSS permits a
        // bare leading dot. Reject percent / calc / etc.
        var m = Regex.Match(s, @"^(-?(?:\d+(?:\.\d+)?|\.\d+))\s*(px|pt|em|rem|in|cm|mm)?$", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        var n = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        var unit = m.Groups[2].Success ? m.Groups[2].Value.ToLowerInvariant() : "px";
        pts = unit switch
        {
            "pt" => n,
            "px" => n * 0.75,          // 96dpi: 1px = 0.75pt
            // (…or against the body's PERCENT size when the sheet declares one - the hospital letter's
            //  `body { font-size: 62.5% }` sizes its 1.02em cells 7.65 pt, probed; a px/pt body keeps the legacy 11)
            "em" => n * (SheetEmBasePt > 0 ? SheetEmBasePt : 11),
            "rem" => n * CssRootFontPt,
            "in" => n * 72,
            "cm" => n * 72 / 2.54,
            "mm" => n * 72 / 25.4,
            _ => n,
        };
        return pts > 0 ? pts : null;
    }

    /// <summary>True for an explicit zero length (<c>0</c>, <c>0px</c>, <c>0.0em</c>) — the
    /// one value <see cref="TryParseLength"/> cannot hand back, because its null means
    /// "no usable length" to every caller that reads a default through it.</summary>
    private static bool IsZeroLength(string s) =>
        Regex.IsMatch(s, @"^-?0+(?:\.0+)?\s*(?:px|pt|em|rem|in|cm|mm|%)?$", RegexOptions.IgnoreCase);

    private static void MarkInline(Stack<BlockStyle> stack, string fontRes)
    {
        // Inline emphasis modifies the *current* block's style mid-stream.
        // Minimal fidelity: promote the whole block to the emphasised font
        // when any part of it uses <b>/<i>. Real mixed-style output would
        // require splitting Block into sub-runs.
        if (stack.Count == 0) return;
        var top = stack.Peek();
        if (top.FontRes == "F1") top.FontRes = fontRes;
        // Track bold/italic independently so the embedded-face path can combine them
        // (FontRes alone collapses <b><i> to whichever emphasis opened first).
        if (fontRes == "F2") top.EmBold = true;
        else if (fontRes == "F3") top.EmItalic = true;
    }

    /// <summary>`font-size: smaller`, the UA sheet's size for sub and sup: one step down the
    /// scale, i.e. the size divided by the 1.2 ratio between neighbouring steps. Measured against
    /// the reference on a marked stretch of a 12 pt serif line: it draws 132.95 pt wide at the
    /// block's own size and 110.88 in the reference, and 132.95 / 1.2 = 110.79.</summary>
    private const double SubSupFontScale = 1.0 / 1.2;

    private static void MarkInlineSize(Stack<BlockStyle> stack, double factor)
    {
        if (stack.Count == 0) return;
        var top = stack.Peek();
        top.FontSize *= factor;
    }

    /// <summary>Apply ONLY an inline element's font-family — a
    /// <c>&lt;span style="font-family:…"&gt;</c> or <c>&lt;font face="…"&gt;</c> — to the
    /// current run by mutating the top-of-stack block style. Deliberately layout-neutral:
    /// any size/margin the same element declares is ignored (the wrap metric is
    /// family-independent), so pagination is unchanged and only the rendered face differs.</summary>
    private static void MarkInlineFontFamily(Stack<BlockStyle> stack, Dictionary<string, string>? attrs)
    {
        if (stack.Count == 0 || attrs is null) return;
        string? fam = null;
        Color? color = null;
        if (attrs.TryGetValue("style", out var styleStr) && !string.IsNullOrWhiteSpace(styleStr))
        {
            foreach (Match m in StyleDeclRx.Matches(styleStr))
            {
                var prop = m.Groups[1].Value.Trim();
                if (prop.Equals("font-family", StringComparison.OrdinalIgnoreCase))
                    fam = FirstFontFamily(m.Groups[2].Value.Trim());
                else if (prop.Equals("color", StringComparison.OrdinalIgnoreCase))
                    color = ParseCssColor(m.Groups[2].Value.Trim());
            }
        }
        if (fam is null && attrs.TryGetValue("face", out var face))
            fam = FirstFontFamily(face);
        // Legacy <font color="…"> attribute (named or #hex).
        if (color is null && attrs.TryGetValue("color", out var colAttr))
            color = ParseCssColor(colAttr.Trim());
        // Legacy <font size="1".."7"> attribute → point size (3 = medium = 12pt). Stored
        // separately (LegacyFontPt) so it stays inert for the legacy flow.
        // Browser-style value parse: read the leading integer (junk suffixes like
        // "8px" still count) and clamp into the 1..7 scale — size=8px renders as 7.
        if (attrs.TryGetValue("size", out var sizeAttr))
        {
            var st = sizeAttr.Trim();
            var digitsEnd = 0;
            while (digitsEnd < st.Length && (char.IsDigit(st[digitsEnd])
                   || (digitsEnd == 0 && (st[0] == '+' || st[0] == '-')))) digitsEnd++;
            if (digitsEnd > 0 && int.TryParse(st[..digitsEnd], out var sz))
            {
                // A leading +N/-N is relative to the default size 3.
                if (st[0] is '+' or '-') sz = 3 + sz;
                sz = Compat.Clamp(sz, 1, 7);
                stack.Peek().LegacyFontPt = HtmlFontSizeToPt(sz);
                stack.Peek().LegacyFontSized = true;
            }
        }
        var top = stack.Peek();
        if (fam is not null) top.FontFamily = fam;
        if (color is not null) top.ForeColor = color;
    }

    /// <summary>Legacy HTML &lt;font size="N"> (1-7) → point size. Size 3 is the browser
    /// default "medium" (16px = 12pt); the curve follows the classic HTML mapping.</summary>
    private static double HtmlFontSizeToPt(int size) => size switch
    {
        1 => 7.5, 2 => 10, 3 => 12, 4 => 13.5, 5 => 18, 6 => 24, _ => 36,
    };

    // ── Styled-run row extraction (nav bars, centered link rows) ────────────

    /// <summary>True when the simple selector ("tag", ".class", "tag.class", "#id")
    /// matches the element.</summary>
    private static bool SimpleSelectorMatches(string sel, HtmlNode el)
    {
        if (el.Tag.Length == 0 || sel.Length == 0) return false;
        if (sel[0] == '#')
            return el.Attrs is not null && el.Attrs.TryGetValue("id", out var id)
                   && id.Trim().Equals(sel.Substring(1), StringComparison.Ordinal);
        var tagPart = sel;
        var clsPart = "";
        var dot = sel.IndexOf('.');
        if (dot >= 0) { tagPart = sel.Substring(0, dot); clsPart = sel.Substring(dot + 1); }
        if (tagPart.Length > 0 && !el.Tag.Equals(tagPart, StringComparison.OrdinalIgnoreCase))
            return false;
        if (clsPart.Length > 0)
        {
            if (el.Attrs is null || !el.Attrs.TryGetValue("class", out var cls) || string.IsNullOrEmpty(cls))
                return false;
            var found = false;
            foreach (var c in cls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (c.Equals(clsPart, StringComparison.Ordinal)) { found = true; break; }
            if (!found) return false;
        }
        return true;
    }

    /// <summary>Resolve one CSS property on a DOM element: inline style, then two-part
    /// descendant rules whose target matches the element and whose context matches an
    /// ancestor, then #id / tag.class / .class / tag rules. "!important" suffixes are
    /// stripped from the returned value. Null = no declaration on this element.</summary>
    private static string? DomDecl(HtmlNode el,
        string prop, IReadOnlyDictionary<string, Dictionary<string, string>>? css)
    {
        static string Clean(string v)
        {
            var ix = v.IndexOf("!important", StringComparison.OrdinalIgnoreCase);
            return (ix >= 0 ? v.Substring(0, ix) : v).Trim();
        }
        if (el.Attrs is not null && el.Attrs.TryGetValue("style", out var inlineStyle)
            && !string.IsNullOrEmpty(inlineStyle))
        {
            foreach (Match m in StyleDeclRx.Matches(inlineStyle))
                if (m.Groups[1].Value.Trim().Equals(prop, StringComparison.OrdinalIgnoreCase))
                    return Clean(m.Groups[2].Value);
        }
        if (css is null || css.Count == 0) return null;

        string? best = null;
        var bestRank = -1;
        foreach (var kv in css)
        {
            if (!kv.Value.TryGetValue(prop, out var val)) continue;
            var key = kv.Key;
            int rank;
            var sp = key.IndexOf(' ');
            if (sp >= 0)
            {
                var anc = key.Substring(0, sp);
                var desc = key.Substring(sp + 1);
                if (!SimpleSelectorMatches(desc, el)) continue;
                var ancMatch = false;
                for (var p = el.Parent; p is not null; p = p.Parent)
                    if (SimpleSelectorMatches(anc, p)) { ancMatch = true; break; }
                if (!ancMatch) continue;
                rank = 4;
            }
            else if (key[0] == '#') { if (!SimpleSelectorMatches(key, el)) continue; rank = 3; }
            else if (key.IndexOf('.') > 0) { if (!SimpleSelectorMatches(key, el)) continue; rank = 2; }
            else if (key[0] == '.') { if (!SimpleSelectorMatches(key, el)) continue; rank = 1; }
            else { if (!SimpleSelectorMatches(key, el)) continue; rank = 0; }
            if (rank >= bestRank) { bestRank = rank; best = Clean(val); }
        }
        return best;
    }

    private static double ParsePxValue(string? v)
    {
        if (string.IsNullOrEmpty(v)) return 0;
        var m = Regex.Match(v, @"(-?[\d.]+)\s*px", RegexOptions.IgnoreCase);
        if (m.Success && double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var px))
            return px;
        var mp = Regex.Match(v, @"(-?[\d.]+)\s*pt", RegexOptions.IgnoreCase);
        if (mp.Success && double.TryParse(mp.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var pt))
            return pt / 0.75;
        return 0;
    }

    /// <summary>Horizontal components of a box-shorthand ("margin"/"padding": 1-4 values)
    /// combined with the -left/-right longhands. Px units only.</summary>
    private static (double left, double right) DomBoxLR(HtmlNode el, string box,
        IReadOnlyDictionary<string, Dictionary<string, string>>? css)
    {
        double left = 0, right = 0;
        var sh = DomDecl(el, box, css);
        if (!string.IsNullOrEmpty(sh))
        {
            var parts = sh.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // 1 value: all; 2: v h; 3: t h b; 4: t r b l.
            switch (parts.Length)
            {
                case 1: left = right = ParsePxValue(parts[0]); break;
                case 2: case 3: left = right = ParsePxValue(parts[1]); break;
                case 4: right = ParsePxValue(parts[1]); left = ParsePxValue(parts[3]); break;
            }
        }
        var l2 = DomDecl(el, box + "-left", css);
        if (!string.IsNullOrEmpty(l2)) left = ParsePxValue(l2);
        var r2 = DomDecl(el, box + "-right", css);
        if (!string.IsNullOrEmpty(r2)) right = ParsePxValue(r2);
        return (left, right);
    }

    /// <summary>Font size in px resolved via the inherited font-size or `font` shorthand
    /// (e.g. "13px/27px Arial"). Falls back to <paramref name="defaultPx"/>.</summary>
    private static double DomFontPx(HtmlNode el, double defaultPx,
        IReadOnlyDictionary<string, Dictionary<string, string>>? css)
    {
        for (HtmlNode? n = el; n is not null; n = n.Parent)
        {
            if (n.Tag.Length == 0) continue;
            var fs = DomDecl(n, "font-size", css);
            if (!string.IsNullOrEmpty(fs)) { var v = ParsePxValue(fs); if (v > 0) return v; }
            var f = DomDecl(n, "font", css);
            if (!string.IsNullOrEmpty(f))
            {
                var m = Regex.Match(f, @"([\d.]+)\s*(px|pt)", RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    var v = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    return m.Groups[2].Value.Equals("pt", StringComparison.OrdinalIgnoreCase) ? v / 0.75 : v;
                }
            }
        }
        return defaultPx;
    }

    private static bool DomBold(HtmlNode el,
        IReadOnlyDictionary<string, Dictionary<string, string>>? css)
    {
        for (HtmlNode? n = el; n is not null; n = n.Parent)
        {
            if (n.Tag.Length == 0) continue;
            if (n.Tag is "b" or "strong") return true;
            var w = DomDecl(n, "font-weight", css);
            if (!string.IsNullOrEmpty(w))
                return w.StartsWith("bold", StringComparison.OrdinalIgnoreCase)
                       || (int.TryParse(w, out var n2) && n2 >= 600);
        }
        return false;
    }

    private static Color? DomColor(HtmlNode el,
        IReadOnlyDictionary<string, Dictionary<string, string>>? css)
    {
        for (HtmlNode? n = el; n is not null; n = n.Parent)
        {
            if (n.Tag.Length == 0) continue;
            var v = DomDecl(n, "color", css);
            if (!string.IsNullOrEmpty(v))
            {
                var c = ParseCssColor(v);
                if (c is not null) return c;
            }
        }
        return null;
    }

    /// <summary>Plain text content of an element subtree (entity-decoded, whitespace
    /// collapsed), skipping hidden descendants.</summary>
    private static string DomText(HtmlNode el,
        IReadOnlyDictionary<string, Dictionary<string, string>>? css)
    {
        var sb = new StringBuilder();
        void Walk(HtmlNode n)
        {
            foreach (var c in n.Children)
            {
                if (c.Tag.Length == 0) { sb.Append(c.Text); continue; }
                if (IsHiddenElement(c.Tag, c.Attrs, css)) continue;
                Walk(c);
            }
        }
        if (el.Tag.Length == 0) return CollapseWs(DecodeEntities(el.Text));
        Walk(el);
        return CollapseWs(DecodeEntities(sb.ToString()));
    }

    /// <summary>A document's own <c>@page</c> rules, resolved for
    /// <see cref="HtmlLoadOptions.IsPriorityCssPageRule"/>: the sheet size the CSS
    /// asks for and the margins it declares, plus the <c>:first</c> page's own top
    /// margin when it overrides the general one.</summary>
    private readonly struct CssPageRule
    {
        public double WidthPt { get; init; }
        public double HeightPt { get; init; }
        public double? MarginLeftPt { get; init; }
        public double? MarginRightPt { get; init; }
        public double? MarginTopPt { get; init; }
        public double? MarginBottomPt { get; init; }
        public double? FirstMarginTopPt { get; init; }
        public bool Any => WidthPt > 0 || MarginLeftPt is not null || MarginRightPt is not null
                           || MarginTopPt is not null || MarginBottomPt is not null
                           || FirstMarginTopPt is not null;
    }

    /// <summary>The named page sizes a CSS <c>@page { size: … }</c> may ask for, in
    /// points (portrait). CSS orders them width-then-height, so a `landscape`
    /// keyword swaps the pair.</summary>
    private static readonly Dictionary<string, (double W, double H)> CssPageSizes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["a3"] = (841.890, 1190.551),
            ["a4"] = (595.276, 841.890),
            ["a5"] = (419.528, 595.276),
            ["b4"] = (708.661, 1000.630),
            ["b5"] = (498.898, 708.661),
            ["letter"] = (612.0, 792.0),
            ["legal"] = (612.0, 1008.0),
            ["ledger"] = (1224.0, 792.0),
        };

    /// <summary>Read the document's <c>@page</c> at-rules (the general one and the
    /// <c>:first</c> page's) off its style blocks. Only sheets that reach paper are
    /// read — a <c>media="screen"</c> block styles the flow but never sizes the sheet.
    /// Returns false when the document declares no page rule at all.</summary>
    private static CssPageRule? TryReadCssPageRule(string html)
    {
        CssPageRule rule = default;
        if (html.IndexOf("@page", StringComparison.OrdinalIgnoreCase) < 0) return null;

        double w = 0, h = 0;
        double? ml = null, mr = null, mt = null, mb = null, firstTop = null;

        foreach (Match styleBlock in Regex.Matches(html, @"<style\b([^>]*)>([\s\S]*?)</style\s*>",
                     RegexOptions.IgnoreCase))
        {
            var mediaM = Regex.Match(styleBlock.Groups[1].Value, @"\bmedia\s*=\s*[""']?([^""'>]*)",
                RegexOptions.IgnoreCase);
            if (mediaM.Success && !Regex.IsMatch(mediaM.Groups[1].Value, @"\b(all|print)\b",
                    RegexOptions.IgnoreCase))
                continue;
            // `@page <pseudo>? { … }` — the pseudo-page selector (:first / :left /
            // :right / a named page) decides which sheets the block applies to; only
            // the un-pseudo'd rule and `:first` are modelled.
            foreach (Match pageAt in Regex.Matches(styleBlock.Groups[2].Value,
                         @"@page\s*(?<sel>[^{]*)\{(?<body>[^{}]*)\}", RegexOptions.IgnoreCase))
            {
                var sel = pageAt.Groups["sel"].Value.Trim();
                var body = pageAt.Groups["body"].Value;
                var isFirst = sel.StartsWith(":first", StringComparison.OrdinalIgnoreCase);
                if (sel.Length > 0 && !isFirst) continue;

                if (!isFirst)
                {
                    var sizeM = Regex.Match(body, @"\bsize\s*:\s*([^;}]+)", RegexOptions.IgnoreCase);
                    if (sizeM.Success)
                    {
                        var tokens = sizeM.Groups[1].Value.Trim()
                            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                        var landscape = false;
                        var lens = new List<double>();
                        foreach (var tk in tokens)
                        {
                            if (string.Equals(tk, "landscape", StringComparison.OrdinalIgnoreCase))
                            { landscape = true; continue; }
                            if (string.Equals(tk, "portrait", StringComparison.OrdinalIgnoreCase)) continue;
                            if (CssPageSizes.TryGetValue(tk, out var named)) { w = named.W; h = named.H; continue; }
                            if (TryParseLength(tk) is { } lp) lens.Add(lp);
                        }
                        if (lens.Count == 2) { w = lens[0]; h = lens[1]; }
                        else if (lens.Count == 1) { w = lens[0]; h = lens[0]; }
                        if (landscape && w > 0 && w < h) (w, h) = (h, w);
                    }
                }

                double? Side(string prop)
                {
                    var m = Regex.Match(body, @"\bmargin-" + prop + @"\s*:\s*([^;}]+)",
                        RegexOptions.IgnoreCase);
                    return m.Success && TryParseLength(m.Groups[1].Value.Trim()) is { } v ? v : null;
                }
                // The `margin` shorthand's 1-to-4 values seed every side the longhands
                // do not restate (CSS top / right / bottom / left order).
                double? sT = null, sR = null, sB = null, sL = null;
                var shorthand = Regex.Match(body, @"(?<![-\w])margin\s*:\s*([^;}]+)", RegexOptions.IgnoreCase);
                if (shorthand.Success)
                {
                    var parts = shorthand.Groups[1].Value.Trim()
                        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    var vals = new List<double>();
                    foreach (var p in parts)
                        if (TryParseLength(p) is { } pv) vals.Add(pv);
                        else vals.Clear();
                    if (vals.Count is 1) { sT = sR = sB = sL = vals[0]; }
                    else if (vals.Count is 2) { sT = sB = vals[0]; sR = sL = vals[1]; }
                    else if (vals.Count is 3) { sT = vals[0]; sR = sL = vals[1]; sB = vals[2]; }
                    else if (vals.Count is 4) { sT = vals[0]; sR = vals[1]; sB = vals[2]; sL = vals[3]; }
                }
                var top = Side("top") ?? sT;
                if (isFirst) { if (top is not null) firstTop = top; continue; }
                if (top is not null) mt = top;
                if ((Side("right") ?? sR) is { } rv) mr = rv;
                if ((Side("bottom") ?? sB) is { } bv) mb = bv;
                if ((Side("left") ?? sL) is { } lv) ml = lv;
            }
        }

        rule = new CssPageRule
        {
            WidthPt = w, HeightPt = h,
            MarginLeftPt = ml, MarginRightPt = mr, MarginTopPt = mt, MarginBottomPt = mb,
            FirstMarginTopPt = firstTop,
        };
        return (rule.Any) ? rule : null;
    }

    private static readonly HashSet<string> InlineRowTags = new(StringComparer.OrdinalIgnoreCase)
    { "a", "span", "b", "i", "em", "strong", "font", "small", "u", "sup", "sub" };
}
