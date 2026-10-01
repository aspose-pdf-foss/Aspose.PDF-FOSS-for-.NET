using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The `position` values that make an element a containing block for its
    /// absolutely positioned descendants.</summary>
    private static readonly string[] PositionedKeywords = { "relative", "absolute", "fixed", "sticky" };

    /// <summary>Whether an inline wrapper takes its content out of flow: a `position: absolute` (or
    /// `fixed`) span holds its picture at a seat of its own and lends the cell it stands in none of its
    /// width. Probed on the Word mail's VML picture: a 471 px image inside such a span draws 4.5 pt
    /// into a 50.45 pt column and the column stays 50.45; reading it as the cell's image had made the
    /// column 353 pt and the sheet twice too wide.</summary>
    private static bool PositionedInline(Token tok)
        => tok.Attributes is { } attrs && attrs.TryGetValue("style", out var style) && style is not null
            && Regex.IsMatch(style, @"(?<![-\w])position\s*:\s*(absolute|fixed)\b", RegexOptions.IgnoreCase);

    /// <summary>Whether the run being read sits inside such a wrapper.</summary>
    private static bool InPositionedInline(TableParseState ps)
        => ps.styleStack.Exists(entry => entry.Positioned);

    /// <summary>The sides of a CSS box, in the order a 1-4 value shorthand names them.</summary>
    private const int CssSideTop = 0;
    private const int CssSideLeft = 3;

    /// <summary>An absolutely positioned image's seat: where it draws, measured in points from
    /// the page's content origin, and whether the declarations seat it at all.</summary>
    /// <remarks>
    /// An absolutely positioned box is laid out against the PADDING box of its nearest positioned
    /// ancestor, so its offset from the page's content origin is three sums: the chrome every
    /// ancestor OUTSIDE that one bills on this side (margin + border + padding), the positioned
    /// ancestor's own margin and border (its padding lies INSIDE the containing block and is not
    /// billed), and then the box's own `left`/`top` and margin on the same side.
    /// </remarks>
    private static void ReadAbsoluteImageSeat(ParseBlocksState pb, Token tok)
    {
        pb.imgAbsPos = false;
        pb.imgAbsLeftPt = 0;
        pb.imgAbsTopPt = 0;
        pb.imgPageBandPt = 0;
        var decls = AbsoluteSeatDeclarations(pb, tok);
        if (!decls.TryGetValue("position", out var pos)
            || !pos.Trim().Equals("absolute", StringComparison.OrdinalIgnoreCase)) return;
        // Only a box seated on BOTH axes is placed here: one that declares a single offset (or
        // none) keeps the browser's static position, which the flow already knows.
        if (CssBoxLengthPt(decls, "left") is not { } leftPt
            || CssBoxLengthPt(decls, "top") is not { } topPt) return;
        var (insetX, insetY) = PositionedAncestorInsetPt(pb);
        pb.imgAbsPos = true;
        pb.imgAbsLeftPt = insetX + leftPt + CssSideLengthPt(decls, "margin", CssSideLeft);
        pb.imgAbsTopPt = insetY + topPt + CssSideLengthPt(decls, "margin", CssSideTop);
        if (leftPt == 0 && topPt == 0) pb.imgPageBandPt = PageBandHeightPt(pb);
    }

    /// <summary>The declared height, in points, of the fixed-size container a full-bleed page
    /// image fills - the raster page export's one-container-per-source-page shape: a block
    /// container sized in ems on BOTH axes, holding only the positioned wrapper the image sits
    /// in. Zero when no ancestor is such a container.</summary>
    /// <remarks>
    /// The em here is the ROOT font size, not the flow's: these containers declare
    /// `font-size: 1em` and the sheet names no body size, so a browser sizes them off the
    /// initial 16px. Reading the height through <see cref="TryParseLength"/> instead would
    /// spend its 11 pt fallback and put every band 8% short.
    /// </remarks>
    private static double PageBandHeightPt(ParseBlocksState pb)
    {
        for (var i = pb.divTagStack.Count - 1; i >= 0; i--)
        {
            if (AncestorSeatDeclarations(pb, i) is not { } d) continue;
            if (!d.TryGetValue("display", out var disp)
                || !disp.Trim().Equals("block", StringComparison.OrdinalIgnoreCase)) continue;
            if (CssEmLength(d, "height") is not { } hEm || CssEmLength(d, "width") is not { } wEm
                || hEm <= 0 || wEm <= 0) continue;
            return hEm * CssRootFontPt;
        }
        return 0;
    }

    /// <summary>A declaration given in ems, as its em count; null when absent or in any other unit.</summary>
    private static double? CssEmLength(Dictionary<string, string> bag, string prop)
    {
        if (!bag.TryGetValue(prop, out var v) || v is null) return null;
        var m = Regex.Match(v.Trim(), @"^(-?(?:\d+(?:\.\d+)?|\.\d+))\s*em$", RegexOptions.IgnoreCase);
        return m.Success ? double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : null;
    }

    /// <summary>The image's seating declarations after the cascade: its class rules in source order,
    /// then its id rule, then its own inline style.</summary>
    private static Dictionary<string, string> AbsoluteSeatDeclarations(ParseBlocksState pb, Token tok)
    {
        var bag = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (tok.Attributes is not { } attrs) return bag;
        if (pb.css is not null && attrs.TryGetValue("class", out var cls) && cls is not null)
            foreach (var cn in cls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                if (pb.css.TryGetValue("." + cn, out var clsRule)) TakeSeatDeclarations(bag, clsRule);
        if (pb.css is not null && attrs.TryGetValue("id", out var id) && !string.IsNullOrWhiteSpace(id)
            && (pb.css.TryGetValue("#" + id.Trim(), out var idRule)
                || pb.css.TryGetValue("img#" + id.Trim(), out idRule))) TakeSeatDeclarations(bag, idRule);
        if (attrs.TryGetValue("style", out var st) && !string.IsNullOrEmpty(st))
            TakeInlineSeatDeclarations(bag, st);
        return bag;
    }

    /// <summary>The seating properties of one rule, overlaying what earlier rules put in the bag.</summary>
    private static void TakeSeatDeclarations(Dictionary<string, string> bag, Dictionary<string, string> rule)
    {
        foreach (var kv in rule) if (IsAbsoluteSeatProperty(kv.Key)) bag[kv.Key] = kv.Value;
    }

    /// <summary>The seating properties of an inline style string, overlaying the rules' values.</summary>
    private static void TakeInlineSeatDeclarations(Dictionary<string, string> bag, string style)
    {
        foreach (Match dm in StyleDeclRx.Matches(style))
            if (IsAbsoluteSeatProperty(dm.Groups[1].Value)) bag[dm.Groups[1].Value] = dm.Groups[2].Value.Trim();
    }

    /// <summary>The properties that seat a box: its position, its offsets and its box chrome.</summary>
    private static bool IsAbsoluteSeatProperty(string prop) =>
        prop.Equals("position", StringComparison.OrdinalIgnoreCase)
        || prop.Equals("left", StringComparison.OrdinalIgnoreCase)
        || prop.Equals("top", StringComparison.OrdinalIgnoreCase)
        || prop.StartsWith("margin", StringComparison.OrdinalIgnoreCase)
        || prop.StartsWith("padding", StringComparison.OrdinalIgnoreCase)
        || prop.StartsWith("border", StringComparison.OrdinalIgnoreCase);

    /// <summary>The left and top inset, in points, from the page's content origin to the origin of
    /// the containing block the innermost positioned ancestor establishes; (0, 0) when no ancestor
    /// is positioned, which seats the box against the page's own content box.</summary>
    private static (double X, double Y) PositionedAncestorInsetPt(ParseBlocksState pb)
    {
        var open = new Dictionary<string, string>?[pb.divTagStack.Count];
        var anchor = -1;
        for (var i = open.Length - 1; i >= 0; i--)
        {
            open[i] = AncestorSeatDeclarations(pb, i);
            if (anchor < 0 && open[i] is { } d && d.TryGetValue("position", out var p)
                && Array.IndexOf(PositionedKeywords, p.Trim().ToLowerInvariant()) >= 0) anchor = i;
        }
        if (anchor < 0) return (0, 0);
        double x = 0, y = 0;
        for (var i = 0; i <= anchor; i++)
        {
            if (open[i] is not { } d) continue;
            x += CssSideLengthPt(d, "margin", CssSideLeft) + CssBorderSidePt(d, CssSideLeft);
            y += CssSideLengthPt(d, "margin", CssSideTop) + CssBorderSidePt(d, CssSideTop);
            // the anchor's own padding lies INSIDE the containing block it establishes
            if (i == anchor) continue;
            x += CssSideLengthPt(d, "padding", CssSideLeft);
            y += CssSideLengthPt(d, "padding", CssSideTop);
        }
        return (x, y);
    }

    /// <summary>The seating declarations of the open block at <paramref name="depth"/> after the
    /// cascade: its tag rule, its class rules, its id rule, then its inline style.</summary>
    private static Dictionary<string, string>? AncestorSeatDeclarations(ParseBlocksState pb, int depth)
    {
        var bag = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var tag = depth < pb.divTagStack.Count ? pb.divTagStack[depth] : "div";
        if (pb.css is not null)
        {
            if (pb.css.TryGetValue(tag, out var tagRule)) TakeSeatDeclarations(bag, tagRule);
            if (depth < pb.divClassStack.Count)
                foreach (var cn in pb.divClassStack[depth].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                    if (pb.css.TryGetValue("." + cn, out var clsRule)) TakeSeatDeclarations(bag, clsRule);
            if (depth < pb.divIdStack.Count && pb.divIdStack[depth].Length > 0
                && (pb.css.TryGetValue("#" + pb.divIdStack[depth], out var idRule)
                    || pb.css.TryGetValue(tag + "#" + pb.divIdStack[depth], out idRule)))
                TakeSeatDeclarations(bag, idRule);
        }
        if (depth < pb.divStyleStack.Count && pb.divStyleStack[depth].Length > 0)
            TakeInlineSeatDeclarations(bag, pb.divStyleStack[depth]);
        return bag.Count > 0 ? bag : null;
    }

    /// <summary>A plain length declaration in points; null when absent or not a length (auto, a
    /// percentage, calc()).</summary>
    /// <remarks>
    /// A box offset is SIGNED and may legitimately be zero, which <see cref="TryParseLength"/>
    /// cannot express: its null means "no usable length" to the callers that read a default
    /// through it, so it folds 0 and every negative into that null. `left: 0` and `left: -20px`
    /// are both real seats, so they are recovered here rather than read as "not declared".
    /// </remarks>
    private static double? CssBoxLengthPt(Dictionary<string, string> bag, string prop)
    {
        if (!bag.TryGetValue(prop, out var v) || v is null) return null;
        var s = v.Trim();
        if (TryParseLength(s) is { } positive) return positive;
        if (IsZeroLength(s)) return 0;
        return s.StartsWith("-", StringComparison.Ordinal) && TryParseLength(s.Substring(1)) is { } magnitude
            ? -magnitude : null;
    }

    /// <summary>One side of a `margin`/`padding` box in points - the longhand when it is declared,
    /// otherwise the shorthand's share. <paramref name="side"/> indexes top, right, bottom, left.</summary>
    private static double CssSideLengthPt(Dictionary<string, string> bag, string prop, int side)
    {
        var sideName = side switch { 0 => "top", 1 => "right", 2 => "bottom", _ => "left" };
        if (CssBoxLengthPt(bag, prop + "-" + sideName) is { } longhand) return longhand;
        if (!bag.TryGetValue(prop, out var shorthand) || shorthand is null) return 0;
        var vals = CssSideValues(shorthand.Trim());
        return vals is not null && TryParseLength(vals[side]) is { } pt ? pt : 0;
    }

    /// <summary>One border side's drawn width in points, after the whole border cascade in the bag.
    /// <paramref name="side"/> indexes top, right, bottom, left.</summary>
    private static double CssBorderSidePt(Dictionary<string, string> bag, int side)
    {
        var style = new System.Text.StringBuilder();
        // The bag holds the cascade's winner per property, not its declaration order, so the
        // shorthands are replayed before the longhands that refine them - `border` then
        // `border-left` then `border-left-width` - which is the order that leaves each
        // declaration saying what it was written to say.
        var props = new List<string>();
        foreach (var kv in bag)
            if (kv.Key.StartsWith("border", StringComparison.OrdinalIgnoreCase)) props.Add(kv.Key);
        props.Sort((a, b) =>
        {
            var d = a.Split('-').Length.CompareTo(b.Split('-').Length);
            return d != 0 ? d : string.CompareOrdinal(a, b);
        });
        foreach (var p in props) style.Append(p).Append(':').Append(bag[p]).Append(';');
        return style.Length == 0 ? 0 : CssBorderSides(style.ToString())[side].W;
    }
}
