using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The stages of the CSS rule application: one selector's declarations.
    private static void ApplyCssSelector(CssRulesApplyState cq, string selector)
    {
        if (!cq.css!.TryGetValue(selector, out var decls)) return;
        foreach (var kv in decls)
        {
            if (!ApplyCssDeclaration(cq, kv)) break;
        }
        ApplyPaintedBox(decls, cq.s);
    }

    /// <summary>The painted-box model: a background over a declared width × height fills that
    /// box once, with any border drawn as the box's chrome. A tiny data-URI tile repeated over
    /// the sized element paints the same way (the 1×1-GIF tiling-pattern idiom). The fill and
    /// the declared box travel together — neither applies without the other, so a declaration
    /// block that carries only layout properties still changes nothing. A viewport-relative
    /// height (`100vh`) is kept as its fraction and resolved where the box is painted.</summary>
    private static void ApplyPaintedBox(Dictionary<string, string> decls, BlockStyle s)
    {
        // An absolutely (or fixed) positioned element sits OUTSIDE normal flow - real CSS
        // paints its background at its own stacking position, which a later, opaque,
        // normally-flowing sibling routinely covers (an off-canvas drawer behind the
        // page's real content is the common case). This flow-only renderer has no
        // stacking/paint-order model to reproduce that cover, and the painted-box handoff
        // below (BgBoxHeightPt riding pb.parent onto the first child that flushes) is a
        // FLOW concept — carrying an out-of-flow element's box through it paints ink no
        // later sibling ever gets a chance to hide. Never painting it is the closer match:
        // measured on a dashboard page's absolutely positioned #leftpane (a Material-Design-Lite
        // off-canvas sidebar, left:0/top:0/width:1306px/height:519px/background:#f5f5f5)
        // sat in front of the real content with nothing in this renderer to cover it.
        if (decls.TryGetValue("position", out var posDecl)
            && posDecl.Trim() is "absolute" or "fixed") return;
        if (!decls.TryGetValue("height", out var boxH)) return;
        // A box with no declared width spans its container's content width - only an IMAGE
        // paints such a box (a fill over an unsized element is the ordinary line band).
        var boxWPt = decls.TryGetValue("width", out var boxW) && TryParseLength(boxW) is { } declW ? declW : 0;
        if (boxWPt <= 0 && BackgroundImageUrl(decls) is null) return;
        var boxHPt = TryParseLength(boxH);
        var boxHVh = boxHPt is null ? TryParseViewportFraction(boxH) : null;
        if (boxHPt is null && boxHVh is null) return;
        Color? fill = DataUriTileFill(decls);
        if (fill is null
            && (decls.TryGetValue("background-color", out var bg) || decls.TryGetValue("background", out bg))
            && ParseCssColor(bg) is { } solid
            && !(solid.R >= 250 && solid.G >= 250 && solid.B >= 250))
            fill = solid;
        // An image over the sized box paints the same way a fill does: the box is drawn
        // once at the element's origin and the image sits inside it (the fill, when both
        // are declared, goes under the image).
        var image = BackgroundImageUrl(decls);
        if (fill is null && image is null) return;
        if (image is not null)
        {
            s.BgImageSrc = image;
            s.BgImageSize = decls.TryGetValue("background-size", out var bsz) ? bsz.Trim() : "";
        }
        s.BackgroundColor = fill;
        s.BgBoxWidthPt = boxWPt;
        s.BgBoxIndentPt = s.LeftIndent;
        s.BgBoxHeightPt = boxHPt ?? 0;
        s.BgBoxHeightVh = boxHVh ?? 0;
        if (boxHPt is { } h) s.ExplicitHeight = Math.Max(s.ExplicitHeight, h);
    }

    /// <summary>The url a `background-image` (or the `background` shorthand) names, unquoted; null
    /// when the declarations carry none.</summary>
    private static string? BackgroundImageUrl(Dictionary<string, string> decls)
    {
        if (!decls.TryGetValue("background-image", out var bg) && !decls.TryGetValue("background", out bg))
            return null;
        var m = Regex.Match(DecodeEntities(bg), @"url\(\s*[""']?([^""')]+?)[""']?\s*\)", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    /// <summary>A percentage width recorded for PAINTING only - the background box a container
    /// fills - never for layout: a stylesheet width that moved the wrap metric would repaginate
    /// every document that declares one.</summary>
    private static void ApplyPaintOnlyWidthFraction(string val, BlockStyle s)
    {
        if (Regex.Match(val.Trim(), @"^([0-9.]+)\s*%$") is { Success: true } m
            && double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var pct)
            && pct > 0 && pct <= 100)
            s.DeclaredWidthFrac = pct / 100.0;
    }

    /// <summary>A `vh` length as its fraction of the viewport height; null for any other unit.</summary>
    private static double? TryParseViewportFraction(string s)
    {
        var m = Regex.Match(s.Trim(), @"^(\d+(?:\.\d+)?)\s*vh$", RegexOptions.IgnoreCase);
        return m.Success ? double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) / 100.0 : null;
    }

    /// <summary></summary>
    private static bool ApplyCssDeclaration(CssRulesApplyState cq, KeyValuePair<string, string> kv)
    {
        // A container's own padding, recorded for its background box ALONE and never as an
        // else-if in the chain below: the metric flow has its own handling for padding-left and
        // padding-bottom further down, and an arm placed above it would silently shadow it.
        if (kv.Key is "padding-top" or "padding-bottom" or "padding-left"
            && TryParseLength(kv.Value) is { } bgPadOne)
        {
            if (kv.Key == "padding-top") cq.s.BgSpanPadTopPt = bgPadOne;
            else if (kv.Key == "padding-bottom") cq.s.BgSpanPadBottomPt = bgPadOne;
            else cq.s.BgSpanPadLeftPt = bgPadOne;
        }
        // page-break-before:always — a genuine pagination directive, honoured here.
        if (kv.Key == "page-break-before"
            && kv.Value.Contains("always", StringComparison.OrdinalIgnoreCase))
            cq.s.PageBreakBefore = true;
        else if (kv.Key == "page-break-after"
            && kv.Value.Contains("always", StringComparison.OrdinalIgnoreCase))
            cq.s.PageBreakAfter = true;
        // Print-authored cover documents (a body{margin:0} page with an
        // explicit page-break-after separator): the cover classes' OWN type
        // scale and physical-unit margins ARE the layout — the calibrated
        // exclusion below would put the whole cover at the page top.
        // …and the float flow takes its sizes from the sheet as well: the
        // certificate's whole type scale is authored there - `.certificate`
        // sizes the body at 14 px (which is what its table cells render at) and
        // `#title` at 11 px (which is what makes its h1 2em = 16.5 pt).
        else if ((cq.coverStyles || cq.floatFlow) && kv.Key == "font-size")
            ApplyDeclaration(kv.Key, kv.Value, cq.s);
        else if (cq.coverStyles && kv.Key == "margin")
        {
            // TryParseLength deliberately rejects zero (callers treat 0 as
            // "absent") — a shorthand's explicit 0 slots must still parse.
            static double? CoverLen(string v)
            {
                double pt = 0;
                if (TryParseLength(v) is { } len) { pt = len; return pt; }
                if (Regex.IsMatch(v.Trim(), @"^0(px|pt|em|rem|in|cm|mm)?$",
                        RegexOptions.IgnoreCase)) { pt = 0; return pt; }
                return null;
            }
            var mParts = kv.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (mParts.Length >= 1 && CoverLen(mParts[0]) is { } cmT)
            {
                cq.s.MarginTop = cmT;
                // The cover's margin-top positions it on PAGE 1's fresh page —
                // it must survive the flow's page-top margin suppression.
                if (cmT > 0) cq.s.MarginTopAlways = true;
                var cmB = cmT;
                if (mParts.Length >= 3 && CoverLen(mParts[2]) is { } cmB3) cmB = cmB3;
                cq.s.MarginBottom = cmB;
            }
        }
        else if (cq.coverStyles && kv.Key == "line-height"
                 && double.TryParse(kv.Value.Trim(), System.Globalization.NumberStyles.Float,
                     System.Globalization.CultureInfo.InvariantCulture, out var clhF)
                 && clhF > 0)
            cq.s.LineFactor = clhF;
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
            ApplyDeclaration(kv.Key, kv.Value, cq.s);
        // A class rule's WIDTH stays excluded from the list above on purpose: applying it would
        // move the wrap metric and pagination. A PERCENTAGE width is still recorded, on a field
        // only the background box reads - where a container paints, the box's geometry is INK,
        // and reading it there changes no measurement. The point width is deliberately NOT
        // recorded, because the float flow does read that one.
        else if (kv.Key == "width") ApplyPaintOnlyWidthFraction(kv.Value, cq.s);

        // The metric flow reproduces the CSS-driven layout, so for it
        // the layout properties ARE the spec: stylesheet font sizes, MARGIN-LEFT
        // class indents, and centering all apply.
        else if (cq.metricLayout && kv.Key is "font-size" or "margin-left" or "padding-left"
                     or "padding-bottom")
            ApplyDeclaration(kv.Key, kv.Value, cq.s);
        else if (cq.metricLayout && kv.Key == "text-align")
            cq.s.AlignCenter = kv.Value.Trim().Equals("center", StringComparison.OrdinalIgnoreCase);
        return true;
    }
}
