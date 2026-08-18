using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    private static void ApplyBlockTagStyle(string tag, BlockStyle s, bool uaDefaults = false,
        bool browserUa = false, bool bandDialect = false, bool uaBlockRhythm = false,
        bool articleRhythm = false)
    {
        // Styled-article rhythm: the docs-site sheet's own block margins (a
        // Bootstrap-reboot model).
        if (articleRhythm)
        {
            switch (tag.ToLowerInvariant())
            {
                case "p":
                    s.MarginTop = 0; s.MarginBottom = CssRootFontPt; return;
                case "ul": case "ol":
                    s.MarginTop = 0; s.MarginBottom = CssRootFontPt;
                    s.LeftIndent += ArticleListIndentPt; return;
                case "li":
                    s.IsListItem = true; s.MarginBottom = ArticleLiGapPt; return;
                case "h1":
                    s.FontSize = CssRootFontPt * 2; s.FontRes = "F2";
                    // The page opens 31.5 below the top
                    // margin (≈ the sheet's h1 margin-top 2rem + the content row's
                    // .5rem padding + the body's 2px top border).
                    s.MarginTop = ArticleH1TopPt; s.MarginBottom = CssRootFontPt * 0.5; return;
                case "h2":
                    s.FontSize = CssRootFontPt * 1.6; s.FontRes = "F2";
                    s.MarginTop = CssRootFontPt * 2; s.MarginBottom = CssRootFontPt; return;
                case "h3":
                    s.FontSize = CssRootFontPt * 1.4; s.FontRes = "F2";
                    s.MarginTop = CssRootFontPt * 1.5; s.MarginBottom = CssRootFontPt; return;
                case "h4":
                    s.FontSize = CssRootFontPt * 1.2; s.FontRes = "F2";
                    s.MarginTop = CssRootFontPt * 1.5; s.MarginBottom = CssRootFontPt; return;
            }
        }
        // Sectioned-report rhythm: the UA sheet's real block margins, in em of the
        // element's OWN size. A paragraph's 1.12em is the value the source renderer
        // uses; the legacy flow below deliberately stacks line-on-line instead.
        if (uaBlockRhythm)
        {
            switch (tag.ToLowerInvariant())
            {
                case "p": case "h4": case "ul": case "ol": case "blockquote":
                    s.MarginTop = s.MarginBottom = 1.12 * s.FontSize; return;
                case "h1": s.MarginTop = s.MarginBottom = 0.67 * s.FontSize; return;
                case "h2": s.MarginTop = s.MarginBottom = 0.75 * s.FontSize; return;
                case "h3": s.MarginTop = s.MarginBottom = 0.83 * s.FontSize; return;
                case "h5": s.MarginTop = s.MarginBottom = 1.50 * s.FontSize; return;
                case "h6": s.MarginTop = s.MarginBottom = 1.67 * s.FontSize; return;
            }
        }
        // Filing-dialect page header: the repeated <h5> ToC anchor renders at the
        // browser h5 default (0.83em type, 1.67em margins) — its top margin applies
        // below the page margin on every page, dropping the band start with it.
        if (bandDialect && tag.Equals("h5", StringComparison.OrdinalIgnoreCase))
        {
            s.FontSize = 9.96; s.FontRes = "F2";
            s.MarginTop = 24; s.MarginBottom = 4; s.MarginTopAlways = true;
            return;
        }
        // UA-default flow (stylesheet-less MSHTML documents): browser default type
        // scale and REAL block gaps (see grp/T notes). The
        // gaps are the pairwise between-box constants (P↔P 13.44, P↔H1 16.455, …)
        // expressed as margin-top on the tag plus the margin-bottom remainder that
        // tops a following default-size paragraph up to the pair's constant.
        if (uaDefaults)
        {
            // A non-16px body base (the print-grid dialect's CSS body size) scales the
            // 16px-base UA sizes and margins below proportionally; the 12pt default
            // keeps them byte-identical.
            var uaParentSize = s.FontSize;
            var uaScale = browserUa && uaParentSize > 0 ? uaParentSize / 12.0 : 1.0;
            switch (tag.ToLowerInvariant())
            {
                case "h1": s.FontSize = 24; s.FontRes = "F2"; s.MarginTop = 16.455; s.MarginBottom = 3.015; break;
                case "h2": s.FontSize = 18; s.FontRes = "F2"; s.MarginTop = 13.875; s.MarginBottom = 0.435; break;
                case "h3": s.FontSize = 14.039; s.FontRes = "F2"; s.MarginTop = 13.793; s.MarginBottom = 0.353; break;
                case "h4": s.FontSize = 12; s.FontRes = "F2"; s.MarginTop = 13.44; s.MarginBottom = 0; break;
                case "h5": s.FontSize = 9.96; s.FontRes = "F2"; s.MarginTop = 14.9625; s.MarginBottom = 1.5225; break;
                case "h6": s.FontSize = 9; s.FontRes = "F2"; s.MarginTop = 15.2175; s.MarginBottom = 1.7775; break;
                case "p": s.MarginTop = 13.44; s.MarginBottom = 0; break;
                case "blockquote": s.MarginTop = 13.44; s.MarginBottom = 0; s.LeftIndent += 30; break;
                case "ul":
                case "ol": s.LeftIndent += 30; s.MarginTop = 13.44; s.MarginBottom = 0; break;
                case "li": s.IsListItem = true; break;
                case "pre": s.FontRes = "F4"; break;
            }
            // Full-document flow: the pairwise-gap constants above assume the FOLLOWING
            // box supplies its own top margin to complete the gap — true for P↔P/P↔H
            // runs, but a bare <div> or text node adds nothing, so a heading before one
            // would sit too close. Use the browser's real per-element margins (0.67em on
            // h1 rising to 2.33em on h6, 1em on p), symmetric top and bottom.
            if (uaScale != 1.0)
            {
                if (s.FontSize != uaParentSize) s.FontSize *= uaScale;
                s.MarginTop *= uaScale;
                s.MarginBottom *= uaScale;
            }
            if (browserUa)
                switch (tag.ToLowerInvariant())
                {
                    case "h1": s.MarginTop = s.MarginBottom = 0.67 * s.FontSize; break;
                    case "h2": s.MarginTop = s.MarginBottom = 0.83 * s.FontSize; break;
                    case "h3": s.MarginTop = s.MarginBottom = 1.00 * s.FontSize; break;
                    case "h4": s.MarginTop = s.MarginBottom = 1.33 * s.FontSize; break;
                    // h5/h6: the source renderer's UA margins measure ~15pt (20px)
                    // symmetric — NOT 1.67/2.33em of the pt sizes above.
                    case "h5": s.MarginTop = s.MarginBottom = 15.0 * uaScale; break;
                    case "h6": s.MarginTop = s.MarginBottom = 15.0 * uaScale; break;
                    case "p": s.MarginTop = s.MarginBottom = 1.00 * s.FontSize; break;
                }
            return;
        }
        // Minimal margins — only headings and blockquotes get meaningful
        // spacing. p/div/ul/tr stack line-on-line so page counts mirror what
        // the tag-strip + wrap path would produce for the same text volume.
        switch (tag.ToLowerInvariant())
        {
            case "h1": s.FontSize = 18; s.FontRes = "F2"; s.MarginTop = 4; s.MarginBottom = 2; break;
            case "h2": s.FontSize = 15; s.FontRes = "F2"; s.MarginTop = 3; s.MarginBottom = 2; break;
            case "h3": s.FontSize = 13; s.FontRes = "F2"; s.MarginTop = 3; s.MarginBottom = 2; break;
            case "h4": s.FontSize = 12; s.FontRes = "F2"; s.MarginTop = 2; s.MarginBottom = 1; break;
            case "h5": s.FontSize = 11; s.FontRes = "F2"; s.MarginTop = 2; s.MarginBottom = 1; break;
            case "h6": s.FontSize = 10; s.FontRes = "F2"; s.MarginTop = 1; s.MarginBottom = 1; break;
            case "blockquote": s.MarginTop = 3; s.MarginBottom = 3; s.LeftIndent += 20; break;
            case "ul":
            case "ol":         s.LeftIndent += 20; break;
            case "li":         s.IsListItem = true; break;
            case "pre":        s.FontRes = "F4"; break;
            // p, div, tr, td, th, table: inherit parent margins (0 by default).
        }
    }

    // Parse a tiny subset of inline style="…" — enough to let per-block
    // font-size overrides (common in email-style HTML) affect layout.
    /// <summary>U+200B ZERO WIDTH SPACE — invisible, no advance, and deliberately
    /// not a line-break opportunity either. Editor-generated HTML sprays it between runs.</summary>
    private const char ZeroWidthSpace = '​';

    // A declaration's value may carry semicolons inside url(…) — data: URIs embed
    // ";base64," — so a url(…) token is consumed whole before the plain
    // no-semicolon run continues.
    private static readonly Regex StyleDeclRx = new(
        @"([a-z-]+)\s*:\s*((?:url\([^)]*\)|[^;])+?)\s*(?:;|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static bool HasInlineIndentOverride(Dictionary<string, string>? attrs)
    {
        if (attrs is null || !attrs.TryGetValue("style", out var styleStr) || string.IsNullOrWhiteSpace(styleStr))
            return false;
        return Regex.IsMatch(styleStr, @"(padding-left|margin-left)\s*:", RegexOptions.IgnoreCase);
    }

    private static void ApplyInlineStyle(Dictionary<string, string>? attrs, BlockStyle s)
    {
        if (attrs is null) return;
        if (!attrs.TryGetValue("style", out var styleStr) || string.IsNullOrWhiteSpace(styleStr)) return;
        ApplyDeclarationString(styleStr, s);
    }

    /// <summary>Apply a CSS declaration block ("prop:val; prop:val") to a BlockStyle.</summary>
    private static void ApplyDeclarationString(string styleStr, BlockStyle s)
    {
        foreach (Match m in StyleDeclRx.Matches(styleStr))
            ApplyDeclaration(m.Groups[1].Value.ToLowerInvariant(), m.Groups[2].Value.Trim(), s);
    }

    private static void ApplyDeclaration(string prop, string val, BlockStyle s)
    {
        if (prop == "font-size")
        {
            // Form dialect: an em size is relative to the PARENT's resolved size
            // (1.75em on a 12pt body = 21pt), not the legacy flow's fixed 11pt base.
            var emRel = s.FormDialect
                ? Regex.Match(val, @"^([\d.]+)\s*em$", RegexOptions.IgnoreCase)
                : Match.Empty;
            if (emRel.Success && double.TryParse(emRel.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var emRelV)
                && emRelV > 0 && (s.ParentFontSize > 0 || s.FontSize > 0))
                s.FontSize = emRelV * (s.ParentFontSize > 0 ? s.ParentFontSize : s.FontSize);
            else if (TryParseLength(val, out var pts)) s.FontSize = pts;
            else if (Regex.IsMatch(val, @"^0+(\.0+)?\s*(px|pt|em|rem)?$"))
                s.ZeroFontSize = true;
            else if (val.EndsWith("%", StringComparison.Ordinal)
                     && double.TryParse(val.TrimEnd('%'), System.Globalization.NumberStyles.Float,
                         System.Globalization.CultureInfo.InvariantCulture, out var pct)
                     && pct > 0)
                s.FontSize *= pct / 100.0;
        }
        else if (prop == "font-family")
        {
            var fam = FirstFontFamily(val);
            if (fam is not null) s.FontFamily = fam;
        }
        else if (prop == "font")
        {
            // The `font: bold 8pt Verdana,Arial` SHORTHAND carries weight, size and
            // family in one declaration — the longhand branches never see them.
            var m = Regex.Match(val, @"([\d.]+)\s*(px|pt)\s*([^/;]*)");
            if (m.Success && double.TryParse(m.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var shSize) && shSize > 0)
            {
                s.FontSize = m.Groups[2].Value.Equals("px", StringComparison.OrdinalIgnoreCase)
                    ? shSize * 0.75 : shSize;
                var fam = m.Groups[3].Value.Trim().Length > 0 ? FirstFontFamily(m.Groups[3].Value) : null;
                if (fam is not null) s.FontFamily = fam;
            }
            if (Regex.IsMatch(val, @"\bbold(er)?\b", RegexOptions.IgnoreCase)) s.FontRes = "F2";
            if (Regex.IsMatch(val, @"\b(italic|oblique)\b", RegexOptions.IgnoreCase))
                s.FontRes = s.FontRes == "F2" ? "F2" : "F3";
        }
        else if (prop == "font-weight")
        {
            if (val is "bold" or "bolder" || (int.TryParse(val, out var n) && n >= 600))
                s.FontRes = s.FontRes == "F3" ? "F2" : "F2";
            // An explicit normal weight undoes a heading tag's default bold (form
            // dialect only — legacy conversions are calibrated with the bold face).
            else if (s.FormDialect && s.FontRes == "F2"
                     && (val == "normal" || (int.TryParse(val, out var n2) && n2 < 600)))
                s.FontRes = "F1";
        }
        else if (prop == "font-style")
        {
            if (val is "italic" or "oblique")
                s.FontRes = s.FontRes == "F2" ? "F2" : "F3";
        }
        else if (prop == "text-align")
        {
            // Only justify is handled here (draw-time word-gap stretch, layout-neutral);
            // center stays metric-flow-only via ApplyCssRules. Right is recorded and
            // honored by the print-grid dialect only.
            if (val.Trim().Equals("justify", StringComparison.OrdinalIgnoreCase))
                s.AlignJustify = true;
            else if (val.Trim().Equals("right", StringComparison.OrdinalIgnoreCase))
                s.AlignRight = true;
            // Recorded like Right, on its own flag so the metric flow's stylesheet-only
            // centering keeps its calibrated scope.
            else if (val.Trim().Equals("center", StringComparison.OrdinalIgnoreCase))
                s.AlignCenterCss = true;
        }
        else if (prop == "float")
        {
            // Recorded for every document; only a flow that opted into float layout
            // reads it, so this stays inert elsewhere.
            if (val.Trim().Equals("left", StringComparison.OrdinalIgnoreCase))
                s.FloatLeft = true;
            else if (val.Trim().Equals("right", StringComparison.OrdinalIgnoreCase))
                s.FloatRight = true;
        }
        else if (prop == "margin-top")
        {
            if (TryParseLength(val, out var pts))
            {
                s.MarginTop = pts;
                // Authored (not UA-default) margins MAX-collapse with the body
                // margin at the document top — the UA flow reads this flag.
                s.MarginTopAuthored = true;
            }
        }
        else if (prop == "margin-bottom")
        {
            if (TryParseLength(val, out var pts)) s.MarginBottom = pts;
        }
        else if (prop == "line-height")
        {
            // A percentage line-height fixes the LINE BOX against the element's
            // resolved size (Word-filtered pages author 122/123/167 %); the
            // glyphs seat half-leading inside it. UA flow only — the other
            // flows keep their calibrated line models.
            if (s.UaSerif && val.TrimEnd().EndsWith("%", StringComparison.Ordinal)
                && double.TryParse(val.TrimEnd().TrimEnd('%'),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var lhPct)
                && lhPct > 0 && s.FontSize > 0)
                s.LineBoxPt = lhPct / 100.0 * s.FontSize;
        }
        else if (prop == "margin" && s.FormDialect)
        {
            // The `margin:` shorthand (form dialect only): top and bottom margins
            // per the 1/2/3/4-value CSS grammar. Horizontal values are left to the
            // dedicated margin-left handling; a negative value counts as zero.
            static bool NonNegLen(string v, out double p)
            {
                if (TryParseLength(v, out p)) return true;
                // TryParseLength rejects 0 and negatives; a shorthand "0" is valid.
                if (Regex.IsMatch(v, @"^-?\d+(\.\d+)?\s*(px|pt|em|rem|in|cm|mm)?$")) { p = 0; return true; }
                return false;
            }
            var parts = val.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 1 && NonNegLen(parts[0], out var mTop))
            {
                var bottomVal = parts.Length switch
                {
                    1 => parts[0],
                    2 => parts[0],
                    _ => parts[2],
                };
                s.MarginTop = mTop;
                if (NonNegLen(bottomVal, out var mBot)) s.MarginBottom = mBot;
            }
        }
        else if (prop == "padding-bottom" && s.FormDialect)
        {
            // Bottom padding separates a section heading from what follows the same
            // way a bottom margin does in this flow (form dialect only).
            var em = Regex.Match(val, @"^([\d.]+)\s*em$", RegexOptions.IgnoreCase);
            if (em.Success && double.TryParse(em.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var emv))
            {
                var p = emv * s.FontSize;
                if (p > s.MarginBottom) s.MarginBottom = p;
            }
            else if (TryParseLength(val, out var pts) && pts > s.MarginBottom)
                s.MarginBottom = pts;
        }
        else if (prop == "margin-left" || prop == "padding-left")
        {
            if (TryParseLength(val, out var pts)) s.LeftIndent += pts;
            // The Bootstrap gutter pair: a NEGATIVE margin-left cancels the
            // enclosing column padding (`.row { margin-left:-15px }` inside
            // `.col { padding-left:15px }`). Styled-article dialect only — the
            // calibrated dialects never met a negative.
            else if (s.ArticleRhythm && prop == "margin-left"
                && val.TrimStart().StartsWith('-')
                && TryParseLength(val.TrimStart().TrimStart('-'), out var negPts))
                s.LeftIndent = Math.Max(0, s.LeftIndent - negPts);
            // UA-serif flow: a negative margin-left is REAL — the element's box
            // moves left of the content origin and the page clip crops it there
            // (the source renderer clips content at one body margin left of the
            // content origin).
            else if (s.UaSerif && prop == "margin-left"
                && val.TrimStart().StartsWith('-')
                && TryParseLength(val.TrimStart().TrimStart('-'), out var uaNegPts))
                s.LeftIndent -= uaNegPts;
        }
        // Styled-article: a container's padding-bottom is real space below its
        // last line (the panel header's .5rem) — carried on the close channel.
        else if (prop == "padding-bottom" && s.ArticleRhythm)
        {
            if (TryParseLength(val, out var pb) && pb > s.MarginBottom) s.MarginBottom = pb;
        }
        else if (prop == "height" || prop == "min-height")
        {
            // Browser-UA flow: min-height never pushes content down, and a
            // content-bearing element's flow exceeds its floor — the ExplicitHeight
            // spacer/pad machinery would insert phantom space BEFORE the content
            // (the SharePoint layoutszone idiom), so the floor is inert here.
            // A real height keeps the spacer/band semantics everywhere.
            if (prop == "min-height" && s.UaSerif) return;
            // em heights scale with the element's RESOLVED font size (a 12/11 factor
            // maps our 11pt body default onto the browser's 16px=12pt em base).
            var em = Regex.Match(val, @"^([\d.]+)\s*em$", RegexOptions.IgnoreCase);
            if (em.Success && double.TryParse(em.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var emv))
            {
                var h = emv * s.FontSize * (12.0 / 11.0);
                if (h > s.ExplicitHeight) s.ExplicitHeight = h;
            }
            else if (TryParseLength(val, out var pts) && pts > s.ExplicitHeight)
                s.ExplicitHeight = pts;
        }
        else if (prop == "color")
        {
            // Foreground text colour. Layout-neutral — changes only the drawn ink.
            var c = ParseCssColor(val);
            if (c is not null) s.ForeColor = c;
        }
        else if (prop == "background-color" || prop == "background")
        {
            var c = ParseCssColor(val);
            // Ignore white/transparent backgrounds — they add no visible ink.
            if (c is not null && !(c.R >= 250 && c.G >= 250 && c.B >= 250))
                s.BackgroundColor = c;
        }
        else if (prop == "page-break-before" || prop == "break-before")
        {
            if (val.Contains("always", StringComparison.OrdinalIgnoreCase)
                || val.Equals("page", StringComparison.OrdinalIgnoreCase))
                s.PageBreakBefore = true;
        }
        else if (prop == "page-break-after" || prop == "break-after")
        {
            // The break lands AFTER this element's content — an empty
            // `<p style="page-break-after:always"></p>` is the cover-page idiom.
            if (val.Contains("always", StringComparison.OrdinalIgnoreCase)
                || val.Equals("page", StringComparison.OrdinalIgnoreCase))
                s.PageBreakAfter = true;
        }
        else if (prop == "border" || prop == "border-color" || prop == "border-width"
              || prop == "border-top" || prop == "border-bottom"
              || prop == "border-left" || prop == "border-right")
        {
            // A border-TOP declaration over a none/zero box is a divider rule,
            // not a frame; any other side (or the shorthand) re-authors the box.
            if (prop == "border-top"
                && !val.Contains("none", StringComparison.OrdinalIgnoreCase)
                && s.BorderColor is null)
                s.BorderTopOnly = true;
            else if (prop is "border-bottom" or "border-left" or "border-right"
                     && !val.Contains("none", StringComparison.OrdinalIgnoreCase))
                s.BorderTopOnly = false;
            var c = ParseCssColor(val);
            if (c is not null) s.BorderColor = c;
            var wm = Regex.Match(val, @"([\d.]+)\s*px", RegexOptions.IgnoreCase);
            if (wm.Success && double.TryParse(wm.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var bw) && bw > 0)
                s.BorderWidth = bw * 0.75; // px → pt
            // An EXPLICIT zero ("border-width: 0px") authors NO border — it must
            // not fall through to the 1px default.
            else if (wm.Success)
            {
                s.BorderWidth = 0;
                return;
            }
            else if (s.BorderWidth <= 0)
                s.BorderWidth = 0.75;
            // A border with an unspecified colour defaults to black (CSS `border:1px solid`).
            if (s.BorderColor is null && val.IndexOf("none", StringComparison.OrdinalIgnoreCase) < 0)
                s.BorderColor = Color.FromRgb(0, 0, 0);
        }
        else if (prop == "border-style")
        {
            // `border-style: solid` with no width authors a visible border: the
            // source renderer strokes it 1 pt wide in the text colour (probed:
            // the 200px border-radius box strokes w=1.0 centred on a 151 pt
            // centreline = 150 pt content + 2×1 pt border).
            if (!val.Contains("none", StringComparison.OrdinalIgnoreCase)
                && !val.Contains("hidden", StringComparison.OrdinalIgnoreCase))
            {
                if (s.BorderWidth <= 0) s.BorderWidth = StyleOnlyBorderPt;
                s.BorderColor ??= Color.FromRgb(0, 0, 0);
            }
        }
        else if (prop == "border-radius")
        {
            // First shorthand value rounds all corners this flow draws.
            var rv = val.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (rv.Length > 0 && TryParseLength(rv[0], out var rPt))
                s.BorderRadiusPt = rPt;
        }
    }

    /// <summary>Border width of a `border-style` declaration that names no width:
    /// probed on the reference renderer — the stroke draws exactly 1 pt.</summary>
    private const double StyleOnlyBorderPt = 1.0;

    /// <summary>First concrete (non-generic) family name from a CSS font-family list,
    /// with quotes stripped. Returns null for a purely generic list (serif/sans-serif/
    /// monospace/cursive/fantasy) so the Standard-14 Helvetica default applies.</summary>
    private static string? FirstFontFamily(string value)
    {
        // Style attributes reach us with their character entities intact —
        // `font-family: &quot;Arial&quot;` names Arial, not a face called
        // `"Arial` (which resolves nowhere and measures at the 0.5 em fallback).
        if (value.IndexOf('&') >= 0)
            value = value.Replace("&quot;", "\"").Replace("&#34;", "\"")
                         .Replace("&apos;", "'").Replace("&#39;", "'");
        // A fully-quoted value is ONE literal family name, commas included —
        // '"ARIAL,HELVETICA,SANS-SERIFF"' names a single (unknown) face and
        // must not split into a resolvable ARIAL.
        var whole = value.Trim();
        if (whole.Length >= 2 && (whole[0] == '"' || whole[0] == '\'')
            && whole[^1] == whole[0] && whole.IndexOf(whole[0], 1) == whole.Length - 1)
        {
            var one = whole[1..^1].Trim();
            return one.Length > 0 ? one : null;
        }
        foreach (var part in value.Split(','))
        {
            var name = part.Trim().Trim('\'', '"').Trim();
            if (name.Length == 0) continue;
            switch (name.ToLowerInvariant())
            {
                case "serif": case "sans-serif": case "monospace":
                case "cursive": case "fantasy": case "system-ui": case "inherit":
                    continue;
            }
            return name;
        }
        return null;
    }

    /// <summary>Apply the document stylesheet's type-selector and class-selector rules
    /// to <paramref name="s"/> for an element with tag <paramref name="tag"/> and the
    /// given attributes. Type rule first, then each class (left-to-right) — matching the
    /// simple cascade the converter needs for font-family / size.</summary>
    private static void ApplyCssRules(IReadOnlyDictionary<string, Dictionary<string, string>>? css,
        string tag, Dictionary<string, string>? attrs, BlockStyle s, bool metricLayout = false,
        bool coverStyles = false)
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
                else if (coverStyles && kv.Key == "font-size")
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

    /// <summary>An element on a CSS ancestor chain: its tag plus the id/class hooks a
    /// selector can address. The lifted-table builder grows a chain as it walks the
    /// markup (table → td → div/span …) and threads it into nested builds, so rules
    /// addressed through the document tree reach inner grids.</summary>
    internal sealed class CssElem
    {
        public string Tag = "";
        public string? Id;
        public string[]? Classes;
        // The element's matched `display` value (chain hooks fill it in): the
        // nearest non-null one decides whether a styled run rides its line
        // (inline-block) or may break it.
        public string? Display;
    }

    internal sealed class CssChainSeg
    {
        // Relation to the segment on its LEFT: true = direct child ('>'), false =
        // descendant. Structural table containers (tbody/thead/tfoot/tr) are
        // collapsed at parse; a cell reached from its table purely through child
        // hops stays a CHILD, because the builder's chains seat cells directly
        // under their table node.
        public bool Child;
        public string? Tag;
        public string? Id;
        public List<string>? Classes;
    }

    /// <summary>A stylesheet rule kept with its FULL selector chain. Only rules the
    /// flat <see cref="ParseStyleSheet"/> map cannot express are kept (an id anywhere,
    /// a child combinator, or three-plus compound parts), so every existing flat-rule
    /// consumer keeps its exact behaviour and the chain pass adds styling only where
    /// none could exist before.</summary>
    internal sealed class CssChainRule
    {
        public List<CssChainSeg> Segs = null!;
        public Dictionary<string, string> Decls = null!;
        public int Spec;      // id 100 / class 10 / tag 1, summed
        public int Order;     // source order, ties broken towards later rules
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

    /// <summary>An open inline-box run during the cell token walk: a chain-matched
    /// background + inline-block element (title plate, status pill) collecting the
    /// line span it covers; a TrafficLight child adds the trailing circle and its
    /// letter (which leaves the flowed text).</summary>
    private sealed class ChainBoxRun
    {
        public CssElem Elem = null!;
        public int StartLen;
        public double PadL, PadR, PadT, PadB, Radius;
        // CSS-declared box height (the title plates' `height: 4ex`): the box is
        // drawn once with pads + this height and may span following lines.
        public double DeclH;
        // padding-top of a run INSIDE the box that continues onto the next line
        // (`.SmallerTitle { padding-top: 0.75ex }`): the continuation line's gap.
        public double ContPadTop;
        // CSS letter-spacing on the box's text (the title plates' 0.05ex).
        public double LetterSpacing;
        // Null = no rectangle (a standalone badge draws only its circle).
        public Color? Fill;
        public Color? CircleFill;
        public double CircleD;
        public string CircleLetter = "";
        public Color? CircleLetterColor;
        // Block-level box (a section <h1> bar): spans the cell's content width at
        // draw time, its text centred, in its own colour (white on the red bars).
        public bool FullWidth;
        public bool TextCentered;
        public Color? TextColor;
    }

    /// <summary>The uniform fill a tiny repeated background tile paints: a
    /// `background-image: url(data:…)` whose bitmap is at most a few pixels
    /// (the classic 1×1-GIF pattern) tiles to a solid colour, sampled from the
    /// tile's centre pixel. Null when the declarations carry no such tile, when
    /// the repeat mode is not a full tile (`no-repeat`, `repeat-x`, …), or when
    /// the tile is large enough that its own drawing would show.</summary>
    private static Color? DataUriTileFill(Dictionary<string, string> decls)
    {
        if (!decls.TryGetValue("background-image", out var bg)
            && !decls.TryGetValue("background", out bg)) return null;
        var um = Regex.Match(bg, @"url\(\s*[""']?\s*data:image/[^;,]+;base64,([A-Za-z0-9+/=]+)",
            RegexOptions.IgnoreCase);
        if (!um.Success) return null;
        if (decls.TryGetValue("background-repeat", out var rep)
            && !rep.Trim().Equals("repeat", StringComparison.OrdinalIgnoreCase)) return null;
        // GDI+ decoding is Windows-only (the repo-wide System.Drawing convention).
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
#pragma warning disable CA1416
            var bytes = System.Convert.FromBase64String(um.Groups[1].Value);
            using var ms = new System.IO.MemoryStream(bytes);
            using var bmp = new System.Drawing.Bitmap(ms);
            if (bmp.Width > MaxUniformTilePx || bmp.Height > MaxUniformTilePx) return null;
            var px = bmp.GetPixel(bmp.Width / 2, bmp.Height / 2);
            if (px.A < 32) return null;
            return Color.FromRgb(px.R, px.G, px.B);
#pragma warning restore CA1416
        }
        catch { return null; }
    }

    // A repeated tile up to this many pixels per side reads as a uniform fill at
    // render resolution (4px = 3pt — smaller than one 8px comparator block).
    private const int MaxUniformTilePx = 4;

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
            return (Color.FromRgb(px.R, px.G, px.B), Math.Max(bmp.Width, bmp.Height) * 0.75);
