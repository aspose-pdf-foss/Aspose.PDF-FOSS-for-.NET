using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The sheet rule that addresses the block. The UA flow keeps its one-rule reading
    /// (its tag qualified by a class, the class alone, or the bare tag - the first that exists).
    /// The calibrated flow's typography reads the CASCADE instead: the bare tag rule, overlaid by
    /// every class rule the element carries, in class order - a reset sheet's `h2 { font-size:
    /// 100% }` then still yields to the title class's own size, as it does in the browser.</summary>
    private static Dictionary<string, string>? SheetRuleFor(ParseBlocksState pb, Token tok, string tag)
    {
        var lower = tag.ToLowerInvariant();
        var classes = tok.Attributes is { } attrs && attrs.TryGetValue("class", out var cls) && cls is not null
            ? cls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>();
        if (!pb.browserUa)
        {
            Dictionary<string, string>? merged = null;
            void Overlay(Dictionary<string, string> rule)
            {
                merged ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in rule) merged[kv.Key] = kv.Value;
            }
            if (pb.css!.TryGetValue(lower, out var tagRule)) Overlay(tagRule);
            foreach (var c in classes)
            {
                if (pb.css.TryGetValue("." + c, out var classRule)) Overlay(classRule);
                if (pb.css.TryGetValue(lower + "." + c, out var tagClassRule)) Overlay(tagClassRule);
            }
            // …then the two-part descendant rules the open ancestors satisfy, outer to
            // inner (the flat map keys them as "ancestor descendant"): `.inlineDisplay h2
            // { font-size: 1.3125rem }` sizes the title a reset sheet's bare `h2 {
            // font-size: 100% }` had just levelled…
            for (var i = 0; i < pb.divClassStack.Count; i++)
            {
                var ancTag = i < pb.divTagStack.Count ? pb.divTagStack[i] : "div";
                if (pb.css.TryGetValue(ancTag + " " + lower, out var tagTagRule)) Overlay(tagTagRule);
                foreach (var ac in pb.divClassStack[i].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (pb.css.TryGetValue("." + ac + " " + lower, out var classTagRule)) Overlay(classTagRule);
                    foreach (var c in classes)
                        if (pb.css.TryGetValue("." + ac + " ." + c, out var classClassRule)) Overlay(classClassRule);
                }
            }
            // …and the longer chains, most specific last.
            if (pb.chainRules is not null && MatchChainDecls(pb.chainRules, OpenElementChain(pb, tok, lower)) is { } chained)
                Overlay(chained);
            if (OwnIdRule(pb, tok, lower) is { } mergedIdRule) Overlay(mergedIdRule);
            return merged;
        }
        Dictionary<string, string>? bmRule = null;
        foreach (var pc in classes)
            if (pb.css!.TryGetValue(lower + "." + pc, out bmRule) || pb.css.TryGetValue("." + pc, out bmRule))
                break;
        if (bmRule is null && pb.css!.TryGetValue(lower, out var bmBare))
            bmRule = bmBare;
        // (…and where BOTH a tag rule and a class rule address the block, the class rule's declarations
        //  stand over the tag rule's, the tag rule's remaining ones still apply - the cascade; the
        //  field-list page's `div { margin-bottom }` under its `div.new-group { margin-top }`)
        else if (bmRule is not null && pb.cv?.profile.fieldListDoc == true && pb.css!.TryGetValue(lower, out var bmTagUnder))
        {
            var cascaded = new Dictionary<string, string>(bmTagUnder, StringComparer.OrdinalIgnoreCase);
            foreach (var kv in bmRule) cascaded[kv.Key] = kv.Value;
            bmRule = cascaded;
        }
        // (…the comment-wrapped sheet's first rule included)
        bmRule ??= CommentWrappedRule(pb.css, lower);
        // …and the element's own id rule outranks them (its margins and typography; its box size is
        // the host law's - see Block.HostWidthPt)
        if (OwnIdRule(pb, tok, lower) is { } idRule)
        {
            var withId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (bmRule is not null) foreach (var kv in bmRule) withId[kv.Key] = kv.Value;
            foreach (var kv in idRule) withId[kv.Key] = kv.Value;
            bmRule = withId;
        }
        return bmRule;
    }

    /// <summary>The sheet rule addressed to the element's own id (`#divTitle`, `div#divTitle`), without its
    /// width and height (a sized wrapper is the grid host law's, not the block flow's); null where none.</summary>
    private static Dictionary<string, string>? OwnIdRule(ParseBlocksState pb, Token tok, string lowerTag)
    {
        if (pb.css is null || tok.Attributes is not { } attrs || !attrs.TryGetValue("id", out var idv)
            || idv is null || idv.Trim().Length == 0) return null;
        var id = idv.Trim();
        if (!pb.css.TryGetValue("#" + id, out var rule) && !pb.css.TryGetValue(lowerTag + "#" + id, out rule)) return null;
        Dictionary<string, string>? kept = null;
        foreach (var kv in rule)
        {
            if (kv.Key.Equals("width", StringComparison.OrdinalIgnoreCase) || kv.Key.Equals("height", StringComparison.OrdinalIgnoreCase)) continue;
            (kept ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))[kv.Key] = kv.Value;
        }
        return kept;
    }

    /// <summary>The first face of a CSS font-family stack the flow can both measure and embed, or null.</summary>
    private static string? ResolvableFamily(string? stack)
    {
        if (string.IsNullOrEmpty(stack)) return null;
        foreach (var fam in stack.Split(','))
        {
            var f = fam.Trim().Trim('"', '\'');
            if (IsDrawableFace(f)) return f;
        }
        return null;
    }

    /// <summary>A face the flow can measure (installed metrics) and embed (a face program).</summary>
    private static bool IsDrawableFace(string? face) =>
        face is { Length: > 0 } && WinMetricsFor(face) is not null && PosFace(face).ttf is not null;

    /// <summary>The element as a chain of CSS elements: every open block above it (tag and
    /// classes, as the stacks recorded them) and then itself.</summary>
    private static List<CssElem> OpenElementChain(ParseBlocksState pb, Token tok, string lowerTag)
    {
        var chain = new List<CssElem>(pb.divClassStack.Count + 1);
        for (var i = 0; i < pb.divClassStack.Count; i++)
            chain.Add(new CssElem
            {
                Tag = i < pb.divTagStack.Count ? pb.divTagStack[i] : "div",
                Id = _quirksChainSheet && i < pb.divIdStack.Count && pb.divIdStack[i].Length > 0 ? pb.divIdStack[i] : null,
                Classes = pb.divClassStack[i].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries),
            });
        string? id = null;
        tok.Attributes?.TryGetValue("id", out id);
        chain.Add(new CssElem
        {
            Tag = lowerTag,
            Id = id,
            Classes = tok.Attributes is { } a && a.TryGetValue("class", out var cls) && cls is not null
                ? cls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) : null,
        });
        return chain;
    }

    /// <summary>A sheet rule's line-height on a calibrated-flow block (see the caller).</summary>
    private static void ApplySheetLineHeight(BlockStyle style, string value)
    {
        var lower = value.ToLowerInvariant();
        if (lower == "normal" || lower == "inherit") { if (lower == "normal") { style.SheetLineFactor = 0; style.LineBoxPt = 0; } return; }
        // A PERCENT line-height on the UA flow is a factor the block's descendants inherit as a
        // factor, each line resolving it against its own size (MEASURED, the evaluation form: the
        // `H1 { line-height: 125% }` heading lines 17.875 at 14.3 and its .8em span's line 14.3 at
        // 11.44; the body's inline `line-height: 100%` paces its 11 pt breaks 11 and the 12.65 h2
        // 12.65). A length or em stays the box it names.
        if (style.UaSerif && Regex.Match(lower, @"^([\d.]+)\s*%$") is { Success: true } pct
            && double.TryParse(pct.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var pctFactor) && pctFactor > 0)
        { style.UaLineFactor = pctFactor / 100.0; style.LineBoxPt = 0; return; }
        if (Regex.IsMatch(lower, @"^[\d.]+$") && double.TryParse(lower,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var factor) && factor > 0)
        { style.SheetLineFactor = factor; style.LineBoxPt = 0; return; }
        if (lower.EndsWith("em") && !lower.EndsWith("rem") && double.TryParse(lower[..^2],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var ems) && ems > 0)
        { style.LineBoxPt = ems * style.FontSize; style.SheetLineFactor = 0; return; }
        if (TryParseLength(lower) is { } pt && pt > 0) { style.LineBoxPt = pt; style.SheetLineFactor = 0; }
    }

    /// <summary>The rule's typography rides the block: a PERCENT font-size resolves against the
    /// inherited size (h1 { font-size: 120% } = 14.4 on the UA base), a length replaces it, a
    /// RESOLVABLE family rides the block's runs (h6 { font-family: Verdana }), a bold weight
    /// takes the bold resource, and text-align centres or justifies.</summary>
    private static void ApplySheetRuleTypography(ParseBlocksState pb, Dictionary<string, string> bmRule)
    {
        if (bmRule.TryGetValue("font-size", out var bmFsV))
        {
            var bmFs = bmFsV.Trim();
            // A relative size resolves against the INHERITED size. The UA flow reads that off
            // the style as it stands; the calibrated flow has already seated the tag's own
            // default there, so it resolves against the parent block instead (probed: an
            // h2 { font-size: 1.46em } under a 13 px body draws 14.235, not 1.46 x the
            // h2 default nor 1.46 x a fixed 11).
            var inherited = pb.sheetElementTypography && pb.parent is { FontSize: > 0 } par
                ? par.FontSize : pb.style.FontSize;
            if (bmFs.EndsWith("%", StringComparison.Ordinal)
                && double.TryParse(bmFs.TrimEnd('%'),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var bmPct) && bmPct > 0)
                pb.style.FontSize = inherited * bmPct / 100.0;
            else if (pb.sheetElementTypography && bmFs.EndsWith("em", StringComparison.OrdinalIgnoreCase)
                && !bmFs.EndsWith("rem", StringComparison.OrdinalIgnoreCase)
                && double.TryParse(bmFs[..^2],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var bmEm) && bmEm > 0)
                pb.style.FontSize = inherited * bmEm;
            else if (TryParseCssFontSize(bmFs) is { } bmPt && bmPt > 0)
                pb.style.FontSize = bmPt;
        }
        // The sheet-typography flow takes the stack's first RESOLVABLE face (a "Segoe UI
        // Semilight", Segoe UI, … stack draws Segoe UI); the UA flow keeps its first-face reading.
        if (bmRule.TryGetValue("font-family", out var bmFamV))
        {
            if (pb.sheetElementTypography)
            {
                if (ResolvableFamily(bmFamV) is { } sheetFam) pb.style.FontFamily = sheetFam;
            }
            else if (FirstFontFamily(bmFamV) is { Length: > 0 } bmFam && WinMetricsFor(bmFam) is not null)
                pb.style.FontFamily = bmFam;
        }
        // The rule's line-height paces the block (calibrated flow): a unitless value is
        // a factor of the block's own size, a length is the line box itself, an em
        // length that box at the block's size, and `normal` returns to the flow's pitch
        // (probed: a 21 px body line-height paces every 10.5 pt heading line at 15.75;
        // a `p { line-height: 1.6 }` paces 9.75 pt text at 15.6).
        // (…and the UA flow reads the same box: its lines seat half-leading inside it)
        if ((pb.sheetElementTypography || pb.browserUa) && bmRule.TryGetValue("line-height", out var bmLhV))
            ApplySheetLineHeight(pb.style, bmLhV.Trim());
        if (bmRule.TryGetValue("font-weight", out var bmFwV)
            && (bmFwV.Trim() is "bold" or "bolder"
                || (int.TryParse(bmFwV.Trim(), out var bmFwN) && bmFwN >= 600)))
            pb.style.FontRes = "F2";
        // …and a NORMAL weight undoes the tag's legacy bold (the sheet-typography flow:
        // probed, `h2 { font-weight: normal }` draws the title in the regular face).
        else if (pb.sheetElementTypography && bmFwV is not null
            && (bmFwV.Trim() is "normal" or "lighter"
                || (int.TryParse(bmFwV.Trim(), out var bmFwLight) && bmFwLight < 600))
            && pb.style.FontRes == "F2")
        { pb.style.FontRes = "F1"; pb.style.EmBold = false; }
        if (bmRule.TryGetValue("text-align", out var bmTaV))
        {
            var bmTa = bmTaV.Trim().ToLowerInvariant();
            if (bmTa == "center") pb.style.AlignCenterAttr = true;
            else if (bmTa == "justify") pb.style.AlignJustify = true;
        }
        if (_quirksChainSheet || (pb.uaGridBlocks && pb.sheetElementTypography)) ApplySheetRuleBox(pb, bmRule);
    }

    /// <summary>The sheet rule's BOX on a block of the sheet-typography flow: a background fills the
    /// block's line box, padding-left insets its text, and border-top / border-bottom are rules of
    /// their own width above and below the box - box space the flow steps over (measured on the
    /// section heading: a `#right_column H2 { BORDER-TOP: 3px solid; BACKGROUND; LINE-HEIGHT: 32px;
    /// BORDER-BOTTOM: 1px solid; PADDING-LEFT: 10px }` band is 27 pt tall, its title centred in the
    /// box 7.5 pt narrower than the line).</summary>
    private static void ApplySheetRuleBox(ParseBlocksState pb, Dictionary<string, string> bmRule)
    {
        var boxed = false;
        if ((bmRule.TryGetValue("background-color", out var bg) || bmRule.TryGetValue("background", out bg))
            && ParseCssColor(bg) is { } bgCol)
        {
            pb.style.BackgroundColor = bgCol;
            boxed = true;
        }
        if (bmRule.TryGetValue("border-top", out var bt)
            && TryParseBorderShorthand("border-top:" + bt, "border-top") is (var btW, var btCol) && btW > 0
            && !Regex.IsMatch(bt, @"\bnone\b", RegexOptions.IgnoreCase))
        {
            pb.style.BorderTopOnly = true;
            pb.style.BorderWidth = btW;
            pb.style.BorderColor = btCol ?? Color.Black;
            if (pb.uaGridBlocks) { pb.style.UaBoxTopPt += btW; pb.style.UaRuleTopPt = btW; pb.style.UaRuleTopColor = btCol ?? Color.Black; }
            else pb.style.MarginTop += btW;
            boxed = true;
        }
        if (bmRule.TryGetValue("border-bottom", out var bb)
            && TryParseBorderShorthand("border-bottom:" + bb, "border-bottom") is (var bbW, var bbCol) && bbW > 0
            && !Regex.IsMatch(bb, @"\bnone\b", RegexOptions.IgnoreCase))
        {
            pb.style.BorderBottomWidth = bbW;
            pb.style.BorderBottomColor = bbCol ?? Color.Black;
            if (pb.uaGridBlocks) pb.style.UaBoxBottomPt += bbW; else pb.style.MarginBottom += bbW;
            boxed = true;
        }
        if (boxed && bmRule.TryGetValue("padding-left", out var pl)
            && ChainLenPt(pl, pb.style.FontSize) is > 0 and var plPt)
            pb.style.BgPadLeftPt = plPt;
        // A UA-grid document's heading box: the rule's padding-top is space under its top rule and its
        // height a floor on the content box (measured on the quotation: `h4 { border-top: 2px; padding-top:
        // 10px; height: 30px; border-bottom: 1px }` stands 32.25 tall).
        if (pb.uaGridBlocks && pb.sheetElementTypography)
        {
            if (bmRule.TryGetValue("padding-top", out var ptV) && ChainLenPt(ptV, pb.style.FontSize) is > 0 and var ptPt)
                pb.style.UaPadTopPt += ptPt;
            if (bmRule.TryGetValue("padding-bottom", out var pbV) && ChainLenPt(pbV, pb.style.FontSize) is > 0 and var pbPt)
                pb.style.UaBoxBottomPt += pbPt;
            if (bmRule.TryGetValue("height", out var hV) && ChainLenPt(hV, pb.style.FontSize) is > 0 and var hPt)
                pb.style.ExplicitHeight = Math.Max(pb.style.ExplicitHeight, hPt);
            if (bmRule.TryGetValue("width", out var wV) && ChainLenPt(wV, pb.style.FontSize) is > 0 and var wPt)
                pb.style.WidthPx = wPt / 0.75;
        }
        if (boxed) pb.style.SheetBox = true;
    }
}
