using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Apply the document stylesheet's type-selector and class-selector rules
    /// to <paramref name="s"/> for an element with tag <paramref name="tag"/> and the
    /// given attributes. Type rule first, then each class (left-to-right) — matching the
    /// simple cascade the converter needs for font-family / size.</summary>
    private static void ApplyCssRules(IReadOnlyDictionary<string, Dictionary<string, string>>? css,
        string tag, Dictionary<string, string>? attrs, BlockStyle s, bool metricLayout = false,
        bool coverStyles = false, bool floatFlow = false)
    {
        if (css is null || css.Count == 0) return;
        void ApplySelector(string selector)
        {
            if (!css.TryGetValue(selector, out var decls)) return;
            foreach (var kv in decls)
            {
                // page-break-before:always — a genuine pagination directive, honoured here.
                if (kv.Key == "page-break-before"
                    && kv.Value.Contains("always", StringComparison.OrdinalIgnoreCase))
                    s.PageBreakBefore = true;
                else if (kv.Key == "page-break-after"
                    && kv.Value.Contains("always", StringComparison.OrdinalIgnoreCase))
                    s.PageBreakAfter = true;
                // Print-authored cover documents (a body{margin:0} page with an
                // explicit page-break-after separator): the cover classes' OWN type
                // scale and physical-unit margins ARE the layout — the calibrated
                // exclusion below would put the whole cover at the page top.
                // …and the float flow takes its sizes from the sheet as well: the
                // certificate's whole type scale is authored there - `.certificate`
                // sizes the body at 14 px (which is what its table cells render at) and
                // `#title` at 11 px (which is what makes its h1 2em = 16.5 pt).
                else if ((coverStyles || floatFlow) && kv.Key == "font-size")
                    ApplyDeclaration(kv.Key, kv.Value, s);
                else if (coverStyles && kv.Key == "margin")
                {
                    // TryParseLength deliberately rejects zero (callers treat 0 as
                    // "absent") — a shorthand's explicit 0 slots must still parse.
                    static bool CoverLen(string v, out double pt)
                    {
                        if (TryParseLength(v, out pt)) return true;
                        if (Regex.IsMatch(v.Trim(), @"^0(px|pt|em|rem|in|cm|mm)?$",
                                RegexOptions.IgnoreCase)) { pt = 0; return true; }
                        return false;
                    }
                    var mParts = kv.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (mParts.Length >= 1 && CoverLen(mParts[0], out var cmT))
                    {
                        s.MarginTop = cmT;
                        // The cover's margin-top positions it on PAGE 1's fresh page —
                        // it must survive the flow's page-top margin suppression.
                        if (cmT > 0) s.MarginTopAlways = true;
                        var cmB = cmT;
                        if (mParts.Length >= 3 && CoverLen(mParts[2], out var cmB3)) cmB = cmB3;
                        s.MarginBottom = cmB;
                    }
                }
                else if (coverStyles && kv.Key == "line-height"
                         && double.TryParse(kv.Value.Trim(), System.Globalization.NumberStyles.Float,
                             System.Globalization.CultureInfo.InvariantCulture, out var clhF)
                         && clhF > 0)
                    s.LineFactor = clhF;
                // Apply only layout-NEUTRAL font properties from <style> rules.
                // Size/margin/height/indent from a stylesheet are deliberately NOT
                // applied: the converter historically ignored <style> blocks entirely,
                // and honouring those here would shift wrapping/pagination and break
                // documents whose page count is asserted. font-family/weight/style
                // don't affect the metrics WordWrap uses, so they're safe to apply.
                // Font props + box decoration (background/border) are layout-NEUTRAL —
                // they change only the drawn ink, not the wrap metric or pagination — so
                // they're safe to apply from a stylesheet. Size/margin/height stay excluded.
                if (kv.Key is "font-family" or "font-weight" or "font-style" or "color"
                    or "background-color" or "background"
                    or "border" or "border-color" or "border-width"
                    or "border-top" or "border-bottom" or "border-left" or "border-right"
                    // float only RECORDS a flag; the flows that ignore floats are unaffected.
                    or "float")
                    ApplyDeclaration(kv.Key, kv.Value, s);
                // The metric flow reproduces the CSS-driven layout, so for it
                // the layout properties ARE the spec: stylesheet font sizes, MARGIN-LEFT
                // class indents, and centering all apply.
                else if (metricLayout && kv.Key is "font-size" or "margin-left" or "padding-left"
                             or "padding-bottom")
                    ApplyDeclaration(kv.Key, kv.Value, s);
                else if (metricLayout && kv.Key == "text-align")
                    s.AlignCenter = kv.Value.Trim().Equals("center", StringComparison.OrdinalIgnoreCase);
            }
            // A tiny data-URI tile repeated over an explicitly sized element paints
            // as one uniform fill (the 1×1-GIF tiling-pattern idiom). The fill and
            // the declared box travel together — neither applies without the other,
            // so a rule that carries only layout properties still changes nothing.
            if (DataUriTileFill(decls) is { } tileFill
                && decls.TryGetValue("width", out var bbw) && TryParseLength(bbw, out var bbwPt)
                && decls.TryGetValue("height", out var bbh) && TryParseLength(bbh, out var bbhPt))
            {
                s.BackgroundColor = tileFill;
                s.BgBoxWidthPt = bbwPt;
                s.BgBoxHeightPt = bbhPt;
                s.ExplicitHeight = Math.Max(s.ExplicitHeight, bbhPt);
            }
            // A solid (or alpha-composited) background over a declared width × height
            // is the same painted-box model: the fill and the declared box travel
            // together, with any border drawn as the box's chrome.
            else if ((decls.TryGetValue("background-color", out var pbBg)
                      || decls.TryGetValue("background", out pbBg))
                && ParseCssColor(pbBg) is { } pbFill
                && !(pbFill.R >= 250 && pbFill.G >= 250 && pbFill.B >= 250)
                && decls.TryGetValue("width", out var pbw) && TryParseLength(pbw, out var pbwPt)
                && decls.TryGetValue("height", out var pbh) && TryParseLength(pbh, out var pbhPt))
            {
                s.BackgroundColor = pbFill;
                s.BgBoxWidthPt = pbwPt;
                s.BgBoxHeightPt = pbhPt;
                s.ExplicitHeight = Math.Max(s.ExplicitHeight, pbhPt);
            }
        }
        var tagLower = tag.ToLowerInvariant();
        ApplySelector(tagLower);
        if (attrs is not null && attrs.TryGetValue("class", out var cls) && !string.IsNullOrWhiteSpace(cls))
            foreach (var c in cls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                // Styled-article dialect: the responsive GRID classes model the
                // SCREEN column system — rows' negative gutters cancel column
                // paddings exactly, and the print layout nets the
                // whole family to zero. Skip their box rules rather than
                // accumulate one side of a pair (content sits at x=90).
                if (s.ArticleRhythm && Regex.IsMatch(c,
                        @"^(container(-\w+)?|row|col(-\w+)*|split|[mp][slxeytb]?-(\w+-)?\d)$",
                        RegexOptions.IgnoreCase))
                    continue;
                ApplySelector("." + c);
                ApplySelector(tagLower + "." + c); // compound "tag.class" (e.g. h1.page)
            }
        // An id is at least as specific as any class — an "#elem { … }" rule
        // resolves for the element that carries the id, through the same
        // restricted property subset every other selector form gets.
        if (attrs is not null && attrs.TryGetValue("id", out var idAttr)
            && !string.IsNullOrWhiteSpace(idAttr))
            ApplySelector("#" + idAttr.Trim());
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
        foreach (Match block in Regex.Matches(html, @"<style[^>]*>([\s\S]*?)</style>", RegexOptions.IgnoreCase))
        {
            var css = Regex.Replace(block.Groups[1].Value, @"/\*[\s\S]*?\*/", "");
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
                    // A child chain through table structure (".cls > tbody > tr > td") says
                    // the same thing as the descendant form the parser already collapses
                    // (".cls tr td" → ".cls td"): tbody/thead/tfoot/tr add no selectivity
                    // between a table and its cells. Rewrite those to the descendant form
                    // so the cell grid picks the rule up; every other combinator still
                    // disqualifies the selector.
                    key = Regex.Replace(key, @"\s*>\s*(tbody|thead|tfoot|tr)\b", " ",
                        RegexOptions.IgnoreCase);
                    key = Regex.Replace(key, @"\s*>\s*(t[dh])\b", " $1", RegexOptions.IgnoreCase);
                    if (key.Length == 0 || key.IndexOfAny(new[] { '>', '+', '~', ':', '[' }) >= 0)
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
    private const double UaCellPadPt = 0.75;

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
        if (!OperatingSystem.IsWindows()) return null;
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

    private static bool TryParseLength(string s, out double pts)
    {
        pts = 0;
        // Accept "13px" / "10pt" / "1em" / ".875rem" / "6.25in" — CSS permits a
        // bare leading dot. Reject percent / calc / etc.
        var m = Regex.Match(s, @"^(-?(?:\d+(?:\.\d+)?|\.\d+))\s*(px|pt|em|rem|in|cm|mm)?$", RegexOptions.IgnoreCase);
        if (!m.Success) return false;
        var n = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        var unit = m.Groups[2].Success ? m.Groups[2].Value.ToLowerInvariant() : "px";
        pts = unit switch
        {
            "pt" => n,
            "px" => n * 0.75,          // 96dpi: 1px = 0.75pt
            "em" => n * 11,            // against our default body 11pt
            "rem" => n * CssRootFontPt,
            "in" => n * 72,
            "cm" => n * 72 / 2.54,
            "mm" => n * 72 / 25.4,
            _ => n,
        };
        return pts > 0;
    }

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
                sz = Math.Clamp(sz, 1, 7);
                stack.Peek().LegacyFontPt = HtmlFontSizeToPt(sz);
                stack.Peek().LegacyFontSized = true;
            }
        }
        var top = stack.Peek();
        if (fam is not null) top.FontFamily = fam;
        if (color is not null) top.ForeColor = color;
    }

    /// <summary>Legacy HTML <font size="N"> (1-7) → point size. Size 3 is the browser
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
    private static bool TryReadCssPageRule(string html, out CssPageRule rule)
    {
        rule = default;
        if (html.IndexOf("@page", StringComparison.OrdinalIgnoreCase) < 0) return false;

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
                            if (TryParseLength(tk, out var lp)) lens.Add(lp);
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
                    return m.Success && TryParseLength(m.Groups[1].Value.Trim(), out var v) ? v : null;
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
                        if (TryParseLength(p, out var pv)) vals.Add(pv);
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
        return rule.Any;
    }

    private static readonly HashSet<string> InlineRowTags = new(StringComparer.OrdinalIgnoreCase)
    { "a", "span", "b", "i", "em", "strong", "font", "small", "u", "sup", "sub" };
}