#pragma warning restore CA1416
        }
        catch { return null; }
    }

    /// <summary>@media handling for the chain parser: a screen-only group is dropped
    /// whole, any other group is unwrapped in place — the PDF renderer is a print
    /// target (print rules are honoured, screen-only
    /// linked sheets ignored).</summary>
    private static string FlattenMediaBlocks(string css)
    {
        if (css.IndexOf("@media", StringComparison.OrdinalIgnoreCase) < 0) return css;
        var sb = new StringBuilder(css.Length);
        var i = 0;
        while (i < css.Length)
        {
            var at = css.IndexOf("@media", i, StringComparison.OrdinalIgnoreCase);
            if (at < 0) { sb.Append(css, i, css.Length - i); break; }
            sb.Append(css, i, at - i);
            var brace = css.IndexOf('{', at);
            if (brace < 0) break;
            var depth = 1; var j = brace + 1;
            while (j < css.Length && depth > 0)
            {
                if (css[j] == '{') depth++;
                else if (css[j] == '}') depth--;
                j++;
            }
            var contentEnd = depth == 0 ? j - 1 : css.Length;
            var media = css[(at + 6)..brace];
            var screenOnly = media.IndexOf("screen", StringComparison.OrdinalIgnoreCase) >= 0
                && media.IndexOf("print", StringComparison.OrdinalIgnoreCase) < 0
                && media.IndexOf("all", StringComparison.OrdinalIgnoreCase) < 0;
            if (!screenOnly) sb.Append(css, brace + 1, contentEnd - brace - 1);
            i = j;
        }
        return sb.ToString();
    }

    private static List<CssChainSeg>? ParseChainSelector(string sel, out int spec, out bool chainOnly)
    {
        spec = 0; chainOnly = false;
        sel = sel.Trim();
        if (sel.Length == 0 || sel.IndexOfAny(new[] { '+', '~', ':', '[', '*', '@' }) >= 0) return null;
        var segs = new List<CssChainSeg>();
        var child = false; var hadChild = false; var parts = 0; var hasId = false;
        var hadDrop = false; var dropAllChild = true;
        foreach (var tokRaw in Regex.Split(sel, @"(>)|\s+"))
        {
            var t = tokRaw?.Trim();
            if (string.IsNullOrEmpty(t)) continue;
            if (t == ">") { child = true; hadChild = true; continue; }
            var m = Regex.Match(t, @"^([a-zA-Z][\w-]*)?((?:[.#][\w-]+)+)?$");
            if (!m.Success || (m.Groups[1].Length == 0 && m.Groups[2].Length == 0)) return null;
            parts++;
            var seg = new CssChainSeg
            {
                Child = child,
                Tag = m.Groups[1].Length > 0 ? m.Groups[1].Value.ToLowerInvariant() : null,
            };
            child = false;
            if (seg.Tag is not null) spec += 1;
            foreach (Match h in Regex.Matches(m.Groups[2].Value, @"[.#][\w-]+"))
            {
                if (h.Value[0] == '#') { seg.Id = h.Value[1..]; spec += 100; hasId = true; }
                else { (seg.Classes ??= new List<string>()).Add(h.Value[1..]); spec += 10; }
            }
            // Structural table containers add no selectivity between a table and
            // its cells — collapse them, keeping the child relation only when every
            // dropped hop was a child combinator.
            if (seg.Id is null && seg.Classes is null
                && seg.Tag is "tbody" or "thead" or "tfoot" or "tr")
            {
                hadDrop = true;
                dropAllChild &= seg.Child;
                continue;
            }
            if (hadDrop)
            {
                seg.Child = seg.Child && dropAllChild;
                hadDrop = false; dropAllChild = true;
            }
            segs.Add(seg);
        }
        chainOnly = hasId || hadChild || parts > 2;
        return segs.Count > 0 ? segs : null;
    }

    /// <summary>Parse every style block into full-chain rules (see
    /// <see cref="CssChainRule"/>). Screen-only blocks — the media attribute an
    /// inlined &lt;link&gt; carries, or an @media group — are excluded. Returns null
    /// when the document has no rule the flat map could not express.</summary>
    internal static List<CssChainRule>? ParseChainRules(string html)
    {
        List<CssChainRule>? rules = null;
        var order = 0;
        foreach (Match block in Regex.Matches(html, @"<style\b([^>]*)>([\s\S]*?)</style>", RegexOptions.IgnoreCase))
        {
            var mAttr = Regex.Match(block.Groups[1].Value, @"media\s*=\s*[""']([^""']*)[""']", RegexOptions.IgnoreCase);
            if (mAttr.Success)
            {
                var mv = mAttr.Groups[1].Value;
                if (mv.IndexOf("screen", StringComparison.OrdinalIgnoreCase) >= 0
                    && mv.IndexOf("print", StringComparison.OrdinalIgnoreCase) < 0
                    && mv.IndexOf("all", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
            }
            var cssText = FlattenMediaBlocks(Regex.Replace(block.Groups[2].Value, @"/\*[\s\S]*?\*/", ""));
            foreach (Match rule in Regex.Matches(cssText, @"([^{}]+)\{([^{}]*)\}"))
            {
                Dictionary<string, string>? decls = null;
                foreach (Match d in StyleDeclRx.Matches(rule.Groups[2].Value))
                    (decls ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
                        [d.Groups[1].Value.Trim().ToLowerInvariant()] = d.Groups[2].Value.Trim();
                if (decls is null) continue;
                foreach (var selRaw in rule.Groups[1].Value.Split(','))
                {
                    var segs = ParseChainSelector(selRaw, out var spec, out var chainOnly);
                    if (segs is null || !chainOnly) continue;
                    (rules ??= new List<CssChainRule>()).Add(new CssChainRule
                    { Segs = segs, Decls = decls, Spec = spec, Order = order++ });
                }
            }
        }
        return rules;
    }

    private static bool ChainSegMatches(CssChainSeg s, CssElem e)
    {
        if (s.Tag is not null && !s.Tag.Equals(e.Tag, StringComparison.OrdinalIgnoreCase)) return false;
        if (s.Id is not null
            && !(e.Id is not null && s.Id.Equals(e.Id, StringComparison.OrdinalIgnoreCase))) return false;
        if (s.Classes is not null)
        {
            if (e.Classes is null) return false;
            foreach (var c in s.Classes)
            {
                var ok = false;
                foreach (var ec in e.Classes)
                    if (string.Equals(c, ec, StringComparison.OrdinalIgnoreCase)) { ok = true; break; }
                if (!ok) return false;
            }
        }
        return true;
    }

    private static bool MatchChainAt(List<CssChainSeg> segs, int si, IReadOnlyList<CssElem> chain, int ci)
    {
        if (ci < 0 || !ChainSegMatches(segs[si], chain[ci])) return false;
        if (si == 0) return true;
        if (segs[si].Child) return MatchChainAt(segs, si - 1, chain, ci - 1);
        for (var k = ci - 1; k >= 0; k--)
            if (MatchChainAt(segs, si - 1, chain, k)) return true;
        return false;
    }

    /// <summary>Merged declarations of every chain rule whose selector matches the
    /// chain's LAST element through its ancestors — lower specificity first, source
    /// order breaking ties, so the most specific rule's property wins. Null when
    /// nothing matches.</summary>
    internal static Dictionary<string, string>? MatchChainDecls(List<CssChainRule>? rules, List<CssElem> chain)
    {
        if (rules is null || chain.Count == 0) return null;
        List<CssChainRule>? hit = null;
        foreach (var r in rules)
            if (MatchChainAt(r.Segs, r.Segs.Count - 1, chain, chain.Count - 1))
                (hit ??= new List<CssChainRule>()).Add(r);
        if (hit is null) return null;
        hit.Sort((a, b) => a.Spec != b.Spec ? a.Spec.CompareTo(b.Spec) : a.Order.CompareTo(b.Order));
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in hit)
            foreach (var kv in r.Decls) merged[kv.Key] = kv.Value;
        return merged;
    }

    /// <summary>Resolve a CSS length to points against a font-size context: px at
    /// 0.75, em on the font, ex at half an em. 0 when unparsable (percent lengths
    /// need their own base and are the caller's job).</summary>
    private static double ChainLenPt(string v, double fontPt)
    {
        var m = Regex.Match(v.Trim(), @"^(-?[\d.]+)\s*(px|pt|em|ex|in|cm|mm)?$", RegexOptions.IgnoreCase);
        if (!m.Success || !double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var n)) return 0;
        return m.Groups[2].Value.ToLowerInvariant() switch
        {
            "pt" => n,
            "em" => n * fontPt,
            "ex" => n * fontPt / 2,
            "in" => n * 72,
            "cm" => n * 72 / 2.54,
            "mm" => n * 72 / 25.4,
            _ => n * 0.75,
        };
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

    /// <summary>A `border: 1px solid white` shorthand as a box BorderInfo; null for
    /// zero-width/none borders. currentColor and a missing colour fall to black.</summary>
    private static BorderInfo? ChainBorder(string v)
    {
        var t = v.Trim();
        if (t.StartsWith("0", StringComparison.Ordinal)
            || t.IndexOf("none", StringComparison.OrdinalIgnoreCase) >= 0) return null;
        var w = 0.75;
        var wm = Regex.Match(t, @"([\d.]+)\s*(px|pt)", RegexOptions.IgnoreCase);
        if (wm.Success && double.TryParse(wm.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var wv) && wv > 0)
            w = wm.Groups[2].Value.Equals("pt", StringComparison.OrdinalIgnoreCase) ? wv : wv * 0.75;
        var residue = Regex.Replace(t,
            @"([\d.]+)\s*(px|pt)|solid|outset|inset|dotted|dashed|double|groove|ridge|currentcolor", "",
            RegexOptions.IgnoreCase).Trim();
        return new BorderInfo(BorderSide.Box, w, ParseCssColor(residue) ?? Color.Black);
    }

    /// <summary>Chain element for an open tag: its name plus the id/classes a
    /// selector can address.</summary>
    private static CssElem ChainTokElem(string tag, Dictionary<string, string>? attrs)
    {
        var e = new CssElem { Tag = tag };
        if (attrs is not null)
        {
            if (attrs.TryGetValue("id", out var idv) && !string.IsNullOrWhiteSpace(idv)) e.Id = idv.Trim();
            if (attrs.TryGetValue("class", out var clv) && !string.IsNullOrWhiteSpace(clv))
                e.Classes = clv.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        }
        return e;
    }

    /// <summary>A CSS <c>li:nth-child(An+B)::before { content: "…" }</c> generated-content
    /// marker: the item text a matching &lt;li&gt; is prefixed with. Only the small subset used
    /// by list styling (an optional container class, an nth-child index, a literal content
    /// string) is modelled — enough to reproduce editor-authored ordered-list markers.</summary>
    private sealed class BeforeMarker
    {
        public string? ContainerClass; // class on the enclosing <ol>/<ul> (null = any list)
        public int A;                  // nth-child(An+B) coefficient
        public int B;                  // nth-child(An+B) offset
        public string Content = "";    // generated text, logical order
        public bool Matches(int index1Based) => A == 0
            ? index1Based == B
            : (index1Based - B) % A == 0 && (index1Based - B) / A >= 0;
    }

    // .class > li:nth-child(An+B)::before  /  li:nth-child(An+B):before  — the container class
    // and combinator are optional; nth-child arg captured raw for NthChildRx.
    private static readonly Regex BeforeSelectorRx = new(
        @"(?:\.(?<cc>[A-Za-z_][\w-]*)\s*[>\s]\s*)?[A-Za-z]+:nth-child\(\s*(?<nc>[^)]+?)\s*\)\s*::?before",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex NthChildRx = new(
        @"^(?:(?<a>-?\d*)n\s*(?:(?<sign>[+-])\s*(?<b>\d+))?|(?<lit>-?\d+))$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex BeforeContentRx = new(
        @"content\s*:\s*(['""])(?<v>.*?)\1",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    /// <summary>Scan the document's &lt;style&gt; blocks for
    /// <c>li:nth-child(An+B)::before { content: "…" }</c> rules and return them as generated-content
    /// markers, so an <c>&lt;ol&gt;</c> whose CSS supplies its own markers (list-style:none + ::before)
    /// renders those instead of the numeric default.</summary>
    private static List<BeforeMarker> ParseBeforeMarkers(string html)
    {
        var result = new List<BeforeMarker>();
        foreach (Match block in Regex.Matches(html, @"<style[^>]*>([\s\S]*?)</style>", RegexOptions.IgnoreCase))
        {
            var css = Regex.Replace(block.Groups[1].Value, @"/\*[\s\S]*?\*/", "");
            foreach (Match rule in Regex.Matches(css, @"([^{}]+)\{([^{}]*)\}"))
            {
                var sel = BeforeSelectorRx.Match(rule.Groups[1].Value);
                if (!sel.Success) continue;
                var cm = BeforeContentRx.Match(rule.Groups[2].Value);
                if (!cm.Success) continue;
                var nc = NthChildRx.Match(sel.Groups["nc"].Value.Trim());
                if (!nc.Success) continue;
                int a, b;
                if (nc.Groups["lit"].Success) { a = 0; b = int.Parse(nc.Groups["lit"].Value); }
                else
                {
                    var av = nc.Groups["a"].Value;
                    a = av.Length == 0 ? 1 : av == "-" ? -1 : int.Parse(av);
                    b = nc.Groups["b"].Success
                        ? int.Parse(nc.Groups["b"].Value) * (nc.Groups["sign"].Value == "-" ? -1 : 1)
                        : 0;
                }
                result.Add(new BeforeMarker
                {
                    ContainerClass = sel.Groups["cc"].Success ? sel.Groups["cc"].Value : null,
                    A = a,
                    B = b,
                    Content = DecodeEntities(cm.Groups["v"].Value),
                });
            }
        }
        return result;
    }

    /// <summary>The subset of <paramref name="markers"/> that applies to an <c>&lt;ol&gt;/&lt;ul&gt;</c>
    /// carrying <paramref name="classAttr"/> — a rule with no container class matches any list; a
    /// rule scoped to <c>.foo</c> matches only when the list has class <c>foo</c>. Null when none.</summary>
    private static List<BeforeMarker>? ResolveListBeforeRules(IReadOnlyList<BeforeMarker>? markers, string? classAttr)
    {
        if (markers is null || markers.Count == 0) return null;
        var classes = classAttr?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? System.Array.Empty<string>();
        List<BeforeMarker>? hits = null;
        foreach (var m in markers)
            if (m.ContainerClass is null || System.Array.IndexOf(classes, m.ContainerClass) >= 0)
                (hits ??= new()).Add(m);
        return hits;
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

    /// <summary>DomDecl walking up the ancestor chain (inherited properties:
    /// color, font-size, …).</summary>
    private static string? DomDeclInherited(HtmlNode el, string prop,
        IReadOnlyDictionary<string, Dictionary<string, string>>? css)
    {
        for (HtmlNode? n = el; n is not null; n = n.Parent)
        {
            if (n.Tag.Length == 0) continue;
            var v = DomDecl(n, prop, css);
            if (!string.IsNullOrEmpty(v)) return v;
        }
        return null;
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

    private static readonly HashSet<string> InlineRowTags = new(StringComparer.OrdinalIgnoreCase)
    { "a", "span", "b", "i", "em", "strong", "font", "small", "u", "sup", "sub" };
}
