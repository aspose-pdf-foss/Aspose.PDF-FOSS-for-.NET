using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    private static void ApplyBlockTagStyle(string tag, BlockStyle s, bool uaDefaults = false,
        bool browserUa = false, bool bandDialect = false, bool uaBlockRhythm = false,
        bool articleRhythm = false, bool msoParagraphs = false, bool emHeadings = false,
        bool html5UaHeadings = false)
    {
        // Float flow: a heading's UA size and margins are em of the CASCADE, not the flat
        // points the legacy flows are calibrated on. The certificate's h1 sits in a
        // `#title { font-size: 11px }` div, so it is 2 x 8.25 = 16.5 pt with 0.67em
        // margins - measured (16.5 pt, and 11.055 pt of margin puts its
        // first line at 264.38 against the expected 264.13).
        // …and its paragraphs carry the same 1.12 em UA margin the rest of the engine
        // measured — on the element's INHERITED size, and on the shorthand channel so the
        // margin belongs to the paragraph BOX rather than to each line a <br> cuts off
        // inside it. Measured on the certificate: the gap over its `</p><p>` boundary is
        // 11.77, which is 1.12 x 10.5. An authored `margin:` shorthand overrides it.
        if (emHeadings && tag.Equals("p", StringComparison.OrdinalIgnoreCase) && s.FontSize > 0)
        {
            s.ShorthandTopPt = UaBlockMarginEm * s.FontSize;
            return;
        }
        if (emHeadings && tag.Length == 2 && tag[0] is 'h' or 'H' && tag[1] is >= '1' and <= '6')
        {
            var emBase = s.ParentFontSize > 0 ? s.ParentFontSize : s.FontSize;
            var (sizeEm, marginEm) = tag[1] switch
            {
                '1' => (2.00, 0.67),
                '2' => (1.50, 0.83),
                '3' => (1.17, 1.00),
                '4' => (1.00, 1.33),
                '5' => (0.83, 1.67),
                _   => (0.67, 2.33),
            };
            s.FontSize = sizeEm * emBase;
            s.FontRes = "F2";
            s.MarginTop = s.MarginBottom = marginEm * s.FontSize;
            return;
        }
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
        // element's OWN size. A paragraph's 1.12em is the value the expected render
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
            // An html5-doctype bare UA document (see Convert's html5BareUa): the
            // heading margins are the browser's real per-element em values resolved
            // against the PARENT size, symmetric — measured on the
            // h3-over-inline sheet: the h3 opens 72 + max(6, 12) from the page top
            // and stands 12 above the following bare inline (1.00 em of the 12 pt
            // root, NOT of its own 14.04). The mid-document pairwise constants
            // assume a following block completes the gap, which a bare inline
            // never does.
            if (html5UaHeadings && tag.Length == 2 && (tag[0] is 'h' or 'H')
                && tag[1] is >= '1' and <= '6')
            {
                var rootEm = uaParentSize > 0 ? uaParentSize : 12.0;
                var marginEm = tag[1] switch
                {
                    '1' => 0.67, '2' => 0.83, '3' => 1.00, '4' => 1.33, '5' => 1.67, _ => 2.33,
                };
                s.MarginTop = s.MarginBottom = marginEm * rootEm;
            }
            if (browserUa)
                switch (tag.ToLowerInvariant())
                {
                    case "h1": s.MarginTop = s.MarginBottom = 0.67 * s.FontSize; break;
                    case "h2": s.MarginTop = s.MarginBottom = 0.83 * s.FontSize; break;
                    case "h3": s.MarginTop = s.MarginBottom = 1.00 * s.FontSize; break;
                    case "h4": s.MarginTop = s.MarginBottom = 1.33 * s.FontSize; break;
                    // h5/h6: the UA margins measure ~15pt (20px)
                    // symmetric — NOT 1.67/2.33em of the pt sizes above.
                    case "h5": s.MarginTop = s.MarginBottom = 15.0 * uaScale; break;
                    case "h6": s.MarginTop = s.MarginBottom = 15.0 * uaScale; break;
                    // The UA paragraph margin is 1.12em (probed:
                    // a text→p / p→p / p→text ladder gaps uniformly at 13.44 on
                    // the 12 pt base). The Word-filtered arm keeps the 1.00em its
                    // calibrated constants compose with.
                    case "p":
                        s.MarginTop = s.MarginBottom = (msoParagraphs ? 1.00 : 1.12) * s.FontSize;
                        break;
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

    /// <summary>Apply a CSS declaration block ("prop:val; prop:val") to a BlockStyle.
    /// <c>font-size</c> is applied FIRST: every other <c>em</c> length on the same element
    /// is a multiple of that element's OWN size, whatever order the declarations were
    /// written in (`margin-bottom:1em; font-size:10pt` is a 10 pt margin).</summary>
    private static void ApplyDeclarationString(string styleStr, BlockStyle s)
    {
        foreach (Match m in StyleDeclRx.Matches(styleStr))
            if (m.Groups[1].Value.Equals("font-size", StringComparison.OrdinalIgnoreCase))
                ApplyDeclaration("font-size", m.Groups[2].Value.Trim(), s);
        foreach (Match m in StyleDeclRx.Matches(styleStr))
        {
            var prop = m.Groups[1].Value.ToLowerInvariant();
            if (prop == "font-size") continue;
            ApplyDeclaration(prop, m.Groups[2].Value.Trim(), s);
        }
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
            if (fam is not null) { s.FontFamily = fam; s.FontFamilyStack = val; }
        }
        else if (prop == "line-height")
        {
            // A UNITLESS line-height is a factor of the element's own font size
            // (line-height:2 on a 9px paragraph paces 18px lines — measured).
            // Unit lengths keep their dialect-specific handling.
            if (Regex.IsMatch(val, @"^[\d.]+$") && double.TryParse(val,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var lf)
                && lf > 0)
            {
                s.LineFactor = lf;
                s.DeclaredLineFactor = true;
            }
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
            // An `em` margin is a multiple of the element's OWN resolved size
            // (`margin-bottom: 1em` on a 10 pt block is 10 pt); TryParseLength
            // can only assume the document default, so resolve it here.
            var mbEm = Regex.Match(val, @"^([\d.]+)\s*em$", RegexOptions.IgnoreCase);
            if (mbEm.Success && s.FontSize > 0
                && double.TryParse(mbEm.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var mbEmv))
                s.MarginBottom = mbEmv * s.FontSize;
            else if (TryParseLength(val, out var pts)) s.MarginBottom = pts;
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
        else if (prop == "width")
        {
            // A pixel width, recorded for every document; only the float flow reads it.
            if (Regex.IsMatch(val, @"^\s*[0-9.]+\s*px\s*$", RegexOptions.IgnoreCase)
                && TryParseLength(val, out var wDeclPt) && wDeclPt > 0)
                s.DeclaredWidthPt = wDeclPt;
        }
        else if (prop == "margin")
        {
            // The shorthand's LEFT value (the 4th of four, else the 2nd of two/three)
            // is recorded for every document; only the float flow reads it.
            var hParts = val.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var leftVal = hParts.Length switch
            {
                0 => null,
                1 => hParts[0],
                >= 4 => hParts[3],
                _ => hParts[1],
            };
            if (leftVal is not null && TryParseLength(leftVal, out var mLeftPt) && mLeftPt > 0)
                s.ShorthandLeftPt = mLeftPt;
            if (hParts.Length >= 1 && TryParseLength(hParts[0], out var mTopPt) && mTopPt > 0)
                s.ShorthandTopPt = mTopPt;
            if (s.FormDialect || s.UaSerif)
            {
            // The `margin:` shorthand (form dialect and UA-serif flows): top and
            // bottom margins per the 1/2/3/4-value CSS grammar — an authored
            // `margin: 0pt` really zeroes the UA paragraph margins. Horizontal
            // values are left to the dedicated margin-left handling; a negative
            // value counts as zero.
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
        }
        else if (prop == "padding")
        {
            var padParts = val.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // The `padding:` shorthand's TOP value on a top-rule DIVIDER wrapper
            // is the box space its marker block spends under the rule (the saved
            // email's `padding: 3pt 0cm 0cm` From-block frame).
            if (s.UaSerif && s.BorderTopOnly
                && padParts.Length > 0 && TryParseLength(padParts[0], out var padTopPt)
                && padTopPt > 0)
                // Max, not sum — the style applier can visit a declaration twice.
                s.PadTop = Math.Max(s.PadTop, padTopPt);
            // The CSS box's own padding, per the 1/2/3/4-value grammar. Only a
            // block that PAINTS (a background colour) spends it — the fill covers
            // its line boxes plus this much above and below, and its text starts
            // this far inside the content edge. Every other flow ignores these,
            // so an unpainted block's box is unchanged.
            if (padParts.Length > 0)
            {
                double Pad(int i)
                    => i < padParts.Length && TryParseLength(padParts[i], out var v) ? v : 0;
                var padT = Pad(0);
                var padR = padParts.Length > 1 ? Pad(1) : padT;
                var padB = padParts.Length > 2 ? Pad(2) : padT;
                var padL = padParts.Length > 3 ? Pad(3) : padR;
                s.BgPadTopPt = padT;
                s.BgPadBottomPt = padB;
                s.BgPadLeftPt = padL;
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
        else if (prop == "margin-right")
        {
            // Recorded for every document; only the float flow reads it (see
            // StyledDoc.MarginRightPt).
            if (TryParseLength(val, out var mrPts) && mrPts > 0) s.MarginRightPt = mrPts;
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
            // (the expected render clips content at one body margin left of the
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
            // em heights scale with the element's RESOLVED font size (a 12/11 factor
            // maps our 11pt body default onto the browser's 16px=12pt em base).
            double hPt;
            var em = Regex.Match(val, @"^([\d.]+)\s*em$", RegexOptions.IgnoreCase);
            if (em.Success && double.TryParse(em.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var emv))
                hPt = emv * s.FontSize * (12.0 / 11.0);
            else if (!TryParseLength(val, out hPt))
                return;
            // Both properties state the same FLOOR: measured, a
            // declared height and a min-height behave identically - the element's
            // own content keeps its position and only what FOLLOWS the element
            // moves down to clear the floor.
            if (hPt > s.HeightFloorPt) s.HeightFloorPt = hPt;
            // Browser-UA flow: min-height paints and pads nothing, so it leaves
            // ExplicitHeight (the box/spacer channel) alone and speaks only
            // through the floor above.
            if (prop == "min-height" && s.UaSerif) return;
            if (hPt > s.ExplicitHeight) s.ExplicitHeight = hPt;
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
              || prop == "border-style"
              || prop == "border-top" || prop == "border-bottom"
              || prop == "border-left" || prop == "border-right")
        {
            // A border-TOP declaration over a none/zero box is a divider rule,
            // not a frame; any other side (or the shorthand) re-authors the box.
            // The per-side TRIPLET spelling (`border-style: solid none none`,
            // the browser-saved email's divider) marks the same top-only rule.
            if (prop == "border-top"
                && !val.Contains("none", StringComparison.OrdinalIgnoreCase)
                && s.BorderColor is null)
                s.BorderTopOnly = true;
            // The per-side TRIPLET spelling is explicit about its sides — it
            // marks the top-only rule regardless of declaration order.
            else if (prop == "border-style"
                && Regex.IsMatch(val.Trim(), @"^solid(\s+none){1,3}$", RegexOptions.IgnoreCase))
                s.BorderTopOnly = true;
            else if (prop is "border-bottom" or "border-left" or "border-right"
                     && !val.Contains("none", StringComparison.OrdinalIgnoreCase))
                s.BorderTopOnly = false;
            // …whose colour may carry -moz- debris after the real value.
            var c = ParseCssColor(val) ?? (Regex.Match(val,
                    @"rgb\([^)]*\)|#[0-9a-fA-F]{3,6}") is { Success: true } cm
                ? ParseCssColor(cm.Value) : null);
            if (c is not null) s.BorderColor = c;
            var wm = Regex.Match(val, @"([\d.]+)\s*(px|pt)", RegexOptions.IgnoreCase);
            if (wm.Success && double.TryParse(wm.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var bw) && bw > 0)
                s.BorderWidth = bw * (wm.Groups[2].Value.Equals("pt",
                    StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.75); // px → pt
            // An EXPLICIT zero ("border-width: 0px" — or the unit-free "0", which
            // is a valid CSS zero length) authors NO border — it must not fall
            // through to the 1px default.
            else if (wm.Success || Regex.IsMatch(val.Trim(), @"^0(\.0+)?\s*(!.*)?$"))
            {
                s.BorderWidth = 0;
                return;
            }
            else if (s.BorderWidth <= 0)
                s.BorderWidth = 0.75;
            // A border with an unspecified colour defaults to black (CSS `border:1px solid`).
            if (s.BorderColor is null && val.IndexOf("none", StringComparison.OrdinalIgnoreCase) < 0)
                s.BorderColor = Color.FromArgb(0, 0, 0);
        }
        else if (prop == "border-style")
        {
            // `border-style: solid` with no width authors a visible border: the
            // expected render strokes it 1 pt wide in the text colour (probed:
            // the 200px border-radius box strokes w=1.0 centred on a 151 pt
            // centreline = 150 pt content + 2×1 pt border).
            if (!val.Contains("none", StringComparison.OrdinalIgnoreCase)
                && !val.Contains("hidden", StringComparison.OrdinalIgnoreCase))
            {
                if (s.BorderWidth <= 0) s.BorderWidth = StyleOnlyBorderPt;
                s.BorderColor ??= Color.FromArgb(0, 0, 0);
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
    /// measured — the stroke draws exactly 1 pt.</summary>
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
}
