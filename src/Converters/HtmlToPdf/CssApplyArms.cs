using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The Border declaration applied to the block style.</summary>
    private static bool ApplyBorderDecl(string prop, string val, BlockStyle s)
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
            return false;
        }
        else if (s.BorderWidth <= 0)
            s.BorderWidth = 0.75;
        // A border with an unspecified colour defaults to black (CSS `border:1px solid`).
        if (s.BorderColor is null && val.IndexOf("none", StringComparison.OrdinalIgnoreCase) < 0)
            s.BorderColor = Color.FromArgb(0, 0, 0);
        return true;
    }

    /// <summary>The Height declaration applied to the block style.</summary>
    private static bool ApplyHeightDecl(string prop, string val, BlockStyle s)
    {
        // em heights scale with the element's RESOLVED font size (a 12/11 factor
        // maps our 11pt body default onto the browser's 16px=12pt em base).
        double hPt;
        var em = Regex.Match(val, @"^([\d.]+)\s*em$", RegexOptions.IgnoreCase);
        if (em.Success && double.TryParse(em.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var emv))
            hPt = emv * s.FontSize * (12.0 / 11.0);
        else if (TryParseLength(val) is { } lenPt)
            hPt = lenPt;
        else
            return false;
        // Both properties state the same FLOOR: measured, a
        // declared height and a min-height behave identically - the element's
        // own content keeps its position and only what FOLLOWS the element
        // moves down to clear the floor.
        if (hPt > s.HeightFloorPt) s.HeightFloorPt = hPt;
        // Browser-UA flow: min-height paints and pads nothing, so it leaves
        // ExplicitHeight (the box/spacer channel) alone and speaks only
        // through the floor above.
        if (prop == "min-height" && s.UaSerif) return false;
        if (hPt > s.ExplicitHeight) s.ExplicitHeight = hPt;
        return true;
    }

    /// <summary>The MarginLeft declaration applied to the block style.</summary>
    private static void ApplyMarginLeftDecl(string prop, string val, BlockStyle s)
    {
        if (TryParseLength(val) is { } pts) s.LeftIndent += pts;
        // The Bootstrap gutter pair: a NEGATIVE margin-left cancels the
        // enclosing column padding (`.row { margin-left:-15px }` inside
        // `.col { padding-left:15px }`). Styled-article dialect only — the
        // calibrated dialects never met a negative.
        else if (s.ArticleRhythm && prop == "margin-left"
            && val.TrimStart().StartsWith('-')
            && TryParseLength(val.TrimStart().TrimStart('-')) is { } negPts)
            s.LeftIndent = Math.Max(0, s.LeftIndent - negPts);
        // UA-serif flow: a negative margin-left is REAL — the element's box
        // moves left of the content origin and the page clip crops it there
        // (the expected render clips content at one body margin left of the
        // content origin).
        else if (s.UaSerif && prop == "margin-left"
            && val.TrimStart().StartsWith('-')
            && TryParseLength(val.TrimStart().TrimStart('-')) is { } uaNegPts)
            s.LeftIndent -= uaNegPts;
    }

    /// <summary>The PaddingBottom declaration applied to the block style.</summary>
    private static void ApplyPaddingBottomDecl(string val, BlockStyle s)
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
        else if (TryParseLength(val) is { } pts && pts > s.MarginBottom)
            s.MarginBottom = pts;
    }

    /// <summary>The Padding declaration applied to the block style.</summary>
    private static void ApplyPaddingDecl(string val, BlockStyle s)
    {
        var padParts = val.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // The `padding:` shorthand's TOP value on a top-rule DIVIDER wrapper
        // is the box space its marker block spends under the rule (the saved
        // email's `padding: 3pt 0cm 0cm` From-block frame).
        if (s.UaSerif && s.BorderTopOnly
            && padParts.Length > 0 && TryParseLength(padParts[0]) is { } padTopPt
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
                => i < padParts.Length && TryParseLength(padParts[i]) is { } v ? v : 0;
            var padT = Pad(0);
            var padR = padParts.Length > 1 ? Pad(1) : padT;
            var padB = padParts.Length > 2 ? Pad(2) : padT;
            var padL = padParts.Length > 3 ? Pad(3) : padR;
            s.BgPadTopPt = padT;
            s.BgPadBottomPt = padB;
            s.BgPadLeftPt = padL;
            // The UA-serif flow: a block's padding is box space on every side - its content
            // starts padding-left inside its left edge, ends padding-right short of its right
            // edge, and its first line stands padding-top below its top (probed on the reward
            // letter: `padding: 10px 50px` seats the banner text at 43.5 x 33.96 against 6 x 26.46
            // without it, and its right-aligned runs 37.5 short of the content edge).
            if (s.UaBoxes)
            {
                s.LeftIndent += padL;
                s.RightInsetPt += padR;
                s.PadTop = Math.Max(s.PadTop, padT);
                // ...and its padding-bottom stands below its last line (the statement body's
                // `padding: 0 30px 20px 22px` keeps 15 under "Alexander Burgess TEST!")
                if (padB > s.MarginBottom) { s.MarginBottom = padB; s.MarginBottomAuthored = true; }
            }
        }
    }

    /// <summary>The Margin declaration applied to the block style.</summary>
    private static void ApplyMarginDecl(string val, BlockStyle s)
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
        if (leftVal is not null && TryParseLength(leftVal) is { } mLeftPt && mLeftPt > 0)
            s.ShorthandLeftPt = mLeftPt;
        // ...and in the UA-serif flow the shorthand's left and right values move the box in
        // from both edges (the reward letter's `margin: 0px 50px 10px 50px` statement box).
        if (s.UaBoxes)
        {
            var rightVal = hParts.Length switch { 0 => null, 1 => hParts[0], _ => hParts[1] };
            if (leftVal is not null && TryParseLength(leftVal) is { } uaMl && uaMl > 0) s.LeftIndent += uaMl;
            if (rightVal is not null && TryParseLength(rightVal) is { } uaMr && uaMr > 0) s.RightInsetPt += uaMr;
        }
        if (hParts.Length >= 1 && TryParseLength(hParts[0]) is { } mTopPt && mTopPt > 0)
            s.ShorthandTopPt = mTopPt;
        if (s.FormDialect || s.UaSerif)
        {
        // The `margin:` shorthand (form dialect and UA-serif flows): top and
        // bottom margins per the 1/2/3/4-value CSS grammar — an authored
        // `margin: 0pt` really zeroes the UA paragraph margins. Horizontal
        // values are left to the dedicated margin-left handling; a negative
        // value counts as zero.
        static double? NonNegLen(string v)
        {
            double p = 0;
            if (TryParseLength(v) is { } len) { p = len; return p; }
            // TryParseLength rejects 0 and negatives; a shorthand "0" is valid.
            if (Regex.IsMatch(v, @"^-?\d+(\.\d+)?\s*(px|pt|em|rem|in|cm|mm)?$")) { p = 0; return p; }
            return null;
        }
        var parts = val.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 1 && NonNegLen(parts[0]) is { } mTop)
        {
            var bottomVal = parts.Length switch
            {
                1 => parts[0],
                2 => parts[0],
                _ => parts[2],
            };
            s.MarginTop = mTop;
            if (NonNegLen(bottomVal) is { } mBot) { s.MarginBottom = mBot; s.MarginBottomAuthored = true; }
        }
        }
    }

    /// <summary>The Font declaration applied to the block style.</summary>
    private static void ApplyFontDecl(string val, BlockStyle s)
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

    /// <summary>The FontSize declaration applied to the block style.</summary>
    private static void ApplyFontSizeDecl(string val, BlockStyle s)
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
        else if (s.InPageFragment
            && Regex.Match(val.Trim(), @"^(\d+(?:\.\d+)?)$") is { Success: true } bare
            && double.TryParse(bare.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var barePt) && barePt > 0)
            s.FontSize = barePt;
        else if (TryParseLength(val) is { } pts) s.FontSize = pts;
        else if (Regex.IsMatch(val, @"^0+(\.0+)?\s*(px|pt|em|rem)?$"))
            s.ZeroFontSize = true;
        else if (val.EndsWith("%", StringComparison.Ordinal)
                 && double.TryParse(val.TrimEnd('%'), System.Globalization.NumberStyles.Float,
                     System.Globalization.CultureInfo.InvariantCulture, out var pct)
                 && pct > 0)
            s.FontSize *= pct / 100.0;
    }

    /// <summary>The BorderStyle declaration applied to the block style.</summary>
    private static void ApplyBorderStyleDecl(string val, BlockStyle s)
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

    /// <summary>The LineHeight2 declaration applied to the block style.</summary>
    private static void ApplyLineHeight2Decl(string val, BlockStyle s)
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

    /// <summary>The MarginBottom declaration applied to the block style.</summary>
    private static void ApplyMarginBottomDecl(string val, BlockStyle s)
    {
        // An `em` margin is a multiple of the element's OWN resolved size
        // (`margin-bottom: 1em` on a 10 pt block is 10 pt); TryParseLength
        // can only assume the document default, so resolve it here.
        var mbEm = Regex.Match(val, @"^([\d.]+)\s*em$", RegexOptions.IgnoreCase);
        if (mbEm.Success && s.FontSize > 0
            && double.TryParse(mbEm.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var mbEmv))
        { s.MarginBottom = mbEmv * s.FontSize; s.MarginBottomAuthored = true; }
        else if (TryParseLength(val) is { } pts) { s.MarginBottom = pts; s.MarginBottomAuthored = true; }
        // An authored ZERO is a margin of zero, not "no margin declared": it
        // replaces the UA default the same way any other authored value does.
        else if (IsZeroLength(val)) { s.MarginBottom = 0; s.MarginBottomAuthored = true; }
    }

    /// <summary>The MarginTop declaration applied to the block style.</summary>
    private static void ApplyMarginTopDecl(string val, BlockStyle s)
    {
        if (TryParseLength(val) is { } pts)
        {
            s.MarginTop = pts;
            // Authored (not UA-default) margins MAX-collapse with the body
            // margin at the document top — the UA flow reads this flag.
            s.MarginTopAuthored = true;
        }
        // An authored ZERO replaces the UA default like any other authored
        // value (a `<ul style="margin-top:0px">` opens flush under the line
        // above it, not one UA list margin down).
        else if (IsZeroLength(val))
        {
            s.MarginTop = 0;
            s.MarginTopAuthored = true;
        }
    }

    /// <summary>The Float declaration applied to the block style.</summary>
    private static void ApplyFloatDecl(string val, BlockStyle s)
    {
        // Recorded for every document; only a flow that opted into float layout
        // reads it, so this stays inert elsewhere.
        if (val.Trim().Equals("left", StringComparison.OrdinalIgnoreCase))
            s.FloatLeft = true;
        else if (val.Trim().Equals("right", StringComparison.OrdinalIgnoreCase))
            s.FloatRight = true;
    }

    /// <summary>The TextAlign declaration applied to the block style.</summary>
    private static void ApplyTextAlignDecl(string val, BlockStyle s)
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

    /// <summary>The FontWeight declaration applied to the block style.</summary>
    private static void ApplyFontWeightDecl(string val, BlockStyle s)
    {
        if (val is "bold" or "bolder" || (int.TryParse(val, out var n) && n >= 600))
            s.FontRes = s.FontRes == "F3" ? "F2" : "F2";
        // An explicit normal weight undoes a heading tag's default bold (form
        // dialect only — legacy conversions are calibrated with the bold face).
        else if (s.FormDialect && s.FontRes == "F2"
                 && (val == "normal" || (int.TryParse(val, out var n2) && n2 < 600)))
            s.FontRes = "F1";
    }

    /// <summary>The LineHeight declaration applied to the block style.</summary>
    private static void ApplyLineHeightDecl(string val, BlockStyle s)
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

    /// <summary>The BorderRadius declaration applied to the block style.</summary>
    private static void ApplyBorderRadiusDecl(string val, BlockStyle s)
    {
        // First shorthand value rounds all corners this flow draws.
        var rv = val.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (rv.Length > 0 && TryParseLength(rv[0]) is { } rPt)
            s.BorderRadiusPt = rPt;
    }

    /// <summary>The PageBreakAfter declaration applied to the block style.</summary>
    private static void ApplyPageBreakAfterDecl(string val, BlockStyle s)
    {
        // The break lands AFTER this element's content — an empty
        // `<p style="page-break-after:always"></p>` is the cover-page idiom.
        if (val.Contains("always", StringComparison.OrdinalIgnoreCase)
            || val.Equals("page", StringComparison.OrdinalIgnoreCase))
            s.PageBreakAfter = true;
    }

    /// <summary>The CSS3 paged-media `page: &lt;name>` declaration applied to the block style. The
    /// named page box itself is not read - only the NAME matters, as the trigger of a forced break
    /// when the flow enters a differently named page (MEASURED: the Word e-mail export whose
    /// `div.WordSection1 { page: WordSection1 }` starts its body on a fresh sheet; deleting that one
    /// declaration and nothing else puts the body back under the prologue).</summary>
    private static void ApplyPageNameDecl(string val, BlockStyle s)
    {
        var name = val.Trim();
        if (name.Length == 0 || name.Equals("auto", StringComparison.OrdinalIgnoreCase)) return;
        s.PageName = name;
    }

    /// <summary>The PageBreakBefore declaration applied to the block style.</summary>
    private static void ApplyPageBreakBeforeDecl(string val, BlockStyle s)
    {
        if (val.Contains("always", StringComparison.OrdinalIgnoreCase)
            || val.Equals("page", StringComparison.OrdinalIgnoreCase))
            s.PageBreakBefore = true;
    }

    /// <summary>The BackgroundColor declaration applied to the block style.</summary>
    private static void ApplyBackgroundColorDecl(string val, BlockStyle s)
    {
        var c = ParseCssColor(val);
        // Ignore white/transparent backgrounds — they add no visible ink. Except in the UA-serif
        // flow, where a white box COVERS what stands behind it (the reward letter's white strip
        // over the bottom of its banner picture).
        if (c is not null && (s.UaBoxes || !(c.R >= 250 && c.G >= 250 && c.B >= 250)))
            s.BackgroundColor = c;
    }

    /// <summary>The Color declaration applied to the block style.</summary>
    private static void ApplyColorDecl(string val, BlockStyle s)
    {
        // Foreground text colour. Layout-neutral — changes only the drawn ink.
        var c = ParseCssColor(val);
        if (c is not null) s.ForeColor = c;
    }

    /// <summary>The MarginRight declaration applied to the block style.</summary>
    private static void ApplyMarginRightDecl(string val, BlockStyle s)
    {
        // Recorded for every document; only the float flow reads it (see
        // StyledDoc.MarginRightPt).
        if (TryParseLength(val) is { } mrPts && mrPts > 0) s.MarginRightPt = mrPts;
    }

    /// <summary>The Width declaration applied to the block style.</summary>
    private static void ApplyWidthDecl(string val, BlockStyle s)
    {
        // A pixel width, recorded for every document; only the float flow reads it.
        if (Regex.IsMatch(val, @"^\s*[0-9.]+\s*px\s*$", RegexOptions.IgnoreCase)
            && TryParseLength(val) is { } wDeclPt && wDeclPt > 0)
            s.DeclaredWidthPt = wDeclPt;
        // …and a PERCENTAGE width, which cannot be resolved here: the box it is a fraction of
        // depends on the final sheet. Recorded as a fraction and resolved where the sheet is known.
        else if (Regex.Match(val, @"^\s*([0-9.]+)\s*%\s*$") is { Success: true } wPct
            && double.TryParse(wPct.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var wFrac)
            && wFrac > 0 && wFrac <= 100)
            s.DeclaredWidthFrac = wFrac / 100.0;
    }
}
