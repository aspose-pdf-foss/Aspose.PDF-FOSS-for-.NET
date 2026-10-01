using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The UA sheet's block margin for a tag, in em of the element's own size (0 for a tag that carries none).</summary>
    private static double UaBlockMarginEmOf(string tag) => tag.ToLowerInvariant() switch
    {
        "p" or "h4" or "blockquote" or "ul" or "ol" => UaBlockMarginEm,
        "h1" => 0.67, "h2" => 0.75, "h3" => 0.83, "h5" => 1.50, "h6" => 1.67,
        _ => 0,
    };

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
        if (articleRhythm && ApplyArticleRhythmBlockStyle(tag, s)) return;
        // Sectioned-report rhythm: the UA sheet's real block margins, in em of the
        // element's OWN size. A paragraph's 1.12em is the value the expected render
        // uses; the legacy flow below deliberately stacks line-on-line instead.
        if (uaBlockRhythm && UaBlockMarginEmOf(tag) is > 0 and var uaRhythmEm)
        {
            s.MarginTop = s.MarginBottom = uaRhythmEm * s.FontSize;
            if (s.InPageFragment && tag.ToLowerInvariant() is "ul" or "ol") s.LeftIndent += InPageListIndentPt;
            return;
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
            ApplyUaDefaultBlockStyle(tag, s, browserUa, msoParagraphs, html5UaHeadings);
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
            case "ol":         s.LeftIndent += s.InPageFragment ? InPageListIndentPt : 20; break;
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
    // (a `*prop` / `_prop` browser hack is an invalid name: the declaration is dropped, not read as `prop`)
    private static readonly Regex StyleDeclRx = new(
        @"(?<![*_\w-])([a-z-]+)\s*:\s*((?:url\([^)]*\)|[^;])+?)\s*(?:;|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static bool HasInlineIndentOverride(Dictionary<string, string>? attrs)
    {
        if (attrs is null || !attrs.TryGetValue("style", out var styleStr) || string.IsNullOrWhiteSpace(styleStr))
            return false;
        return Regex.IsMatch(styleStr, @"(padding-left|margin-left)\s*:", RegexOptions.IgnoreCase);
    }

    /// <summary>True when the text opens a single or double quote it never closes.</summary>
    private static bool HasUnbalancedQuote(string s)
    {
        int single = 0, dbl = 0;
        foreach (var ch in s)
        {
            if (ch == '\'') single++;
            else if (ch == '"') dbl++;
        }
        return (single & 1) == 1 || (dbl & 1) == 1;
    }

    /// <summary>An in-page fragment's list items start 30 pt inside the content edge
    /// (probed: "Alpha item" at x = 120 on a 90 pt margin, the bullet ending at 115.5).</summary>
    private const double InPageListIndentPt = 30.0;

    private static void ApplyInlineStyle(Dictionary<string, string>? attrs, BlockStyle s, ParseBlocksState? pb = null)
    {
        if (attrs is null) return;
        if (!attrs.TryGetValue("style", out var styleStr) || string.IsNullOrWhiteSpace(styleStr)) return;
        // A style attribute whose value carries an unbalanced quote is not a declaration
        // block at all: it applies nothing (probed: `style="style='font-family:Arial;
        // font-size:13; "` leaves the text in the default serif at the default size).
        if (HasUnbalancedQuote(styleStr)) return;
        ApplyDeclarationString(styleStr, s);
        // An inline background over a declared width × height is a painted box, as it
        // is from a class rule (probed: a 900px × 100vh lightblue div fills that box).
        var decls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in StyleDeclRx.Matches(styleStr))
            decls[m.Groups[1].Value.ToLowerInvariant()] = m.Groups[2].Value.Trim();
        // An absolutely (or fixed) positioned element paints its background at its own
        // stacking position in real CSS, which a later, normally-flowing opaque sibling
        // routinely covers (an off-canvas drawer sat behind the page's real content is
        // the common shape - measured on a Material-Design-Lite dashboard's #leftpane sidebar).
        // This flow-only renderer has no paint-order model to reproduce that cover, so
        // ApplyDeclarationString's plain `background`/`background-color` handling - which
        // does not know about `position` - must not leave a background on this style for
        // ApplyArticleRhythmAndBands' per-line bgSpans push to pick up right after.
        if (decls.TryGetValue("position", out var posDecl) && posDecl.Trim() is "absolute" or "fixed")
        {
            s.BackgroundColor = null;
            ApplyAbsolutePositionResolve(decls, s, pb);
        }
        else
            ApplyPaintedBox(decls, s);
        ApplyCardFrame(decls, s);
        // A border-box card's own border, plus a content-box child's padding, insets
        // where THEIR children start - textbook CSS box model, measured exactly
        // additive (border_pt + padding_pt) even threaded through a transformed
        // position:absolute ancestor chain. Only tracked once something has resolved an
        // absolute containing box (InAbsoluteChain), so this is a no-op for the vast
        // majority of the corpus. (An earlier row applied a measured 0.553 discount here: it
        // was compensating for seating the chain from the calibrated flow's 96 pt margin
        // instead of the page's own 90 - the reference seats an absolute chain from the
        // PAGE margin box, and with that the additive chrome lands the dashboard's chart at the
        // reference's 376.5 / 90.75 exactly.)
        if (s.InAbsoluteChain)
        {
            var border = decls.TryGetValue("border", out var bd) ? BoxChromeLen(bd)
                : decls.TryGetValue("border-left-width", out var blw) ? BoxChromeLen(blw) : 0;
            s.AbsOriginLeftPt += border + PaddingLeftPt(decls);
            s.AbsOriginTopPt += border + PaddingTopPt(decls);
        }
    }

    /// <summary>The widget card: a positioned element that draws a solid border becomes the frame
    /// its descendants carry, and every element between the card's outer edge and the picture
    /// adds its border and padding to the inset the frame is drawn at. The reference strokes
    /// that border around an inline chart (measured on a dashboard page: a 1px #eee `position:absolute`
    /// card, a 16px-padded container, the svg - the frame's lines sit 16.5px outside the
    /// picture on the left, right and top, and half a border below it). Its background is not
    /// painted (see the absolute-position note above); only the border is.</summary>
    private static void ApplyCardFrame(Dictionary<string, string> decls, BlockStyle s)
    {
        var positioned = decls.TryGetValue("position", out var pos) && pos.Trim() is "absolute" or "fixed";
        var borderPt = decls.TryGetValue("border", out var bd) ? BoxChromeLen(bd) : 0;
        if (positioned && borderPt > 0 && bd is not null && bd.Contains("solid", StringComparison.OrdinalIgnoreCase)
            && ParseCssColor(bd) is { } borderColor)
        {
            s.CardFrameColor = borderColor;
            s.CardFrameBorderPt = borderPt;
            s.CardFrameInsetPt = borderPt + PaddingLeftPt(decls);
            return;
        }
        if (s.CardFrameColor is not null)
            s.CardFrameInsetPt += borderPt + PaddingLeftPt(decls);
    }

    /// <summary>The top padding a declaration block sets, from `padding` or `padding-top`.</summary>
    private static double PaddingTopPt(Dictionary<string, string> decls)
    {
        if (decls.TryGetValue("padding-top", out var pt)) return BoxChromeLen(pt);
        if (decls.TryGetValue("padding", out var pd)) return BoxChromeLen(pd);
        return 0;
    }

    /// <summary>The left padding a declaration block sets, from `padding` or `padding-left`.</summary>
    private static double PaddingLeftPt(Dictionary<string, string> decls)
    {
        if (decls.TryGetValue("padding-left", out var pl)) return BoxChromeLen(pl);
        if (decls.TryGetValue("padding", out var pd)) return BoxChromeLen(pd);
        return 0;
    }

    /// <summary>A length declaration that may legitimately be zero — <see cref="TryParseLength"/>
    /// hands back null for both "not declared" and "declared 0", which this call site must tell
    /// apart (an absolute box at `left:0` is fully resolved, not unresolved).</summary>
    private static double? ParseOffsetOrZero(string s) => IsZeroLength(s) ? 0.0 : TryParseLength(s);

    /// <summary>Resolves a `position:absolute`/`fixed` element's own box against its nearest
    /// positioned ancestor (BlockStyle.AbsOriginLeftPt/WidthPt, composed forward through nested
    /// absolute ancestors by BeginBlockStyle's copy-forward and seeded at the document root to
    /// the page's baseline content box) and, when the box is anchored by `left`, publishes its
    /// resolved right edge as a candidate the page may need to widen for.
    /// Measured against the reference (two probe rounds, 18+
    /// confirmatory cells): a `left`-anchored box can push the page wider; a `right`-anchored one
    /// (no `left` present) never can, however deep the nesting — so only the `left` branch
    /// updates the document-wide accumulator, but BOTH branches update this element's own
    /// AbsOriginLeftPt/WidthPt, since a `left`-anchored DESCENDANT of a `right`-anchored ancestor
    /// must still resolve against that ancestor's real box, not the page origin. An element with
    /// neither `left` nor `right` (or no declared `width`) is not modelled — it inherits its
    /// parent's containing box unchanged, the safe (never-widens) default.</summary>
    private static void ApplyAbsolutePositionResolve(Dictionary<string, string> decls, BlockStyle s, ParseBlocksState? pb)
    {
        if (!decls.TryGetValue("width", out var w)) return;
        var width = ParseOffsetOrZero(w);
        if (width is null) return;
        double structuralLeft;
        bool leftAnchored;
        if (decls.TryGetValue("left", out var l) && ParseOffsetOrZero(l) is { } leftPt)
        {
            structuralLeft = s.AbsOriginLeftPt + leftPt;
            leftAnchored = true;
        }
        else if (decls.TryGetValue("right", out var r) && ParseOffsetOrZero(r) is { } rightPt)
        {
            structuralLeft = s.AbsOriginLeftPt + s.AbsOriginWidthPt - rightPt - width.Value;
            leftAnchored = false;
        }
        else return;
        // Own transform: a translate shifts where this element and everything inside it PAINTS,
        // applied once to the origin its descendants resolve against (probed: a child of a
        // `matrix(1,0,0,1,365,8)` parent paints at 90 + 273.75, 72 + 6).
        var ownTxPt = decls.TryGetValue("transform", out var tr) ? TransformTranslateXPt(tr) ?? 0 : 0;
        var ownTyPt = decls.TryGetValue("transform", out var trY) ? TransformTranslateYPt(trY) ?? 0 : 0;
        var translateSum = s.AbsTranslateSumPt + ownTxPt;
        // The page widens for the box's INK, not its box: a box that paints nothing - no
        // background it can parse, no border - contributes nothing whatever its width and
        // offsets, `right` never counts, and every translate X of a transformed absolute
        // ancestor-or-self counts a SECOND time on top of the painted edge. Measured on
        // 12+38 cells: 1263 px = (365 + 533) + 365 for a 533 px
        // bordered tile under a translate(365px) parent, while its 1306 px unpainted
        // ancestor adds nothing.
        if (leftAnchored && pb is not null && pb.cv is not null && AbsoluteBoxPaints(decls))
            pb.cv.absMaxResolvedRightPt = Math.Max(pb.cv.absMaxResolvedRightPt,
                structuralLeft + ownTxPt + width.Value + translateSum);
        var structuralTop = s.AbsOriginTopPt
            + (decls.TryGetValue("top", out var t) && ParseOffsetOrZero(t) is { } topPt ? topPt : 0);
        s.AbsOriginLeftPt = structuralLeft + ownTxPt;
        s.AbsOriginTopPt = structuralTop + ownTyPt;
        s.AbsOriginWidthPt = width.Value;
        s.AbsTranslateSumPt = translateSum;
        s.InAbsoluteChain = true;
    }

    /// <summary>Whether an absolutely positioned box paints anything of its own that the
    /// reference draws - a border, or a background it parses. A `background` shorthand that
    /// carries a box keyword (`padding-box`, `border-box`, `content-box`) is dropped by the
    /// reference whole, colour and all (probed: the Material sidebar's `background: none 0% 0% /
    /// auto repeat scroll padding-box border-box rgb(245,245,245)` paints nothing and widens
    /// nothing; the same box with `background-color` paints and widens).</summary>
    private static bool AbsoluteBoxPaints(Dictionary<string, string> decls)
    {
        static bool Transparent(string v) => v.Trim().Equals("transparent", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(v, @"rgba\([^)]*,\s*0(?:\.0+)?\s*\)", RegexOptions.IgnoreCase);
        if (decls.TryGetValue("border", out var bd) && BoxChromeLen(bd) > 0
            && bd.Contains("solid", StringComparison.OrdinalIgnoreCase))
            return true;
        if (decls.TryGetValue("background-color", out var bc) && ParseCssColor(bc) is not null && !Transparent(bc))
            return true;
        if (decls.TryGetValue("background", out var bg) && ParseCssColor(bg) is not null && !Transparent(bg)
            && !Regex.IsMatch(bg, @"\b(padding|border|content)-box\b", RegexOptions.IgnoreCase))
            return true;
        return false;
    }

    /// <summary>The vertical component of a CSS `transform`, in points - the twin of
    /// <see cref="TransformTranslateXPt"/>: `translate(tx, ty)`, `translateY(ty)`, or the
    /// axis-aligned identity `matrix(1,0,0,1,tx,ty)`.</summary>
    private static double? TransformTranslateYPt(string transform)
    {
        var m = Regex.Match(transform,
            @"matrix\(\s*1\s*,\s*0\s*,\s*0\s*,\s*1\s*,\s*-?[\d.]+(?:px)?\s*,\s*(-?[\d.]+)(?:px)?\s*\)",
            RegexOptions.IgnoreCase);
        if (!m.Success)
            m = Regex.Match(transform, @"translate\(\s*-?[\d.]+(?:px)?\s*,\s*(-?[\d.]+)(?:px)?", RegexOptions.IgnoreCase);
        if (!m.Success)
            m = Regex.Match(transform, @"translateY\(\s*(-?[\d.]+)(?:px)?", RegexOptions.IgnoreCase);
        return m.Success
            ? double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * 0.75
            : null;
    }

    /// <summary>The horizontal component of a CSS `transform`, in points: `translate(tx, ty)`,
    /// `translateX(tx)`, or the axis-aligned identity form of `matrix(1,0,0,1,tx,ty)` (a real
    /// rotation/scale matrix is not modelled here). Null when `transform` is `none`/absent or
    /// matches neither form.</summary>
    private static double? TransformTranslateXPt(string transform)
    {
        var m = Regex.Match(transform,
            @"matrix\(\s*1\s*,\s*0\s*,\s*0\s*,\s*1\s*,\s*(-?[\d.]+)(?:px)?\s*,\s*-?[\d.]+(?:px)?\s*\)",
            RegexOptions.IgnoreCase);
        if (!m.Success)
            m = Regex.Match(transform, @"translate(?:X)?\(\s*(-?[\d.]+)(?:px)?", RegexOptions.IgnoreCase);
        return m.Success
            ? double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * 0.75
            : null;
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
        if (prop == "font-size") ApplyFontSizeDecl(val, s);
        else if (prop == "font-family")
        {
            var fam = FirstFontFamily(val);
            if (fam is not null) { s.FontFamily = fam; s.FontFamilyStack = val; }
        }
        else if (prop == "line-height") ApplyLineHeightDecl(val, s);
        else if (prop == "font") ApplyFontDecl(val, s);
        else if (prop == "font-weight") ApplyFontWeightDecl(val, s);
        else if (prop == "font-style")
        {
            if (val is "italic" or "oblique")
                s.FontRes = s.FontRes == "F2" ? "F2" : "F3";
        }
        else if (prop == "text-align") ApplyTextAlignDecl(val, s);
        else if (prop == "float") ApplyFloatDecl(val, s);
        else if (prop == "margin-top") ApplyMarginTopDecl(val, s);
        else if (prop == "margin-bottom") ApplyMarginBottomDecl(val, s);
        else if (prop == "line-height") ApplyLineHeight2Decl(val, s);
        else if (prop == "width") ApplyWidthDecl(val, s);
        else if (prop == "margin") ApplyMarginDecl(val, s);
        else if (prop == "padding") ApplyPaddingDecl(val, s);
        else if (prop == "padding-bottom" && s.FormDialect) ApplyPaddingBottomDecl(val, s);
        else if (prop == "margin-right") ApplyMarginRightDecl(val, s);
        else if (prop == "margin-left" || prop == "padding-left") ApplyMarginLeftDecl(prop, val, s);
        // Styled-article: a container's padding-bottom is real space below its
        // last line (the panel header's .5rem) — carried on the close channel.
        else if (prop == "padding-bottom" && s.ArticleRhythm)
        {
            if (TryParseLength(val) is { } pb && pb > s.MarginBottom) s.MarginBottom = pb;
        }
        else if (prop == "height" || prop == "min-height")
        {
            if (!ApplyHeightDecl(prop, val, s)) return;
        }
        else if (prop == "color") ApplyColorDecl(val, s);
        else if (prop == "background-color" || prop == "background") ApplyBackgroundColorDecl(val, s);
        else if (prop == "page") ApplyPageNameDecl(val, s);
        else if (prop == "page-break-before" || prop == "break-before") ApplyPageBreakBeforeDecl(val, s);
        else if (prop == "page-break-after" || prop == "break-after") ApplyPageBreakAfterDecl(val, s);
        else if (prop == "border" || prop == "border-color" || prop == "border-width"
              || prop == "border-style"
              || prop == "border-top" || prop == "border-bottom"
              || prop == "border-left" || prop == "border-right")
        {
            if (!ApplyBorderDecl(prop, val, s)) return;
        }
        else if (prop == "border-style") ApplyBorderStyleDecl(val, s);
        else if (prop == "border-radius") ApplyBorderRadiusDecl(val, s);
    }

    /// <summary>Border width of a `border-style` declaration that names no width:
    /// measured — the stroke draws exactly 1 pt.</summary>
    private const double StyleOnlyBorderPt = 1.0;

    /// <summary>First concrete (non-generic) family name from a CSS font-family list,
    /// with quotes stripped. Returns null for a purely generic list (serif/sans-serif/
    /// monospace/cursive/fantasy) so the Standard-14 Helvetica default applies.</summary>
    internal static string? FirstFontFamily(string value)
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
