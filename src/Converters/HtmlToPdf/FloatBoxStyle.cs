using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The inherited text state of an element: face, size, weight, colour, alignment,
    /// wrapping and a declared line height.</summary>
    private sealed class FbStyle
    {
        public bool Sans;                                     // Arial; else Times New Roman
        public double Px = FbDefaultFontPx;
        public bool Bold;
        public string Rgb = "0 0 0";
        public string Align = "left";
        public bool Nowrap;
        public double LineHeightPx;                           // a line height declared as a LENGTH (px, pt, rem); 0 = none
        public double LineHeightFactor;                       // one declared as a number, em or % - inherited as a factor of each descendant's own size (probed); 0 = none
        public string? BgRgb;                                 // an inline element's background, painted behind its runs
        public FbFace? Own;                                   // one of the sheet's own faces; null = a built-in face
        public bool Uppercase;                                // text-transform: uppercase
        public double RaisePt;                                // a relatively positioned run's lift above the baseline
        public FbStyle Clone() => (FbStyle)MemberwiseClone();
        public string MeasureFace => Sans ? (Bold ? FbSansBoldMeasureFace : FbSansMeasureFace) : (Bold ? FbSerifBoldMeasureFace : FbSerifMeasureFace);
        public string Res => Sans ? (Bold ? FbSansBoldRes : FbSansRes) : (Bold ? FbSerifBoldRes : FbSerifRes);
        public double Pt => Px * FbPxPt;
    }

    /// <summary>Four edges in pt (top, right, bottom, left).</summary>
    private sealed class FbEdges
    {
        public double T, R, B, L;
        public double H => L + R;
        public double V => T + B;
    }

    /// <summary>The box properties of an element that do not inherit, in pt.</summary>
    private sealed class FbBoxProps
    {
        public string Display = "";                           // "", block, inline, inline-block, none, table…
        public string Float = "none";
        public string Clear = "none";
        public bool WidthAuto = true;
        public double WidthPt;
        public bool HeightAuto = true;
        public double HeightPt;
        public bool MarginLeftAuto, MarginRightAuto;
        public bool VAlignTop;                                // vertical-align: top (an inline-block's top on the line top)
        public bool PadDeclared;                              // a padding declaration exists (else a cell takes cellpadding)
        public bool MarginDeclared;                           // a margin declaration exists (else a control takes the UA margins)
        public bool Bfc;                                      // overflow other than visible: the box contains its floats
        public FbEdges Margin = new(), Pad = new(), Border = new();
        public string BorderRgb = "0 0 0";
        public string? BgRgb;
        public bool HasVisibleBorder => Border.T > 0 || Border.R > 0 || Border.B > 0 || Border.L > 0;
        public double ChromeH => Border.H + Pad.H;
        public double ChromeV => Border.V + Pad.V;
    }

    private static FbStyle FbRootStyle() => new();

    /// <summary>An element's declared value for a property, inline style first then the sheet's
    /// cascade, then a rule keyed on the element's type attribute (`input[type="radio"]`, with or
    /// without an ancestor part); an `inherit` value is no declaration.</summary>
    private static string? FbDecl(FbState fb, HtmlNode el, string prop)
    {
        // (the styled-sheet claim resolves every property through its own cascade)
        var v = fb.sheet is { } sheet
            ? (FbSheetDecls(sheet, el).TryGetValue(prop, out var sv) ? sv : null)
            : DomDecl(el, prop, fb.css) ?? FbAttrRuleDecl(fb, el, prop);
        return v is not null && v.Trim().Equals("inherit", StringComparison.OrdinalIgnoreCase) ? null : v;
    }

    private static readonly Regex FbAttrRuleRx = new(@"^(?:(?<anc>[^\s\[]+)\s+)?(?<tag>[a-zA-Z]+)\[type\s*=\s*[""']?(?<type>[a-zA-Z]+)[""']?\]$", RegexOptions.Compiled);

    private static string? FbAttrRuleDecl(FbState fb, HtmlNode el, string prop)
    {
        if (el.Attrs is null || !el.Attrs.TryGetValue("type", out var type)) return null;
        string? best = null;
        foreach (var (anc, tag, ty, decls) in fb.attrRules)
        {
            if (!decls.TryGetValue(prop, out var val)) continue;
            if (!tag.Equals(el.Tag, StringComparison.OrdinalIgnoreCase) || !ty.Equals(type.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
            if (anc.Length > 0)
            {
                var ok = false;
                for (var p = el.Parent; p is not null; p = p.Parent)
                    if (SimpleSelectorMatches(anc, p)) { ok = true; break; }
                if (!ok) continue;
            }
            best = val.Replace("!important", "").Trim();
        }
        return best;
    }

    /// <summary>The text state of an element: its parent's, updated by its own declarations and by
    /// the tags that carry typography (b / strong / th).</summary>
    private static FbStyle FbStyleOf(FbState fb, HtmlNode el, FbStyle inherited)
    {
        var st = inherited.Clone();
        if (el.Tag is "b" or "strong" or "th") st.Bold = true;
        var family = FbDecl(fb, el, "font-family");
        if (!string.IsNullOrEmpty(family))
        {
            // the first family the sheet ships, else the first generic or installed one; an unknown
            // family in between falls through to the next (the reference converter's fallback order)
            foreach (var famRaw in family.Split(','))
            {
                var fam = famRaw.Trim().Trim('"', '\'');
                if (fam.Length == 0) continue;
                if (fb.sheet is { } sheet && sheet.Faces.TryGetValue(fam, out var own)) { st.Own = own; break; }
                var lower = fam.ToLowerInvariant();
                if (lower is "arial" or "helvetica" or "verdana" or "tahoma" or "sans-serif") { st.Own = null; st.Sans = true; break; }
                if (lower is "times new roman" or "times" or "georgia" or "serif") { st.Own = null; st.Sans = false; break; }
                if (fb.sheet is null) { st.Sans = false; break; }
            }
        }
        var size = FbDecl(fb, el, "font-size");
        if (!string.IsNullOrEmpty(size))
        {
            var px = FbLengthPx(size, st.Px);
            if (px > 0) st.Px = px;
        }
        var weight = FbDecl(fb, el, "font-weight");
        if (!string.IsNullOrEmpty(weight))
        {
            var wl = weight.Trim().ToLowerInvariant();
            if (wl is "bold" or "bolder" || (int.TryParse(wl, out var wn) && wn >= 600)) st.Bold = true;
            else if (wl is "normal" or "lighter" || (int.TryParse(wl, out var wn2) && wn2 < 600)) st.Bold = false;
        }
        var color = FbDecl(fb, el, "color");
        if (!string.IsNullOrEmpty(color) && FbRgb(color) is { } rgb) st.Rgb = rgb;
        var align = FbDecl(fb, el, "text-align");
        if (!string.IsNullOrEmpty(align)) st.Align = align.Trim().ToLowerInvariant();
        else if (el.Attrs is not null && el.Attrs.TryGetValue("align", out var attrAlign)) st.Align = attrAlign.Trim().ToLowerInvariant();
        var ws = FbDecl(fb, el, "white-space");
        if (!string.IsNullOrEmpty(ws)) st.Nowrap = ws.Trim().Equals("nowrap", StringComparison.OrdinalIgnoreCase);
        var lh = FbDecl(fb, el, "line-height");
        if (!string.IsNullOrEmpty(lh))
        {
            var lhl = lh.Trim().ToLowerInvariant();
            // (probed: a number, an em or a % line height is a FACTOR of each descendant's own size -
            //  the em and % forms inherit as the factor, not as the resolved length; px / rem inherit as lengths)
            var factor = Regex.Match(lhl, @"^(\d*\.?\d+)\s*(em|%)?$");
            if (lhl == "normal") { st.LineHeightPx = 0; st.LineHeightFactor = 0; }
            else if (factor.Success)
            {
                var n = double.Parse(factor.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                st.LineHeightFactor = factor.Groups[2].Value == "%" ? n / 100.0 : n;
                st.LineHeightPx = 0;
            }
            else { st.LineHeightPx = FbLengthPx(lhl, st.Px); st.LineHeightFactor = 0; }
        }
        var transform = FbDecl(fb, el, "text-transform");
        if (!string.IsNullOrEmpty(transform)) st.Uppercase = transform.Trim().Equals("uppercase", StringComparison.OrdinalIgnoreCase);
        // a relatively positioned inline (a superscript's `top: -0.5em`) lifts its run off the baseline
        var position = FbDecl(fb, el, "position");
        if (!string.IsNullOrEmpty(position) && position.Trim().Equals("relative", StringComparison.OrdinalIgnoreCase)
            && FbDecl(fb, el, "top") is { } topV && !string.IsNullOrEmpty(topV))
            st.RaisePt = -FbLengthPx(topV, st.Px) * FbPxPt;
        return st;
    }

    /// <summary>A CSS length in px: px, pt, em (of the running size) and the absolute keywords.</summary>
    private static double FbLengthPx(string value, double emPx)
    {
        var v = value.Trim().ToLowerInvariant();
        switch (v)
        {
            case "xx-small": return 9;
            case "x-small": return 10;
            case "small": return 13;
            case "medium": return 16;
            case "large": return 18;
            case "x-large": return 24;
            case "xx-large": return 32;
        }
        var m = Regex.Match(v, @"^(-?\d*\.?\d+)\s*(px|pt|em|rem|ex|%)?$");
        if (!m.Success) return 0;
        var n = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        return m.Groups[2].Value switch
        {
            "pt" => n / FbPxPt,
            "em" => n * emPx,
            "rem" => n * FbRootFontPx,
            "ex" => n * emPx / 2,
            "%" => n / 100.0 * emPx,
            _ => n,
        };
    }

    /// <summary>A CSS colour as a PDF rgb triple, or null.</summary>
    private static string? FbRgb(string css)
    {
        if (ParseCssColor(css) is not { } c) return null;
        return Compat.Format(System.Globalization.CultureInfo.InvariantCulture, $"{c.R / 255.0:0.###} {c.G / 255.0:0.###} {c.B / 255.0:0.###}");
    }

    /// <summary>The box properties of an element against its containing block's content width
    /// (percent widths resolve against it) and its declared content height (a percent height
    /// resolves against that, and is auto when the block has none).</summary>
    private static FbBoxProps FbPropsOf(FbState fb, HtmlNode el, double cbW, double cbH = 0, double emPx = FbDefaultFontPx)
    {
        var p = new FbBoxProps();
        p.Display = (FbDecl(fb, el, "display") ?? "").Trim().ToLowerInvariant();
        p.Float = (FbDecl(fb, el, "float") ?? "none").Trim().ToLowerInvariant();
        if (p.Float != "left" && p.Float != "right") p.Float = "none";
        p.Clear = (FbDecl(fb, el, "clear") ?? "none").Trim().ToLowerInvariant();
        var width = FbDecl(fb, el, "width");
        if (string.IsNullOrEmpty(width) && el.Attrs is not null && el.Attrs.TryGetValue("width", out var wAttr)) width = wAttr;
        if (!string.IsNullOrEmpty(width) && FbSizePt(width, cbW, emPx) is { } wpt) { p.WidthAuto = false; p.WidthPt = wpt; }
        var height = FbDecl(fb, el, "height");
        if (!string.IsNullOrEmpty(height) && FbSizePt(height, cbH, emPx) is { } hpt) { p.HeightAuto = false; p.HeightPt = hpt; }
        p.MarginDeclared = FbReadBoxEdges(fb, el, "margin", p.Margin, cbW, emPx, out p.MarginLeftAuto, out p.MarginRightAuto);
        p.PadDeclared = FbReadBoxEdges(fb, el, "padding", p.Pad, cbW, emPx, out _, out _);
        FbReadBorders(fb, el, p);
        p.VAlignTop = (FbDecl(fb, el, "vertical-align") ?? "").Trim().Equals("top", StringComparison.OrdinalIgnoreCase);
        var overflow = (FbDecl(fb, el, "overflow") ?? "visible").Trim().ToLowerInvariant();
        p.Bfc = overflow is "auto" or "hidden" or "scroll";
        var bg = FbDecl(fb, el, "background-color") ?? FbDecl(fb, el, "background");
        // (a functional colour keeps its spaces: `rgb(237, 28, 36)`; a shorthand's first token is the colour)
        if (!string.IsNullOrEmpty(bg) && FbRgb(bg.Contains('(') ? bg : bg.Split(' ')[0]) is { } bgRgb) p.BgRgb = bgRgb;
        return p;
    }

    /// <summary>A width / height value in pt: px, pt, or a percent of the base; null for auto or unparsable.</summary>
    private static double? FbSizePt(string value, double pctBase, double emPx = FbDefaultFontPx, bool allowNegative = false)
    {
        var v = value.Trim().ToLowerInvariant();
        if (v == "auto" || v.Length == 0) return null;
        var pm = Regex.Match(v, @"^(-?\d+(?:\.\d+)?)\s*%$");
        if (pm.Success) return pctBase <= 0 ? null : double.Parse(pm.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) / 100.0 * pctBase;
        var px = FbLengthPx(v, emPx);
        if (px == 0 && v != "0" && !v.StartsWith("0")) return null;
        if (px < 0 && !allowNegative) return null;
        return px * FbPxPt;
    }

    /// <summary>The margin / padding edges from the shorthand and the longhands, in pt; true when
    /// the element declares any of them.</summary>
    private static bool FbReadBoxEdges(FbState fb, HtmlNode el, string box, FbEdges e, double cbW, double emPx, out bool leftAuto, out bool rightAuto)
    {
        leftAuto = rightAuto = false;
        var declared = false;
        var sh = FbDecl(fb, el, box);
        if (!string.IsNullOrEmpty(sh))
        {
            declared = true;
            var parts = sh.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string t = parts[0], r = parts.Length > 1 ? parts[1] : parts[0], b = parts.Length > 2 ? parts[2] : parts[0], l = parts.Length > 3 ? parts[3] : parts.Length > 1 ? parts[1] : parts[0];
            e.T = FbEdgePt(t, cbW, emPx); e.R = FbEdgePt(r, cbW, emPx); e.B = FbEdgePt(b, cbW, emPx); e.L = FbEdgePt(l, cbW, emPx);
            leftAuto = l.Equals("auto", StringComparison.OrdinalIgnoreCase);
            rightAuto = r.Equals("auto", StringComparison.OrdinalIgnoreCase);
        }
        var top = FbDecl(fb, el, box + "-top");
        if (!string.IsNullOrEmpty(top)) { e.T = FbEdgePt(top, cbW, emPx); declared = true; }
        var right = FbDecl(fb, el, box + "-right");
        if (!string.IsNullOrEmpty(right)) { e.R = FbEdgePt(right, cbW, emPx); rightAuto = right.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase); declared = true; }
        var bottom = FbDecl(fb, el, box + "-bottom");
        if (!string.IsNullOrEmpty(bottom)) { e.B = FbEdgePt(bottom, cbW, emPx); declared = true; }
        var left = FbDecl(fb, el, box + "-left");
        if (!string.IsNullOrEmpty(left)) { e.L = FbEdgePt(left, cbW, emPx); leftAuto = left.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase); declared = true; }
        return declared;
    }

    /// <summary>A margin or padding edge in pt (a margin may be negative; a percent is of the containing width).</summary>
    private static double FbEdgePt(string v, double cbW, double emPx = FbDefaultFontPx) => FbSizePt(v, cbW, emPx, allowNegative: true) ?? 0;

    /// <summary>The border widths and colour: the `border` shorthand, per-side shorthands, the
    /// `border-width` / `border-color` / `border-style` longhands; a side with style none has no width.</summary>
    private static void FbReadBorders(FbState fb, HtmlNode el, FbBoxProps p)
    {
        var styles = new[] { "none", "none", "none", "none" };
        var widths = new double[4];
        string? rgb = null;
        var sides = new[] { "top", "right", "bottom", "left" };
        void ApplyShorthand(string decl, int[] onto)
        {
            foreach (var tok in decl.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var t = tok.ToLowerInvariant();
                if (t is "solid" or "dashed" or "dotted" or "double" or "none" or "hidden")
                    foreach (var i in onto) { styles[i] = t; if (t != "none" && t != "hidden" && widths[i] <= 0) widths[i] = FbMediumBorderPx * FbPxPt; }
                else if (t is "thin" or "medium" or "thick" || Regex.IsMatch(t, @"^\d"))
                    foreach (var i in onto) widths[i] = FbBorderWidthPt(t);
                else if (FbRgb(tok) is { } c) rgb = c;
            }
        }
        var all = new[] { 0, 1, 2, 3 };
        // (a rule that resets `border-style: none` and then states `border: 1px solid` means the
        //  shorthand - the reset-then-set idiom; the longhands are read first so the shorthand wins)
        var bs = FbDecl(fb, el, "border-style");
        if (!string.IsNullOrEmpty(bs)) ApplyShorthand(bs, all);
        var sh = FbDecl(fb, el, "border");
        if (!string.IsNullOrEmpty(sh)) ApplyShorthand(sh, all);
        var bw = FbDecl(fb, el, "border-width");
        if (!string.IsNullOrEmpty(bw))
        {
            var parts = bw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string t = parts[0], r = parts.Length > 1 ? parts[1] : parts[0], b = parts.Length > 2 ? parts[2] : parts[0], l = parts.Length > 3 ? parts[3] : parts.Length > 1 ? parts[1] : parts[0];
            widths[0] = FbBorderWidthPt(t); widths[1] = FbBorderWidthPt(r); widths[2] = FbBorderWidthPt(b); widths[3] = FbBorderWidthPt(l);
        }
        var bc = FbDecl(fb, el, "border-color");
        if (!string.IsNullOrEmpty(bc) && FbRgb(bc) is { } bcRgb) rgb = bcRgb;
        for (var i = 0; i < 4; i++)
        {
            var side = FbDecl(fb, el, "border-" + sides[i]);
            if (!string.IsNullOrEmpty(side)) ApplyShorthand(side, new[] { i });
            var sw = FbDecl(fb, el, "border-" + sides[i] + "-width");
            if (!string.IsNullOrEmpty(sw)) widths[i] = FbBorderWidthPt(sw);
            var ss = FbDecl(fb, el, "border-" + sides[i] + "-style");
            if (!string.IsNullOrEmpty(ss)) styles[i] = ss.Trim().ToLowerInvariant();
        }
        for (var i = 0; i < 4; i++) if (styles[i] is "none" or "hidden") widths[i] = 0;
        p.Border.T = widths[0]; p.Border.R = widths[1]; p.Border.B = widths[2]; p.Border.L = widths[3];
        if (rgb is not null) p.BorderRgb = rgb;
    }

    private static double FbBorderWidthPt(string v)
    {
        var t = v.Trim().ToLowerInvariant();
        return t switch
        {
            "thin" => 1 * FbPxPt,
            "medium" => FbMediumBorderPx * FbPxPt,
            "thick" => 5 * FbPxPt,
            _ => FbLengthPx(t, FbDefaultFontPx) * FbPxPt,
        };
    }

    private static bool FbIsBlockTag(string tag) => tag is "div" or "p" or "form" or "table" or "ul" or "ol" or "li" or "hr" or "blockquote" or "fieldset" or "center" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "pre" or "address" or "dl" or "dt" or "dd"
        or "header" or "footer" or "main" or "nav" or "article" or "aside" or "section" or "figure" or "figcaption" or "hgroup" or "details" or "summary";

    /// <summary>Is the element laid out as a block box (its own tag, or a display override)?</summary>
    private static bool FbIsBlock(HtmlNode el, FbBoxProps p)
    {
        if (p.Display is "block" or "table") return true;
        if (p.Display is "inline" or "inline-block") return false;
        return FbIsBlockTag(el.Tag);
    }
}
